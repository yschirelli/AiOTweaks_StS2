using System;
using System.Collections.Generic;
using System.Text;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Autonomous per-turn combat metrics tracker.
/// Tracks and displays player HP, damage taken, damage absorbed by block, block generated,
/// energy consumption, cards played, and active status effect snapshots at each turn boundary.
/// Active exclusively in DEBUG configuration.
/// </summary>
public static class TurnMetricsTracker
{
#if DEBUG
    private static int _currentTurnNumber = 0;
    private static bool _inTurn = false;

    private static int _turnStartHp = 0;
    private static int _turnStartBlock = 0;
    private static decimal _totalDirectDamageTaken = 0;
    private static decimal _totalDamageAbsorbedByBlock = 0;
    private static decimal _totalBlockGained = 0;
    private static int _totalEnergySpent = 0;
    private static readonly List<string> _cardsPlayedThisTurn = new();

    public static int CurrentTurnNumber => _currentTurnNumber;

    public static void OnTurnStart()
    {
        // If we were already in a turn, finish and report that turn first
        if (_inTurn)
        {
            EmitTurnReport();
        }

        _inTurn = true;
        _currentTurnNumber++;

        var player = GameHelper.GetActivePlayer();
        _turnStartHp = player?.Creature?.CurrentHp ?? 0;
        _turnStartBlock = player?.Creature?.Block ?? 0;

        _totalDirectDamageTaken = 0;
        _totalDamageAbsorbedByBlock = 0;
        _totalBlockGained = 0;
        _totalEnergySpent = 0;
        _cardsPlayedThisTurn.Clear();

        BreadcrumbTracker.Record("Combat", $"Turn {_currentTurnNumber} started. Initial HP: {_turnStartHp}, Block: {_turnStartBlock}");
    }

    public static void OnCombatEnd()
    {
        if (_inTurn)
        {
            EmitTurnReport();
        }

        _inTurn = false;
        _currentTurnNumber = 0;
        _cardsPlayedThisTurn.Clear();
        BreadcrumbTracker.Record("Combat", "Combat ended. Turn metrics reset.");
    }

    public static void RecordDirectDamage(decimal amount)
    {
        if (amount > 0)
        {
            _totalDirectDamageTaken += amount;
            BreadcrumbTracker.Record("Damage", $"Player took {amount} direct HP damage.");
        }
    }

    public static void RecordBlockDamage(decimal amount)
    {
        if (amount > 0)
        {
            _totalDamageAbsorbedByBlock += amount;
            BreadcrumbTracker.Record("Block", $"Player block absorbed {amount} damage.");
        }
    }

    public static void RecordBlockGained(decimal amount)
    {
        if (amount > 0)
        {
            _totalBlockGained += amount;
            BreadcrumbTracker.Record("Block", $"Player gained {amount} block.");
        }
    }

    public static void RecordCardPlayed(string cardName)
    {
        _cardsPlayedThisTurn.Add(cardName);
        BreadcrumbTracker.Record("Card", $"Card played: {cardName}");
    }

    public static void RecordEnergySpent(int amount)
    {
        if (amount > 0)
        {
            _totalEnergySpent += amount;
        }
    }

    public static void EmitTurnReport()
    {
        if (!_inTurn) return;

        var player = GameHelper.GetActivePlayer();
        var creature = player?.Creature;
        int currentHp = creature?.CurrentHp ?? _turnStartHp;
        int maxHp = creature?.MaxHp ?? 0;
        int currentBlock = creature?.Block ?? 0;
        int hpDelta = currentHp - _turnStartHp;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"╔══════════════════════════════ [TURN {_currentTurnNumber} REPORT] ══════════════════════════════╗");
        sb.AppendLine($"║ HP:        {currentHp}/{maxHp} (Delta: {(hpDelta >= 0 ? "+" : "")}{hpDelta} | Direct Dmg Taken: {_totalDirectDamageTaken} | Block Absorbed: {_totalDamageAbsorbedByBlock})");
        sb.AppendLine($"║ Block:     Current: {currentBlock} (Starting: {_turnStartBlock} | Gained This Turn: {_totalBlockGained})");
        sb.AppendLine($"║ Energy:    Spent: {_totalEnergySpent}");
        sb.AppendLine($"║ Cards:     Count: {_cardsPlayedThisTurn.Count} [{( _cardsPlayedThisTurn.Count > 0 ? string.Join(", ", _cardsPlayedThisTurn) : "None" )}]");

        // Format active status powers
        var powers = GameHelper.GetCreatureActivePowers(creature);
        if (powers != null && powers.Count > 0)
        {
            var statusStrings = new List<string>();
            foreach (var p in powers)
            {
                if (p == null) continue;
                string title = !string.IsNullOrWhiteSpace(p.Title?.GetFormattedText()) 
                    ? p.Title.GetFormattedText() 
                    : p.GetType().Name;
                statusStrings.Add($"{title} ({p.Amount})");
            }
            sb.AppendLine($"║ Statuses:  {string.Join(", ", statusStrings)}");
        }
        else
        {
            sb.AppendLine("║ Statuses:  None");
        }

        sb.AppendLine("╚════════════════════════════════════════════════════════════════════════════════╝");

        string report = sb.ToString();
        ModLogger.Info(report);
        BreadcrumbTracker.Record("TurnReport", $"Turn {_currentTurnNumber}: HP={currentHp}/{maxHp} (DmgTaken={_totalDirectDamageTaken}, BlockAbsorbed={_totalDamageAbsorbedByBlock}), Block={currentBlock}");
    }
#else
    public static int CurrentTurnNumber => 0;
    public static void OnTurnStart() { }
    public static void OnCombatEnd() { }
    public static void RecordDirectDamage(decimal amount) { }
    public static void RecordBlockDamage(decimal amount) { }
    public static void RecordBlockGained(decimal amount) { }
    public static void RecordCardPlayed(string cardName) { }
    public static void RecordEnergySpent(int amount) { }
    public static void EmitTurnReport() { }
#endif
}
