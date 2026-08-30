using System;
using System.Collections.Generic;
using Godot;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using AIOTweaks.Cheats;
using AIOTweaks.UI.Menu;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace AIOTweaks.UI.Overlay;

/// <summary>
/// In-game Godot overlay providing an interactive cheat console, quick toggles, and live log viewer.
/// </summary>
public partial class DebugConsole : CanvasLayer
{
    private PanelContainer? _rootPanel;
    private RichTextLabel? _logLabel;
    private LineEdit? _commandInput;
    private Button? _godModeBtn;
    private Button? _infEnergyBtn;
    private Button? _oneHitKillBtn;
    private bool _isConsoleVisible = false;

    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;

    public override void _Ready()
    {
        ModLogger.Verbose("DebugConsole", "_Ready called: setting Layer=128, constructing UI...");
        Layer = 128; // Ensure it renders on top of game UI
        SetupUI();

        ModLogger.OnLogged += OnLogReceived;
        RuntimeStateManager.OnStateReset += OnSessionReset;

        SetConsoleVisibility(ConfigManager.Current.UI.ShowDebugConsoleOnStart);
        ModLogger.Info("DebugConsole initialized and ready.");
    }

    public override void _ExitTree()
    {
        ModLogger.Verbose("DebugConsole", "_ExitTree called: cleaning up listeners and blocking state...");
        UpdateBlockingState(false);
        ModLogger.OnLogged -= OnLogReceived;
        RuntimeStateManager.OnStateReset -= OnSessionReset;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            // Toggle AIOTweaks Console
            string consoleHotkey = !string.IsNullOrWhiteSpace(ConfigManager.Current.General.ConsoleHotkey) && !ConfigManager.Current.General.ConsoleHotkey.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? ConfigManager.Current.General.ConsoleHotkey
                : GeneralConfig.DefaultConsoleHotkey;
            if (GameHelper.IsKeyMatch(keyEvent, consoleHotkey))
            {
                ModLogger.Verbose("DebugConsole", $"Console hotkey matched ({keyEvent.Keycode}). Toggling visibility...");
                ToggleConsoleVisibility();
                GetViewport().SetInputAsHandled();
                return;
            }

            // Close console on Escape if currently visible
            if (_isConsoleVisible && keyEvent.Keycode == Key.Escape)
            {
                ModLogger.Verbose("DebugConsole", "Escape key pressed while console visible. Closing console...");
                SetConsoleVisibility(false);
                GetViewport().SetInputAsHandled();
                return;
            }

            // If console is NOT visible, allow opening GUI overlay and quick cheat shortcuts
            if (!_isConsoleVisible)
            {
                // Toggle GUI Menu Overlay (Default: F3)
                string guiHotkey = !string.IsNullOrWhiteSpace(ConfigManager.Current.General.GuiOverlayHotkey) && !ConfigManager.Current.General.GuiOverlayHotkey.Equals("None", StringComparison.OrdinalIgnoreCase)
                    ? ConfigManager.Current.General.GuiOverlayHotkey
                    : GeneralConfig.DefaultGuiOverlayHotkey;
                if (GameHelper.IsKeyMatch(keyEvent, guiHotkey))
                {
                    ModLogger.Verbose("DebugConsole", $"GUI overlay hotkey matched ({guiHotkey}). Toggling ModSettingsDialog...");
                    ModSettingsDialog.ToggleDialog();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                // Quick God Mode
                if (GameHelper.IsKeyMatch(keyEvent, ConfigManager.Current.General.QuickGodModeKey))
                {
                    ModLogger.Verbose("DebugConsole", "Quick God Mode hotkey matched. Toggling GodMode...");
                    CombatDirector.ToggleGodMode();
                    UpdateStatusButtons();
                    GetViewport().SetInputAsHandled();
                    return;
                }

                // Quick Kill Enemies
                if (GameHelper.IsKeyMatch(keyEvent, ConfigManager.Current.General.QuickKillEnemiesKey))
                {
                    ModLogger.Verbose("DebugConsole", "Quick Kill Enemies hotkey matched. Invoking KillAllEnemies...");
                    CombatDirector.KillAllEnemies();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
        }

        // When console is shown, consume all remaining unhandled inputs so normal game inputs/hotkeys are disabled
        if (_isConsoleVisible)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public void ToggleConsoleVisibility()
    {
        SetConsoleVisibility(!_isConsoleVisible);
    }

    public void SetConsoleVisibility(bool visible)
    {
        ModLogger.Verbose("DebugConsole", $"SetConsoleVisibility: {visible}");
        _isConsoleVisible = visible;
        if (_rootPanel != null)
        {
            _rootPanel.Visible = visible;
            if (visible)
            {
                _commandInput?.GrabFocus();
                UpdateBlockingState(true);
            }
            else
            {
                _commandInput?.ReleaseFocus();
                UpdateBlockingState(false);
            }
        }
        UpdateStatusButtons();
    }

    private void UpdateBlockingState(bool block)
    {
        try
        {
            var hotkeyManager = NGame.Instance?.HotkeyManager;
            if (hotkeyManager != null && GodotObject.IsInstanceValid(hotkeyManager))
            {
                var targetNode = (Node?)_rootPanel ?? this;
                if (block)
                {
                    hotkeyManager.AddBlockingScreen(targetNode);
                    ModLogger.Verbose("DebugConsole", "DebugConsole: In-game hotkey input disabled while console is shown.");
                }
                else
                {
                    hotkeyManager.RemoveBlockingScreen(targetNode);
                    ModLogger.Verbose("DebugConsole", "DebugConsole: In-game hotkey input restored.");
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"DebugConsole UpdateBlockingState note: {ex.Message}");
        }
    }

    private static Theme CreateConsoleTheme()
    {
        var theme = new Theme();
        var tooltipStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.10f, 0.98f),
            BorderColor = new Color(0.35f, 0.6f, 0.95f, 0.9f),
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            ContentMarginBottom = 10,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ShadowColor = new Color(0, 0, 0, 0.85f),
            ShadowSize = 6
        };

        theme.SetStylebox("panel", "TooltipPanel", tooltipStyle);
        theme.SetColor("font_color", "TooltipLabel", new Color(0.95f, 0.95f, 0.98f, 1f));
        theme.SetColor("font_shadow_color", "TooltipLabel", new Color(0, 0, 0, 0.95f));
        theme.SetConstant("shadow_offset_x", "TooltipLabel", 1);
        theme.SetConstant("shadow_offset_y", "TooltipLabel", 1);
        theme.SetConstant("font_size", "TooltipLabel", 14);

        return theme;
    }

    private void SetupUI()
    {
        _rootPanel = new PanelContainer
        {
            Name = "DebugConsolePanel",
            AnchorLeft = 0.05f,
            AnchorTop = 0.05f,
            AnchorRight = 0.95f,
            AnchorBottom = 0.65f,
            Theme = CreateConsoleTheme(),
            Visible = false
        };
        AddChild(_rootPanel);

        var vbox = new VBoxContainer { Name = "MainLayout" };
        vbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _rootPanel.AddChild(vbox);

        // Header bar
        var headerHBox = new HBoxContainer();
        var title = new Label { Text = " [AIOTweaks] Slay the Spire 2 Sandbox & Debug Console ", Modulate = new Color(0.3f, 0.85f, 1f) };
        var closeBtn = new Button { Text = " X " };
        closeBtn.Pressed += () => SetConsoleVisibility(false);
        headerHBox.AddChild(title);
        headerHBox.AddSpacer(false);
        headerHBox.AddChild(closeBtn);
        vbox.AddChild(headerHBox);

        // Fast Action Bar
        var fastBar = new HBoxContainer();
        _godModeBtn = CreateActionButton("God Mode: OFF", () => { CombatDirector.ToggleGodMode(); UpdateStatusButtons(); });
        _infEnergyBtn = CreateActionButton("Inf Energy: OFF", () => { CombatDirector.ToggleInfiniteEnergy(); UpdateStatusButtons(); });
        _oneHitKillBtn = CreateActionButton("1-Hit Kill: OFF", () => { CombatDirector.ToggleOneHitKill(); UpdateStatusButtons(); });
        var killAllBtn = CreateActionButton("Kill All", CombatDirector.KillAllEnemies);
        var addGoldBtn = CreateActionButton("+500 Gold", () => InventoryDirector.AddGold(500));
        var healBtn = CreateActionButton("Heal +50", () => InventoryDirector.Heal(50));
        var dmgBtn = CreateActionButton("Dmg 25", () => InventoryDirector.DamagePlayer(25));
        var drawBtn = CreateActionButton("Draw 3", () => CombatDirector.DrawCards(3));
        var clearLogBtn = CreateActionButton("Clear Log", ClearLog);

        fastBar.AddChild(_godModeBtn);
        fastBar.AddChild(_infEnergyBtn);
        fastBar.AddChild(_oneHitKillBtn);
        fastBar.AddChild(killAllBtn);
        fastBar.AddChild(addGoldBtn);
        fastBar.AddChild(healBtn);
        fastBar.AddChild(dmgBtn);
        fastBar.AddChild(drawBtn);
        fastBar.AddChild(clearLogBtn);
        vbox.AddChild(fastBar);

        // Scrollable Log Console
        _logLabel = new RichTextLabel
        {
            Name = "ConsoleLog",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            ScrollFollowing = true,
            BbcodeEnabled = true
        };
        vbox.AddChild(_logLabel);

        // Command line input
        var inputHBox = new HBoxContainer();
        var promptLabel = new Label { Text = "> " };
        _commandInput = new LineEdit
        {
            PlaceholderText = "Type command (e.g. 'help', 'gold 1000', 'relic Vajra', 'card Strike', 'event BigFish')...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _commandInput.TextSubmitted += ExecuteCommand;
        _commandInput.GuiInput += @event =>
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                if (keyEvent.Keycode == Key.Up && !keyEvent.Echo)
                {
                    if (_commandHistory.Count > 0)
                    {
                        _historyIndex = Math.Clamp(_historyIndex - 1, 0, _commandHistory.Count - 1);
                        _commandInput.Text = _commandHistory[_historyIndex];
                        _commandInput.CaretColumn = _commandInput.Text.Length;
                        GetViewport().SetInputAsHandled();
                    }
                }
                else if (keyEvent.Keycode == Key.Down && !keyEvent.Echo)
                {
                    if (_commandHistory.Count > 0)
                    {
                        _historyIndex = Math.Clamp(_historyIndex + 1, 0, _commandHistory.Count);
                        if (_historyIndex < _commandHistory.Count)
                        {
                            _commandInput.Text = _commandHistory[_historyIndex];
                        }
                        else
                        {
                            _commandInput.Text = string.Empty;
                        }
                        _commandInput.CaretColumn = _commandInput.Text.Length;
                        GetViewport().SetInputAsHandled();
                    }
                }
                else if (keyEvent.Keycode == Key.Escape)
                {
                    SetConsoleVisibility(false);
                    GetViewport().SetInputAsHandled();
                }
            }
        };
        var sendBtn = new Button { Text = " Run " };
        sendBtn.Pressed += () =>
        {
            if (_commandInput != null && !string.IsNullOrWhiteSpace(_commandInput.Text))
            {
                ExecuteCommand(_commandInput.Text);
            }
        };

        inputHBox.AddChild(promptLabel);
        inputHBox.AddChild(_commandInput);
        inputHBox.AddChild(sendBtn);
        vbox.AddChild(inputHBox);
    }

    private Button CreateActionButton(string text, Action onClick)
    {
        var btn = new Button { Text = text };
        btn.Pressed += onClick;
        return btn;
    }

    private void UpdateStatusButtons()
    {
        if (_godModeBtn != null)
        {
            _godModeBtn.Text = $"God Mode: {(RuntimeStateManager.GodModeEnabled ? "ON" : "OFF")}";
            _godModeBtn.Modulate = RuntimeStateManager.GodModeEnabled ? new Color(0.2f, 1f, 0.4f) : Colors.White;
        }

        if (_infEnergyBtn != null)
        {
            _infEnergyBtn.Text = $"Inf Energy: {(RuntimeStateManager.InfiniteEnergyEnabled ? "ON" : "OFF")}";
            _infEnergyBtn.Modulate = RuntimeStateManager.InfiniteEnergyEnabled ? new Color(0.2f, 1f, 0.4f) : Colors.White;
        }

        if (_oneHitKillBtn != null)
        {
            _oneHitKillBtn.Text = $"1-Hit Kill: {(RuntimeStateManager.OneHitKillEnabled ? "ON" : "OFF")}";
            _oneHitKillBtn.Modulate = RuntimeStateManager.OneHitKillEnabled ? new Color(1f, 0.4f, 0.4f) : Colors.White;
        }
    }

    private void ExecuteCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;

        string trimmed = input.Trim();
        ModLogger.Verbose("DebugConsole", $"ExecuteCommand: '{trimmed}'");
        _commandHistory.Add(trimmed);
        _historyIndex = _commandHistory.Count;
        if (_commandInput != null) _commandInput.Text = string.Empty;

        LogToConsole($"[color=yellow]> {trimmed}[/color]");

        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "help":
                LogToConsole("[color=cyan]Commands available:\n" +
                             "  god, infenergy, onehitkill, killall, endturn\n" +
                             "  gold <amount>, setgold <amount>, heal <amount>, damage <amount>, setmaxhp <amount>\n" +
                             "  relic <id>, rmrelic <id>, card <id> [upgraded=true/false], handcard <id>\n" +
                             "  event <id>, clearevent\n" +
                             "  draw <count>, energy <amount>, verbose [on/off], clear, reset[/color]");
                break;

            case "verbose":
            case "debuglog":
                if (parts.Length > 1)
                {
                    bool enable = parts[1].Equals("on", StringComparison.OrdinalIgnoreCase) || parts[1].Equals("true", StringComparison.OrdinalIgnoreCase) || parts[1] == "1";
                    ConfigManager.Current.General.DebugLogging = enable;
                    ModLogger.MinimumLevel = enable ? LogLevel.Debug : LogLevel.Info;
                    LogToConsole($"[color=green]Verbose debugging log is now {(enable ? "ENABLED" : "DISABLED")}.[/color]");
                }
                else
                {
                    bool toggle = !ConfigManager.Current.General.DebugLogging;
                    ConfigManager.Current.General.DebugLogging = toggle;
                    ModLogger.MinimumLevel = toggle ? LogLevel.Debug : LogLevel.Info;
                    LogToConsole($"[color=green]Verbose debugging log toggled to {(toggle ? "ENABLED" : "DISABLED")}.[/color]");
                }
                break;

            case "god":
                CombatDirector.ToggleGodMode();
                UpdateStatusButtons();
                break;

            case "infenergy":
                CombatDirector.ToggleInfiniteEnergy();
                UpdateStatusButtons();
                break;

            case "onehitkill":
            case "ohk":
                CombatDirector.ToggleOneHitKill();
                UpdateStatusButtons();
                break;

            case "killall":
                CombatDirector.KillAllEnemies();
                break;

            case "endturn":
                CombatDirector.EndTurn();
                break;

            case "gold":
                if (parts.Length > 1 && int.TryParse(parts[1], out int goldAmt))
                    InventoryDirector.AddGold(goldAmt);
                else
                    LogToConsole("[color=red]Usage: gold <amount>[/color]");
                break;

            case "setgold":
                if (parts.Length > 1 && int.TryParse(parts[1], out int sGold))
                    InventoryDirector.SetGold(sGold);
                else
                    LogToConsole("[color=red]Usage: setgold <amount>[/color]");
                break;

            case "heal":
                if (parts.Length > 1 && int.TryParse(parts[1], out int healAmt))
                    InventoryDirector.Heal(healAmt);
                else
                    LogToConsole("[color=red]Usage: heal <amount>[/color]");
                break;

            case "damage":
            case "dmg":
                if (parts.Length > 1 && int.TryParse(parts[1], out int dmgAmt))
                    InventoryDirector.DamagePlayer(dmgAmt);
                else
                    LogToConsole("[color=red]Usage: damage <amount>[/color]");
                break;

            case "setmaxhp":
                if (parts.Length > 1 && int.TryParse(parts[1], out int maxHp))
                    InventoryDirector.SetMaxHp(maxHp);
                else
                    LogToConsole("[color=red]Usage: setmaxhp <amount>[/color]");
                break;

            case "relic":
                if (parts.Length > 1)
                    RelicDirector.AddRelic(parts[1]);
                else
                    LogToConsole("[color=red]Usage: relic <relic_id>[/color]");
                break;

            case "rmrelic":
                if (parts.Length > 1)
                    RelicDirector.RemoveRelic(parts[1]);
                else
                    LogToConsole("[color=red]Usage: rmrelic <relic_id>[/color]");
                break;

            case "card":
                if (parts.Length > 1)
                {
                    bool up = parts.Length > 2 && bool.TryParse(parts[2], out bool u) && u;
                    CardDirector.AddCardToDeck(parts[1], up);
                }
                else
                {
                    LogToConsole("[color=red]Usage: card <card_id> [true/false][/color]");
                }
                break;

            case "handcard":
                if (parts.Length > 1)
                    CardDirector.SpawnCardInHand(parts[1]);
                else
                    LogToConsole("[color=red]Usage: handcard <card_id>[/color]");
                break;

            case "rmcard":
            case "removecard":
                if (parts.Length > 1)
                    CardDirector.RemoveCardFromDeck(parts[1]);
                else
                    LogToConsole("[color=red]Usage: rmcard <card_id>[/color]");
                break;

            case "upcard":
            case "upgradecard":
                if (parts.Length > 1)
                    CardDirector.ToggleUpgradeInDeck(parts[1]);
                else
                    LogToConsole("[color=red]Usage: upcard <card_id>[/color]");
                break;

            case "exhaustcard":
            case "exhaust":
                if (parts.Length > 1)
                    CardDirector.ExhaustCardFromDeck(parts[1]);
                else
                    LogToConsole("[color=red]Usage: exhaustcard <card_id>[/color]");
                break;

            case "enchantcard":
            case "enchant":
                if (parts.Length > 2)
                {
                    decimal amt = 1;
                    if (parts.Length > 3 && decimal.TryParse(parts[3], out decimal a)) amt = a;
                    CardDirector.EnchantCardInDeck(parts[1], parts[2], amt);
                }
                else
                {
                    LogToConsole("[color=red]Usage: enchant <card_id> <enchantment_id> [amount][/color]");
                }
                break;

            case "clearenchant":
            case "disenchant":
                if (parts.Length > 1)
                    CardDirector.ClearEnchantmentInDeck(parts[1]);
                else
                    LogToConsole("[color=red]Usage: disenchant <card_id>[/color]");
                break;

            case "attr":
            case "attribute":
            case "keyword":
                if (parts.Length > 2)
                {
                    if (Enum.TryParse<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword>(parts[2], true, out var kw))
                    {
                        string op = parts.Length > 3 ? parts[3].ToLowerInvariant() : "toggle";
                        if (op == "add") CardDirector.AddKeywordToDeck(parts[1], kw);
                        else if (op == "remove" || op == "rm") CardDirector.RemoveKeywordFromDeck(parts[1], kw);
                        else CardDirector.ToggleKeywordInDeck(parts[1], kw);
                    }
                    else
                    {
                        LogToConsole($"[color=red]Unknown keyword '{parts[2]}'. Valid: {string.Join(", ", GameHelper.GetAllCardKeywords())}[/color]");
                    }
                }
                else
                {
                    LogToConsole("[color=red]Usage: attr <card_id> <keyword> [add|remove|toggle][/color]");
                }
                break;

            case "event":
                if (parts.Length > 1)
                    EventDirector.ForceNextEvent(parts[1]);
                else
                    LogToConsole("[color=red]Usage: event <event_id>[/color]");
                break;

            case "clearevent":
                EventDirector.ClearForcedEvent();
                break;

            case "draw":
                if (parts.Length > 1 && int.TryParse(parts[1], out int drawCount))
                    CombatDirector.DrawCards(drawCount);
                else
                    LogToConsole("[color=red]Usage: draw <count>[/color]");
                break;

            case "energy":
                if (parts.Length > 1 && int.TryParse(parts[1], out int engAmt))
                    CombatDirector.AddEnergy(engAmt);
                else
                    LogToConsole("[color=red]Usage: energy <amount>[/color]");
                break;

            case "clear":
                ClearLog();
                break;

            case "reset":
                RuntimeStateManager.ResetSessionState();
                UpdateStatusButtons();
                LogToConsole("[color=green]Session cheats reset to default state.[/color]");
                break;

            default:
                GameHelper.ExecuteConsoleCommand(trimmed);
                LogToConsole($"[color=gray]Dispatched '{trimmed}' to engine command processor.[/color]");
                break;
        }
    }

    private void LogToConsole(string text)
    {
        if (_logLabel == null) return;
        _logLabel.AppendText($"{text}\n");
    }

    private void ClearLog()
    {
        if (_logLabel != null)
        {
            _logLabel.Clear();
            _logLabel.AppendText("[color=gray]--- Log Cleared ---[/color]\n");
        }
    }

    private void OnLogReceived(LogLevel level, string message)
    {
        string color = level switch
        {
            LogLevel.Debug => "gray",
            LogLevel.Info => "white",
            LogLevel.Warn => "yellow",
            LogLevel.Error => "red",
            _ => "white"
        };

        CallDeferred(nameof(DeferredAppendLog), $"[color={color}]{message}[/color]");
    }

    private void DeferredAppendLog(string bbcode)
    {
        LogToConsole(bbcode);
    }

    private void OnSessionReset()
    {
        CallDeferred(nameof(UpdateStatusButtons));
    }
}
