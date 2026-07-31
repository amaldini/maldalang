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

    public UiEvent(string type, string targetPath, RuntimeValue payload)
    {
        Type = type;
        TargetPath = targetPath;
        Payload = payload;
        CreatedUtc = DateTime.UtcNow;
    }

    public RuntimeValue ToRuntimeValue()
    {
        var obj = new JsonObject();
        obj.Set("type", RuntimeValue.String(Type));
        obj.Set("targetPath", RuntimeValue.String(TargetPath));
        obj.Set("payload", Payload);
        obj.Set("createdUtc", RuntimeValue.String(CreatedUtc.ToString("O")));
        return RuntimeValue.Object(obj);
    }
}
