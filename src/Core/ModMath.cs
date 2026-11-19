using System;
using System.Collections.Generic;

namespace AIOTweaks.Core;

/// <summary>
/// Pure C# gameplay calculation helpers for map treasure placement, economy scaling, and Endless combat multipliers.
/// Isolated from Godot/sts2 engine types so they can be unit-tested directly.
/// </summary>
public static class ModMath
{
    public static List<int> CalculateTreasureRows(int rowCount, float treasureMultiplier)
    {
        var rows = new List<int>();
        if (treasureMultiplier <= 0.001f || rowCount < 4)
        {
            return rows;
        }

        float effectiveMultiplier = (rowCount / 15.0f) * treasureMultiplier;
        int numTreasureRows = Math.Max(1, (int)Math.Round(effectiveMultiplier));

        int maxPossibleTreasureRows = Math.Max(1, rowCount - 3);
        numTreasureRows = Math.Min(numTreasureRows, maxPossibleTreasureRows);

        if (numTreasureRows == 1 && rowCount == 15 && Math.Abs(treasureMultiplier - 1.0f) < 0.001f)
        {
            rows.Add(8);
            return rows;
        }

        float step = (float)(rowCount - 1) / (numTreasureRows + 1);
        for (int i = 1; i <= numTreasureRows; i++)
        {
            int r = (int)Math.Round(i * step);
            r = Math.Clamp(r, 2, rowCount - 2);

            if (rows.Count > 0 && r <= rows[^1])
            {
                r = rows[^1] + 1;
            }

            if (r <= rowCount - 2)
            {
                rows.Add(r);
            }
        }

        return rows;
    }

    public static float ComputeEndlessMultiplier(float baseMultiplier, bool endlessEnabled, float endlessScaling, int loopCount, float minValue)
    {
        float result = baseMultiplier;
        if (endlessEnabled && loopCount > 0)
        {
            result *= (float)Math.Pow(endlessScaling, loopCount);
        }
        return Math.Max(minValue, result);
    }

    public static int ComputeOneHitKillDamage(int incomingDamage, int targetCurrentHp)
    {
        if (incomingDamage <= 0)
        {
            return incomingDamage;
        }
        return Math.Max(incomingDamage, Math.Max(targetCurrentHp, 99999));
    }

    public static int ComputeGoldDrop(int baseGold, float multiplier, int? forcedAmount = null)
    {
        if (forcedAmount.HasValue)
        {
            return forcedAmount.Value;
        }

        if (Math.Abs(multiplier - 1.0f) > 0.001f)
        {
            return (int)Math.Round(baseGold * multiplier);
        }

        return baseGold;
    }

    public static int ComputeShopPrice(int basePrice, float discountMultiplier)
    {
        if (Math.Abs(discountMultiplier - 1.0f) > 0.001f)
        {
            return Math.Max(1, (int)Math.Round(basePrice * discountMultiplier));
        }

        return basePrice;
    }
}
