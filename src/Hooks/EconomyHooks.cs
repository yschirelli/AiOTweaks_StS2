using System;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using HarmonyLib;
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
                int goldBonus = ConfigManager.Current.PreRunTweaks.StartingGoldBonus;
                ModLogger.Verbose("EconomyHooks", $"PopulateStartingInventory Postfix: checking goldBonus={goldBonus}, hpBonus={ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus}...");
                if (goldBonus > 0)
                {
                    __instance.Gold += goldBonus;
                    ModLogger.Info($"Granted starting gold bonus: +{goldBonus} (Total: {__instance.Gold})");
                }

                int hpBonus = ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus;
                if (hpBonus > 0 && __instance.Creature != null)
                {
                    GameHelper.ModifyCreatureHealth(__instance.Creature, hpBonus, hpBonus);
                    ModLogger.Info($"Granted starting Max HP bonus: +{hpBonus} (Max HP: {__instance.Creature.MaxHp})");
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
                int customCount = ConfigManager.Current.PreRunTweaks.CardRewardCount;
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

        float multiplier = ConfigManager.Current.PreRunTweaks.GoldRewardMultiplier;
        if (ConfigManager.ActiveRunSettings.GoldMultiplier > 0f)
        {
            multiplier *= ConfigManager.ActiveRunSettings.GoldMultiplier;
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
        float discountMult = ConfigManager.Current.PreRunTweaks.ShopDiscountMultiplier;
        if (Math.Abs(discountMult - 1.0f) > 0.001f)
        {
            int modified = Math.Max(1, (int)Math.Round(basePrice * discountMult));
            ModLogger.Info($"EconomyHook: Shop Price {basePrice} -> Discounted {modified} (x{discountMult:F2})");
            return modified;
        }

        return basePrice;
    }
}

