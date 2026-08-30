using System;
using Godot;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;

namespace AIOTweaks.UI.Menu;

/// <summary>
/// Pre-run configuration menu for modifying run parameters, multipliers, and seed/deck presets before embarking.
/// </summary>
public partial class PreRunSettingsMenu : Control
{
    private HSlider? _goldSlider;
    private Label? _goldValLabel;

    private HSlider? _eliteSlider;
    private Label? _eliteValLabel;

    private HSlider? _shopSlider;
    private Label? _shopValLabel;

    private HSlider? _eventSlider;
    private Label? _eventValLabel;

    private SpinBox? _cardRewardSpin;
    private SpinBox? _bonusGoldSpin;
    private SpinBox? _bonusHpSpin;

    public override void _Ready()
    {
        ModLogger.Verbose("PreRunSettingsMenu", "_Ready called: building PreRunSettings UI...");
        SetAnchorsPreset(LayoutPreset.FullRect);
        BuildMenuUI();
        LoadValuesFromConfig();
        ModLogger.Info("PreRunSettingsMenu ready.");
    }

    private void BuildMenuUI()
    {
        var panel = new PanelContainer
        {
            AnchorLeft = 0.1f,
            AnchorTop = 0.1f,
            AnchorRight = 0.9f,
            AnchorBottom = 0.9f
        };
        AddChild(panel);

        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddChild(scroll);

        var vbox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(vbox);

        // Header
        var header = new Label
        {
            Text = "AIOTweaks - Pre-Run Configuration & Modifiers",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        vbox.AddChild(header);
        vbox.AddChild(new HSeparator());

        // Economy Section
        vbox.AddChild(new Label { Text = "--- Economy Tweaks ---", Modulate = new Color(1f, 0.85f, 0.3f) });

        var goldRow = CreateSliderRow("Gold Reward Multiplier:", 0.1f, 5.0f, 0.1f, 1.0f, out _goldSlider, out _goldValLabel);
        vbox.AddChild(goldRow);

        var bonusGoldRow = new HBoxContainer();
        bonusGoldRow.AddChild(new Label { Text = "Starting Gold Bonus: " });
        _bonusGoldSpin = new SpinBox { MinValue = 0, MaxValue = 9999, Step = 25, Value = 0 };
        bonusGoldRow.AddChild(_bonusGoldSpin);
        vbox.AddChild(bonusGoldRow);

        var bonusHpRow = new HBoxContainer();
        bonusHpRow.AddChild(new Label { Text = "Starting Max HP Bonus: " });
        _bonusHpSpin = new SpinBox { MinValue = 0, MaxValue = 500, Step = 5, Value = 0 };
        bonusHpRow.AddChild(_bonusHpSpin);
        vbox.AddChild(bonusHpRow);

        vbox.AddChild(new HSeparator());

        // Map Node Distribution Section
        vbox.AddChild(new Label { Text = "--- Map Node Generation Multipliers ---", Modulate = new Color(0.4f, 1f, 0.6f) });
        var fairPlayNote = new Label
        {
            Text = "Note: Fair Play: Customizing map multipliers automatically marks the run as Seeded/Custom\n   (locks unlocks & achievements). Leave all at 1.0x to proceed as normal standard run.",
            Modulate = new Color(1f, 0.8f, 0.4f)
        };
        vbox.AddChild(fairPlayNote);

        var eliteRow = CreateSliderRow("Elite Encounter Weight:", 0.0f, 5.0f, 0.1f, 1.0f, out _eliteSlider, out _eliteValLabel);
        vbox.AddChild(eliteRow);

        var shopRow = CreateSliderRow("Shop Node Weight:", 0.0f, 5.0f, 0.1f, 1.0f, out _shopSlider, out _shopValLabel);
        vbox.AddChild(shopRow);

        var eventRow = CreateSliderRow("Unknown/Event Node Weight:", 0.0f, 5.0f, 0.1f, 1.0f, out _eventSlider, out _eventValLabel);
        vbox.AddChild(eventRow);

        vbox.AddChild(new HSeparator());

        // Reward Customization
        vbox.AddChild(new Label { Text = "--- Rewards & Deck Tweaks ---", Modulate = new Color(0.7f, 0.5f, 1f) });
        var cardRewardRow = new HBoxContainer();
        cardRewardRow.AddChild(new Label { Text = "Card Choices per Reward: " });
        _cardRewardSpin = new SpinBox { MinValue = 1, MaxValue = 10, Step = 1, Value = 3 };
        cardRewardRow.AddChild(_cardRewardSpin);
        vbox.AddChild(cardRewardRow);

        vbox.AddChild(new HSeparator());

        // Action Buttons (Save, Reset, Close)
        var btnRow = new HBoxContainer();
        var saveBtn = new Button { Text = " Save & Apply " };
        saveBtn.Pressed += OnSavePressed;
        var resetBtn = new Button { Text = " Reset Defaults " };
        resetBtn.Pressed += OnResetPressed;
        var closeBtn = new Button { Text = " Close " };
        closeBtn.Pressed += () => Visible = false;

        btnRow.AddChild(saveBtn);
        btnRow.AddChild(resetBtn);
        btnRow.AddChild(closeBtn);
        vbox.AddChild(btnRow);
    }

    private static HBoxContainer CreateSliderRow(string title, float min, float max, float step, float def, out HSlider slider, out Label valLabel)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = title, CustomMinimumSize = new Vector2(220, 0) });

        slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = def,
            CustomMinimumSize = new Vector2(200, 0)
        };
        row.AddChild(slider);

        valLabel = new Label { Text = $" {def:F1}x" };
        var capturedLabel = valLabel;
        slider.ValueChanged += val => capturedLabel.Text = $" {val:F1}x";
        row.AddChild(valLabel);

        return row;
    }

    private void LoadValuesFromConfig()
    {
        var tweaks = ConfigManager.Current.PreRunTweaks;

        if (_goldSlider != null) _goldSlider.Value = tweaks.GoldRewardMultiplier;
        if (_eliteSlider != null) _eliteSlider.Value = tweaks.MapNodeDistribution.EliteWeightMultiplier;
        if (_shopSlider != null) _shopSlider.Value = tweaks.MapNodeDistribution.ShopWeightMultiplier;
        if (_eventSlider != null) _eventSlider.Value = tweaks.MapNodeDistribution.EventWeightMultiplier;
        if (_cardRewardSpin != null) _cardRewardSpin.Value = tweaks.CardRewardCount;
        if (_bonusGoldSpin != null) _bonusGoldSpin.Value = tweaks.StartingGoldBonus;
        if (_bonusHpSpin != null) _bonusHpSpin.Value = tweaks.StartingMaxHpBonus;
    }

    private void OnSavePressed()
    {
        var tweaks = ConfigManager.Current.PreRunTweaks;

        if (_goldSlider != null) tweaks.GoldRewardMultiplier = (float)_goldSlider.Value;
        if (_eliteSlider != null) tweaks.MapNodeDistribution.EliteWeightMultiplier = (float)_eliteSlider.Value;
        if (_shopSlider != null) tweaks.MapNodeDistribution.ShopWeightMultiplier = (float)_shopSlider.Value;
        if (_eventSlider != null) tweaks.MapNodeDistribution.EventWeightMultiplier = (float)_eventSlider.Value;
        if (_cardRewardSpin != null) tweaks.CardRewardCount = (int)_cardRewardSpin.Value;
        if (_bonusGoldSpin != null) tweaks.StartingGoldBonus = (int)_bonusGoldSpin.Value;
        if (_bonusHpSpin != null) tweaks.StartingMaxHpBonus = (int)_bonusHpSpin.Value;

        ConfigManager.SaveConfig();
        ModLogger.Info("Pre-run settings saved successfully.");
    }

    private void OnResetPressed()
    {
        ConfigManager.Current.PreRunTweaks = new PreRunTweaksConfig();
        ConfigManager.SaveConfig();
        LoadValuesFromConfig();
        ModLogger.Info("Pre-run settings reset to default values.");
    }
}
