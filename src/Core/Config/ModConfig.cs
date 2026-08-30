using System;
using System.Text.Json.Serialization;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Root configuration schema for AIOTweaks mod.
/// </summary>
public sealed class ModConfig
{
    [JsonPropertyName("general")]
    public GeneralConfig General { get; set; } = new();

    [JsonPropertyName("preRunTweaks")]
    public PreRunTweaksConfig PreRunTweaks { get; set; } = new();

    [JsonPropertyName("combatSandbox")]
    public CombatSandboxConfig CombatSandbox { get; set; } = new();

    [JsonPropertyName("ui")]
    public UIConfig UI { get; set; } = new();
}

public sealed class GeneralConfig
{
    public const string DefaultConsoleHotkey = "F1";
    public const string DefaultGuiOverlayHotkey = "F3";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("debugLogging")]
    public bool DebugLogging { get; set; } = true;

    [JsonPropertyName("consoleHotkey")]
    public string ConsoleHotkey { get; set; } = DefaultConsoleHotkey;

    [JsonPropertyName("guiOverlayHotkey")]
    public string GuiOverlayHotkey { get; set; } = DefaultGuiOverlayHotkey;

    [JsonPropertyName("toggleOverlayKey")]
    public string? ToggleOverlayKey
    {
        get => ConsoleHotkey;
        set
        {
            if (value != null)
            {
                ConsoleHotkey = value;
            }
        }
    }

    [JsonPropertyName("quickGodModeKey")]
    public string QuickGodModeKey { get; set; } = "";

    [JsonPropertyName("quickKillEnemiesKey")]
    public string QuickKillEnemiesKey { get; set; } = "";
}

public sealed class PreRunTweaksConfig
{
    [JsonPropertyName("goldRewardMultiplier")]
    public float GoldRewardMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("shopDiscountMultiplier")]
    public float ShopDiscountMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("cardRewardCount")]
    public int CardRewardCount { get; set; } = 3;

    [JsonPropertyName("startingGoldBonus")]
    public int StartingGoldBonus { get; set; } = 0;

    [JsonPropertyName("startingMaxHpBonus")]
    public int StartingMaxHpBonus { get; set; } = 0;

    [JsonPropertyName("forceNeowBonus")]
    public bool ForceNeowBonus { get; set; } = true;

    private int _mapRoomCount = 15;

    [JsonPropertyName("mapRoomCount")]
    public int MapRoomCount
    {
        get => Math.Max(15, _mapRoomCount);
        set => _mapRoomCount = Math.Max(15, value);
    }

    [JsonPropertyName("enemyHealthMultiplier")]
    public float EnemyHealthMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("enemyDamageMultiplier")]
    public float EnemyDamageMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("enemyDefendMultiplier")]
    public float EnemyDefendMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("freeMapNavigation")]
    public bool FreeMapNavigation { get; set; } = false;

    [JsonPropertyName("endlessMode")]
    public EndlessModeConfig EndlessMode { get; set; } = new();

    [JsonPropertyName("mapNodeDistribution")]
    public MapNodeDistributionConfig MapNodeDistribution { get; set; } = new();
}

public sealed class EndlessModeConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;

    [JsonPropertyName("enemyScalingMultiplier")]
    public float EnemyScalingMultiplier { get; set; } = 2.0f;
}

public sealed class MapNodeDistributionConfig
{
    [JsonPropertyName("eliteWeightMultiplier")]
    public float EliteWeightMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("shopWeightMultiplier")]
    public float ShopWeightMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("eventWeightMultiplier")]
    public float EventWeightMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("restSiteWeightMultiplier")]
    public float RestSiteWeightMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("combatWeightMultiplier")]
    public float CombatWeightMultiplier { get; set; } = 1.0f;
}

public sealed class CombatSandboxConfig
{
    [JsonPropertyName("godMode")]
    public bool GodMode { get; set; } = false;

    [JsonPropertyName("infiniteEnergy")]
    public bool InfiniteEnergy { get; set; } = false;

    [JsonPropertyName("oneHitKill")]
    public bool OneHitKill { get; set; } = false;

    [JsonPropertyName("bonusDrawPerTurn")]
    public int BonusDrawPerTurn { get; set; } = 0;

    [JsonPropertyName("maxHandSizeOverride")]
    public int MaxHandSizeOverride { get; set; } = 10;

    [JsonPropertyName("infinitePotions")]
    public bool InfinitePotions { get; set; } = false;

    [JsonPropertyName("noCardExhaust")]
    public bool NoCardExhaust { get; set; } = false;
}

public sealed class UIConfig
{
    [JsonPropertyName("overlayScale")]
    public float OverlayScale { get; set; } = 1.0f;

    [JsonPropertyName("overlayOpacity")]
    public float OverlayOpacity { get; set; } = 0.95f;

    [JsonPropertyName("showDebugConsoleOnStart")]
    public bool ShowDebugConsoleOnStart { get; set; } = false;

    [JsonPropertyName("enableAudioCues")]
    public bool EnableAudioCues { get; set; } = true;
}
