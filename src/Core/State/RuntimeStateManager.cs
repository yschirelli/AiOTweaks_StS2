using System;
using System.Collections.Generic;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Core.State;

/// <summary>
/// Tracks in-memory runtime session state, active director overrides, and ensures non-destructive lifecycle reset.
/// </summary>
public static class RuntimeStateManager
{
    private static readonly object LockObject = new();

    // Cheat toggles
    public static bool GodModeEnabled { get; set; } = false;
    public static bool InfiniteEnergyEnabled { get; set; } = false;
    public static bool OneHitKillEnabled { get; set; } = false;
    public static bool InfinitePotionsEnabled { get; set; } = false;
    public static bool NoCardExhaustEnabled { get; set; } = false;
    public static bool FreeMapNavigationEnabled { get; set; } = false;

    // Endless mode state
    public static int CurrentEndlessLoopCount { get; set; } = 0;

    // Director transient overrides
    public static string? ForcedNextEventId { get; set; } = null;
    public static int? ForcedGoldDropAmount { get; set; } = null;
    public static int? OverrideBonusEnergy { get; set; } = null;
    public static int? OverrideCardDrawCount { get; set; } = null;

    // Custom runtime modifiers and active cheats log
    private static readonly HashSet<string> ActiveCheatFlags = new();
    private static readonly List<string> PendingRewardRelics = new();
    private static readonly List<string> PendingRewardCards = new();

    public static event Action? OnStateReset;
    public static event Action<string, bool>? OnCheatToggled;

    public static void SetCheatFlag(string flagName, bool active)
    {
        lock (LockObject)
        {
            if (active)
            {
                ActiveCheatFlags.Add(flagName);
            }
            else
            {
                ActiveCheatFlags.Remove(flagName);
            }
        }

        ModLogger.Verbose("RuntimeStateManager", $"SetCheatFlag: '{flagName}' -> {active} (Total active cheat flags: {ActiveCheatFlags.Count})");
        ModLogger.Info($"Cheat flag '{flagName}' is now {(active ? "ENABLED" : "DISABLED")}");
        OnCheatToggled?.Invoke(flagName, active);
    }

    public static bool IsCheatActive(string flagName)
    {
        lock (LockObject)
        {
            bool active = ActiveCheatFlags.Contains(flagName);
            ModLogger.Verbose("RuntimeStateManager", $"IsCheatActive('{flagName}'): {active}");
            return active;
        }
    }

    public static IReadOnlyCollection<string> GetActiveCheatFlags()
    {
        lock (LockObject)
        {
            ModLogger.Verbose("RuntimeStateManager", $"GetActiveCheatFlags: returning {ActiveCheatFlags.Count} flags.");
            return new List<string>(ActiveCheatFlags);
        }
    }

    public static float GetEffectiveEnemyHealthMultiplier()
    {
        float baseHp = ConfigManager.Current.PreRunTweaks.EnemyHealthMultiplier;
        var endless = ConfigManager.Current.PreRunTweaks.EndlessMode;
        if (endless.Enabled && CurrentEndlessLoopCount > 0)
        {
            baseHp *= (float)Math.Pow(endless.EnemyScalingMultiplier, CurrentEndlessLoopCount);
        }
        return Math.Max(0.01f, baseHp);
    }

    public static float GetEffectiveEnemyDamageMultiplier()
    {
        float baseDmg = ConfigManager.Current.PreRunTweaks.EnemyDamageMultiplier;
        var endless = ConfigManager.Current.PreRunTweaks.EndlessMode;
        if (endless.Enabled && CurrentEndlessLoopCount > 0)
        {
            baseDmg *= (float)Math.Pow(endless.EnemyScalingMultiplier, CurrentEndlessLoopCount);
        }
        return Math.Max(0.0f, baseDmg);
    }

    public static float GetEffectiveEnemyDefendMultiplier()
    {
        float baseDef = ConfigManager.Current.PreRunTweaks.EnemyDefendMultiplier;
        var endless = ConfigManager.Current.PreRunTweaks.EndlessMode;
        if (endless.Enabled && CurrentEndlessLoopCount > 0)
        {
            baseDef *= (float)Math.Pow(endless.EnemyScalingMultiplier, CurrentEndlessLoopCount);
        }
        return Math.Max(0.0f, baseDef);
    }

    public static void QueueRewardRelic(string relicId)
    {
        lock (LockObject)
        {
            PendingRewardRelics.Add(relicId);
            ModLogger.Verbose("RuntimeStateManager", $"QueueRewardRelic: added '{relicId}' (Total pending: {PendingRewardRelics.Count})");
            ModLogger.Info($"Queued reward relic: {relicId}");
        }
    }

    public static List<string> ConsumePendingRewardRelics()
    {
        lock (LockObject)
        {
            var list = new List<string>(PendingRewardRelics);
            PendingRewardRelics.Clear();
            ModLogger.Verbose("RuntimeStateManager", $"ConsumePendingRewardRelics: consumed {list.Count} relics.");
            return list;
        }
    }

    public static void QueueRewardCard(string cardId)
    {
        lock (LockObject)
        {
            PendingRewardCards.Add(cardId);
            ModLogger.Verbose("RuntimeStateManager", $"QueueRewardCard: added '{cardId}' (Total pending: {PendingRewardCards.Count})");
            ModLogger.Info($"Queued reward card: {cardId}");
        }
    }

    public static List<string> ConsumePendingRewardCards()
    {
        lock (LockObject)
        {
            var list = new List<string>(PendingRewardCards);
            PendingRewardCards.Clear();
            ModLogger.Verbose("RuntimeStateManager", $"ConsumePendingRewardCards: consumed {list.Count} cards.");
            return list;
        }
    }

    /// <summary>
    /// Resets all transient in-run cheats and director overrides back to clean defaults when returning to menu or starting run.
    /// </summary>
    public static void ResetSessionState()
    {
        ModLogger.Verbose("RuntimeStateManager", "ResetSessionState: Resetting all active cheats, overrides, and queues...");
        lock (LockObject)
        {
            GodModeEnabled = false;
            InfiniteEnergyEnabled = false;
            OneHitKillEnabled = false;
            InfinitePotionsEnabled = false;
            NoCardExhaustEnabled = false;
            FreeMapNavigationEnabled = false;
            CurrentEndlessLoopCount = 0;

            ForcedNextEventId = null;
            ForcedGoldDropAmount = null;
            OverrideBonusEnergy = null;
            OverrideCardDrawCount = null;

            ActiveCheatFlags.Clear();
            PendingRewardRelics.Clear();
            PendingRewardCards.Clear();
        }

        ModLogger.Info("RuntimeStateManager: Clean session reset executed. All cheats and overrides cleared.");
        OnStateReset?.Invoke();
    }
}
