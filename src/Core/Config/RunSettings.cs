using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AIOTweaks.Core.Config;

/// <summary>
/// Active run-level customization profile that can be adjusted pre-run and saved/loaded.
/// </summary>
public sealed class RunSettings
{
    [JsonPropertyName("profileName")]
    public string ProfileName { get; set; } = "Default Profile";

    [JsonPropertyName("lastModifiedUtc")]
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("activeModifiers")]
    public List<string> ActiveModifiers { get; set; } = new();

    [JsonPropertyName("startingDeckPreset")]
    public string StartingDeckPreset { get; set; } = "Standard";

    [JsonPropertyName("customStartingCards")]
    public List<string> CustomStartingCards { get; set; } = new();

    [JsonPropertyName("customStartingRelics")]
    public List<string> CustomStartingRelics { get; set; } = new();

    [JsonPropertyName("goldMultiplier")]
    public float GoldMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("eliteSpawnMultiplier")]
    public float EliteSpawnMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("shopSpawnMultiplier")]
    public float ShopSpawnMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("eventSpawnMultiplier")]
    public float EventSpawnMultiplier { get; set; } = 1.0f;

    [JsonPropertyName("cardRewardCount")]
    public int CardRewardCount { get; set; } = 3;

    [JsonPropertyName("draftModeEnabled")]
    public bool DraftModeEnabled { get; set; } = false;

    [JsonPropertyName("allowMultipleRelics")]
    public bool AllowMultipleRelics { get; set; } = false;

    [JsonPropertyName("potionSlots")]
    public int PotionSlots { get; set; } = 3;

    public RunSettings Clone()
    {
        return new RunSettings
        {
            ProfileName = ProfileName,
            LastModifiedUtc = DateTime.UtcNow,
            ActiveModifiers = new List<string>(ActiveModifiers),
            StartingDeckPreset = StartingDeckPreset,
            CustomStartingCards = new List<string>(CustomStartingCards),
            CustomStartingRelics = new List<string>(CustomStartingRelics),
            GoldMultiplier = GoldMultiplier,
            EliteSpawnMultiplier = EliteSpawnMultiplier,
            ShopSpawnMultiplier = ShopSpawnMultiplier,
            EventSpawnMultiplier = EventSpawnMultiplier,
            CardRewardCount = CardRewardCount,
            DraftModeEnabled = DraftModeEnabled,
            AllowMultipleRelics = AllowMultipleRelics,
            PotionSlots = PotionSlots
        };
    }
}
