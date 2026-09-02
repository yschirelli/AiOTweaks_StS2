using System;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

/// <summary>
/// Harmony patches intercepting gold drops, combat rewards, and starting bonuses.
/// </summary>
public static class EconomyHooks
{

    [HarmonyPatch(typeof(GoldReward), nameof(GoldReward.Populate))]
    public static class GoldRewardPopulatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(GoldReward __instance)
        {
            try
            {
                int baseAmount = __instance.Amount;
                int modified = ProcessGoldDrop(baseAmount);
                ModLogger.Verbose("EconomyHooks", $"GoldReward.Populate Postfix: base={baseAmount}, modified={modified}");
                if (modified != baseAmount)
                {
                    GameHelper.SetGoldRewardAmount(__instance, modified);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting GoldReward amount", ex);
            }
        }
    }

    [HarmonyPatch(typeof(Player), "PopulateStartingInventory")]
    public static class PlayerStartingInventoryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance)
        {
            try
            {
                int maxEnergy = RunTweaksSaveManager.GetEffectivePreRunTweaks().MaxEnergy;
                if (maxEnergy > 0 && maxEnergy != 3)
                {
                    __instance.MaxEnergy = maxEnergy;
                    ModLogger.Info($"Configured starting player MaxEnergy: {maxEnergy}");
                }

                int goldBonus = RunTweaksSaveManager.GetEffectivePreRunTweaks().StartingGoldBonus;
                ModLogger.Verbose("EconomyHooks", $"PopulateStartingInventory Postfix: checking goldBonus={goldBonus}, hpBonus={RunTweaksSaveManager.GetEffectivePreRunTweaks().StartingMaxHpBonus}, maxEnergy={maxEnergy}...");
                if (goldBonus > 0)
                {
                    __instance.Gold += goldBonus;
                    ModLogger.Info($"Granted starting gold bonus: +{goldBonus} (Total: {__instance.Gold})");
                }

                int hpBonus = RunTweaksSaveManager.GetEffectivePreRunTweaks().StartingMaxHpBonus;
                if (hpBonus > 0 && __instance.Creature != null)
                {
                    GameHelper.ModifyCreatureHealth(__instance.Creature, hpBonus, hpBonus);
                    ModLogger.Info($"Granted starting Max HP bonus: +{hpBonus} (Max HP: {__instance.Creature.MaxHp})");
                }

                int customPotionSlots = RunTweaksSaveManager.GetEffectivePreRunTweaks().PotionSlots;
                if (customPotionSlots > 0 && customPotionSlots != 3)
                {
                    var method = typeof(Player).GetMethod("SetMaxPotionCountInternal", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (method != null)
                    {
                        method.Invoke(__instance, new object[] { customPotionSlots });
                    }
                    else
                    {
                        int diff = customPotionSlots - __instance.MaxPotionCount;
                        if (diff > 0) __instance.AddToMaxPotionCount(diff);
                        else if (diff < 0) __instance.SubtractFromMaxPotionCount(-diff);
                    }
                    ModLogger.Info($"Configured custom starting player PotionSlots: {customPotionSlots} (was {__instance.MaxPotionCount})");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error applying starting player bonuses", ex);
            }
        }
    }

    [HarmonyPatch(typeof(CardReward), MethodType.Constructor, new Type[] { typeof(CardCreationOptions), typeof(int), typeof(Player), typeof(PlayerChoiceSynchronizer) })]
    public static class CardRewardConstructorPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref int cardCount)
        {
            try
            {
                int customCount = RunTweaksSaveManager.GetEffectivePreRunTweaks().CardRewardCount;
                if (customCount > 0 && customCount != 3)
                {
                    ModLogger.Verbose("EconomyHooks", $"CardReward constructor: overriding default card count {cardCount} -> {customCount}");
                    cardCount = customCount;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting CardReward count", ex);
            }
        }
    }

    [HarmonyPatch(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CalcCost))]
    public static class MerchantCardEntryCalcCostPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MerchantCardEntry __instance)
        {
            try
            {
                int baseCost = __instance.Cost;
                int modified = ProcessShopPrice(baseCost);
                if (modified != baseCost)
                {
                    Traverse.Create(__instance).Field("_cost").SetValue(modified);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MerchantCardEntry cost", ex);
            }
        }
    }

    [HarmonyPatch(typeof(MerchantRelicEntry), nameof(MerchantRelicEntry.CalcCost))]
    public static class MerchantRelicEntryCalcCostPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MerchantRelicEntry __instance)
        {
            try
            {
                int baseCost = __instance.Cost;
                int modified = ProcessShopPrice(baseCost);
                if (modified != baseCost)
                {
                    Traverse.Create(__instance).Field("_cost").SetValue(modified);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MerchantRelicEntry cost", ex);
            }
        }
    }

    [HarmonyPatch(typeof(MerchantPotionEntry), nameof(MerchantPotionEntry.CalcCost))]
    public static class MerchantPotionEntryCalcCostPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MerchantPotionEntry __instance)
        {
            try
            {
                int baseCost = __instance.Cost;
                int modified = ProcessShopPrice(baseCost);
                if (modified != baseCost)
                {
                    Traverse.Create(__instance).Field("_cost").SetValue(modified);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MerchantPotionEntry cost", ex);
            }
        }
    }

    [HarmonyPatch(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.CalcCost))]
    public static class MerchantCardRemovalEntryCalcCostPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MerchantCardRemovalEntry __instance)
        {
            try
            {
                int baseCost = __instance.Cost;
                int modified = ProcessShopPrice(baseCost);
                if (modified != baseCost)
                {
                    Traverse.Create(__instance).Field("_cost").SetValue(modified);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MerchantCardRemovalEntry cost", ex);
            }
        }
    }

    /// <summary>
    /// Multiplies or overrides gold drops awarded after combat encounters.
    /// </summary>
    public static int ProcessGoldDrop(int baseGold)
    {
        if (RuntimeStateManager.ForcedGoldDropAmount.HasValue)
        {
            int forced = RuntimeStateManager.ForcedGoldDropAmount.Value;
            ModLogger.Verbose("EconomyHooks", $"ProcessGoldDrop: ForcedGoldDropAmount active: {baseGold} -> {forced}");
            return forced;
        }

        float multiplier = RunTweaksSaveManager.GetEffectivePreRunTweaks().GoldRewardMultiplier;
        if (RunTweaksSaveManager.GetEffectiveRunSettings().GoldMultiplier > 0f)
        {
            multiplier *= RunTweaksSaveManager.GetEffectiveRunSettings().GoldMultiplier;
        }

        if (Math.Abs(multiplier - 1.0f) > 0.001f)
        {
            int modified = (int)Math.Round(baseGold * multiplier);
            ModLogger.Info($"EconomyHook: Base Gold {baseGold} -> Modified {modified} (x{multiplier:F2})");
            return modified;
        }

        return baseGold;
    }

    /// <summary>
    /// Applies shop discount multiplier to merchant item prices.
    /// </summary>
    public static int ProcessShopPrice(int basePrice)
    {
        float discountMult = RunTweaksSaveManager.GetEffectivePreRunTweaks().ShopDiscountMultiplier;
        if (Math.Abs(discountMult - 1.0f) > 0.001f)
        {
            int modified = Math.Max(1, (int)Math.Round(basePrice * discountMult));
            ModLogger.Info($"EconomyHook: Shop Price {basePrice} -> Discounted {modified} (x{discountMult:F2})");
            return modified;
        }

        return basePrice;
    }
}

