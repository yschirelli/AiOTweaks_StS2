using System;
using System.Linq;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for inspecting, granting, and removing relics at runtime.
/// Directly grants relics via RelicCmd to properly initialize hooks and UI icons.
/// </summary>
public static class RelicDirector
{
    public static event Action<string>? OnRelicAdded;
    public static event Action<string>? OnRelicRemoved;
    public static event Action? OnRelicsChanged;

    /// <summary>
    /// Adds a relic directly to the active player's inventory.
    /// Uses RelicCmd.Obtain to properly initialize relic hooks, state, and NRelicInventory UI nodes.
    /// </summary>
    public static bool AddRelic(string relicId, int counter = 0)
    {
        ModLogger.Verbose("RelicDirector", $"AddRelic called: relicId='{relicId}', counter={counter}");
        if (string.IsNullOrWhiteSpace(relicId))
        {
            ModLogger.Warn("Cannot add relic with null or empty ID.");
            return false;
        }

        if (relicId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("RelicDirector", "AddRelic: Batch adding all available relics to inventory...");
            var allRelics = GameHelper.GetAllRelicIds();
            foreach (var rel in allRelics)
            {
                AddRelic(rel, counter);
            }
            OnRelicsChanged?.Invoke();
            return true;
        }

        try
        {
            var player = GameHelper.GetActivePlayer();
            var canonical = GameHelper.FindCanonicalRelicModel(relicId);
            ModLogger.Verbose("RelicDirector", $"Player resolved: {player != null}, Canonical relic model resolved: {canonical?.GetType().Name ?? "null"}");

            if (player != null && canonical != null)
            {
                var relicInstance = GameHelper.CreateRelicForPlayer(canonical, player);
                if (relicInstance != null)
                {
                    if (counter > 0)
                    {
                        try
                        {
                            var counterProp = relicInstance.GetType().GetProperty("Counter") ?? relicInstance.GetType().GetProperty("Count");
                            if (counterProp != null && counterProp.CanWrite)
                            {
                                counterProp.SetValue(relicInstance, counter);
                                ModLogger.Verbose("RelicDirector", $"Set relic counter property to {counter}.");
                            }
                        }
                        catch { }
                    }

                    // RelicCmd.Obtain correctly attaches to player, sets up hooks, and fires RelicObtained for NRelicInventory
                    ModLogger.Verbose("RelicDirector", $"Calling RelicCmd.Obtain for '{relicInstance.GetType().Name}'...");
                    var obtained = RelicCmd.Obtain(relicInstance, player, -1);
                    if (obtained != null)
                    {
                        string resName = canonical.GetType().Name;
                        ModLogger.Info($"Relic '{resName}' successfully obtained via RelicCmd.Obtain. (Player relics count: {player.Relics.Count})");
                        OnRelicAdded?.Invoke(resName);
                        OnRelicsChanged?.Invoke();
                        return true;
                    }
                    else
                    {
                        ModLogger.Verbose("RelicDirector", "RelicCmd.Obtain returned null.");
                    }
                }
            }

            // Fallback: If player not yet in an active run, queue for next run startup/reward injection
            ModLogger.Verbose("RelicDirector", $"Active player not ready; queueing reward relic '{relicId}' in RuntimeStateManager.");
            RuntimeStateManager.QueueRewardRelic(relicId);
            ModLogger.Warn($"No active player run detected; relic '{relicId}' queued for next run/combat.");
            OnRelicAdded?.Invoke(relicId);
            OnRelicsChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to add relic '{relicId}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes a relic from the active player's inventory by type name, entry ID, or title.
    /// Uses RelicCmd.Remove to properly deactivate hooks and clean up NRelicInventory UI nodes.
    /// </summary>
    public static bool RemoveRelic(string relicId)
    {
        ModLogger.Verbose("RelicDirector", $"RemoveRelic called: relicId='{relicId}'");
        if (string.IsNullOrWhiteSpace(relicId))
        {
            ModLogger.Warn("Cannot remove relic with null or empty ID.");
            return false;
        }

        var player = GameHelper.GetActivePlayer();

        if (relicId.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("RelicDirector", "RemoveRelic: Removing ALL relics from player inventory...");
            if (player?.Relics != null)
            {
                var allPlayerRelics = player.Relics.ToList();
                foreach (var r in allPlayerRelics)
                {
                    try
                    {
                        RelicCmd.Remove(r);
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Warn($"Error removing relic '{r?.GetType().Name}': {ex.Message}");
                    }
                }
                ModLogger.Info("All player relics removed via RelicCmd.Remove.");
                OnRelicRemoved?.Invoke("all");
                OnRelicsChanged?.Invoke();
                return true;
            }
            return false;
        }

        try
        {
            if (player != null && player.Relics != null)
            {
                var matchingRelic = player.Relics.FirstOrDefault(r => 
                    r != null && (
                        r.GetType().Name.Equals(relicId, StringComparison.OrdinalIgnoreCase) ||
                        r.Id.Entry.Equals(relicId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(r.Title.GetFormattedText()) && r.Title.GetFormattedText().Equals(relicId, StringComparison.OrdinalIgnoreCase))
                    ));

                if (matchingRelic != null)
                {
                    ModLogger.Verbose("RelicDirector", $"Found matching relic in inventory: {matchingRelic.GetType().Name}. Calling RelicCmd.Remove...");
                    RelicCmd.Remove(matchingRelic);
                    string rName = matchingRelic.GetType().Name;
                    ModLogger.Info($"Relic '{rName}' successfully removed via RelicCmd.Remove. (Remaining: {player.Relics.Count})");
                    OnRelicRemoved?.Invoke(rName);
                    OnRelicsChanged?.Invoke();
                    return true;
                }
                else
                {
                    ModLogger.Warn($"Relic '{relicId}' was not found in player's current relics ({player.Relics.Count} checked).");
                    return false;
                }
            }

            ModLogger.Warn($"Cannot remove relic '{relicId}': player or player.Relics is null.");
            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to remove relic '{relicId}'", ex);
            return false;
        }
    }
}


