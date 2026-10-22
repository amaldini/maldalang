// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.UI;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

public sealed class UiEvent
{
    public string Type { get; }
    public string TargetPath { get; }
    public RuntimeValue Payload { get; }
    public DateTime CreatedUtc { get; }

    /// <summary>The browser's id for the event (its <c>envelopeId</c>); a render after pulling it answers it.</summary>
    public string? EventId { get; }

    /// <summary>
    /// The <c>type</c> of the node the user acted on, or null when the sender did not say (lifecycle
    /// events, most <c>ui.dispatchEvent</c> calls). With it, the session drops the event once the
    /// node at <see cref="TargetPath"/> has another type or <see cref="TargetKey"/>.
    /// </summary>
    public string? TargetType { get; }

    /// <summary>The <c>key</c> of that node; null for an unkeyed node.</summary>
    public string? TargetKey { get; }

    public UiEvent(string type, string targetPath, RuntimeValue payload, string? eventId = null, string? targetType = null, string? targetKey = null)
    {
        Type = type;
        TargetPath = targetPath;
        Payload = payload;
        CreatedUtc = DateTime.UtcNow;
        EventId = eventId;
        TargetType = targetType;
        TargetKey = string.IsNullOrWhiteSpace(targetKey) ? null : targetKey;
    }

    public RuntimeValue ToRuntimeValue()
    {
        var obj = new JsonObject();
        obj.Set("type", RuntimeValue.String(Type));
        obj.Set("targetPath", RuntimeValue.String(TargetPath));
        if (TargetKey != null)
            obj.Set("targetKey", RuntimeValue.String(TargetKey));
        obj.Set("payload", Payload);
        obj.Set("createdUtc", RuntimeValue.String(CreatedUtc.ToString("O")));
        return RuntimeValue.Object(obj);
    }
}
