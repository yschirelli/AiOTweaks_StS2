using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Automatic crash and unhandled exception interceptor.
/// Generates detailed state snapshots and breadcrumb dumps to disk upon fatal errors.
/// Only active in DEBUG configuration. In RELEASE configuration, all operations are no-ops.
/// </summary>
public static class CrashDumpHandler
{
#if DEBUG
    private static bool _initialized = false;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledAppDomainException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            ModLogger.Info("[CrashDumpHandler] Initialized automatic crash and exception interceptor.");
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"[CrashDumpHandler] Failed to register exception handlers: {ex.Message}");
        }
    }

    private static void OnUnhandledAppDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            CaptureCrashDump("UnhandledAppDomainException", ex, e.IsTerminating);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CaptureCrashDump("UnobservedTaskException", e.Exception, false);
    }

    public static void CaptureCrashDump(string source, Exception ex, bool isTerminating)
    {
        try
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            string dumpFileName = $"aiotweaks_crash_{timestamp}.txt";
            string logDir = Path.GetDirectoryName(ModLogger.LogFilePath) ?? AppContext.BaseDirectory;
            string dumpFilePath = Path.Combine(logDir, dumpFileName);

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine("                    AIOTWEAKS AUTOMATIC CRASH DUMP SNAPSHOT                    ");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:O}");
            sb.AppendLine($"Fault Source:    {source}");
            sb.AppendLine($"Is Terminating:  {isTerminating}");
            sb.AppendLine($"Exception Type:  {ex.GetType().FullName}");
            sb.AppendLine($"Exception Msg:   {ex.Message}");
            sb.AppendLine();
            sb.AppendLine("--- STACK TRACE ---");
            sb.AppendLine(ex.StackTrace ?? "No stack trace available.");
            if (ex.InnerException != null)
            {
                sb.AppendLine();
                sb.AppendLine("--- INNER EXCEPTION ---");
                sb.AppendLine($"{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                sb.AppendLine(ex.InnerException.StackTrace);
            }
            sb.AppendLine();

            sb.AppendLine("--- RUNTIME CONTEXT ---");
            try
            {
                bool inCombat = GameHelper.IsInCombat();
                sb.AppendLine($"IsInCombat:     {inCombat}");

                var player = GameHelper.GetActivePlayer();
                var creature = player?.Creature;
                if (player != null)
                {
                    sb.AppendLine($"Player:         {player.GetType().Name}");
                    if (creature != null)
                    {
                        sb.AppendLine($"Player HP:      {creature.CurrentHp}/{creature.MaxHp}");
                        sb.AppendLine($"Player Block:   {creature.Block}");
                    }
                    if (player.PlayerCombatState != null)
                    {
                        sb.AppendLine($"Player Energy:  {player.PlayerCombatState.Energy}/{player.PlayerCombatState.MaxEnergy}");
                        sb.AppendLine($"Hand Cards:     {player.PlayerCombatState.Hand?.Cards?.Count ?? 0}");
                        sb.AppendLine($"Draw Pile:      {player.PlayerCombatState.DrawPile?.Cards?.Count ?? 0}");
                        sb.AppendLine($"Discard Pile:   {player.PlayerCombatState.DiscardPile?.Cards?.Count ?? 0}");
                        sb.AppendLine($"Exhaust Pile:   {player.PlayerCombatState.ExhaustPile?.Cards?.Count ?? 0}");
                    }
                }
                else
                {
                    sb.AppendLine("Player:         None (Out of Run/Combat)");
                }

                if (inCombat)
                {
                    var enemies = GameHelper.GetActiveCombatEnemies();
                    if (enemies != null)
                    {
                        sb.AppendLine($"Enemies Count:  {enemies.Count}");
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            var e = enemies[i];
                            if (e != null)
                            {
                                sb.AppendLine($"  - Enemy [{i}] {e.GetType().Name}: HP={e.CurrentHp}/{e.MaxHp}, Block={e.Block}, Dead={e.IsDead}");
                            }
                        }
                    }
                }
            }
            catch (Exception ctxEx)
            {
                sb.AppendLine($"[Context Query Exception]: {ctxEx.Message}");
            }
            sb.AppendLine();

            sb.AppendLine("--- ACTIVE CHEAT & RUNTIME FLAGS ---");
            sb.AppendLine($"GodMode:               {RuntimeStateManager.GodModeEnabled}");
            sb.AppendLine($"InfiniteEnergy:        {RuntimeStateManager.InfiniteEnergyEnabled}");
            sb.AppendLine($"OneHitKill:            {RuntimeStateManager.OneHitKillEnabled}");
            sb.AppendLine($"InfinitePotions:       {RuntimeStateManager.InfinitePotionsEnabled}");
            sb.AppendLine($"NoCardExhaust:         {RuntimeStateManager.NoCardExhaustEnabled}");
            sb.AppendLine($"PlayerDmgMult:         {RuntimeStateManager.GetEffectivePlayerDamageMultiplier()}");
            sb.AppendLine($"PlayerDefMult:         {RuntimeStateManager.GetEffectivePlayerDefendMultiplier()}");
            sb.AppendLine($"EnemyDmgMult:          {RuntimeStateManager.GetEffectiveEnemyDamageMultiplier()}");
            sb.AppendLine($"EnemyDefMult:          {RuntimeStateManager.GetEffectiveEnemyDefendMultiplier()}");
            sb.AppendLine();

            sb.AppendLine(BreadcrumbTracker.DumpTrail());
            sb.AppendLine("================================================================================");

            File.WriteAllText(dumpFilePath, sb.ToString(), Encoding.UTF8);
            ModLogger.Error($"[CrashDumpHandler] CRASH DUMP CAPTURED! Written to: {dumpFilePath}", ex);
        }
        catch (Exception dumpEx)
        {
            ModLogger.Error("[CrashDumpHandler] Failed writing crash dump.", dumpEx);
        }
    }
#else
    public static void Initialize() { }
    public static void CaptureCrashDump(string source, Exception ex, bool isTerminating) { }
#endif
}
