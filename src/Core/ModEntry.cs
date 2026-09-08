using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using AIOTweaks.Core.Config;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using AIOTweaks.Hooks;
using AIOTweaks.UI.Menu;
using AIOTweaks.UI.Overlay;

using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;

namespace AIOTweaks.Core;

/// <summary>
/// Main Mod Entry Point for AIOTweaks in Slay the Spire 2 (Godot Engine C#).
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class ModEntry : Node
{
    public const string ModId = "AIOTweaks";
    public const string ModName = "AIOTweaks";
    public const string ModVersion = "1.1.0";

#if DEBUG
    public const string BuildConfiguration = "DEBUG";
#else
    public const string BuildConfiguration = "RELEASE";
#endif

    public static string GetVersionString()
    {
        try
        {
            var infoVerAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(
                typeof(ModEntry).Assembly,
                typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            if (!string.IsNullOrWhiteSpace(infoVerAttr?.InformationalVersion))
            {
                string ver = infoVerAttr.InformationalVersion.Split('+')[0].Trim();
                if (!string.IsNullOrEmpty(ver))
                    return ver;
            }

            var asmVer = typeof(ModEntry).Assembly.GetName().Version;
            if (asmVer != null && (asmVer.Major > 0 || asmVer.Minor > 0 || asmVer.Build > 0))
            {
                return $"{asmVer.Major}.{asmVer.Minor}.{Math.Max(0, asmVer.Build)}";
            }
        }
        catch
        {
            // Fallback to static constant
        }
        return ModVersion;
    }

    private static ModEntry? _instance;
    public static ModEntry? Instance => _instance;
    private static bool _initialized = false;

    private Harmony? _harmony;
    private DebugConsole? _debugConsoleOverlay;
    private ModSettingsDialog? _modSettingsDialog;

    /// <summary>
    /// Static mod initializer called automatically by Slay the Spire 2's ModManager upon loading the DLL.
    /// </summary>
    public static void Initialize()
    {
        ModLogger.Verbose("ModEntry", "Slay the Spire 2 ModInitializer callback invoked: ModEntry.Initialize().");
        try
        {
            if (_instance != null && GodotObject.IsInstanceValid(_instance))
            {
                ModLogger.Verbose("ModEntry", "ModEntry singleton instance already valid and initialized. Skipping.");
                return;
            }

            _instance = new ModEntry { Name = "AIOTweaksModEntry" };

            // Attach ModEntry node to the running Godot SceneTree root
            if (Engine.GetMainLoop() is SceneTree sceneTree && sceneTree.Root != null)
            {
                sceneTree.Root.CallDeferred("add_child", _instance);
                ModLogger.Info("AIOTweaks ModEntry node registered with Godot SceneTree.");
            }
            else
            {
                ModLogger.Verbose("ModEntry", "SceneTree root not ready; initializing ModEntry directly.");
                _instance.InitializeMod();
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Critical error during AIOTweaks ModEntry.Initialize()", ex);
        }
    }

    public override void _Ready()
    {
        ModLogger.Verbose("ModEntry", "ModEntry._Ready() called by Godot engine.");
        InitializeMod();
    }

    public void InitializeMod()
    {
        if (_initialized)
        {
            ModLogger.Verbose("ModEntry", "ModEntry.InitializeMod() already executed. Skipping redundant call.");
            return;
        }
        _initialized = true;

        try
        {
            ConfigManager.Initialize();
            ModLogger.Info("=========================================");
            ModLogger.Info($"Initializing {ModName} v{ModVersion}...");
#if DEBUG
            ModLogger.Info($"[DEBUG BUILD] Verbose logging forcefully enabled by default.");
            ModLogger.Info($"[DEBUG BUILD] Mod log file: {ModLogger.LogFilePath}");
#endif
            ModLogger.Info("=========================================");

            try
            {
                ModLogger.Verbose("ModEntry", "Registering AIOTweaks with BaseLib ModConfigRegistry...");
                BaseLib.Config.ModConfigRegistry.Register(ModId, new AIOTweaksBaseLibConfig());
                ModLogger.Info("Registered AIOTweaks with BaseLib ModConfigRegistry.");
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"BaseLib ModConfig registration note: {ex.Message}");
            }

            // Register serialization resolver to fix BaseLib and custom mod save serialization
            SaveSerializationHooks.Initialize();

            if (!ConfigManager.Current.General.Enabled)
            {
                ModLogger.Warn($"{ModName} is disabled in configuration.");
                return;
            }

            InitializeHarmony();
            AttachUIComponents();
            RegisterLocalizationStrings();

            try
            {
                LocManager.Instance?.SubscribeToLocaleChange(RegisterLocalizationStrings);
            }
            catch (Exception ex)
            {
                ModLogger.Debug($"LocManager.SubscribeToLocaleChange note: {ex.Message}");
            }

            if (GetTree()?.Root != null)
            {
                GetTree().Root.Connect("child_order_changed", Callable.From(OnSceneChanged));
                ModLogger.Verbose("ModEntry", "Connected to SceneTree.child_order_changed signal.");
            }

            ModLogger.Info($"{ModName} initialized successfully!");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Critical error during {ModName} initialization.", ex);
        }
    }

    private void InitializeHarmony()
    {
        try
        {
            ModLogger.Verbose("ModEntry", $"Creating Harmony instance with ID: '{ModId}'");
            _harmony = new Harmony(ModId);
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            ModLogger.Verbose("ModEntry", $"Scanning and applying Harmony patches from assembly: {assembly.FullName}");

            int successCount = 0;
            int failureCount = 0;

            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0)
                {
                    try
                    {
                        var processor = _harmony.CreateClassProcessor(type);
                        var methods = processor.Patch();
                        if (methods != null && methods.Count > 0)
                        {
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failureCount++;
                        ModLogger.Error($"Failed to apply Harmony patch class '{type.FullName}'", ex);
                    }
                }
            }

            if (failureCount == 0)
            {
                ModLogger.Info($"All {successCount} Harmony patch classes applied successfully.");
            }
            else
            {
                ModLogger.Warn($"Harmony patching completed: {successCount} succeeded, {failureCount} failed.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Error("Harmony initialization encountered a critical issue.", ex);
        }
    }

    private void AttachUIComponents()
    {
        try
        {
            ModLogger.Verbose("ModEntry", "Instantiating DebugConsole overlay...");
            _debugConsoleOverlay = new DebugConsole();
            GetTree()?.Root?.CallDeferred("add_child", _debugConsoleOverlay);

            ModLogger.Verbose("ModEntry", "Instantiating ModSettingsDialog GUI menu overlay...");
            _modSettingsDialog = new ModSettingsDialog();
            GetTree()?.Root?.CallDeferred("add_child", _modSettingsDialog);

            ModLogger.Info("Debug console and GUI menu overlay attached to scene root.");
        }
        catch (Exception ex)
        {
            ModLogger.Error("Failed to attach UI components.", ex);
        }
    }

    private void OnSceneChanged()
    {
        // Safe check for returning to Main Menu or reloading run
        var tree = GetTree();
        if (tree == null || !GodotObject.IsInstanceValid(tree)) return;

        Node? currentScene = tree.CurrentScene;
        if (currentScene != null && GodotObject.IsInstanceValid(currentScene))
        {
            string sceneName = currentScene.Name.ToString();
            ModLogger.Verbose("ModEntry", $"Scene changed detected: '{sceneName}'");
            if (sceneName.Contains("MainMenu") || sceneName.Contains("Title"))
            {
                ModLogger.Verbose("ModEntry", "Main menu scene detected. Resetting session state and hiding dialogs.");
                RuntimeStateManager.ResetSessionState();
                ModSettingsDialog.HideDialog();
            }
        }
    }

    public override void _ExitTree()
    {
        ModLogger.Verbose("ModEntry", "ModEntry._ExitTree() called. Unloading mod...");
        UnloadMod();
    }

    public void UnloadMod()
    {
        ModLogger.Info($"Unloading {ModName}...");

        try
        {
            ModLogger.Verbose("ModEntry", "Unpatching Harmony ID '{ModId}'...");
            _harmony?.UnpatchAll(ModId);

            try
            {
                LocManager.Instance?.UnsubscribeToLocaleChange(RegisterLocalizationStrings);
            }
            catch { }

            ModLogger.Verbose("ModEntry", "Freeing UI overlay nodes...");
            _debugConsoleOverlay?.QueueFree();
            _modSettingsDialog?.QueueFree();

            ModLogger.Verbose("ModEntry", "Resetting runtime session state...");
            RuntimeStateManager.ResetSessionState();
            ModLogger.Info($"{ModName} unloaded cleanly.");
        }
        catch (Exception ex)
        {
            ModLogger.Error($"Error during {ModName} unload.", ex);
        }
    }

    /// <summary>
    /// Registers custom AIOTweaks localization strings into the game's LocManager tables.
    /// Safe to call multiple times or during locale changes.
    /// </summary>
    public static void RegisterLocalizationStrings()
    {
        try
        {
            if (LocManager.Instance != null)
            {
                var customStrings = new Dictionary<string, string>
                {
                    { "AIOTWEAKS_ENDLESS_LEAVE.title", "Continue (Victory)" },
                    { "AIOTWEAKS_ENDLESS_LEAVE.description", "Proceed to the Victory screen." },
                    { "AIOTWEAKS_ENDLESS_PROCEED.title", "(Endless) Continue Run (Loop to Act 1)" },
                    { "AIOTWEAKS_ENDLESS_PROCEED.description", "Loop back to Act 1 with scaled enemies. Keep all progress." }
                };

                var ancientsTable = LocManager.Instance.GetTable("ancients");
                ancientsTable?.MergeWith(customStrings);

                var eventsTable = LocManager.Instance.GetTable("events");
                eventsTable?.MergeWith(customStrings);

                ModLogger.Verbose("ModEntry", "Merged AIOTweaks Endless Mode localization keys into 'ancients' and 'events' LocTables.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"RegisterLocalizationStrings warning: {ex.Message}");
        }
    }
}
