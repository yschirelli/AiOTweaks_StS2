using System;
using System.Linq;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using MegaCrit.Sts2.Core.ValueProps;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for combat sandbox controls, cheats, and turn manipulation.
/// </summary>
public static class CombatDirector
{
    public static event Action<bool>? OnGodModeToggled;
    public static event Action<bool>? OnInfiniteEnergyToggled;
    public static event Action<bool>? OnOneHitKillToggled;

    public static void ToggleGodMode()
    {
        bool oldVal = RuntimeStateManager.GodModeEnabled;
        RuntimeStateManager.GodModeEnabled = !oldVal;
        RuntimeStateManager.SetCheatFlag("GodMode", RuntimeStateManager.GodModeEnabled);
        ModLogger.Verbose("CombatDirector", $"ToggleGodMode: {oldVal} -> {RuntimeStateManager.GodModeEnabled}");
        ModLogger.Info($"God Mode: {(RuntimeStateManager.GodModeEnabled ? "ENABLED" : "DISABLED")}");

        GameHelper.ExecuteConsoleCommand("god");
        OnGodModeToggled?.Invoke(RuntimeStateManager.GodModeEnabled);
    }

    public static void ToggleInfiniteEnergy()
    {
        bool oldVal = RuntimeStateManager.InfiniteEnergyEnabled;
        RuntimeStateManager.InfiniteEnergyEnabled = !oldVal;
        RuntimeStateManager.SetCheatFlag("InfiniteEnergy", RuntimeStateManager.InfiniteEnergyEnabled);
        ModLogger.Verbose("CombatDirector", $"ToggleInfiniteEnergy: {oldVal} -> {RuntimeStateManager.InfiniteEnergyEnabled}");
        ModLogger.Info($"Infinite Energy: {(RuntimeStateManager.InfiniteEnergyEnabled ? "ENABLED" : "DISABLED")}");
        OnInfiniteEnergyToggled?.Invoke(RuntimeStateManager.InfiniteEnergyEnabled);
    }

    public static void ToggleOneHitKill()
    {
        bool oldVal = RuntimeStateManager.OneHitKillEnabled;
        RuntimeStateManager.OneHitKillEnabled = !oldVal;
        RuntimeStateManager.SetCheatFlag("OneHitKill", RuntimeStateManager.OneHitKillEnabled);
        ModLogger.Verbose("CombatDirector", $"ToggleOneHitKill: {oldVal} -> {RuntimeStateManager.OneHitKillEnabled}");
        ModLogger.Info($"One Hit Kill: {(RuntimeStateManager.OneHitKillEnabled ? "ENABLED" : "DISABLED")}");
        OnOneHitKillToggled?.Invoke(RuntimeStateManager.OneHitKillEnabled);
    }

    public static void KillAllEnemies()
    {
        ModLogger.Verbose("CombatDirector", "KillAllEnemies: Invoked. Attempting to eliminate all active combat enemies with animations and instant win resolution...");
        try
        {
            var enemies = GameHelper.GetActiveCombatEnemies()?.Where(e => e != null && !e.IsDead && e.CurrentHp > 0).ToList();
            if (enemies != null && enemies.Count > 0)
            {
                MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(KillEnemiesAsync(enemies));
                ModLogger.Info($"Executed lethal kill on {enemies.Count} active enemies with death animation and win condition check.");
                return;
            }

            // Fallback to dev console if combat state not resolved
            GameHelper.ExecuteConsoleCommand("kill all");
            ModLogger.Info("Executed 'kill all' command via DevConsole (fallback).");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to execute direct Kill All Enemies, attempting dev console fallback.", ex);
            GameHelper.ExecuteConsoleCommand("kill all");
        }
    }

    private static async System.Threading.Tasks.Task KillEnemiesAsync(System.Collections.Generic.List<MegaCrit.Sts2.Core.Entities.Creatures.Creature> enemies)
    {
        foreach (var enemy in enemies)
        {
            if (enemy != null && !enemy.IsDead)
            {
                await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(enemy);
            }
        }
        if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance != null)
        {
            await MegaCrit.Sts2.Core.Combat.CombatManager.Instance.CheckWinCondition();
        }
    }

    public static void KillEnemy(MegaCrit.Sts2.Core.Entities.Creatures.Creature enemy)
    {
        if (enemy == null || enemy.IsDead) return;
        ModLogger.Verbose("CombatDirector", $"KillEnemy: Invoked for {enemy.Monster?.Id.Entry ?? enemy.GetType().Name}...");
        try
        {
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(KillSingleEnemyAsync(enemy));
            ModLogger.Info($"Executed lethal kill on enemy {enemy.Monster?.Id.Entry ?? enemy.GetType().Name} with death animation and win condition check.");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to execute direct Kill Enemy.", ex);
        }
    }

    private static async System.Threading.Tasks.Task KillSingleEnemyAsync(MegaCrit.Sts2.Core.Entities.Creatures.Creature enemy)
    {
        if (enemy != null && !enemy.IsDead)
        {
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(enemy);
        }
        if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance != null)
        {
            await MegaCrit.Sts2.Core.Combat.CombatManager.Instance.CheckWinCondition();
        }
    }

    public static void AddEnergy(int amount)
    {
        ModLogger.Verbose("CombatDirector", $"AddEnergy: amount={amount}. Dispatching 'energy {amount}' to DevConsole...");
        try
        {
            GameHelper.ExecuteConsoleCommand($"energy {amount}");
            ModLogger.Info($"Added {amount} Energy via DevConsole.");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to add {amount} energy.", ex);
        }
    }

    public static void DrawCards(int count)
    {
        ModLogger.Verbose("CombatDirector", $"DrawCards: count={count}. InCombat={GameHelper.IsInCombat()}. Dispatching 'draw {count}' to DevConsole...");
        try
        {
            GameHelper.ExecuteConsoleCommand($"draw {count}");
            ModLogger.Info($"Requested draw of {count} cards via DevConsole.");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to draw {count} cards.", ex);
        }
    }

    public static void EndTurn()
    {
        ModLogger.Verbose("CombatDirector", "EndTurn: Invoked. Dispatching 'endturn' to DevConsole...");
        try
        {
            GameHelper.ExecuteConsoleCommand("endturn");
            ModLogger.Info("Executed End Turn command via DevConsole.");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to end turn.", ex);
        }
    }

    public static void RefreshCombatIntents()
    {
        GameHelper.RefreshCombatIntents();
    }
}
