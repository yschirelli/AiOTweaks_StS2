using System;
using System.Linq;
using System.Collections.Generic;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for inspecting, applying, modifying, and removing status effects (powers/buffs/debuffs)
/// on the player and active combat enemies in real-time.
/// </summary>
public static class StatusDirector
{
    public static event Action<Creature, string, int>? OnStatusApplied;
    public static event Action<Creature, string>? OnStatusRemoved;
    public static event Action? OnStatusesChanged;

    /// <summary>
    /// Applies a status effect / power to a target creature (player or enemy) in combat.
    /// </summary>
    public static bool ApplyStatus(Creature target, string powerId, int amount, Creature? applier = null)
    {
        ModLogger.Verbose("StatusDirector", $"ApplyStatus called: target={(target?.IsPlayer == true ? "Player" : target?.Monster?.Id.Entry ?? target?.GetType().Name)}, powerId='{powerId}', amount={amount}");
        if (target == null)
        {
            ModLogger.Warn("Cannot apply status: target creature is null.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(powerId))
        {
            ModLogger.Warn("Cannot apply status: powerId is null or empty.");
            return false;
        }

        try
        {
            var canonical = GameHelper.FindCanonicalPowerModel(powerId);
            if (canonical != null)
            {
                var mutable = canonical.ToMutable(amount);
                if (mutable != null)
                {
                    ModLogger.Verbose("StatusDirector", $"Calling PowerCmd.Apply for '{mutable.GetType().Name}' on target...");
                    try
                    {
                        var task = PowerCmd.Apply(null!, mutable, target, (decimal)amount, applier ?? target, null!, false);
                        task.ContinueWith(t =>
                        {
                            if (t.IsFaulted && t.Exception != null)
                            {
                                ModLogger.Warn($"PowerCmd.Apply async notice: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Warn($"PowerCmd.Apply direct notice: {ex.Message}; fallback to target.ApplyPowerInternal");
                        target.ApplyPowerInternal(mutable);
                    }

                    string pName = !string.IsNullOrWhiteSpace(canonical.Title?.GetFormattedText()) 
                        ? canonical.Title.GetFormattedText() 
                        : canonical.GetType().Name;
                    ModLogger.Info($"Applied {amount}x status '{pName}' to {(target.IsPlayer ? "Player" : "Enemy")}.");
                    OnStatusApplied?.Invoke(target, canonical.GetType().Name, amount);
                    OnStatusesChanged?.Invoke();
                    return true;
                }
            }

            // Fallback via DevConsole (if player creature)
            if (target.IsPlayer)
            {
                ModLogger.Verbose("StatusDirector", $"Dispatching DevConsole command 'power {powerId} {amount}'");
                GameHelper.ExecuteConsoleCommand($"power {powerId} {amount}");
                OnStatusApplied?.Invoke(target, powerId, amount);
                OnStatusesChanged?.Invoke();
                return true;
            }

            ModLogger.Warn($"Failed to resolve PowerModel for '{powerId}'.");
            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to apply status '{powerId}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes a specific active status effect / power instance from a creature.
    /// </summary>
    public static bool RemoveStatus(Creature target, PowerModel power)
    {
        if (target == null || power == null) return false;
        try
        {
            string pName = power.GetType().Name;
            ModLogger.Verbose("StatusDirector", $"Removing power '{pName}' from creature...");
            try
            {
                var task = PowerCmd.Remove(power);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        ModLogger.Warn($"PowerCmd.Remove async notice: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                    }
                });
            }
            catch
            {
                target.RemovePowerInternal(power);
            }

            ModLogger.Info($"Removed status '{pName}' from {(target.IsPlayer ? "Player" : "Enemy")}.");
            OnStatusRemoved?.Invoke(target, pName);
            OnStatusesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to remove power instance", ex);
            return false;
        }
    }

    /// <summary>
    /// Removes a status effect by name or ID from a creature.
    /// </summary>
    public static bool RemoveStatus(Creature target, string powerId)
    {
        if (target == null || string.IsNullOrWhiteSpace(powerId)) return false;
        try
        {
            var powers = GameHelper.GetCreatureActivePowers(target);
            var match = powers.FirstOrDefault(p => 
                p != null && (
                    p.GetType().Name.Equals(powerId, StringComparison.OrdinalIgnoreCase) ||
                    p.Id.Entry.Equals(powerId, StringComparison.OrdinalIgnoreCase) ||
                    p.Title.GetFormattedText().Equals(powerId, StringComparison.OrdinalIgnoreCase)));

            if (match != null)
            {
                return RemoveStatus(target, match);
            }
            ModLogger.Warn($"Power '{powerId}' not found on creature.");
            return false;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to remove status '{powerId}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Modifies the amount / duration / stacks of an existing active power on a creature.
    /// </summary>
    public static bool ModifyStatusAmount(Creature target, PowerModel power, int delta, Creature? applier = null)
    {
        if (target == null || power == null) return false;
        try
        {
            if (power.Amount + delta <= 0 && !power.AllowNegative)
            {
                return RemoveStatus(target, power);
            }

            ModLogger.Verbose("StatusDirector", $"Modifying amount of power '{power.GetType().Name}' by {delta} (current={power.Amount})...");
            try
            {
                var task = PowerCmd.ModifyAmount(null!, power, (decimal)delta, applier ?? target, null!, false);
                task.ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                    {
                        ModLogger.Warn($"PowerCmd.ModifyAmount async notice: {t.Exception.InnerException?.Message ?? t.Exception.Message}");
                    }
                });
            }
            catch
            {
                try
                {
                    var prop = typeof(PowerModel).GetProperty("Amount");
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(power, power.Amount + delta);
                    }
                    else
                    {
                        var field = typeof(PowerModel).GetField("_amount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public) ??
                                    typeof(PowerModel).GetField("<Amount>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        field?.SetValue(power, power.Amount + delta);
                    }
                    target.InvokePowerModified(power, delta, false);
                }
                catch { }
            }

            OnStatusesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to modify status amount for '{power.GetType().Name}'", ex);
            return false;
        }
    }

    /// <summary>
    /// Clears all active status effects / powers from a target creature.
    /// </summary>
    public static bool ClearAllStatuses(Creature target)
    {
        if (target == null) return false;
        try
        {
            ModLogger.Verbose("StatusDirector", $"Clearing all status effects from {(target.IsPlayer ? "Player" : "Enemy")}...");
            foreach (var p in GameHelper.GetCreatureActivePowers(target).ToList())
            {
                try
                {
                    PowerCmd.Remove(p);
                }
                catch { }
            }

            ModLogger.Info($"Cleared all statuses from {(target.IsPlayer ? "Player" : "Enemy")}.");
            OnStatusRemoved?.Invoke(target, "all");
            OnStatusesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to clear all statuses", ex);
            return false;
        }
    }

    /// <summary>
    /// Returns the active list of powers on a creature.
    /// </summary>
    public static IReadOnlyList<PowerModel> GetActiveStatuses(Creature? creature)
    {
        return GameHelper.GetCreatureActivePowers(creature);
    }
}
