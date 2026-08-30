using System;
using System.Reflection;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts map node generation weights (Elites, Shops, Unknowns, Rest Sites, Combats),
/// map floor/room length, and free map navigation (Flying Boots mode).
/// Enforces fair play by switching runs to Seeded/Custom mode when map generation or cheats are active.
/// </summary>
public static class MapGenerationHooks
{
    private static readonly FieldInfo? ElitesField = typeof(MapPointTypeCounts).GetField("<NumOfElites>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? ShopsField = typeof(MapPointTypeCounts).GetField("<NumOfShops>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? UnknownsField = typeof(MapPointTypeCounts).GetField("<NumOfUnknowns>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RestsField = typeof(MapPointTypeCounts).GetField("<NumOfRests>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? MapLengthField = typeof(StandardActMap).GetField("_mapLength", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Evaluates if any map generation weights, multipliers, or pre-run tweaks have been modified from game default.
    /// </summary>
    public static bool AreMapTweaksModified()
    {
        var preRun = ConfigManager.Current.PreRunTweaks;
        var dist = preRun.MapNodeDistribution;
        var runSettings = ConfigManager.ActiveRunSettings;

        bool modified = Math.Abs(dist.EliteWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.ShopWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.EventWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.RestSiteWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(dist.CombatWeightMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(runSettings.EliteSpawnMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(runSettings.ShopSpawnMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(runSettings.EventSpawnMultiplier - 1.0f) > 0.001f ||
                        preRun.MapRoomCount != 15 ||
                        preRun.FreeMapNavigation ||
                        RuntimeStateManager.FreeMapNavigationEnabled ||
                        Math.Abs(preRun.EnemyHealthMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(preRun.EnemyDamageMultiplier - 1.0f) > 0.001f ||
                        Math.Abs(preRun.EnemyDefendMultiplier - 1.0f) > 0.001f ||
                        preRun.EndlessMode.Enabled;

        ModLogger.Verbose("MapGenerationHooks", $"AreMapTweaksModified check: {modified} (RoomCount={preRun.MapRoomCount}, FreeNav={preRun.FreeMapNavigation}, Endless={preRun.EndlessMode.Enabled})");
        return modified;
    }

    /// <summary>
    /// Enforces fair play on singleplayer/new run embarkation:
    /// If tweaks are customized, switch GameMode to Custom (Seeded/Fair mode)
    /// to disable achievements and epoch unlocks.
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
                        ModLogger.Info("Pre-run tweaks or map modifiers are active: Automatically set GameMode to Custom (Seeded/Fair Mode).");
                    }
                }
                else
                {
                    ModLogger.Info("Map generation tweaks are at game default: Proceeding with standard run.");
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
                        ModLogger.Info("Pre-run tweaks are modified: Automatically set shared GameMode to Custom (Seeded/Fair Mode).");
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
    /// Adjusts generated map length (room/floor count) during ActModel room count calculation across all acts.
    /// StandardActMap and SpoilsActMap invoke ActModel.GetNumberOfRooms() to determine map length.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.ActModel), nameof(MegaCrit.Sts2.Core.Models.ActModel.GetNumberOfRooms))]
    public static class ActModelGetNumberOfRoomsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isMultiplayer, ref int __result)
        {
            try
            {
                int desiredRooms = Math.Max(15, ConfigManager.Current.PreRunTweaks.MapRoomCount);
                if (desiredRooms > 15)
                {
                    __result = isMultiplayer ? Math.Max(1, desiredRooms - 1) : desiredRooms;
                    ModLogger.Verbose("MapGenerationHooks", $"ActModel.GetNumberOfRooms: Custom MapRoomCount applied -> {__result} rooms for act.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in ActModelGetNumberOfRoomsPatch setting map room count", ex);
            }
        }
    }

    /// <summary>
    /// Adjusts generated map node counts according to configured multipliers and room count scaling during map construction.
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
                int desiredRooms = Math.Max(15, ConfigManager.Current.PreRunTweaks.MapRoomCount);
                float roomScale = desiredRooms > 15 ? (float)desiredRooms / 15.0f : 1.0f;

                float eliteMult = dist.EliteWeightMultiplier * runSettings.EliteSpawnMultiplier * roomScale;
                float shopMult = dist.ShopWeightMultiplier * runSettings.ShopSpawnMultiplier * roomScale;
                float eventMult = dist.EventWeightMultiplier * runSettings.EventSpawnMultiplier * roomScale;
                float restMult = dist.RestSiteWeightMultiplier * roomScale;

                ModLogger.Verbose("MapGenerationHooks", $"MapPointTypeCounts constructor postfix: raw Elites={__instance.NumOfElites}, Shops={__instance.NumOfShops}, Unknowns={__instance.NumOfUnknowns}, Rests={__instance.NumOfRests}, roomScale={roomScale:F2}");

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

                ModLogger.Verbose("MapGenerationHooks", $"MapPointTypeCounts adjusted: Elites={__instance.NumOfElites}, Shops={__instance.NumOfShops}, Unknowns={__instance.NumOfUnknowns}, Rests={__instance.NumOfRests}");
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error adjusting MapPointTypeCounts for modified map generation tweaks", ex);
            }
        }
    }

    /// <summary>
    /// Prevents 'Cannot find next node' exception in StandardActMap.GenerateNextCoord when generating large maps.
    /// If all 3 direction choices produce a crossover conflict, relaxes crossover check to guarantee path completion.
    /// </summary>
    [HarmonyPatch(typeof(StandardActMap), "GenerateNextCoord")]
    public static class StandardActMapGenerateNextCoordPatch
    {
        private static readonly MethodInfo? HasInvalidCrossoverMethod = typeof(StandardActMap).GetMethod("HasInvalidCrossover", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? RngField = typeof(StandardActMap).GetField("_rng", BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPrefix]
        public static bool Prefix(StandardActMap __instance, MapPoint current, ref MapCoord __result)
        {
            try
            {
                int col = current.coord.col;
                int leftCol = Math.Max(0, col - 1);
                int rightCol = Math.Min(col + 1, 6);
                int row = current.coord.row + 1;

                var rng = (RngField?.GetValue(__instance) as MegaCrit.Sts2.Core.Random.Rng) ?? new MegaCrit.Sts2.Core.Random.Rng();
                var directions = new System.Collections.Generic.List<int> { -1, 0, 1 };

                // Shuffle candidate offsets
                for (int i = directions.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    (directions[i], directions[j]) = (directions[j], directions[i]);
                }

                // 1. Try standard candidates without crossover
                foreach (int dir in directions)
                {
                    int targetCol = dir switch
                    {
                        -1 => leftCol,
                        0 => col,
                        1 => rightCol,
                        _ => col
                    };

                    bool hasCrossover = false;
                    if (HasInvalidCrossoverMethod != null)
                    {
                        hasCrossover = (bool)(HasInvalidCrossoverMethod.Invoke(__instance, new object[] { current, targetCol }) ?? false);
                    }

                    if (!hasCrossover)
                    {
                        __result = new MapCoord { col = targetCol, row = row };
                        return false; // Skip original method
                    }
                }

                // 2. Safe Fallback for long maps: if boxed in by crossovers, choose straight or nearest valid column without crashing
                int fallbackCol = col;
                if (leftCol != col && rightCol != col)
                {
                    fallbackCol = rng.NextBool() ? leftCol : rightCol;
                }
                else if (leftCol != col)
                {
                    fallbackCol = leftCol;
                }
                else if (rightCol != col)
                {
                    fallbackCol = rightCol;
                }

                ModLogger.Verbose("MapGenerationHooks", $"GenerateNextCoord fallback step used at row {row} (col {col} -> {fallbackCol}) to avoid generation deadlock.");
                __result = new MapCoord { col = fallbackCol, row = row };
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error in StandardActMapGenerateNextCoordPatch safe generation", ex);
                __result = new MapCoord { col = current.coord.col, row = current.coord.row + 1 };
                return false;
            }
        }
    }

    /// <summary>
    /// Prevents map pruning from throwing 'Unable to prune matching segments in 50 iterations' on large maps.
    /// Pruning duplicate cosmetic segments is non-essential and should fail gracefully rather than crash into a black screen.
    /// </summary>
    [HarmonyPatch(typeof(MapPathPruning), nameof(MapPathPruning.PruneDuplicateSegments))]
    public static class MapPathPruningPruneDuplicateSegmentsPatch
    {
        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                ModLogger.Verbose("MapGenerationHooks", $"MapPathPruning duplicate segment pruning completed with safe stop: {__exception.Message}");
                return null; // Suppress exception so map generation succeeds cleanly
            }
            return null;
        }
    }

    /// <summary>
    /// Protects PruneAndRepair against any uncaught generation edge cases.
    /// </summary>
    [HarmonyPatch(typeof(MapPathPruning), nameof(MapPathPruning.PruneAndRepair))]
    public static class MapPathPruningPruneAndRepairPatch
    {
        [HarmonyFinalizer]
        public static Exception? Finalizer(Exception? __exception)
        {
            if (__exception != null)
            {
                ModLogger.Verbose("MapGenerationHooks", $"MapPathPruning.PruneAndRepair finished with safe recovery: {__exception.Message}");
                return null;
            }
            return null;
        }
    }

    /// <summary>
    /// Protects StandardActMap.CreateFor against any uncaught exceptions by returning a verified fallback map.
    /// </summary>
    [HarmonyPatch(typeof(StandardActMap), nameof(StandardActMap.CreateFor))]
    public static class StandardActMapCreateForPatch
    {
        [HarmonyFinalizer]
        public static Exception? Finalizer(RunState runState, bool replaceTreasureWithElites, Exception? __exception, ref StandardActMap __result)
        {
            if (__exception != null)
            {
                ModLogger.Error("StandardActMap.CreateFor encountered an error during custom generation; recovering with fallback standard map.", __exception);
                try
                {
                    __result = new StandardActMap(
                        new MegaCrit.Sts2.Core.Random.Rng(runState.Rng.Seed, $"act_{runState.CurrentActIndex + 1}_map_fallback"),
                        runState.Act,
                        runState.Players.Count > 1,
                        replaceTreasureWithElites,
                        runState.Act.HasSecondBoss,
                        null,
                        false // Disable pruning on fallback to guarantee 100% reliability
                    );
                    return null; // Suppress exception
                }
                catch (Exception fallbackEx)
                {
                    ModLogger.Error("Critical error creating fallback StandardActMap", fallbackEx);
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Free Map Navigation (Flying Boots style): makes all map points clickable regardless of standard pathing connections.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapPoint), "get_IsTravelable")]
    public static class NMapPointIsTravelablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || ConfigManager.Current.PreRunTweaks.FreeMapNavigation)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Enables debug travel on NMapScreen when Free Map Navigation is enabled.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), "get_IsDebugTravelEnabled")]
    public static class NMapScreenIsDebugTravelEnabledPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || ConfigManager.Current.PreRunTweaks.FreeMapNavigation)
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// When traveling on map with Free Map Navigation active, marks active run as Custom mode and resets IsTraveling.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.TravelToMapCoord))]
    public static class NMapScreenTravelToMapCoordPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || ConfigManager.Current.PreRunTweaks.FreeMapNavigation)
            {
                GameHelper.EnsureCustomRunMode();
            }
        }

        [HarmonyPostfix]
        public static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen __instance)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || ConfigManager.Current.PreRunTweaks.FreeMapNavigation)
            {
                __instance.IsTraveling = false;
                __instance.RefreshAllPointVisuals();
            }
        }
    }

    /// <summary>
    /// Ensures map points remain travelable and IsTraveling is reset when opening the map screen.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Open))]
    public static class NMapScreenOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen __instance)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || ConfigManager.Current.PreRunTweaks.FreeMapNavigation)
            {
                __instance.IsTraveling = false;
                __instance.RefreshAllPointVisuals();
            }
        }
    }

    /// <summary>
    /// Ensures map points are refreshed when a new act or map is loaded.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.SetMap))]
    public static class NMapScreenSetMapPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen __instance)
        {
            if (RuntimeStateManager.FreeMapNavigationEnabled || ConfigManager.Current.PreRunTweaks.FreeMapNavigation)
            {
                __instance.IsTraveling = false;
                __instance.RefreshAllPointVisuals();
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
