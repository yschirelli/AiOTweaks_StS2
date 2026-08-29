using System;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for deck and hand manipulation (spawning cards, upgrading, removing).
/// Directly and immediately modifies the player's master deck and active combat piles without queueing.
/// </summary>
public static class CardDirector
{
    public static event Action<string, bool>? OnCardAdded;
    public static event Action<string>? OnCardRemoved;
    public static event Action? OnDeckChanged;

    /// <summary>
    /// Adds a card directly and immediately into the player's master deck.
    /// If currently in combat, also adds an active instance to the player's combat draw pile.
    /// </summary>
    public static bool AddCardToDeck(string cardId, bool upgraded = false)
    {
        ModLogger.Verbose("CardDirector", $"AddCardToDeck called: cardId='{cardId}', upgraded={upgraded}");
        if (string.IsNullOrWhiteSpace(cardId))
        {
            ModLogger.Warn("Card ID cannot be null or empty.");
            return false;
        }
        
        if (cardId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("CardDirector", "AddCardToDeck: Batch adding all available cards to deck...");
            var allCards = GameHelper.GetAllCardIds();
            foreach (var c in allCards)
            {
                AddCardToDeck(c, upgraded);
            }
            OnDeckChanged?.Invoke();
            return true;
        }

        try
        {
            var player = GameHelper.GetActivePlayer();
            var canonical = GameHelper.FindCanonicalCardModel(cardId);
            ModLogger.Verbose("CardDirector", $"Player resolved: {player != null}, Canonical model resolved: {canonical?.GetType().Name ?? "null"}");

            if (player != null && canonical != null)
            {
                var newCard = GameHelper.CreateCardForPlayer(canonical, player);
                if (newCard != null)
                {
                    if (upgraded)
                    {
                        try
                        {
                            newCard.UpgradeInternal();
                            ModLogger.Verbose("CardDirector", $"Upgraded new card instance: {newCard.GetType().Name} (IsUpgraded={newCard.IsUpgraded})");
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Warn($"Could not upgrade card '{cardId}': {ex.Message}");
                        }
                    }

                    // 1. Add directly to master deck
                    if (player.Deck != null)
                    {
                        player.Deck.AddInternal(newCard, -1, false);
                        player.Deck.InvokeContentsChanged();
                        ModLogger.Info($"Card '{canonical.GetType().Name}' (Upgraded: {newCard.IsUpgraded}) added directly to Master Deck. (Deck count: {player.Deck.Cards.Count})");
                    }

                    // 2. If currently in combat, also add an active instance to Draw Pile for immediate availability
                    if (player.PlayerCombatState?.DrawPile != null)
                    {
                        try
                        {
                            var combatCard = GameHelper.CreateCardForPlayer(canonical, player);
                            if (combatCard != null)
                            {
                                if (upgraded && !combatCard.IsUpgraded)
                                {
                                    combatCard.UpgradeInternal();
                                }
                                combatCard.DeckVersion = newCard;

                                try
                                {
                                    _ = CardPileCmd.Add(combatCard, PileType.Draw, CardPilePosition.Top, null, true);
                                }
                                catch
                                {
                                    player.PlayerCombatState.DrawPile.AddInternal(combatCard, -1, false);
                                    player.PlayerCombatState.DrawPile.InvokeContentsChanged();
                                }

                                ModLogger.Info($"Card '{canonical.GetType().Name}' also added to active Combat Draw Pile. (DrawPile count: {player.PlayerCombatState.DrawPile.Cards.Count})");
                            }
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Debug($"Notice adding card to combat draw pile: {ex.Message}");
                        }
                    }

                    string resolvedId = canonical.GetType().Name;
                    OnCardAdded?.Invoke(resolvedId, upgraded);
                    OnDeckChanged?.Invoke();
                    return true;
                }
            }

            // Fallback to DevConsole if direct runtime addition was not possible
            string cmd = upgraded ? $"card {cardId}+" : $"card {cardId}";
            ModLogger.Verbose("CardDirector", $"Direct addition unfulfilled; executing DevConsole fallback: '{cmd}'");
            GameHelper.ExecuteConsoleCommand(cmd);

            ModLogger.Info($"Card '{cardId}' (Upgraded: {upgraded}) dispatched via DevConsole fallback.");
            OnCardAdded?.Invoke(cardId, upgraded);
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to add card '{cardId}' to deck", ex);
            return false;
        }
    }

    /// <summary>
    /// Spawns a card directly into the player's active combat hand.
    /// </summary>
    public static bool SpawnCardInHand(string cardId, int costOverride = -1)
    {
        ModLogger.Verbose("CardDirector", $"SpawnCardInHand called: cardId='{cardId}', costOverride={costOverride}");
        if (string.IsNullOrWhiteSpace(cardId))
        {
            ModLogger.Warn("Card ID cannot be null or empty.");
            return false;
        }

        if (cardId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("CardDirector", "SpawnCardInHand: Batch spawning all cards into hand...");
            var allCards = GameHelper.GetAllCardIds();
            foreach (var c in allCards)
            {
                SpawnCardInHand(c, costOverride);
            }
            OnDeckChanged?.Invoke();
            return true;
        }

        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player?.PlayerCombatState?.Hand != null)
            {
                var canonical = GameHelper.FindCanonicalCardModel(cardId);
                if (canonical != null)
                {
                    var handCard = GameHelper.CreateCardForPlayer(canonical, player);
                    if (handCard != null)
                    {
                        if (costOverride == 0)
                        {
                            try
                            {
                                handCard.SetToFreeThisTurn();
                                ModLogger.Verbose("CardDirector", $"Set hand card '{handCard.GetType().Name}' to free this turn.");
                            }
                            catch { }
                        }

                        bool spawnedVisually = false;
                        try
                        {
                            _ = CardPileCmd.Add(handCard, PileType.Hand, CardPilePosition.Top, null, false);
                            spawnedVisually = true;
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Debug($"CardPileCmd.Add notice: {ex.Message}");
                        }

                        if (!spawnedVisually || !player.PlayerCombatState.Hand.Cards.Contains(handCard))
                        {
                            player.PlayerCombatState.Hand.AddInternal(handCard, -1, false);
                            player.PlayerCombatState.Hand.InvokeContentsChanged();
                            if (NPlayerHand.Instance != null)
                            {
                                try
                                {
                                    var ncard = NCard.Create(handCard);
                                    if (ncard != null)
                                    {
                                        NPlayerHand.Instance.Add(ncard, -1);
                                        NPlayerHand.Instance.ForceRefreshCardIndices();
                                    }
                                }
                                catch { }
                            }
                        }

                        ModLogger.Info($"Card '{canonical.GetType().Name}' spawned directly into Hand. (Hand count: {player.PlayerCombatState.Hand.Cards.Count})");
                        OnDeckChanged?.Invoke();
                        return true;
                    }
                }
            }

            // Fallback via DevConsole
            ModLogger.Verbose("CardDirector", $"Player combat hand not ready; dispatching DevConsole command 'card {cardId}'");
            GameHelper.ExecuteConsoleCommand($"card {cardId}");
            ModLogger.Info($"Spawn card requested for '{cardId}' via DevConsole fallback.");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to spawn card in hand '{cardId}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes a specific card model from the player's master deck and all combat piles in real time.
    /// </summary>
    public static bool RemoveCard(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card == null)
        {
            ModLogger.Verbose("CardDirector", "RemoveCard: card argument is null.");
            return false;
        }
        string cardTypeName = card.GetType().Name;
        ModLogger.Verbose("CardDirector", $"RemoveCard called for instance: {cardTypeName} (IsUpgraded={card.IsUpgraded}, Pile={card.Pile?.Type})");
        try
        {
            var player = GameHelper.GetActivePlayer();
            bool removed = false;

            // 1. Remove from its current host pile directly
            if (card.Pile != null)
            {
                var pile = card.Pile;
                ModLogger.Verbose("CardDirector", $"Removing card from host pile ({pile.Type})...");
                pile.RemoveInternal(card, false);
                pile.InvokeContentsChanged();
                removed = true;
            }

            // 2. Remove from player master deck if present (or remove master deck version if this was a combat copy)
            if (player?.Deck != null)
            {
                if (player.Deck.Cards.Contains(card))
                {
                    ModLogger.Verbose("CardDirector", "Removing card instance directly from Player.Deck...");
                    player.Deck.RemoveInternal(card, false);
                    player.Deck.InvokeContentsChanged();
                    removed = true;
                }
                else
                {
                    var masterCard = card.DeckVersion ?? card.CloneOf ?? player.Deck.Cards.FirstOrDefault(m => 
                        m == card || 
                        (m.GetType() == card.GetType() && m.IsUpgraded == card.IsUpgraded));
                    if (masterCard != null)
                    {
                        ModLogger.Verbose("CardDirector", $"Removing corresponding master deck card for combat clone: {masterCard.GetType().Name}");
                        player.Deck.RemoveInternal(masterCard, false);
                        player.Deck.InvokeContentsChanged();
                        removed = true;
                    }
                }
            }

            // 3. Remove from combat piles if in combat
            if (player?.PlayerCombatState != null)
            {
                var combatMatches = player.PlayerCombatState.AllPiles
                    .Where(p => p?.Cards != null)
                    .SelectMany(p => p.Cards)
                    .Where(c => c != null && (
                        c == card || 
                        c.DeckVersion == card || 
                        c.CloneOf == card || 
                        (c.GetType() == card.GetType() && c.IsUpgraded == card.IsUpgraded)
                    ))
                    .Distinct()
                    .ToList();

                foreach (var combatCard in combatMatches)
                {
                    ModLogger.Verbose("CardDirector", $"Removing matching combat card ({combatCard.GetType().Name}) from pile {combatCard.Pile?.Type}...");
                    try
                    {
                        _ = CardPileCmd.RemoveFromCombat(combatCard, true);
                        removed = true;
                    }
                    catch
                    {
                        combatCard.Pile?.RemoveInternal(combatCard, false);
                        combatCard.Pile?.InvokeContentsChanged();
                        removed = true;
                    }

                    if (NPlayerHand.Instance != null && combatCard.Pile == player.PlayerCombatState.Hand)
                    {
                        try
                        {
                            NPlayerHand.Instance.Remove(combatCard);
                            NPlayerHand.Instance.ForceRefreshCardIndices();
                        }
                        catch { }
                    }
                }
            }

            // 4. Safe state removal notify
            try
            {
                card.RemoveFromState();
                ModLogger.Verbose("CardDirector", "Called card.RemoveFromState().");
            }
            catch { }

            if (removed)
            {
                ModLogger.Info($"Card '{cardTypeName}' successfully removed from player deck and combat piles.");
                OnCardRemoved?.Invoke(cardTypeName);
                OnDeckChanged?.Invoke();
                return true;
            }

            ModLogger.Verbose("CardDirector", $"Card '{cardTypeName}' was not found in any active piles.");
            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to remove card '{cardTypeName}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes cards matching the ID from the player's deck and active piles in real time.
    /// Supports 'all' to wipe the master deck and combat piles clean.
    /// </summary>
    public static bool RemoveCardFromDeck(string cardId)
    {
        ModLogger.Verbose("CardDirector", $"RemoveCardFromDeck called: cardId='{cardId}'");
        if (string.IsNullOrWhiteSpace(cardId)) return false;

        var player = GameHelper.GetActivePlayer();

        if (cardId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("CardDirector", "RemoveCardFromDeck: Removing ALL cards from Master Deck and combat piles...");
            bool anyRemoved = false;
            if (player?.Deck != null)
            {
                var allCards = player.Deck.Cards.ToList();
                foreach (var card in allCards)
                {
                    RemoveCard(card);
                    anyRemoved = true;
                }
                player.Deck.InvokeContentsChanged();
            }

            if (player?.PlayerCombatState != null)
            {
                foreach (var pile in player.PlayerCombatState.AllPiles)
                {
                    if (pile?.Cards != null)
                    {
                        var pileCards = pile.Cards.ToList();
                        foreach (var c in pileCards)
                        {
                            try { _ = CardPileCmd.RemoveFromCombat(c, true); } catch { pile.RemoveInternal(c, false); }
                            anyRemoved = true;
                        }
                        pile.InvokeContentsChanged();
                    }
                }
                if (NPlayerHand.Instance != null)
                {
                    NPlayerHand.Instance.CancelAllCardPlay();
                    NPlayerHand.Instance.ForceRefreshCardIndices();
                }
            }

            ModLogger.Info("All cards removed from Master Deck and active combat piles.");
            OnDeckChanged?.Invoke();
            return anyRemoved;
        }

        try
        {
            if (player?.Deck?.Cards != null)
            {
                var card = player.Deck.Cards.FirstOrDefault(c => 
                    c != null && (
                        c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    ));
                if (card != null)
                {
                    ModLogger.Verbose("CardDirector", $"Found matching card in player.Deck: {card.GetType().Name}");
                    return RemoveCard(card);
                }
            }

            if (player?.PlayerCombatState != null)
            {
                foreach (var pile in player.PlayerCombatState.AllPiles)
                {
                    var combatCard = pile?.Cards?.FirstOrDefault(c => 
                        c != null && (
                            c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                            c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                        ));
                    if (combatCard != null)
                    {
                        ModLogger.Verbose("CardDirector", $"Found matching card in combat pile: {combatCard.GetType().Name}");
                        return RemoveCard(combatCard);
                    }
                }
            }

            ModLogger.Verbose("CardDirector", $"Card '{cardId}' not directly matched; sending DevConsole command 'remove {cardId}'");
            GameHelper.ExecuteConsoleCommand($"remove {cardId}");
            ModLogger.Info($"Attempted to remove card '{cardId}' via DevConsole fallback.");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to remove card '{cardId}' from deck", ex);
            return false;
        }
    }

    /// <summary>
    /// Upgrades or downgrades a specific card model immediately in-place across deck and combat piles.
    /// </summary>
    public static bool ToggleUpgradeCard(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card == null)
        {
            ModLogger.Verbose("CardDirector", "ToggleUpgradeCard: card is null.");
            return false;
        }
        string cardName = card.GetType().Name;
        ModLogger.Verbose("CardDirector", $"ToggleUpgradeCard called for: {cardName} (Currently Upgraded={card.IsUpgraded})");
        try
        {
            bool targetUpgraded = !card.IsUpgraded;
            if (card.IsUpgraded)
            {
                card.DowngradeInternal();
                ModLogger.Info($"Card '{cardName}' downgraded.");
            }
            else
            {
                card.UpgradeInternal();
                ModLogger.Info($"Card '{cardName}' upgraded.");
            }
            card.Pile?.InvokeContentsChanged();

            // Synchronize upgrade state with matching master or combat instance
            var player = GameHelper.GetActivePlayer();
            if (player != null)
            {
                if (player.Deck?.Cards != null && !player.Deck.Cards.Contains(card))
                {
                    var master = card.DeckVersion ?? card.CloneOf ?? player.Deck.Cards.FirstOrDefault(m => m == card || m.GetType() == card.GetType());
                    if (master != null && master.IsUpgraded != targetUpgraded)
                    {
                        if (targetUpgraded) master.UpgradeInternal(); else master.DowngradeInternal();
                        player.Deck.InvokeContentsChanged();
                    }
                }

                if (player.PlayerCombatState != null)
                {
                    var combatCard = player.PlayerCombatState.AllCards?.FirstOrDefault(c => c != null && (c == card || c.DeckVersion == card || c.CloneOf == card || c.GetType() == card.GetType()));
                    if (combatCard != null && combatCard != card && combatCard.IsUpgraded != targetUpgraded)
                    {
                        if (targetUpgraded) combatCard.UpgradeInternal(); else combatCard.DowngradeInternal();
                        combatCard.Pile?.InvokeContentsChanged();
                    }
                }
            }

            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to toggle upgrade for card '{cardName}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Toggles upgrade on the first matching card found in the player's deck or combat piles.
    /// </summary>
    public static bool ToggleUpgradeInDeck(string cardId)
    {
        ModLogger.Verbose("CardDirector", $"ToggleUpgradeInDeck called for: '{cardId}'");
        if (string.IsNullOrWhiteSpace(cardId)) return false;
        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player?.Deck?.Cards != null)
            {
                var card = player.Deck.Cards.FirstOrDefault(c => 
                    c != null && (
                        c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    ));
                if (card != null)
                {
                    ModLogger.Verbose("CardDirector", $"Found matching card in player.Deck to toggle upgrade: {card.GetType().Name}");
                    return ToggleUpgradeCard(card);
                }
            }

            if (player?.PlayerCombatState != null)
            {
                foreach (var pile in player.PlayerCombatState.AllPiles)
                {
                    var combatCard = pile?.Cards?.FirstOrDefault(c => 
                        c != null && (
                            c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                            c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                        ));
                    if (combatCard != null)
                    {
                        ModLogger.Verbose("CardDirector", $"Found matching card in combat pile to toggle upgrade: {combatCard.GetType().Name}");
                        return ToggleUpgradeCard(combatCard);
                    }
                }
            }

            ModLogger.Verbose("CardDirector", $"Card '{cardId}' not directly found in deck; executing DevConsole command 'upgrade {cardId}'");
            GameHelper.ExecuteConsoleCommand($"upgrade {cardId}");
            ModLogger.Info($"Attempted to upgrade card '{cardId}' via DevConsole fallback.");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to upgrade card '{cardId}' in deck", ex);
            return false;
        }
    }

    /// <summary>
    /// Moves a card from any combat pile (Draw, Discard, Exhaust) directly into the active combat hand.
    /// Creates the visual card node in the scene tree in real time.
    /// </summary>
    public static bool DrawCardToHand(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card == null)
        {
            ModLogger.Verbose("CardDirector", "DrawCardToHand: card is null.");
            return false;
        }
        string cardName = card.GetType().Name;
        ModLogger.Verbose("CardDirector", $"DrawCardToHand called for: {cardName} (Currently in pile: {card.Pile?.Type})");
        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player?.PlayerCombatState == null)
            {
                ModLogger.Warn("Cannot draw card: not currently in combat.");
                return false;
            }

            if (card.Pile == player.PlayerCombatState.Hand)
            {
                ModLogger.Info($"Card '{cardName}' is already in combat hand.");
                return true;
            }

            bool drawn = false;
            try
            {
                _ = CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, null, false);
                drawn = true;
                ModLogger.Verbose("CardDirector", "Drawn to hand via CardPileCmd.Add.");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"CardPileCmd.Add fallback notice: {ex.Message}");
            }

            if (!drawn || card.Pile != player.PlayerCombatState.Hand)
            {
                var oldPile = card.Pile;
                if (oldPile != null)
                {
                    oldPile.RemoveInternal(card, false);
                    oldPile.InvokeContentsChanged();
                }
                else
                {
                    if (player.PlayerCombatState.DrawPile?.Cards != null && player.PlayerCombatState.DrawPile.Cards.Contains(card))
                    {
                        player.PlayerCombatState.DrawPile.RemoveInternal(card, false);
                        player.PlayerCombatState.DrawPile.InvokeContentsChanged();
                    }
                    else if (player.PlayerCombatState.DiscardPile?.Cards != null && player.PlayerCombatState.DiscardPile.Cards.Contains(card))
                    {
                        player.PlayerCombatState.DiscardPile.RemoveInternal(card, false);
                        player.PlayerCombatState.DiscardPile.InvokeContentsChanged();
                    }
                    else if (player.PlayerCombatState.ExhaustPile?.Cards != null && player.PlayerCombatState.ExhaustPile.Cards.Contains(card))
                    {
                        player.PlayerCombatState.ExhaustPile.RemoveInternal(card, false);
                        player.PlayerCombatState.ExhaustPile.InvokeContentsChanged();
                    }
                }

                player.PlayerCombatState.Hand?.AddInternal(card, -1, false);
                player.PlayerCombatState.Hand?.InvokeContentsChanged();

                if (NPlayerHand.Instance != null)
                {
                    try
                    {
                        var ncard = NCard.Create(card);
                        if (ncard != null)
                        {
                            NPlayerHand.Instance.Add(ncard, -1);
                            NPlayerHand.Instance.ForceRefreshCardIndices();
                        }
                    }
                    catch { }
                }
            }

            ModLogger.Info($"Card '{cardName}' drawn directly to combat hand. (Hand count: {player.PlayerCombatState.Hand?.Cards.Count ?? 0})");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to draw card '{cardName}' to hand", ex);
            return false;
        }
    }

    /// <summary>
    /// Force exhausts a card model immediately in real-time, moving it to the player's Exhaust pile.
    /// Supports cards in Hand, Draw, Discard, or any active combat pile.
    /// </summary>
    public static bool ExhaustCard(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card == null)
        {
            ModLogger.Verbose("CardDirector", "ExhaustCard: card is null.");
            return false;
        }

        string cardName = card.GetType().Name;
        ModLogger.Verbose("CardDirector", $"ExhaustCard called for: {cardName} (Currently in pile: {card.Pile?.Type})");

        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player?.PlayerCombatState == null)
            {
                ModLogger.Warn("Cannot exhaust card: not currently in combat.");
                return false;
            }

            var combatState = player.PlayerCombatState;

            // If card is from master deck, find active combat match
            var targetCombatCard = card;
            if (card.Pile == null || !combatState.AllPiles.Any(p => p == card.Pile))
            {
                var match = combatState.AllPiles
                    .Where(p => p?.Cards != null)
                    .SelectMany(p => p.Cards)
                    .FirstOrDefault(c => c != null && (
                        c == card ||
                        c.DeckVersion == card ||
                        c.CloneOf == card ||
                        (c.GetType() == card.GetType() && c.IsUpgraded == card.IsUpgraded)
                    ));

                if (match != null)
                {
                    targetCombatCard = match;
                }
            }

            if (targetCombatCard.Pile == combatState.ExhaustPile)
            {
                ModLogger.Info($"Card '{cardName}' is already in exhaust pile.");
                return true;
            }

            bool exhausted = false;

            // 1. Try engine CardCmd.Exhaust
            try
            {
                _ = CardCmd.Exhaust(null!, targetCombatCard, false, false);
                exhausted = true;
                ModLogger.Verbose("CardDirector", "Exhausted via CardCmd.Exhaust.");
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"CardCmd.Exhaust notice: {ex.Message}");
            }

            // 2. Try CardPileCmd.Add to Exhaust pile
            if (!exhausted || targetCombatCard.Pile != combatState.ExhaustPile)
            {
                try
                {
                    _ = CardPileCmd.Add(targetCombatCard, PileType.Exhaust, CardPilePosition.Top, null, false);
                    exhausted = true;
                    ModLogger.Verbose("CardDirector", "Exhausted via CardPileCmd.Add(PileType.Exhaust).");
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"CardPileCmd.Add(Exhaust) notice: {ex.Message}");
                }
            }

            // 3. Fallback: direct pile manipulation
            if (targetCombatCard.Pile != combatState.ExhaustPile)
            {
                var oldPile = targetCombatCard.Pile;
                if (oldPile != null)
                {
                    oldPile.RemoveInternal(targetCombatCard, false);
                    oldPile.InvokeContentsChanged();
                }
                else
                {
                    foreach (var pile in combatState.AllPiles)
                    {
                        if (pile?.Cards != null && pile.Cards.Contains(targetCombatCard))
                        {
                            pile.RemoveInternal(targetCombatCard, false);
                            pile.InvokeContentsChanged();
                        }
                    }
                }

                combatState.ExhaustPile?.AddInternal(targetCombatCard, -1, false);
                combatState.ExhaustPile?.InvokeContentsChanged();
            }

            // Clean up visual node if card was in hand
            if (NPlayerHand.Instance != null)
            {
                try
                {
                    NPlayerHand.Instance.Remove(targetCombatCard);
                    NPlayerHand.Instance.ForceRefreshCardIndices();
                }
                catch { }
            }

            ModLogger.Info($"Card '{cardName}' force exhausted to Exhaust pile. (Exhaust count: {combatState.ExhaustPile?.Cards.Count ?? 0})");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to force exhaust card '{cardName}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Force exhausts the first matching card found in the active combat piles.
    /// </summary>
    public static bool ExhaustCardFromDeck(string cardId)
    {
        ModLogger.Verbose("CardDirector", $"ExhaustCardFromDeck called for: '{cardId}'");
        if (string.IsNullOrWhiteSpace(cardId)) return false;

        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player?.PlayerCombatState != null)
            {
                foreach (var pile in player.PlayerCombatState.AllPiles)
                {
                    var combatCard = pile?.Cards?.FirstOrDefault(c => 
                        c != null && (
                            c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                            c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                        ));
                    if (combatCard != null)
                    {
                        ModLogger.Verbose("CardDirector", $"Found matching combat card to exhaust: {combatCard.GetType().Name}");
                        return ExhaustCard(combatCard);
                    }
                }
            }

            ModLogger.Warn($"Cannot exhaust card '{cardId}': not found in any active combat pile.");
            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to exhaust card '{cardId}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Enchants a specific card model with the specified enchantment in real-time,
    /// synchronizing with matching deck and combat instances.
    /// </summary>
    public static bool EnchantCard(MegaCrit.Sts2.Core.Models.CardModel card, string enchantmentId, decimal amount = 1)
    {
        if (card == null)
        {
            ModLogger.Verbose("CardDirector", "EnchantCard: card is null.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(enchantmentId)) return false;

        string cardName = card.GetType().Name;
        ModLogger.Verbose("CardDirector", $"EnchantCard called: card='{cardName}', enchantment='{enchantmentId}', amount={amount}");

        try
        {
            var canonical = GameHelper.FindCanonicalEnchantmentModel(enchantmentId);
            if (canonical == null)
            {
                ModLogger.Warn($"Enchantment '{enchantmentId}' could not be resolved.");
                return false;
            }

            var ench = GameHelper.CreateEnchantment(canonical, amount);
            if (ench != null)
            {
                try
                {
                    _ = CardCmd.Enchant(ench, card, amount);
                }
                catch
                {
                    card.EnchantInternal(ench, amount);
                }
            }
            else
            {
                card.EnchantInternal(canonical, amount);
            }

            card.Pile?.InvokeContentsChanged();

            // Sync with master deck or combat clone
            var player = GameHelper.GetActivePlayer();
            if (player != null)
            {
                if (player.Deck?.Cards != null && !player.Deck.Cards.Contains(card))
                {
                    var master = card.DeckVersion ?? card.CloneOf ?? player.Deck.Cards.FirstOrDefault(m => m == card || m.GetType() == card.GetType());
                    if (master != null)
                    {
                        var masterEnch = GameHelper.CreateEnchantment(canonical, amount) ?? canonical;
                        try { _ = CardCmd.Enchant(masterEnch, master, amount); } catch { master.EnchantInternal(masterEnch, amount); }
                        player.Deck.InvokeContentsChanged();
                    }
                }

                if (player.PlayerCombatState != null)
                {
                    var combatCard = player.PlayerCombatState.AllCards?.FirstOrDefault(c => c != null && c != card && (c == card || c.DeckVersion == card || c.CloneOf == card || c.GetType() == card.GetType()));
                    if (combatCard != null)
                    {
                        var combatEnch = GameHelper.CreateEnchantment(canonical, amount) ?? canonical;
                        try { _ = CardCmd.Enchant(combatEnch, combatCard, amount); } catch { combatCard.EnchantInternal(combatEnch, amount); }
                        combatCard.Pile?.InvokeContentsChanged();
                    }
                }
            }

            ModLogger.Info($"Card '{cardName}' successfully enchanted with '{enchantmentId}' (Amount: {amount}).");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to enchant card '{cardName}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Clears any active enchantment from the card model immediately across deck and combat piles.
    /// </summary>
    public static bool ClearEnchantment(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        if (card == null)
        {
            ModLogger.Verbose("CardDirector", "ClearEnchantment: card is null.");
            return false;
        }

        string cardName = card.GetType().Name;
        ModLogger.Verbose("CardDirector", $"ClearEnchantment called for: {cardName}");

        try
        {
            try
            {
                CardCmd.ClearEnchantment(card);
            }
            catch
            {
                card.ClearEnchantmentInternal();
            }

            card.Pile?.InvokeContentsChanged();

            var player = GameHelper.GetActivePlayer();
            if (player != null)
            {
                if (player.Deck?.Cards != null && !player.Deck.Cards.Contains(card))
                {
                    var master = card.DeckVersion ?? card.CloneOf ?? player.Deck.Cards.FirstOrDefault(m => m == card || m.GetType() == card.GetType());
                    if (master != null)
                    {
                        try { CardCmd.ClearEnchantment(master); } catch { master.ClearEnchantmentInternal(); }
                        player.Deck.InvokeContentsChanged();
                    }
                }

                if (player.PlayerCombatState != null)
                {
                    var combatCard = player.PlayerCombatState.AllCards?.FirstOrDefault(c => c != null && c != card && (c == card || c.DeckVersion == card || c.CloneOf == card || c.GetType() == card.GetType()));
                    if (combatCard != null)
                    {
                        try { CardCmd.ClearEnchantment(combatCard); } catch { combatCard.ClearEnchantmentInternal(); }
                        combatCard.Pile?.InvokeContentsChanged();
                    }
                }
            }

            ModLogger.Info($"Enchantment cleared from card '{cardName}'.");
            OnDeckChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to clear enchantment on card '{cardName}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Enchants the first matching card found in the player's deck or combat piles.
    /// </summary>
    public static bool EnchantCardInDeck(string cardId, string enchantmentId, decimal amount = 1)
    {
        if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(enchantmentId)) return false;
        var player = GameHelper.GetActivePlayer();

        if (player?.Deck?.Cards != null)
        {
            var card = player.Deck.Cards.FirstOrDefault(c => 
                c != null && (
                    c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                    c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                ));
            if (card != null)
            {
                return EnchantCard(card, enchantmentId, amount);
            }
        }

        if (player?.PlayerCombatState != null)
        {
            foreach (var pile in player.PlayerCombatState.AllPiles)
            {
                var combatCard = pile?.Cards?.FirstOrDefault(c => 
                    c != null && (
                        c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    ));
                if (combatCard != null)
                {
                    return EnchantCard(combatCard, enchantmentId, amount);
                }
            }
        }

        ModLogger.Warn($"Card '{cardId}' not found to enchant.");
        return false;
    }

    /// <summary>
    /// Clears enchantment on the first matching card found in the player's deck or combat piles.
    /// </summary>
    public static bool ClearEnchantmentInDeck(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return false;
        var player = GameHelper.GetActivePlayer();

        if (player?.Deck?.Cards != null)
        {
            var card = player.Deck.Cards.FirstOrDefault(c => 
                c != null && (
                    c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                    c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                ));
            if (card != null)
            {
                return ClearEnchantment(card);
            }
        }

        if (player?.PlayerCombatState != null)
        {
            foreach (var pile in player.PlayerCombatState.AllPiles)
            {
                var combatCard = pile?.Cards?.FirstOrDefault(c => 
                    c != null && (
                        c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    ));
                if (combatCard != null)
                {
                    return ClearEnchantment(combatCard);
                }
            }
        }

        ModLogger.Warn($"Card '{cardId}' not found to clear enchantment.");
        return false;
    }
}
