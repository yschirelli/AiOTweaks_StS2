using System;
using System.Linq;
using System.Reflection;
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
                    int oldMaxPotionCount = __instance.MaxPotionCount;
                    var method = typeof(Player).GetMethod("SetMaxPotionCountInternal", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (method != null)
                    {
                        method.Invoke(__instance, new object[] { customPotionSlots });
                    }
                    else
                    {
                        int diff = customPotionSlots - oldMaxPotionCount;
                        if (diff > 0) __instance.AddToMaxPotionCount(diff);
                        else if (diff < 0) __instance.SubtractFromMaxPotionCount(-diff);
                    }
                    ModLogger.Info($"Configured custom starting player PotionSlots: {customPotionSlots} (was {oldMaxPotionCount})");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error applying starting player bonuses", ex);
            }
        }
    }

    [HarmonyPatch]
    public static class CardRewardConstructorPatch
    {
        [HarmonyTargetMethod]
        public static MethodBase? TargetMethod()
        {
            return typeof(CardReward).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c =>
                {
                    var p = c.GetParameters();
                    return p.Length >= 3
                        && p[0].ParameterType == typeof(CardCreationOptions)
                        && p[1].ParameterType == typeof(int)
                        && p[2].ParameterType == typeof(Player);
                });
        }

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

    [HarmonyPatch]
    public static class MerchantEntryCalcCostPatch
    {
        [HarmonyTargetMethods]
        public static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
        {
            Type[] entryTypes =
            [
                typeof(MerchantCardEntry),
                typeof(MerchantRelicEntry),
                typeof(MerchantPotionEntry),
                typeof(MerchantCardRemovalEntry)
            ];

            foreach (var type in entryTypes)
            {
                var method = type.GetMethod("CalcCost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MerchantEntry __instance)
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
                ModLogger.Error($"Error adjusting {__instance?.GetType().Name ?? "MerchantEntry"} cost", ex);
            }
        }
    }

    /// <summary>
    /// Multiplies or overrides gold drops awarded after combat encounters.
    /// </summary>
    public static int ProcessGoldDrop(int baseGold)
    {
        float multiplier = RunTweaksSaveManager.GetEffectivePreRunTweaks().GoldRewardMultiplier;
        int modified = ModMath.ComputeGoldDrop(baseGold, multiplier, RuntimeStateManager.ForcedGoldDropAmount);
        if (modified != baseGold)
        {
            ModLogger.Info($"EconomyHook: Base Gold {baseGold} -> Modified {modified} (x{multiplier:F2})");
        }
        return modified;
    }

    /// <summary>
    /// Applies shop discount multiplier to merchant item prices.
    /// </summary>
    public static int ProcessShopPrice(int basePrice)
    {
        float discountMult = RunTweaksSaveManager.GetEffectivePreRunTweaks().ShopDiscountMultiplier;
        int modified = ModMath.ComputeShopPrice(basePrice, discountMult);
        if (modified != basePrice)
        {
            ModLogger.Info($"EconomyHook: Shop Price {basePrice} -> Discounted {modified} (x{discountMult:F2})");
        }
        return modified;
    }
}

