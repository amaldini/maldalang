// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Runtime.Journal;

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Per-call spend attached as <c>.usage</c> metadata on a <see cref="RuntimeValue"/>.
/// </summary>
public sealed class RunUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens => PromptTokens + CompletionTokens;
    public double Cost { get; set; }
    public long Ms { get; set; }
    public int Repairs { get; set; }
    public string? Model { get; set; }

    public RuntimeValue ToRuntimeValue()
    {
        var obj = new JsonObject();
        obj.Set("tokens", RuntimeValue.Integer(TotalTokens));
        obj.Set("promptTokens", RuntimeValue.Integer(PromptTokens));
        obj.Set("completionTokens", RuntimeValue.Integer(CompletionTokens));
        obj.Set("cost", RuntimeValue.Float(Cost));
        obj.Set("ms", RuntimeValue.Integer((int)Math.Min(Ms, int.MaxValue)));
        obj.Set("repairs", RuntimeValue.Integer(Repairs));
        obj.Set("model", RuntimeValue.String(Model ?? ""));
        return RuntimeValue.Object(obj);
    }
}
