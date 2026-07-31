// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using System.Collections.Concurrent;

public static class UiSessionRegistry
{
    private static readonly ConcurrentDictionary<string, UiSession> Sessions = new(StringComparer.Ordinal);
    private static TimeSpan _sessionTtl = TimeSpan.FromMinutes(30);

    public static UiSession GetOrCreate(string sessionId)
    {
        PruneExpiredSessions();
        return Sessions.GetOrAdd(sessionId, static id => new UiSession(id));
    }

    public static void Remove(string sessionId)
    {
        if (Sessions.TryRemove(sessionId, out var removedSession))
        {
            removedSession.DisposeTrackedComponents();
        }
    }

    public static void Clear()
    {
        foreach (var session in Sessions.Values)
        {
            session.DisposeTrackedComponents();
        }

        Sessions.Clear();
    }

    public static void ConfigureTtl(TimeSpan ttl)
    {
        if (ttl < TimeSpan.FromMinutes(1))
        {
            throw new Exception("UI session TTL must be at least 1 minute.");
        }

        _sessionTtl = ttl;
    }

    public static int PruneExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var kvp in Sessions)
        {
            if (now - kvp.Value.LastAccessUtc > _sessionTtl)
            {
                if (Sessions.TryRemove(kvp.Key, out var removedSession))
                {
                    removedSession.DisposeTrackedComponents();
                    removed++;
                }
            }
        }

        return removed;
    }
}
