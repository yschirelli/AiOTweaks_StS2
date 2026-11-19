using System;
using System.Collections.Generic;
using System.Text.Json;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
};

int passed = 0;
int failed = 0;

void Run(string name, Action test)
{
    try
    {
        test();
        passed++;
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"[FAIL] {name}: {ex.Message}");
    }
}

void AssertEqual<T>(T expected, T actual, string? msg = null)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"{msg ?? "Assertion failed"}: expected '{expected}', got '{actual}'");
    }
}

void AssertTrue(bool condition, string msg)
{
    if (!condition)
    {
        throw new Exception(msg);
    }
}

Run("ModConfig: editing consoleHotkey is not overwritten by legacy toggleOverlayKey", () =>
{
    string json = """
    {
      "general": {
        "consoleHotkey": "F2",
        "toggleOverlayKey": "F1"
      }
    }
    """;
    var cfg = JsonSerializer.Deserialize<ModConfig>(json, jsonOptions)!;
    AssertEqual("F2", cfg.General.ConsoleHotkey);

    string resaved = JsonSerializer.Serialize(cfg, jsonOptions);
    AssertTrue(!resaved.Contains("toggleOverlayKey"), "Saved config should omit legacy toggleOverlayKey");
});

Run("ModConfig: editing consoleHotkey back to default F1 is not overwritten by old toggleOverlayKey", () =>
{
    string json = """
    {
      "general": {
        "consoleHotkey": "F1",
        "toggleOverlayKey": "F9"
      }
    }
    """;
    var cfg = JsonSerializer.Deserialize<ModConfig>(json, jsonOptions)!;
    AssertEqual("F1", cfg.General.ConsoleHotkey);
});

Run("ModConfig: legacy file with only toggleOverlayKey migrates into ConsoleHotkey", () =>
{
    string json = """
    {
      "general": {
        "toggleOverlayKey": "F6"
      }
    }
    """;
    var cfg = JsonSerializer.Deserialize<ModConfig>(json, jsonOptions)!;
    AssertEqual("F6", cfg.General.ConsoleHotkey);
});

Run("ModConfig: ignores removed legacy keys without error and clamps MapRoomCount", () =>
{
    string json = """
    {
      "general": { "enabled": true, "debugLogging": true },
      "preRunTweaks": { "mapRoomCount": 999 },
      "combatSandbox": { "maxHandSizeOverride": 12 },
      "ui": { "overlayScale": 1.5, "overlayOpacity": 0.8, "enableAudioCues": false }
    }
    """;
    var cfg = JsonSerializer.Deserialize<ModConfig>(json, jsonOptions)!;
    AssertEqual(50, cfg.PreRunTweaks.MapRoomCount);
    cfg.PreRunTweaks.MapRoomCount = 3;
    AssertEqual(15, cfg.PreRunTweaks.MapRoomCount);
});

Run("ModMath: CalculateTreasureRows vanilla 15 floors uses row 8", () =>
{
    var rows = ModMath.CalculateTreasureRows(15, 1.0f);
    AssertEqual(1, rows.Count);
    AssertEqual(8, rows[0]);
});

Run("ModMath: CalculateTreasureRows zero multiplier returns empty and large maps stay in bounds", () =>
{
    AssertEqual(0, ModMath.CalculateTreasureRows(15, 0.0f).Count);
    AssertEqual(0, ModMath.CalculateTreasureRows(3, 1.0f).Count);

    var large = ModMath.CalculateTreasureRows(50, 5.0f);
    AssertTrue(large.Count > 1, "Expected multiple treasure rows on 50-floor 5x map");
    for (int i = 0; i < large.Count; i++)
    {
        AssertTrue(large[i] >= 2 && large[i] <= 48, $"Row {large[i]} out of [2, 48]");
        if (i > 0) AssertTrue(large[i] > large[i - 1], "Treasure rows must be strictly increasing");
    }
});

Run("ModMath: ComputeEndlessMultiplier scales per loop and respects bounds", () =>
{
    AssertEqual(1.0f, ModMath.ComputeEndlessMultiplier(1.0f, false, 2.0f, 5, 0.01f));
    AssertEqual(1.0f, ModMath.ComputeEndlessMultiplier(1.0f, true, 2.0f, 0, 0.01f));
    AssertEqual(8.0f, ModMath.ComputeEndlessMultiplier(1.0f, true, 2.0f, 3, 0.01f));
    AssertEqual(0.01f, ModMath.ComputeEndlessMultiplier(0.0f, true, 2.0f, 2, 0.01f));
});

Run("ModMath: ComputeOneHitKillDamage kills high-HP Endless enemies", () =>
{
    AssertEqual(0, ModMath.ComputeOneHitKillDamage(0, 500));
    AssertEqual(99999, ModMath.ComputeOneHitKillDamage(6, 50));
    AssertEqual(250000, ModMath.ComputeOneHitKillDamage(6, 250000));
    AssertEqual(500000, ModMath.ComputeOneHitKillDamage(500000, 250000));
});

Run("ModMath: ComputeGoldDrop and ComputeShopPrice", () =>
{
    AssertEqual(50, ModMath.ComputeGoldDrop(25, 2.0f));
    AssertEqual(777, ModMath.ComputeGoldDrop(25, 2.0f, forcedAmount: 777));
    AssertEqual(1, ModMath.ComputeShopPrice(5, 0.05f));
    AssertEqual(50, ModMath.ComputeShopPrice(100, 0.5f));
});

Run("TextFormatting: FormatBbCodeForGodot maps colors, strips anim tags, sizes img", () =>
{
    string input = " [jitter][gold]Deal 6[/gold][/jitter] [img]res://icon.png[/img] ";
    string formatted = TextFormatting.FormatBbCodeForGodot(input);
    AssertEqual("[color=#EFC851]Deal 6[/color] [img=18x18]res://icon.png[/img]", formatted);
});

Run("TextFormatting: CleanBbCode converts icons and removes tags", () =>
{
    string input = "[gold]Gain[/gold] [img=18x18]res://energy_icon.png[/img] and [img]res://star_icon.png[/img]\n\n\nEnd";
    string cleaned = TextFormatting.CleanBbCode(input);
    AssertEqual("Gain Energy and Stars\n\nEnd", cleaned);
});

Run("TextFormatting: ResolveDynamicVariables handles plurals, icons, and vars", () =>
{
    var vars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
    {
        ["Damage"] = 12,
        ["Cards"] = 1,
        ["Energy"] = 2
    };
    string input = "Deal {Damage:diff()} damage, draw {Cards} {Cards:plural:card|cards}, gain {Energy:energyIcons()}.";
    string resolved = TextFormatting.ResolveDynamicVariables(input, vars);
    AssertEqual("Deal 12 damage, draw 1 card, gain 2 [img=18x18]res://images/packed/sprite_fonts/colorless_energy_icon.png[/img].", resolved);
});

Console.WriteLine($"\nSummary: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;
