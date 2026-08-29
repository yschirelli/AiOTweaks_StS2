using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using AIOTweaks.Core.Logging;
using AIOTweaks.UI.Menu;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace AIOTweaks.Hooks;

/// <summary>
/// Intercepts the in-game Modding Screen and Mod Info container to inject configuration buttons.
/// </summary>
public static class ModdingScreenHooks
{
    private const string ModConfigButtonName = "AIOTweaksModdingScreenConfigBtn";
    private const string InfoConfigButtonName = "AIOTweaksModInfoConfigBtn";

    public static void ApplyPatches(Harmony harmony)
    {
        ModLogger.Verbose("ModdingScreenHooks", "Applying ModdingScreenHooks Harmony patches...");
        try
        {
            harmony.CreateClassProcessor(typeof(ModdingScreenHooks)).Patch();
            ModLogger.Info("ModdingScreenHooks Harmony patches applied successfully.");
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"ModdingScreenHooks patch note: {ex.Message}");
        }
    }

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
                        Text = "⚙ AIOTweaks Mod Settings",
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

                    __instance.CallDeferred("add_child", margin);

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
                            Text = "⚙ Open AIOTweaks Configuration",
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
}
