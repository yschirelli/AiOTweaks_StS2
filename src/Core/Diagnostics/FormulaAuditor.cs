using System;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Real-time mathematical formula auditor for combat multipliers and modifiers.
/// Cross-verifies calculations in damage and block pipelines, detecting compounding errors,
/// target leakage (e.g. player multipliers accidentally modifying enemies), or rounding anomalies.
/// Only active in DEBUG configuration.
/// </summary>
public static class FormulaAuditor
{
#if DEBUG
    public static void AuditBlockCalculation(Creature? target, decimal originalBlock, float multiplier, decimal calculatedBlock, string sourceName)
    {
        try
        {
            if (originalBlock <= 0) return;

            decimal expected = Math.Max(0, (decimal)Math.Round((double)originalBlock * multiplier));

            if (Math.Abs(calculatedBlock - expected) > 0.001m)
            {
                string targetType = target?.IsPlayer == true ? "Player" : target?.GetType().Name ?? "Unknown";
                string violation = $"[FORMULA AUDIT MISMATCH] Block calculation mismatch for {targetType} via '{sourceName}'!\n" +
                                   $"  Original: {originalBlock} | Multiplier: {multiplier:F2}x\n" +
                                   $"  Expected: {expected} | Actual: {calculatedBlock}\n" +
                                   $"  Discrepancy: {calculatedBlock - expected:+#.##;-#.##;0}";

                ModLogger.Warn(violation);
                BreadcrumbTracker.Record("FormulaViolation", violation);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[FormulaAuditor] AuditBlockCalculation notice: {ex.Message}");
        }
    }

    public static void AuditDamageCalculation(Creature? dealer, Creature? target, decimal originalDamage, float multiplier, decimal calculatedDamage, string sourceName)
    {
        try
        {
            if (originalDamage <= 0) return;

            decimal expected = Math.Max(0, (decimal)Math.Round((double)originalDamage * multiplier));

            if (Math.Abs(calculatedDamage - expected) > 0.001m)
            {
                string dealerType = dealer?.IsPlayer == true ? "Player" : dealer?.GetType().Name ?? "Unknown";
                string targetType = target?.IsPlayer == true ? "Player" : target?.GetType().Name ?? "Unknown";

                string violation = $"[FORMULA AUDIT MISMATCH] Damage calculation mismatch ({dealerType} -> {targetType}) via '{sourceName}'!\n" +
                                   $"  Original: {originalDamage} | Multiplier: {multiplier:F2}x\n" +
                                   $"  Expected: {expected} | Actual: {calculatedDamage}\n" +
                                   $"  Discrepancy: {calculatedDamage - expected:+#.##;-#.##;0}";

                ModLogger.Warn(violation);
                BreadcrumbTracker.Record("FormulaViolation", violation);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[FormulaAuditor] AuditDamageCalculation notice: {ex.Message}");
        }
    }
#else
    public static void AuditBlockCalculation(Creature? target, decimal originalBlock, float multiplier, decimal calculatedBlock, string sourceName) { }
    public static void AuditDamageCalculation(Creature? dealer, Creature? target, decimal originalDamage, float multiplier, decimal calculatedDamage, string sourceName) { }
#endif
}
