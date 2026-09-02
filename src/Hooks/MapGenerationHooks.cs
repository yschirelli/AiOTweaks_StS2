using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace AIOTweaks.Hooks;

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

    [JsonPropertyName("runSettings")]
    public RunSettings RunSettings { get; set; } = new();

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
            PreRunTweaks = ClonePreRunTweaks(PreRunTweaks),
            RunSettings = RunSettings.Clone()
        };
    }

    public static PreRunTweaksConfig ClonePreRunTweaks(PreRunTweaksConfig source)
    {
        if (source == null) return new PreRunTweaksConfig();
        return new PreRunTweaksConfig
        {
            GoldRewardMultiplier = source.GoldRewardMultiplier,
            ShopDiscountMultiplier = source.ShopDiscountMultiplier,
            CardRewardCount = source.CardRewardCount,
            StartingGoldBonus = source.StartingGoldBonus,
            StartingMaxHpBonus = source.StartingMaxHpBonus,
            ForceNeowBonus = source.ForceNeowBonus,
            MapRoomCount = source.MapRoomCount,
            PlayerDamageMultiplier = source.PlayerDamageMultiplier,
            MaxEnergy = source.MaxEnergy,
            EnemyHealthMultiplier = source.EnemyHealthMultiplier,
            EnemyDamageMultiplier = source.EnemyDamageMultiplier,
            EnemyDefendMultiplier = source.EnemyDefendMultiplier,
            FreeMapNavigation = source.FreeMapNavigation,
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

    private static readonly JsonSerializerOptions JsonOpts = new()
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
        var pendingSettings = ConfigManager.ActiveRunSettings;

        bool isCustom = IsCustomRun(pendingTweaks, pendingSettings);

        var snapshot = new ActiveRunTweaksSnapshot
        {
            ProfileId = profileId,
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RunSeed = runState?.Rng?.Seed.ToString() ?? "",
            IsCustom = isCustom,
            EndlessLoopCount = 0,
            SavedAtUtc = DateTime.UtcNow,
            PreRunTweaks = ActiveRunTweaksSnapshot.ClonePreRunTweaks(pendingTweaks),
            RunSettings = pendingSettings.Clone()
        };

        ActiveSnapshot = snapshot;
        RuntimeStateManager.CurrentEndlessLoopCount = 0;
        SaveActiveSnapshot();

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
            // Create a clean default snapshot so pending modified menu tweaks do NOT contaminate this existing run!
            ModLogger.Info($"RunTweaksSaveManager: No custom snapshot found on disk for profile {profileId}. Binding clean default snapshot (GameMode.Standard).");
            ActiveSnapshot = new ActiveRunTweaksSnapshot
            {
                ProfileId = profileId,
                StartTime = save?.StartTime ?? 0,
                IsCustom = false,
                EndlessLoopCount = 0,
                PreRunTweaks = new PreRunTweaksConfig(),
                RunSettings = new RunSettings()
            };
            RuntimeStateManager.CurrentEndlessLoopCount = 0;
        }
        else if (ActiveSnapshot != null)
        {
            RuntimeStateManager.CurrentEndlessLoopCount = ActiveSnapshot.EndlessLoopCount;
        }
    }

    public static void ClearActiveRun(string reason)
    {
        int profileId = ActiveSnapshot?.ProfileId ?? GetCurrentProfileId();
        ModLogger.Info($"RunTweaksSaveManager: Clearing active run snapshot (Reason: {reason}, Profile: {profileId}).");
        ActiveSnapshot = null;
        RuntimeStateManager.CurrentEndlessLoopCount = 0;

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

    public static void IncrementEndlessLoop()
    {
        if (ActiveSnapshot != null)
        {
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

    public static PreRunTweaksConfig GetEffectivePreRunTweaks()
    {
        if (ActiveSnapshot != null)
        {
            return ActiveSnapshot.PreRunTweaks;
        }
        return ConfigManager.Current.PreRunTweaks;
    }

    public static RunSettings GetEffectiveRunSettings()
    {
        if (ActiveSnapshot != null)
        {
            return ActiveSnapshot.RunSettings;
        }
        return ConfigManager.ActiveRunSettings;
    }

    public static bool IsMapTweakModified(PreRunTweaksConfig preRun, RunSettings runSettings)
    {
        if (preRun == null || runSettings == null) return false;
        var dist = preRun.MapNodeDistribution;

        return Math.Abs(dist.EliteWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.ShopWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.EventWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.RestSiteWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.CombatWeightMultiplier - 1.0f) > 0.001f ||
               Math.Abs(dist.TreasureRoomMultiplier - 1.0f) > 0.001f ||
               Math.Abs(runSettings.EliteSpawnMultiplier - 1.0f) > 0.001f ||
               Math.Abs(runSettings.ShopSpawnMultiplier - 1.0f) > 0.001f ||
               Math.Abs(runSettings.EventSpawnMultiplier - 1.0f) > 0.001f ||
               preRun.MapRoomCount != 15 ||
               preRun.FreeMapNavigation ||
               RuntimeStateManager.FreeMapNavigationEnabled ||
               preRun.EndlessMode.Enabled;
    }

    public static bool IsCustomRun(PreRunTweaksConfig preRun, RunSettings runSettings)
    {
        if (IsMapTweakModified(preRun, runSettings)) return true;

        return Math.Abs(preRun.GoldRewardMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.ShopDiscountMultiplier - 1.0f) > 0.001f ||
               preRun.CardRewardCount != 3 ||
               preRun.StartingGoldBonus != 0 ||
               preRun.StartingMaxHpBonus != 0 ||
               Math.Abs(preRun.PlayerDamageMultiplier - 1.0f) > 0.001f ||
               preRun.MaxEnergy != 3 ||
               Math.Abs(preRun.EnemyHealthMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.EnemyDamageMultiplier - 1.0f) > 0.001f ||
               Math.Abs(preRun.EnemyDefendMultiplier - 1.0f) > 0.001f ||
               Math.Abs(runSettings.GoldMultiplier - 1.0f) > 0.001f ||
               runSettings.ActiveModifiers.Count > 0 ||
               runSettings.CustomStartingCards.Count > 0 ||
               runSettings.CustomStartingRelics.Count > 0 ||
               runSettings.DraftModeEnabled;
    }

    public static bool IsCustomRunPending()
    {
        return IsCustomRun(ConfigManager.Current.PreRunTweaks, ConfigManager.ActiveRunSettings);
    }

    public static bool AreMapTweaksModified()
    {
        return IsMapTweakModified(GetEffectivePreRunTweaks(), GetEffectiveRunSettings());
    }
}

/// <summary>
/// Intercepts map node generation weights (Elites, Shops, Unknowns, Rest Sites, Combats),
/// map floor/room length, Neow/Ancient room assignment, and free map navigation (Flying Boots mode).
/// Enforces fair play by switching runs to Seeded/Custom mode when map generation or cheats are active.
/// </summary>
public static class MapGenerationHooks
{
    private static readonly FieldInfo? ElitesField = typeof(MapPointTypeCounts).GetField("<NumOfElites>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ShopsField = typeof(MapPointTypeCounts).GetField("<NumOfShops>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? UnknownsField = typeof(MapPointTypeCounts).GetField("<NumOfUnknowns>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RestsField = typeof(MapPointTypeCounts).GetField("<NumOfRests>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? MapLengthField = typeof(StandardActMap).GetField("_mapLength", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly PropertyInfo? RunManagerStateProp = typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RoomSetAncientField = typeof(RoomSet).GetField("_ancient", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RoomSetBossField = typeof(RoomSet).GetField("_boss", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ActModelRoomsField = typeof(ActModel).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance);

    private static RunState? GetRunState(RunManager? runManager)
    {
        if (runManager == null) return null;
        return (RunManagerStateProp?.GetValue(runManager) as RunState) ?? runManager.DebugOnlyGetState();
    }

    /// <summary>
    /// Evaluates if any map generation weights, multipliers, or pre-run tweaks have been modified from game default.
    /// </summary>
    public static bool AreMapTweaksModified()
    {
        return RunTweaksSaveManager.AreMapTweaksModified();
    }

    /// <summary>
    /// Enforces fair play on singleplayer/new run embarkation:
    /// If tweaks are customized, switch GameMode to Custom (Seeded/Fair mode)
    /// to disable achievements and epoch unlocks.
    /// If defaults are used, preserves GameMode.Standard.
    /// </summary>
    [HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
    public static class RunStateCreateForNewRunPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref GameMode gameMode)
        {
            try
            {
                ModLogger.Verbose("MapGenerationHooks", $"RunState.CreateForNewRun prefix evaluating GameMode: incoming={gameMode}");
                if (RunTweaksSaveManager.IsCustomRunPending())
                {
                    if (gameMode == GameMode.Standard)
                    {
                        gameMode = GameMode.Custom;
                        ModLogger.Info("Pre-run tweaks or map modifiers are active: Automatically set GameMode to Custom (Seeded/Fair Mode).");
                    }
                }
                else
                {
                    ModLogger.Info("Map generation tweaks are at game default: Proceeding with standard run (GameMode.Standard).");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error evaluating fair play GameMode in RunState.CreateForNewRun", ex);
            }
        }
    }

    /// <summary>
    /// Enforces fair play on shared/multiplayer run creation.
    /// </summary>
    [HarmonyPatch(typeof(RunState), "CreateShared")]
    public static class RunStateCreateSharedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref GameMode gameMode)
        {
            try
            {
                ModLogger.Verbose("MapGenerationHooks", $"RunState.CreateShared prefix evaluating GameMode: incoming={gameMode}");
                if (RunTweaksSaveManager.IsCustomRunPending())
                {
                    if (gameMode == GameMode.Standard)
                    {
                        gameMode = GameMode.Custom;
                        ModLogger.Info("Pre-run tweaks are modified: Automatically set shared GameMode to Custom (Seeded/Fair Mode).");
                    }
                }
                else
                {
                    ModLogger.Info("Shared map generation tweaks at game default: Proceeding with standard run.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error evaluating fair play GameMode in RunState.CreateShared", ex);
            }
        }
    }

    #region Run Lifecycle & Persistence Patches

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
    public static class RunManagerSetUpNewSingleplayerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RunState state)
        {
            try
            {
                RunTweaksSaveManager.StartNewRun(state);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerSetUpNewSingleplayerPatch snapshotting active run tweaks", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
    public static class RunManagerSetUpNewMultiplayerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RunState state)
        {
            try
            {
                RunTweaksSaveManager.StartNewRun(state);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerSetUpNewMultiplayerPatch snapshotting active run tweaks", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer))]
    public static class RunManagerSetUpSavedSingleplayerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RunState state, MegaCrit.Sts2.Core.Saves.SerializableRun save)
        {
            try
            {
                RunTweaksSaveManager.LoadSavedRun(state, save);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerSetUpSavedSingleplayerPatch loading active run tweaks snapshot", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))]
    public static class RunManagerSetUpSavedMultiplayerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RunState state, MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.LoadRunLobby lobby)
        {
            try
            {
                RunTweaksSaveManager.LoadSavedRun(state, lobby?.Run);
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerSetUpSavedMultiplayerPatch loading active run tweaks snapshot", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), "AbandonInternal")]
    public static class RunManagerAbandonPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                RunTweaksSaveManager.ClearActiveRun("Abandon");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerAbandonPatch clearing active run snapshot", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), "GuaranteeKillAllPlayers")]
    public static class RunManagerGuaranteeKillAllPlayersPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                RunTweaksSaveManager.ClearActiveRun("PlayerDeath");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerGuaranteeKillAllPlayersPatch clearing active run snapshot", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.WinRun))]
    public static class RunManagerWinRunPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RunManager __instance, ref Task __result)
        {
            try
            {
                var tweaks = RunTweaksSaveManager.GetEffectivePreRunTweaks();
                if (tweaks.EndlessMode.Enabled)
                {
                    ModLogger.Info("Endless Mode active upon Act completion! Looping back to Act 0 and scaling enemies...");
                    RunTweaksSaveManager.IncrementEndlessLoop();
                    __result = LoopEndlessRunAsync(__instance);
                    return false;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error evaluating Endless Mode in RunManager.WinRun", ex);
            }
            return true;
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                var tweaks = RunTweaksSaveManager.GetEffectivePreRunTweaks();
                if (!tweaks.EndlessMode.Enabled)
                {
                    RunTweaksSaveManager.ClearActiveRun("WinRun");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RunManagerWinRunPatch clearing active run snapshot", ex);
            }
        }

        private static async Task LoopEndlessRunAsync(RunManager runManager)
        {
            await runManager.EnterAct(0, true);
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Saves.SaveManager), nameof(MegaCrit.Sts2.Core.Saves.SaveManager.DeleteCurrentRun))]
    public static class SaveManagerDeleteCurrentRunPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                RunTweaksSaveManager.ClearActiveRun("DeleteCurrentRun");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in SaveManagerDeleteCurrentRunPatch clearing active run snapshot", ex);
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Saves.SaveManager), nameof(MegaCrit.Sts2.Core.Saves.SaveManager.DeleteCurrentMultiplayerRun))]
    public static class SaveManagerDeleteCurrentMultiplayerRunPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                RunTweaksSaveManager.ClearActiveRun("DeleteCurrentMultiplayerRun");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in SaveManagerDeleteCurrentMultiplayerRunPatch clearing active run snapshot", ex);
            }
        }
    }

    #endregion

    /// <summary>
    /// Adjusts generated map length (room/floor count) during ActModel room count calculation across all acts.
    /// StandardActMap and SpoilsActMap invoke ActModel.GetNumberOfRooms() to determine map length.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.ActModel), nameof(MegaCrit.Sts2.Core.Models.ActModel.GetNumberOfRooms))]
    public static class ActModelGetNumberOfRoomsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isMultiplayer, ref int __result)
        {
            try
            {
                int desiredRooms = Math.Max(15, RunTweaksSaveManager.GetEffectivePreRunTweaks().MapRoomCount);
                if (desiredRooms > 15)
                {
                    __result = isMultiplayer ? Math.Max(1, desiredRooms - 1) : desiredRooms;
                    ModLogger.Verbose("MapGenerationHooks", $"ActModel.GetNumberOfRooms: Custom MapRoomCount applied -> {__result} rooms for act.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in ActModelGetNumberOfRoomsPatch setting map room count", ex);
            }
        }
    }

    /// <summary>
    /// Adjusts generated map node counts according to configured multipliers and room count scaling during map construction.
    /// </summary>
    [HarmonyPatch(typeof(MapPointTypeCounts), MethodType.Constructor, new Type[] { typeof(int), typeof(int) })]
    public static class MapPointTypeCountsConstructorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MapPointTypeCounts __instance, int unknownCount, int restCount)
        {
            try
            {
                if (!AreMapTweaksModified()) return;

                var dist = RunTweaksSaveManager.GetEffectivePreRunTweaks().MapNodeDistribution;
                var runSettings = RunTweaksSaveManager.GetEffectiveRunSettings();
                int desiredRooms = Math.Max(15, RunTweaksSaveManager.GetEffectivePreRunTweaks().MapRoomCount);
                float roomScale = desiredRooms > 15 ? (float)desiredRooms / 15.0f : 1.0f;

                float combatMult = dist.CombatWeightMultiplier;
                float normalize = 1.0f / Math.Max(0.01f, combatMult);

                float eliteMult = dist.EliteWeightMultiplier * runSettings.EliteSpawnMultiplier * roomScale * normalize;
                float shopMult = dist.ShopWeightMultiplier * runSettings.ShopSpawnMultiplier * roomScale * normalize;
                float eventMult = dist.EventWeightMultiplier * runSettings.EventSpawnMultiplier * roomScale * normalize;
                float restMult = dist.RestSiteWeightMultiplier * roomScale * normalize;

                ModLogger.Verbose("MapGenerationHooks", $"MapPointTypeCounts constructor postfix: raw Elites={__instance.NumOfElites}, Shops={__instance.NumOfShops}, Unknowns={__instance.NumOfUnknowns}, Rests={__instance.NumOfRests}, roomScale={roomScale:F2}");

                if (Math.Abs(eliteMult - 1.0f) > 0.001f && ElitesField != null)
                {
                    int adjustedElites = Math.Max(0, (int)Math.Round(__instance.NumOfElites * eliteMult));
                    ElitesField.SetValue(__instance, adjustedElites);
                }

                if (Math.Abs(shopMult - 1.0f) > 0.001f && ShopsField != null)
                {
                    int adjustedShops = Math.Max(0, (int)Math.Round(__instance.NumOfShops * shopMult));
                    ShopsField.SetValue(__instance, adjustedShops);
                }

                if (Math.Abs(eventMult - 1.0f) > 0.001f && UnknownsField != null)
                {
                    int adjustedUnknowns = Math.Max(0, (int)Math.Round(__instance.NumOfUnknowns * eventMult));
                    UnknownsField.SetValue(__instance, adjustedUnknowns);
                }

                if (Math.Abs(restMult - 1.0f) > 0.001f && RestsField != null)
                {
                    int adjustedRests = Math.Max(0, (int)Math.Round(__instance.NumOfRests * restMult));
                    RestsField.SetValue(__instance, adjustedRests);
                }

                ModLogger.Verbose("MapGenerationHooks", $"MapPointTypeCounts adjusted: Elites={__instance.NumOfElites}, Shops={__instance.NumOfShops}, Unknowns={__instance.NumOfUnknowns}, Rests={__instance.NumOfRests}");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MapPointTypeCounts for modified map generation tweaks", ex);
            }
        }
    }

    /// <summary>
    /// Distributes treasure rooms proportionally across the act based on configured
    /// TreasureRoomMultiplier and total act room/floor count.
    /// </summary>
    [HarmonyPatch(typeof(StandardActMap), "AssignPointTypes")]
    public static class StandardActMapAssignPointTypesPatch
    {
        private static readonly FieldInfo? GridField = typeof(StandardActMap).GetField("<Grid>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? PointTypeCountsField = typeof(StandardActMap).GetField("_pointTypeCounts", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? AssignRemainingTypesMethod = typeof(StandardActMap).GetMethod("AssignRemainingTypesToRandomPoints", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void ForEachInRow(MapPoint?[,] grid, int rowIndex, Action<MapPoint> processor)
        {
            if (grid == null || rowIndex < 0 || rowIndex >= grid.GetLength(1)) return;
            for (int i = 0; i < grid.GetLength(0); i++)
            {
                MapPoint? mapPoint = grid[i, rowIndex];
                if (mapPoint != null)
                {
                    processor(mapPoint);
                }
            }
        }

        public static List<int> CalculateTreasureRows(int rowCount, float treasureMultiplier)
        {
            var rows = new List<int>();
            if (treasureMultiplier <= 0.001f || rowCount < 4)
            {
                return rows;
            }

            float effectiveMultiplier = (rowCount / 15.0f) * treasureMultiplier;
            int numTreasureRows = Math.Max(1, (int)Math.Round(effectiveMultiplier));

            // Available intermediate rows span from row 2 to rowCount - 2
            // (row 1 is starting monster row, rowCount - 1 is rest site before boss)
            int maxPossibleTreasureRows = Math.Max(1, rowCount - 3);
            numTreasureRows = Math.Min(numTreasureRows, maxPossibleTreasureRows);

            if (numTreasureRows == 1 && rowCount == 15 && Math.Abs(treasureMultiplier - 1.0f) < 0.001f)
            {
                // Preserve exact vanilla 15-floor standard position (15 - 7 = 8)
                rows.Add(8);
                return rows;
            }

            // Distribute treasure rows with proportional spacing across the playable floor span
            float step = (float)(rowCount - 1) / (numTreasureRows + 1);
            for (int i = 1; i <= numTreasureRows; i++)
            {
                int r = (int)Math.Round(i * step);
                r = Math.Clamp(r, 2, rowCount - 2);

                if (rows.Count > 0 && r <= rows[^1])
                {
                    r = rows[^1] + 1;
                }

                if (r <= rowCount - 2)
                {
                    rows.Add(r);
                }
            }

            return rows;
        }

        [HarmonyPrefix]
        public static bool Prefix(StandardActMap __instance)
        {
            try
            {
                var grid = GridField?.GetValue(__instance) as MapPoint?[,];
                int rowCount = __instance.GetRowCount();
                if (grid == null || rowCount < 2) return true;

                ForEachInRow(grid, rowCount - 1, p =>
                {
                    p.PointType = MapPointType.RestSite;
                    p.CanBeModified = false;
                });

                float treasureMult = RunTweaksSaveManager.GetEffectivePreRunTweaks().MapNodeDistribution.TreasureRoomMultiplier;
                var treasureRows = CalculateTreasureRows(rowCount, treasureMult);
                var treasureType = __instance.ShouldReplaceTreasureWithElites ? MapPointType.Elite : MapPointType.Treasure;

                foreach (int treasureRow in treasureRows)
                {
                    ForEachInRow(grid, treasureRow, p =>
                    {
                        p.PointType = treasureType;
                        p.CanBeModified = false;
                    });
                }

                ForEachInRow(grid, 1, p =>
                {
                    p.PointType = MapPointType.Monster;
                    p.CanBeModified = false;
                });

                var pointTypeCounts = PointTypeCountsField?.GetValue(__instance) as MapPointTypeCounts;
                if (pointTypeCounts != null && AssignRemainingTypesMethod != null)
                {
                    var list = new List<MapPointType>();
                    for (int i = 0; i < pointTypeCounts.NumOfRests; i++) list.Add(MapPointType.RestSite);
                    for (int j = 0; j < pointTypeCounts.NumOfShops; j++) list.Add(MapPointType.Shop);
                    for (int k = 0; k < pointTypeCounts.NumOfElites; k++) list.Add(MapPointType.Elite);
                    for (int l = 0; l < pointTypeCounts.NumOfUnknowns; l++) list.Add(MapPointType.Unknown);

                    var queue = new Queue<MapPointType>(list);
                    AssignRemainingTypesMethod.Invoke(__instance, new object[] { queue });
                }

                foreach (MapPoint p in __instance.GetAllMapPoints())
                {
                    if (p.PointType == MapPointType.Unassigned)
                    {
                        p.PointType = MapPointType.Monster;
                    }
                }

                __instance.BossMapPoint.PointType = MapPointType.Boss;
                __instance.StartingMapPoint.PointType = MapPointType.Ancient;
                if (__instance.SecondBossMapPoint != null)
                {
                    __instance.SecondBossMapPoint.PointType = MapPointType.Boss;
                }

                ModLogger.Verbose("MapGenerationHooks", $"AssignPointTypes completed: rowCount={rowCount}, treasureMult={treasureMult:F1}, treasureRows=[{string.Join(",", treasureRows)}]");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in StandardActMapAssignPointTypesPatch, falling back to original logic", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Prevents 'Cannot find next node' exception in StandardActMap.GenerateNextCoord when generating large maps.
    /// If all 3 direction choices produce a crossover conflict, relaxes crossover check to guarantee path completion.
    /// </summary>
    [HarmonyPatch(typeof(StandardActMap), "GenerateNextCoord")]
    public static class StandardActMapGenerateNextCoordPatch
    {
        private static readonly MethodInfo? HasInvalidCrossoverMethod = typeof(StandardActMap).GetMethod("HasInvalidCrossover", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? RngField = typeof(StandardActMap).GetField("_rng", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        public static bool Prefix(StandardActMap __instance, MapPoint current, ref MapCoord __result)
        {
            try
            {
                int col = current.coord.col;
                int leftCol = Math.Max(0, col - 1);
                int rightCol = Math.Min(col + 1, 6);
                int row = current.coord.row + 1;

                var rng = (RngField?.GetValue(__instance) as MegaCrit.Sts2.Core.Random.Rng) ?? new MegaCrit.Sts2.Core.Random.Rng(0, "generate_next_coord_fallback");
                var directions = new System.Collections.Generic.List<int> { -1, 0, 1 };

                for (int i = directions.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    (directions[i], directions[j]) = (directions[j], directions[i]);
                }

                foreach (int dir in directions)
                {
                    int targetCol = dir switch
                    {
                        -1 => leftCol,
                        0 => col,
                        1 => rightCol,
                        _ => col
                    };

                    bool hasCrossover = false;
                    if (HasInvalidCrossoverMethod != null)
                    {
                        hasCrossover = (bool)(HasInvalidCrossoverMethod.Invoke(__instance, new object[] { current, targetCol }) ?? false);
                    }

                    if (!hasCrossover)
                    {
                        __result = new MapCoord { col = targetCol, row = row };
                        return false;
                    }
                }

                // 2. Safe Fallback for long maps: if boxed in by crossovers, choose straight or nearest valid column without crashing
                int fallbackCol = col;
                if (leftCol != col && rightCol != col)
                {
                    fallbackCol = rng.NextBool() ? leftCol : rightCol;
                }
                else if (leftCol != col)
                {
                    fallbackCol = leftCol;
                }
                else if (rightCol != col)
                {
                    fallbackCol = rightCol;
                }

                ModLogger.Verbose("MapGenerationHooks", $"GenerateNextCoord fallback step used at row {row} (col {col} -> {fallbackCol}) to avoid generation deadlock.");
                __result = new MapCoord { col = fallbackCol, row = row };
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in StandardActMapGenerateNextCoordPatch safe generation", ex);
                __result = new MapCoord { col = current.coord.col, row = current.coord.row + 1 };
                return false;
            }
        }
    }

    /// <summary>
    /// Replaces the exponential O(2^N) recursive path enumeration in MapPathPruning.FindAllPaths
    /// with a fast bounded DFS search (capped to 150 paths).
    /// </summary>
    [HarmonyPatch(typeof(MapPathPruning), nameof(MapPathPruning.FindAllPaths))]
    public static class MapPathPruningFindAllPathsPatch
    {
        private const int MaxPathsToAnalyze = 150;

        [HarmonyPrefix]
        public static bool Prefix(MapPoint currentMapPoint, ref List<List<MapPoint>> __result)
        {
            try
            {
                var result = new List<List<MapPoint>>();
                var currentPath = new List<MapPoint>();

                void Dfs(MapPoint node)
                {
                    if (result.Count >= MaxPathsToAnalyze) return;

                    currentPath.Add(node);

                    if (node.PointType == MapPointType.Boss || node.Children.Count == 0)
                    {
                        result.Add(new List<MapPoint>(currentPath));
                    }
                    else
                    {
                        foreach (MapPoint child in node.Children)
                        {
                            Dfs(child);
                            if (result.Count >= MaxPathsToAnalyze) break;
                        }
                    }

                    currentPath.RemoveAt(currentPath.Count - 1);
                }

                Dfs(currentMapPoint);
                __result = result;
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in MapPathPruningFindAllPathsPatch, falling back to empty path list", ex);
                __result = new List<List<MapPoint>>();
                return false;
            }
        }
    }

    /// <summary>
    /// Caps the duplicate segment pruning loop to at most 20 passes to prevent iteration deadlocks
    /// or throwing InvalidOperationException on complex dense maps.
    /// </summary>
    [HarmonyPatch(typeof(MapPathPruning), nameof(MapPathPruning.PruneDuplicateSegments))]
    public static class MapPathPruningPruneDuplicateSegmentsPatch
    {
        private static readonly MethodInfo? PrunePathsMethod = typeof(MapPathPruning).GetMethod("PrunePaths", BindingFlags.NonPublic | BindingFlags.Static);

        [HarmonyPrefix]
        public static bool Prefix(MapPoint?[,] grid, HashSet<MapPoint> startMapPoints, MapPoint startingMapPoint, MegaCrit.Sts2.Core.Random.Rng rng)
        {
            try
            {
                int iterations = 0;
                List<List<MapPoint[]>> matchingSegments = MapPathPruning.FindMatchingSegments(startingMapPoint);
                while (matchingSegments.Count > 0 && iterations < 20)
                {
                    bool pruned = false;
                    if (PrunePathsMethod != null)
                    {
                        pruned = (bool)(PrunePathsMethod.Invoke(null, new object[] { grid, startMapPoints, matchingSegments, rng }) ?? false);
                    }
                    if (!pruned) break;

                    iterations++;
                    matchingSegments = MapPathPruning.FindMatchingSegments(startingMapPoint);
                }
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Verbose("MapGenerationHooks", $"PruneDuplicateSegments safe completion: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Caps the oscillation loop in SpreadAdjacentMapPoints to at most 10 passes per row.
    /// Prevents infinite loops if node placement constraints oscillate.
    /// </summary>
    [HarmonyPatch(typeof(MapPostProcessing), nameof(MapPostProcessing.SpreadAdjacentMapPoints))]
    public static class MapPostProcessingSpreadAdjacentMapPointsPatch
    {
        private static readonly MethodInfo? GetAllowedPositionsMethod = typeof(MapPostProcessing).GetMethod("GetAllowedPositions", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? ComputeGapMethod = typeof(MapPostProcessing).GetMethod("ComputeGap", BindingFlags.NonPublic | BindingFlags.Static);

        [HarmonyPrefix]
        public static bool Prefix(MapPoint?[,] grid, ref MapPoint?[,] __result)
        {
            try
            {
                int length = grid.GetLength(0);
                int length2 = grid.GetLength(1);
                for (int i = 0; i < length2; i++)
                {
                    List<MapPoint> list = new List<MapPoint>(length2);
                    for (int j = 0; j < length; j++)
                    {
                        MapPoint? mapPoint = grid[j, i];
                        if (mapPoint != null)
                        {
                            list.Add(mapPoint);
                        }
                    }

                    int passes = 0;
                    bool flag;
                    do
                    {
                        flag = false;
                        passes++;
                        if (passes > 10) break; // Hard safety cap against infinite oscillation

                        foreach (MapPoint item in list)
                        {
                            int col = item.coord.col;
                            HashSet<int>? allowedPositions = GetAllowedPositionsMethod?.Invoke(null, new object[] { item, length }) as HashSet<int>;
                            if (allowedPositions == null) continue;

                            int num = (int)(ComputeGapMethod?.Invoke(null, new object[] { col, list, item }) ?? int.MaxValue);
                            int num2 = col;
                            int num3 = num;
                            foreach (int item2 in allowedPositions)
                            {
                                if (item2 != col && (grid[item2, i] == null || grid[item2, i] == item))
                                {
                                    int num4 = (int)(ComputeGapMethod?.Invoke(null, new object[] { item2, list, item }) ?? int.MaxValue);
                                    if (num4 > num3)
                                    {
                                        num2 = item2;
                                        num3 = num4;
                                    }
                                }
                            }
                            if (num2 != col)
                            {
                                grid[col, i] = null;
                                grid[num2, i] = item;
                                item.coord.col = num2;
                                flag = true;
                            }
                        }
                    }
                    while (flag);
                }

                __result = grid;
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in MapPostProcessingSpreadAdjacentMapPointsPatch, returning raw grid", ex);
                __result = grid;
                return false;
            }
        }
    }

    /// Protects StandardActMap.CreateFor against any uncaught exceptions by returning a verified fallback map.
    /// </summary>
    [HarmonyPatch(typeof(StandardActMap), nameof(StandardActMap.CreateFor))]
    public static class StandardActMapCreateForPatch
    {
        [HarmonyFinalizer]
        public static Exception? Finalizer(RunState runState, bool replaceTreasureWithElites, Exception? __exception, ref StandardActMap __result)
        {
            if (__exception != null)
            {
                ModLogger.Error("StandardActMap.CreateFor encountered an error during custom generation; recovering with fallback standard map.", __exception);
                try
                {
                    __result = new StandardActMap(
                        new MegaCrit.Sts2.Core.Random.Rng(runState.Rng.Seed, $"act_{runState.CurrentActIndex + 1}_map_fallback"),
                        runState.Act,
                        runState.Players.Count > 1,
                        replaceTreasureWithElites,
                        runState.Act.HasSecondBoss,
                        null,
                        false // Disable pruning on fallback to guarantee 100% reliability
                    );
                    return null; // Suppress exception
                }
                catch (Exception fallbackEx)
                {
                    ModLogger.Error("Critical error creating fallback StandardActMap", fallbackEx);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Free Map Navigation (Flying Boots style): makes all map points clickable regardless of standard pathing connections.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint), "get_IsTravelable")]
    public static class NMapPointIsTravelablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || RunTweaksSaveManager.GetEffectivePreRunTweaks().FreeMapNavigation)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Enables debug travel on NMapScreen when Free Map Navigation is enabled.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), "get_IsDebugTravelEnabled")]
    public static class NMapScreenIsDebugTravelEnabledPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || RunTweaksSaveManager.GetEffectivePreRunTweaks().FreeMapNavigation)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// When traveling on map with Free Map Navigation active, marks active run as Custom mode and resets IsTraveling.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.TravelToMapCoord))]
    public static class NMapScreenTravelToMapCoordPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || RunTweaksSaveManager.GetEffectivePreRunTweaks().FreeMapNavigation)
            {
                GameHelper.EnsureCustomRunMode();
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen __instance)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || RunTweaksSaveManager.GetEffectivePreRunTweaks().FreeMapNavigation)
            {
                __instance.IsTraveling = false;
                __instance.RefreshAllPointVisuals();
            }
        }
    }

    /// <summary>
    /// Ensures map points remain travelable and IsTraveling is reset when opening the map screen.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Open))]
    public static class NMapScreenOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen __instance)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || RunTweaksSaveManager.GetEffectivePreRunTweaks().FreeMapNavigation)
            {
                __instance.IsTraveling = false;
                __instance.RefreshAllPointVisuals();
            }
        }
    }

    private static float _currentExtraOffsetY = 0f;

    /// <summary>
    /// Gets the current extra vertical offset for the parchment background (NMapBg).
    /// </summary>
    public static float GetCurrentExtraOffsetY() => _currentExtraOffsetY;

    /// <summary>
    /// Computes the dynamic maximum top scroll position based on the act's total height and boss position.
    /// </summary>
    public static float GetMaxScrollTop(NMapScreen screen)
    {
        var map = AccessTools.Field(typeof(NMapScreen), "_map")?.GetValue(screen) as ActMap;
        if (map == null) return 1800f;

        int rowCount = map.GetRowCount();
        float num = (map.SecondBossMapPoint != null) ? 0.9f : 1f;
        float distY = (float)(AccessTools.Field(typeof(NMapScreen), "_distY")?.GetValue(screen) ?? (166.0714f * num));
        float totalHeight = (rowCount - 1) * distY;
        float bossY = (740f - totalHeight - 395f) * num;
        if (map.SecondBossMapPoint != null)
        {
            bossY -= 300f * num;
        }

        // Camera top limit is reached when boss is clearly visible at top of viewport
        float maxScroll = -bossY - 180f;
        return Math.Max(-600f, maxScroll);
    }

    /// <summary>
    /// Adjusts the parchment background (NMapBg) height and Y position to fit the act seamlessly.
    /// </summary>
    private static void AdjustMapBackground(NMapBg? mapBg, float totalHeight, float bossY)
    {
        try
        {
            if (mapBg == null) return;

            // Clean up any previously added dynamic extra middle segments
            var existingExtras = mapBg.GetChildren().OfType<TextureRect>().Where(c => c.Name.ToString().StartsWith("MapMid_Extra_")).ToList();
            foreach (var extra in existingExtras)
            {
                mapBg.RemoveChild(extra);
                extra.QueueFree();
            }

            // Vanilla parchment covers boss at -1980 with bg top at -1620 (height = 3240px)
            // If map is longer than 15 floors, add identical 1080px middle parchment tiles
            float extraHeight = Math.Max(0f, (-bossY - 360f) - 1620f);
            int extraSections = (int)Math.Ceiling(extraHeight / 1080f);
            _currentExtraOffsetY = extraSections * 1080f;

            var mapTop = AccessTools.Field(typeof(NMapBg), "_mapTop")?.GetValue(mapBg) as TextureRect;
            var mapMid = AccessTools.Field(typeof(NMapBg), "_mapMid")?.GetValue(mapBg) as TextureRect;
            var mapBot = AccessTools.Field(typeof(NMapBg), "_mapBot")?.GetValue(mapBg) as TextureRect;
            var runState = AccessTools.Field(typeof(NMapBg), "_runState")?.GetValue(mapBg) as RunState;

            // Ensure vanilla MapTop, MapMid, and MapBot maintain exact standard dimensions
            if (mapTop != null)
            {
                mapTop.CustomMinimumSize = new Vector2(0f, 1080f);
                mapTop.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                mapTop.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                mapTop.UseParentMaterial = true;
            }
            if (mapMid != null)
            {
                mapMid.CustomMinimumSize = new Vector2(0f, 1080f);
                mapMid.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                mapMid.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                mapMid.UseParentMaterial = true;
            }
            if (mapBot != null)
            {
                mapBot.CustomMinimumSize = new Vector2(0f, 1080f);
                mapBot.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                mapBot.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                mapBot.UseParentMaterial = true;
            }

            if (mapMid != null && extraSections > 0)
            {
                for (int i = 0; i < extraSections; i++)
                {
                    var extraMid = new TextureRect
                    {
                        Name = $"MapMid_Extra_{i}",
                        Texture = runState?.Act?.MapMidBg ?? mapMid.Texture,
                        CustomMinimumSize = new Vector2(0f, 1080f),
                        LayoutMode = 2,
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                        UseParentMaterial = true,
                        MouseFilter = Control.MouseFilterEnum.Ignore,
                        TextureFilter = mapMid.TextureFilter,
                        TextureRepeat = mapMid.TextureRepeat,
                        SizeFlagsHorizontal = mapMid.SizeFlagsHorizontal,
                        SizeFlagsVertical = mapMid.SizeFlagsVertical
                    };
                    mapBg.AddChild(extraMid);
                    mapBg.MoveChild(extraMid, 1 + i); // Place between MapTop (0) and MapBot
                }
            }

            float bgTop = -1620f - _currentExtraOffsetY;
            float adjustY = -1540f - _currentExtraOffsetY;

            var window = AccessTools.Field(typeof(NMapBg), "_window")?.GetValue(mapBg) as Window;
            float offsetX = (float)(AccessTools.Field(typeof(NMapBg), "_offsetX")?.GetValue(mapBg) ?? mapBg.Position.X);

            if (window != null)
            {
                float num = Math.Max(1.3333334f, (float)window.Size.X / (float)window.Size.Y);
                if (num < 1.7777778f)
                {
                    float p = (num - 1.3333334f) / 0.44444442f;
                    mapBg.Position = new Vector2(offsetX, Mathf.Remap(Ease.CubicOut(p), 0f, 1f, adjustY, bgTop));
                }
                else
                {
                    mapBg.Position = new Vector2(offsetX, bgTop);
                }
            }
            else
            {
                mapBg.Position = new Vector2(offsetX, bgTop);
            }

            var drawings = AccessTools.Field(typeof(NMapBg), "_drawings")?.GetValue(mapBg) as NMapDrawings;
            drawings?.RepositionBasedOnBackground(mapBg);
        }
        catch (Exception ex)
        {
            ModLogger.Error("Error adjusting map background parchment", ex);
        }
    }

    /// <summary>
    /// Ensures map parchment position remains accurate across window/resolution changes.
    /// </summary>
    [HarmonyPatch(typeof(NMapBg), "OnWindowChange")]
    public static class NMapBgOnWindowChangePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(NMapBg __instance)
        {
            try
            {
                var window = AccessTools.Field(typeof(NMapBg), "_window")?.GetValue(__instance) as Window;
                float offsetX = (float)(AccessTools.Field(typeof(NMapBg), "_offsetX")?.GetValue(__instance) ?? __instance.Position.X);
                float extraOffsetY = GetCurrentExtraOffsetY();
                float bgTop = -1620f - extraOffsetY;
                float adjustY = -1540f - extraOffsetY;

                if (window != null)
                {
                    float num = Math.Max(1.3333334f, (float)window.Size.X / (float)window.Size.Y);
                    if (num < 1.7777778f)
                    {
                        float p = (num - 1.3333334f) / 0.44444442f;
                        __instance.Position = new Vector2(offsetX, Mathf.Remap(Ease.CubicOut(p), 0f, 1f, adjustY, bgTop));
                    }
                    else
                    {
                        __instance.Position = new Vector2(offsetX, bgTop);
                    }
                }
                else
                {
                    __instance.Position = new Vector2(offsetX, bgTop);
                }

                var drawings = AccessTools.Field(typeof(NMapBg), "_drawings")?.GetValue(__instance) as NMapDrawings;
                drawings?.RepositionBasedOnBackground(__instance);
                return false; // Skip original method
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in NMapBg.OnWindowChange prefix", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Sets up map screen with proportional, natural floor distances and dynamic parchment scroll height
    /// for any custom floor count selected in AIOTweaks.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
    public static class NMapScreenSetMapPatch
    {
        private static readonly FieldInfo MapField = AccessTools.Field(typeof(NMapScreen), "_map");
        private static readonly FieldInfo PointsField = AccessTools.Field(typeof(NMapScreen), "_points");
        private static readonly FieldInfo BossPointNodeField = AccessTools.Field(typeof(NMapScreen), "_bossPointNode");
        private static readonly FieldInfo SecondBossPointNodeField = AccessTools.Field(typeof(NMapScreen), "_secondBossPointNode");
        private static readonly FieldInfo StartingPointNodeField = AccessTools.Field(typeof(NMapScreen), "_startingPointNode");
        private static readonly FieldInfo MapBgContainerField = AccessTools.Field(typeof(NMapScreen), "_mapBgContainer");
        private static readonly FieldInfo MarkerField = AccessTools.Field(typeof(NMapScreen), "_marker");
        private static readonly FieldInfo MapPointDictField = AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary");
        private static readonly FieldInfo PathsField = AccessTools.Field(typeof(NMapScreen), "_paths");
        private static readonly FieldInfo RunStateField = AccessTools.Field(typeof(NMapScreen), "_runState");
        private static readonly FieldInfo HasPlayedAnimationField = AccessTools.Field(typeof(NMapScreen), "_hasPlayedAnimation");
        private static readonly FieldInfo DistXField = AccessTools.Field(typeof(NMapScreen), "_distX");
        private static readonly FieldInfo DistYField = AccessTools.Field(typeof(NMapScreen), "_distY");
        private static readonly FieldInfo TickTraveledScaleField = AccessTools.Field(typeof(NMapScreen), "_tickTraveledScale");

        private static readonly MethodInfo RemoveAllMapPointsAndPathsMethod = AccessTools.Method(typeof(NMapScreen), "RemoveAllMapPointsAndPaths");
        private static readonly MethodInfo DrawPathsMethod = AccessTools.Method(typeof(NMapScreen), "DrawPaths");
        private static readonly MethodInfo InitMapVotesMethod = AccessTools.Method(typeof(NMapScreen), "InitMapVotes");
        private static readonly MethodInfo RefreshAllMapPointVotesMethod = AccessTools.Method(typeof(NMapScreen), "RefreshAllMapPointVotes");
        private static readonly MethodInfo RecalculateTravelabilityMethod = AccessTools.Method(typeof(NMapScreen), "RecalculateTravelability");
        private static readonly MethodInfo RefreshAllPointVisualsMethod = AccessTools.Method(typeof(NMapScreen), "RefreshAllPointVisuals");

        [HarmonyPrefix]
        public static bool Prefix(NMapScreen __instance, ActMap map, ulong seed, bool clearDrawings)
        {
            try
            {
                MapField.SetValue(__instance, map);
                var mapPointDict = (Dictionary<MapCoord, NMapPoint>)MapPointDictField.GetValue(__instance)!;
                var paths = (Dictionary<(MapCoord, MapCoord), IReadOnlyList<TextureRect>>)PathsField.GetValue(__instance)!;
                var points = (Control)PointsField.GetValue(__instance)!;
                var marker = (NMapMarker)MarkerField.GetValue(__instance)!;
                var runState = (RunState)RunStateField.GetValue(__instance)!;
                var mapBgContainer = MapBgContainerField.GetValue(__instance) as NMapBg;

                mapPointDict.Clear();
                paths.Clear();
                RemoveAllMapPointsAndPathsMethod.Invoke(__instance, null);
                marker.ResetMapPoint();

                if (clearDrawings)
                {
                    __instance.Drawings.ClearAllLines();
                }
                HasPlayedAnimationField.SetValue(__instance, false);

                int rowCount = map.GetRowCount();
                int columnCount = map.GetColumnCount();
                float num = (map.SecondBossMapPoint != null) ? 0.9f : 1f;

                // Standard vanilla spacing: 2325 / 14 = 166.0714f.
                // Maintain standard natural floor distance for any row count!
                const float StandardFloorSpacing = 166.0714f;
                float distY = StandardFloorSpacing * num;
                float distX = 1050f / (float)columnCount;
                float totalHeight = (rowCount - 1) * distY;

                DistXField.SetValue(__instance, distX);
                DistYField.SetValue(__instance, distY);

                Rng rng = new Rng(seed, $"map_jitter_{runState.CurrentActIndex}");
                Vector2 vector = new Vector2(-500f, 740f);
                Vector2 vector2 = new Vector2(distX, 0f - distY);

                foreach (MapPoint allMapPoint in map.GetAllMapPoints())
                {
                    NNormalMapPoint nNormalMapPoint = NNormalMapPoint.Create(allMapPoint, __instance, runState);
                    nNormalMapPoint.Position = new Vector2(allMapPoint.coord.col, allMapPoint.coord.row) * vector2 + vector;
                    float x = rng.NextFloat(-21f, 21f);
                    float y = rng.NextFloat(-25f, 25f);
                    nNormalMapPoint.Position += new Vector2(x, y);
                    mapPointDict.Add(allMapPoint.coord, nNormalMapPoint);
                    points.AddChildSafely(nNormalMapPoint);
                    nNormalMapPoint.SetAngle(Rng.Chaotic.NextGaussianFloat(0f, 8f));
                }

                float bossY = (740f - totalHeight - 395f) * num;
                var bossPointNode = NBossMapPoint.Create(map.BossMapPoint, __instance, runState);
                bossPointNode.Position = new Vector2(-200f, bossY);
                points.AddChildSafely(bossPointNode);
                mapPointDict[map.BossMapPoint.coord] = bossPointNode;
                BossPointNodeField.SetValue(__instance, bossPointNode);

                NBossMapPoint? secondBossPointNode = null;
                if (map.SecondBossMapPoint != null)
                {
                    bossPointNode.Scale = new Vector2(0.75f, 0.75f);
                    secondBossPointNode = NBossMapPoint.Create(map.SecondBossMapPoint, __instance, runState);
                    secondBossPointNode.Position = new Vector2(-200f, bossY - 300f * num);
                    secondBossPointNode.Scale = new Vector2(0.75f, 0.75f);
                    points.AddChildSafely(secondBossPointNode);
                    mapPointDict[map.SecondBossMapPoint.coord] = secondBossPointNode;
                    SecondBossPointNodeField.SetValue(__instance, secondBossPointNode);
                }
                else
                {
                    SecondBossPointNodeField.SetValue(__instance, null);
                }

                NMapPoint startingPointNode;
                if (map.StartingMapPoint.PointType == MapPointType.Ancient)
                {
                    startingPointNode = NAncientMapPoint.Create(map.StartingMapPoint, __instance, runState);
                    startingPointNode.Position = new Vector2(-80f, (float)map.StartingMapPoint.coord.row * (0f - distY) + 720f);
                }
                else
                {
                    startingPointNode = NNormalMapPoint.Create(map.StartingMapPoint, __instance, runState);
                    startingPointNode.Position = new Vector2(-80f, (float)map.StartingMapPoint.coord.row * (0f - distY) + 800f);
                }
                points.AddChildSafely(startingPointNode);
                mapPointDict[map.StartingMapPoint.coord] = startingPointNode;
                StartingPointNodeField.SetValue(__instance, startingPointNode);

                // Adjust parchment background to match the boss position and proportional map height
                AdjustMapBackground(mapBgContainer, totalHeight, bossY);

                foreach (MapPoint allMapPoint2 in map.GetAllMapPoints())
                {
                    DrawPathsMethod.Invoke(__instance, new object[] { mapPointDict[allMapPoint2.coord], allMapPoint2 });
                }
                DrawPathsMethod.Invoke(__instance, new object[] { startingPointNode, map.StartingMapPoint });
                DrawPathsMethod.Invoke(__instance, new object[] { bossPointNode, map.BossMapPoint });

                IReadOnlyList<MapCoord> visitedMapCoords = runState.VisitedMapCoords;
                Vector2 tickTraveledScale = (Vector2)(TickTraveledScaleField.GetValue(null) ?? new Vector2(1.2f, 1.2f));
                for (int i = 0; i < visitedMapCoords.Count - 1; i++)
                {
                    if (!paths.TryGetValue((visitedMapCoords[i], visitedMapCoords[i + 1]), out IReadOnlyList<TextureRect>? value) || value == null)
                    {
                        continue;
                    }
                    foreach (TextureRect item in value)
                    {
                        item.Modulate = runState.Act.MapTraveledColor;
                        item.Scale = tickTraveledScale;
                    }
                }

                InitMapVotesMethod.Invoke(__instance, null);
                RefreshAllMapPointVotesMethod.Invoke(__instance, null);

                for (int j = 0; j < map.GetRowCount(); j++)
                {
                    IEnumerable<MapPoint> pointsInRow = map.GetPointsInRow(j);
                    List<NMapPoint> list = pointsInRow.Select(p => mapPointDict[p.coord]).ToList();
                    for (int k = 0; k < list.Count; k++)
                    {
                        list[k].FocusNeighborLeft = ((k > 0) ? list[k - 1].GetPath() : list[k].GetPath());
                        list[k].FocusNeighborRight = ((k < list.Count - 1) ? list[k + 1].GetPath() : list[k].GetPath());
                        list[k].FocusNeighborTop = list[k].GetPath();
                        list[k].FocusNeighborBottom = list[k].GetPath();
                    }
                }

                startingPointNode.FocusNeighborLeft = startingPointNode.GetPath();
                startingPointNode.FocusNeighborRight = startingPointNode.GetPath();
                startingPointNode.FocusNeighborTop = startingPointNode.GetPath();
                startingPointNode.FocusNeighborBottom = startingPointNode.GetPath();
                bossPointNode.FocusNeighborLeft = bossPointNode.GetPath();
                bossPointNode.FocusNeighborRight = bossPointNode.GetPath();
                bossPointNode.FocusNeighborBottom = bossPointNode.GetPath();

                if (secondBossPointNode != null)
                {
                    bossPointNode.FocusNeighborTop = secondBossPointNode.GetPath();
                    secondBossPointNode.FocusNeighborBottom = bossPointNode.GetPath();
                    secondBossPointNode.FocusNeighborLeft = secondBossPointNode.GetPath();
                    secondBossPointNode.FocusNeighborRight = secondBossPointNode.GetPath();
                    secondBossPointNode.FocusNeighborTop = secondBossPointNode.GetPath();
                }
                else
                {
                    bossPointNode.FocusNeighborTop = bossPointNode.GetPath();
                }

                if (__instance.IsVisible())
                {
                    RecalculateTravelabilityMethod.Invoke(__instance, null);
                    RefreshAllPointVisualsMethod.Invoke(__instance, null);
                }

                if (RuntimeStateManager.FreeMapNavigationEnabled || RunTweaksSaveManager.GetEffectivePreRunTweaks().FreeMapNavigation)
                {
                    __instance.IsTraveling = false;
                    __instance.RefreshAllPointVisuals();
                }

                ModLogger.Verbose("MapGenerationHooks", $"SetMap completed: rowCount={rowCount}, distY={distY:F1}, totalHeight={totalHeight:F1}, maxScrollTop={GetMaxScrollTop(__instance):F1}");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in proportional NMapScreen.SetMap patch, falling back to original", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Dynamically scales scroll bounce bounds to support scrolling across extended map lengths.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), "UpdateScrollPosition")]
    public static class NMapScreenUpdateScrollPositionPatch
    {
        private static readonly FieldInfo MapContainerField = AccessTools.Field(typeof(NMapScreen), "_mapContainer");
        private static readonly FieldInfo TargetDragPosField = AccessTools.Field(typeof(NMapScreen), "_targetDragPos");
        private static readonly FieldInfo IsDraggingField = AccessTools.Field(typeof(NMapScreen), "_isDragging");

        [HarmonyPrefix]
        public static bool Prefix(NMapScreen __instance, double delta)
        {
            try
            {
                var mapContainer = (Control)MapContainerField.GetValue(__instance)!;
                var targetDragPos = (Vector2)TargetDragPosField.GetValue(__instance)!;
                bool isDragging = (bool)IsDraggingField.GetValue(__instance)!;

                float maxScrollTop = GetMaxScrollTop(__instance);

                if (mapContainer.Position != targetDragPos)
                {
                    float a = Mathf.Sign(mapContainer.Position.Y - targetDragPos.Y);
                    mapContainer.Position = mapContainer.Position.Lerp(targetDragPos, (float)delta * 15f);
                    float b = Mathf.Sign(mapContainer.Position.Y - targetDragPos.Y);
                    if (Math.Abs(mapContainer.Position.Y - targetDragPos.Y) < 0.5f || !Mathf.IsEqualApprox(a, b))
                    {
                        mapContainer.Position = targetDragPos;
                    }
                }

                if (!isDragging)
                {
                    if (targetDragPos.Y < -600f)
                    {
                        targetDragPos = targetDragPos.Lerp(new Vector2(0f, -600f), (float)delta * 12f);
                        TargetDragPosField.SetValue(__instance, targetDragPos);
                    }
                    else if (targetDragPos.Y > maxScrollTop)
                    {
                        targetDragPos = targetDragPos.Lerp(new Vector2(0f, maxScrollTop), (float)delta * 12f);
                        TargetDragPosField.SetValue(__instance, targetDragPos);
                    }
                }

                NGame.Instance?.RemoteCursorContainer?.ForceUpdateAllCursors();
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in NMapScreen.UpdateScrollPosition patch", ex);
                return true;
            }
        }
    }

    /// <summary>
    /// Starts the opening Act animation from the dynamic boss height and smoothly sweeps down to floor 0.
    /// </summary>
    [HarmonyPatch(typeof(NMapScreen), "PlayStartOfActAnimation")]
    public static class NMapScreenPlayStartOfActAnimationPatch
    {
        private static readonly FieldInfo HasPlayedAnimationField = AccessTools.Field(typeof(NMapScreen), "_hasPlayedAnimation");
        private static readonly FieldInfo RunStateField = AccessTools.Field(typeof(NMapScreen), "_runState");
        private static readonly FieldInfo MapContainerField = AccessTools.Field(typeof(NMapScreen), "_mapContainer");
        private static readonly FieldInfo TargetDragPosField = AccessTools.Field(typeof(NMapScreen), "_targetDragPos");
        private static readonly FieldInfo ActAnimTweenField = AccessTools.Field(typeof(NMapScreen), "_actAnimTween");
        private static readonly FieldInfo MapAnimStartDelayField = AccessTools.Field(typeof(NMapScreen), "_mapAnimStartDelay");
        private static readonly FieldInfo MapAnimDurationField = AccessTools.Field(typeof(NMapScreen), "_mapAnimDuration");

        private static readonly MethodInfo SetInterruptableMethod = AccessTools.Method(typeof(NMapScreen), "SetInterruptable");
        private static readonly MethodInfo InitMapPromptMethod = AccessTools.Method(typeof(NMapScreen), "InitMapPrompt");

        [HarmonyPrefix]
        public static bool Prefix(NMapScreen __instance)
        {
            try
            {
                bool hasPlayed = (bool)(HasPlayedAnimationField.GetValue(__instance) ?? false);
                if (hasPlayed)
                {
                    ModLogger.Warn("Tried to play start of act animation twice! Ignoring second try");
                    return false;
                }

                HasPlayedAnimationField.SetValue(__instance, true);
                var runState = RunStateField.GetValue(__instance) as RunState;
                if (runState != null)
                {
                    NActBanner? child = NActBanner.Create(runState.Act, runState.CurrentActIndex);
                    if (child != null)
                    {
                        NRun.Instance?.GlobalUi.MapScreen.AddChildSafely(child);
                    }
                }

                TaskHelper.RunSafely(CustomStartOfActAnim(__instance));
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in PlayStartOfActAnimation prefix", ex);
                return true;
            }
        }

        private static async Task CustomStartOfActAnim(NMapScreen instance)
        {
            var mapContainer = (Control)MapContainerField.GetValue(instance)!;
            float maxScrollTop = GetMaxScrollTop(instance);
            mapContainer.Position = new Vector2(0f, maxScrollTop);

            var existingTween = ActAnimTweenField.GetValue(instance) as Tween;
            existingTween?.Kill();

            var tween = instance.CreateTween().SetParallel();
            ActAnimTweenField.SetValue(instance, tween);

            double startDelay = (double)MapAnimStartDelayField.GetValue(instance)!;
            double duration = (double)MapAnimDurationField.GetValue(instance)!;

            // Scale duration proportionally for long maps so the camera pan remains smooth
            if (maxScrollTop > 1800f)
            {
                duration *= Math.Min(2.2, 1.0 + (maxScrollTop - 1800f) / 3200f);
            }

            tween.TweenInterval(startDelay);
            tween.Chain();
            Vector2 targetDragPos = new Vector2(0f, -600f);
            tween.TweenProperty(mapContainer, "position:y", -600f, duration).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Expo);
            tween.TweenCallback(Callable.From(() => SetInterruptableMethod.Invoke(instance, null))).SetDelay(duration * 0.25);
            TargetDragPosField.SetValue(instance, targetDragPos);

            if (await tween.AwaitFinished(instance))
            {
                ActAnimTweenField.SetValue(instance, null);
                InitMapPromptMethod.Invoke(instance, null);
            }
        }
    }

    #region Neow & Run Initialization Patches

    [HarmonyPatch(typeof(RunManager), "SetStartedWithNeowFlag")]
    public static class SetStartedWithNeowFlagPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RunManager __instance)
        {
            try
            {
                var state = GetRunState(__instance);
                if (state?.ExtraFields != null)
                {
                    bool forceNeow = RunTweaksSaveManager.GetEffectivePreRunTweaks().ForceNeowBonus;
                    state.ExtraFields.StartedWithNeow = forceNeow;
                    ModLogger.Info($"MapGenerationHooks: SetStartedWithNeowFlag override applied -> StartedWithNeow = {forceNeow}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in SetStartedWithNeowFlagPatch", ex);
            }
        }
    }

    [HarmonyPatch(typeof(RunManager), "InitializeNewRun")]
    public static class InitializeNewRunPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RunManager __instance)
        {
            try
            {
                var state = GetRunState(__instance);
                if (state?.ExtraFields != null)
                {
                    bool forceNeow = RunTweaksSaveManager.GetEffectivePreRunTweaks().ForceNeowBonus;
                    state.ExtraFields.StartedWithNeow = forceNeow;
                    ModLogger.Verbose("MapGenerationHooks", $"InitializeNewRun -> StartedWithNeow ensured to {forceNeow}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in InitializeNewRunPatch", ex);
            }
        }
    }

    /// <summary>
    /// Guarantees that Overgrowth (Act 1) always provides Neow as an unlocked Ancient
    /// whenever ForceNeowBonus is enabled or when the profile has not yet unlocked NeowEpoch.
    /// </summary>
    [HarmonyPatch(typeof(Overgrowth), nameof(Overgrowth.GetUnlockedAncients))]
    public static class OvergrowthGetUnlockedAncientsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Overgrowth __instance, UnlockState unlockState, ref IEnumerable<AncientEventModel> __result)
        {
            try
            {
                if (RunTweaksSaveManager.GetEffectivePreRunTweaks().ForceNeowBonus || !__result.Any())
                {
                    __result = new AncientEventModel[] { ModelDb.AncientEvent<Neow>() };
                    ModLogger.Verbose("MapGenerationHooks", "Overgrowth.GetUnlockedAncients: Injected Neow into unlocked ancients list.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in OvergrowthGetUnlockedAncientsPatch", ex);
            }
        }
    }

    /// <summary>
    /// Ensures that after ActModel.GenerateRooms finishes, the act's RoomSet has a valid Ancient assigned
    /// (especially for Act 1 / Overgrowth when NeowEpoch is unrevealed on fresh profiles).
    /// </summary>
    [HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
    public static class ActModelGenerateRoomsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ActModel __instance, Rng rng, UnlockState unlockState, bool isMultiplayer)
        {
            try
            {
                if (ActModelRoomsField?.GetValue(__instance) is RoomSet rooms)
                {
                    if (!rooms.HasAncient)
                    {
                        AncientEventModel? fallbackAncient = null;
                        if (__instance.AllAncients != null && __instance.AllAncients.Any())
                        {
                            fallbackAncient = rng.NextItem(__instance.AllAncients) ?? __instance.AllAncients.FirstOrDefault();
                        }
                        if (fallbackAncient == null)
                        {
                            fallbackAncient = ModelDb.AncientEvent<Neow>();
                        }

                        rooms.Ancient = fallbackAncient;
                        ModLogger.Info($"ActModel.GenerateRooms: Assigned fallback Ancient '{fallbackAncient?.Id.Entry}' to act '{__instance.Id.Entry}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in ActModelGenerateRoomsPatch", ex);
            }
        }
    }

    /// <summary>
    /// Prevents 'RoomSet.Ancient not set! You must call GenerateRooms' InvalidOperationException
    /// by returning a valid fallback Ancient (Neow) if accessed before generation or on unassigned sets.
    /// </summary>
    [HarmonyPatch(typeof(RoomSet), "get_Ancient")]
    public static class RoomSetGetAncientPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RoomSet __instance, ref AncientEventModel __result)
        {
            try
            {
                var ancient = RoomSetAncientField?.GetValue(__instance) as AncientEventModel;
                if (ancient != null)
                {
                    __result = ancient;
                    return false; // Skip original getter to avoid null check throw
                }

                var fallback = ModelDb.AncientEvent<Neow>();
                RoomSetAncientField?.SetValue(__instance, fallback);
                __result = fallback;
                ModLogger.Warn("RoomSet.get_Ancient: _ancient was null on access; safely supplied Neow fallback.");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RoomSetGetAncientPatch", ex);
                return true; // Let original run if reflection fails
            }
        }
    }

    /// <summary>
    /// Prevents 'RoomSet.Boss not set! You must call GenerateRooms' InvalidOperationException
    /// by providing a safe fallback boss if accessed prior to generation.
    /// </summary>
    [HarmonyPatch(typeof(RoomSet), "get_Boss")]
    public static class RoomSetGetBossPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RoomSet __instance, ref EncounterModel __result)
        {
            try
            {
                var boss = RoomSetBossField?.GetValue(__instance) as EncounterModel;
                if (boss != null)
                {
                    __result = boss;
                    return false; // Skip original getter
                }

                var fallbackBoss = ModelDb.Encounter<VantomBoss>();
                RoomSetBossField?.SetValue(__instance, fallbackBoss);
                __result = fallbackBoss;
                ModLogger.Warn("RoomSet.get_Boss: _boss was null on access; safely supplied boss fallback.");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in RoomSetGetBossPatch", ex);
                return true; // Let original run if reflection fails
            }
        }
    }

    #endregion
}

