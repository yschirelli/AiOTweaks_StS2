using System;
using System.Collections.Generic;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Autonomous gameplay invariant monitor.
/// Verifies core game rules and state boundaries at turn and action boundaries,
/// immediately catching corrupted numerical states, leaks, or illegal values.
/// Only active in DEBUG configuration.
/// </summary>
public static class GameplayInvariantMonitor
{
#if DEBUG
    private static int _combatStartDeckTotal = -1;

    public static void RecordCombatStartDeckSize(int count)
    {
        _combatStartDeckTotal = count;
        BreadcrumbTracker.Record("Invariant", $"Initial combat deck size registered: {count}");
    }

    public static void ResetCombatTracking()
    {
        _combatStartDeckTotal = -1;
    }

    public static void CheckInvariants(string eventContext)
    {
        try
        {
            if (!GameHelper.IsInCombat()) return;

            var player = GameHelper.GetActivePlayer();
            var creature = player?.Creature;
            if (creature == null) return;

            // Invariant 1: Player Block must be >= 0
            if (creature.Block < 0)
            {
                ReportViolation(eventContext, $"Player Block is negative: {creature.Block}");
            }

            // Invariant 2: Player HP sanity
            if (creature.CurrentHp > creature.MaxHp)
            {
                ReportViolation(eventContext, $"Player HP exceeded MaxHP: {creature.CurrentHp}/{creature.MaxHp}");
            }

            // Invariant 3: Player Energy must be >= 0
            if (player?.PlayerCombatState != null && player.PlayerCombatState.Energy < 0)
            {
                ReportViolation(eventContext, $"Player Energy is negative: {player.PlayerCombatState.Energy}");
            }

            // Invariant 4: Status power stacks sanity
            var powers = GameHelper.GetCreatureActivePowers(creature);
            if (powers != null)
            {
                foreach (var p in powers)
                {
                    if (p != null && !p.AllowNegative && p.Amount < 0)
                    {
                        ReportViolation(eventContext, $"Player status '{p.Id.Entry ?? p.GetType().Name}' has illegal negative stack: {p.Amount}");
                    }
                }
            }

            // Invariant 5: Enemy health bounds
            var enemies = GameHelper.GetActiveCombatEnemies();
            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy != null && !enemy.IsDead)
                    {
                        if (enemy.Block < 0)
                        {
                            ReportViolation(eventContext, $"Enemy '{enemy.GetType().Name}' has negative Block: {enemy.Block}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[GameplayInvariantMonitor] CheckInvariants notice: {ex.Message}");
        }
    }

    private static void ReportViolation(string context, string details)
    {
        ModLogger.Error($"[INVARIANT VIOLATION @ '{context}'] {details}");
        BreadcrumbTracker.Record("InvariantViolation", $"[{context}] {details}");
    }
#else
    public static void RecordCombatStartDeckSize(int count) { }
    public static void ResetCombatTracking() { }
    public static void CheckInvariants(string eventContext) { }
#endif
}
