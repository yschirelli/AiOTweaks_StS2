using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using AIOTweaks.Core.Logging;
using AIOTweaks.UI.Menu;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts in-game screens and containers (Modding Screen, Mod Info container, Character Select Screen)
/// to inject AIOTweaks configuration and settings buttons.
/// </summary>
public static class ModdingScreenHooks
{
    private const string ModConfigButtonName = "AIOTweaksModdingScreenConfigBtn";
    private const string InfoConfigButtonName = "AIOTweaksModInfoConfigBtn";
    private const string CharSelectConfigButtonName = "AIOTweaksCharSelectConfigButton";

    #region Modding Screen Patches

    [HarmonyPatch(typeof(NModdingScreen), nameof(NModdingScreen.OnSubmenuOpened))]
    public static class NModdingScreenOnSubmenuOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NModdingScreen __instance)
        {
            try
            {
                if (__instance == null || !GodotObject.IsInstanceValid(__instance)) return;

                ModLogger.Verbose("ModdingScreenHooks", "NModdingScreen.OnSubmenuOpened Postfix triggered.");
                if (__instance.FindChild(ModConfigButtonName, true, false) == null)
                {
                    var configBtn = new Button
                    {
                        Name = ModConfigButtonName,
                        Text = "AIOTweaks Mod Settings",
                        Modulate = new Color(0.35f, 0.85f, 1f),
                        CustomMinimumSize = new Vector2(230, 44),
                        SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd
                    };

                    configBtn.Pressed += () =>
                    {
                        ModLogger.Verbose("ModdingScreenHooks", "Modding Screen 'AIOTweaks Mod Settings' clicked. Opening ModSettingsDialog...");
                        ModLogger.Info("Modding Screen: 'AIOTweaks Mod Settings' clicked.");
                        ModSettingsDialog.ShowDialog();
                    };

                    var margin = new MarginContainer { Name = "AIOTweaksModdingScreenMargin" };
                    margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                    margin.MouseFilter = Control.MouseFilterEnum.Ignore;
                    margin.AddThemeConstantOverride("margin_right", 250);
                    margin.AddThemeConstantOverride("margin_top", 20);
                    margin.AddChild(configBtn);

                    __instance.AddChild(margin);

                    ModLogger.Info("Injected 'AIOTweaks Mod Settings' button into NModdingScreen.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Failed to inject AIOTweaks button into NModdingScreen.", ex);
            }
        }
    }

    [HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
    public static class NModInfoContainerFillPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NModInfoContainer __instance, Mod mod)
        {
            try
            {
                if (__instance == null || !GodotObject.IsInstanceValid(__instance) || mod == null) return;

                bool isAioTweaks = string.Equals(mod.manifest?.id, "AIOTweaks", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(mod.manifest?.name, "AIOTweaks", StringComparison.OrdinalIgnoreCase);

                ModLogger.Verbose("ModdingScreenHooks", $"NModInfoContainer.Fill for mod '{mod.manifest?.id ?? mod.manifest?.name}'. IsAIOTweaks={isAioTweaks}");

                var existingBtn = __instance.FindChild(InfoConfigButtonName, true, false);

                if (isAioTweaks)
                {
                    if (existingBtn == null)
                    {
                        var configBtn = new Button
                        {
                            Name = InfoConfigButtonName,
                            Text = "Open AIOTweaks Configuration",
                            Modulate = new Color(0.3f, 0.9f, 1f),
                            CustomMinimumSize = new Vector2(260, 48),
                            ZIndex = 50
                        };

                        configBtn.Pressed += () =>
                        {
                            ModLogger.Verbose("ModdingScreenHooks", "Mod Info Container 'Open AIOTweaks Configuration' clicked. Opening dialog...");
                            ModLogger.Info("Mod Info Container: 'Open AIOTweaks Configuration' clicked.");
                            ModSettingsDialog.ShowDialog();
                        };

                        var margin = new MarginContainer { Name = "AIOTweaksInfoConfigMargin", ZIndex = 50 };
                        margin.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
                        margin.Position = new Vector2(-280, -60);
                        margin.AddChild(configBtn);

                        __instance.AddChild(margin);

                        ModLogger.Info("Injected 'Open AIOTweaks Configuration' button into NModInfoContainer.");
                    }
                    else
                    {
                        ((Control)existingBtn).Visible = true;
                    }
                }
                else
                {
                    if (existingBtn != null)
                    {
                        ((Control)existingBtn).Visible = false;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error("Failed to handle NModInfoContainer.Fill patch.", ex);
            }
        }
    }

    #endregion

    #region Character Select Screen Patches

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen._Ready))]
    public static class NCharacterSelectScreenReadyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance)
        {
            ModLogger.Verbose("ModdingScreenHooks", "NCharacterSelectScreen._Ready Postfix triggered.");
            InjectCharacterSelectButton(__instance);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
    public static class NCharacterSelectScreenOnSubmenuOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance)
        {
            ModLogger.Verbose("ModdingScreenHooks", "NCharacterSelectScreen.OnSubmenuOpened Postfix triggered.");
            InjectCharacterSelectButton(__instance);
        }
    }

    [HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.InitializeSingleplayer))]
    public static class NCharacterSelectScreenInitializeSingleplayerPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NCharacterSelectScreen __instance)
        {
            ModLogger.Verbose("ModdingScreenHooks", "NCharacterSelectScreen.InitializeSingleplayer Postfix triggered.");
            InjectCharacterSelectButton(__instance);
        }
    }

    public static void InjectCharacterSelectButton(NCharacterSelectScreen charSelectNode)
    {
        if (charSelectNode == null || !GodotObject.IsInstanceValid(charSelectNode)) return;

        try
        {
            if (charSelectNode.FindChild(CharSelectConfigButtonName, true, false) != null)
            {
                ModLogger.Verbose("ModdingScreenHooks", "InjectCharacterSelectButton: ModConfigButton already present in tree.");
                return;
            }

            var buttonContainer = new PanelContainer
            {
                Name = CharSelectConfigButtonName,
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
                ModLogger.Verbose("ModdingScreenHooks", "Character Select 'AIOTweaks' button clicked. Opening ModSettingsDialog...");
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

    #endregion
}
