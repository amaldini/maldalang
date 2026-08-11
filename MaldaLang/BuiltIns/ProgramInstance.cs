// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Collections.Generic;
using MaldaLang.Interpreter;

/// <summary>
/// Validated TypeChat-style program produced by <c>await prompt(...) -&gt; program(Api)</c>.
/// </summary>
public sealed class ProgramInstance : ObjectInstance
{
    public sealed class Step
    {
        public Step(string call, List<RuntimeValue> args, string alias)
        {
            Call = call;
            Args = args;
            Alias = alias;
        }

        public string Call { get; }
        public List<RuntimeValue> Args { get; }
        public string Alias { get; }
    }

    public ProgramInstance(string apiName, List<Step> steps, RuntimeValue returnValue)
        : base(null)
    {
        ApiName = apiName;
        Steps = steps;
        ReturnValue = returnValue;
    }

    public string ApiName { get; }
    public IReadOnlyList<Step> Steps { get; }
    public RuntimeValue ReturnValue { get; }

    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        switch (name)
        {
            case "api":
            case "@api":
                return RuntimeValue.String(ApiName);
            case "return":
                return ReturnValue;
            case "steps":
            {
                var list = new List<RuntimeValue>();
                foreach (var step in Steps)
                {
                    var obj = new JsonObject();
                    obj.Set("call", RuntimeValue.String(step.Call));
                    obj.Set("args", RuntimeValue.Array(new List<RuntimeValue>(step.Args)));
                    obj.Set("as", RuntimeValue.String(step.Alias));
                    list.Add(RuntimeValue.Object(obj));
                }

                return RuntimeValue.Array(list);
            }
            default:
                return base.Get(name, accessingClass);
        }
    }
}
