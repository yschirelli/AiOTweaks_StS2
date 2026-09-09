using System;
using Godot;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Autonomous combat stall and softlock detection watchdog.
/// Periodically monitors combat state progression. If combat is frozen in an unresolved state
/// for longer than the stall threshold, outputs diagnostic warnings and breadcrumb logs.
/// Only active in DEBUG configuration.
/// </summary>
public partial class CombatWatchdog : Node
{
#if DEBUG
    private double _stalledSeconds = 0;
    private const double StallThresholdSeconds = 15.0;
    private int _lastStateFingerprint = 0;
    private bool _hasReportedCurrentStall = false;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ModLogger.Info("[CombatWatchdog] Combat stall watchdog node initialized.");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (!GameHelper.IsInCombat())
        {
            _stalledSeconds = 0;
            _hasReportedCurrentStall = false;
            return;
        }

        int currentFingerprint = ComputeCombatFingerprint();
        if (currentFingerprint != _lastStateFingerprint)
        {
            _lastStateFingerprint = currentFingerprint;
            _stalledSeconds = 0;
            _hasReportedCurrentStall = false;
            return;
        }

        _stalledSeconds += delta;
        if (_stalledSeconds >= StallThresholdSeconds && !_hasReportedCurrentStall)
        {
            _hasReportedCurrentStall = true;
            OnCombatStallDetected();
        }
    }

    private int ComputeCombatFingerprint()
    {
        unchecked
        {
            int hash = 17;
            var player = GameHelper.GetActivePlayer();
            if (player?.Creature != null)
            {
                hash = hash * 31 + player.Creature.CurrentHp;
                hash = hash * 31 + player.Creature.Block;
            }
            if (player?.PlayerCombatState != null)
            {
                hash = hash * 31 + player.PlayerCombatState.Energy;
                hash = hash * 31 + (player.PlayerCombatState.Hand?.Cards?.Count ?? 0);
                hash = hash * 31 + (player.PlayerCombatState.DrawPile?.Cards?.Count ?? 0);
                hash = hash * 31 + (player.PlayerCombatState.DiscardPile?.Cards?.Count ?? 0);
            }

            var enemies = GameHelper.GetActiveCombatEnemies();
            if (enemies != null)
            {
                hash = hash * 31 + enemies.Count;
                foreach (var e in enemies)
                {
                    if (e != null)
                    {
                        hash = hash * 31 + e.CurrentHp;
                        hash = hash * 31 + e.Block;
                    }
                }
            }
            return hash;
        }
    }

    private void OnCombatStallDetected()
    {
        ModLogger.Warn($"[CombatWatchdog] Potential combat stall / softlock detected! Combat state has not progressed for {StallThresholdSeconds:F0} seconds.");
        ModLogger.Warn(BreadcrumbTracker.DumpTrail());
    }
#endif
}
