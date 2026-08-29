using System;
using System.Reflection;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts combat lifecycle methods (damage calculation, energy consumption, draw phases).
/// </summary>
public static class CombatHooks
{
    public static void ApplyPatches(Harmony harmony)
    {
        ModLogger.Verbose("CombatHooks", "Applying CombatHooks Harmony patches...");
        try
        {
            harmony.CreateClassProcessor(typeof(CombatHooks)).Patch();
            ModLogger.Info("CombatHooks successfully initialized.");
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"CombatHooks partial patch notice: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(NCreatureStateDisplay), "SubscribeToCreatureEvents")]
    public static class CreatureStateDisplaySubscribeEventsPatch
    {
        private static readonly FieldInfo? CreatureField = typeof(NCreatureStateDisplay).GetField("_creature", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo? RefreshValuesMethod = typeof(NCreatureStateDisplay).GetMethod("RefreshValues", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        [HarmonyPostfix]
        public static void Postfix(NCreatureStateDisplay __instance)
        {
            try
            {
                if (CreatureField?.GetValue(__instance) is Creature creature)
                {
                    ModLogger.Verbose("CombatHooks", $"Subscribing state display update handlers for creature '{creature.GetType().Name}'.");
                    creature.CurrentHpChanged += (oldHp, newHp) => 
                    {
                        try
                        {
                            if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree())
                            {
                                Callable.From(() =>
                                {
                                    if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree())
                                    {
                                        RefreshValuesMethod?.Invoke(__instance, null);
                                    }
                                }).CallDeferred();
                            }
                        }
                        catch { }
                    };
                    creature.MaxHpChanged += (oldMax, newMax) => 
                    {
                        try
                        {
                            if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree())
                            {
                                Callable.From(() =>
                                {
                                    if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree())
                                    {
                                        RefreshValuesMethod?.Invoke(__instance, null);
                                    }
                                }).CallDeferred();
                            }
                        }
                        catch { }
                    };
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"CreatureStateDisplaySubscribeEventsPatch notice: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "LoseHpInternal")]
    public static class CreatureLoseHpInternalPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MegaCrit.Sts2.Core.Entities.Creatures.Creature __instance, ref decimal amount)
        {
            decimal originalAmount = amount;
            if (__instance.IsPlayer)
            {
                amount = ProcessPlayerIncomingDamage((int)amount);
                if (amount != originalAmount)
                {
                    ModLogger.Verbose("CombatHooks", $"Player LoseHpInternal intercept: {originalAmount} -> {amount}");
                }
            }
            else
            {
                amount = ProcessMonsterIncomingDamage((int)amount);
                if (amount != originalAmount)
                {
                    ModLogger.Verbose("CombatHooks", $"Monster LoseHpInternal intercept ({__instance.GetType().Name}): {originalAmount} -> {amount}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), "DamageBlockInternal")]
    public static class CreatureDamageBlockInternalPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MegaCrit.Sts2.Core.Entities.Creatures.Creature __instance, ref decimal amount)
        {
            if (__instance.IsPlayer && (RuntimeStateManager.GodModeEnabled || ConfigManager.Current.CombatSandbox.GodMode))
            {
                ModLogger.Verbose("CombatHooks", $"GodMode prevented {amount} block damage on player.");
                amount = 0; // Don't lose block in god mode
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Cards.CardEnergyCost), "GetAmountToSpend")]
    public static class CardEnergyCostGetAmountToSpendPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref int __result)
        {
            if (ShouldBypassEnergyCost())
            {
                if (__result > 0)
                {
                    ModLogger.Verbose("CombatHooks", $"Infinite Energy bypassed energy spend: {__result} -> 0");
                }
                __result = 0;
            }
        }
    }

    /// <summary>
    /// Processes incoming damage to the player. If GodMode is active, prevents damage.
    /// </summary>
    public static int ProcessPlayerIncomingDamage(int incomingDamage)
    {
        if (RuntimeStateManager.GodModeEnabled || ConfigManager.Current.CombatSandbox.GodMode)
        {
            ModLogger.Info($"CombatHook: GodMode absorbed {incomingDamage} incoming damage.");
            return 0;
        }

        return incomingDamage;
    }

    /// <summary>
    /// Processes damage dealt to enemies. If OneHitKill is active, boosts damage significantly.
    /// </summary>
    public static int ProcessMonsterIncomingDamage(int incomingDamage)
    {
        if (RuntimeStateManager.OneHitKillEnabled || ConfigManager.Current.CombatSandbox.OneHitKill)
        {
            int lethal = Math.Max(incomingDamage, 99999);
            ModLogger.Info($"CombatHook: OneHitKill modified damage {incomingDamage} -> {lethal}");
            return lethal;
        }

        return incomingDamage;
    }

    /// <summary>
    /// Computes turn start card draw count including sandbox bonuses.
    /// </summary>
    public static int ProcessCardDrawCount(int baseDraw)
    {
        int bonus = ConfigManager.Current.CombatSandbox.BonusDrawPerTurn;
        if (RuntimeStateManager.OverrideCardDrawCount.HasValue)
        {
            int overridden = RuntimeStateManager.OverrideCardDrawCount.Value;
            ModLogger.Verbose("CombatHooks", $"ProcessCardDrawCount: overridden {baseDraw} -> {overridden}");
            return overridden;
        }

        if (bonus > 0)
        {
            int boosted = baseDraw + bonus;
            ModLogger.Verbose("CombatHooks", $"ProcessCardDrawCount: base={baseDraw} + bonus={bonus} -> {boosted}");
            return boosted;
        }

        return baseDraw;
    }

    /// <summary>
    /// Checks if card energy expenditure should be bypassed.
    /// </summary>
    public static bool ShouldBypassEnergyCost()
    {
        bool bypass = RuntimeStateManager.InfiniteEnergyEnabled || ConfigManager.Current.CombatSandbox.InfiniteEnergy;
        return bypass;
    }
}
