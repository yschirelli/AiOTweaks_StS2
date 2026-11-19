using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
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
                    string typeName = canonical.GetType().Name;
                    string pName = !string.IsNullOrWhiteSpace(canonical.Title?.GetFormattedText())
                        ? canonical.Title.GetFormattedText()
                        : typeName;

                    ModLogger.Verbose("StatusDirector", $"Calling PowerCmd.Apply for '{mutable.GetType().Name}' on target...");
                    TaskHelper.RunSafely(ApplyPowerAsync(mutable, target, amount, applier ?? target, typeName, pName));
                    return true;
                }
            }

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
            TaskHelper.RunSafely(RemovePowerAsync(target, power, pName));
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
            TaskHelper.RunSafely(ModifyPowerAmountAsync(target, power, delta, applier ?? target));
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to modify status amount for '{power.GetType().Name}'", ex);
            return false;
        }
    }

    private static async Task ApplyPowerAsync(PowerModel mutable, Creature target, int amount, Creature applier, string typeName, string displayName)
    {
        try
        {
            await PowerCmd.Apply(new BlockingPlayerChoiceContext(), mutable, target, (decimal)amount, applier, null, false);
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"PowerCmd.Apply failed for '{typeName}': {ex.Message}; fallback to target.ApplyPowerInternal");
            target.ApplyPowerInternal(mutable);
        }

        ModLogger.Info($"Applied {amount}x status '{displayName}' to {(target.IsPlayer ? "Player" : "Enemy")}.");
        OnStatusApplied?.Invoke(target, typeName, amount);
        OnStatusesChanged?.Invoke();
    }

    private static async Task RemovePowerAsync(Creature target, PowerModel power, string powerName)
    {
        try
        {
            await PowerCmd.Remove(power);
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"PowerCmd.Remove failed for '{powerName}': {ex.Message}; fallback to target.RemovePowerInternal");
            target.RemovePowerInternal(power);
        }

        ModLogger.Info($"Removed status '{powerName}' from {(target.IsPlayer ? "Player" : "Enemy")}.");
        OnStatusRemoved?.Invoke(target, powerName);
        OnStatusesChanged?.Invoke();
    }

    private static async Task ModifyPowerAmountAsync(Creature target, PowerModel power, int delta, Creature applier)
    {
        try
        {
            await PowerCmd.ModifyAmount(new BlockingPlayerChoiceContext(), power, (decimal)delta, applier, null, false);
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"PowerCmd.ModifyAmount failed for '{power.GetType().Name}': {ex.Message}; using direct fallback.");
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
                    TaskHelper.RunSafely(PowerCmd.Remove(p));
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
