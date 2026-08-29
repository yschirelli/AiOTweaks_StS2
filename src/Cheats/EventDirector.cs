using System;
using AIOTweaks.Core;
using AIOTweaks.Core.Logging;
using AIOTweaks.Core.State;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace AIOTweaks.Cheats;

/// <summary>
/// Atomic director for forcing specific narrative events and managing event pools.
/// </summary>
public static class EventDirector
{
    public static event Action<string?>? OnForcedEventChanged;

    public static void ForceImmediateEvent(string eventId)
    {
        ModLogger.Verbose("EventDirector", $"ForceImmediateEvent called: eventId='{eventId}'");
        if (string.IsNullOrWhiteSpace(eventId))
        {
            ModLogger.Warn("Event ID cannot be null or empty.");
            return;
        }
        
        string normalizedId = GameHelper.NormalizeEventId(eventId);
        var eventModel = GameHelper.GetEventModel(eventId);
        ModLogger.Verbose("EventDirector", $"Normalized event ID: '{normalizedId}', EventModel resolved: {eventModel?.GetType().Name ?? "null"}");

        ModLogger.Info($"Immediately forcing event: '{eventId}' (Normalized: '{normalizedId}')");

        // Primary: Native direct room transition
        try
        {
            if (eventModel != null && RunManager.Instance != null && RunManager.Instance.IsInProgress)
            {
                var player = GameHelper.GetActivePlayer();
                ModLogger.Verbose("EventDirector", $"Direct room transition executing: adding map history entry for '{eventModel.Id}'...");
                player?.RunState?.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Unknown, RoomType.Event, eventModel.Id);

                var mutableModel = eventModel.ToMutable() ?? eventModel;
                _ = RunManager.Instance.EnterRoom(new EventRoom(mutableModel));
                ModLogger.Info($"Directly transitioned to event room '{eventModel.Id}'.");
                return;
            }
            else
            {
                ModLogger.Verbose("EventDirector", "RunManager not in progress or eventModel null. Switching to DevConsole fallback.");
            }
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"Direct event room transition notice: {ex.Message}. Falling back to console command.");
        }

        // Secondary fallback: DevConsole command
        ModLogger.Verbose("EventDirector", $"Dispatching DevConsole command: 'event {normalizedId}'");
        GameHelper.ExecuteConsoleCommand($"event {normalizedId}");
    }

    public static void ForceNextEvent(string eventId)
    {
        ModLogger.Verbose("EventDirector", $"ForceNextEvent called: eventId='{eventId}'");
        if (string.IsNullOrWhiteSpace(eventId))
        {
            ModLogger.Warn("Event ID cannot be null or empty.");
            return;
        }

        string normalizedId = GameHelper.NormalizeEventId(eventId);
        RuntimeStateManager.ForcedNextEventId = normalizedId;
        ModLogger.Verbose("EventDirector", $"Set RuntimeStateManager.ForcedNextEventId to '{normalizedId}'");
        ModLogger.Info($"Next event forced to: '{normalizedId}' (original query: '{eventId}')");
        OnForcedEventChanged?.Invoke(normalizedId);
    }

    public static void ClearForcedEvent()
    {
        ModLogger.Verbose("EventDirector", "ClearForcedEvent: clearing forced event override.");
        RuntimeStateManager.ForcedNextEventId = null;
        ModLogger.Info("Forced event override cleared.");
        OnForcedEventChanged?.Invoke(null);
    }

    public static string? GetForcedEvent()
    {
        string? ev = RuntimeStateManager.ForcedNextEventId;
        ModLogger.Verbose("EventDirector", $"GetForcedEvent: returned '{ev ?? "null"}'");
        return ev;
    }

    public static bool TryConsumeForcedEvent(out EventModel? forcedEventModel)
    {
        forcedEventModel = null;
        string? queuedId = RuntimeStateManager.ForcedNextEventId;
        ModLogger.Verbose("EventDirector", $"TryConsumeForcedEvent: queuedId='{queuedId ?? "null"}'");
        if (string.IsNullOrEmpty(queuedId))
        {
            return false;
        }

        var model = GameHelper.GetEventModel(queuedId);
        if (model != null)
        {
            forcedEventModel = model.ToMutable() ?? model;
            ModLogger.Verbose("EventDirector", $"TryConsumeForcedEvent: Successfully resolved and consuming model '{forcedEventModel.Id}'.");
            ClearForcedEvent();
            return true;
        }

        ModLogger.Verbose("EventDirector", $"TryConsumeForcedEvent: Failed to resolve model for '{queuedId}'.");
        return false;
    }
}
