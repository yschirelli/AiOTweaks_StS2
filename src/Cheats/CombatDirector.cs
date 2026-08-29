using System;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;

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
        ModLogger.Verbose("CombatDirector", "KillAllEnemies: Invoked. Dispatching 'kill' command to DevConsole...");
        try
        {
            GameHelper.ExecuteConsoleCommand("kill");
            ModLogger.Info("Executed Kill All Enemies command via DevConsole.");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to execute Kill All Enemies.", ex);
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
            ModLogger.Error("Failed to execute End Turn.", ex);
        }
    }
}


