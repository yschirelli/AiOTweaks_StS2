using System;
using System.Collections.Generic;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using HarmonyLib;

namespace AIOTweaks.Hooks;

/// <summary>
/// Interception points for relic rewards, drop table overrides, and dynamic relic injection.
/// </summary>
public static class RelicHooks
{
    public static void ApplyPatches(Harmony harmony)
    {
        ModLogger.Verbose("RelicHooks", "Applying RelicHooks Harmony patches...");
        try
        {
            harmony.CreateClassProcessor(typeof(RelicHooks)).Patch();
            ModLogger.Info("RelicHooks successfully initialized.");
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"RelicHooks partial patch notice: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for any pending relic rewards queued via the debug console or directors and injects them.
    /// </summary>
    public static void ProcessPendingRelicRewards(List<string> rewardList)
    {
        var pending = RuntimeStateManager.ConsumePendingRewardRelics();
        ModLogger.Verbose("RelicHooks", $"ProcessPendingRelicRewards: {pending.Count} pending relics in queue.");
        if (pending.Count > 0)
        {
            foreach (var relicId in pending)
            {
                if (!rewardList.Contains(relicId))
                {
                    rewardList.Add(relicId);
                    ModLogger.Info($"Injected queued relic '{relicId}' into rewards.");
                }
            }
        }
    }
}
