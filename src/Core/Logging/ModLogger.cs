using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Godot;

namespace AIOTweaks.Core.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

public sealed class LogHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string SourceTag { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Centralized logging utility for AIOTweaks mod with tag formatting, timestamping, and log level filtering.
/// Delivers real-time diagnostic information to the in-game debug console, Godot terminal, and file output.
/// Features high-performance Lag-1 deduplication to suppress repeating log spam and provides SQL-style GROUP BY log analytics.
/// When compiled in DEBUG configuration, verbose logging and file logging to the mod's root folder are forcefully enabled by default.
/// </summary>
public static class ModLogger
{
    private const string Tag = "[AIOTweaks]";
    private const string LogFileName = "aiotweaks_debug.log";
    private static readonly object _fileLock = new();
    private static string? _logFilePath;
    private static StreamWriter? _logWriter;
    private static bool _fileLoggingInitialized = false;

    // Lag-1 deduplication engine
    private static readonly object _lagLock = new();
    private static string? _lastRawMessage;
    private static LogLevel _lastLogLevel = LogLevel.Debug;
    private static int _repeatCount = 0;
    private static DateTime _firstOccurrenceTime = DateTime.MinValue;
    private static DateTime _lastOccurrenceTime = DateTime.MinValue;
    private static readonly System.Threading.Timer _quietTimer = new(OnQuietTimerExpired, null, Timeout.Infinite, Timeout.Infinite);

    // SQL Group By history state
    private static readonly object _historyLock = new();
    private static readonly List<LogHistoryEntry> _history = new(1024);
    private const int MaxHistoryEntries = 1000;

#if DEBUG
    public const bool IsDebugBuild = true;
#else
    public const bool IsDebugBuild = false;
#endif

    /// <summary>
    /// Whether Lag-1 deduplication is enabled. When true, consecutive identical log messages
    /// are collapsed into a single summary with repetition counts, eliminating per-frame loop spam.
    /// Defaults to true.
    /// </summary>
    public static bool LagDeduplicationEnabled { get; set; } = true;

    /// <summary>
    /// Milliseconds of silence after which a repeated message's lag count is automatically flushed.
    /// </summary>
    public static int LagQuietPeriodMs { get; set; } = 750;

    /// <summary>
    /// Interval at which an intermediate heartbeat notification is emitted during continuous floods.
    /// </summary>
    public static int LagHeartbeatInterval { get; set; } = 100;

    /// <summary>
    /// Whether log output should be saved to a text file in the root folder of the mod.
    /// Defaults to true in Debug builds.
    /// </summary>
    public static bool FileLoggingEnabled { get; set; } = IsDebugBuild;

    /// <summary>
    /// Path to the debug log file in the mod root folder.
    /// </summary>
    public static string LogFilePath
    {
        get
        {
            if (string.IsNullOrEmpty(_logFilePath))
            {
                EnsureFileLoggingInitialized();
            }
            return _logFilePath ?? Path.Combine(GetModRootDirectory(), LogFileName);
        }
    }

    /// <summary>
    /// Minimum log level for filtering. In debug builds, defaults to Debug so all verbose diagnostics are captured.
    /// </summary>
    public static LogLevel MinimumLevel { get; set; } = IsDebugBuild ? LogLevel.Debug : LogLevel.Info;

    public static event Action<LogLevel, string>? OnLogged;

    public static void Debug(string message)
    {
        if (MinimumLevel <= LogLevel.Debug)
        {
            Log(LogLevel.Debug, message);
        }
    }

    public static void Verbose(string caller, string message)
    {
        if (MinimumLevel <= LogLevel.Debug)
        {
            Log(LogLevel.Debug, $"[{caller}] {message}");
        }
    }

    public static void Info(string message)
    {
        if (MinimumLevel <= LogLevel.Info)
        {
            Log(LogLevel.Info, message);
        }
    }

    public static void Warn(string message)
    {
        if (MinimumLevel <= LogLevel.Warn)
        {
            Log(LogLevel.Warn, message);
        }
    }

    public static void Error(string message, Exception? ex = null)
    {
        if (MinimumLevel <= LogLevel.Error)
        {
            string fullMessage = ex != null ? $"{message} | Exception: {ex.Message}\n{ex.StackTrace}" : message;
#if DEBUG
            if (ex != null)
            {
                fullMessage += "\n" + AIOTweaks.Core.Diagnostics.BreadcrumbTracker.DumpTrail();
            }
#endif
            Log(LogLevel.Error, fullMessage);
        }
    }

    private static void Log(LogLevel level, string message)
    {
        if (string.IsNullOrEmpty(message)) return;

        string? pendingRepeatMessage = null;
        LogLevel pendingRepeatLevel = LogLevel.Debug;
        string? messageToEmit = null;
        LogLevel emitLevel = level;

        if (LagDeduplicationEnabled)
        {
            lock (_lagLock)
            {
                DateTime now = DateTime.Now;
                bool isDuplicate = _lastRawMessage != null
                    && _lastLogLevel == level
                    && string.Equals(_lastRawMessage, message, StringComparison.Ordinal);

                if (isDuplicate)
                {
                    _repeatCount++;
                    _lastOccurrenceTime = now;

                    // Periodic heartbeat threshold for massive floods (e.g. every 100 repeats)
                    if (_repeatCount % LagHeartbeatInterval == 0)
                    {
                        string ts = now.ToString("HH:mm:ss.fff");
                        string preview = TruncatePreview(_lastRawMessage, 50);
                        pendingRepeatMessage = $"{Tag} [{ts}] [{level.ToString().ToUpperInvariant()}] ↳ [Lag Group] Repeated {_repeatCount} times: '{preview}'";
                        pendingRepeatLevel = level;
                    }

                    try
                    {
                        _quietTimer.Change(LagQuietPeriodMs, Timeout.Infinite);
                    }
                    catch { }

                    // Duplicate suppressed unless threshold heartbeat triggered
                    if (pendingRepeatMessage == null)
                    {
                        RecordHistoryInternal(level, message);
                        return;
                    }
                }
                else
                {
                    // Message changed from lag(1)! Flush previous repeat if it was repeated
                    if (_repeatCount > 1 && _lastRawMessage != null)
                    {
                        string ts = now.ToString("HH:mm:ss.fff");
                        int suppressedCount = _repeatCount - 1;
                        pendingRepeatMessage = $"{Tag} [{ts}] [{_lastLogLevel.ToString().ToUpperInvariant()}] ↳ [Lag Group] Previous message repeated {suppressedCount} time{(suppressedCount > 1 ? "s" : "")} (x{_repeatCount} total).";
                        pendingRepeatLevel = _lastLogLevel;
                    }

                    _lastRawMessage = message;
                    _lastLogLevel = level;
                    _repeatCount = 1;
                    _firstOccurrenceTime = now;
                    _lastOccurrenceTime = now;

                    try
                    {
                        _quietTimer.Change(LagQuietPeriodMs, Timeout.Infinite);
                    }
                    catch { }

                    string timestamp = now.ToString("HH:mm:ss.fff");
                    messageToEmit = $"{Tag} [{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}";
                    emitLevel = level;
                }
            }
        }
        else
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            messageToEmit = $"{Tag} [{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}";
            emitLevel = level;
        }

        RecordHistoryInternal(level, message);

        if (pendingRepeatMessage != null)
        {
            DispatchFormattedLog(pendingRepeatLevel, pendingRepeatMessage);
        }

        if (messageToEmit != null)
        {
            DispatchFormattedLog(emitLevel, messageToEmit);
        }
    }

    private static void DispatchFormattedLog(LogLevel level, string formatted)
    {
        try
        {
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Info:
                    GD.Print(formatted);
                    break;
                case LogLevel.Warn:
                    GD.PushWarning(formatted);
                    break;
                case LogLevel.Error:
                    GD.PushError(formatted);
                    break;
            }
        }
        catch
        {
            Console.WriteLine(formatted);
        }

        try
        {
            OnLogged?.Invoke(level, formatted);
        }
        catch
        {
            // Safeguard against subscriber errors in logging pipeline
        }

        if (FileLoggingEnabled)
        {
            WriteToFile(formatted);
        }
    }

    private static void OnQuietTimerExpired(object? state)
    {
        string? repeatMessage = null;
        LogLevel repeatLevel = LogLevel.Debug;

        lock (_lagLock)
        {
            if (_repeatCount > 1 && _lastRawMessage != null)
            {
                string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                int suppressedCount = _repeatCount - 1;
                repeatMessage = $"{Tag} [{ts}] [{_lastLogLevel.ToString().ToUpperInvariant()}] ↳ [Lag Group] Previous message repeated {suppressedCount} time{(suppressedCount > 1 ? "s" : "")} (x{_repeatCount} total).";
                repeatLevel = _lastLogLevel;
                _repeatCount = 0;
                _lastRawMessage = null;
            }
        }

        if (repeatMessage != null)
        {
            DispatchFormattedLog(repeatLevel, repeatMessage);
        }
    }

    /// <summary>
    /// Flushes any pending lag deduplication buffer immediately and forces StreamWriter flush.
    /// </summary>
    public static void FlushLagBuffer()
    {
        string? repeatMessage = null;
        LogLevel repeatLevel = LogLevel.Debug;

        lock (_lagLock)
        {
            try
            {
                _quietTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch { }

            if (_repeatCount > 1 && _lastRawMessage != null)
            {
                string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                int suppressedCount = _repeatCount - 1;
                repeatMessage = $"{Tag} [{ts}] [{_lastLogLevel.ToString().ToUpperInvariant()}] ↳ [Lag Group] Previous message repeated {suppressedCount} time{(suppressedCount > 1 ? "s" : "")} (x{_repeatCount} total).";
                repeatLevel = _lastLogLevel;
                _repeatCount = 0;
                _lastRawMessage = null;
            }
        }

        if (repeatMessage != null)
        {
            DispatchFormattedLog(repeatLevel, repeatMessage);
        }
    }

    private static void RecordHistoryInternal(LogLevel level, string message)
    {
        try
        {
            lock (_historyLock)
            {
                var (sourceTag, cleanMsg) = ExtractSourceAndMessage(message);
                _history.Add(new LogHistoryEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level,
                    SourceTag = sourceTag,
                    Message = cleanMsg
                });

                if (_history.Count > MaxHistoryEntries)
                {
                    _history.RemoveRange(0, _history.Count - MaxHistoryEntries);
                }
            }
        }
        catch { }
    }

    private static (string Source, string Message) ExtractSourceAndMessage(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage)) return ("General", string.Empty);

        if (rawMessage.StartsWith("["))
        {
            int close = rawMessage.IndexOf(']');
            if (close > 1 && close <= 32)
            {
                string src = rawMessage.Substring(1, close - 1);
                string msg = rawMessage.Substring(close + 1).Trim();
                return (src, msg);
            }
        }

        return ("General", rawMessage);
    }

    private static string TruncatePreview(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength - 3) + "...";
    }

    /// <summary>
    /// Generates a SQL-like GROUP BY summary of recent log records ordered by occurrence count descending.
    /// Emulates: SELECT COUNT(*), LEVEL, SOURCE, MESSAGE ... GROUP BY LEVEL, SOURCE, MESSAGE ORDER BY COUNT(*) DESC.
    /// </summary>
    public static string GetSqlGroupBySummary(int limit = 25)
    {
        lock (_historyLock)
        {
            if (_history.Count == 0)
            {
                return "=== SQL GROUP BY RECENT LOGS ===\nNo logs recorded in history buffer.";
            }

            var groups = _history
                .GroupBy(e => new { e.Level, e.SourceTag, e.Message })
                .Select(g => new
                {
                    g.Key.Level,
                    g.Key.SourceTag,
                    g.Key.Message,
                    Count = g.Count(),
                    FirstSeen = g.Min(e => e.Timestamp),
                    LastSeen = g.Max(e => e.Timestamp)
                })
                .OrderByDescending(g => g.Count)
                .Take(limit)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"=== SQL GROUP BY RECENT LOGS (Top {groups.Count} Groups) ===");
            sb.AppendLine(string.Format("{0,-7} | {1,-6} | {2,-18} | {3}", "COUNT", "LEVEL", "SOURCE", "MESSAGE"));
            sb.AppendLine(new string('-', 78));

            foreach (var row in groups)
            {
                string src = row.SourceTag;
                if (src.Length > 18) src = src.Substring(0, 15) + "...";
                string msg = row.Message;
                if (msg.Length > 44) msg = msg.Substring(0, 41) + "...";

                sb.AppendLine(string.Format("{0,-7} | {1,-6} | {2,-18} | {3}", 
                    row.Count, 
                    row.Level.ToString().ToUpperInvariant(), 
                    src, 
                    msg));
            }

            sb.AppendLine(new string('-', 78));
            sb.AppendLine($"Total log events tracked: {_history.Count} | Distinct signatures: {groups.Count} | Deduplication (Lag): {(LagDeduplicationEnabled ? "ACTIVE" : "OFF")}");
            return sb.ToString();
        }
    }

    public static string GetModRootDirectory()
    {
        try
        {
            string? assemblyLocation = typeof(ModLogger).Assembly.Location;
            if (!string.IsNullOrEmpty(assemblyLocation))
            {
                string? dir = Path.GetDirectoryName(assemblyLocation);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    return dir;
                }
            }
        }
        catch { }

        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
            {
                return baseDir;
            }
        }
        catch { }

        try
        {
            string userDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDir) && Directory.Exists(userDir))
            {
                return userDir;
            }
        }
        catch { }

        return Directory.GetCurrentDirectory();
    }

    private static void EnsureFileLoggingInitialized()
    {
        if (_fileLoggingInitialized) return;

        lock (_fileLock)
        {
            if (_fileLoggingInitialized) return;
            _fileLoggingInitialized = true;

            try
            {
                string rootDir = GetModRootDirectory();
                _logFilePath = Path.Combine(rootDir, LogFileName);

                var fileStream = new FileStream(_logFilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.ReadWrite);
                _logWriter = new StreamWriter(fileStream, Encoding.UTF8)
                {
                    AutoFlush = true
                };

                string modeStr = IsDebugBuild ? "DEBUG BUILD (Forceful Verbose Logging Enabled)" : "RELEASE BUILD";
                string banner = 
                    "=================================================================\n" +
                    $" AIOTweaks Log File Initialized\n" +
                    $" Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                    $" Configuration: {modeStr}\n" +
                    $" Mod Root Directory: {rootDir}\n" +
                    $" Log File Path: {_logFilePath}\n" +
                    "=================================================================";

                _logWriter.WriteLine(banner);
                GD.Print($"{Tag} [INIT] File logging initialized at: {_logFilePath}");
            }
            catch (Exception ex)
            {
                GD.PushWarning($"{Tag} Failed to initialize debug log file: {ex.Message}");
            }
        }
    }

    private static void WriteToFile(string line)
    {
        try
        {
            if (!_fileLoggingInitialized)
            {
                EnsureFileLoggingInitialized();
            }

            if (_logWriter != null)
            {
                lock (_fileLock)
                {
                    _logWriter.WriteLine(line);
                }
            }
        }
        catch
        {
            // Do not throw from logging subsystem
        }
    }

    public static void Flush()
    {
        try
        {
            FlushLagBuffer();
            lock (_fileLock)
            {
                _logWriter?.Flush();
            }
        }
        catch { }
    }
}

