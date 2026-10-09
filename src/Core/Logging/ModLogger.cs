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

public enum ErrorType
{
    General,
    Syntax,
    NotFound,
    Validation,
    Exception,
    Critical
}

/// <summary>
/// Centralized logging utility for AIOTweaks mod with tag formatting, timestamping, and log level filtering.
/// Delivers real-time diagnostic information to the in-game debug console, Godot terminal, and file output.
/// Features high-performance Lag-1 deduplication to suppress repeating log spam.
/// When compiled in DEBUG configuration, verbose logging and file logging to the mod's root folder are forcefully enabled by default.
/// In RELEASE configuration, debug messages are fully disabled and stripped, keeping only necessary diagnostic logs.
/// </summary>
public static class ModLogger
{
    private const string Tag = "[AIOTweaks]";
    private const string LogFileName = "aiotweaks_debug.log";
    private static readonly object _fileLock = new();
    private static string? _logFilePath;
    private static StreamWriter? _logWriter;
    private static bool _fileLoggingInitialized = false;

    private static readonly object _lagLock = new();
    private static string? _lastRawMessage;
    private static LogLevel _lastLogLevel = LogLevel.Debug;
    private static int _repeatCount = 0;
    private static DateTime _firstOccurrenceTime = DateTime.MinValue;
    private static DateTime _lastOccurrenceTime = DateTime.MinValue;
    private static readonly System.Threading.Timer _quietTimer = new(OnQuietTimerExpired, null, Timeout.Infinite, Timeout.Infinite);

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

    private static LogLevel _minimumLevel = IsDebugBuild ? LogLevel.Debug : LogLevel.Info;

    /// <summary>
    /// Minimum log level for filtering. In debug builds, defaults to Debug so all verbose diagnostics are captured.
    /// In release builds, debug messages are disabled and cannot be set below Info.
    /// </summary>
    public static LogLevel MinimumLevel
    {
        get => _minimumLevel;
        set
        {
#if !DEBUG
            if (value < LogLevel.Info)
            {
                _minimumLevel = LogLevel.Info;
                return;
            }
#endif
            _minimumLevel = value;
        }
    }

    public static event Action<LogLevel, string>? OnLogged;

    [System.Diagnostics.Conditional("DEBUG")]
    public static void Debug(string message)
    {
        if (MinimumLevel <= LogLevel.Debug)
        {
            Log(LogLevel.Debug, message);
        }
    }

    [System.Diagnostics.Conditional("DEBUG")]
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

    /// <summary>
    /// Resolves BBCode color text representing the category of the given error message.
    /// </summary>
    public static string GetErrorColor(string message, ErrorType type = ErrorType.General)
    {
        if (type == ErrorType.Critical ||
            message.Contains("CRASH", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("INVARIANT", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("REGRESSION", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
        {
            return "magenta";
        }

        if (type == ErrorType.Exception ||
            message.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("NullReference", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("StackTrace", StringComparison.OrdinalIgnoreCase))
        {
            return "crimson";
        }

        if (type == ErrorType.Syntax ||
            message.Contains("Usage:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Syntax", StringComparison.OrdinalIgnoreCase))
        {
            return "orange";
        }

        if (type == ErrorType.NotFound ||
            message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("No active", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Ensure you are in an active run", StringComparison.OrdinalIgnoreCase))
        {
            return "tomato";
        }

        if (type == ErrorType.Validation ||
            message.Contains("Unknown", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Invalid", StringComparison.OrdinalIgnoreCase))
        {
            return "coral";
        }

        return "red"; // Standard operation failures
    }

    private static void Log(LogLevel level, string message)
    {
        if (string.IsNullOrEmpty(message)) return;

#if !DEBUG
        // In release builds, disable all debug messages; only keep necessary (Info, Warn, Error)
        if (level == LogLevel.Debug) return;
#endif

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

                    if (pendingRepeatMessage == null)
                    {
                        return;
                    }
                }
                else
                {
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
#if DEBUG
                    GD.Print(formatted);
#endif
                    break;
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

    private static string TruncatePreview(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength - 3) + "...";
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
#if DEBUG
                GD.Print($"{Tag} [INIT] File logging initialized at: {_logFilePath}");
#endif
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

