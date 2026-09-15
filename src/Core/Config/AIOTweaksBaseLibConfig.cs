using System;
using BaseLib.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.UI.Menu;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Registers AIOTweaks directly with BaseLib's Mod Configuration screen.
/// Minimal configuration: Menu Hotkey, Console Hotkey, and Reset GUI Position button.
/// Note: BaseLib requires all exposed configuration properties to be static.
/// </summary>
public sealed class AIOTweaksBaseLibConfig : SimpleModConfig
{
    public AIOTweaksBaseLibConfig()
    {
        ModId = "AIOTweaks";
    }

    [ConfigSection("Keybindings & Overlay")]
    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    public static string GuiOverlayHotkey
    {
        get => ConfigManager.Current.General.GuiOverlayHotkey;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ConfigManager.Current.General.GuiOverlayHotkey = value.Trim();
                ConfigManager.SaveConfig();
            }
        }
    }

    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    public static string ConsoleHotkey
    {
        get => ConfigManager.Current.General.ConsoleHotkey;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ConfigManager.Current.General.ConsoleHotkey = value.Trim();
                ConfigManager.SaveConfig();
            }
        }
    }

    [ConfigButton("Reset GUI Position")]
    public static void ResetGuiLayoutAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "ResetGuiLayoutAction clicked.");
        ModSettingsDialog.ResetWindowLayout();
        ModLogger.Info("AIOTweaks: GUI position and size reset to default via BaseLib Mod Config.");
    }
}
