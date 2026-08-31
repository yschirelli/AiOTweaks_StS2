using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using AIOTweaks.Core.Config;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Random;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Hooks;

/// <summary>
/// Controls whether Neow spawns at the start of a run based on the ForceNeowBonus pre-run tweak.
/// If true, guarantees Neow spawns (StartedWithNeow = true).
/// If false, skips Neow and enters the map directly (StartedWithNeow = false).
/// Also guarantees that RoomSet.Ancient is always populated so that map rendering and embarkation never fail.
/// </summary>
public static class NeowHooks
{
    private static readonly PropertyInfo? RunManagerStateProp = typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RoomSetAncientField = typeof(RoomSet).GetField("_ancient", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RoomSetBossField = typeof(RoomSet).GetField("_boss", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ActModelRoomsField = typeof(ActModel).GetField("_rooms", BindingFlags.NonPublic | BindingFlags.Instance);

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
                if (ConfigManager.Current.PreRunTweaks.ForceNeowBonus || !__result.Any())
                {
                    __result = new AncientEventModel[] { ModelDb.AncientEvent<Neow>() };
                    ModLogger.Verbose("NeowHooks", "Overgrowth.GetUnlockedAncients: Injected Neow into unlocked ancients list.");
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
}
