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

    private ColorRect? _backdrop;
    private PanelContainer? _dialogPanel;
    private TabContainer? _tabs;

    private bool _isDragging = false;
    private Vector2 _dragOffset;

    private bool _isResizing = false;
    private Vector2 _resizeStartMousePos;
    private Vector2 _resizeStartPanelSize;

    private LineEdit? _consoleHotkeyInput;
    private LineEdit? _guiHotkeyInput;
    private LineEdit? _quickGodModeInput;
    private LineEdit? _quickKillEnemiesInput;

    private HSlider? _goldSlider;
    private HSlider? _shopDiscountSlider;
    private HSlider? _eliteSlider;
    private HSlider? _shopSlider;
    private HSlider? _eventSlider;
    private HSlider? _restSlider;
    private HSlider? _combatSlider;
    private HSlider? _treasureSlider;
    private SpinBox? _cardRewardSpin;
    private SpinBox? _bonusGoldSpin;
    private SpinBox? _bonusHpSpin;
    private SpinBox? _mapRoomCountSpin;
    private Label? _mapRoomWarningLabel;
    private CheckBox? _forceNeowCheck;
    private Label? _tweaksRunLockNoticeLabel;
    private PanelContainer? _tweaksRunLockNoticeContainer;

    private HSlider? _playerDmgSlider;
    private SpinBox? _maxEnergySpin;

    private HSlider? _enemyHpSlider;
    private HSlider? _enemyDmgSlider;
    private HSlider? _enemyDefSlider;
    private CheckBox? _endlessModeCheck;
    private SpinBox? _endlessMultiplierSpin;
    private CheckBox? _freeMapNavCheck;

    private CheckBox? _godModeCheck;
    private CheckBox? _infEnergyCheck;
    private CheckBox? _oneHitKillCheck;
    private CheckBox? _infPotionsCheck;
    private CheckBox? _noExhaustCheck;
    private SpinBox? _bonusDrawSpin;

    private GridContainer? _deckGrid;
    private GridContainer? _handGrid;
    private GridContainer? _drawGrid;
    private GridContainer? _discardGrid;
    private GridContainer? _exhaustGrid;
    private Label? _deckTitleLabel;
    private Label? _handTitleLabel;
    private Label? _drawTitleLabel;
    private Label? _discardTitleLabel;
    private Label? _exhaustTitleLabel;
    private System.Collections.Generic.List<ItemEntry> _availableCardEntries = new();

    private GridContainer? _activeRelicsGrid;
    private System.Collections.Generic.List<ItemEntry> _availableRelicEntries = new();

    private LineEdit? _relicInput;
    private LineEdit? _cardInput;
    private SpinBox? _goldAmountSpin;
    private SpinBox? _currentHpAmountSpin;
    private SpinBox? _damageAmountSpin;
    private SpinBox? _maxHpAmountSpin;
    private Label? _eventOverrideLabel;

    private RichTextLabel? _logLabel;
    private LineEdit? _commandInput;

    public override void _Ready()
    {
        _instance = this;
        Layer = 130;
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

        if (@event is InputEventMouseButton mouseEv && !mouseEv.Pressed && mouseEv.ButtonIndex == MouseButton.Left)
        {
            _isDragging = false;
            _isResizing = false;
        }

        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            string guiKey = !string.IsNullOrWhiteSpace(ConfigManager.Current.General.GuiOverlayHotkey) && !ConfigManager.Current.General.GuiOverlayHotkey.Equals("None", StringComparison.OrdinalIgnoreCase)
                ? ConfigManager.Current.General.GuiOverlayHotkey
                : GeneralConfig.DefaultGuiOverlayHotkey;
            if (GameHelper.IsKeyMatch(keyEvent, guiKey))
            {
                ToggleDialog();
                GetViewport().SetInputAsHandled();
                return;
            }

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

    public static void ResetWindowLayout()
    {
        ConfigManager.Current.UI.MenuPosX = null;
        ConfigManager.Current.UI.MenuPosY = null;
        ConfigManager.Current.UI.MenuWidth = null;
        ConfigManager.Current.UI.MenuHeight = null;
        ConfigManager.SaveConfig();
        if (_instance != null && _instance._dialogPanel != null)
        {
            _instance.ApplyOrRestoreWindowLayout();
        }
        ModLogger.Info("ModSettingsDialog: GUI window position and size reset to defaults.");
    }

    public void OpenDialog()
    {
        ModLogger.Verbose("ModSettingsDialog", "OpenDialog called. Loading settings values and computing run status...");
        if (_dialogPanel != null)
        {
            LoadSettingsValues();
            ApplyOrRestoreWindowLayout();
            
            bool inRun = GameHelper.GetActivePlayer() != null;
            ModLogger.Verbose("ModSettingsDialog", $"Player inRun status: {inRun}");
            if (_tabs != null)
            {
                int tweaksIdx = 4;
                _tabs.SetTabDisabled(tweaksIdx, false);
            }
            UpdateTweaksRunLockState(inRun);

            if (_backdrop != null) _backdrop.Visible = true;
            _dialogPanel.Visible = true;
            UpdateBlockingState(true);
            RefreshRealTimeCardTabs();
            RefreshRealTimeRelicTabs();
        }
    }

    public void CloseDialog()
    {
        ModLogger.Verbose("ModSettingsDialog", "CloseDialog called.");
        _isDragging = false;
        _isResizing = false;
        SaveSettingsValues();
        if (_dialogPanel != null)
        {
            _dialogPanel.Visible = false;
        }
        if (_backdrop != null)
        {
            _backdrop.Visible = false;
        }
        UpdateBlockingState(false);
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

    private void ApplyOrRestoreWindowLayout()
    {
        if (_dialogPanel == null) return;

        Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
        if (viewportSize.X <= 200 || viewportSize.Y <= 200)
        {
            viewportSize = new Vector2(1920, 1080);
        }

        // Proportional initial size: 85% width, 85% height of active game resolution
        float propWidth = Mathf.Clamp(viewportSize.X * 0.85f, 650f, Math.Max(650f, viewportSize.X - 40f));
        float propHeight = Mathf.Clamp(viewportSize.Y * 0.85f, 450f, Math.Max(450f, viewportSize.Y - 40f));
        float propX = Math.Max(0f, (viewportSize.X - propWidth) / 2f);
        float propY = Math.Max(0f, (viewportSize.Y - propHeight) / 2f);

        var uiCfg = ConfigManager.Current.UI;
        float width = (uiCfg.MenuWidth.HasValue && uiCfg.MenuWidth.Value >= 200f) ? uiCfg.MenuWidth.Value : propWidth;
        float height = (uiCfg.MenuHeight.HasValue && uiCfg.MenuHeight.Value >= 200f) ? uiCfg.MenuHeight.Value : propHeight;

        // Clamp to current viewport
        width = Mathf.Clamp(width, 650f, viewportSize.X);
        height = Mathf.Clamp(height, 450f, viewportSize.Y);

        float posX = uiCfg.MenuPosX.HasValue ? uiCfg.MenuPosX.Value : propX;
        float posY = uiCfg.MenuPosY.HasValue ? uiCfg.MenuPosY.Value : propY;

        posX = Mathf.Clamp(posX, 0f, Math.Max(0f, viewportSize.X - width));
        posY = Mathf.Clamp(posY, 0f, Math.Max(0f, viewportSize.Y - height));

        _dialogPanel.Position = new Vector2(posX, posY);
        _dialogPanel.Size = new Vector2(width, height);
    }

    private void SetupDialogUI()
    {
        _backdrop = new ColorRect
        {
            Name = "Backdrop",
            Color = new Color(0, 0, 0, 0.45f),
            Visible = false
        };
        _backdrop.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _backdrop.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                CloseDialog();
            }
        };
        AddChild(_backdrop);

        _dialogPanel = new PanelContainer
        {
            Name = "DialogPanel",
            Theme = CreateModTheme(),
            Visible = false,
            CustomMinimumSize = new Vector2(650, 450)
        };
        _dialogPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);

        var contentVBox = new VBoxContainer 
        { 
            Name = "ContentBox",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        contentVBox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _dialogPanel.AddChild(contentVBox);

        var header = new HBoxContainer
        {
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Move,
            TooltipText = "Click and drag to move window"
        };
        header.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mouse)
            {
                if (mouse.ButtonIndex == MouseButton.Left)
                {
                    if (mouse.Pressed)
                    {
                        _isDragging = true;
                        _dragOffset = mouse.GlobalPosition - (_dialogPanel?.Position ?? Vector2.Zero);
                    }
                    else
                    {
                        _isDragging = false;
                    }
                }
            }
            else if (@event is InputEventMouseMotion motion && _isDragging && _dialogPanel != null)
            {
                Vector2 newPos = motion.GlobalPosition - _dragOffset;
                Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
                float maxX = Math.Max(0f, viewportSize.X - _dialogPanel.Size.X);
                float maxY = Math.Max(0f, viewportSize.Y - _dialogPanel.Size.Y);
                _dialogPanel.Position = new Vector2(Mathf.Clamp(newPos.X, 0f, maxX), Mathf.Clamp(newPos.Y, 0f, maxY));
            }
        };

        var title = new Label
        {
            Text = "  AIOTweaks - In-Game Mod Settings & Sandbox Suite  ",
            Modulate = new Color(0.35f, 0.85f, 1f),
            MouseFilter = Control.MouseFilterEnum.Pass
        };
        var closeBtn = new Button { Text = " X ", MouseDefaultCursorShape = Control.CursorShape.Arrow };
        closeBtn.Pressed += CloseDialog;

        header.AddChild(title);
        header.AddSpacer(false);
        header.AddChild(closeBtn);
        contentVBox.AddChild(header);
        contentVBox.AddChild(new HSeparator());

        _tabs = new TabContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        contentVBox.AddChild(_tabs);

        _tabs.AddChild(BuildRelicsTab());

        _tabs.AddChild(BuildCardsTab());

        _tabs.AddChild(BuildPlayerTab());

        _tabs.AddChild(BuildCombatSandboxTab());

        _tabs.AddChild(BuildTweaksTab());

        _tabs.TabChanged += (tabIdx) =>
        {
            if (tabIdx == 0)
            {
                RefreshRealTimeRelicTabs();
            }
            else if (tabIdx == 1)
            {
                RefreshRealTimeCardTabs();
            }
        };

        var footer = new HBoxContainer();

        var defaultBtn = new Button { Text = " Reset to Game Defaults " };
        defaultBtn.Pressed += () =>
        {
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
            ConfigManager.Current.PreRunTweaks.MapNodeDistribution.TreasureRoomMultiplier = 1.0f;

            ConfigManager.Current.PreRunTweaks.EnemyHealthMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.EnemyDamageMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.EnemyDefendMultiplier = 1.0f;

            ConfigManager.Current.PreRunTweaks.EndlessMode.Enabled = false;
            ConfigManager.Current.PreRunTweaks.EndlessMode.EnemyScalingMultiplier = 2.0f;
            ConfigManager.Current.PreRunTweaks.FreeMapNavigation = false;

            ConfigManager.Current.PreRunTweaks.PlayerDamageMultiplier = 1.0f;
            ConfigManager.Current.PreRunTweaks.MaxEnergy = 3;

            ConfigManager.Current.CombatSandbox.GodMode = false;
            ConfigManager.Current.CombatSandbox.InfiniteEnergy = false;
            ConfigManager.Current.CombatSandbox.OneHitKill = false;
            ConfigManager.Current.CombatSandbox.InfinitePotions = false;
            ConfigManager.Current.CombatSandbox.NoCardExhaust = false;
            ConfigManager.Current.CombatSandbox.BonusDrawPerTurn = 0;
            ConfigManager.Current.CombatSandbox.MaxHandSizeOverride = 10;

            RuntimeStateManager.ResetSessionState();
            RuntimeStateManager.FreeMapNavigationEnabled = false;
            GameHelper.SetPlayerMaxEnergy(3);
            GameHelper.RefreshCombatIntents();
            GameHelper.RefreshAllVisibleCards();
            try
            {
                MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.RefreshAllPointVisuals();
            }
            catch { }

            LoadSettingsValues();
            ConfigManager.SaveConfig();
            ModLogger.Info("Reset all tweaks and settings to game defaults.");
        };

        var doneBtn = new Button { Text = " Return to Game " };
        doneBtn.Pressed += CloseDialog;

        var resizeGrip = new Control
        {
            Name = "ResizeGrip",
            CustomMinimumSize = new Vector2(26, 26),
            MouseFilter = Control.MouseFilterEnum.Stop,
            MouseDefaultCursorShape = Control.CursorShape.Fdiagsize,
            TooltipText = "Drag to resize window"
        };
        resizeGrip.Draw += () =>
        {
            var col = new Color(0.45f, 0.75f, 1.0f, 0.75f);
            var sz = resizeGrip.Size;
            resizeGrip.DrawLine(new Vector2(sz.X - 4, sz.Y - 14), new Vector2(sz.X - 14, sz.Y - 4), col, 2f);
            resizeGrip.DrawLine(new Vector2(sz.X - 4, sz.Y - 9), new Vector2(sz.X - 9, sz.Y - 4), col, 2f);
            resizeGrip.DrawLine(new Vector2(sz.X - 4, sz.Y - 4), new Vector2(sz.X - 4, sz.Y - 4), col, 2f);
        };

        resizeGrip.GuiInput += @event =>
        {
            if (@event is InputEventMouseButton mouse)
            {
                if (mouse.ButtonIndex == MouseButton.Left)
                {
                    if (mouse.Pressed)
                    {
                        _isResizing = true;
                        _resizeStartMousePos = mouse.GlobalPosition;
                        _resizeStartPanelSize = _dialogPanel?.Size ?? Vector2.Zero;
                    }
                    else
                    {
                        _isResizing = false;
                    }
                }
            }
            else if (@event is InputEventMouseMotion motion && _isResizing && _dialogPanel != null)
            {
                Vector2 delta = motion.GlobalPosition - _resizeStartMousePos;
                Vector2 newSize = _resizeStartPanelSize + delta;
                Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1920, 1080);
                float maxWidth = viewportSize.X - _dialogPanel.Position.X;
                float maxHeight = viewportSize.Y - _dialogPanel.Position.Y;
                float clampedW = Mathf.Clamp(newSize.X, 650f, Math.Max(650f, maxWidth));
                float clampedH = Mathf.Clamp(newSize.Y, 450f, Math.Max(450f, maxHeight));
                _dialogPanel.Size = new Vector2(clampedW, clampedH);
            }
        };

        footer.AddChild(defaultBtn);
        footer.AddSpacer(false);
        footer.AddChild(doneBtn);
        footer.AddChild(resizeGrip);
        contentVBox.AddChild(new HSeparator());
        contentVBox.AddChild(footer);

        AddChild(_dialogPanel);
    }

    private void MarkTweaksModified()
    {
        SaveSettingsValues();
    }

    private static PanelContainer CreateSectionCard(string title, Control content, Color? accentColor = null, string? subtitle = null)
    {
        var card = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };
        
        var cardStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.10f, 0.14f, 0.95f),
            BorderColor = new Color(0.20f, 0.25f, 0.35f, 0.85f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 12,
            ContentMarginBottom = 12,
            ShadowColor = new Color(0, 0, 0, 0.35f),
            ShadowSize = 4
        };
        card.AddThemeStyleboxOverride("panel", cardStyle);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        var headerHBox = new HBoxContainer();
        var titleColor = accentColor ?? new Color(0.45f, 0.78f, 1.0f);
        var titleLabel = new Label
        {
            Text = title,
            Modulate = titleColor
        };
        headerHBox.AddChild(titleLabel);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var subLabel = new Label
            {
                Text = $" — {subtitle}",
                Modulate = new Color(0.6f, 0.65f, 0.72f, 0.85f)
            };
            headerHBox.AddChild(subLabel);
        }
        vbox.AddChild(headerHBox);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        vbox.AddChild(content);
        card.AddChild(vbox);
        return card;
    }

    private Control BuildTweaksTab()
    {
        var scroll = new ScrollContainer 
        { 
            Name = "Tweaks & Multipliers",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        var rootVbox = new VBoxContainer 
        { 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        rootVbox.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(rootVbox);

        _tweaksRunLockNoticeContainer = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            Visible = false
        };
        var bannerStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.2f, 0.08f, 0.08f, 0.95f),
            BorderColor = new Color(0.9f, 0.35f, 0.25f, 0.9f),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        };
        _tweaksRunLockNoticeContainer.AddThemeStyleboxOverride("panel", bannerStyle);

        _tweaksRunLockNoticeLabel = new Label
        {
            Text = "[Locked] Pre-run map generation & starting bonus settings are locked during an active run.\n(All options can be freely customized from the Main Menu)",
            Modulate = new Color(1f, 0.7f, 0.6f)
        };
        _tweaksRunLockNoticeContainer.AddChild(_tweaksRunLockNoticeLabel);
        rootVbox.AddChild(_tweaksRunLockNoticeContainer);

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 14);
        rootVbox.AddChild(grid);

        var leftCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        leftCol.AddThemeConstantOverride("separation", 14);
        var rightCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rightCol.AddThemeConstantOverride("separation", 14);
        grid.AddChild(leftCol);
        grid.AddChild(rightCol);

        var hotkeysBox = new VBoxContainer();
        hotkeysBox.AddThemeConstantOverride("separation", 8);
        
        var hotkeyNote = new Label 
        { 
            Text = "If left empty, default hotkeys (F1 for Console, F3 for GUI) will be automatically restored.", 
            Modulate = new Color(0.65f, 0.7f, 0.78f, 0.8f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        hotkeysBox.AddChild(hotkeyNote);

        var consoleKeyRow = new HBoxContainer();
        consoleKeyRow.AddChild(new Label { Text = "Console Hotkey: ", CustomMinimumSize = new Vector2(200, 0) });
        _consoleHotkeyInput = new LineEdit { PlaceholderText = "e.g. F1, Quoteleft (Default: F1)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _consoleHotkeyInput.TextChanged += _ => MarkTweaksModified();
        consoleKeyRow.AddChild(_consoleHotkeyInput);
        hotkeysBox.AddChild(consoleKeyRow);

        var guiKeyRow = new HBoxContainer();
        guiKeyRow.AddChild(new Label { Text = "GUI Menu Overlay Hotkey: ", CustomMinimumSize = new Vector2(200, 0) });
        _guiHotkeyInput = new LineEdit { PlaceholderText = "e.g. F3, F8 (Default: F3)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _guiHotkeyInput.TextChanged += _ => MarkTweaksModified();
        guiKeyRow.AddChild(_guiHotkeyInput);
        hotkeysBox.AddChild(guiKeyRow);

        var godKeyRow = new HBoxContainer();
        godKeyRow.AddChild(new Label { Text = "Quick God Mode Hotkey: ", CustomMinimumSize = new Vector2(200, 0) });
        _quickGodModeInput = new LineEdit { PlaceholderText = "e.g. F2 (Empty = Disabled)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _quickGodModeInput.TextChanged += _ => MarkTweaksModified();
        godKeyRow.AddChild(_quickGodModeInput);
        hotkeysBox.AddChild(godKeyRow);

        var killKeyRow = new HBoxContainer();
        killKeyRow.AddChild(new Label { Text = "Quick Kill All Enemies Hotkey: ", CustomMinimumSize = new Vector2(200, 0) });
        _quickKillEnemiesInput = new LineEdit { PlaceholderText = "e.g. F4 (Empty = Disabled)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _quickKillEnemiesInput.TextChanged += _ => MarkTweaksModified();
        killKeyRow.AddChild(_quickKillEnemiesInput);
        hotkeysBox.AddChild(killKeyRow);

        leftCol.AddChild(CreateSectionCard("Keybindings & Hotkeys", hotkeysBox, new Color(0.4f, 0.85f, 1f)));

        var preRunBox = new VBoxContainer();
        preRunBox.AddThemeConstantOverride("separation", 8);

        var mapRoomRow = new HBoxContainer();
        mapRoomRow.AddChild(new Label { Text = "Map Size (Floors / Room Count): ", CustomMinimumSize = new Vector2(230, 0) });
        _mapRoomCountSpin = new SpinBox { MinValue = 15, MaxValue = 50, Value = 15, Step = 1, TooltipText = "Sets the number of rooms/floors per act (15 to 50). Minimum 15 is required by map generator." };
        _mapRoomWarningLabel = new Label
        {
            Text = "  [Warning: >30 rooms may cause unexpected crashes and bugs]",
            Modulate = new Color(1f, 0.45f, 0.3f),
            Visible = false
        };
        _mapRoomCountSpin.ValueChanged += val =>
        {
            MarkTweaksModified();
            if (_mapRoomWarningLabel != null)
            {
                _mapRoomWarningLabel.Visible = val > 30;
            }
        };
        mapRoomRow.AddChild(_mapRoomCountSpin);
        mapRoomRow.AddChild(_mapRoomWarningLabel);
        preRunBox.AddChild(mapRoomRow);

        _goldSlider = AddSliderControl(preRunBox, "Gold Drop Multiplier:", 0.1f, 5.0f, 0.1f, 1.0f);
        _goldSlider.ValueChanged += _ => MarkTweaksModified();
        _shopDiscountSlider = AddSliderControl(preRunBox, "Shop Discount Multiplier:", 0.1f, 2.0f, 0.05f, 1.0f);
        _shopDiscountSlider.ValueChanged += _ => MarkTweaksModified();

        var cardRewardRow = new HBoxContainer();
        cardRewardRow.AddChild(new Label { Text = "Card Choices per Reward: ", CustomMinimumSize = new Vector2(230, 0) });
        _cardRewardSpin = new SpinBox { MinValue = 1, MaxValue = 10, Value = 3 };
        _cardRewardSpin.ValueChanged += _ => MarkTweaksModified();
        cardRewardRow.AddChild(_cardRewardSpin);
        preRunBox.AddChild(cardRewardRow);

        var startGoldRow = new HBoxContainer();
        startGoldRow.AddChild(new Label { Text = "Starting Gold Bonus: ", CustomMinimumSize = new Vector2(230, 0) });
        _bonusGoldSpin = new SpinBox { MinValue = 0, MaxValue = 9999, Step = 25, Value = 0 };
        _bonusGoldSpin.ValueChanged += _ => MarkTweaksModified();
        startGoldRow.AddChild(_bonusGoldSpin);
        preRunBox.AddChild(startGoldRow);

        var startHpRow = new HBoxContainer();
        startHpRow.AddChild(new Label { Text = "Starting Max HP Bonus: ", CustomMinimumSize = new Vector2(230, 0) });
        _bonusHpSpin = new SpinBox { MinValue = 0, MaxValue = 500, Step = 5, Value = 0 };
        _bonusHpSpin.ValueChanged += _ => MarkTweaksModified();
        startHpRow.AddChild(_bonusHpSpin);
        preRunBox.AddChild(startHpRow);

        _forceNeowCheck = new CheckBox { Text = " Spawn Neow at start? (Uncheck to skip Neow and start on map)", TooltipText = "Guarantees Neow blessing when checked. When unchecked, skips Neow and starts directly on the map." };
        _forceNeowCheck.Toggled += _ => MarkTweaksModified();
        preRunBox.AddChild(_forceNeowCheck);

        leftCol.AddChild(CreateSectionCard("Pre-Run Tweaks & Map Generation (Pre-Run Only)", preRunBox, new Color(1f, 0.85f, 0.35f)));

        var endlessBox = new VBoxContainer();
        endlessBox.AddThemeConstantOverride("separation", 8);

        _endlessModeCheck = new CheckBox { Text = " Enable Endless Mode (Scale enemies progressively each loop reset)" };
        _endlessModeCheck.Toggled += _ => MarkTweaksModified();
        endlessBox.AddChild(_endlessModeCheck);

        var endlessMultRow = new HBoxContainer();
        endlessMultRow.AddChild(new Label { Text = "Endless Loop Scaling Multiplier: ", CustomMinimumSize = new Vector2(230, 0) });
        _endlessMultiplierSpin = new SpinBox { MinValue = 1.0, MaxValue = 10.0, Step = 0.1, Value = 2.0 };
        _endlessMultiplierSpin.ValueChanged += _ => MarkTweaksModified();
        endlessMultRow.AddChild(_endlessMultiplierSpin);
        endlessBox.AddChild(endlessMultRow);

        _freeMapNavCheck = new CheckBox { Text = " Free Map Navigation (Click & travel to ANY room freely on map)" };
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
        endlessBox.AddChild(_freeMapNavCheck);

        leftCol.AddChild(CreateSectionCard("Endless Mode & Map Navigation", endlessBox, new Color(0.8f, 0.55f, 1f)));

        var weightsBox = new VBoxContainer();
        weightsBox.AddThemeConstantOverride("separation", 8);

        var fairNote = new Label
        {
            Text = "Note: Non-default map multipliers mark runs as Seeded/Custom (locks achievements). Keep all at 1.0x for standard runs.",
            Modulate = new Color(1f, 0.8f, 0.4f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        weightsBox.AddChild(fairNote);

        _eliteSlider = AddSliderControl(weightsBox, "Elite Encounter Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _eliteSlider.ValueChanged += _ => MarkTweaksModified();
        _shopSlider = AddSliderControl(weightsBox, "Shop Node Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _shopSlider.ValueChanged += _ => MarkTweaksModified();
        _eventSlider = AddSliderControl(weightsBox, "Event / Unknown Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _eventSlider.ValueChanged += _ => MarkTweaksModified();
        _restSlider = AddSliderControl(weightsBox, "Rest Site Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _restSlider.ValueChanged += _ => MarkTweaksModified();
        _combatSlider = AddSliderControl(weightsBox, "Normal Combat Weight:", 0.0f, 5.0f, 0.1f, 1.0f);
        _combatSlider.ValueChanged += _ => MarkTweaksModified();
        _treasureSlider = AddSliderControl(weightsBox, "Treasure Room Multiplier:", 0.0f, 5.0f, 0.1f, 1.0f);
        _treasureSlider.ValueChanged += _ => MarkTweaksModified();

        rightCol.AddChild(CreateSectionCard("Map Node Generation Weights", weightsBox, new Color(0.45f, 0.95f, 0.65f)));

        var enemyBox = new VBoxContainer();
        enemyBox.AddThemeConstantOverride("separation", 8);

        _enemyHpSlider = AddSliderControl(enemyBox, "Enemy Health Multiplier:", 0.1f, 10.0f, 0.1f, 1.0f);
        _enemyHpSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.EnemyHealthMultiplier = (float)val;
            GameHelper.RefreshCombatIntents();
        };

        _enemyDmgSlider = AddSliderControl(enemyBox, "Enemy Damage Multiplier:", 0.0f, 10.0f, 0.1f, 1.0f);
        _enemyDmgSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.EnemyDamageMultiplier = (float)val;
            GameHelper.RefreshCombatIntents();
        };

        _enemyDefSlider = AddSliderControl(enemyBox, "Enemy Defend/Block Multiplier:", 0.0f, 10.0f, 0.1f, 1.0f);
        _enemyDefSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.EnemyDefendMultiplier = (float)val;
            GameHelper.RefreshCombatIntents();
        };

        rightCol.AddChild(CreateSectionCard("Enemy Multipliers & Scaling", enemyBox, new Color(1f, 0.45f, 0.45f)));

        var playerScalingBox = new VBoxContainer();
        playerScalingBox.AddThemeConstantOverride("separation", 8);

        _playerDmgSlider = AddSliderControl(playerScalingBox, "Player Damage Multiplier:", 0.0f, 10.0f, 0.1f, 1.0f);
        _playerDmgSlider.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.PlayerDamageMultiplier = (float)val;
            GameHelper.RefreshAllVisibleCards();
        };

        var maxEnergyRow = new HBoxContainer();
        maxEnergyRow.AddChild(new Label { Text = "Max Energy Count: ", CustomMinimumSize = new Vector2(230, 0) });
        _maxEnergySpin = new SpinBox { MinValue = 1, MaxValue = 20, Step = 1, Value = GameHelper.GetPlayerMaxEnergy(), TooltipText = "Sets the baseline Max Energy count. Dynamically reads and syncs with the active game state." };
        _maxEnergySpin.ValueChanged += val =>
        {
            MarkTweaksModified();
            ConfigManager.Current.PreRunTweaks.MaxEnergy = (int)val;
            GameHelper.SetPlayerMaxEnergy((int)val);
        };
        maxEnergyRow.AddChild(_maxEnergySpin);
        playerScalingBox.AddChild(maxEnergyRow);

        rightCol.AddChild(CreateSectionCard("Player & Combat Scaling", playerScalingBox, new Color(0.35f, 0.85f, 1f)));

        return scroll;
    }

    private void UpdateTweaksRunLockState(bool inRun)
    {
        if (_tweaksRunLockNoticeContainer != null)
        {
            _tweaksRunLockNoticeContainer.Visible = inRun;
        }
        else if (_tweaksRunLockNoticeLabel != null)
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
        if (_treasureSlider != null) _treasureSlider.Editable = allowPreRunEdits;
    }

    private Control BuildCombatSandboxTab()
    {
        var scroll = new ScrollContainer 
        { 
            Name = "Combat Sandbox",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        var rootVbox = new VBoxContainer 
        { 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        rootVbox.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(rootVbox);

        var cheatsBox = new VBoxContainer();
        cheatsBox.AddThemeConstantOverride("separation", 10);

        var cheatsGrid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        cheatsGrid.AddThemeConstantOverride("h_separation", 24);
        cheatsGrid.AddThemeConstantOverride("v_separation", 10);
        cheatsBox.AddChild(cheatsGrid);

        _godModeCheck = new CheckBox { Text = " God Mode (Immune to all incoming damage)" };
        _godModeCheck.Toggled += val => { RuntimeStateManager.GodModeEnabled = val; ConfigManager.Current.CombatSandbox.GodMode = val; ConfigManager.SaveConfig(); };
        cheatsGrid.AddChild(_godModeCheck);

        _infEnergyCheck = new CheckBox { Text = " Infinite Energy (Playing cards does not drain energy)" };
        _infEnergyCheck.Toggled += val => { RuntimeStateManager.InfiniteEnergyEnabled = val; ConfigManager.Current.CombatSandbox.InfiniteEnergy = val; ConfigManager.SaveConfig(); };
        cheatsGrid.AddChild(_infEnergyCheck);

        _oneHitKillCheck = new CheckBox { Text = " 1-Hit Kill (Attacks deal lethal damage to enemies)" };
        _oneHitKillCheck.Toggled += val => { RuntimeStateManager.OneHitKillEnabled = val; ConfigManager.Current.CombatSandbox.OneHitKill = val; ConfigManager.SaveConfig(); };
        cheatsGrid.AddChild(_oneHitKillCheck);

        _infPotionsCheck = new CheckBox { Text = " Infinite Potions (Using potions does not consume them)" };
        _infPotionsCheck.Toggled += val => { ConfigManager.Current.CombatSandbox.InfinitePotions = val; ConfigManager.SaveConfig(); };
        cheatsGrid.AddChild(_infPotionsCheck);

        _noExhaustCheck = new CheckBox { Text = " No Card Exhaust (Exhausted cards are retained)" };
        _noExhaustCheck.Toggled += val => { ConfigManager.Current.CombatSandbox.NoCardExhaust = val; ConfigManager.SaveConfig(); };
        cheatsGrid.AddChild(_noExhaustCheck);

        var drawRow = new HBoxContainer();
        drawRow.AddChild(new Label { Text = "Bonus Card Draw per Turn: ", CustomMinimumSize = new Vector2(200, 0) });
        _bonusDrawSpin = new SpinBox { MinValue = 0, MaxValue = 10, Value = 0 };
        _bonusDrawSpin.ValueChanged += val => { ConfigManager.Current.CombatSandbox.BonusDrawPerTurn = (int)val; ConfigManager.SaveConfig(); };
        drawRow.AddChild(_bonusDrawSpin);
        cheatsGrid.AddChild(drawRow);

        rootVbox.AddChild(CreateSectionCard("Real-Time Combat Cheats", cheatsBox, new Color(1f, 0.45f, 0.45f)));

        var actionsBox = new VBoxContainer();
        actionsBox.AddThemeConstantOverride("separation", 10);

        var actionGrid = new GridContainer
        {
            Columns = 4,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        actionGrid.AddThemeConstantOverride("h_separation", 12);
        actionGrid.AddThemeConstantOverride("v_separation", 8);

        var killAllBtn = new Button 
        { 
            Text = " Kill All Enemies Now ",
            CustomMinimumSize = new Vector2(180, 38)
        };
        killAllBtn.Pressed += CombatDirector.KillAllEnemies;

        var endTurnBtn = new Button 
        { 
            Text = " Force End Turn ",
            CustomMinimumSize = new Vector2(180, 38)
        };
        endTurnBtn.Pressed += CombatDirector.EndTurn;

        var draw3Btn = new Button 
        { 
            Text = " Draw 3 Cards ",
            CustomMinimumSize = new Vector2(180, 38)
        };
        draw3Btn.Pressed += () => CombatDirector.DrawCards(3);

        var energy3Btn = new Button 
        { 
            Text = " +3 Energy ",
            CustomMinimumSize = new Vector2(180, 38)
        };
        energy3Btn.Pressed += () => CombatDirector.AddEnergy(3);

        actionGrid.AddChild(killAllBtn);
        actionGrid.AddChild(endTurnBtn);
        actionGrid.AddChild(draw3Btn);
        actionGrid.AddChild(energy3Btn);
        actionsBox.AddChild(actionGrid);

        rootVbox.AddChild(CreateSectionCard("Immediate Combat Actions", actionsBox, new Color(1f, 0.75f, 0.3f)));

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

        var filterRow = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var searchInput = new LineEdit
        {
            PlaceholderText = "Search cards (e.g. 'Strike', 'Bash', 'DemonForm')...",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        filterRow.AddChild(searchInput);

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

                var canonicalKws = GameHelper.GetCardKeywords(canonical);
                if (canonicalKws != null && canonicalKws.Count > 0)
                {
                    var kwHBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
                    kwHBox.AddThemeConstantOverride("separation", 2);
                    foreach (var kw in canonicalKws)
                    {
                        if (kw == MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.None) continue;
                        var kwLbl = new Label
                        {
                            Text = $"[{kw}]",
                            Modulate = GameHelper.GetKeywordBadgeColor(kw),
                            TooltipText = fullTooltip
                        };
                        kwHBox.AddChild(kwLbl);
                    }
                    cardVbox.AddChild(kwHBox);
                }
            }

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

        poolPopup.IndexPressed += (long index) =>
        {
            int idx = (int)index;
            if (idx == 0)
            {
                for (int i = poolItemOffset; i < poolPopup.ItemCount; i++)
                {
                    poolPopup.SetItemChecked(i, true);
                    selectedPools.Add(poolPopup.GetItemMetadata(i).AsString());
                }
            }
            else if (idx == 1)
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

        typePopup.IndexPressed += (long index) =>
        {
            int idx = (int)index;
            if (idx == 0)
            {
                for (int i = typeItemOffset; i < typePopup.ItemCount; i++)
                {
                    typePopup.SetItemChecked(i, true);
                    selectedTypes.Add((MegaCrit.Sts2.Core.Entities.Cards.CardType)typePopup.GetItemMetadata(i).AsInt32());
                }
            }
            else if (idx == 1)
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

        rarityPopup.IndexPressed += (long index) =>
        {
            int idx = (int)index;
            if (idx == 0)
            {
                for (int i = rarityItemOffset; i < rarityPopup.ItemCount; i++)
                {
                    rarityPopup.SetItemChecked(i, true);
                    selectedRarities.Add((MegaCrit.Sts2.Core.Entities.Cards.CardRarity)rarityPopup.GetItemMetadata(i).AsInt32());
                }
            }
            else if (idx == 1)
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
        _deckTitleLabel = new Label { Text = "Current Deck:", Modulate = new Color(0.4f, 1f, 0.4f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleBox.AddChild(_deckTitleLabel);
        
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
        _handTitleLabel = new Label { Text = "Combat Hand:", Modulate = new Color(0.4f, 0.9f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleBox.AddChild(_handTitleLabel);
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
        _drawTitleLabel = new Label { Text = "Draw Pile:", Modulate = new Color(1f, 0.9f, 0.4f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleBox.AddChild(_drawTitleLabel);
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
        _discardTitleLabel = new Label { Text = "Discard Pile:", Modulate = new Color(1f, 0.4f, 0.4f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleBox.AddChild(_discardTitleLabel);
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
        _exhaustTitleLabel = new Label { Text = "Exhaust Pile:", Modulate = new Color(0.85f, 0.5f, 1f), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleBox.AddChild(_exhaustTitleLabel);
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
        bool inCombat = GameHelper.IsInCombat();

        if (_deckTitleLabel != null) _deckTitleLabel.Text = $"Current Deck ({deckCards?.Count ?? 0} cards):";
        if (_handTitleLabel != null) _handTitleLabel.Text = inCombat ? $"Combat Hand ({handCards?.Count ?? 0} cards):" : "Combat Hand (Not in combat):";
        if (_drawTitleLabel != null) _drawTitleLabel.Text = inCombat ? $"Draw Pile ({drawCards?.Count ?? 0} cards):" : "Draw Pile (Not in combat):";
        if (_discardTitleLabel != null) _discardTitleLabel.Text = inCombat ? $"Discard Pile ({discardCards?.Count ?? 0} cards):" : "Discard Pile (Not in combat):";
        if (_exhaustTitleLabel != null) _exhaustTitleLabel.Text = inCombat ? $"Exhaust Pile ({exhaustCards?.Count ?? 0} cards):" : "Exhaust Pile (Not in combat):";

        RefreshGrid(_deckGrid, deckCards, true, "deck");
        RefreshGrid(_handGrid, handCards, false, "hand");
        RefreshGrid(_drawGrid, drawCards, false, "draw");
        RefreshGrid(_discardGrid, discardCards, false, "discard");
        RefreshGrid(_exhaustGrid, exhaustCards, false, "exhaust");

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
        
        grid.AddThemeConstantOverride("h_separation", 15);
        grid.AddThemeConstantOverride("v_separation", 15);
        
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
                titleColor = new Color(0.4f, 1f, 0.9f);
            }
            else if (c.IsUpgraded)
            {
                titleColor = new Color(0.4f, 1f, 0.5f);
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
                    Modulate = new Color(0.9f, 0.65f, 1f),
                    TooltipText = fullTooltip
                };
                vbox.AddChild(enchBadge);
            }

            var cardKeywords = GameHelper.GetCardKeywords(c);
            if (cardKeywords != null && cardKeywords.Count > 0)
            {
                var kwHBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
                kwHBox.AddThemeConstantOverride("separation", 2);
                foreach (var kw in cardKeywords)
                {
                    if (kw == MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.None) continue;
                    var kwBadge = new Label
                    {
                        Text = $"[{kw}]",
                        Modulate = GameHelper.GetKeywordBadgeColor(kw),
                        TooltipText = fullTooltip
                    };
                    kwHBox.AddChild(kwBadge);
                }
                vbox.AddChild(kwHBox);
            }

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

                var attrBtn = new Button { Text = "Attrs", TooltipText = "View and toggle attributes (Ethereal, Exhaust, Eternal, Unplayable, etc.)" };
                attrBtn.Pressed += () => ShowAttributePicker(targetCard);
                var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
                enchBtn.Pressed += () => ShowEnchantmentPicker(targetCard);
                actionGrid.AddChild(attrBtn);
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
                var attrBtn = new Button { Text = "Attrs", TooltipText = "View and toggle attributes (Ethereal, Exhaust, Eternal, Unplayable, etc.)" };
                attrBtn.Pressed += () => ShowAttributePicker(targetCard);
                actionGrid.AddChild(upBtn);
                actionGrid.AddChild(attrBtn);

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

                var attrBtn = new Button { Text = "Attrs", TooltipText = "View and toggle attributes (Ethereal, Exhaust, Eternal, Unplayable, etc.)" };
                attrBtn.Pressed += () => ShowAttributePicker(targetCard);
                var enchBtn = new Button { Text = "Enchant", TooltipText = "Apply custom enchantment" };
                enchBtn.Pressed += () => ShowEnchantmentPicker(targetCard);
                actionGrid.AddChild(attrBtn);
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
                var attrBtn = new Button { Text = "Attrs", TooltipText = "View and toggle attributes (Ethereal, Exhaust, Eternal, Unplayable, etc.)" };
                attrBtn.Pressed += () => ShowAttributePicker(targetCard);
                actionGrid.AddChild(upBtn);
                actionGrid.AddChild(attrBtn);

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

    private void ShowAttributePicker(MegaCrit.Sts2.Core.Models.CardModel card)
    {
        string cardTitle = !string.IsNullOrWhiteSpace(card.Title) ? card.Title : card.GetType().Name;
        var dialog = new ConfirmationDialog
        {
            Title = $"Attributes: {cardTitle}",
            Size = new Vector2I(420, 360)
        };

        var vbox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        dialog.AddChild(vbox);

        vbox.AddChild(new Label 
        { 
            Text = "Toggle Card Attributes & Keywords:", 
            Modulate = new Color(0.4f, 0.9f, 1f) 
        });

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 8);
        vbox.AddChild(grid);

        var allKeywords = GameHelper.GetAllCardKeywords();
        var activeKeywords = GameHelper.GetCardKeywords(card);

        var checkBoxes = new System.Collections.Generic.Dictionary<MegaCrit.Sts2.Core.Entities.Cards.CardKeyword, CheckBox>();

        foreach (var kw in allKeywords)
        {
            bool isActive = activeKeywords.Contains(kw);
            var color = GameHelper.GetKeywordBadgeColor(kw);
            var cb = new CheckBox
            {
                Text = $" {kw}",
                ButtonPressed = isActive,
                Modulate = color,
                TooltipText = $"Toggle {kw} attribute on {cardTitle}"
            };
            checkBoxes[kw] = cb;
            grid.AddChild(cb);
        }

        var infoLabel = new Label
        {
            Text = "Active Attributes affect gameplay immediately across deck and combat piles (e.g. Ethereal exhausts at turn end, Innate draws on Turn 1, Eternal prevents exhaust/removal).",
            Modulate = new Color(0.8f, 0.8f, 0.8f, 0.75f),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(380, 60)
        };
        vbox.AddChild(infoLabel);

        dialog.Confirmed += () =>
        {
            foreach (var kvp in checkBoxes)
            {
                var kw = kvp.Key;
                bool shouldBeActive = kvp.Value.ButtonPressed;
                bool isCurrentlyActive = GameHelper.HasCardKeyword(card, kw);

                if (shouldBeActive && !isCurrentlyActive)
                {
                    CardDirector.AddKeyword(card, kw);
                }
                else if (!shouldBeActive && isCurrentlyActive)
                {
                    CardDirector.RemoveKeyword(card, kw);
                }
            }
            RefreshRealTimeCardTabs();
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
            Name = "Player Sandbox", 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        var rootVbox = new VBoxContainer 
        { 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, 
            SizeFlagsVertical = Control.SizeFlags.ExpandFill 
        };
        rootVbox.AddThemeConstantOverride("separation", 14);
        scroll.AddChild(rootVbox);

        var selectedChar = GameHelper.GetSelectedCharacterModel();
        var activePlayer = GameHelper.GetActivePlayer();

        int defaultGold = activePlayer != null ? activePlayer.Gold : (selectedChar != null ? selectedChar.StartingGold : 99);
        int defaultMaxHp = (activePlayer?.Creature != null) ? (int)activePlayer.Creature.MaxHp : (selectedChar != null ? selectedChar.StartingHp : 80);
        int defaultCurrentHp = (activePlayer?.Creature != null) ? (int)activePlayer.Creature.CurrentHp : defaultMaxHp;

        var vitalsBox = new VBoxContainer();
        vitalsBox.AddThemeConstantOverride("separation", 10);

        var goldRow = new HBoxContainer();
        goldRow.AddThemeConstantOverride("separation", 8);
        goldRow.AddChild(new Label { Text = "Gold Amount: ", CustomMinimumSize = new Vector2(140, 0) });
        _goldAmountSpin = new SpinBox { MinValue = 0, MaxValue = 99999, Step = 50, Value = Math.Max(0, defaultGold), CustomMinimumSize = new Vector2(120, 0) };
        var addGoldBtn = new Button { Text = " Add Gold ", CustomMinimumSize = new Vector2(110, 32) };
        addGoldBtn.Pressed += () => InventoryDirector.AddGold((int)_goldAmountSpin.Value);
        var setGoldBtn = new Button { Text = " Set Exact Gold ", CustomMinimumSize = new Vector2(130, 32) };
        setGoldBtn.Pressed += () => InventoryDirector.SetGold((int)_goldAmountSpin.Value);
        goldRow.AddChild(_goldAmountSpin);
        goldRow.AddChild(addGoldBtn);
        goldRow.AddChild(setGoldBtn);
        vitalsBox.AddChild(goldRow);

        var healRow = new HBoxContainer();
        healRow.AddThemeConstantOverride("separation", 8);
        healRow.AddChild(new Label { Text = "Heal Amount: ", CustomMinimumSize = new Vector2(140, 0) });
        _currentHpAmountSpin = new SpinBox { MinValue = 1, MaxValue = 999, Step = 10, Value = defaultCurrentHp, CustomMinimumSize = new Vector2(120, 0) };
        var healBtn = new Button { Text = " Heal Player ", CustomMinimumSize = new Vector2(110, 32) };
        healBtn.Pressed += () => InventoryDirector.Heal((int)_currentHpAmountSpin.Value);
        healRow.AddChild(_currentHpAmountSpin);
        healRow.AddChild(healBtn);
        vitalsBox.AddChild(healRow);

        var damageRow = new HBoxContainer();
        damageRow.AddThemeConstantOverride("separation", 8);
        damageRow.AddChild(new Label { Text = "Damage Amount: ", CustomMinimumSize = new Vector2(140, 0) });
        _damageAmountSpin = new SpinBox { MinValue = 1, MaxValue = 999, Step = 5, Value = 5, CustomMinimumSize = new Vector2(120, 0) };
        var damageBtn = new Button { Text = " Damage Player ", CustomMinimumSize = new Vector2(110, 32) };
        damageBtn.Pressed += () => InventoryDirector.DamagePlayer((int)_damageAmountSpin.Value);
        damageRow.AddChild(_damageAmountSpin);
        damageRow.AddChild(damageBtn);
        vitalsBox.AddChild(damageRow);

        var maxHpRow = new HBoxContainer();
        maxHpRow.AddThemeConstantOverride("separation", 8);
        maxHpRow.AddChild(new Label { Text = "Max HP Amount: ", CustomMinimumSize = new Vector2(140, 0) });
        _maxHpAmountSpin = new SpinBox { MinValue = 1, MaxValue = 999, Step = 10, Value = defaultMaxHp, CustomMinimumSize = new Vector2(120, 0) };
        var maxHpBtn = new Button { Text = " Set Max HP ", CustomMinimumSize = new Vector2(110, 32) };
        maxHpBtn.Pressed += () => InventoryDirector.SetMaxHp((int)_maxHpAmountSpin.Value);
        maxHpRow.AddChild(_maxHpAmountSpin);
        maxHpRow.AddChild(maxHpBtn);
        vitalsBox.AddChild(maxHpRow);

        rootVbox.AddChild(CreateSectionCard("Gold & Health Manipulation", vitalsBox, new Color(0.4f, 0.95f, 0.65f)));

        var eventCardBox = new VBoxContainer();
        eventCardBox.AddThemeConstantOverride("separation", 10);

        var shopRow = new HBoxContainer();
        var openShopBtn = new Button
        {
            Text = " Open Shop Menu Anywhere (Randomized) ",
            CustomMinimumSize = new Vector2(340, 36),
            TooltipText = "Directly transitions and opens a freshly randomized merchant shop room anywhere during a run (cards, relics, potions, card removal)."
        };
        openShopBtn.Pressed += () =>
        {
            if (GameHelper.OpenShopMenu())
            {
                CloseDialog();
            }
        };
        shopRow.AddChild(openShopBtn);
        eventCardBox.AddChild(shopRow);
        
        var indicatorRow = new HBoxContainer();
        indicatorRow.AddThemeConstantOverride("separation", 8);
        indicatorRow.AddChild(new Label { Text = "Current Override: " });
        _eventOverrideLabel = new Label { Text = "None", Modulate = new Color(1f, 0.5f, 0.5f) };
        indicatorRow.AddChild(_eventOverrideLabel);
        
        var clearEventBtn = new Button { Text = " Clear Override " };
        clearEventBtn.Pressed += EventDirector.ClearForcedEvent;
        indicatorRow.AddChild(clearEventBtn);
        eventCardBox.AddChild(indicatorRow);

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
        eventCardBox.AddChild(searchRow);

        var grid = new GridContainer 
        { 
            Columns = 3, 
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        eventCardBox.AddChild(grid);

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

        rootVbox.AddChild(CreateSectionCard("Special Rooms & Event Director", eventCardBox, new Color(1f, 0.88f, 0.4f)));

        return scroll;
    }

    private static HSlider AddSliderControl(Control parent, string labelText, float min, float max, float step, float def)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = labelText, CustomMinimumSize = new Vector2(230, 0) });

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = def,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(160, 0)
        };
        row.AddChild(slider);

        var valLabel = new Label { Text = $" {def:F2}x", CustomMinimumSize = new Vector2(60, 0), HorizontalAlignment = HorizontalAlignment.Right };
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

        if (_mapRoomCountSpin != null)
        {
            _mapRoomCountSpin.Value = tweaks.MapRoomCount;
            if (_mapRoomWarningLabel != null)
            {
                _mapRoomWarningLabel.Visible = tweaks.MapRoomCount > 30;
            }
        }
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
        if (_treasureSlider != null) _treasureSlider.Value = tweaks.MapNodeDistribution.TreasureRoomMultiplier;

        if (_enemyHpSlider != null) _enemyHpSlider.Value = tweaks.EnemyHealthMultiplier;
        if (_enemyDmgSlider != null) _enemyDmgSlider.Value = tweaks.EnemyDamageMultiplier;
        if (_enemyDefSlider != null) _enemyDefSlider.Value = tweaks.EnemyDefendMultiplier;

        if (_playerDmgSlider != null) _playerDmgSlider.Value = tweaks.PlayerDamageMultiplier;
        if (_maxEnergySpin != null) _maxEnergySpin.Value = tweaks.MaxEnergy;

        if (_endlessModeCheck != null) _endlessModeCheck.ButtonPressed = tweaks.EndlessMode.Enabled;
        if (_endlessMultiplierSpin != null) _endlessMultiplierSpin.Value = tweaks.EndlessMode.EnemyScalingMultiplier;
        if (_freeMapNavCheck != null) _freeMapNavCheck.ButtonPressed = tweaks.FreeMapNavigation;

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
    }

    private void SaveSettingsValues()
    {
        var tweaks = ConfigManager.Current.PreRunTweaks;
        var sandbox = ConfigManager.Current.CombatSandbox;
        var general = ConfigManager.Current.General;

        if (_consoleHotkeyInput != null)
        {
            string consoleVal = _consoleHotkeyInput.Text.Trim();
            general.ConsoleHotkey = string.IsNullOrWhiteSpace(consoleVal) ? GeneralConfig.DefaultConsoleHotkey : consoleVal;
        }

        if (_guiHotkeyInput != null)
        {
            string guiVal = _guiHotkeyInput.Text.Trim();
            general.GuiOverlayHotkey = string.IsNullOrWhiteSpace(guiVal) ? GeneralConfig.DefaultGuiOverlayHotkey : guiVal;
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
        if (_treasureSlider != null) tweaks.MapNodeDistribution.TreasureRoomMultiplier = (float)_treasureSlider.Value;

        if (_enemyHpSlider != null) tweaks.EnemyHealthMultiplier = (float)_enemyHpSlider.Value;
        if (_enemyDmgSlider != null) tweaks.EnemyDamageMultiplier = (float)_enemyDmgSlider.Value;
        if (_enemyDefSlider != null) tweaks.EnemyDefendMultiplier = (float)_enemyDefSlider.Value;

        if (_playerDmgSlider != null)
        {
            tweaks.PlayerDamageMultiplier = (float)_playerDmgSlider.Value;
            GameHelper.RefreshAllVisibleCards();
        }
        if (_maxEnergySpin != null)
        {
            int energyVal = (int)_maxEnergySpin.Value;
            tweaks.MaxEnergy = energyVal;
            GameHelper.SetPlayerMaxEnergy(energyVal);
        }

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

        if (_dialogPanel != null)
        {
            ConfigManager.Current.UI.MenuPosX = _dialogPanel.Position.X;
            ConfigManager.Current.UI.MenuPosY = _dialogPanel.Position.Y;
            ConfigManager.Current.UI.MenuWidth = _dialogPanel.Size.X;
            ConfigManager.Current.UI.MenuHeight = _dialogPanel.Size.Y;
        }

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
            case "shop":
            case "openshop":
            case "merchant":
                if (GameHelper.OpenShopMenu())
                {
                    CloseDialog();
                }
                break;
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
