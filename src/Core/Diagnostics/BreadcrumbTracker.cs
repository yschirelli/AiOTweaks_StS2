using System;
using System.Collections.Generic;
using System.Text;

namespace AIOTweaks.Core.Diagnostics;

/// <summary>
/// Circular in-memory event buffer that records the last N game actions, hook triggers, and state transitions.
/// When an error, crash, or invariant violation occurs, the buffer dumps a chronological trail.
/// Only active in DEBUG configuration. In RELEASE configuration, all operations are no-ops.
/// </summary>
public static class BreadcrumbTracker
{
#if DEBUG
    private const int MaxBreadcrumbs = 80;
    private static readonly Queue<BreadcrumbEntry> _history = new(MaxBreadcrumbs);
    private static readonly object _lock = new();

    public readonly record struct BreadcrumbEntry(DateTime Timestamp, string Category, string Message);

    public static void Record(string category, string message)
    {
        lock (_lock)
        {
            if (_history.Count >= MaxBreadcrumbs)
            {
                _history.Dequeue();
            }
            _history.Enqueue(new BreadcrumbEntry(DateTime.UtcNow, category, message));
        }
    }

    public static string DumpTrail()
    {
        lock (_lock)
        {
            if (_history.Count == 0)
            {
                return "[BreadcrumbTracker] No recorded events.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== RECENT BREADCRUMB TRAIL (Chronological) ===");
            foreach (var b in _history)
            {
                sb.AppendLine($"[{b.Timestamp:HH:mm:ss.fff}] [{b.Category}] {b.Message}");
            }
            sb.AppendLine("===============================================");
            return sb.ToString();
        }
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _history.Clear();
        }
    }
#else
    public static void Record(string category, string message) { }
    public static string DumpTrail() => string.Empty;
    public static void Clear() { }
#endif
}
