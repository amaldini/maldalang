// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    // Spectre.Console functions
    private static RuntimeValue BuiltInSpectreConsoleMarkup(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("markup() expects 1 string argument");

        WriteSpectreMarkup(args[0].AsString(), appendNewLine: false);
        return RuntimeValue.Null();
    }

    /// <summary>
    /// Renders Spectre markup when valid; otherwise writes the text literally.
    /// Dynamic paths/errors often contain <c>[...]</c> that must not abort the program.
    /// </summary>
    public static void WriteSpectreMarkup(string text, bool appendNewLine)
    {
        if (IsParseableSpectreMarkup(text))
        {
            if (appendNewLine)
                AnsiConsole.MarkupLine(text);
            else
                AnsiConsole.Markup(text);
            return;
        }

        if (appendNewLine)
            AnsiConsole.WriteLine(text);
        else
            AnsiConsole.Write(text);
    }

    /// <summary>
    /// Spectre caches the console writer. Tests that redirect <see cref="Console.Out"/>
    /// must rebind after restore or later markup calls hit a disposed StringWriter.
    /// </summary>
    public static void RebindSpectreConsoleForTesting(TextWriter? writer = null)
    {
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer ?? Console.Out)
        });
    }
    
    public static RuntimeValue BuiltInSpectreConsoleTable(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("table() expects at least 1 argument: (data, title?, columns?)");
        
        var table = new Table();
        
        // Parse data (array of objects or array of arrays)
        if (args[0].Type != ValueType.Array)
            throw new Exception("table() first argument must be an array");
        
        var dataArray = args[0].AsArray();
        
        // Optional title
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            table.Title = new TableTitle(args[1].AsString());
        }
        
        // Optional columns (array of strings)
        List<string>? columnNames = null;
        if (args.Count > 2 && args[2].Type == ValueType.Array)
        {
            var colsArray = args[2].AsArray();
            columnNames = new List<string>();
            foreach (var col in colsArray)
            {
                if (col.Type == ValueType.String)
                    columnNames.Add(col.AsString());
            }
        }
        
        // Determine structure from first row
        if (dataArray.Count == 0)
        {
            AnsiConsole.Write(table);
            return RuntimeValue.Null();
        }
        
        var firstRow = dataArray[0];
        if (firstRow.Type == ValueType.Object)
        {
            var firstRowObj = firstRow.AsObject();
            if (firstRowObj is ObjectInstance firstJsonObj)
            {
                // Object array - use object keys as columns
                if (columnNames == null)
                {
                    columnNames = new List<string>();
                    foreach (var key in firstJsonObj.GetAllKeys())
                    {
                        columnNames.Add(key);
                        table.AddColumn(key);
                    }
                }
                else
                {
                    foreach (var col in columnNames)
                    {
                        table.AddColumn(col);
                    }
                }
                
                // Add rows
                foreach (var row in dataArray)
                {
                    if (row.Type == ValueType.Object && row.AsObject() is ObjectInstance rowObj)
                    {
                        var rowValues = new List<string>();
                        foreach (var col in columnNames)
                        {
                            var value = GetObjectField(rowObj, col);
                            rowValues.Add(value?.ToString() ?? "");
                        }
                        table.AddRow(rowValues.ToArray());
                    }
                }
            }
        }
        else if (firstRow.Type == ValueType.Array)
        {
            // Array of arrays
            var firstArray = firstRow.AsArray();
            if (columnNames == null)
            {
                // Use first row as headers
                var headers = new List<string>();
                foreach (var item in firstArray)
                {
                    headers.Add(item.ToString());
                }
                foreach (var header in headers)
                {
                    table.AddColumn(header);
                }
                
                // Add remaining rows
                for (int i = 1; i < dataArray.Count; i++)
                {
                    var row = dataArray[i];
                    if (row.Type == ValueType.Array)
                    {
                        var rowArray = row.AsArray();
                        var rowValues = new List<string>();
                        foreach (var item in rowArray)
                        {
                            rowValues.Add(item.ToString());
                        }
                        table.AddRow(rowValues.ToArray());
                    }
                }
            }
            else
            {
                // Use provided column names
                foreach (var col in columnNames)
                {
                    table.AddColumn(col);
                }
                
                // Add all rows
                foreach (var row in dataArray)
                {
                    if (row.Type == ValueType.Array)
                    {
                        var rowArray = row.AsArray();
                        var rowValues = new List<string>();
                        foreach (var item in rowArray)
                        {
                            rowValues.Add(item.ToString());
                        }
                        table.AddRow(rowValues.ToArray());
                    }
                }
            }
        }
        
        AnsiConsole.Write(table);
        return RuntimeValue.Null();
    }
    
    private static string EscapeSpectrePlainText(string text)
    {
        return text.Replace("[", "[[", StringComparison.Ordinal)
            .Replace("]", "]]", StringComparison.Ordinal);
    }

    /// <summary>
    /// Spectre throws on malformed markup, and panels have always accepted arbitrary text
    /// (JSON, code, error dumps). Content that does not parse is rendered literally rather
    /// than aborting the program.
    /// </summary>
    private static bool IsParseableSpectreMarkup(string text)
    {
        try
        {
            _ = new Markup(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
    
    public static RuntimeValue BuiltInSpectreConsolePanel(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("panel() expects at least 1 string argument: (content, title?, borderStyle?)");
        
        var content = args[0].AsString();
        IRenderable body = IsParseableSpectreMarkup(content)
            ? new Markup(content)
            : new Text(content);
        var panel = new Panel(body);
        
        if (args.Count > 1 && args[1].Type == ValueType.String)
        {
            var header = args[1].AsString();
            panel.Header = new PanelHeader(IsParseableSpectreMarkup(header)
                ? header
                : EscapeSpectrePlainText(header));
        }
        
        if (args.Count > 2 && args[2].Type == ValueType.String)
        {
            var borderStyle = args[2].AsString().ToLower();
            panel.Border = borderStyle switch
            {
                "rounded" => BoxBorder.Rounded,
                "double" => BoxBorder.Double,
                "heavy" => BoxBorder.Heavy,
                _ => BoxBorder.Square
            };
        }
        
        AnsiConsole.Write(panel);
        return RuntimeValue.Null();
    }
    
    public static RuntimeValue BuiltInSpectreConsoleTree(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("tree() expects at least 1 string argument: (label, items?)");
        
        var label = args[0].AsString();
        var tree = new Tree(label);
        
        if (args.Count > 1 && args[1].Type == ValueType.Array)
        {
            var itemsArray = args[1].AsArray();
            foreach (var item in itemsArray)
            {
                if (item.Type == ValueType.String)
                {
                    tree.AddNode(item.AsString());
                }
                else if (item.Type == ValueType.Object && item.AsObject() is ObjectInstance itemObj)
                {
                    // Object with label and children
                    var itemLabelValue = GetObjectField(itemObj, "label");
                    var itemLabel = itemLabelValue != null && itemLabelValue.Type == ValueType.String 
                        ? itemLabelValue.AsString() 
                        : "";
                    var node = tree.AddNode(itemLabel);
                    
                    var children = GetObjectField(itemObj, "children");
                    if (children != null && children.Type == ValueType.Array)
                    {
                        var childrenArray = children.AsArray();
                        foreach (var child in childrenArray)
                        {
                            if (child.Type == ValueType.String)
                            {
                                node.AddNode(child.AsString());
                            }
                        }
                    }
                }
            }
        }
        
        AnsiConsole.Write(tree);
        return RuntimeValue.Null();
    }
    
    public static async Task<RuntimeValue> BuiltInSpectreConsoleStatusAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args[0].Type != ValueType.String)
            throw new Exception("status() expects at least 1 string argument: (message, action?)");
        
        var message = args[0].AsString();
        
        if (args.Count > 1 && args[1].Type == ValueType.Function)
        {
            // action is a function/callback
            await AnsiConsole.Status()
                .StartAsync(message, async ctx =>
                {
                    // Call the action function
                    var funcValue = args[1].AsFunction();
                    await InvokeConsoleCallbackAsync(funcValue, null, interpreter);
                });
        }
        else
        {
            AnsiConsole.Status().Start(message, ctx => { });
        }
        
        return RuntimeValue.Null();
    }
    
    public static async Task<RuntimeValue> BuiltInSpectreConsolePromptAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1 || args[0].Type != ValueType.Object)
            throw new Exception("prompt() expects at least 1 object argument: (type, message, defaultValue?)");
        
        var promptJson = args[0].AsObject() ?? throw new Exception("prompt() first argument must be an object");
        
        var typeValue = GetObjectField(promptJson, "type");
        var type = typeValue != null && typeValue.Type == ValueType.String 
            ? typeValue.AsString().ToLower() 
            : "text";
        var messageValue = GetObjectField(promptJson, "message");
        var message = messageValue != null && messageValue.Type == ValueType.String 
            ? messageValue.AsString() 
            : "";
        var defaultValue = GetObjectField(promptJson, "defaultValue");
        
        string result;
        
        switch (type)
        {
            case "text":
            case "input":
                if (defaultValue != null && defaultValue.Type == ValueType.String)
                {
                    result = AnsiConsole.Prompt(new TextPrompt<string>(message).DefaultValue(defaultValue.AsString()));
                }
                else
                {
                    result = AnsiConsole.Prompt(new TextPrompt<string>(message));
                }
                break;
                
            case "confirm":
            case "yesno":
                var defaultBool = defaultValue != null && defaultValue.Type == ValueType.Boolean && defaultValue.AsBoolean();
                var confirmed = AnsiConsole.Confirm(message, defaultBool);
                return RuntimeValue.Boolean(confirmed);
                
            case "selection":
            case "choice":
                var choicesValue = GetObjectField(promptJson, "choices");
                if (choicesValue == null || choicesValue.Type != ValueType.Array)
                    throw new Exception("prompt() with type 'selection' requires 'choices' array");
                var choices = choicesValue;
                
                var choicesArray = choices.AsArray();
                var choiceList = new List<string>();
                foreach (var choice in choicesArray)
                {
                    if (choice.Type == ValueType.String)
                        choiceList.Add(choice.AsString());
                }
                
                var selected = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .Title(message)
                    .AddChoices(choiceList));
                return RuntimeValue.String(selected);
                
            default:
                throw new Exception($"Unknown prompt type: {type}. Supported: text, confirm, selection");
        }
        
        return RuntimeValue.String(result);
    }
    
    public static async Task<RuntimeValue> BuiltInSpectreConsoleProgressAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1)
            throw new Exception("progress() expects at least 1 argument");
        
        // Check if the first argument is a function (new callback syntax)
        if (args[0].Type == ValueType.Function)
        {
            var callbackFunc = args[0].AsFunction();
            
            await AnsiConsole.Progress()
                .StartAsync(async ctx =>
                {
                    var taskDict = new Dictionary<string, ProgressTask>();
                    var progressCtx = new ProgressContextWrapper(ctx, taskDict);
                    await InvokeConsoleCallbackAsync(callbackFunc, progressCtx, interpreter);
                });
            
            return RuntimeValue.Null();
        }
        
        // Old syntax: object with tasks array and optional action callback
        if (args[0].Type != ValueType.Object)
            throw new Exception("progress() expects either a function callback or an object with 'tasks' array");
        
        var progressSpec = args[0].AsObject() ?? throw new Exception("progress() first argument must be an object");
        
        var tasks = GetObjectField(progressSpec, "tasks");
        if (tasks == null || tasks.Type != ValueType.Array)
            throw new Exception("progress() requires 'tasks' array");
        
        var tasksArray = tasks.AsArray();
        var action = GetObjectField(progressSpec, "action");
        
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var progressTasks = new List<ProgressTask>();
                
                // Create progress tasks
                foreach (var task in tasksArray)
                {
                    if (task.Type == ValueType.Object && task.AsObject() is ObjectInstance taskObj)
                    {
                        var taskNameValue = GetObjectField(taskObj, "name");
                        var taskName = taskNameValue != null && taskNameValue.Type == ValueType.String 
                            ? taskNameValue.AsString() 
                            : "";
                        var maxValueValue = GetObjectField(taskObj, "maxValue");
                        var maxValue = maxValueValue != null && maxValueValue.Type == ValueType.Integer 
                            ? maxValueValue.AsInteger() 
                            : (maxValueValue != null && maxValueValue.Type == ValueType.Float 
                                ? (int)maxValueValue.AsFloat() 
                                : 100);
                        var progressTask = ctx.AddTask(taskName, maxValue: maxValue);
                        progressTasks.Add(progressTask);
                    }
                }
                
                // Execute action if provided
                if (action != null && action.Type == ValueType.Function)
                {
                    var funcValue = action.AsFunction();
                    
                    // Create a dictionary mapping task names to ProgressTask instances
                    var taskDict = new Dictionary<string, ProgressTask>();
                    for (int i = 0; i < progressTasks.Count; i++)
                    {
                        var taskName = $"Task{i}";
                        if (i < tasksArray.Count && tasksArray[i].Type == ValueType.Object && tasksArray[i].AsObject() is ObjectInstance t)
                        {
                            var nameValue = GetObjectField(t, "name");
                            if (nameValue != null && nameValue.Type == ValueType.String)
                            {
                                taskName = nameValue.AsString();
                            }
                        }
                        taskDict[taskName] = progressTasks[i];
                    }
                    
                    // Create a wrapper object that allows reading and updating progress
                    var progressWrapper = new ProgressWrapperObjectInstance(taskDict);
                    await InvokeConsoleCallbackAsync(funcValue, progressWrapper, interpreter);
                }
            });
        
        return RuntimeValue.Null();
    }

    /// <summary>
    /// Reads an option field from an object argument, or null when absent. Interpreted object
    /// literals arrive as JsonObjects, C# transpiled ones as DictionaryInstances, so the console
    /// built-ins accept any ObjectInstance.
    /// </summary>
    private static RuntimeValue? GetObjectField(ObjectInstance obj, string name) =>
        obj.TryGet(name, out var value) ? value : null;

    /// <summary>
    /// Runs a status/progress callback with an optional context object: through the interpreter
    /// when interpreting, or through the transpiled lambda delegate (which takes the raw object)
    /// in C# transpiled programs, which call these built-ins with no interpreter.
    /// </summary>
    private static async Task InvokeConsoleCallbackAsync(FunctionValue callback, ObjectInstance? context, Interpreter? interpreter)
    {
        if (interpreter != null)
        {
            var arguments = context == null
                ? new List<RuntimeValue>()
                : new List<RuntimeValue> { RuntimeValue.Object(context) };
            await interpreter.CallFunctionAsync(callback, arguments);
            return;
        }

        if (callback.TranspiledDelegate != null)
        {
            await callback.TranspiledDelegate(context!).ConfigureAwait(false);
            return;
        }

        throw new Exception("AnsiConsole callback cannot be invoked without an interpreter.");
    }
}
