using System;
using System.Linq;
using System.Collections.Generic;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for inspecting, granting, discarding, and managing player potions and potion slots at runtime.
/// </summary>
public static class PotionDirector
{
    public static event Action<string>? OnPotionAdded;
    public static event Action<string>? OnPotionRemoved;
    public static event Action? OnPotionsChanged;
    public static event Action<int>? OnPotionSlotsChanged;

    /// <summary>
    /// Adds a potion to the active player's inventory.
    /// </summary>
    public static bool AddPotion(string potionId, int slotIndex = -1)
    {
        ModLogger.Verbose("PotionDirector", $"AddPotion called: potionId='{potionId}', slotIndex={slotIndex}");
        if (string.IsNullOrWhiteSpace(potionId))
        {
            ModLogger.Warn("Cannot add potion with null or empty ID.");
            return false;
        }

        if (potionId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("PotionDirector", "AddPotion: Batch adding potions until inventory slots are full...");
            var allPotions = GameHelper.GetAllPotionInfos();
            var player = GameHelper.GetActivePlayer();
            if (player != null)
            {
                int added = 0;
                foreach (var info in allPotions)
                {
                    if (!player.HasOpenPotionSlots) break;
                    if (AddPotion(info.TypeName))
                    {
                        added++;
                    }
                }
                ModLogger.Info($"Batch procured {added} potions to fill player inventory.");
                OnPotionsChanged?.Invoke();
                return true;
            }
            return false;
        }

        try
        {
            var player = GameHelper.GetActivePlayer();
            var canonical = GameHelper.FindCanonicalPotionModel(potionId);
            ModLogger.Verbose("PotionDirector", $"Player resolved: {player != null}, Canonical potion model resolved: {canonical?.GetType().Name ?? "null"}");

            if (player != null && canonical != null)
            {
                var mutable = canonical.ToMutable();
                if (mutable != null)
                {
                    ModLogger.Verbose("PotionDirector", $"Calling PotionCmd.TryToProcure for '{mutable.GetType().Name}'...");
                    var procureTask = PotionCmd.TryToProcure(mutable, player, slotIndex);
                    
                    // Direct fallback if TryToProcure didn't place it
                    if (player.GetPotionSlotIndex(mutable) == -1 && player.HasOpenPotionSlots)
                    {
                        player.AddPotionInternal(mutable, slotIndex, false);
                    }

                    string pName = !string.IsNullOrWhiteSpace(canonical.Title?.GetFormattedText()) 
                        ? canonical.Title.GetFormattedText() 
                        : canonical.GetType().Name;
                    ModLogger.Info($"Potion '{pName}' successfully added to player inventory.");
                    OnPotionAdded?.Invoke(canonical.GetType().Name);
                    OnPotionsChanged?.Invoke();
                    return true;
                }
            }

            // Fallback via DevConsole
            ModLogger.Verbose("PotionDirector", $"Dispatching DevConsole command 'potion {potionId}'");
            GameHelper.ExecuteConsoleCommand($"potion {potionId}");
            OnPotionAdded?.Invoke(potionId);
            OnPotionsChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to add potion '{potionId}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Discards/removes a specific active potion from the player's inventory.
    /// </summary>
    public static bool RemovePotion(PotionModel potion)
    {
        if (potion == null) return false;
        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player != null)
            {
                string pName = potion.GetType().Name;
                ModLogger.Verbose("PotionDirector", $"Discarding potion '{pName}' via player.DiscardPotionInternal...");
                player.DiscardPotionInternal(potion, false);
                ModLogger.Info($"Discarded potion '{pName}'.");
                OnPotionRemoved?.Invoke(pName);
                OnPotionsChanged?.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to remove potion instance", ex);
        }
        return false;
    }

    /// <summary>
    /// Discards/removes a potion at a specific slot index.
    /// </summary>
    public static bool RemovePotionAt(int slotIndex)
    {
        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player != null && slotIndex >= 0 && slotIndex < player.MaxPotionCount)
            {
                var potion = player.GetPotionAtSlotIndex(slotIndex);
                if (potion != null)
                {
                    return RemovePotion(potion);
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to remove potion at slot {slotIndex}", ex);
        }
        return false;
    }

    /// <summary>
    /// Discards all potions in the player's inventory.
    /// </summary>
    public static bool ClearAllPotions()
    {
        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player != null)
            {
                var potions = player.Potions.ToList();
                foreach (var p in potions)
                {
                    if (p != null)
                    {
                        player.DiscardPotionInternal(p, false);
                    }
                }
                ModLogger.Info($"Cleared all ({potions.Count}) potions from player inventory.");
                OnPotionRemoved?.Invoke("all");
                OnPotionsChanged?.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to clear all potions", ex);
        }
        return false;
    }

    /// <summary>
    /// Sets the maximum number of potion slots for the player in real-time.
    /// </summary>
    public static void SetMaxPotionSlots(int slots)
    {
        int safeSlots = Math.Clamp(slots, 1, 10);
        GameHelper.SetPlayerMaxPotionSlots(safeSlots);
        OnPotionSlotsChanged?.Invoke(safeSlots);
        OnPotionsChanged?.Invoke();
    }

    /// <summary>
    /// Gets the current maximum potion slots (in-run or from configuration).
    /// </summary>
    public static int GetMaxPotionSlots()
    {
        return GameHelper.GetPlayerMaxPotionSlots();
    }

    /// <summary>
    /// Gets all active potion slots for the player (including null/empty slots).
    /// </summary>
    public static IReadOnlyList<PotionModel?>? GetCurrentPotionSlots()
    {
        return GameHelper.GetActivePlayerPotionSlots();
    }
}
