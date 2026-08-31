using System;
using System.IO;
using System.Text.Json;
using AIOTweaks.Core.Logging;
using Godot;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Manages reading, writing, caching, and fallback validation of AIOTweaks configuration.
/// Saves and loads settings directly from the mod root directory (config.json) so settings persist across game sessions.
/// </summary>
public static class ConfigManager
{
    public const string ConfigFileName = "config.json";

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

    public static string GetConfigFilePath()
    {
        string rootDir = ModLogger.GetModRootDirectory();
        return Path.Combine(rootDir, ConfigFileName);
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
        string primaryPath = GetConfigFilePath();
        string rootDir = ModLogger.GetModRootDirectory();
        ModLogger.Verbose("ConfigManager", $"Attempting to load configuration from primary path: '{primaryPath}'");
        try
        {
            if (File.Exists(primaryPath))
            {
                string json = File.ReadAllText(primaryPath);
                ModLogger.Verbose("ConfigManager", $"Read {json.Length} bytes of JSON configuration from primary path.");
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
                    ModLogger.Info($"Loaded configuration successfully from root directory: {primaryPath} (DebugLogging={Current.General.DebugLogging}, MinimumLevel={ModLogger.MinimumLevel})");
                    OnConfigChanged?.Invoke(Current);
                    return;
                }
            }

            // Fallback checks if primary config.json in root does not exist yet:
            // Check config/config.json or config/default_config.json in mod root, or legacy user data dir
            string[] fallbackCandidates = new[]
            {
                Path.Combine(rootDir, "config", "config.json"),
                Path.Combine(rootDir, "config", "default_config.json"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "default_config.json"),
                Path.Combine(OS.GetUserDataDir(), "AIOTweaks", "config.json")
            };

            foreach (string fallbackPath in fallbackCandidates)
            {
                try
                {
                    if (!string.IsNullOrEmpty(fallbackPath) && File.Exists(fallbackPath))
                    {
                        ModLogger.Verbose("ConfigManager", $"Checking fallback configuration at: '{fallbackPath}'");
                        string json = File.ReadAllText(fallbackPath);
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
                            ModLogger.Info($"Loaded configuration from fallback source: {fallbackPath}. Migrating/saving to root directory: {primaryPath}");
                            SaveConfig();
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"ConfigManager fallback check note for '{fallbackPath}': {ex.Message}");
                }
            }

            ModLogger.Warn($"Configuration file not found. Generating default configuration at root directory: {primaryPath}");
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
            ModLogger.Error($"Failed to parse configuration file at {primaryPath}. Reverting to safe defaults.", ex);
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
        ModLogger.Verbose("ConfigManager", $"Saving configuration to root directory: '{path}'");
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(Current, JsonOptions);
            File.WriteAllText(path, json);
            ModLogger.Info($"Saved configuration ({json.Length} bytes) to root directory: {path}");
            OnConfigChanged?.Invoke(Current);
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Failed to save configuration to root directory: {path}", ex);
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
