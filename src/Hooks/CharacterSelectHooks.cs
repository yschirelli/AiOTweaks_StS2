using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using AIOTweaks.Core.Logging;
using AIOTweaks.UI.Menu;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts NCharacterSelectScreen to inject the AiOTweaks Configuration & Pre-Run Tweaks button.
/// </summary>
public static class CharacterSelectHooks
{
    private const string ModConfigButtonName = "AIOTweaksCharSelectConfigButton";

    public static void ApplyPatches(Harmony harmony)
    {
        ModLogger.Verbose("CharacterSelectHooks", "Applying CharacterSelectHooks Harmony patches...");
        try
        {
            harmony.CreateClassProcessor(typeof(CharacterSelectHooks)).Patch();
            ModLogger.Info("CharacterSelectHooks Harmony patches applied successfully.");
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"CharacterSelectHooks patch note: {ex.Message}");
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Ready))]
    public static class NCharacterSelectScreenReadyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance)
        {
            ModLogger.Verbose("CharacterSelectHooks", "NCharacterSelectScreen._Ready Postfix triggered.");
            InjectButton(__instance);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    public static class NCharacterSelectScreenOnSubmenuOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance)
        {
            ModLogger.Verbose("CharacterSelectHooks", "NCharacterSelectScreen.OnSubmenuOpened Postfix triggered.");
            InjectButton(__instance);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
    public static class NCharacterSelectScreenInitializeSingleplayerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance)
        {
            ModLogger.Verbose("CharacterSelectHooks", "NCharacterSelectScreen.InitializeSingleplayer Postfix triggered.");
            InjectButton(__instance);
        }
    }

    public static void InjectButton(NCharacterSelectScreen charSelectNode)
    {
        if (charSelectNode == null || !GodotObject.IsInstanceValid(charSelectNode)) return;

        try
        {
            if (charSelectNode.FindChild(ModConfigButtonName, true, false) != null)
            {
                ModLogger.Verbose("CharacterSelectHooks", "InjectButton: ModConfigButton already present in tree.");
                return;
            }

            var buttonContainer = new PanelContainer
            {
                Name = ModConfigButtonName,
                ZIndex = 100,
                MouseFilter = Control.MouseFilterEnum.Pass
            };

            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.08f, 0.12f, 0.20f, 0.90f),
                BorderColor = new Color(0.35f, 0.85f, 1f, 0.8f),
                BorderWidthBottom = 2,
                BorderWidthTop = 2,
                BorderWidthLeft = 2,
                BorderWidthRight = 2,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6
            };
            buttonContainer.AddThemeStyleboxOverride("panel", style);

            var configBtn = new Button
            {
                Name = "AIOTweaksBtn",
                Text = "AIOTweaks",
                TooltipText = "Open AIOTweaks Mod Settings & Pre-Run Tweaks",
                Modulate = new Color(0.35f, 0.85f, 1f),
                CustomMinimumSize = new Vector2(180, 46)
            };

            configBtn.Pressed += () =>
            {
                ModLogger.Verbose("CharacterSelectHooks", "Character Select 'AIOTweaks' button clicked. Opening ModSettingsDialog...");
                ModLogger.Info("Character Select: 'AIOTweaks' clicked.");
                ModSettingsDialog.ShowDialog();
            };

            buttonContainer.AddChild(configBtn);
            charSelectNode.AddChild(buttonContainer);

            // Anchor to top right
            buttonContainer.SetAnchorsPreset(Control.LayoutPreset.TopRight);
            buttonContainer.Position = new Vector2(-220, 24);

            ModLogger.Info("Injected 'AIOTweaks' button into NCharacterSelectScreen.");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to inject AIOTweaks button into Character Select Screen.", ex);
        }
    }
}
