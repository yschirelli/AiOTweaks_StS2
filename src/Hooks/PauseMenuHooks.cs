using System;
using System.Linq;
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
/// cleanly handles active vs. finished room states, protects death screen runs from being saved into stuck states,
/// prevents in-progress rooms from corrupting determinism, and validates Main Menu button states.
/// </summary>
public static class PauseMenuHooks
{
    private static readonly FieldInfo? ShouldSaveBackingField = typeof(RunManager).GetField(
        "<ShouldSave>k__BackingField",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Flag indicating that a Save & Quit transition is currently in progress.
    /// Used to prevent clean-up or abandon hooks from inadvertently wiping save files during normal in-run quits.
    /// </summary>
    public static bool IsSavingAndQuitting { get; private set; }

    private static bool _isCleaningDeadRun = false;

    /// <summary>
    /// Harmony prefix patch on NPauseMenu.CloseToMenu.
    /// Intercepts CloseToMenu to handle pre-finished rooms, avoid saving dead/game-over runs,
    /// preserve pristine start-of-floor saves for in-progress rooms, and cleanly return to the main menu.
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
        try
        {
            ModLogger.Info("[PauseMenuHooks] 'Save and Quit' triggered. Intercepting CloseToMenu...");

            // 1. Disable pause menu buttons to prevent double interactions
            DisablePauseMenuButtons(menu);

            // 2. Stop in-game music and debug audio
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

            // 3. Check if the run has ended, player is dead, or game is over
            bool isGameOverOrDead = runManager == null ||
                                    runState == null ||
                                    !runManager.IsInProgress ||
                                    runState.IsGameOver ||
                                    (runState.Players != null && runState.Players.Count > 0 && runState.Players.All(p => p.Creature == null || p.Creature.IsDead)) ||
                                    (runState.CurrentRoom != null && runState.CurrentRoom.IsVictoryRoom);

            if (isGameOverOrDead)
            {
                ModLogger.Info("[PauseMenuHooks] Run is over or player is dead. Deleting save and active snapshot instead of saving to avoid stuck screen.");
                IsSavingAndQuitting = false;
                try
                {
                    MapGenerationHooks.ForceDeleteCurrentRunSaves();
                    RunTweaksSaveManager.ClearActiveRun("GameOverOnSaveAndQuit");
                }
                catch (Exception ex)
                {
                    ModLogger.Warn($"[PauseMenuHooks] Error clearing dead run save: {ex.Message}");
                }

                if (NGame.Instance != null)
                {
                    await NGame.Instance.ReturnToMainMenu();
                    ModLogger.Info("[PauseMenuHooks] Successfully returned to main menu after dead run cleanup.");
                }
                return;
            }

            // 4. Run is alive and in progress
            IsSavingAndQuitting = true;
            try
            {
                AbstractRoom? currentRoom = runState?.CurrentRoom;

                // In STS2, only legitimately pre-finished rooms (e.g. combat rewards screen or completed event)
                // can and should be saved with preFinishedRoom.
                // If a room is currently in progress (active combat, active event choice, unknown room, shop, rest site),
                // the base game ALREADY committed a pristine start-of-floor save to disk upon entering the floor.
                // Overwriting it with preFinishedRoom = null corrupts the save by writing post-enter counters and consumed RNG,
                // causing unknown rooms, events, and monster encounters to change upon re-entering.
                if (currentRoom != null && currentRoom.IsPreFinished && (currentRoom is CombatRoom || currentRoom is EventRoom))
                {
                    ModLogger.Info($"[PauseMenuHooks] Saving pre-finished room ({currentRoom.GetType().Name})...");
                    try
                    {
                        await SaveManager.Instance.SaveRun(currentRoom, saveProgress: true);
                        ModLogger.Info("[PauseMenuHooks] SaveManager.Instance.SaveRun finished successfully.");
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Error("[PauseMenuHooks] Exception occurred while saving pre-finished room!", ex);
                    }
                }
                else
                {
                    ModLogger.Info($"[PauseMenuHooks] Room is in-progress ({currentRoom?.GetType().Name ?? "null"}). Preserving pristine start-of-floor save without overwriting.");
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

                // 5. Return to main menu
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
        catch (Exception ex)
        {
            ModLogger.Error("[PauseMenuHooks] Exception in SafeCloseToMenuAsync!", ex);
            IsSavingAndQuitting = false;
            if (NGame.Instance != null)
            {
                await NGame.Instance.ReturnToMainMenu();
            }
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
    /// Harmony postfix on NMainMenu.RefreshButtons to log diagnostic state of the Continue button
    /// and automatically heal corrupt dead runs (0 HP) saved by previous bugs so the player never gets stuck.
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

                if (hasRunSave && !_isCleaningDeadRun)
                {
                    var readResult = SaveManager.Instance.LoadRunSave();
                    if (readResult?.Success == true && readResult.SaveData?.Players != null && readResult.SaveData.Players.Count > 0)
                    {
                        if (readResult.SaveData.Players.All(p => p.CurrentHp <= 0))
                        {
                            ModLogger.Warn("[PauseMenuHooks] Detected saved dead run with 0 HP on main menu! Cleaning up corrupt save...");
                            _isCleaningDeadRun = true;
                            try
                            {
                                MapGenerationHooks.ForceDeleteCurrentRunSaves();
                                RunTweaksSaveManager.ClearActiveRun("StaleDeadRunHealedOnMainMenu");
                                __instance.RefreshButtons();
                            }
                            finally
                            {
                                _isCleaningDeadRun = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[PauseMenuHooks] RefreshButtons diagnostics error: {ex.Message}");
            }
        }
    }
}
