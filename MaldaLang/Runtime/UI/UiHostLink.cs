// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.UIHost;

/// <summary>
/// Joins the program's UI sessions to the in-process UI host
/// (<see cref="EmbeddedUiHostRuntime"/>). Once <see cref="StartAsync"/> succeeded, every
/// <c>ui.mount</c> / <c>ui.render</c> of a <see cref="UiSessionRegistry"/> session reaches the
/// browsers on that session, and browser events queue for <c>ui.pullEvent</c>.
/// <para>
/// The link lives here and not in <c>MaldaLang.UIHost</c> because <c>MaldaLang</c> references the
/// host, not the other way round. The CLI, the Desktop IDE and transpiled programs call
/// <see cref="StartAsync"/> instead of starting the host themselves.
/// </para>
/// </summary>
public static class UiHostLink
{
    private static int _attached;

    /// <summary>True once the embedded host runs and sessions publish to it.</summary>
    public static bool IsAttached => Volatile.Read(ref _attached) == 1;

    /// <summary>Starts the embedded host if needed and links the sessions to it. False when the host did not start.</summary>
    public static async Task<bool> StartAsync()
    {
        if (!await EmbeddedUiHostRuntime.TryStartAsync())
            return false;

        if (Interlocked.Exchange(ref _attached, 1) == 0)
            EmbeddedUiHostRuntime.Hub.ClientEvent += OnClientEvent;
        return true;
    }

    /// <summary>The <see cref="UiSession"/> publish hook: hands a mount / patch envelope to the host's hub.</summary>
    public static void Publish(string sessionId, string kind, string envelopeJson)
    {
        if (!IsAttached)
            return;

        var error = EmbeddedUiHostRuntime.Hub.Publish(sessionId, kind, envelopeJson);
        if (error != null)
            throw new InvalidOperationException($"UI host refused the {kind} envelope of session '{sessionId}': {error}");
    }

    /// <summary>
    /// A browser <c>event</c> message (<c>eventType</c>, <c>targetPath</c>, <c>payload</c>,
    /// <c>envelopeId</c>, <c>targetType</c>, <c>targetKey</c>) becomes a queued <see cref="UiEvent"/>.
    /// </summary>
    private static void OnClientEvent(string sessionId, System.Text.Json.Nodes.JsonObject message)
    {
        using var document = JsonDocument.Parse(message.ToJsonString());
        if (BuiltInFunctions.JsonToRuntimeValue(document.RootElement).AsObject() is not JsonObject received)
            return;

        var evtObject = new JsonObject();
        evtObject.Set("type", received.Get("eventType", null));
        evtObject.Set("targetPath", received.Get("targetPath", null));
        evtObject.Set("payload", received.Get("payload", null));
        evtObject.Set("eventId", received.Get("envelopeId", null));
        evtObject.Set("targetType", received.Get("targetType", null));
        evtObject.Set("targetKey", received.Get("targetKey", null));
        BuiltInFunctions.EnqueueUiEvent(UiSessionRegistry.GetOrCreate(sessionId), evtObject);
    }
}
