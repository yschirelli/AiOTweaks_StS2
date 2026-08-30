using System;
using AIOTweaks.Core.Logging;
using AIOTweaks.Cheats;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts event room resolution to allow forcing specific narrative events.
/// </summary>
public static class EventHooks
{

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEvent))]
    public static class ActModelPullNextEventPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref EventModel __result, RunState runState)
        {
            try
            {
                ModLogger.Verbose("EventHooks", $"ActModel.PullNextEvent intercepted. Rolled event: '{__result?.Id}'");
                if (EventDirector.TryConsumeForcedEvent(out var forcedModel) && forcedModel != null)
                {
                    ModLogger.Info($"EventHook: Overriding rolled event '{__result?.Id}' with forced event '{forcedModel.Id}'");
                    __result = forcedModel;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"ActModel.PullNextEvent patch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ActModel), nameof(ActModel.PullAncient))]
    public static class ActModelPullAncientPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref EventModel __result)
        {
            try
            {
                ModLogger.Verbose("EventHooks", $"ActModel.PullAncient intercepted. Rolled ancient: '{__result?.Id}'");
                if (EventDirector.TryConsumeForcedEvent(out var forcedModel) && forcedModel != null)
                {
                    ModLogger.Info($"EventHook: Overriding rolled ancient '{__result?.Id}' with forced event '{forcedModel.Id}'");
                    __result = forcedModel;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"ActModel.PullAncient patch error: {ex.Message}");
            }
        }
    }
}
