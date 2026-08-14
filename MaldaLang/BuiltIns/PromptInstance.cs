// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using MaldaLang.Interpreter;

public class PromptInstance : ObjectInstance
{
    public string? System { get; }
    public string User { get; }
    public string? Model { get; }
    public double? Temperature { get; }
    public List<string>? Tools { get; }
    public List<string>? Gather { get; }
    public int? MaxTokens { get; }
    public RuntimeValue? ResponseFormatSchema { get; }
    public IReadOnlyList<PromptExample>? Examples { get; }
    public int? WithinTimeoutMs { get; }
    public bool HasGather => Gather != null && Gather.Count > 0;
    public bool HasTools => Tools != null && Tools.Count > 0;

    public PromptInstance(
        string? system,
        string user,
        string? model = null,
        double? temperature = null,
        List<string>? tools = null,
        int? maxTokens = null,
        RuntimeValue? responseFormatSchema = null,
        IReadOnlyList<PromptExample>? examples = null,
        int? withinTimeoutMs = null,
        List<string>? gather = null)
        : base(null)
    {
        System = system;
        User = user;
        Model = model;
        Temperature = temperature;
        Tools = tools;
        Gather = gather;
        MaxTokens = maxTokens;
        ResponseFormatSchema = responseFormatSchema;
        Examples = examples;
        WithinTimeoutMs = withinTimeoutMs;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        switch (name)
        {
            case "system":
                return System != null ? RuntimeValue.String(System) : RuntimeValue.Null();
            case "user":
                return RuntimeValue.String(User);
            case "model":
                return Model != null ? RuntimeValue.String(Model) : RuntimeValue.Null();
            case "temperature":
                return Temperature.HasValue ? RuntimeValue.Float(Temperature.Value) : RuntimeValue.Null();
            case "tools":
                return ToStringArray(Tools);
            case "gather":
                return ToStringArray(Gather);
            case "maxTokens":
                return MaxTokens.HasValue ? RuntimeValue.Integer(MaxTokens.Value) : RuntimeValue.Null();
            case "examples":
                return PromptExampleHelpers.ToRuntimeArray(Examples);
            case "toPromptString":
                // Return a FunctionValue for method call
                var wrapper = new FunctionValue(null, null, false, null);
                wrapper.BuiltInInstance = this;
                wrapper.BuiltInMethod = "toPromptString";
                return RuntimeValue.Function(wrapper);
            case "getSystem":
                wrapper = new FunctionValue(null, null, false, null);
                wrapper.BuiltInInstance = this;
                wrapper.BuiltInMethod = "getSystem";
                return RuntimeValue.Function(wrapper);
            case "getUser":
                wrapper = new FunctionValue(null, null, false, null);
                wrapper.BuiltInInstance = this;
                wrapper.BuiltInMethod = "getUser";
                return RuntimeValue.Function(wrapper);
            default:
                throw new Exception($"Undefined property '{name}' on PromptInstance.");
        }
    }
    
    private static RuntimeValue ToStringArray(List<string>? items)
    {
        if (items == null)
            return RuntimeValue.Null();
        var array = new List<RuntimeValue>(items.Count);
        foreach (var item in items)
            array.Add(RuntimeValue.String(item));
        return RuntimeValue.Array(array);
    }

    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter interpreter)
    {
        switch (methodName)
        {
            case "toPromptString":
                return RuntimeValue.String(User);
            case "getSystem":
                return System != null ? RuntimeValue.String(System) : RuntimeValue.Null();
            case "getUser":
                return RuntimeValue.String(User);
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    public override string ToString()
    {
        return $"<prompt instance: user=\"{User.Substring(0, Math.Min(50, User.Length))}...\">";
    }
}
