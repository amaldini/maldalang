// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public class AnsiConsoleInstance : ObjectInstance
{
    // Event fired when AnsiConsole is used - allows IDE to open a console window
    public static event Action? OnAnsiConsoleUsed;
    
    public AnsiConsoleInstance() : base(null)
    {
    }
    
    private static void NotifyAnsiConsoleUsed()
    {
        OnAnsiConsoleUsed?.Invoke();
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle method access - create a FunctionValue wrapper
        if (name == "markup" || name == "markupLine" || name == "table" || name == "panel" || name == "tree" ||
            name == "status" || name == "prompt" || name == "progress")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on AnsiConsole.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "markup" => CallMarkup(args),
            "markupLine" => CallMarkupLine(args),
            "table" => CallTable(args),
            "panel" => CallPanel(args),
            "tree" => CallTree(args),
            "status" => throw new Exception("status() must be called via CallMethodAsync"),
            "prompt" => throw new Exception("prompt() must be called via CallMethodAsync"),
            "progress" => throw new Exception("progress() must be called via CallMethodAsync"),
            _ => throw new Exception($"AnsiConsole has no method '{methodName}'.")
        };
    }
    
    public async Task<RuntimeValue> CallMethodAsync(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        if (interpreter == null)
            throw new Exception("Interpreter is required for async AnsiConsole methods");
            
        return methodName switch
        {
            "status" => await CallStatusAsync(args, interpreter),
            "prompt" => await CallPromptAsync(args, interpreter),
            "progress" => await CallProgressAsync(args, interpreter),
            _ => throw new Exception($"AnsiConsole has no async method '{methodName}'.")
        };
    }
    
    private RuntimeValue CallMarkup(List<RuntimeValue> args)
    {
        NotifyAnsiConsoleUsed();
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("AnsiConsole.markup() expects 1 string argument");

        // Same fallback as panel(): unparseable markup (e.g. Windows paths in errors) prints literally.
        BuiltInFunctions.WriteSpectreMarkup(args[0].AsString(), appendNewLine: false);
        return RuntimeValue.Null();
    }

    private RuntimeValue CallMarkupLine(List<RuntimeValue> args)
    {
        NotifyAnsiConsoleUsed();
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("AnsiConsole.markupLine() expects 1 string argument");

        BuiltInFunctions.WriteSpectreMarkup(args[0].AsString(), appendNewLine: true);
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallTable(List<RuntimeValue> args)
    {
        NotifyAnsiConsoleUsed();
        return BuiltInFunctions.BuiltInSpectreConsoleTable(args);
    }
    
    private RuntimeValue CallPanel(List<RuntimeValue> args)
    {
        NotifyAnsiConsoleUsed();
        return BuiltInFunctions.BuiltInSpectreConsolePanel(args);
    }
    
    private RuntimeValue CallTree(List<RuntimeValue> args)
    {
        NotifyAnsiConsoleUsed();
        return BuiltInFunctions.BuiltInSpectreConsoleTree(args);
    }
    
    private async Task<RuntimeValue> CallStatusAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        NotifyAnsiConsoleUsed();
        return await BuiltInFunctions.BuiltInSpectreConsoleStatusAsync(args, interpreter);
    }
    
    private async Task<RuntimeValue> CallPromptAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        NotifyAnsiConsoleUsed();
        return await BuiltInFunctions.BuiltInSpectreConsolePromptAsync(args, interpreter);
    }
    
    private async Task<RuntimeValue> CallProgressAsync(List<RuntimeValue> args, Interpreter interpreter)
    {
        NotifyAnsiConsoleUsed();
        return await BuiltInFunctions.BuiltInSpectreConsoleProgressAsync(args, interpreter);
    }
}
