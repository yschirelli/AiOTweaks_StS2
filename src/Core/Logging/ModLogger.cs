using System;
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
/// Delivers real-time verbose diagnostic information to the in-game debug console and Godot terminal.
/// </summary>
public static class ModLogger
{
    private const string Tag = "[AIOTweaks]";
    
    /// <summary>
    /// Minimum log level for filtering. Defaults to Debug so all verbose function executions are captured.
    /// </summary>
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

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
    }
}

