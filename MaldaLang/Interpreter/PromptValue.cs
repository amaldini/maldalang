// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class PromptValue
{
    public PromptDeclaration Declaration { get; set; }
    public Environment? Closure { get; set; }
    
    public PromptValue(PromptDeclaration declaration, Environment? closure = null)
    {
        Declaration = declaration;
        Closure = closure;
    }
    
    public async Task<RuntimeValue> Call(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        return await BuildPromptInstanceAsync(arguments, interpreter);
    }
    
    public async Task<RuntimeValue> CallAsync(List<RuntimeValue> arguments, Interpreter interpreter)
    {
        return await ExecutePromptAsync(arguments, interpreter);
    }

    private static string? TryExtractResponseContent(RuntimeValue response)
    {
        if (response.Type == ValueType.Object && response.AsObject() is JsonObject jsonObj)
        {
            var contentValue = jsonObj.Get("content");
            if (contentValue.Type == ValueType.String)
                return contentValue.AsString();
        }

        if (response.Type == ValueType.String)
            return response.AsString();

        return null;
    }
    
    public override string ToString()
    {
        return $"<prompt {Declaration.Name}>";
    }
}
