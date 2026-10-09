using System;
using System.Collections.Generic;
using System.Linq;
using AIOTweaks.Core.Diagnostics;
using AIOTweaks.Core.Logging;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts and records all major game events in Debug mode.
/// Dispatches events to ModLogger (which streams to aiotweaks_debug.log, Godot console, in-game Debug Console,
/// and the active per-run diagnostic log in logs/runs/) and records breadcrumb traces into BreadcrumbTracker.
/// </summary>
public static class GameEventHooks
{
    private static void LogEvent(string category, string message)
    {
        try
        {
            BreadcrumbTracker.Record(category, message);
            ModLogger.Debug($"[{category}] {message}");
        }
        catch { }
    }

    private static string GetCardName(CardModel? card)
    {
        if (card == null) return "Unknown Card";
        string title = card.Title ?? "";
        return !string.IsNullOrWhiteSpace(title) ? title : (card.Id.Entry ?? card.GetType().Name);
    }

    private static string GetRelicName(RelicModel? relic)
    {
        if (relic == null) return "Unknown Relic";
        string title = relic.Title?.GetFormattedText() ?? "";
        return !string.IsNullOrWhiteSpace(title) ? title : (relic.Id.Entry ?? relic.GetType().Name);
    }

    private static string GetPotionName(PotionModel? potion)
    {
        if (potion == null) return "Unknown Potion";
        string title = potion.Title?.GetFormattedText() ?? "";
        return !string.IsNullOrWhiteSpace(title) ? title : (potion.Id.Entry ?? potion.GetType().Name);
    }

    private static string GetCreatureName(Creature? creature)
    {
        if (creature == null) return "None";
        if (!string.IsNullOrWhiteSpace(creature.Name)) return creature.Name;
        if (!string.IsNullOrWhiteSpace(creature.LogName)) return creature.LogName;
        return creature.ModelId.Entry ?? creature.GetType().Name;
    }

    [HarmonyPatch(typeof(NSubmenu), nameof(NSubmenu.OnSubmenuOpened))]
    public static class SubmenuOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NSubmenu __instance)
        {
            try
            {
                string menuName = __instance.GetType().Name;
                LogEvent("MENU", $"Submenu opened: {menuName}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] SubmenuOpenedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NSubmenu), nameof(NSubmenu.OnSubmenuClosed))]
    public static class SubmenuClosedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NSubmenu __instance)
        {
            try
            {
                string menuName = __instance.GetType().Name;
                LogEvent("MENU", $"Submenu closed: {menuName}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] SubmenuClosedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Push))]
    public static class OverlayStackPushPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IOverlayScreen screen)
        {
            try
            {
                string screenName = screen?.GetType().Name ?? "UnknownOverlay";
                LogEvent("MENU", $"Overlay screen opened: {screenName}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] OverlayStackPushPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Remove))]
    public static class OverlayStackRemovePatch
    {
        [HarmonyPostfix]
        public static void Postfix(IOverlayScreen screen)
        {
            try
            {
                string screenName = screen?.GetType().Name ?? "UnknownOverlay";
                LogEvent("MENU", $"Overlay screen closed: {screenName}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] OverlayStackRemovePatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    public static class MapScreenOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isOpenedFromTopBar)
        {
            try
            {
                LogEvent("MENU", $"Map screen opened (FromTopBar: {isOpenedFromTopBar})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] MapScreenOpenPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Close))]
    public static class MapScreenClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool animateOut)
        {
            try
            {
                LogEvent("MENU", $"Map screen closed (Animate: {animateOut})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] MapScreenClosePatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NCardsViewScreen), nameof(NCardsViewScreen.AfterCapstoneOpened))]
    public static class CardsViewScreenOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCardsViewScreen __instance)
        {
            try
            {
                string viewName = __instance.GetType().Name;
                LogEvent("MENU", $"Cards view opened: {viewName}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardsViewScreenOpenedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NCardsViewScreen), nameof(NCardsViewScreen.AfterCapstoneClosed))]
    public static class CardsViewScreenClosedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCardsViewScreen __instance)
        {
            try
            {
                string viewName = __instance.GetType().Name;
                LogEvent("MENU", $"Cards view closed: {viewName}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardsViewScreenClosedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionUsed))]
    public static class AfterPotionUsedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, ICombatState combatState, PotionModel potion, Creature target)
        {
            try
            {
                string potionName = GetPotionName(potion);
                string targetName = GetCreatureName(target);
                LogEvent("POTION", $"Drank/Used Potion: '{potionName}' (ID: '{potion?.Id.Entry}') on Target: '{targetName}'");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterPotionUsedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionProcured))]
    public static class AfterPotionProcuredPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, ICombatState combatState, PotionModel potion)
        {
            try
            {
                string potionName = GetPotionName(potion);
                LogEvent("POTION", $"Procured Potion: '{potionName}' (ID: '{potion?.Id.Entry}')");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterPotionProcuredPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPotionDiscarded))]
    public static class AfterPotionDiscardedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, ICombatState combatState, PotionModel potion)
        {
            try
            {
                string potionName = GetPotionName(potion);
                LogEvent("POTION", $"Discarded Potion: '{potionName}' (ID: '{potion?.Id.Entry}')");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterPotionDiscardedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally))]
    public static class MapPointSelectedLocallyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NMapPoint point)
        {
            try
            {
                var mapPoint = point?.Point;
                if (mapPoint != null)
                {
                    LogEvent("MAP", $"Selected Map Node: Coord: (col: {mapPoint.coord.col}, row: {mapPoint.coord.row}) | PointType: {mapPoint.PointType}");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] MapPointSelectedLocallyPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.TravelToMapCoord))]
    public static class MapScreenTravelToMapCoordPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MapCoord coord)
        {
            try
            {
                LogEvent("MAP", $"Traveling to Map Node: Coord: (col: {coord.col}, row: {coord.row})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] MapScreenTravelToMapCoordPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.BeforeRoomEntered))]
    public static class BeforeRoomEnteredPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, AbstractRoom room)
        {
            try
            {
                int actIndex = runState?.CurrentActIndex ?? 0;
                var coord = runState?.CurrentMapCoord;
                string coordStr = coord != null ? $"(col: {coord.Value.col}, row: {coord.Value.row})" : "N/A";
                LogEvent("ROOM", $"Entering Room: Type: {room?.RoomType} | Coord: {coordStr} | Act Index: {actIndex}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] BeforeRoomEnteredPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
    public static class AfterRoomEnteredPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, AbstractRoom room)
        {
            try
            {
                LogEvent("ROOM", $"Room entered and initialized: Type: {room?.RoomType}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterRoomEnteredPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterActEntered))]
    public static class AfterActEnteredPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState)
        {
            try
            {
                int actIndex = runState?.CurrentActIndex ?? 0;
                string actName = runState?.Act?.Id.Entry ?? $"Act {actIndex}";
                LogEvent("ACT", $"Entered Act: {actName} (Index: {actIndex})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterActEnteredPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardPile), nameof(CardPile.AddInternal))]
    public static class CardPileAddInternalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CardPile __instance, CardModel card, int index, bool silent)
        {
            try
            {
                string cardName = GetCardName(card);
                string pileName = __instance.Type.ToString();
                int upgradeLvl = card?.CurrentUpgradeLevel ?? 0;
                int cost = card?.EnergyCost?.Canonical ?? 0;

                if (__instance.Type == PileType.Deck)
                {
                    LogEvent("DECK", $"Added card to Master Deck: '{cardName}' (ID: '{card?.Id.Entry}' | Upgrade: +{upgradeLvl} | Cost: {cost})");
                }
                else
                {
                    LogEvent("CARD", $"Added card to {pileName}: '{cardName}' (ID: '{card?.Id.Entry}')");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardPileAddInternalPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardPile), nameof(CardPile.RemoveInternal))]
    public static class CardPileRemoveInternalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CardPile __instance, CardModel card, bool silent)
        {
            try
            {
                string cardName = GetCardName(card);
                string pileName = __instance.Type.ToString();

                if (__instance.Type == PileType.Deck)
                {
                    LogEvent("DECK", $"Removed card from Master Deck: '{cardName}' (ID: '{card?.Id.Entry}')");
                }
                else
                {
                    LogEvent("CARD", $"Removed card from {pileName}: '{cardName}' (ID: '{card?.Id.Entry}')");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardPileRemoveInternalPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Upgrade), new Type[] { typeof(CardModel), typeof(CardPreviewStyle) })]
    public static class CardCmdUpgradePatch
    {
        [HarmonyPostfix]
        public static void Postfix(CardModel card, CardPreviewStyle style)
        {
            try
            {
                string cardName = GetCardName(card);
                int upgradeLvl = card?.CurrentUpgradeLevel ?? 0;
                LogEvent("CARD", $"Upgraded Card: '{cardName}' (ID: '{card?.Id.Entry}' | New Level: +{upgradeLvl})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardCmdUpgradePatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Transform), new Type[] { typeof(CardModel), typeof(CardModel), typeof(CardPreviewStyle) })]
    public static class CardCmdTransformPatch
    {
        [HarmonyPostfix]
        public static void Postfix(CardModel original, CardModel replacement, CardPreviewStyle style)
        {
            try
            {
                string origName = GetCardName(original);
                string repName = GetCardName(replacement);
                LogEvent("CARD", $"Transformed Card: '{origName}' (ID: '{original?.Id.Entry}') -> '{repName}' (ID: '{replacement?.Id.Entry}')");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardCmdTransformPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Enchant), new Type[] { typeof(CardModel), typeof(decimal) })]
    public static class CardCmdEnchantSimplePatch
    {
        [HarmonyPostfix]
        public static void Postfix(CardModel card, decimal amount)
        {
            try
            {
                string cardName = GetCardName(card);
                string enchName = card?.Enchantment?.Title?.GetFormattedText() ?? card?.Enchantment?.Id.Entry ?? "Default";
                LogEvent("CARD", $"Enchanted Card: '{cardName}' (ID: '{card?.Id.Entry}') with '{enchName}' (Amount: {amount})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardCmdEnchantSimplePatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Enchant), new Type[] { typeof(EnchantmentModel), typeof(CardModel), typeof(decimal) })]
    public static class CardCmdEnchantModelPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EnchantmentModel enchantment, CardModel card, decimal amount)
        {
            try
            {
                string cardName = GetCardName(card);
                string enchName = enchantment?.Title?.GetFormattedText() ?? enchantment?.Id.Entry ?? "UnknownEnchantment";
                LogEvent("CARD", $"Enchanted Card: '{cardName}' (ID: '{card?.Id.Entry}') with '{enchName}' (Amount: {amount})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] CardCmdEnchantModelPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.AddRelicInternal))]
    public static class PlayerAddRelicInternalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance, RelicModel relic, int index, bool silent)
        {
            try
            {
                string relicName = GetRelicName(relic);
                LogEvent("RELIC", $"Obtained Relic: '{relicName}' (ID: '{relic?.Id.Entry}' | Slot: {index})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] PlayerAddRelicInternalPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.RemoveRelicInternal))]
    public static class PlayerRemoveRelicInternalPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance, RelicModel relic, bool silent)
        {
            try
            {
                string relicName = GetRelicName(relic);
                LogEvent("RELIC", $"Removed Relic: '{relicName}' (ID: '{relic?.Id.Entry}')");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] PlayerRemoveRelicInternalPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Player), "OnRelicFlashed")]
    public static class PlayerOnRelicFlashedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Player __instance, RelicModel relic, IEnumerable<Creature> targets)
        {
            try
            {
                string relicName = GetRelicName(relic);
                string countStr = relic?.ShowCounter == true ? $" | Counter: {relic.DisplayAmount}" : "";
                LogEvent("RELIC", $"Relic Activated/Flashed: '{relicName}' (ID: '{relic?.Id.Entry}'{countStr})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] PlayerOnRelicFlashedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(EventModel), nameof(EventModel.BeginEvent))]
    public static class EventModelBeginEventPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EventModel __instance, Player player, bool isPreFinished)
        {
            try
            {
                string eventTitle = __instance.Title?.GetFormattedText() ?? "";
                string titleStr = !string.IsNullOrWhiteSpace(eventTitle) ? $"'{eventTitle}' " : "";
                LogEvent("EVENT", $"Event Started: {titleStr}(ID: '{__instance.Id.Entry}')");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] EventModelBeginEventPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked))]
    public static class EventRoomOptionButtonClickedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EventOption option, int index)
        {
            try
            {
                string optionTitle = option?.Title?.GetFormattedText() ?? option?.TextKey ?? "Option";
                string desc = option?.Description?.GetFormattedText() ?? "";
                string descStr = !string.IsNullOrWhiteSpace(desc) ? $" - Desc: \"{desc}\"" : "";
                LogEvent("EVENT", $"Event Option Selected: [#{index}] \"{optionTitle}\"{descStr}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] EventRoomOptionButtonClickedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(EventModel), "OnEventFinished")]
    public static class EventModelOnEventFinishedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(EventModel __instance)
        {
            try
            {
                string eventTitle = __instance.Title?.GetFormattedText() ?? __instance.Id.Entry ?? "Event";
                LogEvent("EVENT", $"Event Finished: '{eventTitle}'");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] EventModelOnEventFinishedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom.AfterSelectingOption))]
    public static class RestSiteRoomAfterSelectingOptionPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RestSiteOption option)
        {
            try
            {
                string optionType = option?.GetType().Name ?? "UnknownOption";
                LogEvent("REST_SITE", $"Campfire Option Selected: {optionType}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] RestSiteRoomAfterSelectingOptionPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRestSiteHeal))]
    public static class AfterRestSiteHealPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, Player player, bool isMimicked)
        {
            try
            {
                int hp = player?.Creature?.CurrentHp ?? 0;
                int maxHp = player?.Creature?.MaxHp ?? 0;
                LogEvent("REST_SITE", $"Campfire Heal Executed: Current HP: {hp}/{maxHp} (Mimicked: {isMimicked})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterRestSiteHealPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRestSiteSmith))]
    public static class AfterRestSiteSmithPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, Player player)
        {
            try
            {
                LogEvent("REST_SITE", "Campfire Smith (Card Upgrade) Executed");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterRestSiteSmithPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterItemPurchased))]
    public static class AfterItemPurchasedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, Player player, MerchantEntry itemPurchased, int goldSpent)
        {
            try
            {
                string entryType = itemPurchased?.GetType().Name ?? "MerchantEntry";
                int remainingGold = player?.Gold ?? 0;
                string details = "";

                if (itemPurchased is MerchantCardEntry cardEntry)
                {
                    details = $"Card: '{GetCardName(cardEntry.CreationResult?.Card)}'";
                }
                else if (itemPurchased is MerchantRelicEntry relicEntry)
                {
                    details = $"Relic: '{GetRelicName(relicEntry.Model)}'";
                }
                else if (itemPurchased is MerchantPotionEntry potionEntry)
                {
                    details = $"Potion: '{GetPotionName(potionEntry.Model)}'";
                }
                else if (itemPurchased is MerchantCardRemovalEntry)
                {
                    details = "Card Removal Service";
                }

                string detailStr = !string.IsNullOrEmpty(details) ? $" ({details})" : "";
                LogEvent("SHOP", $"Purchased: {entryType}{detailStr} for {goldSpent} Gold (Remaining: {remainingGold} Gold)");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterItemPurchasedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterGoldGained))]
    public static class AfterGoldGainedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, Player player)
        {
            try
            {
                int currentGold = player?.Gold ?? 0;
                LogEvent("GOLD", $"Gold Gained. New Total: {currentGold} Gold");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterGoldGainedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "OpenChest")]
    public static class TreasureRoomOpenChestPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try
            {
                LogEvent("CHEST", "Treasure Chest Opened");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] TreasureRoomOpenChestPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRewardTaken))]
    public static class AfterRewardTakenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, Player player, Reward reward)
        {
            try
            {
                string rewardType = reward?.GetType().Name ?? "Reward";
                string details = "";

                if (reward is CardReward cardReward && cardReward.Cards != null)
                {
                    details = $"Options: [{string.Join(", ", cardReward.Cards.Select(c => GetCardName(c)))}]";
                }
                else if (reward is GoldReward goldReward)
                {
                    details = $"{goldReward.Amount} Gold";
                }
                else if (reward is RelicReward relicReward)
                {
                    details = $"Relic: '{GetRelicName(relicReward.Relic)}'";
                }
                else if (reward is PotionReward potionReward)
                {
                    details = $"Potion: '{GetPotionName(potionReward.Potion)}'";
                }

                string detailStr = !string.IsNullOrEmpty(details) ? $" ({details})" : "";
                LogEvent("REWARD", $"Reward Claimed: {rewardType}{detailStr}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterRewardTakenPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
    public static class AfterPlayerTurnStartPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ICombatState combatState, PlayerChoiceContext choiceContext, Player player)
        {
            try
            {
                int round = combatState?.RoundNumber ?? 1;
                int energy = player?.PlayerCombatState?.Energy ?? 0;
                int handCount = player?.PlayerCombatState?.Hand?.Cards?.Count ?? 0;
                LogEvent("COMBAT", $"Player Turn Started: Round #{round} | Energy: {energy} | Hand: {handCount} cards");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterPlayerTurnStartPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterSideTurnEnd))]
    public static class AfterTurnEndPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ICombatState combatState, CombatSide side, IEnumerable<Creature> participants)
        {
            try
            {
                LogEvent("COMBAT", $"Turn Ended for Side: {side}");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterTurnEndPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    public static class AfterCardPlayedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
        {
            try
            {
                var card = cardPlay?.Card;
                string cardName = GetCardName(card);
                string targetName = GetCreatureName(cardPlay?.Target);
                LogEvent("COMBAT", $"Card Played: '{cardName}' (ID: '{card?.Id.Entry}') -> Target: '{targetName}'");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterCardPlayedPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterDeath))]
    public static class AfterDeathPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, ICombatState combatState, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
        {
            try
            {
                string name = GetCreatureName(creature);
                string role = creature.IsPlayer ? "Player" : "Enemy";
                LogEvent("COMBAT", $"Creature Defeated: '{name}' ({role} | HP: {creature.CurrentHp}/{creature.MaxHp})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterDeathPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
    public static class AfterCombatVictoryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, ICombatState combatState, CombatRoom room)
        {
            try
            {
                int rounds = combatState?.RoundNumber ?? 0;
                LogEvent("COMBAT", $"Combat Victory Achieved! (Total Rounds: {rounds})");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterCombatVictoryPatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
    public static class AfterCombatEndPatch
    {
        [HarmonyPostfix]
        public static void Postfix(IRunState runState, ICombatState combatState, CombatRoom room)
        {
            try
            {
                LogEvent("COMBAT", "Combat Ended");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"[GameEventHooks] AfterCombatEndPatch error: {ex.Message}");
            }
        }
    }
}
