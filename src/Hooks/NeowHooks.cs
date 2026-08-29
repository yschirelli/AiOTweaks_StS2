using System;
using System.Diagnostics;
using HarmonyLib;
using AIOTweaks.Core.Config;
using MegaCrit.Sts2.Core.Runs;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Hooks;

/// <summary>
/// Ensures Neow still spawns when GameMode is set to Custom (due to Tweaks).
/// </summary>
[HarmonyPatch(typeof(RunState), "get_GameMode")]
public static class NeowHooks
{
    [HarmonyPostfix]
    public static void Postfix(ref GameMode __result)
    {
        try
        {
            if (ConfigManager.Current.PreRunTweaks.ForceNeowBonus && __result == GameMode.Custom)
            {
                // Neow generation logic usually checks for GameMode.Standard
                var trace = new StackTrace();
                foreach (var frame in trace.GetFrames())
                {
                    var method = frame.GetMethod();
                    if (method != null && method.Name.Contains("Neow"))
                    {
                        ModLogger.Verbose("NeowHooks", $"ForceNeowBonus active: changing GameMode.Custom -> GameMode.Standard for Neow check in {method.Name}");
                        __result = GameMode.Standard;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"Error in NeowHooks: {ex.Message}");
        }
    }
}
