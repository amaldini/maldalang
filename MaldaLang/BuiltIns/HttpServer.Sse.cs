// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    /// <summary>
    /// Writes an SSE message to a specific connection. Used by built-in functions.
    /// </summary>
    public static void WriteSSEMessage(string connectionId, string data)
    {
        var message = $"data: {data}\n\n";
        var bytes = Encoding.UTF8.GetBytes(message);
        lock (_sseConnectionsLock)
        {
            if (_sseConnections.TryGetValue(connectionId, out var connection))
            {
                if (!connection.TryWrite(bytes))
                {
                    _sseConnections.Remove(connectionId);
                }
            }
        }
    }
    
    /// <summary>
    /// Broadcasts an SSE message to all connected clients.
    /// </summary>
    public static void BroadcastSSEMessage(string data)
    {
        BroadcastSSEMessage(data, null);
    }

    /// <summary>
    /// Broadcasts an SSE message to all connected clients, optionally filtered by channel.
    /// </summary>
    public static void BroadcastSSEMessage(string data, string? channel)
    {
        var message = $"data: {data}\n\n";
        var bytes = Encoding.UTF8.GetBytes(message);
        List<KeyValuePair<string, SseConnection>> targets;
        lock (_sseConnectionsLock)
        {
            targets = _sseConnections
                .Where(kvp => ConnectionMatchesChannel(kvp.Value, channel))
                .ToList();
        }

        var toRemove = new List<string>();
        foreach (var kvp in targets)
        {
            if (!kvp.Value.TryWrite(bytes))
            {
                toRemove.Add(kvp.Key);
            }
        }

        if (toRemove.Count == 0)
        {
            return;
        }

        lock (_sseConnectionsLock)
        {
            foreach (var id in toRemove)
            {
                _sseConnections.Remove(id);
            }
        }
    }

    private static bool ConnectionMatchesChannel(SseConnection connection, string? channel)
    {
        // Empty channel filter means broadcast to all.
        if (string.IsNullOrWhiteSpace(channel))
        {
            return true;
        }

        // Empty subscription list means wildcard subscription.
        if (connection.Channels.Count == 0)
        {
            return true;
        }

        return connection.Channels.Contains(channel);
    }

    private static HashSet<string> ParseSseChannels(Dictionary<string, string> queryParams)
    {
        var channels = new HashSet<string>(StringComparer.Ordinal);
        if (!queryParams.TryGetValue("channel", out var channelValue) || string.IsNullOrWhiteSpace(channelValue))
        {
            return channels;
        }

        var values = channelValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                channels.Add(value);
            }
        }

        return channels;
    }
}
