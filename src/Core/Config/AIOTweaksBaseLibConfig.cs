using System;
using BaseLib.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using AIOTweaks.Hooks;
using AIOTweaks.UI.Menu;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Registers AIOTweaks directly with BaseLib's Mod Configuration screen so it appears
/// natively in the in-game Mod Configuration menu with full toggleable settings, sliders, and buttons.
/// Note: BaseLib requires all exposed configuration properties to be static.
/// </summary>
public sealed class AIOTweaksBaseLibConfig : SimpleModConfig
{
    public AIOTweaksBaseLibConfig()
    {
        ModId = "AIOTweaks";
    }

    [ConfigSection("General Settings")]
    public static bool ModEnabled
    {
        get => ConfigManager.Current.General.Enabled;
        set
        {
            ConfigManager.Current.General.Enabled = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSection("Keybindings & Overlay")]
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
    public static string QuickOpenShopKey
    {
        get => ConfigManager.Current.General.QuickOpenShopKey;
        set
        {
            ConfigManager.Current.General.QuickOpenShopKey = value?.Trim() ?? "";
            ConfigManager.SaveConfig();
        }
    }

    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    public static string QuickGodModeKey
    {
        get => ConfigManager.Current.General.QuickGodModeKey;
        set
        {
            ConfigManager.Current.General.QuickGodModeKey = value?.Trim() ?? "";
            ConfigManager.SaveConfig();
        }
    }

    [ConfigTextInput(TextInputPreset.Alphanumeric)]
    public static string QuickKillEnemiesKey
    {
        get => ConfigManager.Current.General.QuickKillEnemiesKey;
        set
        {
            ConfigManager.Current.General.QuickKillEnemiesKey = value?.Trim() ?? "";
            ConfigManager.SaveConfig();
        }
    }

    [ConfigButton("Open GUI Menu Overlay")]
    public static void OpenOverlayAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "OpenOverlayAction clicked. Opening ModSettingsDialog...");
        ModSettingsDialog.ShowDialog();
    }

    [ConfigButton("Reset GUI Position & Height")]
    public static void ResetGuiLayoutAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "ResetGuiLayoutAction clicked.");
        ModSettingsDialog.ResetWindowLayout();
        ModLogger.Info("AIOTweaks: GUI position and height reset to default via BaseLib Mod Config.");
    }

    [ConfigSection("God Mode & Combat Cheats")]
    public static bool GodMode
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

    public static bool OneHitKill
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

    public static bool InfiniteEnergy
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

    public static bool InfinitePotions
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

    public static bool NoCardExhaust
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
    public static int ExtraCardsDrawnPerTurn
    {
        get => ConfigManager.Current.CombatSandbox.BonusDrawPerTurn;
        set
        {
            ConfigManager.Current.CombatSandbox.BonusDrawPerTurn = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSection("Combat Scaling & Multipliers")]
    [ConfigSlider(0.0, 10.0, 0.1)]
    public static double PlayerDamageMultiplier
    {
        get => RunTweaksSaveManager.GetEffectivePreRunTweaks().PlayerDamageMultiplier;
        set
        {
            float val = (float)value;
            ConfigManager.Current.PreRunTweaks.PlayerDamageMultiplier = val;
            if (RunTweaksSaveManager.ActiveSnapshot?.PreRunTweaks != null)
            {
                RunTweaksSaveManager.ActiveSnapshot.PreRunTweaks.PlayerDamageMultiplier = val;
                RunTweaksSaveManager.SaveActiveSnapshot();
            }
            ConfigManager.SaveConfig();
            GameHelper.RefreshAllVisibleCards();
        }
    }

    [ConfigSlider(0.0, 10.0, 0.1)]
    public static double PlayerDefendMultiplier
    {
        get => RunTweaksSaveManager.GetEffectivePreRunTweaks().PlayerDefendMultiplier;
        set
        {
            float val = (float)value;
            ConfigManager.Current.PreRunTweaks.PlayerDefendMultiplier = val;
            if (RunTweaksSaveManager.ActiveSnapshot?.PreRunTweaks != null)
            {
                RunTweaksSaveManager.ActiveSnapshot.PreRunTweaks.PlayerDefendMultiplier = val;
                RunTweaksSaveManager.SaveActiveSnapshot();
            }
            ConfigManager.SaveConfig();
            GameHelper.RefreshAllVisibleCards();
        }
    }

    [ConfigSlider(0.1, 10.0, 0.1)]
    public static double EnemyHealthMultiplier
    {
        get => RunTweaksSaveManager.GetEffectivePreRunTweaks().EnemyHealthMultiplier;
        set
        {
            float val = (float)value;
            ConfigManager.Current.PreRunTweaks.EnemyHealthMultiplier = val;
            if (RunTweaksSaveManager.ActiveSnapshot?.PreRunTweaks != null)
            {
                RunTweaksSaveManager.ActiveSnapshot.PreRunTweaks.EnemyHealthMultiplier = val;
                RunTweaksSaveManager.SaveActiveSnapshot();
            }
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 10.0, 0.1)]
    public static double EnemyDamageMultiplier
    {
        get => RunTweaksSaveManager.GetEffectivePreRunTweaks().EnemyDamageMultiplier;
        set
        {
            float val = (float)value;
            ConfigManager.Current.PreRunTweaks.EnemyDamageMultiplier = val;
            if (RunTweaksSaveManager.ActiveSnapshot?.PreRunTweaks != null)
            {
                RunTweaksSaveManager.ActiveSnapshot.PreRunTweaks.EnemyDamageMultiplier = val;
                RunTweaksSaveManager.SaveActiveSnapshot();
            }
            ConfigManager.SaveConfig();
            GameHelper.RefreshAllVisibleCards();
        }
    }

    [ConfigSlider(0.0, 10.0, 0.1)]
    public static double EnemyDefendMultiplier
    {
        get => RunTweaksSaveManager.GetEffectivePreRunTweaks().EnemyDefendMultiplier;
        set
        {
            float val = (float)value;
            ConfigManager.Current.PreRunTweaks.EnemyDefendMultiplier = val;
            if (RunTweaksSaveManager.ActiveSnapshot?.PreRunTweaks != null)
            {
                RunTweaksSaveManager.ActiveSnapshot.PreRunTweaks.EnemyDefendMultiplier = val;
                RunTweaksSaveManager.SaveActiveSnapshot();
            }
            ConfigManager.SaveConfig();
            GameHelper.RefreshAllVisibleCards();
        }
    }

    [ConfigSection("Economy & Multipliers")]
    [ConfigSlider(1.0, 10.0, 0.5)]
    public static double GoldRewardMultiplier
    {
        get => ConfigManager.Current.PreRunTweaks.GoldRewardMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.GoldRewardMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.1, 1.0, 0.05)]
    public static double ShopDiscountMultiplier
    {
        get => ConfigManager.Current.PreRunTweaks.ShopDiscountMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.ShopDiscountMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(1, 10, 1)]
    public static int CardRewardCount
    {
        get => ConfigManager.Current.PreRunTweaks.CardRewardCount;
        set
        {
            ConfigManager.Current.PreRunTweaks.CardRewardCount = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0, 1000, 50)]
    public static int StartingGoldBonus
    {
        get => ConfigManager.Current.PreRunTweaks.StartingGoldBonus;
        set
        {
            ConfigManager.Current.PreRunTweaks.StartingGoldBonus = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0, 200, 10)]
    public static int StartingMaxHpBonus
    {
        get => ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus;
        set
        {
            ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(1, 10, 1)]
    public static int PotionSlots
    {
        get => ConfigManager.Current.PreRunTweaks.PotionSlots;
        set
        {
            ConfigManager.Current.PreRunTweaks.PotionSlots = value;
            ConfigManager.SaveConfig();
        }
    }

    public static bool AllowMultipleRelics
    {
        get => ConfigManager.Current.PreRunTweaks.AllowMultipleRelics;
        set
        {
            ConfigManager.Current.PreRunTweaks.AllowMultipleRelics = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSection("Map & Spire Utilities")]
    public static bool ForceNeowBonus
    {
        get => ConfigManager.Current.PreRunTweaks.ForceNeowBonus;
        set
        {
            ConfigManager.Current.PreRunTweaks.ForceNeowBonus = value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(1.0, 5.0, 0.5)]
    public static double EliteNodeEncounterRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EliteWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EliteWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public static double ShopNodeRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.ShopWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.ShopWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public static double EventNodeRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EventWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EventWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(1.0, 5.0, 0.5)]
    public static double RestSiteRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.RestSiteWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.RestSiteWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public static double CombatNodeRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.CombatWeightMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.CombatWeightMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigSlider(0.0, 5.0, 0.5)]
    public static double TreasureRoomRate
    {
        get => ConfigManager.Current.PreRunTweaks.MapNodeDistribution.TreasureRoomMultiplier;
        set
        {
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.TreasureRoomMultiplier = (float)value;
            ConfigManager.SaveConfig();
        }
    }

    [ConfigButton("Reset All Cheats & State")]
    public static void ResetStateAction()
    {
        ModLogger.Verbose("AIOTweaksBaseLibConfig", "ResetStateAction clicked.");
        RuntimeStateManager.ResetSessionState();
        ModLogger.Info("AIOTweaks: Cheats & session state reset via BaseLib Mod Config.");
    }
}
