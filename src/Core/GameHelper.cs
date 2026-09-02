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
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.HoverTips;
using AIOTweaks.Core.Config;

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

    public static CombatState? GetCombatState()
    {
        try
        {
            if (CombatManager.Instance?.StateTracker != null && CombatTrackerStateField != null)
            {
                return CombatTrackerStateField.GetValue(CombatManager.Instance.StateTracker) as CombatState;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetCombatState notice: {ex.Message}");
        }
        return null;
    }

    public static IReadOnlyList<Creature>? GetActiveCombatEnemies()
    {
        try
        {
            var combatState = GetCombatState();
            if (combatState != null)
            {
                var enemies = combatState.Enemies;
                ModLogger.Verbose("GameHelper", $"GetActiveCombatEnemies: Resolved {enemies?.Count ?? 0} enemies from CombatState.");
                return enemies;
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

    /// <summary>
    /// Reads the current player's baseline Max Energy from the active game/player instance.
    /// Falls back to PreRunTweaksConfig or default 3 if not in an active run.
    /// </summary>
    public static int GetPlayerMaxEnergy()
    {
        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                return player.MaxEnergy;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPlayerMaxEnergy error: {ex.Message}");
        }

        return ConfigManager.Current.PreRunTweaks.MaxEnergy;
    }

    /// <summary>
    /// Updates the player's Max Energy in real-time, syncing both the player entity, in-combat energy, UI, and config.
    /// </summary>
    public static void SetPlayerMaxEnergy(int maxEnergy)
    {
        maxEnergy = Math.Max(1, maxEnergy);
        ConfigManager.Current.PreRunTweaks.MaxEnergy = maxEnergy;

        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                int oldMax = player.MaxEnergy;
                player.MaxEnergy = maxEnergy;
                ModLogger.Info($"GameHelper: Set active player MaxEnergy to {maxEnergy} (was {oldMax}).");

                if (player.PlayerCombatState != null)
                {
                    int delta = maxEnergy - oldMax;
                    if (delta > 0)
                    {
                        player.PlayerCombatState.Energy = Math.Min(player.PlayerCombatState.Energy + delta, player.PlayerCombatState.MaxEnergy);
                    }
                    else
                    {
                        player.PlayerCombatState.Energy = Math.Min(player.PlayerCombatState.Energy, player.PlayerCombatState.MaxEnergy);
                    }
                }
            }

            RefreshEnergyUi();
            RefreshAllVisibleCards();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"SetPlayerMaxEnergy error: {ex.Message}");
        }
    }

    /// <summary>
    /// Forces real-time refresh of in-combat energy counters and UI displays on screen.
    /// </summary>
    public static void RefreshEnergyUi()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            if (tree?.Root != null)
            {
                RefreshEnergyCounterNodes(tree.Root);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"RefreshEnergyUi error: {ex.Message}");
        }
    }

    private static readonly MethodInfo? EnergyCounterRefreshLabelMethod = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEnergyCounter).GetMethod("RefreshLabel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private static void RefreshEnergyCounterNodes(Node node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;

        if (node is MegaCrit.Sts2.Core.Nodes.Combat.NEnergyCounter energyCounter && GodotObject.IsInstanceValid(energyCounter) && energyCounter.IsInsideTree())
        {
            try
            {
                EnergyCounterRefreshLabelMethod?.Invoke(energyCounter, null);
            }
            catch { }
        }

        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && GodotObject.IsInstanceValid(child))
            {
                RefreshEnergyCounterNodes(child);
            }
        }
    }

    /// <summary>
    /// Refreshes the visuals, description, and dynamic variable previews (including damage) on all active cards on screen in real time.
    /// </summary>
    public static void RefreshAllVisibleCards()
    {
        try
        {
            // 1. Refresh cards held in combat hand
            if (MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance != null)
            {
                var hand = MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance;
                var activeHolders = hand.ActiveHolders;
                if (activeHolders != null)
                {
                    foreach (var holder in activeHolders)
                    {
                        var card = holder?.CardNode;
                        if (card != null && GodotObject.IsInstanceValid(card) && card.IsInsideTree())
                        {
                            card.UpdateVisuals(card.DisplayingPile, MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode.Normal);
                        }
                    }
                }
            }

            // 2. Scan entire active scene tree for any other active NCard nodes
            var root = ((SceneTree)Engine.GetMainLoop())?.Root;
            if (root != null)
            {
                RefreshCardsInNode(root);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"RefreshAllVisibleCards error: {ex.Message}");
        }
    }

    private static void RefreshCardsInNode(Node node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return;

        if (node is MegaCrit.Sts2.Core.Nodes.Cards.NCard ncard && GodotObject.IsInstanceValid(ncard) && ncard.IsInsideTree())
        {
            try
            {
                ncard.UpdateVisuals(ncard.DisplayingPile, MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode.Normal);
            }
            catch { }
        }

        int childCount = node.GetChildCount();
        for (int i = 0; i < childCount; i++)
        {
            var child = node.GetChild(i);
            if (child != null && GodotObject.IsInstanceValid(child))
            {
                RefreshCardsInNode(child);
            }
        }
    }

    /// <summary>
    /// Checks if the standalone shop overlay menu is currently open.
    /// </summary>
    public static bool IsShopMenuOpen()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var existingLayer = tree?.Root?.GetNodeOrNull<CanvasLayer>("AIOTweaks_ShopCanvasLayer");
            return existingLayer != null && GodotObject.IsInstanceValid(existingLayer);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes the standalone shop overlay menu if currently open.
    /// </summary>
    public static bool CloseShopMenu()
    {
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var existingLayer = tree?.Root?.GetNodeOrNull<CanvasLayer>("AIOTweaks_ShopCanvasLayer");
            if (existingLayer != null && GodotObject.IsInstanceValid(existingLayer))
            {
                NMerchantInventory? inv = null;
                foreach (var child in existingLayer.GetChildren())
                {
                    if (child is NMerchantInventory i)
                    {
                        inv = i;
                        break;
                    }
                }
                if (inv == null)
                {
                    inv = existingLayer.GetNodeOrNull<NMerchantInventory>("Inventory")
                       ?? existingLayer.GetNodeOrNull<NMerchantInventory>("%Inventory")
                       ?? existingLayer.FindChild("Inventory", true, false) as NMerchantInventory;
                }

                if (inv != null && GodotObject.IsInstanceValid(inv) && inv.IsOpen)
                {
                    CloseMerchantInventory(inv);
                }
                else
                {
                    existingLayer.QueueFree();
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"CloseShopMenu failed with exception: {ex.Message}", ex);
        }
        return false;
    }

    /// <summary>
    /// Safely invokes Close on NMerchantInventory via Godot Call / reflection.
    /// </summary>
    public static void CloseMerchantInventory(NMerchantInventory? inv)
    {
        if (inv == null || !GodotObject.IsInstanceValid(inv)) return;
        try
        {
            inv.Call(NMerchantInventory.MethodName.Close);
        }
        catch
        {
            typeof(NMerchantInventory).GetMethod("Close", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)?.Invoke(inv, null);
        }
    }

    /// <summary>
    /// Opens an interactive merchant shop overlay popup anywhere during a run (even during combat) with freshly randomized items.
    /// If the shop is already open, toggles it closed cleanly.
    /// </summary>
    public static bool OpenShopMenu()
    {
        if (IsShopMenuOpen())
        {
            ModLogger.Info("OpenShopMenu: Shop is already open. Toggling closed...");
            return CloseShopMenu();
        }

        var player = GetActivePlayer();
        if (player == null)
        {
            ModLogger.Warn("OpenShopMenu: No active player found. Must be inside a run to open the shop.");
            return false;
        }

        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            if (tree?.Root == null) return false;

            // Check if shop overlay is already open
            var existingLayer = tree.Root.GetNodeOrNull<CanvasLayer>("AIOTweaks_ShopCanvasLayer");
            if (existingLayer != null && GodotObject.IsInstanceValid(existingLayer))
            {
                existingLayer.QueueFree();
            }

            // Load merchant room scene
            var packed = GD.Load<PackedScene>("res://scenes/rooms/merchant_room.tscn")
                ?? PreloadManager.Cache?.GetScene("res://scenes/rooms/merchant_room.tscn");
            if (packed == null)
            {
                ModLogger.Error("OpenShopMenu: Could not load res://scenes/rooms/merchant_room.tscn");
                return false;
            }

            var tempRoom = packed.Instantiate<NMerchantRoom>();
            if (tempRoom == null)
            {
                ModLogger.Error("OpenShopMenu: Instantiated scene is not NMerchantRoom");
                return false;
            }

            // Find NMerchantInventory child node
            NMerchantInventory? inventoryNode = null;
            foreach (var child in tempRoom.GetChildren())
            {
                if (child is NMerchantInventory inv)
                {
                    inventoryNode = inv;
                    break;
                }
            }

            if (inventoryNode == null)
            {
                inventoryNode = tempRoom.GetNodeOrNull<NMerchantInventory>("Inventory")
                             ?? tempRoom.GetNodeOrNull<NMerchantInventory>("%Inventory")
                             ?? tempRoom.FindChild("Inventory", true, false) as NMerchantInventory;
            }

            if (inventoryNode == null)
            {
                ModLogger.Error("OpenShopMenu: NMerchantInventory not found in merchant_room.tscn");
                tempRoom.QueueFree();
                return false;
            }

            // Find NProceedButton child node from merchant_room scene
            NProceedButton? proceedButton = tempRoom.ProceedButton
                ?? tempRoom.GetNodeOrNull<NProceedButton>("%ProceedButton")
                ?? tempRoom.GetNodeOrNull<NProceedButton>("ProceedButton")
                ?? tempRoom.FindChild("ProceedButton", true, false) as NProceedButton;

            // Detach inventory and proceed button before freeing tempRoom
            tempRoom.RemoveChild(inventoryNode);
            if (proceedButton != null)
            {
                tempRoom.RemoveChild(proceedButton);
            }
            tempRoom.QueueFree();

            // Create CanvasLayer overlay with high layer order to display on top of combat/map/events
            var canvasLayer = new CanvasLayer
            {
                Name = "AIOTweaks_ShopCanvasLayer",
                Layer = 100
            };

            // MUST add nodes to scene tree FIRST so Godot calls _Ready() and wires up internal node references!
            canvasLayer.AddChild(inventoryNode);
            if (proceedButton != null)
            {
                canvasLayer.AddChild(proceedButton);
            }
            tree.Root.AddChild(canvasLayer);

            // Generate fresh randomized inventory and initialize inventoryNode now that internal nodes are resolved
            var freshInventory = MerchantInventory.CreateForNormalMerchant(player);
            var dialogue = new MerchantDialogueSet();

            inventoryNode.Initialize(freshInventory, dialogue);

            // Configure proceed button just like the in-game shop event / merchant room
            if (proceedButton != null)
            {
                proceedButton.UpdateText(NProceedButton.ProceedLoc);
                proceedButton.SetPulseState(false);
                proceedButton.Enable();
                proceedButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ =>
                {
                    ModLogger.Verbose("OpenShopMenu", "Proceed/Close button clicked. Closing shop inventory...");
                    CloseMerchantInventory(inventoryNode);
                }));
            }

            // Connect close event to clean up canvasLayer
            inventoryNode.InventoryClosed += () =>
            {
                if (proceedButton != null && GodotObject.IsInstanceValid(proceedButton))
                {
                    proceedButton.Disable();
                }

                Callable.From(() =>
                {
                    if (GodotObject.IsInstanceValid(canvasLayer))
                    {
                        canvasLayer.QueueFree();
                    }
                }).CallDeferred();
            };

            inventoryNode.Open();
            ModLogger.Info("OpenShopMenu: Successfully opened randomized standalone shop overlay with close/proceed button.");
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"OpenShopMenu failed with exception: {ex.Message}", ex);
            return false;
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

    /// <summary>
    /// Forces real-time visual refresh of enemy attack intentions, state displays, and damage numbers across all combat creature nodes.
    /// </summary>
    public static void RefreshCombatIntents()
    {
        try
        {
            if (MegaCrit.Sts2.Core.Nodes.NRun.Instance?.CombatRoom != null)
            {
                var combatRoom = MegaCrit.Sts2.Core.Nodes.NRun.Instance.CombatRoom;
                foreach (var node in combatRoom.CreatureNodes)
                {
                    if (node != null && GodotObject.IsInstanceValid(node))
                    {
                        // 1. Refresh intents asynchronously
                        try
                        {
                            _ = node.RefreshIntents();
                        }
                        catch { }

                        // 2. Refresh creature state display
                        try
                        {
                            var stateDisplayField = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature).GetField("_stateDisplay", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (stateDisplayField?.GetValue(node) is GodotObject stateDisplay && GodotObject.IsInstanceValid(stateDisplay))
                            {
                                stateDisplay.Call("RefreshValues");
                            }
                        }
                        catch { }

                        // 3. Refresh NIntent visual nodes
                        try
                        {
                            var intentContainer = node.IntentContainer;
                            if (intentContainer != null && GodotObject.IsInstanceValid(intentContainer))
                            {
                                foreach (Node child in intentContainer.GetChildren())
                                {
                                    if (child != null && GodotObject.IsInstanceValid(child))
                                    {
                                        var updateVisualsMethod = child.GetType().GetMethod("UpdateVisuals", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        updateVisualsMethod?.Invoke(child, null);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                ModLogger.Verbose("GameHelper", "RefreshCombatIntents: successfully updated monster attack intentions across all active combatants.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"RefreshCombatIntents notice: {ex.Message}");
        }
    }

    public static void RescaleActiveMonstersHp()
    {
        try
        {
            float hpMult = State.RuntimeStateManager.GetEffectiveEnemyHealthMultiplier();
            var enemies = GetActiveCombatEnemies();
            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy != null && !enemy.IsPlayer && !enemy.IsDead)
                    {
                        if (!AIOTweaks.Hooks.CombatHooks.MonsterBaseHpTable.TryGetValue(enemy, out var box))
                        {
                            box = new System.Runtime.CompilerServices.StrongBox<int>(enemy.MaxHp);
                            AIOTweaks.Hooks.CombatHooks.MonsterBaseHpTable.Add(enemy, box);
                        }

                        int baseMax = box.Value;
                        int newMax = Math.Max(1, (int)Math.Round(baseMax * hpMult));
                        float currentRatio = enemy.MaxHp > 0 ? (float)enemy.CurrentHp / enemy.MaxHp : 1.0f;
                        int newCurrent = Math.Max(1, (int)Math.Round(newMax * currentRatio));

                        enemy.SetMaxHpInternal(newMax);
                        enemy.SetCurrentHpInternal(newCurrent);
                        ModLogger.Info($"RescaleActiveMonstersHp: updated '{enemy.GetType().Name}' HP -> {newCurrent}/{newMax} (x{hpMult:F2})");
                    }
                }
            }
            RefreshCombatIntents();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"RescaleActiveMonstersHp error: {ex.Message}");
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

    private static List<PotionInfo>? _cachedPotionInfos;
    private static List<PowerInfo>? _cachedPowerInfos;

    public sealed class PotionInfo
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public PotionRarity Rarity { get; }
        public PotionUsage Usage { get; }
        public TargetType TargetType { get; }
        public string TypeName { get; }
        public PotionModel? CanonicalInstance { get; }

        public PotionInfo(string id, string title, string description, PotionRarity rarity, PotionUsage usage, TargetType targetType, string typeName, PotionModel? canonicalInstance)
        {
            Id = id;
            Title = title;
            Description = description;
            Rarity = rarity;
            Usage = usage;
            TargetType = targetType;
            TypeName = typeName;
            CanonicalInstance = canonicalInstance;
        }
    }

    public static List<PotionInfo> GetAllPotionInfos(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedPotionInfos != null && _cachedPotionInfos.Count > 0)
        {
            return _cachedPotionInfos;
        }

        ModLogger.Verbose("GameHelper", "GetAllPotionInfos: scanning ModelDb and assemblies for potions...");
        var dict = new Dictionary<string, PotionInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. From ModelDb
        try
        {
            if (ModelDb.AllPotions != null)
            {
                foreach (var potion in ModelDb.AllPotions)
                {
                    if (potion == null) continue;
                    string typeName = potion.GetType().Name;
                    string entryId = potion.Id.Entry ?? ConvertPascalToScreamingSnake(typeName);
                    string rawTitle = potion.Title?.GetFormattedText() ?? "";
                    string title = !string.IsNullOrWhiteSpace(rawTitle) ? CleanBbCode(rawTitle) : FormatPascalOrSnakeToWords(typeName);
                    string desc = GetPotionDescription(potion);

                    dict[typeName] = new PotionInfo(entryId, title, desc, potion.Rarity, potion.Usage, potion.TargetType, typeName, potion);
                }
                ModLogger.Verbose("GameHelper", $"Discovered {dict.Count} potions from ModelDb.AllPotions.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModelDb potion scan notice: {ex.Message}");
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
                    if (t != null && !t.IsAbstract && typeof(PotionModel).IsAssignableFrom(t))
                    {
                        if (!dict.ContainsKey(t.Name))
                        {
                            PotionModel? instance = null;
                            try { instance = Activator.CreateInstance(t) as PotionModel; } catch { }
                            string entryId = instance?.Id.Entry ?? ConvertPascalToScreamingSnake(t.Name);
                            string rawTitle = instance?.Title?.GetFormattedText() ?? "";
                            string title = !string.IsNullOrWhiteSpace(rawTitle) ? CleanBbCode(rawTitle) : FormatPascalOrSnakeToWords(t.Name);
                            string desc = instance != null ? GetPotionDescription(instance) : "";
                            PotionRarity rarity = instance?.Rarity ?? PotionRarity.Common;
                            PotionUsage usage = instance?.Usage ?? PotionUsage.CombatOnly;
                            TargetType targetType = instance?.TargetType ?? TargetType.None;

                            dict[t.Name] = new PotionInfo(entryId, title, desc, rarity, usage, targetType, t.Name, instance);
                        }
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Total unique potion infos found: {dict.Count}");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to scan assemblies for PotionModel types.", ex);
        }

        _cachedPotionInfos = dict.Values.OrderBy(x => x.Title).ToList();
        _cachedPotionIds = _cachedPotionInfos.Select(p => p.TypeName).ToList();
        return _cachedPotionInfos;
    }

    public static List<string> GetAllPotionIds(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedPotionIds != null && _cachedPotionIds.Count > 0)
        {
            return _cachedPotionIds;
        }
        GetAllPotionInfos(forceRefresh);
        return _cachedPotionIds ?? new List<string>();
    }

    public static PotionModel? FindCanonicalPotionModel(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;

        var infos = GetAllPotionInfos();
        var match = infos.FirstOrDefault(p => 
            p.TypeName.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Title.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

        if (match?.CanonicalInstance != null) return match.CanonicalInstance;

        try
        {
            if (ModelDb.AllPotions != null)
            {
                var fromDb = ModelDb.AllPotions.FirstOrDefault(p => 
                    p != null && (
                        p.GetType().Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Entry.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                        p.Title.GetFormattedText().Equals(idOrName, StringComparison.OrdinalIgnoreCase)));
                if (fromDb != null) return fromDb;
            }
        }
        catch { }

        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(idOrName, false, true) ?? 
                           asm.GetType($"MegaCrit.Sts2.Core.Models.Potions.{idOrName}", false, true);
                if (type != null && typeof(PotionModel).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    return Activator.CreateInstance(type) as PotionModel;
                }
            }
        }
        catch { }

        return null;
    }

    public static string GetPotionDescription(PotionModel? potion)
    {
        if (potion == null) return "";
        try
        {
            string desc = "";
            try { desc = potion.DynamicDescription?.GetFormattedText() ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(desc) && potion.CanonicalInstance != null)
            {
                try { desc = potion.CanonicalInstance.DynamicDescription?.GetFormattedText() ?? ""; } catch { }
            }
            return CleanBbCode(desc);
        }
        catch
        {
            return "";
        }
    }

    public static string GetPotionFullTooltip(PotionModel? potion)
    {
        if (potion == null) return "";
        try
        {
            string title = !string.IsNullOrWhiteSpace(potion.Title?.GetFormattedText()) 
                ? CleanBbCode(potion.Title.GetFormattedText()) 
                : FormatPascalOrSnakeToWords(potion.GetType().Name);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"Rarity: {potion.Rarity} | Usage: {potion.Usage}");

            string desc = GetPotionDescription(potion);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine();
                sb.AppendLine(desc);
            }

            try
            {
                var hoverTips = potion.HoverTips;
                if (hoverTips != null)
                {
                    foreach (var tip in hoverTips)
                    {
                        if (tip is HoverTip ht)
                        {
                            string tipTitle = CleanBbCode(ht.Title ?? "").Trim();
                            string tipDesc = CleanBbCode(ht.Description ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(tipTitle) && !string.IsNullOrWhiteSpace(tipDesc) &&
                                !tipTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine();
                                sb.AppendLine($"[{tipTitle}]");
                                sb.AppendLine(tipDesc);
                            }
                        }
                    }
                }
            }
            catch { }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPotionFullTooltip error: {ex.Message}");
            return potion.GetType().Name;
        }
    }

    public static Texture2D? GetPotionIcon(PotionModel? potion)
    {
        if (potion == null) return null;
        try
        {
            if (potion.Image != null) return potion.Image;
            if (potion.LargeImage != null) return potion.LargeImage;
            if (potion.Outline != null) return potion.Outline;

            if (!string.IsNullOrEmpty(potion.ImagePath))
            {
                try { var t = GD.Load<Texture2D>(potion.ImagePath); if (t != null) return t; } catch { }
            }
            if (!string.IsNullOrEmpty(potion.LargeImagePath))
            {
                try { var t = GD.Load<Texture2D>(potion.LargeImagePath); if (t != null) return t; } catch { }
            }
        }
        catch { }
        return null;
    }

    public static Color GetPotionRarityColor(PotionRarity rarity)
    {
        return rarity switch
        {
            PotionRarity.Common => new Color(0.85f, 0.85f, 0.9f),
            PotionRarity.Uncommon => new Color(0.35f, 0.75f, 1f),
            PotionRarity.Rare => new Color(1f, 0.85f, 0.25f),
            _ => new Color(0.85f, 0.55f, 1f)
        };
    }

    public static int GetPlayerMaxPotionSlots()
    {
        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                return player.MaxPotionCount;
            }
        }
        catch { }

        return ConfigManager.Current.PreRunTweaks.PotionSlots;
    }

    public static void SetPlayerMaxPotionSlots(int slots)
    {
        slots = Math.Clamp(slots, 1, 10);
        ConfigManager.Current.PreRunTweaks.PotionSlots = slots;

        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                int oldSlots = player.MaxPotionCount;
                var method = typeof(Player).GetMethod("SetMaxPotionCountInternal", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(player, new object[] { slots });
                }
                else
                {
                    int diff = slots - oldSlots;
                    if (diff > 0) player.AddToMaxPotionCount(diff);
                    else if (diff < 0) player.SubtractFromMaxPotionCount(-diff);
                }
                ModLogger.Info($"GameHelper: Set player MaxPotionCount to {slots} (was {oldSlots}).");
            }

            // Directly trigger TopBar & PotionContainer synchronization if active
            var topBar = MegaCrit.Sts2.Core.Nodes.NRun.Instance?.GlobalUi?.TopBar;
            if (topBar != null && GodotObject.IsInstanceValid(topBar))
            {
                AIOTweaks.Hooks.RelicHooks.AdjustTopBarLayout(topBar, slots);
                if (topBar.PotionContainer != null && GodotObject.IsInstanceValid(topBar.PotionContainer))
                {
                    var growMethod = typeof(MegaCrit.Sts2.Core.Nodes.Potions.NPotionContainer).GetMethod("GrowPotionHolders", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    growMethod?.Invoke(topBar.PotionContainer, new object[] { slots });
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error($"SetPlayerMaxPotionSlots failed for {slots}", ex);
        }
    }

    public static IReadOnlyList<PotionModel?>? GetActivePlayerPotionSlots()
    {
        try
        {
            var player = GetActivePlayer();
            if (player != null)
            {
                return player.PotionSlots;
            }
        }
        catch { }
        return null;
    }

    public sealed class PowerInfo
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string TypeName { get; }
        public PowerType Type { get; }
        public PowerStackType StackType { get; }
        public PowerModel? CanonicalInstance { get; }

        public PowerInfo(string id, string displayName, string description, string typeName, PowerType type, PowerStackType stackType, PowerModel? canonicalInstance)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            TypeName = typeName;
            Type = type;
            StackType = stackType;
            CanonicalInstance = canonicalInstance;
        }
    }

    public static List<PowerInfo> GetAllPowerInfos(bool forceRefresh = false)
    {
        if (!forceRefresh && _cachedPowerInfos != null && _cachedPowerInfos.Count > 0)
        {
            return _cachedPowerInfos;
        }

        ModLogger.Verbose("GameHelper", "GetAllPowerInfos: scanning ModelDb and assemblies for powers/statuses...");
        var dict = new Dictionary<string, PowerInfo>(StringComparer.OrdinalIgnoreCase);

        // 1. From ModelDb
        try
        {
            if (ModelDb.AllPowers != null)
            {
                foreach (var power in ModelDb.AllPowers)
                {
                    if (power == null) continue;
                    string typeName = power.GetType().Name;
                    string entryId = power.Id.Entry ?? ConvertPascalToScreamingSnake(typeName);
                    string rawTitle = power.Title?.GetFormattedText() ?? "";
                    string title = !string.IsNullOrWhiteSpace(rawTitle) ? CleanBbCode(rawTitle) : FormatPascalOrSnakeToWords(typeName);
                    string desc = GetPowerDescription(power);

                    dict[typeName] = new PowerInfo(entryId, title, desc, typeName, power.Type, power.StackType, power);
                }
                ModLogger.Verbose("GameHelper", $"Discovered {dict.Count} powers from ModelDb.AllPowers.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModelDb power scan notice: {ex.Message}");
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
                    if (t != null && !t.IsAbstract && typeof(PowerModel).IsAssignableFrom(t))
                    {
                        if (!dict.ContainsKey(t.Name))
                        {
                            PowerModel? instance = null;
                            try { instance = Activator.CreateInstance(t) as PowerModel; } catch { }
                            string entryId = instance?.Id.Entry ?? ConvertPascalToScreamingSnake(t.Name);
                            string rawTitle = instance?.Title?.GetFormattedText() ?? "";
                            string title = !string.IsNullOrWhiteSpace(rawTitle) ? CleanBbCode(rawTitle) : FormatPascalOrSnakeToWords(t.Name);
                            string desc = instance != null ? GetPowerDescription(instance) : "";
                            PowerType powerType = instance?.Type ?? PowerType.Buff;
                            PowerStackType stackType = instance?.StackType ?? PowerStackType.Counter;

                            dict[t.Name] = new PowerInfo(entryId, title, desc, t.Name, powerType, stackType, instance);
                        }
                    }
                }
            }
            ModLogger.Verbose("GameHelper", $"Total unique power infos found: {dict.Count}");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to scan assemblies for PowerModel types.", ex);
        }

        _cachedPowerInfos = dict.Values.OrderBy(x => x.DisplayName).ToList();
        return _cachedPowerInfos;
    }

    public static PowerModel? FindCanonicalPowerModel(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;

        var infos = GetAllPowerInfos();
        var match = infos.FirstOrDefault(p => 
            p.TypeName.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName.Equals(idOrName, StringComparison.OrdinalIgnoreCase));

        if (match?.CanonicalInstance != null) return match.CanonicalInstance;

        try
        {
            if (ModelDb.AllPowers != null)
            {
                var fromDb = ModelDb.AllPowers.FirstOrDefault(p => 
                    p != null && (
                        p.GetType().Name.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                        p.Id.Entry.Equals(idOrName, StringComparison.OrdinalIgnoreCase) ||
                        p.Title.GetFormattedText().Equals(idOrName, StringComparison.OrdinalIgnoreCase)));
                if (fromDb != null) return fromDb;
            }
        }
        catch { }

        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType(idOrName, false, true) ?? 
                           asm.GetType($"MegaCrit.Sts2.Core.Models.Powers.{idOrName}", false, true);
                if (type != null && typeof(PowerModel).IsAssignableFrom(type) && !type.IsAbstract)
                {
                    return Activator.CreateInstance(type) as PowerModel;
                }
            }
        }
        catch { }

        return null;
    }

    public static string GetPowerDescription(PowerModel? power)
    {
        if (power == null) return "";
        try
        {
            string desc = "";
            try { desc = power.SmartDescription?.GetFormattedText() ?? ""; } catch { }
            if (string.IsNullOrWhiteSpace(desc))
            {
                try { desc = power.Description?.GetFormattedText() ?? ""; } catch { }
            }
            return CleanBbCode(desc);
        }
        catch
        {
            return "";
        }
    }

    public static string GetPowerFullTooltip(PowerModel? power)
    {
        if (power == null) return "";
        try
        {
            string title = !string.IsNullOrWhiteSpace(power.Title?.GetFormattedText()) 
                ? CleanBbCode(power.Title.GetFormattedText()) 
                : FormatPascalOrSnakeToWords(power.GetType().Name);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"Type: {power.Type} | Stack: {power.StackType}");

            string desc = GetPowerDescription(power);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine();
                sb.AppendLine(desc);
            }

            try
            {
                var hoverTips = power.HoverTips;
                if (hoverTips != null)
                {
                    foreach (var tip in hoverTips)
                    {
                        if (tip is HoverTip ht)
                        {
                            string tipTitle = CleanBbCode(ht.Title ?? "").Trim();
                            string tipDesc = CleanBbCode(ht.Description ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(tipTitle) && !string.IsNullOrWhiteSpace(tipDesc) &&
                                !tipTitle.Equals(title, StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine();
                                sb.AppendLine($"[{tipTitle}]");
                                sb.AppendLine(tipDesc);
                            }
                        }
                    }
                }
            }
            catch { }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetPowerFullTooltip error: {ex.Message}");
            return power.GetType().Name;
        }
    }

    public static Texture2D? GetPowerIcon(PowerModel? power)
    {
        if (power == null) return null;
        try
        {
            if (power.Icon != null) return power.Icon;
            if (power.BigIcon != null) return power.BigIcon;

            if (!string.IsNullOrEmpty(power.IconPath))
            {
                try { var t = GD.Load<Texture2D>(power.IconPath); if (t != null) return t; } catch { }
            }
            if (!string.IsNullOrEmpty(power.PackedIconPath))
            {
                try { var t = GD.Load<Texture2D>(power.PackedIconPath); if (t != null) return t; } catch { }
            }
            if (!string.IsNullOrEmpty(power.ResolvedBigIconPath))
            {
                try { var t = GD.Load<Texture2D>(power.ResolvedBigIconPath); if (t != null) return t; } catch { }
            }
        }
        catch { }
        return null;
    }

    public static Color GetPowerTypeColor(PowerType type)
    {
        return type switch
        {
            PowerType.Buff => new Color(0.4f, 0.95f, 0.55f),
            PowerType.Debuff => new Color(1f, 0.45f, 0.45f),
            _ => new Color(0.85f, 0.85f, 0.85f)
        };
    }

    public static IReadOnlyList<PowerModel> GetCreatureActivePowers(Creature? creature)
    {
        if (creature == null) return Array.Empty<PowerModel>();
        try
        {
            return creature.Powers ?? (IReadOnlyList<PowerModel>)Array.Empty<PowerModel>();
        }
        catch
        {
            return Array.Empty<PowerModel>();
        }
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

    public static CharacterModel? GetSelectedCharacterModel()
    {
        try
        {
            var player = GetActivePlayer();
            if (player?.Character != null)
            {
                return player.Character;
            }
        }
        catch { }

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = tree?.Root;
            if (root != null)
            {
                var selectScreen = FindNodeOfType<MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen>(root);
                if (selectScreen != null)
                {
                    var field = typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen).GetField("_selectedButton", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field?.GetValue(selectScreen) is MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectButton btn && btn.Character != null)
                    {
                        return btn.Character;
                    }
                }
            }
        }
        catch { }

        try
        {
            if (ModelDb.AllCharacters != null)
            {
                return ModelDb.AllCharacters.FirstOrDefault(c => c != null && c.Id.Entry.Equals("IRONCLAD", StringComparison.OrdinalIgnoreCase))
                    ?? ModelDb.AllCharacters.FirstOrDefault(c => c != null);
            }
        }
        catch { }

        return null;
    }

    public static T? FindNodeOfType<T>(Node parent) where T : Node
    {
        if (parent is T match) return match;
        int count = parent.GetChildCount();
        for (int i = 0; i < count; i++)
        {
            var child = parent.GetChild(i);
            var res = FindNodeOfType<T>(child);
            if (res != null) return res;
        }
        return null;
    }

    public static string CleanBbCode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        // Strip custom STS2 & Godot BBCode tags like [jitter], [sine], [b], [i], [orange], [gold], [aqua], [color=...], etc.
        string cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[a-zA-Z0-9_#=]+\]", "");
        // Clean up excessive whitespace/blank lines while preserving readable paragraphs
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\n{3,}", "\n\n");
        return cleaned.Trim();
    }

    public static string ResolveDynamicVariables(string text, EventModel? ev)
    {
        if (string.IsNullOrWhiteSpace(text) || ev == null) return text ?? "";
        try
        {
            // 1. Resolve from DynamicVars dictionary
            var dynamicVarsProp = ev.GetType().GetProperty("DynamicVars", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dynamicVarsProp?.GetValue(ev) is System.Collections.IEnumerable dynamicVars)
            {
                foreach (var item in dynamicVars)
                {
                    if (item == null) continue;
                    var keyProp = item.GetType().GetProperty("Key");
                    var valProp = item.GetType().GetProperty("Value");
                    string? key = keyProp?.GetValue(item)?.ToString();
                    object? valObj = valProp?.GetValue(item);
                    if (!string.IsNullOrEmpty(key) && valObj != null)
                    {
                        string valStr = valObj.ToString() ?? "";
                        var intValProp = valObj.GetType().GetProperty("IntValue") ?? valObj.GetType().GetProperty("BaseValue");
                        if (intValProp != null)
                        {
                            var v = intValProp.GetValue(valObj);
                            if (v != null) valStr = v.ToString() ?? valStr;
                        }
                        text = text.Replace($"{{{key}}}", valStr, StringComparison.OrdinalIgnoreCase);
                    }
                }
            }

            // 2. Resolve from CanonicalVars if any remain
            var canonicalVarsProp = ev.GetType().GetProperty("CanonicalVars", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (canonicalVarsProp?.GetValue(ev) is System.Collections.IEnumerable canonicalVars)
            {
                foreach (var item in canonicalVars)
                {
                    if (item == null) continue;
                    var nameProp = item.GetType().GetProperty("Name");
                    var baseValProp = item.GetType().GetProperty("BaseValue") ?? item.GetType().GetProperty("IntValue");
                    string? name = nameProp?.GetValue(item)?.ToString();
                    object? bVal = baseValProp?.GetValue(item);
                    if (!string.IsNullOrEmpty(name) && bVal != null)
                    {
                        text = text.Replace($"{{{name}}}", bVal.ToString() ?? "", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ResolveDynamicVariables notice: {ex.Message}");
        }
        return text;
    }

    public static string FormatEventLocString(MegaCrit.Sts2.Core.Localization.LocString? loc, EventModel? ev)
    {
        if (loc == null) return "";
        try
        {
            // Bind dynamic variables if available
            if (ev?.DynamicVars != null)
            {
                try
                {
                    ev.DynamicVars.AddTo(loc);
                }
                catch { }
            }

            string formatted = loc.GetFormattedText();
            if (string.IsNullOrWhiteSpace(formatted) || formatted.StartsWith("LocString table", StringComparison.OrdinalIgnoreCase))
            {
                formatted = loc.GetRawText();
            }

            formatted = ResolveDynamicVariables(formatted, ev);
            return CleanBbCode(formatted);
        }
        catch
        {
            string raw = loc.ToString() ?? "";
            return CleanBbCode(ResolveDynamicVariables(raw, ev));
        }
    }

    public static string GetEventDescription(EventModel? ev)
    {
        if (ev == null) return "";
        try
        {
            string initDesc = FormatEventLocString(ev.InitialDescription, ev);
            if (!string.IsNullOrWhiteSpace(initDesc)) return initDesc;

            string desc = FormatEventLocString(ev.Description, ev);
            if (!string.IsNullOrWhiteSpace(desc)) return desc;

            foreach (var prop in ev.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (prop.Name.Contains("Desc", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Contains("Text", StringComparison.OrdinalIgnoreCase) ||
                    prop.Name.Contains("Story", StringComparison.OrdinalIgnoreCase))
                {
                    var val = prop.GetValue(ev);
                    if (val is MegaCrit.Sts2.Core.Localization.LocString loc)
                    {
                        string t = FormatEventLocString(loc, ev);
                        if (!string.IsNullOrWhiteSpace(t)) return t;
                    }
                    else if (val != null)
                    {
                        var method = val.GetType().GetMethod("GetFormattedText");
                        if (method != null)
                        {
                            string? text = method.Invoke(val, null) as string;
                            if (!string.IsNullOrWhiteSpace(text)) return CleanBbCode(ResolveDynamicVariables(text, ev));
                        }
                    }
                }
            }
        }
        catch { }
        return "";
    }

    public static string GetEventFullTooltip(EventInfo info)
    {
        if (info == null) return "";
        try
        {
            var ev = GetEventModel(info.Id);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(info.DisplayName);
            sb.AppendLine($"Category: {(info.IsAncient ? "Ancient Event" : "Standard Event")} | Type: {info.TypeName} | ID: {info.Id}");

            if (ev != null)
            {
                string desc = GetEventDescription(ev);
                if (!string.IsNullOrWhiteSpace(desc))
                {
                    sb.AppendLine();
                    sb.AppendLine(desc);
                }

                // Extract Choices / Options if available
                var optionLines = new List<string>();

                // 1. Try GameInfoOptions
                var gameInfoProp = ev.GetType().GetProperty("GameInfoOptions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (gameInfoProp != null)
                {
                    try
                    {
                        if (gameInfoProp.GetValue(ev) is System.Collections.IEnumerable infoOpts)
                        {
                            string? pendingTitle = null;
                            foreach (var item in infoOpts)
                            {
                                if (item is MegaCrit.Sts2.Core.Localization.LocString locItem)
                                {
                                    string text = FormatEventLocString(locItem, ev);
                                    if (string.IsNullOrWhiteSpace(text)) continue;

                                    if (locItem.LocEntryKey.EndsWith(".title", StringComparison.OrdinalIgnoreCase))
                                    {
                                        pendingTitle = text;
                                    }
                                    else if (locItem.LocEntryKey.EndsWith(".description", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (!string.IsNullOrEmpty(pendingTitle))
                                        {
                                            optionLines.Add($"• {pendingTitle}: {text}");
                                            pendingTitle = null;
                                        }
                                        else
                                        {
                                            optionLines.Add($"• {text}");
                                        }
                                    }
                                    else
                                    {
                                        optionLines.Add($"• {text}");
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(pendingTitle))
                            {
                                optionLines.Add($"• {pendingTitle}");
                            }
                        }
                    }
                    catch { }
                }

                // 2. Fallback to CurrentOptions / GenerateInitialOptions if GameInfoOptions had no items
                if (optionLines.Count == 0)
                {
                    try
                    {
                        var optionsProp = ev.GetType().GetProperty("CurrentOptions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        var options = optionsProp?.GetValue(ev) as System.Collections.IEnumerable;
                        if (options == null)
                        {
                            var genMethod = ev.GetType().GetMethod("GenerateInitialOptions", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            options = genMethod?.Invoke(ev, null) as System.Collections.IEnumerable;
                        }

                        if (options != null)
                        {
                            foreach (var opt in options)
                            {
                                if (opt == null) continue;
                                if (opt is MegaCrit.Sts2.Core.Events.EventOption evOpt)
                                {
                                    string t = FormatEventLocString(evOpt.Title, ev);
                                    string d = FormatEventLocString(evOpt.Description, ev);
                                    if (!string.IsNullOrWhiteSpace(t))
                                    {
                                        optionLines.Add(!string.IsNullOrWhiteSpace(d) ? $"• {t}: {d}" : $"• {t}");
                                    }
                                }
                                else if (opt is MegaCrit.Sts2.Core.Localization.LocString loc)
                                {
                                    string t = FormatEventLocString(loc, ev);
                                    if (!string.IsNullOrWhiteSpace(t)) optionLines.Add($"• {t}");
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (optionLines.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Choices / Options:");
                    foreach (var line in optionLines)
                    {
                        sb.AppendLine(line);
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetEventFullTooltip error: {ex.Message}");
            return info.DisplayName;
        }
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
            // 1. Try DynamicDescription first (the actual in-game functional text)
            try
            {
                string dyn = relic.DynamicDescription.GetFormattedText();
                if (!string.IsNullOrWhiteSpace(dyn))
                {
                    return CleanBbCode(dyn).Trim();
                }
            }
            catch { }

            // 2. Try HoverTip.Description
            try
            {
                var ht = relic.HoverTip;
                if (!string.IsNullOrWhiteSpace(ht.Description))
                {
                    return CleanBbCode(ht.Description).Trim();
                }
            }
            catch { }

            // 3. Try Description property
            try
            {
                var descProp = relic.GetType().GetProperty("Description", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (descProp?.GetValue(relic) is MegaCrit.Sts2.Core.Localization.LocString loc)
                {
                    string text = loc.GetFormattedText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return CleanBbCode(text).Trim();
                    }
                }
            }
            catch { }

            // 4. Try EventDescription
            try
            {
                var eventDescProp = relic.GetType().GetProperty("DynamicEventDescription", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ??
                                    relic.GetType().GetProperty("EventDescription", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (eventDescProp?.GetValue(relic) is MegaCrit.Sts2.Core.Localization.LocString loc)
                {
                    string text = loc.GetFormattedText();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return CleanBbCode(text).Trim();
                    }
                }
            }
            catch { }

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
            string title = !string.IsNullOrWhiteSpace(relic.Title.GetFormattedText()) ? CleanBbCode(relic.Title.GetFormattedText()) : relic.GetType().Name;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(title);
            sb.AppendLine($"Rarity: {relic.Rarity}");

            // 1. Relic main gameplay description
            string desc = GetRelicDescription(relic);
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.AppendLine();
                sb.AppendLine(desc);
            }

            // 2. Extra Keyword HoverTips (e.g. Doom, Strength, Block, etc.)
            try
            {
                var hoverTips = relic.HoverTips;
                if (hoverTips != null)
                {
                    foreach (var tip in hoverTips)
                    {
                        if (tip is MegaCrit.Sts2.Core.HoverTips.HoverTip ht)
                        {
                            string tipTitle = CleanBbCode(ht.Title ?? "").Trim();
                            string tipDesc = CleanBbCode(ht.Description ?? "").Trim();

                            // Skip if it's the main relic tooltip itself
                            if (tipTitle.Equals(title, StringComparison.OrdinalIgnoreCase) || 
                                tipDesc.Equals(desc, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(tipTitle) && !string.IsNullOrWhiteSpace(tipDesc))
                            {
                                sb.AppendLine();
                                sb.AppendLine($"[{tipTitle}]");
                                sb.AppendLine(tipDesc);
                            }
                        }
                        else if (tip is MegaCrit.Sts2.Core.HoverTips.CardHoverTip cardTip && cardTip.Card != null)
                        {
                            string cardTitle = CleanBbCode(cardTip.Card.Title ?? cardTip.Card.GetType().Name).Trim();
                            string cardDesc = CleanBbCode(cardTip.Card.Description?.GetFormattedText() ?? "").Trim();
                            if (!string.IsNullOrWhiteSpace(cardTitle) && !string.IsNullOrWhiteSpace(cardDesc))
                            {
                                sb.AppendLine();
                                sb.AppendLine($"[{cardTitle}]");
                                sb.AppendLine(cardDesc);
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Add Flavor lore if distinct and not a generic placeholder
            try
            {
                string flavor = CleanBbCode(relic.Flavor.GetFormattedText() ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(flavor) && 
                    !flavor.Contains("revealed in the future", StringComparison.OrdinalIgnoreCase) &&
                    !flavor.Equals(desc, StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine();
                    sb.AppendLine($"\"{flavor}\"");
                }
            }
            catch { }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"GetRelicFullTooltip error: {ex.Message}");
            return relic.GetType().Name;
        }
    }

    public static Texture2D? GetRelicIcon(RelicModel? relic)
    {
        if (relic == null) return null;
        try
        {
            if (relic.Icon != null) return relic.Icon;
            if (relic.BigIcon != null) return relic.BigIcon;
            if (!string.IsNullOrEmpty(relic.IconPath))
            {
                try { var t = GD.Load<Texture2D>(relic.IconPath); if (t != null) return t; } catch {}
            }
            if (!string.IsNullOrEmpty(relic.PackedIconPath))
            {
                try { var t = GD.Load<Texture2D>(relic.PackedIconPath); if (t != null) return t; } catch {}
            }
        }
        catch { }
        return null;
    }

    public static Texture2D? GetRelicIcon(string relicId)
    {
        if (string.IsNullOrWhiteSpace(relicId)) return null;
        var canonical = FindCanonicalRelicModel(relicId);
        return canonical != null ? GetRelicIcon(canonical) : null;
    }

    public static Color GetRelicRarityColor(RelicModel? relic)
    {
        if (relic == null) return new Color(1f, 1f, 1f);
        return relic.Rarity switch
        {
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Starter => new Color(0.65f, 0.9f, 0.65f),
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Common => new Color(0.85f, 0.85f, 0.9f),
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Uncommon => new Color(0.4f, 0.85f, 1f),
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Rare => new Color(1f, 0.85f, 0.3f),
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Shop => new Color(0.9f, 0.6f, 1f),
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Event => new Color(0.45f, 1f, 0.65f),
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Ancient => new Color(1f, 0.7f, 0.2f),
            _ => new Color(1f, 1f, 1f)
        };
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
            if (ModelDb.DebugEnchantments != null)
            {
                foreach (var e in ModelDb.DebugEnchantments)
                {
                    if (e != null && (e.GetType().Name.Equals(enchantmentId, StringComparison.OrdinalIgnoreCase) ||
                                     e.GetType().Name.Replace("Enchantment", "").Equals(enchantmentId, StringComparison.OrdinalIgnoreCase)))
                    {
                        return e;
                    }
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
        EnchantmentModel? result = null;

        try
        {
            result = canonical.ToMutable();
        }
        catch { }

        if (result == null)
        {
            try
            {
                var clone = (EnchantmentModel)canonical.ClonePreservingMutability();
                if (clone != null && !clone.IsCanonical)
                {
                    result = clone;
                }
            }
            catch { }
        }

        if (result == null)
        {
            try
            {
                var inst = Activator.CreateInstance(canonical.GetType()) as EnchantmentModel;
                if (inst != null)
                {
                    result = inst.IsCanonical ? inst.ToMutable() : inst;
                }
            }
            catch { }
        }

        if (result != null)
        {
            int intAmount = Math.Max(1, (int)amount);
            try
            {
                result.Amount = intAmount;
            }
            catch { }

            // Scale DynamicVars based on amount multiplier
            try
            {
                if (result.DynamicVars != null)
                {
                    var valuesProp = result.DynamicVars.GetType().GetProperty("Values", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var values = valuesProp?.GetValue(result.DynamicVars) as System.Collections.IEnumerable;
                    if (values != null)
                    {
                        foreach (var v in values)
                        {
                            if (v is MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar dynVar)
                            {
                                if (dynVar.Name.Equals("Times", StringComparison.OrdinalIgnoreCase))
                                {
                                    dynVar.BaseValue = amount;
                                }
                                else if (dynVar.Name.Equals("Weak", StringComparison.OrdinalIgnoreCase) || dynVar.Name.Equals("WeakPower", StringComparison.OrdinalIgnoreCase))
                                {
                                    dynVar.BaseValue = amount;
                                }
                                else if (dynVar.Name.Equals("Block", StringComparison.OrdinalIgnoreCase))
                                {
                                    dynVar.BaseValue = amount;
                                }
                                else if (dynVar.Name.Equals("Damage", StringComparison.OrdinalIgnoreCase))
                                {
                                    decimal orig = 3m;
                                    try
                                    {
                                        if (canonical.DynamicVars?.Damage != null && canonical.DynamicVars.Damage.BaseValue > 0)
                                        {
                                            orig = canonical.DynamicVars.Damage.BaseValue;
                                        }
                                    }
                                    catch { }
                                    dynVar.BaseValue = orig * amount;
                                }
                                else
                                {
                                    decimal orig = 1m;
                                    try
                                    {
                                        if (canonical.DynamicVars != null && canonical.DynamicVars[dynVar.Name] != null)
                                        {
                                            orig = canonical.DynamicVars[dynVar.Name].BaseValue;
                                        }
                                    }
                                    catch { }
                                    dynVar.BaseValue = orig > 0 ? orig * amount : amount;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            try
            {
                result.RecalculateValues();
            }
            catch { }
        }

        return result;
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
                        if (player.RunState != null && !player.RunState.ContainsCard(scopedCard))
                        {
                            player.RunState.AddCard(scopedCard, player);
                        }
                        ModLogger.Verbose("GameHelper", "Created card via player.RunState ICardScope.");
                        return scopedCard;
                    }
                }
                catch { }
            }

            try
            {
                var clone = canonical.CreateCloneForPlayer(player);
                if (clone != null)
                {
                    if (player.RunState != null && !player.RunState.ContainsCard(clone))
                    {
                        player.RunState.AddCard(clone, player);
                    }
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
                if (player?.RunState != null && !player.RunState.ContainsCard(mutable))
                {
                    player.RunState.AddCard(mutable, player);
                }
                ModLogger.Verbose("GameHelper", "Created card via canonical.ToMutable.");
                return mutable;
            }
        }
        catch { }

        try
        {
            var inst = Activator.CreateInstance(canonical.GetType()) as CardModel;
            if (inst != null && player?.RunState != null && !player.RunState.ContainsCard(inst))
            {
                player.RunState.AddCard(inst, player);
            }
            ModLogger.Verbose("GameHelper", "Created card via Activator.CreateInstance fallback.");
            return inst;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Creates a card model instance specifically scoped and registered in the player's active CombatState.
    /// This prevents turn-loop softlocks when adding cards mid-combat.
    /// </summary>
    public static CardModel? CreateCombatCardForPlayer(CardModel canonical, Player? player = null)
    {
        if (canonical == null) return null;
        player ??= GetActivePlayer();
        ModLogger.Verbose("GameHelper", $"CreateCombatCardForPlayer: canonical={canonical.GetType().Name}, player={player?.GetType().Name ?? "null"}");

        var combatState = GetCombatState();

        if (player != null && combatState != null)
        {
            if (combatState is ICardScope combatScope)
            {
                try
                {
                    var scopedCombatCard = combatScope.CreateCard(canonical, player);
                    if (scopedCombatCard != null)
                    {
                        if (!combatState.ContainsCard(scopedCombatCard))
                        {
                            combatState.AddCard(scopedCombatCard, player);
                        }
                        ModLogger.Verbose("GameHelper", "Created combat card via CombatState ICardScope.");
                        return scopedCombatCard;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Debug($"combatScope.CreateCard notice: {ex.Message}");
                }
            }

            try
            {
                var clone = canonical.CreateCloneForPlayer(player);
                if (clone != null)
                {
                    if (!combatState.ContainsCard(clone))
                    {
                        combatState.AddCard(clone, player);
                    }
                    ModLogger.Verbose("GameHelper", "Created combat card via canonical.CreateCloneForPlayer and registered in CombatState.");
                    return clone;
                }
            }
            catch { }
        }

        // Fallback: regular player creation and explicit CombatState registration if active
        var fallback = CreateCardForPlayer(canonical, player);
        if (fallback != null && combatState != null && player != null && !combatState.ContainsCard(fallback))
        {
            try
            {
                combatState.AddCard(fallback, player);
                ModLogger.Verbose("GameHelper", "Explicitly registered fallback card in CombatState.");
            }
            catch { }
        }
        return fallback;
    }

    /// <summary>
    /// Retrieves all active keywords/attributes assigned to a card model.
    /// </summary>
    public static IReadOnlySet<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword> GetCardKeywords(CardModel card)
    {
        if (card == null) return new HashSet<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword>();
        try
        {
            return card.Keywords ?? new HashSet<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword>();
        }
        catch
        {
            return new HashSet<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword>();
        }
    }

    /// <summary>
    /// Returns whether the card currently possesses the specified keyword/attribute.
    /// </summary>
    public static bool HasCardKeyword(CardModel card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword keyword)
    {
        if (card == null) return false;
        try
        {
            return card.Keywords != null && card.Keywords.Contains(keyword);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns all relevant CardKeyword values available in the game.
    /// </summary>
    public static MegaCrit.Sts2.Core.Entities.Cards.CardKeyword[] GetAllCardKeywords()
    {
        return new[]
        {
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal,
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Exhaust,
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Eternal,
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Unplayable,
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Retain,
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Innate,
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Sly
        };
    }

    /// <summary>
    /// Returns a distinct theme color for rendering keyword badges.
    /// </summary>
    public static Color GetKeywordBadgeColor(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword keyword)
    {
        return keyword switch
        {
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal => new Color(0.35f, 0.85f, 1f),      // Cyan / Ghostly
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Exhaust => new Color(1f, 0.55f, 0.2f),       // Fiery Orange
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Eternal => new Color(1f, 0.85f, 0.25f),      // Gold / Eternal
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Unplayable => new Color(1f, 0.35f, 0.35f),   // Crimson / Red
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Retain => new Color(0.4f, 0.95f, 0.45f),     // Emerald Green
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Innate => new Color(0.85f, 0.45f, 1f),      // Magenta / Purple
            MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Sly => new Color(0.3f, 0.95f, 0.85f),        // Teal / Sly
            _ => new Color(0.8f, 0.8f, 0.8f)
        };
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

    /// <summary>
    /// Configures a SpinBox's internal LineEdit to reject letter typing so only numbers are accepted.
    /// </summary>
    public static SpinBox MakeNumericOnly(this SpinBox spinBox)
    {
        if (spinBox == null) return spinBox!;
        var lineEdit = spinBox.GetLineEdit();
        if (lineEdit == null) return spinBox;

        bool allowDecimals = (spinBox.Step > 0 && Math.Abs(spinBox.Step - Math.Floor(spinBox.Step)) > 0.00001) ||
                             Math.Abs(spinBox.MinValue - Math.Floor(spinBox.MinValue)) > 0.00001 ||
                             Math.Abs(spinBox.MaxValue - Math.Floor(spinBox.MaxValue)) > 0.00001;
        bool allowNegative = spinBox.MinValue < 0;

        lineEdit.VirtualKeyboardType = allowDecimals
            ? LineEdit.VirtualKeyboardTypeEnum.NumberDecimal
            : LineEdit.VirtualKeyboardTypeEnum.Number;

        bool isUpdating = false;
        lineEdit.TextChanged += (newText) =>
        {
            if (isUpdating || string.IsNullOrEmpty(newText)) return;

            int oldCaret = lineEdit.CaretColumn;
            var filtered = new System.Text.StringBuilder(newText.Length);
            bool hasDecimal = false;

            for (int i = 0; i < newText.Length; i++)
            {
                char c = newText[i];
                if (char.IsDigit(c))
                {
                    filtered.Append(c);
                }
                else if (allowNegative && c == '-' && filtered.Length == 0)
                {
                    filtered.Append(c);
                }
                else if (allowDecimals && (c == '.' || c == ',') && !hasDecimal)
                {
                    filtered.Append('.');
                    hasDecimal = true;
                }
            }

            string filteredText = filtered.ToString();
            if (filteredText != newText)
            {
                isUpdating = true;
                int diff = newText.Length - filteredText.Length;
                lineEdit.Text = filteredText;
                lineEdit.CaretColumn = Math.Clamp(oldCaret - diff, 0, filteredText.Length);
                isUpdating = false;
            }
        };

        return spinBox;
    }
}
