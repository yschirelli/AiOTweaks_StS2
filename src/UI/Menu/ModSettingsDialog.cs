using System;
using System.Linq;
using System.Collections.Generic;
using Godot;
using AIOTweaks.Core;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using AIOTweaks.Cheats;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace AIOTweaks.UI.Menu;

/// <summary>
/// Tabbed Mod Settings & Sandbox Dialog invoked via F3 (or configured GUI overlay hotkey).
/// </summary>
public partial class ModSettingsDialog : CanvasLayer
{
    private static ModSettingsDialog? _instance;
    public static ModSettingsDialog? Instance => _instance;

    private PanelContainer? _dialogPanel;
    private TabContainer? _tabs;

    // Hotkey fields
    private LineEdit? _consoleHotkeyInput;
    private LineEdit? _guiHotkeyInput;
    private LineEdit? _quickGodModeInput;
    private LineEdit? _quickKillEnemiesInput;

    // Tweak sliders & spinners
    private HSlider? _goldSlider;
    private HSlider? _shopDiscountSlider;
    private HSlider? _eliteSlider;
    private HSlider? _shopSlider;
    private HSlider? _eventSlider;
    private HSlider? _restSlider;
    private HSlider? _combatSlider;
    private SpinBox? _cardRewardSpin;
    private SpinBox? _bonusGoldSpin;
    private SpinBox? _bonusHpSpin;
    private SpinBox? _mapRoomCountSpin;
    private CheckBox? _forceNeowCheck;
    private Label? _tweaksRunLockNoticeLabel;

    // Enemy Multipliers & Endless Mode
    private HSlider? _enemyHpSlider;
    private HSlider? _enemyDmgSlider;
    private HSlider? _enemyDefSlider;
    private CheckBox? _endlessModeCheck;
    private SpinBox? _endlessMultiplierSpin;
    private CheckBox? _freeMapNavCheck;

    // Sandbox Checkboxes
    private CheckBox? _godModeCheck;
    private CheckBox? _infEnergyCheck;
    private CheckBox? _oneHitKillCheck;
    private CheckBox? _infPotionsCheck;
    private CheckBox? _noExhaustCheck;
    private SpinBox? _bonusDrawSpin;

    // Real-time Card Grids
    private GridContainer? _deckGrid;
    private GridContainer? _handGrid;
    private GridContainer? _drawGrid;
    private GridContainer? _discardGrid;
    private GridContainer? _exhaustGrid;
    private System.Collections.Generic.List<ItemEntry> _availableCardEntries = new();

    // Real-time Relic Grids
    private GridContainer? _activeRelicsGrid;
    private System.Collections.Generic.List<ItemEntry> _availableRelicEntries = new();

    // Spawner fields
    private LineEdit? _relicInput;
    private LineEdit? _cardInput;
    private SpinBox? _goldAmountSpin;
    private SpinBox? _currentHpAmountSpin;
    private SpinBox? _damageAmountSpin;
    private SpinBox? _maxHpAmountSpin;
    private Label? _eventOverrideLabel;
    private Button? _applyConfigBtn;
    private bool _tweaksModified = false;

    // Log terminal
    private RichTextLabel? _logLabel;
    private LineEdit? _commandInput;

    public override void _Ready()
    {
        _instance = this;
        Layer = 130; // Render above pause menu and gameplay UI
        SetupDialogUI();

        ModLogger.OnLogged += OnLogReceived;
        CardDirector.OnDeckChanged += OnDeckModified;
        RelicDirector.OnRelicsChanged += OnRelicsModified;
        LoadSettingsValues();
        HideDialog();

        var refreshTimer = new Timer { WaitTime = 1.0f, Autostart = true };
        refreshTimer.Timeout += () =>
        {
            RefreshRealTimeCardTabs();
            RefreshRealTimeRelicTabs();
        };
        AddChild(refreshTimer);
    }

    private void OnDeckModified()
    {
        CallDeferred(nameof(RefreshRealTimeCardTabs));
    }

    private void OnRelicsModified()
    {
        CallDeferred(nameof(RefreshRealTimeRelicTabs));
    }

    public override void _ExitTree()
    {
        UpdateBlockingState(false);
        ModLogger.OnLogged -= OnLogReceived;
        CardDirector.OnDeckChanged -= OnDeckModified;
        RelicDirector.OnRelicsChanged -= OnRelicsModified;
        if (_instance == this) _instance = null;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        bool isDialogOpen = _dialogPanel != null && _dialogPanel.Visible;

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            // Toggle via configured GUI overlay key (with failsafe fallback)
            string guiKey = !string.IsNullOrWhiteSpace(ConfigManager.Current.General.GuiOverlayHotkey) && !ConfigManager.Current.General.GuiOverlayHotkey.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? ConfigManager.Current.General.GuiOverlayHotkey
                : GeneralConfig.DefaultGuiOverlayHotkey;
            if (GameHelper.IsKeyMatch(keyEvent, guiKey))
            {
                ToggleDialog();
                GetViewport().SetInputAsHandled();
                return;
            }

            // Close dialog via Escape if open
            if (isDialogOpen && keyEvent.Keycode == Key.Escape)
            {
                CloseDialog();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (isDialogOpen)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public static void ShowDialog()
    {
        if (_instance != null)
        {
            _instance.OpenDialog();
        }
    }

    public static void HideDialog()
    {
        if (_instance != null)
        {
            _instance.CloseDialog();
        }
    }

    public static void ToggleDialog()
    {
        if (_instance != null)
        {
            if (_instance._dialogPanel != null && _instance._dialogPanel.Visible)
            {
                _instance.CloseDialog();
            }
            else
            {
                _instance.OpenDialog();
            }
        }
    }

    public void OpenDialog()
    {
        ModLogger.Verbose("ModSettingsDialog", "OpenDialog called. Loading settings values and computing run status...");
        if (_dialogPanel != null)
        {
            LoadSettingsValues();
            
            bool inRun = GameHelper.GetActivePlayer() != null;
            ModLogger.Verbose("ModSettingsDialog", $"Player inRun status: {inRun}");
            if (_tabs != null)
            {
                int tweaksIdx = 4; // Tweaks is the 5th tab
                _tabs.SetTabDisabled(tweaksIdx, false);
                if (_applyConfigBtn != null)
                {
                    _applyConfigBtn.Visible = (_tabs.CurrentTab == tweaksIdx);
                }
            }
            UpdateTweaksRunLockState(inRun);

            _dialogPanel.Visible = true;
            UpdateBlockingState(true);
            RefreshRealTimeCardTabs();
            RefreshRealTimeRelicTabs();
        }
    }

    public void CloseDialog()
    {
        ModLogger.Verbose("ModSettingsDialog", "CloseDialog called.");
        if (_dialogPanel != null)
        {
            _dialogPanel.Visible = false;
            UpdateBlockingState(false);
        }
    }

    private void UpdateBlockingState(bool block)
    {
        try
        {
            var hotkeyManager = NGame.Instance?.HotkeyManager;
            if (hotkeyManager != null && GodotObject.IsInstanceValid(hotkeyManager))
            {
                var targetNode = (Node?)_dialogPanel ?? this;
                if (block)
                {
                    hotkeyManager.AddBlockingScreen(targetNode);
                    ModLogger.Debug("ModSettingsDialog: In-game hotkey input disabled while settings dialog is open.");
                }
                else
                {
                    hotkeyManager.RemoveBlockingScreen(targetNode);
                    ModLogger.Debug("ModSettingsDialog: In-game hotkey input restored.");
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"ModSettingsDialog UpdateBlockingState note: {ex.Message}");
        }
    }

    private static Theme CreateModTheme()
    {
        var theme = new Theme();

        // High contrast, solid / non-transparent dark tooltip styling
        var tooltipStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.10f, 0.98f), // Solid, opaque dark background
            BorderColor = new Color(0.35f, 0.6f, 0.95f, 0.9f), // Crisp blue accent border
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

    private void SetupDialogUI()
    {
        // Dark background overlay backdrop
        var backdrop = new ColorRect
        {
            Name = "Backdrop",
            Color = new Color(0, 0, 0, 0.6f)
        };
        backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        backdrop.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                CloseDialog();
            }
        };

        _dialogPanel = new PanelContainer
        {
            Name = "DialogPanel",
            AnchorLeft = 0.015f,
            AnchorTop = 0.015f,
            AnchorRight = 0.985f,
            AnchorBottom = 0.985f,
            OffsetLeft = 0,
            OffsetTop = 0,
            OffsetRight = 0,
            OffsetBottom = 0,
            Theme = CreateModTheme(),
            Visible = false
        };
        _dialogPanel.AddChild(backdrop);

        var contentVBox = new VBoxContainer { Name = "ContentBox" };
        contentVBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _dialogPanel.AddChild(contentVBox);

        // Header
        var header = new HBoxContainer();
        var title = new Label
        {
            Text = "  AIOTweaks - In-Game Mod Settings & Sandbox Suite  ",
            Modulate = new Color(0.35f, 0.85f, 1f)
        };
        var closeBtn = new Button { Text = " X " };
        closeBtn.Pressed += CloseDialog;

        header.AddChild(title);
        header.AddSpacer(false);
        header.AddChild(closeBtn);
        contentVBox.AddChild(header);
        contentVBox.AddChild(new HSeparator());

        // Tab container
        _tabs = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        contentVBox.AddChild(_tabs);

        // 1. Relics Tab
        _tabs.AddChild(BuildRelicsTab());

        // 2. Cards Tab
        _tabs.AddChild(BuildCardsTab());

        // 3. Player Tab
        _tabs.AddChild(BuildPlayerTab());

        // 4. Combat Tab
        _tabs.AddChild(BuildCombatSandboxTab());

        // 5. Tweaks Tab
        _tabs.AddChild(BuildTweaksTab());

        _tabs.TabChanged += (tabIdx) =>
        {
            if (tabIdx == 0) // Relics Tab
            {
                RefreshRealTimeRelicTabs();
            }
            else if (tabIdx == 1) // Cards Tab
            {
                RefreshRealTimeCardTabs();
            }
            
            if (_applyConfigBtn != null)
            {
                _applyConfigBtn.Visible = (tabIdx == 4);
            }
        };

        // Bottom Action Bar (Save / Apply / Close)
        var footer = new HBoxContainer();
        _applyConfigBtn = new Button { Text = " Apply Configuration ", Disabled = true, Visible = false };
        _applyConfigBtn.Pressed += () =>
        {
            SaveSettingsValues();
            _applyConfigBtn.Disabled = true;
            _tweaksModified = false;
        };

        var defaultBtn = new Button { Text = " Reset to Game Defaults " };
        defaultBtn.Pressed += () =>
        {
            // Reset to defaults
            ConfigManager.Current.General.ConsoleHotkey = GeneralConfig.DefaultConsoleHotkey;
            ConfigManager.Current.General.GuiOverlayHotkey = GeneralConfig.DefaultGuiOverlayHotkey;
            ConfigManager.Current.General.QuickGodModeKey = "";
            ConfigManager.Current.General.QuickKillEnemiesKey = "";

            ConfigManager.Current.PreRunTweaks.MapRoomCount = 15;
            ConfigManager.Current.PreRunTweaks.GoldRewardMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.ShopDiscountMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.CardRewardCount = 3;
            ConfigManager.Current.PreRunTweaks.StartingGoldBonus = 0;
            ConfigManager.Current.PreRunTweaks.StartingMaxHpBonus = 0;
            ConfigManager.Current.PreRunTweaks.ForceNeowBonus = true;

            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EliteWeightMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.ShopWeightMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.EventWeightMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.RestSiteWeightMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.CombatWeightMultiplier = 1.0f;

            ConfigManager.Current.PreRunTweaks.EnemyHealthMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.EnemyDamageMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.EnemyDefendMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.FreeMapNavigation = false;
            ConfigManager.Current.PreRunTweaks.EndlessMode.Enabled = false;
            ConfigManager.Current.PreRunTweaks.EndlessMode.EnemyScalingMultiplier = 2.0f;

            ConfigManager.Current.CombatSandbox.GodMode = false;
            ConfigManager.Current.CombatSandbox.InfiniteEnergy = false;
            ConfigManager.Current.CombatSandbox.OneHitKill = false;
            ConfigManager.Current.CombatSandbox.InfinitePotions = false;
            ConfigManager.Current.CombatSandbox.NoCardExhaust = false;
            ConfigManager.Current.CombatSandbox.BonusDrawPerTurn = 0;

            RuntimeStateManager.ResetSessionState();
            LoadSettingsValues();
            ConfigManager.SaveConfig();
            ModLogger.Info("Reset all settings to game defaults.");

            MarkTweaksModified();
        };

        var doneBtn = new Button { Text = " Return to Game " };
        doneBtn.Pressed += CloseDialog;

        footer.AddChild(_applyConfigBtn);
        footer.AddChild(defaultBtn);
        footer.AddSpacer(false);
        footer.AddChild(doneBtn);
        contentVBox.AddChild(new HSeparator());
        contentVBox.AddChild(footer);

        AddChild(_dialogPanel);
    }

    private void MarkTweaksModified()
    {
        _tweaksModified = true;
        if (_applyConfigBtn != null)
        {
            _applyConfigBtn.Disabled = false;
        }
    }

    private Control BuildTweaksTab()
    {
        var scroll = new ScrollContainer { Name = "Tweaks & Multipliers" };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        // Run lock notification banner
        _tweaksRunLockNoticeLabel = new Label
        {
            Text = "[Locked] Pre-run map generation & starting bonus settings are locked during an active run.\n   (All options can be freely customized in the Main Menu)",
            Modulate = new Color(1f, 0.45f, 0.3f),
            Visible = false
        };
        vbox.AddChild(_tweaksRunLockNoticeLabel);

        vbox.AddChild(new Label { Text = "--- Keybindings & Hotkeys ---", Modulate = new Color(0.35f, 0.85f, 1f) });
        vbox.AddChild(new Label { Text = "Note: If left empty, default hotkeys (F1 for Console, F3 for GUI) will be automatically restored.", Modulate = new Color(0.7f, 0.7f, 0.7f) });

        var consoleKeyRow = new HBoxContainer();
        consoleKeyRow.AddChild(new Label { Text = "Console Hotkey: ", CustomMinimumSize = new Vector2(240, 0) });
        _consoleHotkeyInput = new LineEdit { PlaceholderText = "e.g. F1, Quoteleft (Default: F1)", CustomMinimumSize = new Vector2(200, 0) };
        _consoleHotkeyInput.TextChanged += _ => MarkTweaksModified();
        consoleKeyRow.AddChild(_consoleHotkeyInput);
        vbox.AddChild(consoleKeyRow);

        var guiKeyRow = new HBoxContainer();
        guiKeyRow.AddChild(new Label { Text = "GUI Menu Overlay Hotkey: ", CustomMinimumSize = new Vector2(240, 0) });
        _guiHotkeyInput = new LineEdit { PlaceholderText = "e.g. F3, F8 (Default: F3)", CustomMinimumSize = new Vector2(200, 0) };
        _guiHotkeyInput.TextChanged += _ => MarkTweaksModified();
        guiKeyRow.AddChild(_guiHotkeyInput);
        vbox.AddChild(guiKeyRow);

        var godKeyRow = new HBoxContainer();
        godKeyRow.AddChild(new Label { Text = "Quick God Mode Hotkey: ", CustomMinimumSize = new Vector2(240, 0) });
        _quickGodModeInput = new LineEdit { PlaceholderText = "e.g. F2 (Empty = Disabled)", CustomMinimumSize = new Vector2(200, 0) };
        _quickGodModeInput.TextChanged += _ => MarkTweaksModified();
        godKeyRow.AddChild(_quickGodModeInput);
        vbox.AddChild(godKeyRow);

        var killKeyRow = new HBoxContainer();
        killKeyRow.AddChild(new Label { Text = "Quick Kill All Enemies Hotkey: ", CustomMinimumSize = new Vector2(240, 0) });
        _quickKillEnemiesInput = new LineEdit { PlaceholderText = "e.g. F4 (Empty = Disabled)", CustomMinimumSize = new Vector2(200, 0) };
        _quickKillEnemiesInput.TextChanged += _ => MarkTweaksModified();
        killKeyRow.AddChild(_quickKillEnemiesInput);
        vbox.AddChild(killKeyRow);

        vbox.AddChild(new HSeparator());
        vbox.AddChild(new Label { Text = "--- Pre-Run Tweaks & Map Generation (Pre-Run Only) ---", Modulate = new Color(1f, 0.85f, 0.3f) });

        var mapRoomRow = new HBoxContainer();
        mapRoomRow.AddChild(new Label { Text = "Map Size (Floors / Room Count): ", CustomMinimumSize = new Vector2(240, 0) });
        _mapRoomCountSpin = new SpinBox { MinValue = 15, MaxValue = 30, Value = 15, Step = 1, TooltipText = "Sets the number of rooms/floors per act (15 to 30). Minimum 15 is required by map generator." };
        _mapRoomCountSpin.ValueChanged += _ => MarkTweaksModified();
        mapRoomRow.AddChild(_mapRoomCountSpin);
        vbox.AddChild(mapRoomRow);

        _goldSlider = AddSliderControl(vbox, "Gold Drop Multiplier:", 0.1f, 5.0f, 0.1f, 1.0f);
        _goldSlider.ValueChanged += _ => MarkTweaksModified();
        _shopDiscountSlider = AddSliderControl(vbox, "Shop Discount Multiplier:", 0.1f, 2.0f, 0.05f, 1.0f);
        _shopDiscountSlider.ValueChanged += _ => MarkTweaksModified();

        var cardRewardRow = new HBoxContainer();
        cardRewardRow.AddChild(new Label { Text = "Card Choices per Reward: ", CustomMinimumSize = new Vector2(240, 0) });
        _cardRewardSpin = new SpinBox { MinValue = 1, MaxValue = 10, Value = 3 };
        _cardRewardSpin.ValueChanged += _ => MarkTweaksModified();
        cardRewardRow.AddChild(_cardRewardSpin);
        vbox.AddChild(cardRewardRow);

        var startGoldRow = new HBoxContainer();
        startGoldRow.AddChild(new Label { Text = "Starting Gold Bonus: ", CustomMinimumSize = new Vector2(240, 0) });
        _bonusGoldSpin = new SpinBox { MinValue = 0, MaxValue = 9999, Step = 25, Value = 0 };
        _bonusGoldSpin.ValueChanged += _ => MarkTweaksModified();
        startGoldRow.AddChild(_bonusGoldSpin);
        vbox.AddChild(startGoldRow);

        var startHpRow = new HBoxContainer();
        startHpRow.AddChild(new Label { Text = "Starting Max HP Bonus: ", CustomMinimumSize = new Vector2(240, 0) });
        _bonusHpSpin = new SpinBox { MinValue = 0, MaxValue = 500, Step = 5, Value = 0 };
        _bonusHpSpin.ValueChanged += _ => MarkTweaksModified();
        startHpRow.AddChild(_bonusHpSpin);
        vbox.AddChild(startHpRow);

        _forceNeowCheck = new CheckBox { Text = " Spawn Neow at start? (Uncheck to skip Neow and start directly on map)", TooltipText = "Guarantees Neow blessing when checked. When unchecked, skips Neow and starts directly on the map." };
        _forceNeowCheck.Toggled += _ => MarkTweaksModified();
        vbox.AddChild(_forceNeowCheck);

        vbox.AddChild(new HSeparator());
        vbox.AddChild(new Label { Text = "--- Map Node Generation Weights ---", Modulate = new Color(0.4f, 1f, 0.6f) });
        var fairNote = new Label
        {
            Text = "Note: Fair Play: Non-default map multipliers mark runs as Seeded/Custom (locks achievements/unlocks).\n   Keep all at 1.0x for standard runs.",
            Modulate = new Color(1f, 0.8f, 0.4f)
        };
        vbox.AddChild(fairNote);

        _eliteSlider = AddSliderControl(vbox, "Elite Encounter Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _eliteSlider.ValueChanged += _ => MarkTweaksModified();
        _shopSlider = AddSliderControl(vbox, "Shop Node Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _shopSlider.ValueChanged += _ => MarkTweaksModified();
        _eventSlider = AddSliderControl(vbox, "Event / Unknown Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _eventSlider.ValueChanged += _ => MarkTweaksModified();
        _restSlider = AddSliderControl(vbox, "Rest Site Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _restSlider.ValueChanged += _ => MarkTweaksModified();
        _combatSlider = AddSliderControl(vbox, "Normal Combat Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _combatSlider.ValueChanged += _ => MarkTweaksModified();

        vbox.AddChild(new HSeparator());
        vbox.AddChild(new Label { Text = "--- Enemy Multipliers & Scaling ---", Modulate = new Color(1f, 0.4f, 0.4f) });

        _enemyHpSlider = AddSliderControl(vbox, "Enemy Health Multiplier:", 0.1f, 10.0f, 0.1f, 1.0f);
        _enemyHpSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.EnemyHealthMultiplier = (float)val;
            GameHelper.RefreshCombatIntents();
        };

        _enemyDmgSlider = AddSliderControl(vbox, "Enemy Damage Multiplier:", 0.0f, 10.0f, 0.1f, 1.0f);
        _enemyDmgSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.EnemyDamageMultiplier = (float)val;
            GameHelper.RefreshCombatIntents();
        };

        _enemyDefSlider = AddSliderControl(vbox, "Enemy Defend/Block Multiplier:", 0.0f, 10.0f, 0.1f, 1.0f);
        _enemyDefSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.EnemyDefendMultiplier = (float)val;
            GameHelper.RefreshCombatIntents();
        };

        vbox.AddChild(new HSeparator());
        vbox.AddChild(new Label { Text = "--- Endless Mode & Free Map Navigation ---", Modulate = new Color(0.8f, 0.5f, 1f) });

        _endlessModeCheck = new CheckBox { Text = " Enable Endless Mode (Scale enemies progressively each loop reset)" };
        _endlessModeCheck.Toggled += _ => MarkTweaksModified();
        vbox.AddChild(_endlessModeCheck);

        var endlessMultRow = new HBoxContainer();
        endlessMultRow.AddChild(new Label { Text = "Endless Loop Scaling Multiplier: ", CustomMinimumSize = new Vector2(240, 0) });
        _endlessMultiplierSpin = new SpinBox { MinValue = 1.0, MaxValue = 10.0, Step = 0.1, Value = 2.0 };
        _endlessMultiplierSpin.ValueChanged += _ => MarkTweaksModified();
        endlessMultRow.AddChild(_endlessMultiplierSpin);
        vbox.AddChild(endlessMultRow);

        _freeMapNavCheck = new CheckBox { Text = " Free Map Navigation (Flying Boots mode: click & travel to ANY room freely on map)" };
        _freeMapNavCheck.Toggled += val =>
        {
            MarkTweaksModified();
            RuntimeStateManager.FreeMapNavigationEnabled = val;
            ConfigManager.Current.PreRunTweaks.FreeMapNavigation = val;
            if (val)
            {
                GameHelper.EnsureCustomRunMode();
            }
            try
            {
                MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.RefreshAllPointVisuals();
            }
            catch { }
        };
        vbox.AddChild(_freeMapNavCheck);

        return scroll;
    }

    private void UpdateTweaksRunLockState(bool inRun)
    {
        if (_tweaksRunLockNoticeLabel != null)
        {
            _tweaksRunLockNoticeLabel.Visible = inRun;
        }

        bool allowPreRunEdits = !inRun;

        if (_mapRoomCountSpin != null) _mapRoomCountSpin.Editable = allowPreRunEdits;
        if (_goldSlider != null) _goldSlider.Editable = allowPreRunEdits;
        if (_shopDiscountSlider != null) _shopDiscountSlider.Editable = allowPreRunEdits;
        if (_cardRewardSpin != null) _cardRewardSpin.Editable = allowPreRunEdits;
        if (_bonusGoldSpin != null) _bonusGoldSpin.Editable = allowPreRunEdits;
        if (_bonusHpSpin != null) _bonusHpSpin.Editable = allowPreRunEdits;
        if (_forceNeowCheck != null) _forceNeowCheck.Disabled = !allowPreRunEdits;
        if (_eliteSlider != null) _eliteSlider.Editable = allowPreRunEdits;
        if (_shopSlider != null) _shopSlider.Editable = allowPreRunEdits;
        if (_eventSlider != null) _eventSlider.Editable = allowPreRunEdits;
        if (_restSlider != null) _restSlider.Editable = allowPreRunEdits;
        if (_combatSlider != null) _combatSlider.Editable = allowPreRunEdits;
    }

    private Control BuildCombatSandboxTab()
    {
        var scroll = new ScrollContainer { Name = "Combat Sandbox" };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        vbox.AddChild(new Label { Text = "--- Real-Time Combat Cheats ---", Modulate = new Color(1f, 0.4f, 0.4f) });

        _godModeCheck = new CheckBox { Text = " God Mode (Immune to all incoming damage)" };
        _godModeCheck.Toggled += val => { RuntimeStateManager.GodModeEnabled = val; ConfigManager.Current.CombatSandbox.GodMode = val; };
        vbox.AddChild(_godModeCheck);

        _infEnergyCheck = new CheckBox { Text = " Infinite Energy (Playing cards does not drain energy)" };
        _infEnergyCheck.Toggled += val => { RuntimeStateManager.InfiniteEnergyEnabled = val; ConfigManager.Current.CombatSandbox.InfiniteEnergy = val; };
        vbox.AddChild(_infEnergyCheck);

        _oneHitKillCheck = new CheckBox { Text = " 1-Hit Kill (Attacks deal lethal damage to enemies)" };
        _oneHitKillCheck.Toggled += val => { RuntimeStateManager.OneHitKillEnabled = val; ConfigManager.Current.CombatSandbox.OneHitKill = val; };
        vbox.AddChild(_oneHitKillCheck);

        _infPotionsCheck = new CheckBox { Text = " Infinite Potions (Using potions does not consume them)" };
        _infPotionsCheck.Toggled += val => ConfigManager.Current.CombatSandbox.InfinitePotions = val;
        vbox.AddChild(_infPotionsCheck);

        _noExhaustCheck = new CheckBox { Text = " No Card Exhaust (Exhausted cards are retained)" };
        _noExhaustCheck.Toggled += val => ConfigManager.Current.CombatSandbox.NoCardExhaust = val;
        vbox.AddChild(_noExhaustCheck);

        var drawRow = new HBoxContainer();
        drawRow.AddChild(new Label { Text = "Bonus Card Draw per Turn: ", CustomMinimumSize = new Vector2(240, 0) });
        _bonusDrawSpin = new SpinBox { MinValue = 0, MaxValue = 10, Value = 0 };
        _bonusDrawSpin.ValueChanged += val => ConfigManager.Current.CombatSandbox.BonusDrawPerTurn = (int)val;
        drawRow.AddChild(_bonusDrawSpin);
        vbox.AddChild(drawRow);

        vbox.AddChild(new HSeparator());
        vbox.AddChild(new Label { Text = "--- Immediate Combat Actions ---", Modulate = new Color(1f, 0.7f, 0.2f) });

        var actionHBox = new HBoxContainer();
        var killAllBtn = new Button { Text = " Kill All Enemies Now " };
        killAllBtn.Pressed += CombatDirector.KillAllEnemies;

        var endTurnBtn = new Button { Text = " Force End Turn " };
        endTurnBtn.Pressed += CombatDirector.EndTurn;

        var draw3Btn = new Button { Text = " Draw 3 Cards " };
        draw3Btn.Pressed += () => CombatDirector.DrawCards(3);

        var energy3Btn = new Button { Text = " +3 Energy " };
        energy3Btn.Pressed += () => CombatDirector.AddEnergy(3);

        actionHBox.AddChild(killAllBtn);
        actionHBox.AddChild(endTurnBtn);
        actionHBox.AddChild(draw3Btn);
        actionHBox.AddChild(energy3Btn);
        vbox.AddChild(actionHBox);

        return scroll;
    }

    private Control BuildRelicsTab()
    {
        var relicsRoot = new VBoxContainer { Name = "Relics", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        
        var subTabs = new TabContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        relicsRoot.AddChild(subTabs);

        subTabs.AddChild(BuildAvailableRelicsSubTab());
        subTabs.AddChild(BuildActiveRelicsSubTab());

        subTabs.TabChanged += (subTabIdx) => RefreshRealTimeRelicTabs();

        return relicsRoot;
    }

    private Control BuildAvailableRelicsSubTab()
    {
        var scroll = new ScrollContainer 
        { 
            Name = "Available Relics", 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        var vbox = new VBoxContainer 
        { 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Available Relics Compendium:", Modulate = new Color(0.85f, 0.55f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        
        var addAllBtn = new Button { Text = " Add All to Inventory " };
        addAllBtn.Pressed += () => 
        {
            var confirm = new ConfirmationDialog { Title = "Confirm Add All", DialogText = "Are you sure you want to add one of every available relic to your inventory?" };
            confirm.Confirmed += () => { RelicDirector.AddRelic("all"); RefreshRealTimeRelicTabs(); };
            AddChild(confirm);
            confirm.PopupCentered();
        };
        titleBox.AddChild(addAllBtn);
        vbox.AddChild(titleBox);

        var searchRow = new HBoxContainer();
        var searchInput = new LineEdit
        {
            PlaceholderText = "Search relics by name, ID, or description (e.g. 'Vajra', 'Anchor', 'Akabeko')...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        searchRow.AddChild(searchInput);
        vbox.AddChild(searchRow);

        var grid = new GridContainer 
        { 
            Columns = 4, 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        vbox.AddChild(grid);

        var allRelics = AIOTweaks.Core.GameHelper.GetAllRelicIds();
        _availableRelicEntries.Clear();

        foreach (var r in allRelics)
        {
            var canonical = GameHelper.FindCanonicalRelicModel(r);
            string fullTooltip = canonical != null ? GameHelper.GetRelicFullTooltip(canonical) : r;
            string relicTitle = !string.IsNullOrWhiteSpace(canonical?.Title.GetFormattedText()) ? canonical.Title.GetFormattedText() : r;
            Color rarityColor = GameHelper.GetRelicRarityColor(canonical);

            var panel = new PanelContainer 
            { 
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = fullTooltip
            };
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.12f, 0.18f, 0.95f),
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderColor = new Color(0.25f, 0.25f, 0.38f, 0.7f),
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 6);
            margin.AddThemeConstantOverride("margin_bottom", 6);
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            panel.AddChild(margin);

            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            margin.AddChild(row);

            // Relic Icon
            var tex = GameHelper.GetRelicIcon(canonical);
            if (tex != null)
            {
                var iconRect = new TextureRect
                {
                    Texture = tex,
                    CustomMinimumSize = new Vector2(44, 44),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    TextureFilter = CanvasItem.TextureFilterEnum.Linear
                };
                row.AddChild(iconRect);
            }

            var textVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            var lbl = new Label 
            { 
                Text = relicTitle, 
                CustomMinimumSize = new Vector2(110, 0), 
                ClipText = true, 
                Modulate = rarityColor,
                TooltipText = fullTooltip
            };
            textVBox.AddChild(lbl);

            var rarityLabel = new Label
            {
                Text = canonical != null ? canonical.Rarity.ToString() : "",
                Modulate = new Color(0.6f, 0.6f, 0.7f, 0.8f)
            };
            textVBox.AddChild(rarityLabel);
            row.AddChild(textVBox);

            var btnVBox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            btnVBox.AddThemeConstantOverride("separation", 2);

            var addBtn = new Button { Text = "+", TooltipText = "Add to player inventory", CustomMinimumSize = new Vector2(28, 22) };
            addBtn.Pressed += () => { RelicDirector.AddRelic(r); RefreshRealTimeRelicTabs(); };
            var rmBtn = new Button { Text = "-", TooltipText = "Remove from player inventory", CustomMinimumSize = new Vector2(28, 22) };
            rmBtn.Pressed += () => { RelicDirector.RemoveRelic(r); RefreshRealTimeRelicTabs(); };

            btnVBox.AddChild(addBtn);
            btnVBox.AddChild(rmBtn);
            row.AddChild(btnVBox);

            grid.AddChild(panel);
            _availableRelicEntries.Add(new ItemEntry(r, panel, lbl));
        }

        searchInput.TextChanged += query =>
        {
            string q = query.Trim();
            foreach (var entry in _availableRelicEntries)
            {
                bool matches = string.IsNullOrEmpty(q) || 
                               entry.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               (entry.Label != null && entry.Label.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                               (entry.Label != null && entry.Label.TooltipText.Contains(q, StringComparison.OrdinalIgnoreCase));
                entry.Container.Visible = matches;
            }
        };

        return scroll;
    }

    private Control BuildActiveRelicsSubTab()
    {
        var scroll = new ScrollContainer { Name = "Active Relics", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Equipped Relics:", Modulate = new Color(0.85f, 0.55f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        
        var rmAllBtn = new Button { Text = " Remove All Relics " };
        rmAllBtn.Pressed += () => 
        {
            var confirm = new ConfirmationDialog { Title = "Confirm Remove All Relics", DialogText = "Are you sure you want to remove all relics from your player?" };
            confirm.Confirmed += () => { RelicDirector.RemoveRelic("all"); RefreshRealTimeRelicTabs(); };
            AddChild(confirm);
            confirm.PopupCentered();
        };
        titleBox.AddChild(rmAllBtn);

        var refreshBtn = new Button { Text = " Refresh " };
        refreshBtn.Pressed += () => RefreshRealTimeRelicTabs();
        titleBox.AddChild(refreshBtn);
        vbox.AddChild(titleBox);

        _activeRelicsGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(_activeRelicsGrid);
        return scroll;
    }

    private void RefreshRealTimeRelicTabs()
    {
        if (_dialogPanel == null || !_dialogPanel.Visible) return;

        var player = GameHelper.GetActivePlayer();
        var playerRelics = GameHelper.GetPlayerRelics();

        // 1. Update Active Relics Grid
        if (_activeRelicsGrid != null)
        {
            _activeRelicsGrid.Columns = 4;
            _activeRelicsGrid.AddThemeConstantOverride("h_separation", 10);
            _activeRelicsGrid.AddThemeConstantOverride("v_separation", 10);

            foreach (Node child in _activeRelicsGrid.GetChildren())
            {
                _activeRelicsGrid.RemoveChild(child);
                child.QueueFree();
            }

            if (player == null)
            {
                var notice = new Label
                {
                    Text = "No active run detected.\nStart or resume a run to view equipped relics.",
                    Modulate = new Color(0.8f, 0.8f, 0.8f, 0.7f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CustomMinimumSize = new Vector2(400, 60),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                _activeRelicsGrid.AddChild(notice);
            }
            else if (playerRelics == null || playerRelics.Count == 0)
            {
                var emptyLabel = new Label
                {
                    Text = "You currently have no relics equipped.",
                    Modulate = new Color(0.7f, 0.7f, 0.7f, 0.7f),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CustomMinimumSize = new Vector2(400, 60),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
                };
                _activeRelicsGrid.AddChild(emptyLabel);
            }
            else
            {
                foreach (var relic in playerRelics)
                {
                    if (relic == null) continue;

                    string relicName = !string.IsNullOrWhiteSpace(relic.Title.GetFormattedText()) ? relic.Title.GetFormattedText() : relic.GetType().Name;
                    string fullTooltip = GameHelper.GetRelicFullTooltip(relic);
                    Color rarityColor = GameHelper.GetRelicRarityColor(relic);

                    var panel = new PanelContainer 
                    { 
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        TooltipText = fullTooltip
                    };
                    var style = new StyleBoxFlat
                    {
                        BgColor = new Color(0.14f, 0.13f, 0.22f, 0.95f),
                        BorderWidthBottom = 1,
                        BorderWidthLeft = 1,
                        BorderWidthRight = 1,
                        BorderWidthTop = 1,
                        BorderColor = rarityColor * 0.7f,
                        CornerRadiusTopLeft = 6,
                        CornerRadiusTopRight = 6,
                        CornerRadiusBottomLeft = 6,
                        CornerRadiusBottomRight = 6
                    };
                    panel.AddThemeStyleboxOverride("panel", style);

                    var margin = new MarginContainer();
                    margin.AddThemeConstantOverride("margin_top", 6);
                    margin.AddThemeConstantOverride("margin_bottom", 6);
                    margin.AddThemeConstantOverride("margin_left", 8);
                    margin.AddThemeConstantOverride("margin_right", 8);
                    panel.AddChild(margin);

                    var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
                    margin.AddChild(row);

                    var tex = GameHelper.GetRelicIcon(relic);
                    if (tex != null)
                    {
                        var iconRect = new TextureRect
                        {
                            Texture = tex,
                            CustomMinimumSize = new Vector2(46, 46),
                            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                            TextureFilter = CanvasItem.TextureFilterEnum.Linear
                        };
                        row.AddChild(iconRect);
                    }

                    var textVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
                    var nameLabel = new Label
                    {
                        Text = relicName,
                        CustomMinimumSize = new Vector2(110, 0),
                        ClipText = true,
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        Modulate = rarityColor,
                        TooltipText = fullTooltip
                    };
                    textVBox.AddChild(nameLabel);

                    string statusText = relic.ShowCounter ? $"Counter: {relic.DisplayAmount}" : (relic.StackCount > 1 ? $"x{relic.StackCount}" : relic.Rarity.ToString());
                    var subLabel = new Label
                    {
                        Text = statusText,
                        Modulate = new Color(0.7f, 0.75f, 0.85f, 0.85f)
                    };
                    textVBox.AddChild(subLabel);
                    row.AddChild(textVBox);

                    var rmBtn = new Button { Text = " Remove ", TooltipText = $"Remove {relicName} from equipped inventory" };
                    string relTypeName = relic.GetType().Name;
                    rmBtn.Pressed += () => 
                    {
                        ModLogger.Verbose("ModSettingsDialog", $"Remove relic clicked for '{relTypeName}'");
                        RelicDirector.RemoveRelic(relTypeName);
                        RefreshRealTimeRelicTabs();
                    };
                    row.AddChild(rmBtn);

                    _activeRelicsGrid.AddChild(panel);
                }
            }
            ModLogger.Verbose("ModSettingsDialog", $"RefreshRealTimeRelicTabs: Rendered {playerRelics?.Count ?? 0} active player relics.");
        }

        // 2. Update Available Relic counts
        if (playerRelics != null)
        {
            var relicCountMap = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in playerRelics)
            {
                if (r != null)
                {
                    string tName = r.GetType().Name;
                    relicCountMap[tName] = relicCountMap.TryGetValue(tName, out int c) ? c + 1 : 1;
                }
            }

            foreach (var entry in _availableRelicEntries)
            {
                if (entry.Label != null)
                {
                    var canonical = GameHelper.FindCanonicalRelicModel(entry.Id);
                    string baseTitle = !string.IsNullOrWhiteSpace(canonical?.Title.GetFormattedText()) ? canonical.Title.GetFormattedText() : entry.Id;
                    if (relicCountMap.TryGetValue(entry.Id, out int count) && count > 0)
                    {
                        entry.Label.Text = $"{baseTitle} (x{count})";
                        entry.Label.Modulate = new Color(0.8f, 1f, 0.6f);
                    }
                    else
                    {
                        entry.Label.Text = baseTitle;
                        entry.Label.Modulate = GameHelper.GetRelicRarityColor(canonical);
                    }
                }
            }
        }
    }

    private Control BuildCardsTab()
    {
        var cardsRoot = new VBoxContainer { Name = "Cards", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        
        var subTabs = new TabContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        cardsRoot.AddChild(subTabs);

        subTabs.AddChild(BuildAvailableCardsSubTab());
        subTabs.AddChild(BuildDeckSubTab());
        subTabs.AddChild(BuildHandSubTab());
        subTabs.AddChild(BuildDrawPileSubTab());
        subTabs.AddChild(BuildDiscardPileSubTab());
        subTabs.AddChild(BuildExhaustPileSubTab());

        subTabs.TabChanged += (subTabIdx) => RefreshRealTimeCardTabs();

        return cardsRoot;
    }

    private Control BuildAvailableCardsSubTab()
    {
        var scroll = new ScrollContainer 
        { 
            Name = "Available Cards", 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        var vbox = new VBoxContainer 
        { 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Available Cards:", Modulate = new Color(0.4f, 0.8f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        
        var addAllBtn = new Button { Text = " Add All to Deck " };
        addAllBtn.Pressed += () => 
        {
            var confirm = new ConfirmationDialog { Title = "Confirm Add All", DialogText = "Are you sure you want to add one of every card to your deck?" };
            confirm.Confirmed += () => { CardDirector.AddCardToDeck("all"); RefreshRealTimeCardTabs(); };
            AddChild(confirm);
            confirm.PopupCentered();
        };
        titleBox.AddChild(addAllBtn);
        vbox.AddChild(titleBox);

        // Filter Controls Row (Search Box + Multi-Select Pools, Types, and Rarities)
        var filterRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var searchInput = new LineEdit
        {
            PlaceholderText = "Search cards (e.g. 'Strike', 'Bash', 'DemonForm')...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        filterRow.AddChild(searchInput);

        // 1. Character / Pool Multi-Select Filter
        var poolFilterButton = new MenuButton 
        { 
            Text = "All Pools", 
            CustomMinimumSize = new Vector2(160, 0),
            Flat = false 
        };
        var poolPopup = poolFilterButton.GetPopup();
        poolPopup.HideOnCheckableItemSelection = false;

        var charPools = GameHelper.GetAvailableCharacterCardPools();
        var selectedPools = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        poolPopup.AddItem("Select All Pools", 0);
        poolPopup.AddItem("Clear All Pools", 1);
        poolPopup.AddSeparator();

        int poolItemOffset = 3;
        int pIdx = 0;
        foreach (var (poolId, displayName) in charPools)
        {
            poolPopup.AddCheckItem(displayName, poolItemOffset + pIdx);
            poolPopup.SetItemMetadata(poolItemOffset + pIdx, poolId);
            pIdx++;
        }

        // Auto-select current character's pool if currently in a run
        string? activePoolId = GameHelper.GetCurrentPlayerCharacterPoolId();
        if (!string.IsNullOrEmpty(activePoolId))
        {
            for (int i = poolItemOffset; i < poolPopup.ItemCount; i++)
            {
                if (poolPopup.GetItemMetadata(i).AsString() == activePoolId)
                {
                    poolPopup.SetItemChecked(i, true);
                    selectedPools.Add(activePoolId);
                    break;
                }
            }
        }

        void UpdatePoolButtonText()
        {
            if (selectedPools.Count == 0)
            {
                poolFilterButton.Text = "All Pools";
            }
            else if (selectedPools.Count == 1)
            {
                string cur = selectedPools.First();
                string name = cur;
                for (int i = poolItemOffset; i < poolPopup.ItemCount; i++)
                {
                    if (poolPopup.GetItemMetadata(i).AsString() == cur)
                    {
                        name = poolPopup.GetItemText(i);
                        break;
                    }
                }
                poolFilterButton.Text = name;
            }
            else
            {
                poolFilterButton.Text = $"{selectedPools.Count} Pools Selected";
            }
        }

        // 2. Card Type Multi-Select Filter
        var typeFilterButton = new MenuButton
        {
            Text = "All Types",
            CustomMinimumSize = new Vector2(140, 0),
            Flat = false
        };
        var typePopup = typeFilterButton.GetPopup();
        typePopup.HideOnCheckableItemSelection = false;

        var selectedTypes = new System.Collections.Generic.HashSet<MegaCrit.Sts2.Core.Entities.Cards.CardType>();
        typePopup.AddItem("Select All Types", 0);
        typePopup.AddItem("Clear All Types", 1);
        typePopup.AddSeparator();

        var cardTypes = new[] 
        { 
            MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack,
            MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill,
            MegaCrit.Sts2.Core.Entities.Cards.CardType.Power,
            MegaCrit.Sts2.Core.Entities.Cards.CardType.Status,
            MegaCrit.Sts2.Core.Entities.Cards.CardType.Curse
        };

        int typeItemOffset = 3;
        for (int i = 0; i < cardTypes.Length; i++)
        {
            typePopup.AddCheckItem(cardTypes[i].ToString(), typeItemOffset + i);
            typePopup.SetItemMetadata(typeItemOffset + i, (int)cardTypes[i]);
        }

        void UpdateTypeButtonText()
        {
            if (selectedTypes.Count == 0)
            {
                typeFilterButton.Text = "All Types";
            }
            else if (selectedTypes.Count == 1)
            {
                typeFilterButton.Text = selectedTypes.First().ToString();
            }
            else
            {
                typeFilterButton.Text = $"{selectedTypes.Count} Types Selected";
            }
        }

        // 3. Card Rarity Multi-Select Filter
        var rarityFilterButton = new MenuButton
        {
            Text = "All Rarities",
            CustomMinimumSize = new Vector2(140, 0),
            Flat = false
        };
        var rarityPopup = rarityFilterButton.GetPopup();
        rarityPopup.HideOnCheckableItemSelection = false;

        var selectedRarities = new System.Collections.Generic.HashSet<MegaCrit.Sts2.Core.Entities.Cards.CardRarity>();
        rarityPopup.AddItem("Select All Rarities", 0);
        rarityPopup.AddItem("Clear All Rarities", 1);
        rarityPopup.AddSeparator();

        var cardRarities = new[]
        {
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Basic,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Common,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Ancient,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Event,
            MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Curse
        };

        int rarityItemOffset = 3;
        for (int i = 0; i < cardRarities.Length; i++)
        {
            rarityPopup.AddCheckItem(cardRarities[i].ToString(), rarityItemOffset + i);
            rarityPopup.SetItemMetadata(rarityItemOffset + i, (int)cardRarities[i]);
        }

        void UpdateRarityButtonText()
        {
            if (selectedRarities.Count == 0)
            {
                rarityFilterButton.Text = "All Rarities";
            }
            else if (selectedRarities.Count == 1)
            {
                rarityFilterButton.Text = selectedRarities.First().ToString();
            }
            else
            {
                rarityFilterButton.Text = $"{selectedRarities.Count} Rarities Selected";
            }
        }

        UpdatePoolButtonText();
        UpdateTypeButtonText();
        UpdateRarityButtonText();

        filterRow.AddChild(poolFilterButton);
        filterRow.AddChild(typeFilterButton);
        filterRow.AddChild(rarityFilterButton);
        vbox.AddChild(filterRow);

        var grid = new GridContainer 
        { 
            Columns = 4, 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 15);
        grid.AddThemeConstantOverride("v_separation", 15);
        vbox.AddChild(grid);

        var allCards = AIOTweaks.Core.GameHelper.GetAllCardIds();
        var cardToPool = AIOTweaks.Core.GameHelper.GetCardPoolMapping();
        _availableCardEntries.Clear();

        foreach (var c in allCards)
        {
            var canonical = GameHelper.FindCanonicalCardModel(c);
            string fullTooltip = canonical != null ? GameHelper.GetCardFullTooltip(canonical) : c;

            var panel = new PanelContainer 
            { 
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = fullTooltip
            };
            var style = new StyleBoxFlat 
            { 
                BgColor = new Color(0.14f, 0.15f, 0.20f, 0.95f), 
                BorderColor = new Color(0.28f, 0.32f, 0.45f, 0.8f),
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 8, 
                CornerRadiusTopRight = 8, 
                CornerRadiusBottomLeft = 8, 
                CornerRadiusBottomRight = 8 
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            panel.AddChild(margin);

            var cardVbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            margin.AddChild(cardVbox);

            // 1. Portrait texture from card model
            Texture2D? tex = canonical?.Portrait;
            if (tex == null && !string.IsNullOrEmpty(canonical?.PortraitPath))
            {
                try { tex = GD.Load<Texture2D>(canonical.PortraitPath); } catch {}
            }
            if (tex == null)
            {
                var texPath = GameHelper.GetCardPortraitPath(c);
                if (!string.IsNullOrEmpty(texPath))
                {
                    try { tex = GD.Load<Texture2D>(texPath); } catch {}
                }
            }

            if (tex != null)
            {
                var texRect = new TextureRect 
                { 
                    Texture = tex, 
                    ExpandMode = TextureRect.ExpandModeEnum.FitWidth, 
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(100, 110),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    TooltipText = fullTooltip
                };
                cardVbox.AddChild(texRect);
            }

            // 2. Card Title & Count Label
            string cardTitle = !string.IsNullOrWhiteSpace(canonical?.Title) ? canonical.Title : c;
            var lbl = new Label 
            { 
                Text = cardTitle, 
                CustomMinimumSize = new Vector2(110, 24), 
                ClipText = true, 
                HorizontalAlignment = HorizontalAlignment.Center,
                TooltipText = fullTooltip
            };
            cardVbox.AddChild(lbl);

            // 3. Card Type / Rarity mini badge
            if (canonical != null)
            {
                string costText = canonical.EnergyCost?.CostsX == true ? "X" : (canonical.EnergyCost?.Canonical >= 0 ? canonical.EnergyCost.Canonical.ToString() : "-");
                var badgeLbl = new Label
                {
                    Text = $"[{costText}E | {canonical.Type}]",
                    CustomMinimumSize = new Vector2(110, 18),
                    ClipText = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Modulate = new Color(0.7f, 0.75f, 0.85f, 0.8f),
                    TooltipText = fullTooltip
                };
                cardVbox.AddChild(badgeLbl);
            }

            // 4. Action Buttons Grid
            var actionGrid = new GridContainer 
            { 
                Columns = 2, 
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter 
            };
            actionGrid.AddThemeConstantOverride("h_separation", 6);
            actionGrid.AddThemeConstantOverride("v_separation", 4);

            string cardId = c;
            var addBtn = new Button { Text = "+Deck", TooltipText = "Add to Master Deck (and active Draw Pile in combat)" };
            addBtn.Pressed += () => { CardDirector.AddCardToDeck(cardId); RefreshRealTimeCardTabs(); };
            var addUpBtn = new Button { Text = "+Deck(Up)", TooltipText = "Add Upgraded to Master Deck" };
            addUpBtn.Pressed += () => { CardDirector.AddCardToDeck(cardId, true); RefreshRealTimeCardTabs(); };
            var handBtn = new Button { Text = "+Hand", TooltipText = "Spawn directly into combat Hand" };
            handBtn.Pressed += () => { CardDirector.SpawnCardInHand(cardId); RefreshRealTimeCardTabs(); };
            var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
            enchBtn.Pressed += () => 
            {
                var inst = canonical != null ? GameHelper.CreateCardForPlayer(canonical) : null;
                if (inst != null) ShowEnchantmentPicker(inst);
            };

            actionGrid.AddChild(addBtn);
            actionGrid.AddChild(addUpBtn);
            actionGrid.AddChild(handBtn);
            actionGrid.AddChild(enchBtn);
            cardVbox.AddChild(actionGrid);

            grid.AddChild(panel);
            string poolId = cardToPool.TryGetValue(c, out var pId) ? pId : "";
            var cardType = canonical?.Type ?? MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack;
            var cardRarity = canonical?.Rarity ?? MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Common;
            var entry = new ItemEntry(c, panel, lbl, poolId, cardType, cardRarity);
            _availableCardEntries.Add(entry);
        }

        Action applyFilter = () =>
        {
            string q = searchInput.Text.Trim();

            foreach (var entry in _availableCardEntries)
            {
                bool matchesSearch = string.IsNullOrEmpty(q) || 
                                     entry.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                     (entry.Label != null && entry.Label.TooltipText.Contains(q, StringComparison.OrdinalIgnoreCase));
                bool matchesPool = selectedPools.Count == 0 || (!string.IsNullOrEmpty(entry.PoolId) && selectedPools.Contains(entry.PoolId));
                bool matchesType = selectedTypes.Count == 0 || selectedTypes.Contains(entry.CardType);
                bool matchesRarity = selectedRarities.Count == 0 || selectedRarities.Contains(entry.CardRarity);

                entry.Container.Visible = matchesSearch && matchesPool && matchesType && matchesRarity;
            }
        };

        // Pool Popup Selection Handlers
        poolPopup.IndexPressed += (long index) =>
        {
            int idx = (int)index;
            if (idx == 0) // Select All
            {
                for (int i = poolItemOffset; i < poolPopup.ItemCount; i++)
                {
                    poolPopup.SetItemChecked(i, true);
                    selectedPools.Add(poolPopup.GetItemMetadata(i).AsString());
                }
            }
            else if (idx == 1) // Clear All
            {
                for (int i = poolItemOffset; i < poolPopup.ItemCount; i++)
                {
                    poolPopup.SetItemChecked(i, false);
                }
                selectedPools.Clear();
            }
            else if (idx >= poolItemOffset)
            {
                bool isChecked = !poolPopup.IsItemChecked(idx);
                poolPopup.SetItemChecked(idx, isChecked);
                string poolId = poolPopup.GetItemMetadata(idx).AsString();
                if (isChecked) selectedPools.Add(poolId);
                else selectedPools.Remove(poolId);
            }
            UpdatePoolButtonText();
            applyFilter();
        };

        // Type Popup Selection Handlers
        typePopup.IndexPressed += (long index) =>
        {
            int idx = (int)index;
            if (idx == 0) // Select All
            {
                for (int i = typeItemOffset; i < typePopup.ItemCount; i++)
                {
                    typePopup.SetItemChecked(i, true);
                    selectedTypes.Add((MegaCrit.Sts2.Core.Entities.Cards.CardType)typePopup.GetItemMetadata(i).AsInt32());
                }
            }
            else if (idx == 1) // Clear All
            {
                for (int i = typeItemOffset; i < typePopup.ItemCount; i++)
                {
                    typePopup.SetItemChecked(i, false);
                }
                selectedTypes.Clear();
            }
            else if (idx >= typeItemOffset)
            {
                bool isChecked = !typePopup.IsItemChecked(idx);
                typePopup.SetItemChecked(idx, isChecked);
                var cType = (MegaCrit.Sts2.Core.Entities.Cards.CardType)typePopup.GetItemMetadata(idx).AsInt32();
                if (isChecked) selectedTypes.Add(cType);
                else selectedTypes.Remove(cType);
            }
            UpdateTypeButtonText();
            applyFilter();
        };

        // Rarity Popup Selection Handlers
        rarityPopup.IndexPressed += (long index) =>
        {
            int idx = (int)index;
            if (idx == 0) // Select All
            {
                for (int i = rarityItemOffset; i < rarityPopup.ItemCount; i++)
                {
                    rarityPopup.SetItemChecked(i, true);
                    selectedRarities.Add((MegaCrit.Sts2.Core.Entities.Cards.CardRarity)rarityPopup.GetItemMetadata(i).AsInt32());
                }
            }
            else if (idx == 1) // Clear All
            {
                for (int i = rarityItemOffset; i < rarityPopup.ItemCount; i++)
                {
                    rarityPopup.SetItemChecked(i, false);
                }
                selectedRarities.Clear();
            }
            else if (idx >= rarityItemOffset)
            {
                bool isChecked = !rarityPopup.IsItemChecked(idx);
                rarityPopup.SetItemChecked(idx, isChecked);
                var cRarity = (MegaCrit.Sts2.Core.Entities.Cards.CardRarity)rarityPopup.GetItemMetadata(idx).AsInt32();
                if (isChecked) selectedRarities.Add(cRarity);
                else selectedRarities.Remove(cRarity);
            }
            UpdateRarityButtonText();
            applyFilter();
        };

        searchInput.TextChanged += _ => applyFilter();
        applyFilter();

        return scroll;
    }

    private Control BuildDeckSubTab()
    {
        var scroll = new ScrollContainer { Name = "Deck", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Current Deck:", Modulate = new Color(0.4f, 1f, 0.4f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        
        var rmAllBtn = new Button { Text = " Remove All " };
        rmAllBtn.Pressed += () => 
        {
            var confirm = new ConfirmationDialog { Title = "Confirm Remove All Cards", DialogText = "Are you sure you want to remove all cards from your master deck?" };
            confirm.Confirmed += () => { CardDirector.RemoveCardFromDeck("all"); RefreshRealTimeCardTabs(); };
            AddChild(confirm);
            confirm.PopupCentered();
        };
        titleBox.AddChild(rmAllBtn);

        var refreshBtn = new Button { Text = " Refresh " };
        refreshBtn.Pressed += () => RefreshRealTimeCardTabs();
        titleBox.AddChild(refreshBtn);
        vbox.AddChild(titleBox);

        _deckGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(_deckGrid);
        return scroll;
    }

    private Control BuildHandSubTab()
    {
        var scroll = new ScrollContainer { Name = "Hand", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Combat Hand:", Modulate = new Color(0.4f, 0.9f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refreshBtn = new Button { Text = " Refresh " };
        refreshBtn.Pressed += () => RefreshRealTimeCardTabs();
        titleBox.AddChild(refreshBtn);
        vbox.AddChild(titleBox);

        _handGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(_handGrid);
        return scroll;
    }

    private Control BuildDrawPileSubTab()
    {
        var scroll = new ScrollContainer { Name = "Draw Pile", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Draw Pile:", Modulate = new Color(1f, 0.9f, 0.4f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refreshBtn = new Button { Text = " Refresh " };
        refreshBtn.Pressed += () => RefreshRealTimeCardTabs();
        titleBox.AddChild(refreshBtn);
        vbox.AddChild(titleBox);

        _drawGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(_drawGrid);
        return scroll;
    }

    private Control BuildDiscardPileSubTab()
    {
        var scroll = new ScrollContainer { Name = "Discard Pile", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Discard Pile:", Modulate = new Color(1f, 0.4f, 0.4f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refreshBtn = new Button { Text = " Refresh " };
        refreshBtn.Pressed += () => RefreshRealTimeCardTabs();
        titleBox.AddChild(refreshBtn);
        vbox.AddChild(titleBox);

        _discardGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(_discardGrid);
        return scroll;
    }

    private Control BuildExhaustPileSubTab()
    {
        var scroll = new ScrollContainer { Name = "Exhaust Pile", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        var titleBox = new HBoxContainer();
        titleBox.AddChild(new Label { Text = "Exhaust Pile:", Modulate = new Color(0.85f, 0.5f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var refreshBtn = new Button { Text = " Refresh " };
        refreshBtn.Pressed += () => RefreshRealTimeCardTabs();
        titleBox.AddChild(refreshBtn);
        vbox.AddChild(titleBox);

        _exhaustGrid = new GridContainer { Columns = 4, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vbox.AddChild(_exhaustGrid);
        return scroll;
    }

    private void RefreshRealTimeCardTabs()
    {
        if (_dialogPanel == null || !_dialogPanel.Visible) return;
        
        var deckCards = GameHelper.GetPlayerDeckCards();
        var handCards = GameHelper.GetPlayerHandCards();
        var drawCards = GameHelper.GetPlayerDrawPileCards();
        var discardCards = GameHelper.GetPlayerDiscardPileCards();
        var exhaustCards = GameHelper.GetPlayerExhaustPileCards();

        RefreshGrid(_deckGrid, deckCards, true, "deck");
        RefreshGrid(_handGrid, handCards, false, "hand");
        RefreshGrid(_drawGrid, drawCards, false, "draw");
        RefreshGrid(_discardGrid, discardCards, false, "discard");
        RefreshGrid(_exhaustGrid, exhaustCards, false, "exhaust");
        
        bool inCombat = GameHelper.IsInCombat();

        foreach (var entry in _availableCardEntries)
        {
            if (entry.Label != null)
            {
                int deckCount = 0;
                if (deckCards != null)
                {
                    foreach (var card in deckCards)
                    {
                        if (card != null && card.GetType().Name == entry.Id) deckCount++;
                    }
                }

                int handCount = 0;
                if (inCombat && handCards != null)
                {
                    foreach (var card in handCards)
                    {
                        if (card != null && card.GetType().Name == entry.Id) handCount++;
                    }
                }

                var canonical = GameHelper.FindCanonicalCardModel(entry.Id);
                string baseTitle = !string.IsNullOrWhiteSpace(canonical?.Title) ? canonical.Title : entry.Id;
                string displayText = baseTitle;

                if (deckCount > 0)
                {
                    displayText += $" (x{deckCount})";
                }
                if (handCount > 0)
                {
                    displayText += handCount > 1 ? $" (H x{handCount})" : " (H)";
                }

                entry.Label.Text = displayText;
                if (handCount > 0)
                {
                    entry.Label.Modulate = new Color(0.4f, 1f, 0.9f);
                }
                else if (deckCount > 0)
                {
                    entry.Label.Modulate = new Color(0.8f, 1f, 0.6f);
                }
                else
                {
                    entry.Label.Modulate = new Color(1f, 1f, 1f);
                }
            }
        }
    }

    private void RefreshGrid(GridContainer? grid, System.Collections.Generic.IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel>? cards, bool isDeck, string pileType)
    {
        if (grid == null) return;
        
        // Fancier grid styling
        grid.AddThemeConstantOverride("h_separation", 15);
        grid.AddThemeConstantOverride("v_separation", 15);
        
        // Remove existing children
        foreach (Node child in grid.GetChildren())
        {
            grid.RemoveChild(child);
            child.QueueFree();
        }

        var player = GameHelper.GetActivePlayer();
        ModLogger.Info($"RefreshGrid for {pileType}: cardsCount={cards?.Count ?? 0}, playerFound={player != null}, inCombat={GameHelper.IsInCombat()}");

        if (player == null)
        {
            var noticeLabel = new Label
            {
                Text = "No active run detected.\nStart or resume a run to view your cards.",
                Modulate = new Color(0.8f, 0.8f, 0.8f, 0.7f),
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(400, 80),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            grid.AddChild(noticeLabel);
            return;
        }

        if (!isDeck && !GameHelper.IsInCombat())
        {
            var combatNotice = new Label
            {
                Text = "Combat piles are inactive outside of battle.\nEnter a combat encounter to view and manage cards in this pile.",
                Modulate = new Color(1f, 0.8f, 0.4f, 0.8f),
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(450, 80),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            grid.AddChild(combatNotice);
            return;
        }

        if (cards == null || cards.Count == 0)
        {
            string emptyMsg = isDeck ? "Your master deck is currently empty." : $"Your {pileType} pile is currently empty.";
            var emptyLabel = new Label
            {
                Text = emptyMsg,
                Modulate = new Color(0.7f, 0.7f, 0.7f, 0.7f),
                HorizontalAlignment = HorizontalAlignment.Center,
                CustomMinimumSize = new Vector2(400, 60),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            grid.AddChild(emptyLabel);
            return;
        }

        var handCards = GameHelper.GetPlayerHandCards();
        bool inCombat = GameHelper.IsInCombat();

        foreach (var c in cards)
        {
            if (c == null) continue;

            string fullTooltip = GameHelper.GetCardFullTooltip(c);

            var panel = new PanelContainer 
            { 
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = fullTooltip
            };
            var style = new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.2f, 0.9f), CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8 };
            panel.AddThemeStyleboxOverride("panel", style);
            
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            margin.AddThemeConstantOverride("margin_left", 8);
            margin.AddThemeConstantOverride("margin_right", 8);
            panel.AddChild(margin);

            var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            margin.AddChild(vbox);
            
            // 1. Direct portrait texture from card model
            Texture2D? tex = c.Portrait;
            if (tex == null && !string.IsNullOrEmpty(c.PortraitPath))
            {
                try { tex = GD.Load<Texture2D>(c.PortraitPath); } catch {}
            }
            if (tex == null)
            {
                var texPath = GameHelper.GetCardPortraitPath(c.GetType().Name);
                if (!string.IsNullOrEmpty(texPath))
                {
                    try { tex = GD.Load<Texture2D>(texPath); } catch {}
                }
            }
            
            if (tex != null)
            {
                var texRect = new TextureRect { 
                    Texture = tex, 
                    ExpandMode = TextureRect.ExpandModeEnum.FitWidth, 
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(100, 110),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
                    TooltipText = fullTooltip
                };
                vbox.AddChild(texRect);
            }

            // 2. Determine hand presence indicator (H)
            bool isInHand = false;
            if (inCombat && handCards != null)
            {
                if (pileType == "hand" || c.Pile == player.PlayerCombatState?.Hand)
                {
                    isInHand = true;
                }
                else if (isDeck)
                {
                    isInHand = handCards.Any(h => h != null && (
                        h == c || 
                        h.DeckVersion == c || 
                        h.CloneOf == c || 
                        (h.GetType() == c.GetType() && h.IsUpgraded == c.IsUpgraded)
                    ));
                }
            }

            // 3. Card Title & Upgrade / Hand State
            string cardTitle = !string.IsNullOrWhiteSpace(c.Title) ? c.Title : c.GetType().Name;
            if (c.IsUpgraded && !cardTitle.Contains('+'))
            {
                cardTitle += " (+)";
            }
            if (isInHand)
            {
                cardTitle += " (H)";
            }

            Color titleColor;
            if (isInHand)
            {
                titleColor = new Color(0.4f, 1f, 0.9f); // Cyan highlight for on-hand
            }
            else if (c.IsUpgraded)
            {
                titleColor = new Color(0.4f, 1f, 0.5f); // Green for upgraded
            }
            else
            {
                titleColor = new Color(1f, 1f, 1f);
            }

            var lbl = new Label { 
                Text = cardTitle, 
                CustomMinimumSize = new Vector2(110, 24), 
                ClipText = true, 
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = titleColor,
                TooltipText = fullTooltip
            };
            vbox.AddChild(lbl);

            // 4. Enchantment Badge (if enchanted)
            if (c.Enchantment != null)
            {
                string enchName = !string.IsNullOrWhiteSpace(c.Enchantment.Title?.GetFormattedText()) 
                    ? c.Enchantment.Title.GetFormattedText() 
                    : c.Enchantment.GetType().Name;
                string enchText = c.Enchantment.Amount > 1 
                    ? $"{enchName} (x{c.Enchantment.Amount})" 
                    : $"{enchName}";

                var enchBadge = new Label
                {
                    Text = enchText,
                    CustomMinimumSize = new Vector2(110, 20),
                    ClipText = true,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Modulate = new Color(0.9f, 0.65f, 1f), // Lilac / magical purple
                    TooltipText = fullTooltip
                };
                vbox.AddChild(enchBadge);
            }

            // 5. Action Buttons Grid (2 columns for compact & clean alignment)
            var actionGrid = new GridContainer 
            { 
                Columns = 2, 
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter 
            };
            actionGrid.AddThemeConstantOverride("h_separation", 6);
            actionGrid.AddThemeConstantOverride("v_separation", 4);

            var targetCard = c;

            if (isDeck)
            {
                if (inCombat)
                {
                    var handBtn = new Button { Text = "+Hand", TooltipText = "Spawn copy in Hand" };
                    handBtn.Pressed += () => { CardDirector.SpawnCardInHand(targetCard.GetType().Name); RefreshRealTimeCardTabs(); };
                    var exhaustBtn = new Button { Text = "Exhaust", TooltipText = "Force Exhaust from active combat" };
                    exhaustBtn.Pressed += () => { CardDirector.ExhaustCard(targetCard); RefreshRealTimeCardTabs(); };
                    actionGrid.AddChild(handBtn);
                    actionGrid.AddChild(exhaustBtn);
                }

                var upBtn = new Button { Text = targetCard.IsUpgraded ? "Downgrade" : "Upgrade", TooltipText = "Toggle Upgrade" };
                upBtn.Pressed += () => { CardDirector.ToggleUpgradeCard(targetCard); RefreshRealTimeCardTabs(); };
                var rmBtn = new Button { Text = "Remove", TooltipText = "Remove from Deck" };
                rmBtn.Pressed += () => { CardDirector.RemoveCard(targetCard); RefreshRealTimeCardTabs(); };
                actionGrid.AddChild(upBtn);
                actionGrid.AddChild(rmBtn);

                var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
                enchBtn.Pressed += () => ShowEnchantmentPicker(targetCard);
                actionGrid.AddChild(enchBtn);

                if (targetCard.Enchantment != null)
                {
                    var disBtn = new Button { Text = "Disenchant", TooltipText = "Remove enchantment from card" };
                    disBtn.Pressed += () => { CardDirector.ClearEnchantment(targetCard); RefreshRealTimeCardTabs(); };
                    actionGrid.AddChild(disBtn);
                }
            }
            else if (pileType == "hand")
            {
                var exhaustBtn = new Button { Text = "Exhaust", TooltipText = "Force Exhaust card to Exhaust Pile" };
                exhaustBtn.Pressed += () => { CardDirector.ExhaustCard(targetCard); RefreshRealTimeCardTabs(); };
                var rmBtn = new Button { Text = "Remove", TooltipText = "Remove from combat & run" };
                rmBtn.Pressed += () => { CardDirector.RemoveCard(targetCard); RefreshRealTimeCardTabs(); };
                actionGrid.AddChild(exhaustBtn);
                actionGrid.AddChild(rmBtn);

                var upBtn = new Button { Text = targetCard.IsUpgraded ? "Downgrade" : "Upgrade", TooltipText = "Toggle Upgrade" };
                upBtn.Pressed += () => { CardDirector.ToggleUpgradeCard(targetCard); RefreshRealTimeCardTabs(); };
                var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
                enchBtn.Pressed += () => ShowEnchantmentPicker(targetCard);
                actionGrid.AddChild(upBtn);
                actionGrid.AddChild(enchBtn);

                if (targetCard.Enchantment != null)
                {
                    var disBtn = new Button { Text = "Disenchant", TooltipText = "Remove enchantment from card" };
                    disBtn.Pressed += () => { CardDirector.ClearEnchantment(targetCard); RefreshRealTimeCardTabs(); };
                    actionGrid.AddChild(disBtn);
                }
            }
            else if (pileType == "draw" || pileType == "discard")
            {
                var handBtn = new Button { Text = "Draw", TooltipText = "Draw directly into Hand" };
                handBtn.Pressed += () => { CardDirector.DrawCardToHand(targetCard); RefreshRealTimeCardTabs(); };
                var exhaustBtn = new Button { Text = "Exhaust", TooltipText = "Force Exhaust to Exhaust Pile" };
                exhaustBtn.Pressed += () => { CardDirector.ExhaustCard(targetCard); RefreshRealTimeCardTabs(); };
                actionGrid.AddChild(handBtn);
                actionGrid.AddChild(exhaustBtn);

                var upBtn = new Button { Text = targetCard.IsUpgraded ? "Downgrade" : "Upgrade", TooltipText = "Toggle Upgrade" };
                upBtn.Pressed += () => { CardDirector.ToggleUpgradeCard(targetCard); RefreshRealTimeCardTabs(); };
                var rmBtn = new Button { Text = "Remove", TooltipText = "Remove from combat & run" };
                rmBtn.Pressed += () => { CardDirector.RemoveCard(targetCard); RefreshRealTimeCardTabs(); };
                actionGrid.AddChild(upBtn);
                actionGrid.AddChild(rmBtn);

                var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
                enchBtn.Pressed += () => ShowEnchantmentPicker(targetCard);
                actionGrid.AddChild(enchBtn);

                if (targetCard.Enchantment != null)
                {
                    var disBtn = new Button { Text = "Disenchant", TooltipText = "Remove enchantment from card" };
                    disBtn.Pressed += () => { CardDirector.ClearEnchantment(targetCard); RefreshRealTimeCardTabs(); };
                    actionGrid.AddChild(disBtn);
                }
            }
            else if (pileType == "exhaust")
            {
                var handBtn = new Button { Text = "Draw to Hand", TooltipText = "Recover exhausted card directly into Hand" };
                handBtn.Pressed += () => { CardDirector.DrawCardToHand(targetCard); RefreshRealTimeCardTabs(); };
                var rmBtn = new Button { Text = "Remove", TooltipText = "Remove from combat & run" };
                rmBtn.Pressed += () => { CardDirector.RemoveCard(targetCard); RefreshRealTimeCardTabs(); };
                actionGrid.AddChild(handBtn);
                actionGrid.AddChild(rmBtn);

                var upBtn = new Button { Text = targetCard.IsUpgraded ? "Downgrade" : "Upgrade", TooltipText = "Toggle Upgrade" };
                upBtn.Pressed += () => { CardDirector.ToggleUpgradeCard(targetCard); RefreshRealTimeCardTabs(); };
                var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
                enchBtn.Pressed += () => ShowEnchantmentPicker(targetCard);
                actionGrid.AddChild(upBtn);
                actionGrid.AddChild(enchBtn);

                if (targetCard.Enchantment != null)
                {
                    var disBtn = new Button { Text = "Disenchant", TooltipText = "Remove enchantment from card" };
                    disBtn.Pressed += () => { CardDirector.ClearEnchantment(targetCard); RefreshRealTimeCardTabs(); };
                    actionGrid.AddChild(disBtn);
                }
            }

            vbox.AddChild(actionGrid);
            grid.AddChild(panel);
        }
    }

    private void ShowEnchantmentPicker(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        var dialog = new ConfirmationDialog
        {
            Title = $"Enchant: {(!string.IsNullOrWhiteSpace(card.Title) ? card.Title : card.GetType().Name)}",
            Size = new Vector2I(380, 220)
        };

        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        dialog.AddChild(vbox);

        vbox.AddChild(new Label { Text = "Choose Enchantment:", Modulate = new Color(0.9f, 0.7f, 1f) });

        var enchDropdown = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var allEnchantments = GameHelper.GetAllEnchantmentIds();
        
        int selectedIdx = 0;
        for (int i = 0; i < allEnchantments.Count; i++)
        {
            var eId = allEnchantments[i];
            var model = GameHelper.FindCanonicalEnchantmentModel(eId);
            string title = model?.Title?.GetFormattedText() ?? "";
            string displayName = !string.IsNullOrWhiteSpace(title) ? $"{title} ({eId})" : eId;
            enchDropdown.AddItem(displayName, i);
            enchDropdown.SetItemMetadata(i, eId);

            if (card.Enchantment != null && card.Enchantment.GetType().Name.Equals(eId, StringComparison.OrdinalIgnoreCase))
            {
                selectedIdx = i;
            }
        }
        if (enchDropdown.ItemCount > 0)
        {
            enchDropdown.Selected = selectedIdx;
        }
        vbox.AddChild(enchDropdown);

        var amountRow = new HBoxContainer();
        amountRow.AddChild(new Label { Text = "Amount / Multiplier: ", CustomMinimumSize = new Vector2(140, 0) });
        var amountSpin = new SpinBox
        {
            MinValue = 1,
            MaxValue = 999,
            Step = 1,
            Value = card.Enchantment?.Amount > 0 ? card.Enchantment.Amount : 1,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        amountRow.AddChild(amountSpin);
        vbox.AddChild(amountRow);

        var descLabel = new Label
        {
            Text = "",
            Modulate = new Color(0.8f, 0.8f, 0.8f, 0.85f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 40)
        };
        vbox.AddChild(descLabel);

        Action updateDesc = () =>
        {
            int idx = enchDropdown.Selected;
            if (idx >= 0 && idx < enchDropdown.ItemCount)
            {
                string eId = enchDropdown.GetItemMetadata(idx).AsString();
                var model = GameHelper.FindCanonicalEnchantmentModel(eId);
                string desc = model?.DynamicDescription?.GetFormattedText() ?? model?.DynamicExtraCardText?.GetFormattedText() ?? "";
                descLabel.Text = !string.IsNullOrWhiteSpace(desc) ? desc : "No description available.";
            }
        };

        enchDropdown.ItemSelected += _ => updateDesc();
        updateDesc();

        dialog.Confirmed += () =>
        {
            int idx = enchDropdown.Selected;
            if (idx >= 0 && idx < enchDropdown.ItemCount)
            {
                string eId = enchDropdown.GetItemMetadata(idx).AsString();
                decimal amt = (decimal)amountSpin.Value;
                CardDirector.EnchantCard(card, eId, amt);
                RefreshRealTimeCardTabs();
            }
        };

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private sealed class ItemEntry
    {
        public string Id { get; }
        public Control Container { get; }
        public Label? Label { get; }
        public string PoolId { get; }
        public MegaCrit.Sts2.Core.Entities.Cards.CardType CardType { get; }
        public MegaCrit.Sts2.Core.Entities.Cards.CardRarity CardRarity { get; }

        public ItemEntry(string id, Control container, Label? label = null, string poolId = "", MegaCrit.Sts2.Core.Entities.Cards.CardType type = MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack, MegaCrit.Sts2.Core.Entities.Cards.CardRarity rarity = MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Common)
        {
            Id = id;
            Container = container;
            Label = label;
            PoolId = poolId;
            CardType = type;
            CardRarity = rarity;
        }
    }

    private Control BuildPlayerTab()
    {
        var scroll = new ScrollContainer 
        { 
            Name = "Player & Events", 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        var vbox = new VBoxContainer 
        { 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        scroll.AddChild(vbox);

        var selectedChar = GameHelper.GetSelectedCharacterModel();
        var activePlayer = GameHelper.GetActivePlayer();

        int defaultGold = activePlayer != null ? activePlayer.Gold : (selectedChar != null ? selectedChar.StartingGold : 99);
        int defaultMaxHp = (activePlayer?.Creature != null) ? (int)activePlayer.Creature.MaxHp : (selectedChar != null ? selectedChar.StartingHp : 80);
        int defaultCurrentHp = (activePlayer?.Creature != null) ? (int)activePlayer.Creature.CurrentHp : defaultMaxHp;

        vbox.AddChild(new Label { Text = "--- Gold & Health Manipulation ---", Modulate = new Color(0.3f, 1f, 0.5f) });
        var goldRow = new HBoxContainer();
        _goldAmountSpin = new SpinBox { MinValue = 0, MaxValue = 99999, Step = 50, Value = Math.Max(0, defaultGold) };
        var addGoldBtn = new Button { Text = " Add Gold " };
        addGoldBtn.Pressed += () => InventoryDirector.AddGold((int)_goldAmountSpin.Value);
        var setGoldBtn = new Button { Text = " Set Exact Gold " };
        setGoldBtn.Pressed += () => InventoryDirector.SetGold((int)_goldAmountSpin.Value);
        goldRow.AddChild(new Label { Text = "Gold Amount: ", CustomMinimumSize = new Vector2(120, 0) });
        goldRow.AddChild(_goldAmountSpin);
        goldRow.AddChild(addGoldBtn);
        goldRow.AddChild(setGoldBtn);
        vbox.AddChild(goldRow);

        var healRow = new HBoxContainer();
        _currentHpAmountSpin = new SpinBox { MinValue = 1, MaxValue = 999, Step = 10, Value = defaultCurrentHp };
        var healBtn = new Button { Text = " Heal Player " };
        healBtn.Pressed += () => InventoryDirector.Heal((int)_currentHpAmountSpin.Value);
        healRow.AddChild(new Label { Text = "Heal Amount: ", CustomMinimumSize = new Vector2(120, 0) });
        healRow.AddChild(_currentHpAmountSpin);
        healRow.AddChild(healBtn);
        vbox.AddChild(healRow);

        var damageRow = new HBoxContainer();
        _damageAmountSpin = new SpinBox { MinValue = 1, MaxValue = 999, Step = 5, Value = 5 };
        var damageBtn = new Button { Text = " Damage Player " };
        damageBtn.Pressed += () => InventoryDirector.DamagePlayer((int)_damageAmountSpin.Value);
        damageRow.AddChild(new Label { Text = "Damage Amount: ", CustomMinimumSize = new Vector2(120, 0) });
        damageRow.AddChild(_damageAmountSpin);
        damageRow.AddChild(damageBtn);
        vbox.AddChild(damageRow);

        var maxHpRow = new HBoxContainer();
        _maxHpAmountSpin = new SpinBox { MinValue = 1, MaxValue = 999, Step = 10, Value = defaultMaxHp };
        var maxHpBtn = new Button { Text = " Set Max HP " };
        maxHpBtn.Pressed += () => InventoryDirector.SetMaxHp((int)_maxHpAmountSpin.Value);
        maxHpRow.AddChild(new Label { Text = "Max HP Amount: ", CustomMinimumSize = new Vector2(120, 0) });
        maxHpRow.AddChild(_maxHpAmountSpin);
        maxHpRow.AddChild(maxHpBtn);
        vbox.AddChild(maxHpRow);

        vbox.AddChild(new HSeparator());
        vbox.AddChild(new Label { Text = "--- Event Director ---", Modulate = new Color(1f, 0.9f, 0.4f) });
        
        var indicatorRow = new HBoxContainer();
        indicatorRow.AddChild(new Label { Text = "Current Override: " });
        _eventOverrideLabel = new Label { Text = "None", Modulate = new Color(1f, 0.5f, 0.5f) };
        indicatorRow.AddChild(_eventOverrideLabel);
        
        var clearEventBtn = new Button { Text = " Clear Override " };
        clearEventBtn.Pressed += EventDirector.ClearForcedEvent;
        indicatorRow.AddChild(clearEventBtn);
        vbox.AddChild(indicatorRow);

        EventDirector.OnForcedEventChanged += (eventId) => 
        {
            if (_eventOverrideLabel != null && GodotObject.IsInstanceValid(_eventOverrideLabel))
            {
                if (string.IsNullOrEmpty(eventId))
                {
                    _eventOverrideLabel.Text = "None";
                }
                else
                {
                    var info = GameHelper.GetAllEventInfos().FirstOrDefault(e => e.Id.Equals(eventId, StringComparison.OrdinalIgnoreCase));
                    _eventOverrideLabel.Text = info != null ? $"{info.DisplayName} [{info.Id}]" : eventId;
                }
            }
        };

        var searchRow = new HBoxContainer();
        var searchInput = new LineEdit
        {
            PlaceholderText = "Search events by name, ID, or type...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        searchRow.AddChild(searchInput);
        vbox.AddChild(searchRow);

        var grid = new GridContainer 
        { 
            Columns = 3, 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        vbox.AddChild(grid);

        var allEvents = GameHelper.GetAllEventInfos();
        var eventEntries = new System.Collections.Generic.List<(GameHelper.EventInfo Info, Control Container)>();

        foreach (var info in allEvents)
        {
            string fullTooltip = GameHelper.GetEventFullTooltip(info);

            var panel = new PanelContainer 
            { 
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = fullTooltip
            };
            var borderCol = info.IsAncient ? new Color(1f, 0.75f, 0.3f, 0.8f) : new Color(0.3f, 0.55f, 0.85f, 0.6f);
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.12f, 0.18f, 0.95f),
                BorderWidthBottom = 1,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderColor = borderCol,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6
            };
            panel.AddThemeStyleboxOverride("panel", style);

            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_top", 8);
            margin.AddThemeConstantOverride("margin_bottom", 8);
            margin.AddThemeConstantOverride("margin_left", 10);
            margin.AddThemeConstantOverride("margin_right", 10);
            panel.AddChild(margin);

            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
            margin.AddChild(row);

            var infoVBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            
            var badgeRow = new HBoxContainer();
            var badge = new Label
            {
                Text = info.IsAncient ? "[Ancient Event]" : "[Standard Event]",
                Modulate = info.IsAncient ? new Color(1f, 0.8f, 0.35f) : new Color(0.4f, 0.85f, 1f)
            };
            badgeRow.AddChild(badge);
            infoVBox.AddChild(badgeRow);

            var titleLbl = new Label
            {
                Text = info.DisplayName,
                CustomMinimumSize = new Vector2(160, 0),
                ClipText = true,
                Modulate = info.IsAncient ? new Color(1f, 0.95f, 0.7f) : new Color(1f, 1f, 1f),
                TooltipText = fullTooltip
            };
            infoVBox.AddChild(titleLbl);

            var idLbl = new Label
            {
                Text = $"Type: {info.TypeName}",
                Modulate = new Color(0.6f, 0.65f, 0.75f, 0.7f)
            };
            infoVBox.AddChild(idLbl);
            row.AddChild(infoVBox);

            var forceBtn = new Button 
            { 
                Text = " Force ", 
                TooltipText = $"Force immediate travel/trigger for {info.DisplayName}",
                CustomMinimumSize = new Vector2(60, 32)
            };
            forceBtn.Pressed += () => EventDirector.ForceImmediateEvent(info.Id);
            row.AddChild(forceBtn);

            grid.AddChild(panel);
            eventEntries.Add((info, panel));
        }

        searchInput.TextChanged += query =>
        {
            string q = query.Trim();
            foreach (var (info, container) in eventEntries)
            {
                bool matches = string.IsNullOrEmpty(q) || 
                               info.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               info.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                               info.TypeName.Contains(q, StringComparison.OrdinalIgnoreCase);
                container.Visible = matches;
            }
        };

        // Initialize label
        string? currentForced = EventDirector.GetForcedEvent();
        if (!string.IsNullOrEmpty(currentForced))
        {
            var curInfo = allEvents.FirstOrDefault(e => e.Id.Equals(currentForced, StringComparison.OrdinalIgnoreCase));
            _eventOverrideLabel.Text = curInfo != null ? $"{curInfo.DisplayName} [{curInfo.Id}]" : currentForced;
        }
        else
        {
            _eventOverrideLabel.Text = "None";
        }

        return scroll;
    }

    private static HSlider AddSliderControl(VBoxContainer parent, string labelText, float min, float max, float step, float def)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = labelText, CustomMinimumSize = new Vector2(240, 0) });

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = def,
            CustomMinimumSize = new Vector2(200, 0)
        };
        row.AddChild(slider);

        var valLabel = new Label { Text = $" {def:F2}x" };
        slider.ValueChanged += val => valLabel.Text = $" {val:F2}x";
        row.AddChild(valLabel);

        parent.AddChild(row);
        return slider;
    }

    private void LoadSettingsValues()
    {
        var tweaks = ConfigManager.Current.PreRunTweaks;
        var sandbox = ConfigManager.Current.CombatSandbox;
        var general = ConfigManager.Current.General;

        if (_consoleHotkeyInput != null) _consoleHotkeyInput.Text = general.ConsoleHotkey;
        if (_guiHotkeyInput != null) _guiHotkeyInput.Text = general.GuiOverlayHotkey;
        if (_quickGodModeInput != null) _quickGodModeInput.Text = general.QuickGodModeKey;
        if (_quickKillEnemiesInput != null) _quickKillEnemiesInput.Text = general.QuickKillEnemiesKey;

        if (_mapRoomCountSpin != null) _mapRoomCountSpin.Value = tweaks.MapRoomCount;
        if (_goldSlider != null) _goldSlider.Value = tweaks.GoldRewardMultiplier;
        if (_shopDiscountSlider != null) _shopDiscountSlider.Value = tweaks.ShopDiscountMultiplier;
        if (_cardRewardSpin != null) _cardRewardSpin.Value = tweaks.CardRewardCount;
        if (_bonusGoldSpin != null) _bonusGoldSpin.Value = tweaks.StartingGoldBonus;
        if (_bonusHpSpin != null) _bonusHpSpin.Value = tweaks.StartingMaxHpBonus;
        if (_forceNeowCheck != null) _forceNeowCheck.ButtonPressed = tweaks.ForceNeowBonus;

        if (_eliteSlider != null) _eliteSlider.Value = tweaks.MapNodeDistribution.EliteWeightMultiplier;
        if (_shopSlider != null) _shopSlider.Value = tweaks.MapNodeDistribution.ShopWeightMultiplier;
        if (_eventSlider != null) _eventSlider.Value = tweaks.MapNodeDistribution.EventWeightMultiplier;
        if (_restSlider != null) _restSlider.Value = tweaks.MapNodeDistribution.RestSiteWeightMultiplier;
        if (_combatSlider != null) _combatSlider.Value = tweaks.MapNodeDistribution.CombatWeightMultiplier;

        if (_enemyHpSlider != null) _enemyHpSlider.Value = tweaks.EnemyHealthMultiplier;
        if (_enemyDmgSlider != null) _enemyDmgSlider.Value = tweaks.EnemyDamageMultiplier;
        if (_enemyDefSlider != null) _enemyDefSlider.Value = tweaks.EnemyDefendMultiplier;

        if (_endlessModeCheck != null) _endlessModeCheck.ButtonPressed = tweaks.EndlessMode.Enabled;
        if (_endlessMultiplierSpin != null) _endlessMultiplierSpin.Value = tweaks.EndlessMode.EnemyScalingMultiplier;
        if (_freeMapNavCheck != null) _freeMapNavCheck.ButtonPressed = tweaks.FreeMapNavigation || RuntimeStateManager.FreeMapNavigationEnabled;

        if (_godModeCheck != null) _godModeCheck.ButtonPressed = RuntimeStateManager.GodModeEnabled || sandbox.GodMode;
        if (_infEnergyCheck != null) _infEnergyCheck.ButtonPressed = RuntimeStateManager.InfiniteEnergyEnabled || sandbox.InfiniteEnergy;
        if (_oneHitKillCheck != null) _oneHitKillCheck.ButtonPressed = RuntimeStateManager.OneHitKillEnabled || sandbox.OneHitKill;
        if (_infPotionsCheck != null) _infPotionsCheck.ButtonPressed = sandbox.InfinitePotions;
        if (_noExhaustCheck != null) _noExhaustCheck.ButtonPressed = sandbox.NoCardExhaust;
        if (_bonusDrawSpin != null) _bonusDrawSpin.Value = sandbox.BonusDrawPerTurn;

        var player = InventoryDirector.GetActivePlayer();
        var selectedChar = GameHelper.GetSelectedCharacterModel();

        if (player != null)
        {
            if (_goldAmountSpin != null) _goldAmountSpin.Value = Math.Max(0, player.Gold);
            if (player.Creature != null)
            {
                if (_maxHpAmountSpin != null) _maxHpAmountSpin.Value = (double)player.Creature.MaxHp;
                if (_currentHpAmountSpin != null) _currentHpAmountSpin.Value = (double)player.Creature.CurrentHp;
            }
        }
        else if (selectedChar != null)
        {
            if (_goldAmountSpin != null) _goldAmountSpin.Value = Math.Max(0, selectedChar.StartingGold);
            if (_maxHpAmountSpin != null) _maxHpAmountSpin.Value = (double)selectedChar.StartingHp;
            if (_currentHpAmountSpin != null) _currentHpAmountSpin.Value = (double)selectedChar.StartingHp;
        }

        if (_damageAmountSpin != null)
        {
            _damageAmountSpin.Value = 5;
        }

        if (!_tweaksModified && _applyConfigBtn != null)
        {
            _applyConfigBtn.Disabled = true;
        }
    }

    private void SaveSettingsValues()
    {
        var tweaks = ConfigManager.Current.PreRunTweaks;
        var sandbox = ConfigManager.Current.CombatSandbox;
        var general = ConfigManager.Current.General;

        if (_consoleHotkeyInput != null)
        {
            string consoleVal = _consoleHotkeyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(consoleVal) || consoleVal.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                consoleVal = GeneralConfig.DefaultConsoleHotkey;
                _consoleHotkeyInput.Text = consoleVal;
            }
            general.ConsoleHotkey = consoleVal;
        }

        if (_guiHotkeyInput != null)
        {
            string guiVal = _guiHotkeyInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(guiVal) || guiVal.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                guiVal = GeneralConfig.DefaultGuiOverlayHotkey;
                _guiHotkeyInput.Text = guiVal;
            }
            general.GuiOverlayHotkey = guiVal;
        }
        if (_quickGodModeInput != null) general.QuickGodModeKey = _quickGodModeInput.Text.Trim();
        if (_quickKillEnemiesInput != null) general.QuickKillEnemiesKey = _quickKillEnemiesInput.Text.Trim();

        if (_mapRoomCountSpin != null) tweaks.MapRoomCount = (int)_mapRoomCountSpin.Value;
        if (_goldSlider != null) tweaks.GoldRewardMultiplier = (float)_goldSlider.Value;
        if (_shopDiscountSlider != null) tweaks.ShopDiscountMultiplier = (float)_shopDiscountSlider.Value;
        if (_cardRewardSpin != null) tweaks.CardRewardCount = (int)_cardRewardSpin.Value;
        if (_bonusGoldSpin != null) tweaks.StartingGoldBonus = (int)_bonusGoldSpin.Value;
        if (_bonusHpSpin != null) tweaks.StartingMaxHpBonus = (int)_bonusHpSpin.Value;
        if (_forceNeowCheck != null) tweaks.ForceNeowBonus = _forceNeowCheck.ButtonPressed;

        if (_eliteSlider != null) tweaks.MapNodeDistribution.EliteWeightMultiplier = (float)_eliteSlider.Value;
        if (_shopSlider != null) tweaks.MapNodeDistribution.ShopWeightMultiplier = (float)_shopSlider.Value;
        if (_eventSlider != null) tweaks.MapNodeDistribution.EventWeightMultiplier = (float)_eventSlider.Value;
        if (_restSlider != null) tweaks.MapNodeDistribution.RestSiteWeightMultiplier = (float)_restSlider.Value;
        if (_combatSlider != null) tweaks.MapNodeDistribution.CombatWeightMultiplier = (float)_combatSlider.Value;

        if (_enemyHpSlider != null) tweaks.EnemyHealthMultiplier = (float)_enemyHpSlider.Value;
        if (_enemyDmgSlider != null) tweaks.EnemyDamageMultiplier = (float)_enemyDmgSlider.Value;
        if (_enemyDefSlider != null) tweaks.EnemyDefendMultiplier = (float)_enemyDefSlider.Value;

        if (_endlessModeCheck != null) tweaks.EndlessMode.Enabled = _endlessModeCheck.ButtonPressed;
        if (_endlessMultiplierSpin != null) tweaks.EndlessMode.EnemyScalingMultiplier = (float)_endlessMultiplierSpin.Value;
        if (_freeMapNavCheck != null)
        {
            tweaks.FreeMapNavigation = _freeMapNavCheck.ButtonPressed;
            RuntimeStateManager.FreeMapNavigationEnabled = _freeMapNavCheck.ButtonPressed;
        }

        if (_godModeCheck != null) sandbox.GodMode = _godModeCheck.ButtonPressed;
        if (_infEnergyCheck != null) sandbox.InfiniteEnergy = _infEnergyCheck.ButtonPressed;
        if (_oneHitKillCheck != null) sandbox.OneHitKill = _oneHitKillCheck.ButtonPressed;
        if (_infPotionsCheck != null) sandbox.InfinitePotions = _infPotionsCheck.ButtonPressed;
        if (_noExhaustCheck != null) sandbox.NoCardExhaust = _noExhaustCheck.ButtonPressed;
        if (_bonusDrawSpin != null) sandbox.BonusDrawPerTurn = (int)_bonusDrawSpin.Value;

        ConfigManager.SaveConfig();
        ModLogger.Info("Mod settings saved successfully.");
    }

    private void ExecuteDirectCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        if (_commandInput != null) _commandInput.Text = "";

        string trimmed = input.Trim();
        ModLogger.Info($"Exec: {trimmed}");
        string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "god":
                CombatDirector.ToggleGodMode();
                LoadSettingsValues();
                break;
            case "infenergy":
                CombatDirector.ToggleInfiniteEnergy();
                LoadSettingsValues();
                break;
            case "onehitkill":
            case "ohk":
                CombatDirector.ToggleOneHitKill();
                LoadSettingsValues();
                break;
            case "killall":
                CombatDirector.KillAllEnemies();
                break;
            case "gold":
                if (parts.Length > 1 && int.TryParse(parts[1], out int g)) InventoryDirector.AddGold(g);
                break;
            case "setgold":
                if (parts.Length > 1 && int.TryParse(parts[1], out int sg)) InventoryDirector.SetGold(sg);
                break;
            case "heal":
                if (parts.Length > 1 && int.TryParse(parts[1], out int h)) InventoryDirector.Heal(h);
                break;
            case "damage":
            case "dmg":
                if (parts.Length > 1 && int.TryParse(parts[1], out int d)) InventoryDirector.DamagePlayer(d);
                break;
            case "setmaxhp":
            case "maxhp":
                if (parts.Length > 1 && int.TryParse(parts[1], out int m)) InventoryDirector.SetMaxHp(m);
                break;
            case "relic":
                if (parts.Length > 1) RelicDirector.AddRelic(parts[1]);
                break;
            case "rmrelic":
            case "removerelic":
                if (parts.Length > 1) RelicDirector.RemoveRelic(parts[1]);
                break;
            case "card":
                if (parts.Length > 1) CardDirector.AddCardToDeck(parts[1]);
                break;
            case "event":
                if (parts.Length > 1) EventDirector.ForceNextEvent(parts[1]);
                break;
            default:
                ModLogger.Warn($"Unknown command: {cmd}");
                break;
        }
    }

    private void OnLogReceived(LogLevel level, string msg)
    {
        string color = level switch
        {
            LogLevel.Warn => "yellow",
            LogLevel.Error => "red",
            LogLevel.Debug => "gray",
            _ => "white"
        };
        CallDeferred(nameof(DeferredLogAppend), $"[color={color}]{msg}[/color]");
    }

    private void DeferredLogAppend(string text)
    {
        _logLabel?.AppendText($"{text}\n");
    }
}
