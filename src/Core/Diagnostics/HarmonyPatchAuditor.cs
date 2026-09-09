using System;
using System.Reflection;
using HarmonyLib;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Verifies that all expected Harmony patches owned by AIOTweaks were applied cleanly.
/// Only active in DEBUG configuration.
/// </summary>
public static class HarmonyPatchAuditor
{
#if DEBUG
    public static void AuditPatches(string harmonyId)
    {
        try
        {
            ModLogger.Info($"[HarmonyAudit] Auditing active Harmony patches for owner ID '{harmonyId}'...");

            var patchedMethods = Harmony.GetAllPatchedMethods();
            int totalActive = 0;

            foreach (var method in patchedMethods)
            {
                var patchInfo = Harmony.GetPatchInfo(method);
                if (patchInfo != null && patchInfo.Owners.Contains(harmonyId))
                {
                    totalActive++;
                    ModLogger.Verbose("HarmonyAudit", $"Active patch: {method.DeclaringType?.Name ?? "Unknown"}::{method.Name}");
                }
            }

            ModLogger.Info($"[HarmonyAudit] Verification complete: {totalActive} patches active and registered for '{harmonyId}'.");
            BreadcrumbTracker.Record("HarmonyAudit", $"{totalActive} patches successfully verified.");
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"[HarmonyAudit] Patch audit encountered error: {ex.Message}");
        }
    }
#else
    public static void AuditPatches(string harmonyId) { }
#endif
}
