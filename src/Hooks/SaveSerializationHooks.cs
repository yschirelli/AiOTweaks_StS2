using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using HarmonyLib;
using AIOTweaks.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace AIOTweaks.Hooks;

/// <summary>
/// Resolves run save serialization issues where BaseLib and third-party custom save types
/// (such as LinkedRewardSet.SerializableCustomLinkedRewardSet dictionaries) are not exposed
/// to System.Text.Json's TypeInfoResolverChain on JsonSerializationUtility.Options.
/// </summary>
public static class SaveSerializationHooks
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            if (!JsonSerializationUtility.Options.IsReadOnly)
            {
                JsonSerializationUtility.AddTypeInfoResolver(new BaseLibExtendedSaveResolver());
                ModLogger.Info("[SaveSerializationHooks] Successfully registered BaseLibExtendedSaveResolver with JsonSerializationUtility.");
            }
            else
            {
                var isReadOnlyField = typeof(JsonSerializerOptions).GetField("_isReadOnly", BindingFlags.Instance | BindingFlags.NonPublic);
                if (isReadOnlyField != null)
                {
                    isReadOnlyField.SetValue(JsonSerializationUtility.Options, false);
                    JsonSerializationUtility.AddTypeInfoResolver(new BaseLibExtendedSaveResolver());
                    isReadOnlyField.SetValue(JsonSerializationUtility.Options, true);
                    ModLogger.Info("[SaveSerializationHooks] Successfully registered BaseLibExtendedSaveResolver via reflection override.");
                }
                else
                {
                    ModLogger.Verbose("SaveSerializationHooks", "JsonSerializationUtility.Options is read-only. Save serialization resolver handled via BaseLib.");
                }
            }
        }
        catch (Exception ex)
        {
            ModLogger.Debug($"[SaveSerializationHooks] Note: JsonSerializationUtility registration skipped ({ex.Message})");
        }
    }

    /// <summary>
    /// Custom IJsonTypeInfoResolver that delegates type metadata resolution to BaseLib's ExtendedSaveTypes,
    /// enabling custom mod save dictionary entries (e.g. SerializableCustomLinkedRewardSet) to serialize cleanly.
    /// </summary>
    public class BaseLibExtendedSaveResolver : IJsonTypeInfoResolver
    {
        private static readonly Lazy<Dictionary<Type, Func<IJsonTypeInfoResolver, JsonSerializerOptions, JsonTypeInfo>>?> s_extendedTypes = new(() =>
        {
            try
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => {
                        try { return a.GetType("BaseLib.Patches.Saves.ExtendedSaveTypes"); }
                        catch { return null; }
                    })
                    .FirstOrDefault(t => t != null);

                if (type != null)
                {
                    var field = type.GetField("ExtendedTypes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    var dict = field?.GetValue(null) as Dictionary<Type, Func<IJsonTypeInfoResolver, JsonSerializerOptions, JsonTypeInfo>>;
                    if (dict != null)
                    {
                        ModLogger.Info($"[SaveSerializationHooks] Located BaseLib ExtendedSaveTypes dictionary with {dict.Count} registered types.");
                        return dict;
                    }
                }
                ModLogger.Warn("[SaveSerializationHooks] Could not locate BaseLib.Patches.Saves.ExtendedSaveTypes.ExtendedTypes.");
            }
            catch (Exception ex)
            {
                ModLogger.Warn($"[SaveSerializationHooks] Error accessing BaseLib ExtendedSaveTypes: {ex.Message}");
            }
            return null;
        });

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == null)
                return null;

            try
            {
                var extendedTypes = s_extendedTypes.Value;
                if (extendedTypes != null && extendedTypes.TryGetValue(type, out var factory))
                {
                    var info = factory(this, options);
                    if (info != null)
                    {
                        ModLogger.Verbose("SaveSerializationHooks", $"Resolved JsonTypeInfo for '{type.FullName}' via BaseLib ExtendedSaveTypes factory.");
                        return info;
                    }
                }

                if (type.FullName != null && type.FullName.Contains("SerializableCustomLinkedRewardSet"))
                {
                    var fallbackInfo = JsonTypeInfo.CreateJsonTypeInfo(type, options);
                    if (fallbackInfo != null)
                    {
                        ModLogger.Info($"[SaveSerializationHooks] Created fallback JsonTypeInfo for '{type.FullName}' (Kind={fallbackInfo.Kind}).");
                        return fallbackInfo;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error($"[SaveSerializationHooks] Error resolving JsonTypeInfo for '{type.FullName}': {ex.Message}", ex);
            }

            return null;
        }
    }

    /// <summary>
    /// Harmony patch on SaveManager.SaveRun to log high-level save invocation.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
    public static class SaveManager_SaveRun_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(AbstractRoom? preFinishedRoom, bool saveProgress)
        {
            ModLogger.Info($"[SaveSerializationHooks] SaveManager.SaveRun called (room={preFinishedRoom?.GetType().Name ?? "null"}, saveProgress={saveProgress}).");
        }

        [HarmonyPostfix]
        public static void Postfix(Task? __result, AbstractRoom? preFinishedRoom, bool saveProgress)
        {
            if (__result == null) return;
            __result.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    ModLogger.Error("[SaveSerializationHooks] SaveManager.SaveRun failed asynchronously!", t.Exception);
                }
                else if (t.IsCanceled)
                {
                    ModLogger.Warn("[SaveSerializationHooks] SaveManager.SaveRun was canceled.");
                }
                else
                {
                    ModLogger.Info("[SaveSerializationHooks] SaveManager.SaveRun completed successfully.");
                }
            });
        }
    }

    /// <summary>
    /// Harmony patch on RunSaveManager.SaveRun(SerializableRun, bool) to monitor serialization and catch errors.
    /// </summary>
    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), new[] { typeof(SerializableRun), typeof(bool) })]
    public static class RunSaveManager_SaveRun_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(SerializableRun save, bool isMultiplayer)
        {
            ModLogger.Info($"[SaveSerializationHooks] RunSaveManager.SaveRun started (isMultiplayer={isMultiplayer}, floor={save?.FloorReached}, ascension={save?.Ascension}, hasExtraFields={save?.ExtraFields != null}).");
        }

        [HarmonyPostfix]
        public static void Postfix(Task? __result, SerializableRun save, bool isMultiplayer)
        {
            if (__result == null) return;
            string runType = isMultiplayer ? "Multiplayer" : "Singleplayer";
            __result.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    ModLogger.Error($"[SaveSerializationHooks] CRITICAL: RunSaveManager.SaveRun failed asynchronously ({runType})!", t.Exception);
                }
                else if (t.IsCanceled)
                {
                    ModLogger.Warn($"[SaveSerializationHooks] RunSaveManager.SaveRun was canceled ({runType}).");
                }
                else
                {
                    ModLogger.Info($"[SaveSerializationHooks] RunSaveManager.SaveRun completed successfully ({runType}, Floor={save?.FloorReached}). Save file committed.");
                }
            });
        }
    }
}
