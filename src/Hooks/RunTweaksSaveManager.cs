using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

[JsonSerializable(typeof(ActiveRunTweaksSnapshot))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
internal partial class ActiveRunTweaksJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Serializable data container representing the immutable pre-run configuration snapshot bound to an active run.
/// Persists across game restarts, quits, relogs, and endless loops.
/// </summary>
public sealed class ActiveRunTweaksSnapshot
{
    [JsonPropertyName("profileId")]
    public int ProfileId { get; set; } = 0;

    [JsonPropertyName("startTime")]
    public long StartTime { get; set; } = 0;

    [JsonPropertyName("runSeed")]
    public string RunSeed { get; set; } = "";

    [JsonPropertyName("isCustom")]
    public bool IsCustom { get; set; } = false;

    [JsonPropertyName("endlessLoopCount")]
    public int EndlessLoopCount { get; set; } = 0;

    [JsonPropertyName("savedAtUtc")]
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("preRunTweaks")]
    public PreRunTweaksConfig PreRunTweaks { get; set; } = new();

    public ActiveRunTweaksSnapshot Clone()
    {
        return new ActiveRunTweaksSnapshot
        {
            ProfileId = ProfileId,
            StartTime = StartTime,
            RunSeed = RunSeed,
            IsCustom = IsCustom,
            EndlessLoopCount = EndlessLoopCount,
            SavedAtUtc = SavedAtUtc,
            PreRunTweaks = ClonePreRunTweaks(PreRunTweaks)
        };
    }

    public static PreRunTweaksConfig ClonePreRunTweaks(PreRunTweaksConfig source)
    {
        if (source == null) return new PreRunTweaksConfig();
        return new PreRunTweaksConfig
        {
            CustomSeed = source.CustomSeed,
            BypassTutorialAndDiscoveryLocks = source.BypassTutorialAndDiscoveryLocks,
            GoldRewardMultiplier = source.GoldRewardMultiplier,
            ShopDiscountMultiplier = source.ShopDiscountMultiplier,
            CardRewardCount = source.CardRewardCount,
            StartingGoldBonus = source.StartingGoldBonus,
            StartingMaxHpBonus = source.StartingMaxHpBonus,
            ForceNeowBonus = source.ForceNeowBonus,
            MapRoomCount = source.MapRoomCount,
            PlayerDamageMultiplier = source.PlayerDamageMultiplier,
            PlayerDefendMultiplier = source.PlayerDefendMultiplier,
            MaxEnergy = source.MaxEnergy,
            EnemyHealthMultiplier = source.EnemyHealthMultiplier,
            EnemyDamageMultiplier = source.EnemyDamageMultiplier,
            EnemyDefendMultiplier = source.EnemyDefendMultiplier,
            FreeMapNavigation = source.FreeMapNavigation,
            PotionSlots = source.PotionSlots,
            AllowMultipleRelics = source.AllowMultipleRelics,
            EndlessMode = new EndlessModeConfig
            {
                Enabled = source.EndlessMode.Enabled,
                EnemyScalingMultiplier = source.EndlessMode.EnemyScalingMultiplier
            },
            MapNodeDistribution = new MapNodeDistributionConfig
            {
                EliteWeightMultiplier = source.MapNodeDistribution.EliteWeightMultiplier,
                ShopWeightMultiplier = source.MapNodeDistribution.ShopWeightMultiplier,
                EventWeightMultiplier = source.MapNodeDistribution.EventWeightMultiplier,
                RestSiteWeightMultiplier = source.MapNodeDistribution.RestSiteWeightMultiplier,
                CombatWeightMultiplier = source.MapNodeDistribution.CombatWeightMultiplier,
                TreasureRoomMultiplier = source.MapNodeDistribution.TreasureRoomMultiplier
            }
        };
    }
}

/// <summary>
/// Manages persistence, lifecycle, and retrieval of active run pre-run tweaks.
/// Ensures runs generated with custom map generation parameters remain immutable across sessions,
/// endless loops inherit the original snapshot, and default vanilla runs remain non-custom.
/// </summary>
public static class RunTweaksSaveManager
{
    private const string SnapshotFilePrefix = "active_run_tweaks_p";
    private static readonly object FileLock = new();

    private static readonly FieldInfo? LastTransitioningActIndexField = AccessTools.Field(typeof(ActChangeSynchronizer), "_lastTransitioningActIndex");
    private static readonly FieldInfo? RewardsDisableProceedField = AccessTools.Field(typeof(NRewardsScreen), "_disableProceedForever");
    private static readonly FieldInfo? RewardsProceedButtonField = AccessTools.Field(typeof(NRewardsScreen), "_proceedButton");

    private static readonly JsonSerializerOptions JsonOpts = new(ActiveRunTweaksJsonContext.Default.Options)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static ActiveRunTweaksSnapshot? ActiveSnapshot { get; private set; } = null;
    public static bool HasActiveRunSnapshot => ActiveSnapshot != null;

    public static string GetSnapshotPath(int profileId)
    {
        string rootDir = ModLogger.GetModRootDirectory();
        return Path.Combine(rootDir, $"{SnapshotFilePrefix}{profileId}.json");
    }

    public static int GetCurrentProfileId()
    {
        try
        {
            if (MegaCrit.Sts2.Core.Saves.SaveManager.Instance != null)
            {
                return MegaCrit.Sts2.Core.Saves.SaveManager.Instance.CurrentProfileId;
            }
        }
        catch { }
        return 0;
    }

    public static void Initialize()
    {
        ModLogger.Verbose("RunTweaksSaveManager", "Initializing RunTweaksSaveManager subsystem...");
        TryLoadFromDisk(GetCurrentProfileId());
    }

    public static bool TryLoadFromDisk(int profileId)
    {
        lock (FileLock)
        {
            try
            {
                string path = GetSnapshotPath(profileId);
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<ActiveRunTweaksSnapshot>(json, JsonOpts);
                    if (loaded != null)
                    {
                        ActiveSnapshot = loaded;
                        RuntimeStateManager.CurrentEndlessLoopCount = loaded.EndlessLoopCount;
                        ModLogger.Info($"RunTweaksSaveManager: Loaded persisted active run snapshot for profile {profileId} (IsCustom={loaded.IsCustom}, RoomCount={loaded.PreRunTweaks.MapRoomCount}, Endless={loaded.PreRunTweaks.EndlessMode.Enabled}, Loop={loaded.EndlessLoopCount})");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RunTweaksSaveManager: Error reading snapshot for profile {profileId}", ex);
            }
            return false;
        }
    }

    public static void SaveActiveSnapshot()
    {
        if (ActiveSnapshot == null) return;
        lock (FileLock)
        {
            try
            {
                string path = GetSnapshotPath(ActiveSnapshot.ProfileId);
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(ActiveSnapshot, JsonOpts);
                File.WriteAllText(path, json);
                ModLogger.Info($"RunTweaksSaveManager: Saved active run tweaks snapshot to {path}");
            }
            catch (Exception ex)
            {
                ModLogger.Error("RunTweaksSaveManager: Failed to write active run tweaks snapshot", ex);
            }
        }
    }

    public static void StartNewRun(RunState runState)
    {
        int profileId = GetCurrentProfileId();
        var pendingTweaks = ConfigManager.Current.PreRunTweaks;

        bool isCustom = IsCustomRun(pendingTweaks);

        var snapshot = new ActiveRunTweaksSnapshot
        {
            ProfileId = profileId,
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RunSeed = runState?.Rng?.StringSeed ?? runState?.Rng?.Seed.ToString() ?? "",
            IsCustom = isCustom,
            EndlessLoopCount = 0,
            SavedAtUtc = DateTime.UtcNow,
            PreRunTweaks = ActiveRunTweaksSnapshot.ClonePreRunTweaks(pendingTweaks)
        };

        ActiveSnapshot = snapshot;
        RuntimeStateManager.CurrentEndlessLoopCount = 0;
        RuntimeStateManager.FreeMapNavigationEnabled = snapshot.PreRunTweaks.FreeMapNavigation;
        MapGenerationHooks.ResetEventVisitCounts();
        SaveActiveSnapshot();

#if DEBUG
        AIOTweaks.Core.Diagnostics.RunDiagnosticLogger.StartRunSession(runState, snapshot, ConfigManager.Current);
#endif

        ModLogger.Info($"RunTweaksSaveManager: Started NEW run snapshot (Profile={profileId}, IsCustom={isCustom}, RoomCount={snapshot.PreRunTweaks.MapRoomCount}, Endless={snapshot.PreRunTweaks.EndlessMode.Enabled})");
    }

    public static void LoadSavedRun(RunState runState, MegaCrit.Sts2.Core.Saves.SerializableRun? save)
    {
        int profileId = GetCurrentProfileId();
        ModLogger.Verbose("RunTweaksSaveManager", $"Loading saved run for profile {profileId}...");

        bool loaded = TryLoadFromDisk(profileId);
        if (!loaded)
        {
            // If no custom snapshot was saved on disk for this run, the run was started as a vanilla default run.
            // Create a clean default snapshot so pending modified menu tweaks do NOT contaminate this existing run.
            ModLogger.Info($"RunTweaksSaveManager: No custom snapshot found on disk for profile {profileId}. Binding clean default snapshot (GameMode.Standard).");
            ActiveSnapshot = new ActiveRunTweaksSnapshot
            {
                ProfileId = profileId,
                StartTime = save?.StartTime ?? 0,
                IsCustom = false,
                EndlessLoopCount = 0,
                PreRunTweaks = new PreRunTweaksConfig()
            };
            RuntimeStateManager.CurrentEndlessLoopCount = 0;
            RuntimeStateManager.FreeMapNavigationEnabled = false;
        }
        else if (ActiveSnapshot != null)
        {
            RuntimeStateManager.CurrentEndlessLoopCount = ActiveSnapshot.EndlessLoopCount;
            RuntimeStateManager.FreeMapNavigationEnabled = ActiveSnapshot.PreRunTweaks.FreeMapNavigation;
        }
        MapGenerationHooks.ResetEventVisitCounts();

#if DEBUG
        AIOTweaks.Core.Diagnostics.RunDiagnosticLogger.StartRunSession(runState, ActiveSnapshot, ConfigManager.Current);
#endif
    }

    public static void ClearActiveRun(string reason)
    {
        int profileId = ActiveSnapshot?.ProfileId ?? GetCurrentProfileId();
        ModLogger.Info($"RunTweaksSaveManager: Clearing active run snapshot (Reason: {reason}, Profile: {profileId}).");

#if DEBUG
        AIOTweaks.Core.Diagnostics.RunDiagnosticLogger.CloseRunSession(reason);
#endif

        ActiveSnapshot = null;
        RuntimeStateManager.CurrentEndlessLoopCount = 0;
        RuntimeStateManager.FreeMapNavigationEnabled = false;
        MapGenerationHooks.ResetEventVisitCounts();

        lock (FileLock)
        {
            try
            {
                string path = GetSnapshotPath(profileId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    ModLogger.Verbose("RunTweaksSaveManager", $"Deleted snapshot file: {path}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"RunTweaksSaveManager: Failed to delete snapshot file for profile {profileId}", ex);
            }
        }
    }

    public static string? GetActiveRunSeed()
    {
        string? s = ActiveSnapshot?.RunSeed;
        if (!string.IsNullOrEmpty(s)) return s;
        if (RunManager.Instance != null)
        {
            var runState = MapGenerationHooks.GetRunState(RunManager.Instance);
            return runState?.Rng?.StringSeed;
        }
        return null;
    }

    public static void IncrementEndlessLoop()
    {
        if (ActiveSnapshot != null)
        {
            ActiveSnapshot.PreRunTweaks.EndlessMode.Enabled = true;
            ActiveSnapshot.EndlessLoopCount++;
            RuntimeStateManager.CurrentEndlessLoopCount = ActiveSnapshot.EndlessLoopCount;
            SaveActiveSnapshot();
            ModLogger.Info($"RunTweaksSaveManager: Endless loop incremented to {ActiveSnapshot.EndlessLoopCount} (Scaling factor: {Math.Pow(ActiveSnapshot.PreRunTweaks.EndlessMode.EnemyScalingMultiplier, ActiveSnapshot.EndlessLoopCount):F2}x)");
        }
        else
        {
            RuntimeStateManager.CurrentEndlessLoopCount++;
        }
    }

    public static readonly MethodInfo? RecalculateTravelabilityMethod = AccessTools.Method(typeof(NMapScreen), "RecalculateTravelability");

    public static bool IsEndlessModeActive()
    {
        if (ActiveSnapshot != null)
        {
            return ActiveSnapshot.PreRunTweaks.EndlessMode.Enabled;
        }
        return ConfigManager.Current?.PreRunTweaks?.EndlessMode?.Enabled ?? false;
    }

    public static bool IsFreeMapNavigationActive()
    {
        if (ActiveSnapshot != null)
        {
            return ActiveSnapshot.PreRunTweaks.FreeMapNavigation || RuntimeStateManager.FreeMapNavigationEnabled;
        }
        return RuntimeStateManager.FreeMapNavigationEnabled || (ConfigManager.Current?.PreRunTweaks?.FreeMapNavigation ?? false);
    }

    public static void SetFreeMapNavigation(bool enabled)
    {
        RuntimeStateManager.FreeMapNavigationEnabled = enabled;
        if (ConfigManager.Current?.PreRunTweaks != null)
        {
            ConfigManager.Current.PreRunTweaks.FreeMapNavigation = enabled;
        }
        if (ActiveSnapshot?.PreRunTweaks != null)
        {
            ActiveSnapshot.PreRunTweaks.FreeMapNavigation = enabled;
            SaveActiveSnapshot();
        }
        ModLogger.Info($"RunTweaksSaveManager: FreeMapNavigation set to {enabled} (ActiveSnapshot={(ActiveSnapshot != null ? "updated" : "none")})");
    }

    public static bool IsInCombat()
    {
        try
        {
            // If map screen is open and travel is enabled, the player is on the map and allowed to choose the next node.
            var mapScreen = NMapScreen.Instance;
            if (mapScreen != null && mapScreen.IsTravelEnabled)
            {
                return false;
            }

            if (CombatManager.Instance != null && (CombatManager.Instance.IsInProgress || CombatManager.Instance.IsStarting))
            {
                return true;
            }

            // Check if ActionQueueSynchronizer is in any combat phase (PreCombatSetup, PlayPhase, NotPlayPhase, EndTurnPhaseOne)
            var syncState = RunManager.Instance?.ActionQueueSynchronizer?.CombatState;
            if (syncState.HasValue && syncState.Value != ActionSynchronizerCombatState.NotInCombat)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Error checking IsInCombat", ex);
        }
        return false;
    }

    private static readonly Callable BossProceedCallable = Callable.From<NButton>(OnRescuedBossProceedClicked);

    private static void OnRescuedBossProceedClicked(NButton btn)
    {
        try
        {
            btn.Disable();
            ModLogger.Info("Rescued Boss Room Proceed Button clicked! Initiating act transition.");
            if (RunManager.Instance?.ActChangeSynchronizer != null)
            {
                LastTransitioningActIndexField?.SetValue(RunManager.Instance.ActChangeSynchronizer, -1);
                RunManager.Instance.ActChangeSynchronizer.SetLocalPlayerReady();
            }
            else if (RunManager.Instance != null)
            {
                TaskHelper.RunSafely(RunManager.Instance.EnterNextAct());
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Error handling Boss Proceed button click", ex);
        }
    }

    public static void HideCombatRoomProceedButton()
    {
        try
        {
            var combatRoom = NCombatRoom.Instance;
            if (combatRoom != null && GodotObject.IsInstanceValid(combatRoom) && combatRoom.Mode != CombatRoomMode.VisualOnly)
            {
                var proceedBtn = combatRoom.ProceedButton;
                if (proceedBtn != null && GodotObject.IsInstanceValid(proceedBtn) && proceedBtn.Visible)
                {
                    proceedBtn.Visible = false;
                    proceedBtn.Disable();
                }
            }
        }
        catch { }
    }

    public static void RescueBossRoomProceed()
    {
        try
        {
            var runManager = RunManager.Instance;
            var runState = runManager?.DebugOnlyGetState();
            if (runManager == null || runState == null) return;

            var currentRoom = runState.CurrentRoom;
            if (currentRoom == null || currentRoom.RoomType != RoomType.Boss) return;

            // CRITICAL: The proceed button must ONLY be rescued/shown if the boss combat is ACTUALLY FINISHED!
            // When entering a boss combat or while fighting, IsPreFinished is false.
            if (currentRoom is CombatRoom combatRoomData && !combatRoomData.IsPreFinished)
            {
                HideCombatRoomProceedButton();
                return;
            }

            // If still in active combat, wait until combat ends
            if (IsInCombat())
            {
                HideCombatRoomProceedButton();
                return;
            }

            var combatRoomCheck = NCombatRoom.Instance;
            if (combatRoomCheck != null && GodotObject.IsInstanceValid(combatRoomCheck) && combatRoomCheck.Mode == CombatRoomMode.ActiveCombat)
            {
                HideCombatRoomProceedButton();
                return;
            }

            // Ensure ActChangeSynchronizer's _lastTransitioningActIndex is reset if it blocks the current act
            if (runManager.ActChangeSynchronizer != null && LastTransitioningActIndexField != null)
            {
                int lastAct = (int)(LastTransitioningActIndexField.GetValue(runManager.ActChangeSynchronizer) ?? -1);
                if (lastAct >= runState.CurrentActIndex)
                {
                    LastTransitioningActIndexField.SetValue(runManager.ActChangeSynchronizer, -1);
                    ModLogger.Info($"RescueBossRoomProceed: Reset _lastTransitioningActIndex from {lastAct} to -1.");
                }
            }

            var rewardsScreen = NOverlayStack.Instance?.Peek() as NRewardsScreen;
            if (rewardsScreen != null && GodotObject.IsInstanceValid(rewardsScreen))
            {
                var proceedBtn = RewardsProceedButtonField?.GetValue(rewardsScreen) as NProceedButton;

                if (proceedBtn != null && GodotObject.IsInstanceValid(proceedBtn))
                {
                    bool isForeverDisabled = (bool)(RewardsDisableProceedField?.GetValue(rewardsScreen) ?? false);
                    if (isForeverDisabled || !proceedBtn.IsEnabled)
                    {
                        RewardsDisableProceedField?.SetValue(rewardsScreen, false);
                        proceedBtn.Visible = true;
                        proceedBtn.UpdateText(NProceedButton.ProceedLoc);
                        proceedBtn.Enable();
                        proceedBtn.SetPulseState(true);
                    }
                }
                return;
            }

            var combatRoom = NCombatRoom.Instance;
            if (combatRoom != null && GodotObject.IsInstanceValid(combatRoom))
            {
                var proceedBtn = combatRoom.ProceedButton;
                if (proceedBtn != null && GodotObject.IsInstanceValid(proceedBtn))
                {
                    if (!proceedBtn.Visible || !proceedBtn.IsEnabled)
                    {
                        proceedBtn.Visible = true;
                        proceedBtn.UpdateText(NProceedButton.ProceedLoc);
                        proceedBtn.Enable();
                        proceedBtn.SetPulseState(true);

                        if (!proceedBtn.IsConnected(NClickableControl.SignalName.Released, BossProceedCallable))
                        {
                            proceedBtn.Connect(NClickableControl.SignalName.Released, BossProceedCallable);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Error in RescueBossRoomProceed", ex);
        }
    }

    public static void RefreshMapNavigationState(bool isFreeRoam)
    {
        try
        {
            if (IsInCombat()) return;
            var screen = NMapScreen.Instance;
            if (screen != null && GodotObject.IsInstanceValid(screen))
            {
                screen.IsTraveling = false;
                if (!isFreeRoam)
                {
                    RecalculateTravelabilityMethod?.Invoke(screen, null);
                }
                screen.RefreshAllPointVisuals();
                ModLogger.Info($"RunTweaksSaveManager: Refreshed map navigation state (isFreeRoam={isFreeRoam})");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Error refreshing map navigation state", ex);
        }
    }

    public static PreRunTweaksConfig GetEffectivePreRunTweaks()
    {
        return ActiveSnapshot?.PreRunTweaks ?? ConfigManager.Current.PreRunTweaks;
    }

    public static bool IsMapTweakModified(PreRunTweaksConfig preRun)
    {
        if (preRun == null) return false;
        var dist = preRun.MapNodeDistribution;

        return Math.Abs(dist.EliteWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.ShopWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.EventWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.RestSiteWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.CombatWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.TreasureRoomMultiplier - 1.0f) > 0.001f ||
               preRun.MapRoomCount != 15 ||
               preRun.FreeMapNavigation ||
               RuntimeStateManager.FreeMapNavigationEnabled ||
               preRun.EndlessMode.Enabled;
    }

    public static bool IsCustomRun(PreRunTweaksConfig preRun)
    {
        if (IsMapTweakModified(preRun)) return true;
        if (!string.IsNullOrWhiteSpace(preRun.CustomSeed)) return true;

        return Math.Abs(preRun.GoldRewardMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.ShopDiscountMultiplier - 1.0f) > 0.001f ||
               preRun.CardRewardCount != 3 ||
               preRun.StartingGoldBonus != 0 ||
               preRun.StartingMaxHpBonus != 0 ||
               Math.Abs(preRun.PlayerDamageMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.PlayerDefendMultiplier - 1.0f) > 0.001f ||
               preRun.MaxEnergy != 3 ||
               Math.Abs(preRun.EnemyHealthMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.EnemyDamageMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.EnemyDefendMultiplier - 1.0f) > 0.001f ||
               preRun.PotionSlots != 3 ||
               preRun.AllowMultipleRelics;
    }

    public static bool IsCustomRunPending()
    {
        return IsCustomRun(ConfigManager.Current.PreRunTweaks);
    }

    public static bool AreMapTweaksModified()
    {
        return IsMapTweakModified(GetEffectivePreRunTweaks());
    }

    public static bool IsRunActiveAndCustom()
    {
        return ActiveSnapshot?.IsCustom == true;
    }
}
