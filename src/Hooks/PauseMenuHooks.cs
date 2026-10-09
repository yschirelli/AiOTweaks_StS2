using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace AIOTweaks.Hooks;

/// <summary>
/// Hooks into the in-game Pause Menu (NPauseMenu) to ensure that clicking 'Save and Quit'
/// explicitly triggers and awaits a full run save before transitioning back to the main menu.
/// Also protects save files from premature deletion and validates Main Menu button states.
/// </summary>
public static class PauseMenuHooks
{
    private static readonly FieldInfo? ShouldSaveBackingField = typeof(RunManager).GetField(
        "<ShouldSave>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Flag indicating that a Save & Quit transition is currently in progress.
    /// Used to prevent clean-up or abandon hooks from inadvertently wiping save files.
    /// </summary>
    public static bool IsSavingAndQuitting { get; private set; }

    /// <summary>
    /// Harmony prefix patch on NPauseMenu.CloseToMenu.
    /// The vanilla CloseToMenu method only stops music and immediately calls ReturnToMainMenu(),
    /// which runs RunManager.CleanUp() (ShouldSave = false) WITHOUT ever calling SaveManager.SaveRun().
    /// We intercept CloseToMenu to explicitly flush and await SaveRun, save the active tweaks snapshot,
    /// and then cleanly return to the main menu.
    /// </summary>
    [HarmonyPatch(typeof(NPauseMenu), "CloseToMenu")]
    public static class NPauseMenu_CloseToMenu_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(NPauseMenu __instance, ref Task __result)
        {
            __result = SafeCloseToMenuAsync(__instance);
            return false;
        }
    }

    private static async Task SafeCloseToMenuAsync(NPauseMenu menu)
    {
        IsSavingAndQuitting = true;
        try
        {
            ModLogger.Info("[PauseMenuHooks] 'Save and Quit' triggered. Intercepting CloseToMenu...");

            // Disable pause menu buttons to prevent double interactions
            DisablePauseMenuButtons(menu);

            try
            {
                NRunMusicController.Instance?.StopMusic();
                NDebugAudioManager.Instance?.StopAll();
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[PauseMenuHooks] Non-fatal audio stop warning: {ex.Message}");
            }

            var runManager = RunManager.Instance;
            var runState = MapGenerationHooks.GetRunState(runManager);

            if (runManager != null && runState != null)
            {
                // Ensure ShouldSave is true before invoking SaveRun
                if (!runManager.ShouldSave && ShouldSaveBackingField != null)
                {
                    ShouldSaveBackingField.SetValue(runManager, true);
                }

                AbstractRoom? currentRoom = runState.CurrentRoom;
                // Only pass preFinishedRoom if the current room was already pre-finished (e.g. rewards screen or finished event).
                // During combat, passing null resets the combat encounter on reload, matching base game behavior.
                AbstractRoom? preFinishedRoom = (currentRoom != null && currentRoom.IsPreFinished) ? currentRoom : null;

                ModLogger.Info($"[PauseMenuHooks] Saving active run... (CurrentRoom: {currentRoom?.GetType().Name ?? "null"}, PreFinished: {preFinishedRoom != null})");
                try
                {
                    await SaveManager.Instance.SaveRun(preFinishedRoom, saveProgress: true);
                    ModLogger.Info("[PauseMenuHooks] SaveManager.Instance.SaveRun finished successfully.");
                }
                catch (Exception ex)
                {
                    ModLogger.Error("[PauseMenuHooks] Exception occurred while awaiting SaveManager.Instance.SaveRun!", ex);
                }

                try
                {
                    RunTweaksSaveManager.SaveActiveSnapshot();
                    ModLogger.Info("[PauseMenuHooks] Active run tweaks snapshot saved to disk.");
                }
                catch (Exception ex)
                {
                    ModLogger.Error("[PauseMenuHooks] Exception occurred while saving active run tweaks snapshot!", ex);
                }
            }
            else
            {
                ModLogger.Warn($"[PauseMenuHooks] Run cannot be saved: RunManager={runManager != null}, RunState={runState != null}");
            }

            ModLogger.Info("[PauseMenuHooks] Returning to main menu...");
            if (NGame.Instance != null)
            {
                await NGame.Instance.ReturnToMainMenu();
                ModLogger.Info("[PauseMenuHooks] Successfully returned to main menu.");
            }
            else
            {
                ModLogger.Error("[PauseMenuHooks] NGame.Instance is null when attempting to ReturnToMainMenu!");
            }
        }
        finally
        {
            IsSavingAndQuitting = false;
        }
    }

    private static void DisablePauseMenuButtons(NPauseMenu menu)
    {
        if (menu == null) return;
        try
        {
            string[] buttonFields =
            [
                "_resumeButton",
                "_settingsButton",
                "_compendiumButton",
                "_giveUpButton",
                "_disconnectButton",
                "_saveAndQuitButton",
                "_backButton"
            ];

            foreach (var fieldName in buttonFields)
            {
                var field = typeof(NPauseMenu).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field?.GetValue(menu) is NButton btn)
                {
                    btn.Disable();
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"[PauseMenuHooks] Error disabling pause menu buttons: {ex.Message}");
        }
    }

    /// <summary>
    /// Harmony postfix on NMainMenu.RefreshButtons to log diagnostic state of the Continue button.
    /// </summary>
    [HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu.RefreshButtons))]
    public static class NMainMenu_RefreshButtons_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(NMainMenu __instance)
        {
            try
            {
                bool hasRunSave = SaveManager.Instance.HasRunSave;
                int profileId = SaveManager.Instance.IsProfileInitialized ? SaveManager.Instance.CurrentProfileId : -1;
                ModLogger.Info($"[PauseMenuHooks] NMainMenu.RefreshButtons: Profile={profileId}, HasRunSave={hasRunSave}");
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[PauseMenuHooks] RefreshButtons diagnostics error: {ex.Message}");
            }
        }
    }
}
