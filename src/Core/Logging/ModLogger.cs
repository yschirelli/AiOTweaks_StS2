using System;
using System.IO;
using System.Text;
using Godot;

namespace AIOTweaks.Core.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3
}

/// <summary>
/// Centralized logging utility for AIOTweaks mod with tag formatting, timestamping, and log level filtering.
/// Delivers real-time verbose diagnostic information to the in-game debug console, Godot terminal, and file output.
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

#if DEBUG
    public const bool IsDebugBuild = true;
#else
    public const bool IsDebugBuild = false;
#endif

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
            Log(LogLevel.Error, fullMessage);
        }
    }

    private static void Log(LogLevel level, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string formatted = $"{Tag} [{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}";

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
            lock (_fileLock)
            {
                _logWriter?.Flush();
            }
        }
        catch { }
    }
}

