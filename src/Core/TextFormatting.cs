using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AIOTweaks.Core;

/// <summary>
/// Pure C# BBCode and dynamic-variable text formatting helpers with compile-time generated regexes.
/// </summary>
public static partial class TextFormatting
{
    private static readonly Dictionary<string, string> ColorTagHexMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#FF5555",
        ["darkRed"] = "#C83232",
        ["gold"] = "#EFC851",
        ["yellow"] = "#FFFF55",
        ["blue"] = "#87CEEB",
        ["darkBlue"] = "#67AEEB",
        ["green"] = "#7FFF00",
        ["darkGreen"] = "#32CD32",
        ["aqua"] = "#2AEBBE",
        ["cyan"] = "#00FFFF",
        ["orange"] = "#FFA518",
        ["pink"] = "#FF78A0",
        ["purple"] = "#EE82EE",
        ["cream"] = "#FFF6E2",
        ["white"] = "#FFFFFF",
        ["gray"] = "#A0A0A0",
        ["grey"] = "#A0A0A0"
    };

    [GeneratedRegex(@"\[(/?)(red|darkRed|gold|yellow|blue|darkBlue|green|darkGreen|aqua|cyan|orange|pink|purple|cream|white|gray|grey)\]", RegexOptions.IgnoreCase)]
    private static partial Regex ColorTagRegex();

    [GeneratedRegex(@"\[/?(?:jitter|sine|fadein|flyin|scramble|thinkydots|wave|rainbow)\]", RegexOptions.IgnoreCase)]
    private static partial Regex AnimTagRegex();

    [GeneratedRegex(@"\[img\](res://[^\s\]]+)\[/img\]", RegexOptions.IgnoreCase)]
    private static partial Regex PlainImgTagRegex();

    [GeneratedRegex(@"\[img[^\]]*\]res://[^\s\]]*star_icon\.png\[/img\]", RegexOptions.IgnoreCase)]
    private static partial Regex StarIconImgRegex();

    [GeneratedRegex(@"\[img[^\]]*\]res://[^\s\]]*energy_icon\.png\[/img\]", RegexOptions.IgnoreCase)]
    private static partial Regex EnergyIconImgRegex();

    [GeneratedRegex(@"\[img[^\]]*\][^\[]*\[/img\]", RegexOptions.IgnoreCase)]
    private static partial Regex AnyImgTagRegex();

    [GeneratedRegex(@"\[/?[a-zA-Z0-9_#=]+(?:\s+[^\]]*)?\]")]
    private static partial Regex AnyBbCodeTagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultiNewlineRegex();

    [GeneratedRegex(@"\{(\w+):starIcons(?:\(\))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex StarIconsVarRegex();

    [GeneratedRegex(@"\{(\w+):energyIcons(?:\([^\)]*\))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex EnergyIconsVarRegex();

    [GeneratedRegex(@"\{(\w+):plural:([^\|\}]+)\|([^\|\}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex PluralVarRegex();

    [GeneratedRegex(@"\{(\w+):(diff|inverseDiff)(?:\(\))?\}", RegexOptions.IgnoreCase)]
    private static partial Regex DiffVarRegex();

    [GeneratedRegex(@"\[stars?\]|\[starts?\]", RegexOptions.IgnoreCase)]
    private static partial Regex LiteralStarTagRegex();

    public static string FormatBbCodeForGodot(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        text = ColorTagRegex().Replace(text, m =>
        {
            if (m.Groups[1].Value == "/")
            {
                return "[/color]";
            }
            string tag = m.Groups[2].Value;
            return ColorTagHexMap.TryGetValue(tag, out string? hex) ? $"[color={hex}]" : m.Value;
        });

        text = AnimTagRegex().Replace(text, "");
        text = PlainImgTagRegex().Replace(text, "[img=18x18]$1[/img]");

        return text.Trim();
    }

    public static string CleanBbCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        text = StarIconImgRegex().Replace(text, "Stars");
        text = EnergyIconImgRegex().Replace(text, "Energy");
        text = AnyImgTagRegex().Replace(text, "");

        string cleaned = AnyBbCodeTagRegex().Replace(text, "");
        cleaned = MultiNewlineRegex().Replace(cleaned, "\n\n");
        return cleaned.Trim();
    }

    public static string ResolveDynamicVariables(string text, Dictionary<string, decimal>? vars)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";

        text = StarIconsVarRegex().Replace(text, m =>
        {
            string varName = m.Groups[1].Value;
            if (vars != null && vars.TryGetValue(varName, out decimal v))
            {
                int count = (int)v;
                return $"{count} [img=18x18]res://images/packed/sprite_fonts/star_icon.png[/img]";
            }
            return "[img=18x18]res://images/packed/sprite_fonts/star_icon.png[/img]";
        });

        text = EnergyIconsVarRegex().Replace(text, m =>
        {
            string varName = m.Groups[1].Value;
            if (vars != null && vars.TryGetValue(varName, out decimal v))
            {
                int count = (int)v;
                return $"{count} [img=18x18]res://images/packed/sprite_fonts/colorless_energy_icon.png[/img]";
            }
            return "[img=18x18]res://images/packed/sprite_fonts/colorless_energy_icon.png[/img]";
        });

        text = PluralVarRegex().Replace(text, m =>
        {
            string varName = m.Groups[1].Value;
            string one = m.Groups[2].Value;
            string many = m.Groups[3].Value;
            if (vars != null && vars.TryGetValue(varName, out decimal v))
            {
                return (v == 1) ? one : many;
            }
            return many;
        });

        text = DiffVarRegex().Replace(text, m =>
        {
            string varName = m.Groups[1].Value;
            if (vars != null && vars.TryGetValue(varName, out decimal v))
            {
                return v.ToString();
            }
            return m.Value;
        });

        text = text.Replace("{singleStarIcon}", "[img=18x18]res://images/packed/sprite_fonts/star_icon.png[/img]", StringComparison.OrdinalIgnoreCase);
        text = text.Replace("{energyPrefix}", "colorless", StringComparison.OrdinalIgnoreCase);

        if (vars != null && vars.Count > 0)
        {
            foreach (var kvp in vars)
            {
                text = text.Replace($"{{{kvp.Key}}}", kvp.Value.ToString(), StringComparison.OrdinalIgnoreCase);
            }
        }

        text = LiteralStarTagRegex().Replace(text, _ =>
        {
            if (vars != null && (vars.TryGetValue("Stars", out decimal v) || vars.TryGetValue("Star", out v)))
            {
                return $"{((int)v)} [img=18x18]res://images/packed/sprite_fonts/star_icon.png[/img]";
            }
            return "[img=18x18]res://images/packed/sprite_fonts/star_icon.png[/img]";
        });

        return text;
    }
}
