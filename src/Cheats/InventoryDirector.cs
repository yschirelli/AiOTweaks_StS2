using System;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Players;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for currency, potions, and health manipulation.
/// </summary>
public static class InventoryDirector
{
    public static event Action<int>? OnGoldModified;
    public static event Action<int, int>? OnHealthModified;

    public static Player? GetActivePlayer()
    {
        return GameHelper.GetActivePlayer();
    }

    public static void AddGold(int amount)
    {
        ModLogger.Verbose("InventoryDirector", $"AddGold called: amount={amount}");
        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                int oldGold = player.Gold;
                player.Gold = Math.Max(0, player.Gold + amount);
                ModLogger.Verbose("InventoryDirector", $"Updated player.Gold: {oldGold} -> {player.Gold}");
                ModLogger.Info($"Added {amount} Gold to player inventory (Total: {player.Gold}).");
                OnGoldModified?.Invoke(amount);
                return;
            }

            // Fallback via DevConsole
            ModLogger.Verbose("InventoryDirector", $"Active player not found; dispatching DevConsole command 'gold {amount}'");
            GameHelper.ExecuteConsoleCommand($"gold {amount}");
            ModLogger.Info($"AddGold executed via DevConsole ({amount}).");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to add gold ({amount})", ex);
        }
    }

    public static void SetGold(int amount)
    {
        int safeAmount = Math.Max(0, amount);
        ModLogger.Verbose("InventoryDirector", $"SetGold called: amount={amount} (safe={safeAmount})");
        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                int oldGold = player.Gold;
                player.Gold = safeAmount;
                ModLogger.Verbose("InventoryDirector", $"Set player.Gold: {oldGold} -> {player.Gold}");
                ModLogger.Info($"Set player Gold to {safeAmount}.");
                OnGoldModified?.Invoke(safeAmount);
                return;
            }

            ModLogger.Verbose("InventoryDirector", $"Active player not found; dispatching DevConsole command 'gold {safeAmount}'");
            GameHelper.ExecuteConsoleCommand($"gold {safeAmount}");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to set gold to {amount}", ex);
        }
    }

    public static void Heal(int amount)
    {
        ModLogger.Verbose("InventoryDirector", $"Heal called: amount={amount}");
        try
        {
            if (amount <= 0)
            {
                ModLogger.Verbose("InventoryDirector", "Heal: amount is <= 0. Skipping.");
                return;
            }

            var player = GetActivePlayer();
            if (player?.Creature != null)
            {
                ModLogger.Verbose("InventoryDirector", $"Healing player creature ({player.Creature.CurrentHp}/{player.Creature.MaxHp}) by {amount} HP...");
                GameHelper.ModifyCreatureHealth(player.Creature, amount);
                ModLogger.Info($"Healed player for {amount} HP (Current: {player.Creature.CurrentHp}/{player.Creature.MaxHp}).");
                OnHealthModified?.Invoke(player.Creature.CurrentHp, player.Creature.MaxHp);
                return;
            }

            ModLogger.Verbose("InventoryDirector", $"Player creature not found; dispatching DevConsole command 'heal {amount}'");
            GameHelper.ExecuteConsoleCommand($"heal {amount}");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to heal player for {amount}", ex);
        }
    }

    public static void DamagePlayer(int amount)
    {
        ModLogger.Verbose("InventoryDirector", $"DamagePlayer called: amount={amount}");
        try
        {
            if (amount <= 0)
            {
                ModLogger.Verbose("InventoryDirector", "DamagePlayer: amount is <= 0. Skipping.");
                return;
            }

            var player = GetActivePlayer();
            if (player?.Creature != null)
            {
                var creature = player.Creature;
                ModLogger.Verbose("InventoryDirector", $"Damaging player creature ({creature.CurrentHp}/{creature.MaxHp}) by {amount} HP (InCombat={GameHelper.IsInCombat()})...");
                if (GameHelper.IsInCombat())
                {
                    try
                    {
                        var choiceCtx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                        var task = MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                            choiceCtx, 
                            creature, 
                            (decimal)amount, 
                            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, 
                            creature);

                        task.ContinueWith(t =>
                        {
                            if (t.IsFaulted && t.Exception != null)
                            {
                                ModLogger.Warn($"CreatureCmd.Damage async fault: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                            }
                        });
                        ModLogger.Verbose("InventoryDirector", "Applied damage via CreatureCmd.Damage.");
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Warn($"CreatureCmd.Damage direct exception: {ex.Message}");
                        creature.LoseHpInternal((decimal)amount, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered);
                        ModLogger.Verbose("InventoryDirector", "Applied damage fallback via creature.LoseHpInternal.");
                    }
                }
                else
                {
                    int newHp = Math.Max(1, creature.CurrentHp - amount);
                    creature.SetCurrentHpInternal(newHp);
                    ModLogger.Verbose("InventoryDirector", $"Out-of-combat HP adjusted to {newHp}.");
                }

                GameHelper.RefreshHealthUi(creature);
                ModLogger.Info($"Damaged player for {amount} HP (Current: {creature.CurrentHp}/{creature.MaxHp}).");
                OnHealthModified?.Invoke(creature.CurrentHp, creature.MaxHp);
                return;
            }

            ModLogger.Verbose("InventoryDirector", $"Player creature not found; dispatching DevConsole command 'damage {amount}'");
            GameHelper.ExecuteConsoleCommand($"damage {amount}");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to damage player for {amount}", ex);
        }
    }

    public static void SetMaxHp(int amount)
    {
        ModLogger.Verbose("InventoryDirector", $"SetMaxHp called: amount={amount}");
        try
        {
            int safeAmount = Math.Max(1, amount);
            var player = GetActivePlayer();
            if (player?.Creature != null)
            {
                ModLogger.Verbose("InventoryDirector", $"Setting player creature Max HP to {safeAmount} (Current Max HP: {player.Creature.MaxHp})...");
                GameHelper.SetCreatureHealthExact(player.Creature, Math.Min(player.Creature.CurrentHp, safeAmount), safeAmount);
                ModLogger.Info($"Set player Max HP to {safeAmount} (Current: {player.Creature.CurrentHp}/{player.Creature.MaxHp}).");
                OnHealthModified?.Invoke(player.Creature.CurrentHp, player.Creature.MaxHp);
            }
            else
            {
                ModLogger.Verbose("InventoryDirector", "SetMaxHp: Player or creature is null.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to set Max HP to {amount}", ex);
        }
    }

    /// <summary>
    /// Opens the interactive randomized shop overlay anywhere.
    /// </summary>
    public static bool OpenShopMenu()
    {
        return GameHelper.OpenShopMenu();
    }
}


