using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Godot;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;

namespace AIOTweaks.Core;

/// <summary>
/// Safe reflection & runtime bridges into Slay the Spire 2 internal systems with verbose diagnostic logging.
/// </summary>
public static class GameHelper
{
    private static readonly FieldInfo? DevConsoleField = typeof(NDevConsole).GetField("_devConsole", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? RunStateField = typeof(NRun).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly PropertyInfo? RunManagerStateProp = typeof(RunManager).GetProperty("State", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? CombatTrackerStateField = typeof(CombatStateTracker).GetField("_state", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? GoldRewardAmountField = typeof(GoldReward).GetField("<Amount>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? CreatureCurrentHpField = typeof(Creature).GetField("_currentHp", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? CreatureMaxHpField = typeof(Creature).GetField("_maxHp", BindingFlags.NonPublic | BindingFlags.Instance);

    private static List<string>? _cachedRelicIds;
    private static List<string>? _cachedCardIds;
    private static List<string>? _cachedPotionIds;
    private static List<string>? _cachedEventIds;

    public static void ExecuteConsoleCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;

        ModLogger.Verbose("GameHelper", $"Dispatching engine console command: '{command}'");
        try
        {
            if (NDevConsole.Instance != null && DevConsoleField != null)
            {
                if (DevConsoleField.GetValue(NDevConsole.Instance) is DevConsole devConsole)
                {
                    devConsole.ProcessCommand(command);
                    ModLogger.Debug($"Executed DevConsole command: '{command}'");
                    return;
                }
            }
            ModLogger.Verbose("GameHelper", "NDevConsole.Instance or DevConsole field unavailable for direct command dispatch.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ExecuteConsoleCommand notice: {ex.Message}");
        }
    }

    public static Player? GetActivePlayer()
    {
        try
        {
            // 1. Primary core engine singleton (RunManager.Instance.State)
            if (RunManager.Instance != null && RunManagerStateProp != null)
            {
                if (RunManagerStateProp.GetValue(RunManager.Instance) is RunState runState && runState.Players is { Count: > 0 } runPlayers)
                {
                    var player = LocalContext.GetMe(runPlayers) ?? runPlayers[0];
                    if (player != null)
                    {
                        ModLogger.Verbose("GameHelper", $"GetActivePlayer: Resolved via RunManager.Instance.State (Player: {player.GetType().Name})");
                        return player;
                    }
                }
            }

            // 2. CombatManager singleton (CombatManager.Instance.StateTracker._state)
            if (CombatManager.Instance?.StateTracker != null && CombatTrackerStateField != null)
            {
                if (CombatTrackerStateField.GetValue(CombatManager.Instance.StateTracker) is CombatState combatState && combatState.Players is { Count: > 0 } combatPlayers)
                {
                    var player = LocalContext.GetMe(combatPlayers) ?? combatPlayers[0];
                    if (player != null)
                    {
                        ModLogger.Verbose("GameHelper", $"GetActivePlayer: Resolved via CombatManager.Instance.StateTracker (Player: {player.GetType().Name})");
                        return player;
                    }
                }
            }

            // 3. NPlayerHand combat UI fallback
            if (MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance != null)
            {
                var handStateField = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand).GetField("_combatState", BindingFlags.NonPublic | BindingFlags.Instance);
                if (handStateField?.GetValue(MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance) is CombatState handCombatState && handCombatState.Players is { Count: > 0 } handPlayers)
                {
                    var player = LocalContext.GetMe(handPlayers) ?? handPlayers[0];
                    if (player != null)
                    {
                        ModLogger.Verbose("GameHelper", $"GetActivePlayer: Resolved via NPlayerHand combat state (Player: {player.GetType().Name})");
                        return player;
                    }
                }
            }

            // 4. NRun UI node fallback
            if (NRun.Instance != null && RunStateField != null)
            {
                if (RunStateField.GetValue(NRun.Instance) is RunState runState && runState.Players is { Count: > 0 } nrunPlayers)
                {
                    var player = LocalContext.GetMe(nrunPlayers) ?? nrunPlayers[0];
                    if (player != null)
                    {
                        ModLogger.Verbose("GameHelper", $"GetActivePlayer: Resolved via NRun._state (Player: {player.GetType().Name})");
                        return player;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetActivePlayer notice: {ex.Message}");
        }

        ModLogger.Verbose("GameHelper", "GetActivePlayer: No active player found across all engine singletons.");
        return null;
    }

    public static IReadOnlyList<Creature>? GetActiveCombatEnemies()
    {
        try
        {
            if (CombatManager.Instance?.StateTracker != null && CombatTrackerStateField != null)
            {
                if (CombatTrackerStateField.GetValue(CombatManager.Instance.StateTracker) is CombatState combatState)
                {
                    var enemies = combatState.Enemies;
                    ModLogger.Verbose("GameHelper", $"GetActiveCombatEnemies: Resolved {enemies?.Count ?? 0} enemies from CombatState.");
                    return enemies;
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetActiveCombatEnemies notice: {ex.Message}");
        }
        return null;
    }

    public static void SetGoldRewardAmount(GoldReward reward, int amount)
    {
        ModLogger.Verbose("GameHelper", $"SetGoldRewardAmount: setting amount to {amount}");
        try
        {
            GoldRewardAmountField?.SetValue(reward, amount);
            ModLogger.Debug($"Set GoldReward backing field amount to: {amount}");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"SetGoldRewardAmount notice: {ex.Message}");
        }
    }

    private static readonly FieldInfo? RunStateGameModeField = typeof(RunState).GetField("<GameMode>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

    public static void EnsureCustomRunMode()
    {
        try
        {
            var player = GetActivePlayer();
            if (player?.RunState is RunState runState && runState.GameMode == GameMode.Standard)
            {
                RunStateGameModeField?.SetValue(runState, GameMode.Custom);
                ModLogger.Info("GameHelper: Switched active RunState to GameMode.Custom.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"EnsureCustomRunMode error: {ex.Message}");
        }
    }

    public static void ModifyCreatureHealth(Creature creature, int currentHpChange, int maxHpChange = 0)
    {
        if (creature == null)
        {
            ModLogger.Verbose("GameHelper", "ModifyCreatureHealth: creature is null.");
            return;
        }

        ModLogger.Verbose("GameHelper", $"ModifyCreatureHealth: creature={creature.GetType().Name}, currentHpChange={currentHpChange}, maxHpChange={maxHpChange} (Pre: {creature.CurrentHp}/{creature.MaxHp})");
        try
        {
            if (maxHpChange != 0)
            {
                int newMax = Math.Max(1, creature.MaxHp + maxHpChange);
                creature.SetMaxHpInternal(newMax);
                ModLogger.Verbose("GameHelper", $"Updated MaxHp to {newMax}");
            }

            if (currentHpChange > 0)
            {
                try
                {
                    _ = CreatureCmd.Heal(creature, currentHpChange, true);
                    ModLogger.Verbose("GameHelper", $"Executed CreatureCmd.Heal for {currentHpChange} HP.");
                }
                catch
                {
                    creature.HealInternal(currentHpChange);
                    ModLogger.Verbose("GameHelper", $"Executed creature.HealInternal for {currentHpChange} HP.");
                }
            }
            else if (currentHpChange < 0)
            {
                int newCurrent = Math.Clamp(creature.CurrentHp + currentHpChange, 0, creature.MaxHp);
                creature.SetCurrentHpInternal(newCurrent);
                ModLogger.Verbose("GameHelper", $"Set current HP to {newCurrent}");
            }

            RefreshHealthUi(creature);
            ModLogger.Verbose("GameHelper", $"ModifyCreatureHealth complete: Post: {creature.CurrentHp}/{creature.MaxHp}");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModifyCreatureHealth notice: {ex.Message}");
        }
    }

    public static void SetCreatureHealthExact(Creature creature, int currentHp, int maxHp)
    {
        if (creature == null)
        {
            ModLogger.Verbose("GameHelper", "SetCreatureHealthExact: creature is null.");
            return;
        }

        ModLogger.Verbose("GameHelper", $"SetCreatureHealthExact: currentHp={currentHp}, maxHp={maxHp} (Pre: {creature.CurrentHp}/{creature.MaxHp})");
        try
        {
            int safeMax = Math.Max(1, maxHp);
            int safeCurrent = Math.Clamp(currentHp, 0, safeMax);

            creature.SetMaxHpInternal(safeMax);
            creature.SetCurrentHpInternal(safeCurrent);

            RefreshHealthUi(creature);
            ModLogger.Verbose("GameHelper", $"SetCreatureHealthExact complete: Post: {creature.CurrentHp}/{creature.MaxHp}");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"SetCreatureHealthExact notice: {ex.Message}");
        }
    }

    public static void RefreshHealthUi(Creature? creature = null)
    {
        ModLogger.Verbose("GameHelper", $"RefreshHealthUi invoked for creature: {creature?.GetType().Name ?? "all"}");
        try
        {
            // 1. Update TopBar HP UI in real-time
            if (NRun.Instance?.GlobalUi?.TopBar?.Hp != null)
            {
                try
                {
                    NRun.Instance.GlobalUi.TopBar.Hp.Call("UpdateHealth", 0, 0);
                    ModLogger.Verbose("GameHelper", "Invoked TopBar HP UpdateHealth via Godot Call.");
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"TopBar UpdateHealth notice: {ex.Message}");
                }
            }

            // 2. Update combat room creature health bars in real-time
            if (NRun.Instance?.CombatRoom != null)
            {
                var combatRoom = NRun.Instance.CombatRoom;
                if (creature != null)
                {
                    var node = combatRoom.GetCreatureNode(creature);
                    if (node != null)
                    {
                        var stateDisplayField = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature).GetField("_stateDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (stateDisplayField?.GetValue(node) is GodotObject stateDisplay)
                        {
                            stateDisplay.Call("RefreshValues");
                            ModLogger.Verbose("GameHelper", "Invoked single creature stateDisplay RefreshValues.");
                        }
                    }
                }
                else
                {
                    foreach (var node in combatRoom.CreatureNodes)
                    {
                        if (node != null)
                        {
                            var stateDisplayField = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature).GetField("_stateDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (stateDisplayField?.GetValue(node) is GodotObject stateDisplay)
                            {
                                stateDisplay.Call("RefreshValues");
                            }
                        }
                    }
                    ModLogger.Verbose("GameHelper", "Refreshed all combat room creature state displays.");
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"RefreshHealthUi notice: {ex.Message}");
        }
    }

    public static List<string> GetAllRelicIds(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedRelicIds != null && _cachedRelicIds.Count > 0)
        {
            ModLogger.Verbose("GameHelper", $"GetAllRelicIds: returning {_cachedRelicIds.Count} cached relic IDs.");
            return _cachedRelicIds;
        }

        ModLogger.Verbose("GameHelper", "GetAllRelicIds: scanning ModelDb and assemblies for relics...");
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Try ModelDb first (available if game runtime has initialized ModelDb)
        try
        {
            if (ModelDb.AllRelics != null)
            {
                foreach (var relic in ModelDb.AllRelics)
                {
                    if (relic != null && relic.GetType().Namespace?.Contains("Mocks") != true)
                    {
                        results.Add(relic.GetType().Name);
                    }
                }
                ModLogger.Verbose("GameHelper", $"Discovered {results.Count} relics from ModelDb.AllRelics.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModelDb relic scan notice: {ex.Message}");
        }

        // 2. Scan assemblies for concrete RelicModel subtypes
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var t in types)
                {
                    if (t != null && !t.IsAbstract && typeof(RelicModel).IsAssignableFrom(t))
                    {
                        if (t.Namespace?.Contains("Mocks") != true)
                        {
                            results.Add(t.Name);
                        }
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Assembly scan total unique relics: {results.Count}");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to scan assemblies for RelicModel types.", ex);
        }

        _cachedRelicIds = results.OrderBy(x => x).ToList();
        return _cachedRelicIds;
    }

    public static List<string> GetAllCardIds(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedCardIds != null && _cachedCardIds.Count > 0)
        {
            ModLogger.Verbose("GameHelper", $"GetAllCardIds: returning {_cachedCardIds.Count} cached card IDs.");
            return _cachedCardIds;
        }

        ModLogger.Verbose("GameHelper", "GetAllCardIds: scanning ModelDb and assemblies for cards...");
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Try ModelDb first
        try
        {
            if (ModelDb.AllCards != null)
            {
                foreach (var card in ModelDb.AllCards)
                {
                    if (card != null && card.GetType().Namespace?.Contains("Mocks") != true)
                    {
                        results.Add(card.GetType().Name);
                    }
                }
                ModLogger.Verbose("GameHelper", $"Discovered {results.Count} cards from ModelDb.AllCards.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModelDb card scan notice: {ex.Message}");
        }

        // 2. Scan assemblies for concrete CardModel subtypes
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var t in types)
                {
                    if (t != null && !t.IsAbstract && typeof(CardModel).IsAssignableFrom(t))
                    {
                        if (t.Namespace?.Contains("Mocks") != true)
                        {
                            results.Add(t.Name);
                        }
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Assembly scan total unique cards: {results.Count}");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to scan assemblies for CardModel types.", ex);
        }

        _cachedCardIds = results.OrderBy(x => x).ToList();
        return _cachedCardIds;
    }

    public static Dictionary<string, string> GetCardPoolMapping()
    {
        ModLogger.Verbose("GameHelper", "Building card pool mapping from ModelDb.AllCards...");
        var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (ModelDb.AllCards != null)
            {
                foreach (var card in ModelDb.AllCards)
                {
                    if (card == null) continue;
                    string cardId = card.GetType().Name;
                    string poolId = card.Pool?.Id.ToString() ?? "";
                    if (!string.IsNullOrEmpty(poolId))
                    {
                        mapping[cardId] = poolId;
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Mapped {mapping.Count} cards to character card pools.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetCardPoolMapping error: {ex.Message}");
        }
        return mapping;
    }

    public static List<(string Id, string DisplayName)> GetAvailableCharacterCardPools()
    {
        ModLogger.Verbose("GameHelper", "Retrieving available character card pools...");
        var result = new List<(string Id, string DisplayName)>();
        try
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. From Character models
            if (ModelDb.AllCharacters != null)
            {
                foreach (var ch in ModelDb.AllCharacters)
                {
                    if (ch?.CardPool != null)
                    {
                        string poolId = ch.CardPool.Id.ToString();
                        if (seen.Add(poolId))
                        {
                            string charName = ch.Title.GetFormattedText();
                            string displayName = !string.IsNullOrWhiteSpace(charName) ? charName : (!string.IsNullOrWhiteSpace(ch.CardPool.Title) ? ch.CardPool.Title : poolId);
                            result.Add((poolId, displayName));
                        }
                    }
                }
            }

            // 2. From AllCardPools
            if (ModelDb.AllCardPools != null)
            {
                foreach (var pool in ModelDb.AllCardPools)
                {
                    if (pool == null) continue;
                    string poolId = pool.Id.ToString();
                    if (seen.Add(poolId))
                    {
                        string name = !string.IsNullOrWhiteSpace(pool.Title) ? pool.Title : pool.GetType().Name.Replace("CardPool", "");
                        result.Add((poolId, name));
                    }
                }
            }

            ModLogger.Verbose("GameHelper", $"Found {result.Count} unique character card pools.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetAvailableCharacterCardPools error: {ex.Message}");
        }

        return result;
    }

    public static string? GetCurrentPlayerCharacterPoolId()
    {
        try
        {
            var player = GetActivePlayer();
            if (player?.Character?.CardPool != null)
            {
                string poolId = player.Character.CardPool.Id.ToString();
                ModLogger.Verbose("GameHelper", $"GetCurrentPlayerCharacterPoolId: resolved '{poolId}'");
                return poolId;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetCurrentPlayerCharacterPoolId error: {ex.Message}");
        }
        return null;
    }

    public static List<string> GetAllPotionIds(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedPotionIds != null && _cachedPotionIds.Count > 0)
        {
            ModLogger.Verbose("GameHelper", $"GetAllPotionIds: returning {_cachedPotionIds.Count} cached potion IDs.");
            return _cachedPotionIds;
        }

        ModLogger.Verbose("GameHelper", "GetAllPotionIds: scanning ModelDb and assemblies for potions...");
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (ModelDb.AllPotions != null)
            {
                foreach (var potion in ModelDb.AllPotions)
                {
                    if (potion != null)
                    {
                        results.Add(potion.GetType().Name);
                    }
                }
                ModLogger.Verbose("GameHelper", $"Discovered {results.Count} potions from ModelDb.AllPotions.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModelDb potion scan notice: {ex.Message}");
        }

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var t in types)
                {
                    if (t != null && !t.IsAbstract && typeof(PotionModel).IsAssignableFrom(t))
                    {
                        results.Add(t.Name);
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Total unique potion types found: {results.Count}");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to scan assemblies for PotionModel types.", ex);
        }

        _cachedPotionIds = results.OrderBy(x => x).ToList();
        return _cachedPotionIds;
    }

    public sealed class EventInfo
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string TypeName { get; }
        public bool IsAncient { get; }

        public EventInfo(string id, string displayName, string typeName, bool isAncient)
        {
            Id = id;
            DisplayName = displayName;
            TypeName = typeName;
            IsAncient = isAncient;
        }
    }

    private static List<EventInfo>? _cachedEventInfos;

    public static List<EventInfo> GetAllEventInfos(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedEventInfos != null && _cachedEventInfos.Count > 0)
        {
            return _cachedEventInfos;
        }

        ModLogger.Verbose("GameHelper", "GetAllEventInfos: scanning ModelDb and assemblies for events & ancients...");
        var dict = new Dictionary<string, EventInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. From ModelDb (AllEvents and AllAncients)
        try
        {
            var allEvents = (ModelDb.AllEvents ?? Enumerable.Empty<EventModel>())
                .Concat(ModelDb.AllAncients ?? Enumerable.Empty<AncientEventModel>());

            foreach (var ev in allEvents)
            {
                if (ev == null) continue;
                string typeName = ev.GetType().Name;
                string entryId = ev.Id.Entry ?? ConvertPascalToScreamingSnake(typeName);
                bool isAncient = typeof(AncientEventModel).IsAssignableFrom(ev.GetType());
                string title = ev.Title?.GetFormattedText() ?? "";
                string displayName = !string.IsNullOrWhiteSpace(title) ? title : FormatPascalOrSnakeToWords(typeName);

                dict[entryId] = new EventInfo(entryId, displayName, typeName, isAncient);
            }
            ModLogger.Verbose("GameHelper", $"Discovered {dict.Count} events from ModelDb.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModelDb event discovery error: {ex.Message}");
        }

        // 2. From Assemblies
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var t in types)
                {
                    if (t != null && !t.IsAbstract && typeof(EventModel).IsAssignableFrom(t))
                    {
                        string typeName = t.Name;
                        bool isAncient = typeof(AncientEventModel).IsAssignableFrom(t);
                        string entryId = ModelDb.GetEntry(t) ?? ConvertPascalToScreamingSnake(typeName);
                        if (!dict.ContainsKey(entryId))
                        {
                            string displayName = FormatPascalOrSnakeToWords(typeName);
                            dict[entryId] = new EventInfo(entryId, displayName, typeName, isAncient);
                        }
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Total unique events after assembly scan: {dict.Count}");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to scan assemblies for EventModel types.", ex);
        }

        _cachedEventInfos = dict.Values.OrderBy(e => e.DisplayName).ToList();
        _cachedEventIds = _cachedEventInfos.Select(e => e.Id).ToList();
        return _cachedEventInfos;
    }

    public static List<string> GetAllEventIds(bool forceRefresh = false)
    {
        var infos = GetAllEventInfos(forceRefresh);
        return infos.Select(e => e.Id).ToList();
    }

    public static EventModel? GetEventModel(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;

        string clean = idOrName.Trim();
        string upper = clean.ToUpperInvariant();
        ModLogger.Verbose("GameHelper", $"GetEventModel: searching for event '{idOrName}' (clean: '{clean}', upper: '{upper}')");

        // 1. Try Direct ModelDb.GetByIdOrNull
        try
        {
            var eventId = new ModelId("EVENT", upper);
            var found = ModelDb.GetByIdOrNull<EventModel>(eventId) ?? (EventModel?)ModelDb.GetByIdOrNull<AncientEventModel>(eventId);
            if (found != null)
            {
                ModLogger.Verbose("GameHelper", $"Found event model via ModelDb.GetByIdOrNull: {found.GetType().Name}");
                return found;
            }
        }
        catch { }

        // 2. Iterate ModelDb.AllEvents & AllAncients
        try
        {
            var allEvents = (ModelDb.AllEvents ?? Enumerable.Empty<EventModel>())
                .Concat(ModelDb.AllAncients ?? Enumerable.Empty<AncientEventModel>());

            foreach (var ev in allEvents)
            {
                if (ev == null) continue;
                if (ev.Id.Entry.Equals(upper, StringComparison.OrdinalIgnoreCase) ||
                    ev.GetType().Name.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                    ev.GetType().Name.Replace("Event", "").Equals(clean, StringComparison.OrdinalIgnoreCase))
                {
                    ModLogger.Verbose("GameHelper", $"Found event model via ModelDb iteration: {ev.GetType().Name}");
                    return ev;
                }
            }
        }
        catch { }

        // 3. Fallback: Find type from assemblies
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                var type = asm.GetType($"MegaCrit.Sts2.Core.Models.Events.{clean}") ??
                           asm.GetType($"MegaCrit.Sts2.Core.Models.Events.Mocks.{clean}") ??
                           asm.GetTypes().FirstOrDefault(t => typeof(EventModel).IsAssignableFrom(t) && !t.IsAbstract &&
                               (t.Name.Equals(clean, StringComparison.OrdinalIgnoreCase) || (ModelDb.GetEntry(t) ?? "").Equals(upper, StringComparison.OrdinalIgnoreCase)));

                if (type != null)
                {
                    var instance = Activator.CreateInstance(type) as EventModel;
                    ModLogger.Verbose("GameHelper", $"Found and instantiated event model via assembly scan: {type.Name}");
                    return instance;
                }
            }
        }
        catch { }

        ModLogger.Verbose("GameHelper", $"GetEventModel: event '{idOrName}' could not be resolved.");
        return null;
    }

    public static string NormalizeEventId(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return "";
        var info = GetAllEventInfos().FirstOrDefault(e => e.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || 
                                                          e.TypeName.Equals(idOrName, StringComparison.OrdinalIgnoreCase) || 
                                                          e.DisplayName.Equals(idOrName, StringComparison.OrdinalIgnoreCase));
        string norm = info?.Id ?? ConvertPascalToScreamingSnake(idOrName);
        ModLogger.Verbose("GameHelper", $"NormalizeEventId: '{idOrName}' -> '{norm}'");
        return norm;
    }

    public static string ConvertPascalToScreamingSnake(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsUpper(c) && i > 0 && input[i - 1] != '_' && !char.IsUpper(input[i - 1]))
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    public static string FormatPascalOrSnakeToWords(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string noPrefix = input.Replace('_', ' ');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < noPrefix.Length; i++)
        {
            char c = noPrefix[i];
            if (char.IsUpper(c) && i > 0 && noPrefix[i - 1] != ' ' && !char.IsUpper(noPrefix[i - 1]))
            {
                sb.Append(' ');
            }
            sb.Append(c);
        }
        return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(sb.ToString().ToLowerInvariant());
    }

    public static bool IsKeyMatch(InputEventKey keyEvent, string? hotkey)
    {
        if (keyEvent == null || string.IsNullOrWhiteSpace(hotkey) || hotkey.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string trimmed = hotkey.Trim();
        if (Enum.TryParse<Key>(trimmed, true, out var parsedKey))
        {
            if (keyEvent.Keycode == parsedKey || keyEvent.PhysicalKeycode == parsedKey)
            {
                return true;
            }
        }

        if ((trimmed == "`" || trimmed == "~" || trimmed.Equals("Backquote", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("Grave", StringComparison.OrdinalIgnoreCase))
            && (keyEvent.Keycode == Key.Quoteleft || keyEvent.PhysicalKeycode == Key.Quoteleft))
        {
            return true;
        }

        if (trimmed.Length == 1 && Enum.TryParse<Key>(trimmed.ToUpperInvariant(), true, out var charKey))
        {
            if (keyEvent.Keycode == charKey || keyEvent.PhysicalKeycode == charKey)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsInCombat()
    {
        var player = GetActivePlayer();
        bool inCombat = player?.PlayerCombatState != null || (CombatManager.Instance != null && CombatManager.Instance.IsInProgress);
        return inCombat;
    }

    public static IReadOnlyList<CardModel>? GetPlayerDeckCards()
    {
        try
        {
            var player = GetActivePlayer();
            var cards = player?.Deck?.Cards;
            ModLogger.Verbose("GameHelper", $"GetPlayerDeckCards: count={cards?.Count ?? 0}");
            return cards;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerDeckCards error: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<CardModel>? GetPlayerDrawPileCards()
    {
        try
        {
            var player = GetActivePlayer();
            var cards = player?.PlayerCombatState?.DrawPile?.Cards;
            ModLogger.Verbose("GameHelper", $"GetPlayerDrawPileCards: count={cards?.Count ?? 0}");
            return cards;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerDrawPileCards error: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<CardModel>? GetPlayerDiscardPileCards()
    {
        try
        {
            var player = GetActivePlayer();
            var cards = player?.PlayerCombatState?.DiscardPile?.Cards;
            ModLogger.Verbose("GameHelper", $"GetPlayerDiscardPileCards: count={cards?.Count ?? 0}");
            return cards;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerDiscardPileCards error: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<CardModel>? GetPlayerHandCards()
    {
        try
        {
            var player = GetActivePlayer();
            var cards = player?.PlayerCombatState?.Hand?.Cards;
            ModLogger.Verbose("GameHelper", $"GetPlayerHandCards: count={cards?.Count ?? 0}");
            return cards;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerHandCards error: {ex.Message}");
            return null;
        }
    }

    public static IReadOnlyList<CardModel>? GetPlayerExhaustPileCards()
    {
        try
        {
            var player = GetActivePlayer();
            var cards = player?.PlayerCombatState?.ExhaustPile?.Cards;
            ModLogger.Verbose("GameHelper", $"GetPlayerExhaustPileCards: count={cards?.Count ?? 0}");
            return cards;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerExhaustPileCards error: {ex.Message}");
            return null;
        }
    }

    public static string GetCardDescription(CardModel? card)
    {
        if (card == null) return "";
        try
        {
            string desc = card.Description?.GetFormattedText() ?? "";
            if (string.IsNullOrWhiteSpace(desc))
            {
                desc = card.Description?.GetRawText() ?? "";
            }
            if (string.IsNullOrWhiteSpace(desc))
            {
                desc = card.Description?.ToString() ?? "";
            }
            return desc.Trim();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetCardDescription error: {ex.Message}");
            return "";
        }
    }

    public static string GetCardFullTooltip(CardModel? card)
    {
        if (card == null) return "";
        try
        {
            string title = !string.IsNullOrWhiteSpace(card.Title) ? card.Title : card.GetType().Name;
            if (card.IsUpgraded && !title.Contains('+'))
            {
                title += " (+)";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"Type: {card.Type} | Rarity: {card.Rarity}");

            if (card.EnergyCost != null)
            {
                string energyCostText = card.EnergyCost.CostsX 
                    ? "X" 
                    : (card.EnergyCost.HasLocalModifiers 
                        ? card.EnergyCost.GetResolved().ToString() 
                        : (card.EnergyCost.Canonical >= 0 ? card.EnergyCost.Canonical.ToString() : "Unplayable"));
                sb.AppendLine($"Energy Cost: {energyCostText}");
            }
            if (card.HasStarCostX)
            {
                sb.AppendLine("Star Cost: X");
            }
            else if (card.CurrentStarCost > 0 || card.CanonicalStarCost > 0)
            {
                sb.AppendLine($"Star Cost: {(card.CurrentStarCost > 0 ? card.CurrentStarCost : card.CanonicalStarCost)}");
            }

            string desc = GetCardDescription(card);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine();
                sb.AppendLine(desc);
            }

            if (card.Keywords != null && card.Keywords.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Keywords: " + string.Join(", ", card.Keywords.Select(k => k.ToString())));
            }

            if (card.Enchantment != null)
            {
                sb.AppendLine();
                string enchTitle = card.Enchantment.Title?.GetFormattedText() ?? card.Enchantment.GetType().Name;
                sb.AppendLine($"Enchantment: {enchTitle} (x{card.Enchantment.Amount})");
                string enchDesc = card.Enchantment.DynamicDescription?.GetFormattedText() ?? card.Enchantment.DynamicExtraCardText?.GetFormattedText() ?? "";
                if (!string.IsNullOrWhiteSpace(enchDesc))
                {
                    sb.AppendLine($"  {enchDesc}");
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetCardFullTooltip error: {ex.Message}");
            return card.GetType().Name;
        }
    }

    public static string GetRelicDescription(RelicModel? relic)
    {
        if (relic == null) return "";
        try
        {
            foreach (var prop in relic.GetType().GetProperties())
            {
                if (prop.Name.Contains("Desc", StringComparison.OrdinalIgnoreCase) || 
                    prop.Name.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Contains("Detail", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Contains("Flavor", StringComparison.OrdinalIgnoreCase))
                {
                    var val = prop.GetValue(relic);
                    if (val != null)
                    {
                        var method = val.GetType().GetMethod("GetFormattedText");
                        if (method != null)
                        {
                            string? text = method.Invoke(val, null) as string;
                            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
                        }
                        string s = val.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                    }
                }
            }
            return "";
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetRelicDescription error: {ex.Message}");
            return "";
        }
    }

    public static string GetRelicFullTooltip(RelicModel? relic)
    {
        if (relic == null) return "";
        try
        {
            string title = !string.IsNullOrWhiteSpace(relic.Title.GetFormattedText()) ? relic.Title.GetFormattedText() : relic.GetType().Name;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"Rarity: {relic.Rarity}");

            string desc = GetRelicDescription(relic);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine();
                sb.AppendLine(desc);
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetRelicFullTooltip error: {ex.Message}");
            return relic.GetType().Name;
        }
    }

    private static List<string>? _cachedEnchantmentIds;

    public static List<string> GetAllEnchantmentIds(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedEnchantmentIds != null && _cachedEnchantmentIds.Count > 0)
        {
            return _cachedEnchantmentIds;
        }

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (typeof(EnchantmentModel).IsAssignableFrom(t) && !t.IsAbstract && 
                        !t.Name.Contains("Deprecated") && !t.Name.Contains("Mock") && 
                        t.Namespace?.Contains("Mocks") != true)
                    {
                        results.Add(t.Name);
                    }
                }
            }
        }
        catch { }

        _cachedEnchantmentIds = results.OrderBy(x => x).ToList();
        return _cachedEnchantmentIds;
    }

    public static EnchantmentModel? FindCanonicalEnchantmentModel(string enchantmentId)
    {
        if (string.IsNullOrWhiteSpace(enchantmentId)) return null;

        try
        {
            var id = new ModelId("ENCHANTMENT", enchantmentId.ToUpperInvariant());
            var found = ModelDb.GetByIdOrNull<EnchantmentModel>(id);
            if (found != null) return found;
        }
        catch { }

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (typeof(EnchantmentModel).IsAssignableFrom(t) && !t.IsAbstract && 
                        (t.Name.Equals(enchantmentId, StringComparison.OrdinalIgnoreCase) ||
                         t.Name.Replace("Enchantment", "").Equals(enchantmentId, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (t.Namespace?.Contains("Mocks") != true)
                        {
                            try
                            {
                                var instance = Activator.CreateInstance(t) as EnchantmentModel;
                                if (instance != null) return instance;
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    public static EnchantmentModel? CreateEnchantment(EnchantmentModel canonical, decimal amount = 1)
    {
        if (canonical == null) return null;
        try
        {
            var mutable = canonical.ToMutable();
            if (mutable != null)
            {
                return mutable;
            }
        }
        catch { }

        try
        {
            var clone = (EnchantmentModel)canonical.ClonePreservingMutability();
            if (clone != null && !clone.IsCanonical)
            {
                return clone;
            }
        }
        catch { }

        try
        {
            var inst = Activator.CreateInstance(canonical.GetType()) as EnchantmentModel;
            return inst;
        }
        catch { }

        return null;
    }

    public static List<string> GetPlayerDeckCardIds()
    {
        var result = new List<string>();
        try
        {
            var cards = GetPlayerDeckCards();
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    if (card != null)
                    {
                        result.Add(card.GetType().Name);
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"GetPlayerDeckCardIds: retrieved {result.Count} IDs.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerDeckCardIds error: {ex.Message}");
        }
        return result;
    }

    public static List<string> GetPlayerDrawPileCardIds()
    {
        var result = new List<string>();
        try
        {
            var cards = GetPlayerDrawPileCards();
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    if (card != null)
                    {
                        result.Add(card.GetType().Name);
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"GetPlayerDrawPileCardIds: retrieved {result.Count} IDs.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerDrawPileCardIds error: {ex.Message}");
        }
        return result;
    }

    public static List<string> GetPlayerDiscardPileCardIds()
    {
        var result = new List<string>();
        try
        {
            var cards = GetPlayerDiscardPileCards();
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    if (card != null)
                    {
                        result.Add(card.GetType().Name);
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"GetPlayerDiscardPileCardIds: retrieved {result.Count} IDs.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerDiscardPileCardIds error: {ex.Message}");
        }
        return result;
    }

    public static string? GetCardPortraitPath(string cardId)
    {
        try
        {
            if (ModelDb.AllCards != null)
            {
                var model = ModelDb.AllCards.FirstOrDefault(c => c != null && c.GetType().Name == cardId);
                if (model != null)
                {
                    var prop = model.GetType().GetProperty("PortraitPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null) return prop.GetValue(model) as string;
                    var method = model.GetType().GetMethod("GetPortraitPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (method != null) return method.Invoke(model, null) as string;
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetCardPortraitPath error: {ex.Message}");
        }
        return null;
    }

    public static CardModel? FindCanonicalCardModel(string cardId)
    {
        if (string.IsNullOrWhiteSpace(cardId)) return null;
        ModLogger.Verbose("GameHelper", $"FindCanonicalCardModel: searching for card '{cardId}'");

        try
        {
            if (ModelDb.AllCards != null)
            {
                var match = ModelDb.AllCards.FirstOrDefault(c => 
                    c != null && (
                        c.GetType().Name.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.Entry.Equals(cardId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(c.Title) && c.Title.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    ));
                if (match != null)
                {
                    ModLogger.Verbose("GameHelper", $"Found canonical card model in ModelDb.AllCards: {match.GetType().Name}");
                    return match;
                }
            }
        }
        catch { }

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (typeof(CardModel).IsAssignableFrom(t) && !t.IsAbstract && t.Name.Equals(cardId, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var instance = Activator.CreateInstance(t) as CardModel;
                            ModLogger.Verbose("GameHelper", $"Created canonical card model from assembly type: {t.Name}");
                            return instance;
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }

        ModLogger.Verbose("GameHelper", $"FindCanonicalCardModel: card '{cardId}' not found.");
        return null;
    }

    public static CardModel? CreateCardForPlayer(CardModel canonical, Player? player = null)
    {
        if (canonical == null) return null;
        player ??= GetActivePlayer();
        ModLogger.Verbose("GameHelper", $"CreateCardForPlayer: canonical={canonical.GetType().Name}, player={player?.GetType().Name ?? "null"}");

        if (player != null)
        {
            if (player.RunState is ICardScope cardScope)
            {
                try
                {
                    var scopedCard = cardScope.CreateCard(canonical, player);
                    if (scopedCard != null)
                    {
                        ModLogger.Verbose("GameHelper", "Created card via player.RunState ICardScope.");
                        return scopedCard;
                    }
                }
                catch { }
            }

            if (player.Creature?.CombatState is ICardScope combatScope)
            {
                try
                {
                    var scopedCombatCard = combatScope.CreateCard(canonical, player);
                    if (scopedCombatCard != null)
                    {
                        ModLogger.Verbose("GameHelper", "Created card via player.Creature.CombatState ICardScope.");
                        return scopedCombatCard;
                    }
                }
                catch { }
            }

            try
            {
                var clone = canonical.CreateCloneForPlayer(player);
                if (clone != null)
                {
                    ModLogger.Verbose("GameHelper", "Created card via canonical.CreateCloneForPlayer.");
                    return clone;
                }
            }
            catch { }
        }

        try
        {
            var mutable = canonical.ToMutable();
            if (mutable != null)
            {
                ModLogger.Verbose("GameHelper", "Created card via canonical.ToMutable.");
                return mutable;
            }
        }
        catch { }

        try
        {
            var inst = Activator.CreateInstance(canonical.GetType()) as CardModel;
            ModLogger.Verbose("GameHelper", "Created card via Activator.CreateInstance fallback.");
            return inst;
        }
        catch { }

        return null;
    }

    public static IReadOnlyList<RelicModel>? GetPlayerRelics()
    {
        try
        {
            var player = GetActivePlayer();
            var relics = player?.Relics;
            ModLogger.Verbose("GameHelper", $"GetPlayerRelics: count={relics?.Count ?? 0}");
            return relics;
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerRelics error: {ex.Message}");
            return null;
        }
    }

    public static List<string> GetPlayerRelicIds()
    {
        var result = new List<string>();
        try
        {
            var relics = GetPlayerRelics();
            if (relics != null)
            {
                foreach (var r in relics)
                {
                    if (r != null)
                    {
                        result.Add(r.GetType().Name);
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"GetPlayerRelicIds: retrieved {result.Count} IDs.");
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerRelicIds error: {ex.Message}");
        }
        return result;
    }

    public static RelicModel? FindCanonicalRelicModel(string relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return null;
        ModLogger.Verbose("GameHelper", $"FindCanonicalRelicModel: searching for relic '{relicId}'");

        try
        {
            if (ModelDb.AllRelics != null)
            {
                var match = ModelDb.AllRelics.FirstOrDefault(r => 
                    r != null && (
                        r.GetType().Name.Equals(relicId, StringComparison.OrdinalIgnoreCase) ||
                        r.Id.Entry.Equals(relicId, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(r.Title.GetFormattedText()) && r.Title.GetFormattedText().Equals(relicId, StringComparison.OrdinalIgnoreCase))
                    ));
                if (match != null)
                {
                    ModLogger.Verbose("GameHelper", $"Found canonical relic in ModelDb.AllRelics: {match.GetType().Name}");
                    return match;
                }
            }
        }
        catch { }

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                {
                    if (typeof(RelicModel).IsAssignableFrom(t) && !t.IsAbstract && 
                        (t.Name.Equals(relicId, StringComparison.OrdinalIgnoreCase) || 
                         t.Name.Replace("Relic", "").Equals(relicId, StringComparison.OrdinalIgnoreCase)))
                    {
                        if (t.Namespace?.Contains("Mocks") != true)
                        {
                            try
                            {
                                var instance = Activator.CreateInstance(t) as RelicModel;
                                ModLogger.Verbose("GameHelper", $"Created canonical relic from assembly type: {t.Name}");
                                return instance;
                            }
                            catch { }
                        }
                    }
                }
            }
        }
        catch { }

        ModLogger.Verbose("GameHelper", $"FindCanonicalRelicModel: relic '{relicId}' not found.");
        return null;
    }

    public static RelicModel? CreateRelicForPlayer(RelicModel canonical, Player? player = null)
    {
        if (canonical == null) return null;
        player ??= GetActivePlayer();
        ModLogger.Verbose("GameHelper", $"CreateRelicForPlayer: canonical={canonical.GetType().Name}, player={player?.GetType().Name ?? "null"}");

        try
        {
            var mutable = canonical.ToMutable();
            if (mutable != null)
            {
                ModLogger.Verbose("GameHelper", "Created relic via canonical.ToMutable.");
                return mutable;
            }
        }
        catch { }

        try
        {
            var clone = (RelicModel)canonical.ClonePreservingMutability();
            if (clone != null && !clone.IsCanonical)
            {
                ModLogger.Verbose("GameHelper", "Created relic via canonical.ClonePreservingMutability.");
                return clone;
            }
        }
        catch { }

        try
        {
            var inst = Activator.CreateInstance(canonical.GetType()) as RelicModel;
            ModLogger.Verbose("GameHelper", "Created relic via Activator.CreateInstance fallback.");
            return inst;
        }
        catch { }

        return null;
    }
}
