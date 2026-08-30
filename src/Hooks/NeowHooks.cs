using System;
using System.Reflection;
using HarmonyLib;
using AIOTweaks.Core.Config;
using MegaCrit.Sts2.Core.Runs;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Hooks;

/// <summary>
/// Controls whether Neow spawns at the start of a run based on the ForceNeowBonus pre-run tweak.
/// If true, guarantees Neow spawns (StartedWithNeow = true).
/// If false, skips Neow and enters the map directly (StartedWithNeow = false).
/// </summary>
public static class NeowHooks
{
    private static readonly PropertyInfo? RunManagerStateProp = typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private static RunState? GetRunState(RunManager? runManager)
    {
        if (runManager == null) return null;
        return (RunManagerStateProp?.GetValue(runManager) as RunState) ?? runManager.DebugOnlyGetState();
    }

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
                    bool forceNeow = ConfigManager.Current.PreRunTweaks.ForceNeowBonus;
                    state.ExtraFields.StartedWithNeow = forceNeow;
                    ModLogger.Info($"NeowHooks: SetStartedWithNeowFlag override applied -> StartedWithNeow = {forceNeow}");
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
                    bool forceNeow = ConfigManager.Current.PreRunTweaks.ForceNeowBonus;
                    state.ExtraFields.StartedWithNeow = forceNeow;
                    ModLogger.Verbose("NeowHooks", $"InitializeNewRun -> StartedWithNeow ensured to {forceNeow}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in InitializeNewRunPatch", ex);
            }
        }
    }
}
