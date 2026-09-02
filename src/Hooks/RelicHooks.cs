using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

public static class RelicHooks
{
    private static readonly FieldInfo? DequesField = typeof(RelicGrabBag).GetField("_deques", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RefreshAllowedField = typeof(RelicGrabBag).GetField("_refreshAllowed", BindingFlags.NonPublic | BindingFlags.Instance);

    private static readonly Dictionary<ulong, TopBarBaselinePositions> BaselineMap = new();

    private sealed class TopBarBaselinePositions
    {
        public Vector2 RoomIconPos { get; set; }
        public Vector2 FloorIconPos { get; set; }
        public Vector2 BossIconPos { get; set; }
        public Vector2 TimerPos { get; set; }
        public Vector2 ModifiersPos { get; set; }
        public Vector2 AchievementLockPos { get; set; }
        public Vector2 AscensionIconPos { get; set; }
        public Vector2 AscensionLabelPos { get; set; }
        public bool Initialized { get; set; }
    }

    private static readonly FieldInfo? ModifiersContainerField = typeof(NTopBar).GetField("_modifiersContainer", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? AchievementLockField = typeof(NTopBar).GetField("_achievementLock", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? AscensionIconField = typeof(NTopBar).GetField("_ascensionIcon", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? AscensionLabelField = typeof(NTopBar).GetField("_ascensionLabel", BindingFlags.NonPublic | BindingFlags.Instance);

    [HarmonyPatch(typeof(RelicGrabBag), "Remove", new Type[] { typeof(RelicModel) })]
    public static class RelicGrabBag_Remove_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(RelicGrabBag __instance, RelicModel relic)
        {
            if (ConfigManager.Current.PreRunTweaks.AllowMultipleRelics)
            {
                ModLogger.Verbose("RelicHooks", $"AllowMultipleRelics active: Retaining relic '{relic?.GetType().Name}' in grab bag.");
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(RelicGrabBag), "PullFromFront", new Type[] { typeof(RelicRarity), typeof(Func<RelicModel, bool>), typeof(IRunState) })]
    public static class RelicGrabBag_PullFromFront_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RelicGrabBag __instance, RelicRarity rarity, Func<RelicModel, bool> filter, IRunState runState, RelicModel? __result)
        {
            if (ConfigManager.Current.PreRunTweaks.AllowMultipleRelics && __result != null && DequesField != null)
            {
                try
                {
                    if (DequesField.GetValue(__instance) is Dictionary<RelicRarity, List<RelicModel>> deques &&
                        deques.TryGetValue(rarity, out var list))
                    {
                        if (!list.Contains(__result))
                        {
                            list.Add(__result);
                        }
                    }
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(RelicGrabBag), "PullFromBack", new Type[] { typeof(RelicRarity), typeof(Func<RelicModel, bool>), typeof(IRunState) })]
    public static class RelicGrabBag_PullFromBack_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(RelicGrabBag __instance, RelicRarity rarity, Func<RelicModel, bool> filter, IRunState runState, RelicModel? __result)
        {
            if (ConfigManager.Current.PreRunTweaks.AllowMultipleRelics && __result != null && DequesField != null)
            {
                try
                {
                    if (DequesField.GetValue(__instance) is Dictionary<RelicRarity, List<RelicModel>> deques &&
                        deques.TryGetValue(rarity, out var list))
                    {
                        if (!list.Contains(__result))
                        {
                            list.Add(__result);
                        }
                    }
                }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(RelicGrabBag), "GetAvailableDeque")]
    public static class RelicGrabBag_GetAvailableDeque_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(RelicGrabBag __instance)
        {
            if (ConfigManager.Current.PreRunTweaks.AllowMultipleRelics && RefreshAllowedField != null)
            {
                try
                {
                    RefreshAllowedField.SetValue(__instance, true);
                }
                catch { }
            }
        }
    }

    public static void AdjustTopBarLayout(NTopBar topBar, int maxPotions)
    {
        if (topBar == null || !GodotObject.IsInstanceValid(topBar)) return;

        try
        {
            ulong instanceId = topBar.GetInstanceId();
            if (!BaselineMap.TryGetValue(instanceId, out var baseline))
            {
                baseline = new TopBarBaselinePositions();
                BaselineMap[instanceId] = baseline;
            }

            if (!baseline.Initialized)
            {
                if (topBar.RoomIcon != null) baseline.RoomIconPos = topBar.RoomIcon.Position;
                if (topBar.FloorIcon != null) baseline.FloorIconPos = topBar.FloorIcon.Position;
                if (topBar.BossIcon != null) baseline.BossIconPos = topBar.BossIcon.Position;
                if (topBar.Timer != null) baseline.TimerPos = topBar.Timer.Position;

                if (ModifiersContainerField?.GetValue(topBar) is Control modCtrl) baseline.ModifiersPos = modCtrl.Position;
                if (AchievementLockField?.GetValue(topBar) is Control achCtrl) baseline.AchievementLockPos = achCtrl.Position;
                if (AscensionIconField?.GetValue(topBar) is Control ascCtrl) baseline.AscensionIconPos = ascCtrl.Position;
                if (AscensionLabelField?.GetValue(topBar) is Control ascLbl) baseline.AscensionLabelPos = ascLbl.Position;

                baseline.Initialized = true;
            }

            const float slotWidth = 64.0f;
            int extraSlots = Math.Max(0, maxPotions - 3);
            float shiftX = extraSlots * slotWidth;

            if (topBar.RoomIcon != null && GodotObject.IsInstanceValid(topBar.RoomIcon))
            {
                topBar.RoomIcon.Position = new Vector2(baseline.RoomIconPos.X + shiftX, baseline.RoomIconPos.Y);
            }

            if (topBar.FloorIcon != null && GodotObject.IsInstanceValid(topBar.FloorIcon))
            {
                topBar.FloorIcon.Position = new Vector2(baseline.FloorIconPos.X + shiftX, baseline.FloorIconPos.Y);
            }

            if (topBar.BossIcon != null && GodotObject.IsInstanceValid(topBar.BossIcon))
            {
                topBar.BossIcon.Position = new Vector2(baseline.BossIconPos.X + shiftX, baseline.BossIconPos.Y);
            }

            if (topBar.Timer != null && GodotObject.IsInstanceValid(topBar.Timer))
            {
                topBar.Timer.Position = new Vector2(baseline.TimerPos.X + shiftX, baseline.TimerPos.Y);
            }

            if (ModifiersContainerField?.GetValue(topBar) is Control mCtrl && GodotObject.IsInstanceValid(mCtrl))
            {
                mCtrl.Position = new Vector2(baseline.ModifiersPos.X + shiftX, baseline.ModifiersPos.Y);
            }

            if (AchievementLockField?.GetValue(topBar) is Control aCtrl && GodotObject.IsInstanceValid(aCtrl))
            {
                aCtrl.Position = new Vector2(baseline.AchievementLockPos.X + shiftX, baseline.AchievementLockPos.Y);
            }

            if (AscensionIconField?.GetValue(topBar) is Control asCtrl && GodotObject.IsInstanceValid(asCtrl))
            {
                asCtrl.Position = new Vector2(baseline.AscensionIconPos.X + shiftX, baseline.AscensionIconPos.Y);
            }

            if (AscensionLabelField?.GetValue(topBar) is Control asLbl && GodotObject.IsInstanceValid(asLbl))
            {
                asLbl.Position = new Vector2(baseline.AscensionLabelPos.X + shiftX, baseline.AscensionLabelPos.Y);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"AdjustTopBarLayout notice: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(NTopBar), "MaxPotionsChanged")]
    public static class NTopBar_MaxPotionsChanged_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NTopBar __instance, int maxPotions)
        {
            AdjustTopBarLayout(__instance, maxPotions);
        }
    }

    [HarmonyPatch(typeof(NTopBar), "Initialize")]
    public static class NTopBar_Initialize_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NTopBar __instance, IRunState runState)
        {
            try
            {
                var player = runState?.Players != null ? MegaCrit.Sts2.Core.Context.LocalContext.GetMe(runState.Players) : null;
                int count = player?.MaxPotionCount ?? 3;
                AdjustTopBarLayout(__instance, count);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(NPotionContainer), "GrowPotionHolders")]
    public static class NPotionContainer_GrowPotionHolders_Patch
    {
        private static readonly FieldInfo? HoldersField = typeof(NPotionContainer).GetField("_holders", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? PotionHoldersField = typeof(NPotionContainer).GetField("_potionHolders", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? UpdateNavMethod = typeof(NPotionContainer).GetMethod("UpdateNavigation", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        public static void Prefix(NPotionContainer __instance, int newMaxPotionSlots)
        {
            try
            {
                if (HoldersField?.GetValue(__instance) is List<NPotionHolder> holders &&
                    PotionHoldersField?.GetValue(__instance) is Control potionHoldersCtrl)
                {
                    if (newMaxPotionSlots < holders.Count)
                    {
                        for (int i = holders.Count - 1; i >= newMaxPotionSlots; i--)
                        {
                            var holder = holders[i];
                            if (holder != null && GodotObject.IsInstanceValid(holder))
                            {
                                if (potionHoldersCtrl != null && GodotObject.IsInstanceValid(potionHoldersCtrl) && potionHoldersCtrl.IsAncestorOf(holder))
                                {
                                    potionHoldersCtrl.RemoveChild(holder);
                                }
                                holder.QueueFree();
                            }
                            holders.RemoveAt(i);
                        }
                        UpdateNavMethod?.Invoke(__instance, null);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in NPotionContainer.GrowPotionHolders Prefix", ex);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(NPotionContainer __instance, int newMaxPotionSlots)
        {
            try
            {
                var topBar = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi?.TopBar ??
                             __instance.GetParent() as NTopBar ?? 
                             __instance.GetOwner() as NTopBar ??
                             (__instance.GetTree()?.Root != null ? GameHelper.FindNodeOfType<NTopBar>(__instance.GetTree().Root) : null);
                if (topBar != null)
                {
                    AdjustTopBarLayout(topBar, newMaxPotionSlots);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in NPotionContainer.GrowPotionHolders Postfix", ex);
            }
        }
    }
}
