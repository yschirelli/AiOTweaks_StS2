using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using AIOTweaks.Hooks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Autonomous per-run diagnostic logger.
/// Creates an isolated log file per run under 'logs/runs/' in DEBUG configuration.
/// Captures full run metadata (character, seed, room length, active modifiers, game-state snapshots)
/// and streams live combat events, invariant checks, and audited post-hook values.
/// In RELEASE configuration, all methods compile away to no-ops.
/// </summary>
public static class RunDiagnosticLogger
{
#if DEBUG
    private static readonly object _fileLock = new();
    private static StreamWriter? _currentRunWriter;
    private static string? _currentRunLogPath;
    private static bool _isRunActive = false;

    public const string RunLogsSubdirectory = "logs/runs";
    public static bool IsRunActive => _isRunActive;
    public static string? CurrentRunLogPath => _currentRunLogPath;

    /// <summary>
    /// Starts a dedicated per-run diagnostic log session.
    /// Writes the comprehensive run header with actual game state and snapshot tweaks.
    /// </summary>
    public static void StartRunSession(RunState? runState, ActiveRunTweaksSnapshot? snapshot, ModConfig config)
    {
        lock (_fileLock)
        {
            CloseRunSession("NewRunStarting");

            try
            {
                string modRoot = ModLogger.GetModRootDirectory();
                string logsDir = Path.Combine(modRoot, RunLogsSubdirectory);
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                // Resolve character name / ID from game state
                string charName = ResolveCharacterName(runState);

                // Resolve seed values
                string stringSeed = runState?.Rng?.StringSeed ?? "UNKNOWN";
                string numericSeed = runState?.Rng?.Seed.ToString() ?? "0";

                string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                string fileName = $"aiotweaks_run_{SanitizeFileName(charName)}_{SanitizeFileName(stringSeed)}_{timestamp}.log";
                _currentRunLogPath = Path.Combine(logsDir, fileName);

                var fs = new FileStream(_currentRunLogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                _currentRunWriter = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
                _isRunActive = true;

                // Subscribe to ModLogger stream for this run session
                ModLogger.OnLogged += OnGlobalModLogged;

                // Build header
                string header = BuildRunHeader(runState, snapshot, config, charName, stringSeed, numericSeed, _currentRunLogPath);
                _currentRunWriter.WriteLine(header);

                ModLogger.Info($"[RunDiagnosticLogger] Per-run log session started: {_currentRunLogPath}");
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[RunDiagnosticLogger] Failed to start run diagnostic log: {ex.Message}");
                _isRunActive = false;
                _currentRunWriter = null;
            }
        }
    }

    /// <summary>
    /// Closes the active per-run diagnostic log session and writes the final summary footer.
    /// </summary>
    public static void CloseRunSession(string reason)
    {
        lock (_fileLock)
        {
            if (!_isRunActive || _currentRunWriter == null) return;

            try
            {
                ModLogger.Flush();

                string sqlSummary = ModLogger.GetSqlGroupBySummary(30);
                _currentRunWriter.WriteLine("\n" + sqlSummary);

                string footer =
                    "\n================================================================================\n" +
                    $"                         RUN SESSION CLOSED ({reason.ToUpperInvariant()})\n" +
                    $" Closed At (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}\n" +
                    $" Total Loops    : {RuntimeStateManager.CurrentEndlessLoopCount}\n" +
                    "================================================================================";

                _currentRunWriter.WriteLine(footer);
                _currentRunWriter.Flush();
            }
            catch { }
            finally
            {
                ModLogger.OnLogged -= OnGlobalModLogged;
                try
                {
                    _currentRunWriter?.Dispose();
                }
                catch { }
                _currentRunWriter = null;
                _isRunActive = false;
                _currentRunLogPath = null;
            }
        }
    }

    /// <summary>
    /// Records a combat value audit event into the active run log, showing pre-hook vs post-hook actual values.
    /// </summary>
    public static void RecordCombatCalculationAudit(string calculationType, string sourceCreature, string targetCreature, decimal preHookValue, float multiplier, decimal postHookValue, string hookName)
    {
        if (!_isRunActive || _currentRunWriter == null) return;

        lock (_fileLock)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                string logLine = $"[{timestamp}] [CALC_AUDIT] [{calculationType}] ({sourceCreature} -> {targetCreature}) " +
                                 $"Pre-Hook: {preHookValue} | Mult: x{multiplier:F2} | Post-Hook: {postHookValue} (via {hookName})";
                _currentRunWriter.WriteLine(logLine);
            }
            catch { }
        }
    }

    private static void OnGlobalModLogged(LogLevel level, string formattedMessage)
    {
        if (!_isRunActive || _currentRunWriter == null) return;

        lock (_fileLock)
        {
            try
            {
                _currentRunWriter.WriteLine(formattedMessage);
            }
            catch { }
        }
    }

    private static string BuildRunHeader(RunState? runState, ActiveRunTweaksSnapshot? snapshot, ModConfig config, string charName, string stringSeed, string numericSeed, string logPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("                 AIOTWEAKS PER-RUN DIAGNOSTIC LOG & RUN HEADER                 ");
        sb.AppendLine("================================================================================");
        sb.AppendLine($"Timestamp (UTC) : {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"Mod Version     : {ModEntry.GetVersionString()} ({ModEntry.BuildConfiguration} BUILD)");
        sb.AppendLine($"Log File Path   : {logPath}");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        // 1. Run Metadata
        int profileId = snapshot?.ProfileId ?? RunTweaksSaveManager.GetCurrentProfileId();
        int roomCount = snapshot?.PreRunTweaks?.MapRoomCount ?? config.PreRunTweaks.MapRoomCount;
        bool isCustom = snapshot?.IsCustom ?? RunTweaksSaveManager.IsCustomRun(config.PreRunTweaks, ConfigManager.ActiveRunSettings);
        bool endless = snapshot?.PreRunTweaks?.EndlessMode?.Enabled ?? config.PreRunTweaks.EndlessMode.Enabled;
        float endlessMult = snapshot?.PreRunTweaks?.EndlessMode?.EnemyScalingMultiplier ?? config.PreRunTweaks.EndlessMode.EnemyScalingMultiplier;
        bool freeMapNav = snapshot?.PreRunTweaks?.FreeMapNavigation ?? config.PreRunTweaks.FreeMapNavigation;

        sb.AppendLine("[RUN METADATA]");
        sb.AppendLine($"Profile ID      : {profileId}");
        sb.AppendLine($"Character       : {charName}");
        sb.AppendLine($"Seed (String)   : \"{stringSeed}\"");
        sb.AppendLine($"Seed (Numeric)  : {numericSeed}");
        sb.AppendLine($"Act Room Count  : {roomCount} (Vanilla: 15)");
        sb.AppendLine($"Game Mode       : {(isCustom ? "Custom (Seeded / Achievements Disabled)" : "Standard (Fair Play / Achievements Enabled)")}");
        sb.AppendLine($"Endless Mode    : {(endless ? $"Enabled (Scaling: {endlessMult:F2}x | Loop {snapshot?.EndlessLoopCount ?? 0})" : "Disabled")}");
        sb.AppendLine($"Free Map Nav    : {(freeMapNav ? "Enabled (Flying Boots Mode)" : "Disabled")}");
        sb.AppendLine();

        // 2. Snapshot Tweaks Used to Generate This Run
        var tweaks = snapshot?.PreRunTweaks ?? config.PreRunTweaks;
        sb.AppendLine("[SNAPSHOT TWEAKS USED TO GENERATE THIS RUN]");
        sb.AppendLine($"  - Map Room Count Override       : {tweaks.MapRoomCount}");
        sb.AppendLine($"  - Starting Gold Bonus           : +{tweaks.StartingGoldBonus}");
        sb.AppendLine($"  - Starting Max HP Bonus         : +{tweaks.StartingMaxHpBonus}");
        sb.AppendLine($"  - Force Neow Start              : {tweaks.ForceNeowBonus}");
        sb.AppendLine($"  - Gold Reward Multiplier        : {tweaks.GoldRewardMultiplier:F2}x");
        sb.AppendLine($"  - Shop Discount Multiplier      : {tweaks.ShopDiscountMultiplier:F2}x");
        sb.AppendLine($"  - Card Reward Draft Count       : {tweaks.CardRewardCount}");
        sb.AppendLine($"  - Player Damage Multiplier      : {tweaks.PlayerDamageMultiplier:F2}x");
        sb.AppendLine($"  - Player Defend Multiplier      : {tweaks.PlayerDefendMultiplier:F2}x");
        sb.AppendLine($"  - Max Starting Energy           : {tweaks.MaxEnergy}");
        sb.AppendLine($"  - Enemy Health Multiplier       : {tweaks.EnemyHealthMultiplier:F2}x");
        sb.AppendLine($"  - Enemy Damage Multiplier       : {tweaks.EnemyDamageMultiplier:F2}x");
        sb.AppendLine($"  - Enemy Defend Multiplier       : {tweaks.EnemyDefendMultiplier:F2}x");
        sb.AppendLine($"  - Potion Slots                  : {tweaks.PotionSlots}");
        sb.AppendLine($"  - Map Node Distribution Weights :");
        sb.AppendLine($"      * Elites                    : {tweaks.MapNodeDistribution.EliteWeightMultiplier:F2}x");
        sb.AppendLine($"      * Shops                     : {tweaks.MapNodeDistribution.ShopWeightMultiplier:F2}x");
        sb.AppendLine($"      * Unknown / Events          : {tweaks.MapNodeDistribution.EventWeightMultiplier:F2}x");
        sb.AppendLine($"      * Rest Sites                : {tweaks.MapNodeDistribution.RestSiteWeightMultiplier:F2}x");
        sb.AppendLine($"      * Normal Combats            : {tweaks.MapNodeDistribution.CombatWeightMultiplier:F2}x");
        sb.AppendLine($"      * Treasure Rooms            : {tweaks.MapNodeDistribution.TreasureRoomMultiplier:F2}x");
        sb.AppendLine();

        // 3. Mod Configuration Snapshot
        sb.AppendLine("[MOD CONFIGURATION SNAPSHOT]");
        sb.AppendLine($"  - General Enabled               : {config.General.Enabled}");
        sb.AppendLine($"  - Hotkeys                       : Console={config.General.ConsoleHotkey} | Settings={config.General.GuiOverlayHotkey} | Shop={config.General.QuickOpenShopKey}");
        sb.AppendLine($"  - God Mode                      : {config.CombatSandbox.GodMode || RuntimeStateManager.GodModeEnabled}");
        sb.AppendLine($"  - Infinite Energy               : {config.CombatSandbox.InfiniteEnergy || RuntimeStateManager.InfiniteEnergyEnabled}");
        sb.AppendLine($"  - One-Hit Kill                  : {config.CombatSandbox.OneHitKill || RuntimeStateManager.OneHitKillEnabled}");
        sb.AppendLine($"  - Infinite Potions              : {config.CombatSandbox.InfinitePotions || RuntimeStateManager.InfinitePotionsEnabled}");
        sb.AppendLine($"  - No Card Exhaust               : {config.CombatSandbox.NoCardExhaust || RuntimeStateManager.NoCardExhaustEnabled}");
        sb.AppendLine($"  - Bonus Draw Per Turn           : {config.CombatSandbox.BonusDrawPerTurn}");
        sb.AppendLine();

        sb.AppendLine("================================================================================");
        sb.AppendLine("                                RUN EVENT STREAM                                ");
        sb.AppendLine("================================================================================");

        return sb.ToString();
    }

    private static string ResolveCharacterName(RunState? runState)
    {
        try
        {
            var player = GameHelper.GetActivePlayer();
            if (player?.Character != null)
            {
                string title = player.Character.Title?.GetFormattedText() ?? "";
                if (!string.IsNullOrWhiteSpace(title)) return title;
                return player.Character.GetType().Name;
            }

            var selectedChar = GameHelper.GetSelectedCharacterModel();
            if (selectedChar != null)
            {
                string title = selectedChar.Title?.GetFormattedText() ?? "";
                if (!string.IsNullOrWhiteSpace(title)) return title;
                return selectedChar.GetType().Name;
            }

            if (runState?.Players != null && runState.Players.Count > 0)
            {
                var p = runState.Players[0];
                if (p?.Character != null)
                {
                    string title = p.Character.Title?.GetFormattedText() ?? "";
                    if (!string.IsNullOrWhiteSpace(title)) return title;
                    return p.Character.GetType().Name;
                }
            }
        }
        catch { }

        return "UNKNOWN_CHARACTER";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (Array.IndexOf(invalid, c) < 0 && !char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }
        string result = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "UNKNOWN" : result;
    }
#else
    public const string RunLogsSubdirectory = "logs/runs";
    public static bool IsRunActive => false;
    public static string? CurrentRunLogPath => null;
    public static void StartRunSession(RunState? runState, ActiveRunTweaksSnapshot? snapshot, ModConfig config) { }
    public static void CloseRunSession(string reason) { }
    public static void RecordCombatCalculationAudit(string calculationType, string sourceCreature, string targetCreature, decimal preHookValue, float multiplier, decimal postHookValue, string hookName) { }
#endif
}
