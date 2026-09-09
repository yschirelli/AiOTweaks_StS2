using System;
using System.Reflection;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.ValueProps;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts combat lifecycle methods (damage calculation, energy consumption, draw phases, enemy health/damage/defend scaling).
/// </summary>
public static class CombatHooks
{

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

    public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MegaCrit.Sts2.Core.Entities.Creatures.Creature, System.Runtime.CompilerServices.StrongBox<int>> MonsterBaseHpTable = new();

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature), nameof(MegaCrit.Sts2.Core.Entities.Creatures.Creature.SetUniqueMonsterHpValue))]
    public static class CreatureSetUniqueMonsterHpValuePatch
    {
        [HarmonyPostfix]
        public static void Postfix(MegaCrit.Sts2.Core.Entities.Creatures.Creature __instance)
        {
            try
            {
                if (!__instance.IsPlayer && __instance.MaxHp > 0)
                {
                    if (!MonsterBaseHpTable.TryGetValue(__instance, out var box))
                    {
                        box = new System.Runtime.CompilerServices.StrongBox<int>(__instance.MaxHp);
                        MonsterBaseHpTable.Add(__instance, box);
                    }

                    float hpMult = RuntimeStateManager.GetEffectiveEnemyHealthMultiplier();
                    if (Math.Abs(hpMult - 1.0f) > 0.001f)
                    {
                        int baseHp = box.Value;
                        int scaled = Math.Max(1, (int)Math.Round(baseHp * hpMult));
                        __instance.SetMaxHpInternal(scaled);
                        __instance.SetCurrentHpInternal(scaled);
                        ModLogger.Info($"CombatHook: Scaled monster '{__instance.GetType().Name}' initial HP: {baseHp} -> {scaled} (x{hpMult:F2})");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Error scaling monster initial HP", ex);
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

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage))]
    public static class HookModifyDamagePatch
    {
        [HarmonyPostfix]
        public static void Postfix(Creature? target, Creature? dealer, ValueProp props, CardModel? cardSource, ref decimal __result)
        {
            try
            {
                if (__result <= 0) return;

                // 1. Resolve effective dealer (target attack dealer or card owner creature)
                Creature? effectiveDealer = dealer ?? cardSource?.Owner?.Creature;

                // 2. Guard against self-damage (Breakthrough, Offering, Bloodletting, Hemokinesis, Pain curse, etc.)
                // Offensive damage multipliers only apply to attacks dealt to opposing combatants, never self-harm.
                if (target != null && effectiveDealer != null)
                {
                    if (target == effectiveDealer || (target.IsPlayer && effectiveDealer.IsPlayer))
                    {
                        return;
                    }
                }

                // 3. Guard against pure unblockable HP loss (Poison, card self-harm costs, curses, event ticks)
                if (props.HasFlag(ValueProp.Unblockable))
                {
                    return;
                }

                // 4. Multipliers apply strictly to powered attacks (attack cards and monster attack moves).
                // MegaCrit's IsPoweredAttack() checks: props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered)
                // If cardSource is present, it is an active card attack and allowed through unless flagged unpowered.
                if (props.HasFlag(ValueProp.Unpowered))
                {
                    return;
                }

                if (!props.IsPoweredAttack() && cardSource == null)
                {
                    return;
                }

                // 5. Player attacking enemy
                if (effectiveDealer != null && effectiveDealer.IsPlayer)
                {
                    if (target == null || !target.IsPlayer)
                    {
                        float mult = RuntimeStateManager.GetEffectivePlayerDamageMultiplier();
                        if (Math.Abs(mult - 1.0f) > 0.001f)
                        {
                            decimal original = __result;
                            __result = Math.Max(0, (decimal)Math.Round((double)__result * mult));
                            ModLogger.Verbose("CombatHooks", $"Player attack damage modified ({target?.GetType().Name ?? "All"}): {original} -> {__result} (x{mult:F2})");
                        }
                    }
                }
                // 6. Enemy attacking player
                else if (effectiveDealer != null && !effectiveDealer.IsPlayer)
                {
                    if (target == null || target.IsPlayer)
                    {
                        float mult = RuntimeStateManager.GetEffectiveEnemyDamageMultiplier();
                        if (Math.Abs(mult - 1.0f) > 0.001f)
                        {
                            decimal original = __result;
                            __result = Math.Max(0, (decimal)Math.Round((double)__result * mult));
                            ModLogger.Verbose("CombatHooks", $"Enemy attack damage modified ({effectiveDealer.GetType().Name}): {original} -> {__result} (x{mult:F2})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"HookModifyDamagePatch error: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyBlock))]
    public static class HookModifyBlockPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Creature? target, MegaCrit.Sts2.Core.Models.CardModel? cardSource, ValueProp props, ref decimal __result)
        {
            try
            {
                if (__result <= 0) return;

                // 1. Guard against unpowered block (Status powers like Plating, Relics like Anchor / Orichalcum, Potions)
                // Defend multipliers are meant for active block actions (Cards and Monster moves), not fixed status/relic values.
                if (props.HasFlag(ValueProp.Unpowered))
                {
                    return;
                }

                // 2. Only scale powered block from cards or monster defend moves
                if (!props.IsPoweredCardOrMonsterMoveBlock() && cardSource == null)
                {
                    return;
                }

                Creature? effectiveTarget = target ?? cardSource?.Owner?.Creature;
                if (effectiveTarget != null)
                {
                    float defMult = effectiveTarget.IsPlayer
                        ? RuntimeStateManager.GetEffectivePlayerDefendMultiplier()
                        : RuntimeStateManager.GetEffectiveEnemyDefendMultiplier();

                    if (Math.Abs(defMult - 1.0f) > 0.001f)
                    {
                        decimal original = __result;
                        __result = Math.Max(0, (decimal)Math.Round((double)__result * defMult));
                        string creatureType = effectiveTarget.IsPlayer ? "Player" : "Enemy";
                        ModLogger.Verbose("CombatHooks", $"{creatureType} HookModifyBlock ({effectiveTarget.GetType().Name}): {original} -> {__result} (x{defMult:F2})");
                    }
                }
                else if (cardSource != null)
                {
                    float defMult = RuntimeStateManager.GetEffectivePlayerDefendMultiplier();
                    if (Math.Abs(defMult - 1.0f) > 0.001f)
                    {
                        decimal original = __result;
                        __result = Math.Max(0, (decimal)Math.Round((double)__result * defMult));
                        ModLogger.Verbose("CombatHooks", $"Player Card HookModifyBlock ({cardSource.GetType().Name}): {original} -> {__result} (x{defMult:F2})");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"HookModifyBlockPatch error: {ex.Message}");
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

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd), nameof(MegaCrit.Sts2.Core.Commands.CardPileCmd.Draw), new Type[] {
        typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
        typeof(decimal),
        typeof(MegaCrit.Sts2.Core.Entities.Players.Player),
        typeof(bool)
    })]
    public static class CardPileCmdDrawPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref decimal count, bool fromHandDraw)
        {
            try
            {
                if (fromHandDraw)
                {
                    int bonus = ConfigManager.Current.CombatSandbox.BonusDrawPerTurn;
                    if (RuntimeStateManager.OverrideCardDrawCount.HasValue)
                    {
                        count = RuntimeStateManager.OverrideCardDrawCount.Value;
                        ModLogger.Verbose("CombatHooks", $"CardPileCmd.Draw turn start draw overridden: -> {count}");
                    }
                    else if (bonus > 0)
                    {
                        decimal original = count;
                        count += bonus;
                        ModLogger.Verbose("CombatHooks", $"CardPileCmd.Draw turn start draw boosted: {original} + {bonus} -> {count}");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"CardPileCmdDrawPatch notice: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState), "ResetEnergy")]
    public static class PlayerCombatStateResetEnergyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState __instance)
        {
            try
            {
                int maxEnergy = RunTweaksSaveManager.GetEffectivePreRunTweaks().MaxEnergy;
                var player = GameHelper.GetActivePlayer();
                if (player != null && maxEnergy > 0 && player.MaxEnergy != maxEnergy)
                {
                    player.MaxEnergy = maxEnergy;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"PlayerCombatStateResetEnergyPatch notice: {ex.Message}");
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

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardCmd), "Exhaust")]
    public static class CardCmdExhaustPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Models.CardModel card, bool causedByEthereal, bool skipVisuals, ref System.Threading.Tasks.Task<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult?> __result)
        {
            if (RuntimeStateManager.NoCardExhaustEnabled || ConfigManager.Current.CombatSandbox.NoCardExhaust)
            {
                ModLogger.Info($"NoCardExhaust active: retaining card '{card?.GetType().Name}' to Discard pile instead of exhausting.");
                __result = RedirectToDiscardAsync(card, skipVisuals);
                return false;
            }
            return true;
        }

        private static async System.Threading.Tasks.Task<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult?> RedirectToDiscardAsync(MegaCrit.Sts2.Core.Models.CardModel? card, bool skipVisuals)
        {
            if (card == null) return null;
            try
            {
                var results = await MegaCrit.Sts2.Core.Commands.CardPileCmd.Add(new[] { card }, MegaCrit.Sts2.Core.Entities.Cards.PileType.Discard, MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition.Bottom, null, skipVisuals);
                return results.Count > 0 ? results[0] : null;
            }
            catch (Exception ex)
            {
                ModLogger.Error($"Failed redirecting exhausted card to discard: {ex.Message}", ex);
                return null;
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
            if (incomingDamage > 0)
            {
                int lethal = Math.Max(incomingDamage, 99999);
                ModLogger.Info($"CombatHook: OneHitKill modified damage {incomingDamage} -> {lethal}");
                return lethal;
            }
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
