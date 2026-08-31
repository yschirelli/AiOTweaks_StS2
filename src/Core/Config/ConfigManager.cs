using System;
using System.IO;
using System.Text.Json;
using AIOTweaks.Core.Logging;
using Godot;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Manages reading, writing, caching, and fallback validation of AIOTweaks configuration.
/// </summary>
public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static ModConfig Current { get; private set; } = new();
    public static RunSettings ActiveRunSettings { get; private set; } = new();

    public static event Action<ModConfig>? OnConfigChanged;

    private static string GetConfigFilePath()
    {
        ModLogger.Verbose("ConfigManager", "Resolving configuration file path...");
        try
        {
            // Try Godot's OS user data directory first, fallback to current working directory
            string userDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDir) && Directory.Exists(userDir))
            {
                string configDir = Path.Combine(userDir, "AIOTweaks");
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                    ModLogger.Verbose("ConfigManager", $"Created configuration directory at: {configDir}");
                }
                string userPath = Path.Combine(configDir, "config.json");
                ModLogger.Verbose("ConfigManager", $"Resolved primary user configuration path: {userPath}");
                return userPath;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ConfigManager native OS lookup notice: {ex.Message}");
        }

        // Fallback relative path
        string localDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
        if (!Directory.Exists(localDir))
        {
            Directory.CreateDirectory(localDir);
            ModLogger.Verbose("ConfigManager", $"Created fallback config directory at: {localDir}");
        }
        string fallbackPath = Path.Combine(localDir, "default_config.json");
        ModLogger.Verbose("ConfigManager", $"Resolved fallback configuration path: {fallbackPath}");
        return fallbackPath;
    }

    public static void Initialize()
    {
        ModLogger.Verbose("ConfigManager", "Initializing ConfigManager subsystem...");
        LoadConfig();
    }

    public static void EnsureHotkeyFailsafes()
    {
        if (Current.General == null)
        {
            Current.General = new GeneralConfig();
        }
        if (string.IsNullOrWhiteSpace(Current.General.ConsoleHotkey) || Current.General.ConsoleHotkey.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("ConfigManager", $"Console hotkey empty/None; defaulting to failsafe '{GeneralConfig.DefaultConsoleHotkey}'.");
            Current.General.ConsoleHotkey = GeneralConfig.DefaultConsoleHotkey;
        }
        if (string.IsNullOrWhiteSpace(Current.General.GuiOverlayHotkey) || Current.General.GuiOverlayHotkey.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            ModLogger.Verbose("ConfigManager", $"GUI overlay hotkey empty/None; defaulting to failsafe '{GeneralConfig.DefaultGuiOverlayHotkey}'.");
            Current.General.GuiOverlayHotkey = GeneralConfig.DefaultGuiOverlayHotkey;
        }
    }

    public static void LoadConfig()
    {
        string path = GetConfigFilePath();
        ModLogger.Verbose("ConfigManager", $"Attempting to load configuration from path: '{path}'");
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                ModLogger.Verbose("ConfigManager", $"Read {json.Length} bytes of JSON configuration.");
                ModConfig? loaded = JsonSerializer.Deserialize<ModConfig>(json, JsonOptions);
                if (loaded != null)
                {
                    Current = loaded;
                    EnsureHotkeyFailsafes();
#if DEBUG
                    Current.General.DebugLogging = true;
                    ModLogger.MinimumLevel = LogLevel.Debug;
                    ModLogger.FileLoggingEnabled = true;
#else
                    ModLogger.MinimumLevel = Current.General.DebugLogging ? LogLevel.Debug : LogLevel.Info;
#endif
                    ModLogger.Info($"Loaded configuration successfully from: {path} (DebugLogging={Current.General.DebugLogging}, MinimumLevel={ModLogger.MinimumLevel})");
                    OnConfigChanged?.Invoke(Current);
                    return;
                }
            }

            ModLogger.Warn($"Configuration file not found or empty at {path}. Generating default configuration.");
            Current = new ModConfig();
            EnsureHotkeyFailsafes();
#if DEBUG
            Current.General.DebugLogging = true;
            ModLogger.MinimumLevel = LogLevel.Debug;
            ModLogger.FileLoggingEnabled = true;
#endif
            SaveConfig();
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to parse configuration file at {path}. Reverting to safe defaults.", ex);
            Current = new ModConfig();
            EnsureHotkeyFailsafes();
#if DEBUG
            Current.General.DebugLogging = true;
            ModLogger.MinimumLevel = LogLevel.Debug;
            ModLogger.FileLoggingEnabled = true;
#endif
            OnConfigChanged?.Invoke(Current);
        }
    }

    public static void SaveConfig()
    {
        EnsureHotkeyFailsafes();
        string path = GetConfigFilePath();
        ModLogger.Verbose("ConfigManager", $"Saving configuration to: '{path}'");
        try
        {
            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(path, json);
            ModLogger.Info($"Saved configuration ({json.Length} bytes) to: {path}");
            OnConfigChanged?.Invoke(Current);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to save configuration to: {path}", ex);
        }
    }

    public static void UpdateActiveRunSettings(RunSettings settings)
    {
        ModLogger.Verbose("ConfigManager", $"Updating ActiveRunSettings with profile '{settings?.ProfileName}' (GoldMult={settings?.GoldMultiplier}, EliteMult={settings?.EliteSpawnMultiplier})...");
        ActiveRunSettings = settings?.Clone() ?? new RunSettings();
        ModLogger.Info($"Active Run Settings updated for profile: {ActiveRunSettings.ProfileName}");
    }

    public static void ResetRunSettingsToDefault()
    {
        ModLogger.Verbose("ConfigManager", "Resetting ActiveRunSettings to default profile...");
        ActiveRunSettings = new RunSettings();
        ModLogger.Info("Active Run Settings reset to defaults.");
    }
}
