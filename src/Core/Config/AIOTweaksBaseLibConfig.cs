using System;
using BaseLib.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using AIOTweaks.UI.Menu;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Registers AIOTweaks directly with BaseLib's Mod Configuration screen so it appears
/// natively in the in-game Mod Configuration menu with full toggleable settings, sliders, and buttons.
/// </summary>
public sealed class AIOTweaksBaseLibConfig : SimpleModConfig
{
    public AIOTweaksBaseLibConfig()
    {
        ModId = "AIOTweaks";
    }

    [ConfigSection("General Settings")]
    public bool ModEnabled
    {
        get => ConfigManager.Current.General.Enabled;
        set
        {
            ConfigManager.Current.General.Enabled = value;
            ConfigManager.SaveConfig();
        }
    }

    public bool DebugLogging
    {
        get => ConfigManager.Current.General.DebugLogging;
        set
        {
            ConfigManager.Current.General.DebugLogging = value;
            ModLogger.MinimumLevel = value ? LogLevel.Debug : LogLevel.Info;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSection("Keybindings & Overlay")]
    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    public string ConsoleHotkey
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

    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    public string GuiOverlayHotkey
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
    public string QuickOpenShopKey
    {
        get => ConfigManager.Current.General.QuickOpenShopKey;
        set
        {
            ConfigManager.Current.General.QuickOpenShopKey = value?.Trim() ?? "";
            ConfigManager.SaveConfig();
        }
    }

    [ConfigButton("Open GUI Menu Overlay")]
    public void OpenOverlayAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "OpenOverlayAction clicked. Opening ModSettingsDialog...");
        ModSettingsDialog.ShowDialog();
    }

    [ConfigButton("Reset GUI Position & Height")]
    public void ResetGuiLayoutAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "ResetGuiLayoutAction clicked.");
        ModSettingsDialog.ResetWindowLayout();
        ModLogger.Info("AIOTweaks: GUI position and height reset to default via BaseLib Mod Config.");
    }

    [ConfigSection("God Mode & Combat Cheats")]
    public bool GodMode
    {
        get => RuntimeStateManager.GodModeEnabled || ConfigManager.Current.CombatSandbox.GodMode;
        set
        {
            ModLogger.Verbose("AIOTweaksBaseLibConfig", $"GodMode BaseLib set: {value}");
            RuntimeStateManager.GodModeEnabled = value;
            ConfigManager.Current.CombatSandbox.GodMode = value;
            ConfigManager.SaveConfig();
        }
    }

    public bool OneHitKill
    {
        get => RuntimeStateManager.OneHitKillEnabled || ConfigManager.Current.CombatSandbox.OneHitKill;
        set
        {
            ModLogger.Verbose("AIOTweaksBaseLibConfig", $"OneHitKill BaseLib set: {value}");
            RuntimeStateManager.OneHitKillEnabled = value;
            ConfigManager.Current.CombatSandbox.OneHitKill = value;
            ConfigManager.SaveConfig();
        }
    }

    public bool InfiniteEnergy
    {
        get => RuntimeStateManager.InfiniteEnergyEnabled || ConfigManager.Current.CombatSandbox.InfiniteEnergy;
        set
        {
            ModLogger.Verbose("AIOTweaksBaseLibConfig", $"InfiniteEnergy BaseLib set: {value}");
            RuntimeStateManager.InfiniteEnergyEnabled = value;
            ConfigManager.Current.CombatSandbox.InfiniteEnergy = value;
            ConfigManager.SaveConfig();
        }
    }

    public bool InfinitePotions
    {
        get => RuntimeStateManager.InfinitePotionsEnabled || ConfigManager.Current.CombatSandbox.InfinitePotions;
        set
        {
            ModLogger.Verbose("AIOTweaksBaseLibConfig", $"InfinitePotions BaseLib set: {value}");
            RuntimeStateManager.InfinitePotionsEnabled = value;
            ConfigManager.Current.CombatSandbox.InfinitePotions = value;
            ConfigManager.SaveConfig();
        }
    }

    public bool NoCardExhaust
    {
        get => RuntimeStateManager.NoCardExhaustEnabled || ConfigManager.Current.CombatSandbox.NoCardExhaust;
        set
        {
            ModLogger.Verbose("AIOTweaksBaseLibConfig", $"NoCardExhaust BaseLib set: {value}");
            RuntimeStateManager.NoCardExhaustEnabled = value;
            ConfigManager.Current.CombatSandbox.NoCardExhaust = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0, 10, 1)]
    public int ExtraCardsDrawnPerTurn
    {
        get => ConfigManager.Current.CombatSandbox.BonusDrawPerTurn;
        set
        {
            ConfigManager.Current.CombatSandbox.BonusDrawPerTurn = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSection("Economy & Multipliers")]
    [ConfigSlider(1.0, 10.0, 0.5)]
    public double GoldRewardMultiplier
    {
        get => ConfigManager.Current.PreRunTweaks.GoldRewardMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.GoldRewardMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.1, 1.0, 0.05)]
    public double ShopDiscountMultiplier
    {
        get => ConfigManager.Current.PreRunTweaks.ShopDiscountMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.ShopDiscountMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(1, 10, 1)]
    public int CardRewardCount
    {
        get => ConfigManager.Current.PreRunTweaks.CardRewardCount;
        set
        {
            ConfigManager.Current.PreRunTweaks.CardRewardCount = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0, 1000, 50)]
    public int StartingGoldBonus
    {
        get => ConfigManager.Current.PreRunTweaks.StartingGoldBonus;
        set
        {
            ConfigManager.Current.PreRunTweaks.StartingGoldBonus = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0, 200, 10)]
    public int StartingMaxHpBonus
    {
        get => ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus;
        set
        {
            ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSection("Map & Spire Utilities")]
    [ConfigSlider(1.0, 5.0, 0.5)]
    public double EliteNodeEncounterRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EliteWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EliteWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public double ShopNodeRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.ShopWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.ShopWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public double EventNodeRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EventWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EventWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(1.0, 5.0, 0.5)]
    public double RestSiteRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.RestSiteWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.RestSiteWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public double CombatNodeRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.CombatWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.CombatWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public double TreasureRoomRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.TreasureRoomMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.TreasureRoomMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigButton("Reset All Cheats & State")]
    public void ResetStateAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "ResetStateAction clicked.");
        RuntimeStateManager.ResetSessionState();
        ModLogger.Info("AIOTweaks: Cheats & session state reset via BaseLib Mod Config.");
    }
}
