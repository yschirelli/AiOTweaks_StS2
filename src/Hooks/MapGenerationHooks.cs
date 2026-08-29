using System;
using System.Reflection;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts map node generation weights (Elites, Shops, Unknowns, Rest Sites, Combats)
/// and enforces fair play by switching runs to Seeded/Custom mode when map generation is modified.
/// </summary>
public static class MapGenerationHooks
{
    private static readonly FieldInfo? ElitesField = typeof(MapPointTypeCounts).GetField("<NumOfElites>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ShopsField = typeof(MapPointTypeCounts).GetField("<NumOfShops>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? UnknownsField = typeof(MapPointTypeCounts).GetField("<NumOfUnknowns>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RestsField = typeof(MapPointTypeCounts).GetField("<NumOfRests>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);


    /// <summary>
    /// Evaluates if any map generation weights or multipliers have been modified from game default (1.0x).
    /// </summary>
    public static bool AreMapTweaksModified()
    {
        var dist = ConfigManager.Current.PreRunTweaks.MapNodeDistribution;
        var runSettings = ConfigManager.ActiveRunSettings;

        bool modified = Math.Abs(dist.EliteWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.ShopWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.EventWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.RestSiteWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.CombatWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(runSettings.EliteSpawnMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(runSettings.ShopSpawnMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(runSettings.EventSpawnMultiplier - 1.0f) > 0.001f;

        ModLogger.Verbose("MapGenerationHooks", $"AreMapTweaksModified check: {modified} (Elite={dist.EliteWeightMultiplier}x, Shop={dist.ShopWeightMultiplier}x, Event={dist.EventWeightMultiplier}x, Rest={dist.RestSiteWeightMultiplier}x)");
        return modified;
    }

    /// <summary>
    /// Enforces fair play on singleplayer/new run embarkation:
    /// If map generation tweaks are customized, switch GameMode to Custom (Seeded/Fair mode)
    /// to disable achievements and epoch unlocks.
    /// If tweaks are set to game default (1.0x), proceeds the run as normal.
    /// </summary>
    [HarmonyPatch(typeof(RunState), nameof(RunState.CreateForNewRun))]
    public static class RunStateCreateForNewRunPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref GameMode gameMode)
        {
            try
            {
                ModLogger.Verbose("MapGenerationHooks", $"RunState.CreateForNewRun prefix evaluating GameMode: incoming={gameMode}");
                if (AreMapTweaksModified())
                {
                    if (gameMode == GameMode.Standard)
                    {
                        gameMode = GameMode.Custom;
                        ModLogger.Info("Map generation tweaks are modified: Automatically set GameMode to Custom (Seeded/Fair Mode). Unlocks and achievements are locked for this run.");
                    }
                }
                else
                {
                    ModLogger.Info("Map generation tweaks are at game default (1.0x): Proceeding with standard run.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error evaluating fair play GameMode in RunState.CreateForNewRun", ex);
            }
        }
    }

    /// <summary>
    /// Enforces fair play on shared/multiplayer run creation.
    /// </summary>
    [HarmonyPatch(typeof(RunState), "CreateShared")]
    public static class RunStateCreateSharedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref GameMode gameMode)
        {
            try
            {
                ModLogger.Verbose("MapGenerationHooks", $"RunState.CreateShared prefix evaluating GameMode: incoming={gameMode}");
                if (AreMapTweaksModified())
                {
                    if (gameMode == GameMode.Standard)
                    {
                        gameMode = GameMode.Custom;
                        ModLogger.Info("Map generation tweaks are modified: Automatically set shared GameMode to Custom (Seeded/Fair Mode).");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error evaluating fair play GameMode in RunState.CreateShared", ex);
            }
        }
    }

    /// <summary>
    /// Adjusts generated map node counts according to configured multipliers during map construction.
    /// </summary>
    [HarmonyPatch(typeof(MapPointTypeCounts), MethodType.Constructor, new Type[] { typeof(int), typeof(int) })]
    public static class MapPointTypeCountsConstructorPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MapPointTypeCounts __instance, int unknownCount, int restCount)
        {
            try
            {
                if (!AreMapTweaksModified()) return;

                var dist = ConfigManager.Current.PreRunTweaks.MapNodeDistribution;
                var runSettings = ConfigManager.ActiveRunSettings;

                float eliteMult = dist.EliteWeightMultiplier * runSettings.EliteSpawnMultiplier;
                float shopMult = dist.ShopWeightMultiplier * runSettings.ShopSpawnMultiplier;
                float eventMult = dist.EventWeightMultiplier * runSettings.EventSpawnMultiplier;
                float restMult = dist.RestSiteWeightMultiplier;

                ModLogger.Verbose("MapGenerationHooks", $"MapPointTypeCounts constructor postfix: raw Elites={__instance.NumOfElites}, Shops={__instance.NumOfShops}, Unknowns={__instance.NumOfUnknowns}, Rests={__instance.NumOfRests}");

                if (Math.Abs(eliteMult - 1.0f) > 0.001f && ElitesField != null)
                {
                    int adjustedElites = Math.Max(0, (int)Math.Round(__instance.NumOfElites * eliteMult));
                    ElitesField.SetValue(__instance, adjustedElites);
                }

                if (Math.Abs(shopMult - 1.0f) > 0.001f && ShopsField != null)
                {
                    int adjustedShops = Math.Max(0, (int)Math.Round(__instance.NumOfShops * shopMult));
                    ShopsField.SetValue(__instance, adjustedShops);
                }

                if (Math.Abs(eventMult - 1.0f) > 0.001f && UnknownsField != null)
                {
                    int adjustedUnknowns = Math.Max(0, (int)Math.Round(__instance.NumOfUnknowns * eventMult));
                    UnknownsField.SetValue(__instance, adjustedUnknowns);
                }

                if (Math.Abs(restMult - 1.0f) > 0.001f && RestsField != null)
                {
                    int adjustedRests = Math.Max(0, (int)Math.Round(__instance.NumOfRests * restMult));
                    RestsField.SetValue(__instance, adjustedRests);
                }

                ModLogger.Info($"MapPointTypeCounts adjusted: Elites={__instance.NumOfElites}, Shops={__instance.NumOfShops}, Unknowns={__instance.NumOfUnknowns}, Rests={__instance.NumOfRests}");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MapPointTypeCounts for modified map generation tweaks", ex);
            }
        }
    }

    /// <summary>
    /// Computes adjusted room node weight based on active config multipliers.
    /// </summary>
    public static float AdjustNodeWeight(string nodeType, float baseWeight)
    {
        var dist = ConfigManager.Current.PreRunTweaks.MapNodeDistribution;
        float multiplier = 1.0f;

        switch (nodeType.ToLowerInvariant())
        {
            case "elite":
            case "monster_elite":
                multiplier = dist.EliteWeightMultiplier * ConfigManager.ActiveRunSettings.EliteSpawnMultiplier;
                break;
            case "shop":
            case "merchant":
                multiplier = dist.ShopWeightMultiplier * ConfigManager.ActiveRunSettings.ShopSpawnMultiplier;
                break;
            case "event":
            case "unknown":
            case "question":
                multiplier = dist.EventWeightMultiplier * ConfigManager.ActiveRunSettings.EventSpawnMultiplier;
                break;
            case "rest":
            case "campfire":
                multiplier = dist.RestSiteWeightMultiplier;
                break;
            case "combat":
            case "normal_combat":
                multiplier = dist.CombatWeightMultiplier;
                break;
        }

        if (Math.Abs(multiplier - 1.0f) > 0.001f)
        {
            float adjusted = Math.Max(0.01f, baseWeight * multiplier);
            ModLogger.Info($"MapHook: {nodeType} weight {baseWeight:F2} -> {adjusted:F2} (x{multiplier:F2})");
            return adjusted;
        }

        return baseWeight;
    }
}

