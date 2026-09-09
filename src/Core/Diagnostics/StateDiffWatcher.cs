using System;
using System.Collections.Generic;
using System.Linq;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Core.Diagnostics;

#if DEBUG
public record EntityState(int Hp, int MaxHp, int Block, Dictionary<string, int> Statuses);

public record CombatSnapshot(
    int PlayerHp,
    int PlayerMaxHp,
    int PlayerBlock,
    int PlayerEnergy,
    int HandCount,
    int DrawCount,
    int DiscardCount,
    int ExhaustCount,
    Dictionary<string, int> PlayerStatuses,
    Dictionary<int, EntityState> Enemies
);

public class ActionScope
{
    public int? ExpectedEnergyDelta { get; set; }
    public bool AllowsPlayerHpChange { get; set; } = false;
    public bool AllowsPlayerBlockChange { get; set; } = false;
    public HashSet<int> TargetEnemyIndices { get; } = new();
}
#endif

/// <summary>
/// State snapshot and delta diffing system.
/// Catches unintended state mutations, side-effects on untargeted entities, or numerical regressions.
/// Only active in DEBUG configuration.
/// </summary>
public static class StateDiffWatcher
{
#if DEBUG
    public static CombatSnapshot? CaptureSnapshot()
    {
        try
        {
            if (!GameHelper.IsInCombat()) return null;

            var player = GameHelper.GetActivePlayer();
            var creature = player?.Creature;
            if (creature == null) return null;

            var playerStatuses = new Dictionary<string, int>();
            var powers = GameHelper.GetCreatureActivePowers(creature);
            if (powers != null)
            {
                foreach (var p in powers)
                {
                    if (p != null)
                    {
                        string id = p.Id.Entry ?? p.GetType().Name;
                        playerStatuses[id] = p.Amount;
                    }
                }
            }

            var enemies = new Dictionary<int, EntityState>();
            var activeEnemies = GameHelper.GetActiveCombatEnemies();
            if (activeEnemies != null)
            {
                for (int i = 0; i < activeEnemies.Count; i++)
                {
                    var e = activeEnemies[i];
                    if (e == null || e.IsDead) continue;

                    var enemyStatuses = new Dictionary<string, int>();
                    var ep = GameHelper.GetCreatureActivePowers(e);
                    if (ep != null)
                    {
                        foreach (var p in ep)
                        {
                            if (p != null)
                            {
                                string id = p.Id.Entry ?? p.GetType().Name;
                                enemyStatuses[id] = p.Amount;
                            }
                        }
                    }

                    enemies[i] = new EntityState(e.CurrentHp, e.MaxHp, e.Block, enemyStatuses);
                }
            }

            int energy = player?.PlayerCombatState?.Energy ?? 0;
            int hand = player?.PlayerCombatState?.Hand?.Cards?.Count ?? 0;
            int draw = player?.PlayerCombatState?.DrawPile?.Cards?.Count ?? 0;
            int discard = player?.PlayerCombatState?.DiscardPile?.Cards?.Count ?? 0;
            int exhaust = player?.PlayerCombatState?.ExhaustPile?.Cards?.Count ?? 0;

            return new CombatSnapshot(
                PlayerHp: creature.CurrentHp,
                PlayerMaxHp: creature.MaxHp,
                PlayerBlock: creature.Block,
                PlayerEnergy: energy,
                HandCount: hand,
                DrawCount: draw,
                DiscardCount: discard,
                ExhaustCount: exhaust,
                PlayerStatuses: playerStatuses,
                Enemies: enemies
            );
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[StateDiffWatcher] CaptureSnapshot error: {ex.Message}");
            return null;
        }
    }

    public static void AuditAction(string actionName, CombatSnapshot? before, ActionScope? expectedScope = null)
    {
        if (before == null) return;
        var after = CaptureSnapshot();
        if (after == null) return;

        expectedScope ??= new ActionScope();
        var violations = new List<string>();

        // 1. Audit Energy
        int energyDelta = after.PlayerEnergy - before.PlayerEnergy;
        if (expectedScope.ExpectedEnergyDelta.HasValue && energyDelta != expectedScope.ExpectedEnergyDelta.Value)
        {
            violations.Add($"Energy changed by {energyDelta} (expected {expectedScope.ExpectedEnergyDelta.Value})");
        }

        // 2. Audit Player HP
        int hpDelta = after.PlayerHp - before.PlayerHp;
        if (!expectedScope.AllowsPlayerHpChange && hpDelta != 0)
        {
            violations.Add($"Unintended Player HP change: {before.PlayerHp} -> {after.PlayerHp} ({hpDelta:+#;-#;0})");
        }

        // 3. Audit Player Block
        int blockDelta = after.PlayerBlock - before.PlayerBlock;
        if (!expectedScope.AllowsPlayerBlockChange && blockDelta != 0)
        {
            violations.Add($"Unintended Player Block change: {before.PlayerBlock} -> {after.PlayerBlock} ({blockDelta:+#;-#;0})");
        }

        // 4. Audit Untargeted Enemies
        foreach (var (idx, prevEnemy) in before.Enemies)
        {
            if (!after.Enemies.TryGetValue(idx, out var currentEnemy))
            {
                if (!expectedScope.TargetEnemyIndices.Contains(idx))
                {
                    violations.Add($"Untargeted Enemy [{idx}] disappeared or died!");
                }
                continue;
            }

            if (!expectedScope.TargetEnemyIndices.Contains(idx))
            {
                if (currentEnemy.Hp != prevEnemy.Hp)
                {
                    violations.Add($"Untargeted Enemy [{idx}] HP changed: {prevEnemy.Hp} -> {currentEnemy.Hp}");
                }
                if (currentEnemy.Block != prevEnemy.Block)
                {
                    violations.Add($"Untargeted Enemy [{idx}] Block changed: {prevEnemy.Block} -> {currentEnemy.Block}");
                }
            }
        }

        if (violations.Count > 0)
        {
            string violationDetails = string.Join("\n - ", violations);
            ModLogger.Error($"[REGRESSION DETECTED] Action '{actionName}' caused {violations.Count} unintended state change(s):\n - {violationDetails}");
            BreadcrumbTracker.Record("Violation", $"Action '{actionName}' regression: {violationDetails}");
        }
    }
#endif
}
