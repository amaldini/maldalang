// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using System;
using System.Text;
using System.Reflection;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using MaldaLang.Testing;
using MaldaLang.Scaffolding;
using MaldaLang.Deployment;
using MaldaLang.UIHost;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Cli;
using MaldaLang.Eval;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.BuiltIns;
using MaldaLang.DebugAdapter;
using SystemEnvironment = System.Environment;
using PackageManager = MaldaLang.PackageManager.PackageManager;

namespace MaldaLang;

class InputResult
{
    public string? Code { get; set; }
    public string Action { get; set; } = "run"; // "run", "compile", "transpile", "exit", "help", "vars", "source", "drop", "replace", "editline", "insert"
    /// <summary>1-based source line for <c>editline</c> / <c>insert</c>.</summary>
    public int PatchLine { get; set; }
    /// <summary>Replacement or inserted text for a line patch.</summary>
    public string? PatchText { get; set; }
}

partial class Program
{
    static void RunPrompt()
    {
        var version = GetVersion();
        Console.WriteLine("MALDA (Multi Agent Language with Development Automation) Interpreter");
        if (!string.IsNullOrEmpty(version))
        {
            Console.WriteLine($"Version {version}");
        }
        Console.WriteLine("You can enter multiline code - type 'run', 'compile', or 'transpile' to execute the buffer, or a blank line to submit it and return to the prompt");
        Console.WriteLine("Type 'exit' to quit, 'vars' to list definitions, 'source' to print their code, 'help' for help");
        Console.WriteLine("(c) 2026 - Andrea Maldini");
        // One interpreter for the whole session: variables, functions, and classes
        // defined in one entry stay visible in later ones.
        var interpreter = new Interpreter.Interpreter();
        var hostNames = ReplSessionInventory.SnapshotHostNames(interpreter);
        var sessionSource = new ReplSessionSource();
        while (true)
        {
            var result = ReadMultilineInput(sessionSource);
            if (result == null || result.Action == "exit")
                break;
            ExecutePromptInput(result, interpreter, hostNames, sessionSource);
        }
    }

    private static readonly TokenType[] PromptAssignmentTokenTypes =
    {
        TokenType.Assign,
        TokenType.PlusAssign,
        TokenType.MinusAssign,
        TokenType.MultiplyAssign,
        TokenType.DivideAssign
    };

    /// <summary>
    /// Handles one parsed REPL entry. <c>help</c> carries no code, so it must be
    /// dispatched before the empty-code guard below or it is silently dropped.
    /// <paramref name="interpreter"/> is the session interpreter, reused so state
    /// defined by earlier entries is still in scope.
    /// <paramref name="hostNames"/> are globals present at session start (stdlib
    /// and host classes); <c>vars</c> hides them so only user definitions remain.
    /// <paramref name="sessionSource"/> records original definition text for <c>source</c>.
    /// </summary>
    static void ExecutePromptInput(
        InputResult result,
        Interpreter.Interpreter interpreter,
        ISet<string>? hostNames = null,
        ReplSessionSource? sessionSource = null)
    {
        try
        {
            if (result.Action == "help")
            {
                ShowHelp();
            }
            else if (result.Action == "vars")
            {
                if (!ReplSessionInventory.TryParseKind(result.Code, out var kind, out var error))
                {
                    Console.WriteLine(error);
                    return;
                }
                ReplSessionInventory.Write(Console.Out, interpreter, hostNames, kind);
            }
            else if (result.Action == "source")
            {
                sessionSource ??= new ReplSessionSource();
                sessionSource.Write(Console.Out, result.Code);
            }
            else if (result.Action == ReplSessionEdit.DropAction
                     || result.Action == ReplSessionEdit.ReplaceAction)
            {
                sessionSource ??= new ReplSessionSource();
                ReplSessionEdit.Write(
                    Console.Out,
                    result.Action,
                    result.Code ?? "",
                    interpreter,
                    hostNames,
                    sessionSource);
            }
            else if (result.Action == ReplSessionPatch.UsageAction)
            {
                sessionSource ??= new ReplSessionSource();
                ReplSessionPatch.WriteListing(
                    Console.Out,
                    result.PatchText ?? ReplSessionPatch.EditLineUsage,
                    result.Code ?? "",
                    hostNames,
                    sessionSource);
            }
            else if (result.Action == ReplSessionPatch.EditLineAction
                     || result.Action == ReplSessionPatch.InsertAction)
            {
                sessionSource ??= new ReplSessionSource();
                ReplSessionPatch.Write(
                    Console.Out,
                    result.Action,
                    result.Code ?? "",
                    result.PatchLine,
                    result.PatchText ?? "",
                    interpreter,
                    hostNames,
                    sessionSource,
                    code => Run(code, interpreter));
            }
            else if (string.IsNullOrWhiteSpace(result.Code))
            {
                return;
            }
            else if (result.Action == "run")
            {
                // A bare expression echoes its value (`1 + 2` -> `3`); anything else
                // (statements, declarations, multi-line buffers) runs through Run.
                if (!TryEchoPromptExpression(result.Code, interpreter))
                {
                    Run(result.Code, interpreter);
                    sessionSource?.Record(result.Code);
                }
            }
            else if (result.Action == "compile")
            {
                CompileFromSource(result.Code, "Interpreter");
            }
            else if (result.Action == "transpile")
            {
                CompileFromSource(result.Code, "TranspileToCSharp");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// REPL echo: when <paramref name="code"/> is a single bare expression (no
    /// trailing <c>;</c> and no assignment), evaluate it in the session globals and
    /// print the value. Returns false when the entry is a statement/declaration, so
    /// the caller falls back to <see cref="Run"/>. A null result prints nothing, so
    /// <c>print(x)</c> is not echoed twice.
    /// </summary>
    static bool TryEchoPromptExpression(string code, Interpreter.Interpreter interpreter)
    {
        var trimmed = code.Trim();
        if (trimmed.Length == 0 || trimmed.EndsWith(";", StringComparison.Ordinal))
            return false;

        List<Token> tokens;
        try
        {
            tokens = new Lexer(code).Tokenize();
        }
        catch
        {
            return false;
        }

        // Assignments are statements: evaluating `x = 5` as an expression would
        // return 5 without performing the assignment.
        if (tokens.Any(t => PromptAssignmentTokenTypes.Contains(t.Type)))
            return false;

        Expression expression;
        try
        {
            expression = MaldaLang.Parser.Parser.ParseExpression(tokens);
        }
        catch
        {
            return false;
        }

        var value = interpreter
            .EvaluateInEnvironmentAsync(expression, interpreter.GlobalsEnvironment)
            .GetAwaiter().GetResult();

        if (value.Type != MaldaLang.Interpreter.ValueType.Null)
            Console.WriteLine(value.ToString());

        return true;
    }
    
    static InputResult? ReadMultilineInput(ReplSessionSource? sessionSource = null)
    {
        Console.Write("> ");
        var firstLine = Console.ReadLine();
        if (firstLine == null) return null;
        
        var trimmedFirst = firstLine.Trim();
        var trimmedFirstLower = trimmedFirst.ToLowerInvariant();
        
        // Check if first line is a command
        if (trimmedFirstLower == "exit")
            return new InputResult { Code = null, Action = "exit" };
        if (trimmedFirstLower == "help")
            return new InputResult { Code = null, Action = "help" };
        if (ReplSessionInventory.TryParseCommand(trimmedFirst, out var varsFilter))
            return new InputResult { Code = varsFilter, Action = "vars" };
        if (ReplSessionSource.TryParseCommand(trimmedFirst, out var sourceFilter))
            return new InputResult { Code = sourceFilter, Action = "source" };
        if (ReplSessionEdit.TryParseCommand(trimmedFirst, out var editAction, out var editName))
            return new InputResult { Code = editName, Action = editAction };
        if (TryConsumePatchCommand(trimmedFirst, sessionSource, out var patchResult))
            return patchResult;
        
        // Check if we need multiline input
        if (!NeedsMoreInput(firstLine))
        {
            // Single line, execute immediately
            return new InputResult { Code = firstLine, Action = "run" };
        }
        
        // Continuation until 'run'/'compile'/'transpile', a complete-looking
        // buffer is not auto-submitted (NeedsMoreInput has false negatives).
        // A blank line finishes the entry without ending the REPL; EOF still
        // ends the session — do not treat blank the same as ReadLine null.
        var sb = new StringBuilder(firstLine);
        
        while (true)
        {
            Console.Write("..> ");
            var line = Console.ReadLine();
            if (line == null) return null;
            
            var trimmed = line.Trim();
            var trimmedLower = trimmed.ToLowerInvariant();
            
            // Check if user wants to run, compile, transpile, list defs, or show help
            if (trimmedLower == "run")
            {
                return new InputResult { Code = sb.ToString(), Action = "run" };
            }
            else if (trimmedLower == "compile")
            {
                return new InputResult { Code = sb.ToString(), Action = "compile" };
            }
            else if (trimmedLower == "transpile")
            {
                return new InputResult { Code = sb.ToString(), Action = "transpile" };
            }
            else if (trimmedLower == "help")
            {
                return new InputResult { Code = null, Action = "help" };
            }
            else if (ReplSessionInventory.TryParseCommand(trimmed, out var contVarsFilter))
            {
                return new InputResult { Code = contVarsFilter, Action = "vars" };
            }
            else if (ReplSessionSource.TryParseCommand(trimmed, out var contSourceFilter))
            {
                return new InputResult { Code = contSourceFilter, Action = "source" };
            }
            else if (ReplSessionEdit.TryParseCommand(trimmed, out var contEditAction, out var contEditName))
            {
                return new InputResult { Code = contEditName, Action = contEditAction };
            }
            else if (TryConsumePatchCommand(trimmed, sessionSource, out var contPatchResult))
            {
                return contPatchResult;
            }
            
            if (string.IsNullOrWhiteSpace(line))
                return new InputResult { Code = sb.ToString(), Action = "run" };
                
            sb.AppendLine();
            sb.Append(line);
        }
    }
    
    /// <summary>
    /// <c>editline</c> / <c>insert</c>. A command with no inline text reads
    /// replacement lines until a blank line. Those lines are source, so
    /// <c>run</c> inside the buffer is not a REPL command. EOF ends the session.
    /// </summary>
    static bool TryConsumePatchCommand(string line, ReplSessionSource? sessionSource, out InputResult? result)
    {
        result = null;
        if (!ReplSessionPatch.TryParseCommand(line, out var patch))
            return false;

        if (patch.IsUsage)
        {
            result = new InputResult
            {
                Action = ReplSessionPatch.UsageAction,
                Code = patch.Name,
                PatchText = patch.Usage
            };
            return true;
        }

        if (patch.NeedsBody)
        {
            var recorded = sessionSource?.Peek(ReplSessionEdit.NormalizeName(patch.Name));
            if (!string.IsNullOrEmpty(recorded))
                Console.WriteLine(ReplSessionPatch.Numbered(recorded));
            result = ReadPatchBody(patch.Action, patch.Name, patch.LineNumber);
            return true;
        }

        result = new InputResult
        {
            Action = patch.Action,
            Code = patch.Name,
            PatchLine = patch.LineNumber,
            PatchText = patch.InlineText ?? ""
        };
        return true;
    }

    static InputResult? ReadPatchBody(string action, string name, int lineNumber)
    {
        var lines = new List<string>();
        while (true)
        {
            Console.Write("..> ");
            var line = Console.ReadLine();
            if (line == null)
                return null;
            if (string.IsNullOrWhiteSpace(line))
            {
                return new InputResult
                {
                    Action = action,
                    Code = name,
                    PatchLine = lineNumber,
                    PatchText = string.Join("\n", lines)
                };
            }

            lines.Add(line);
        }
    }

    static bool NeedsMoreInput(string code)
    {
        var trimmed = code.TrimEnd();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;
        
        // Heuristic: check for incomplete patterns
        // Ends with operators or opening brackets
        if (trimmed.EndsWith("{") || trimmed.EndsWith("(") || 
            trimmed.EndsWith("[") || trimmed.EndsWith("+") ||
            trimmed.EndsWith("-") || trimmed.EndsWith("*") ||
            trimmed.EndsWith("/") || trimmed.EndsWith("=") ||
            trimmed.EndsWith("&&") || trimmed.EndsWith("||") ||
            trimmed.EndsWith(",") || trimmed.EndsWith("."))
            return true;
        
        // Check for incomplete keywords (heuristic)
        var lastWord = trimmed.Split(new[] { ' ', '\t', '\n', '\r', '(', '{', '[' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        if (lastWord != null)
        {
            var incompleteKeywords = new[] { "if", "for", "while", "function", "class", "else", "elif" };
            if (incompleteKeywords.Contains(lastWord.ToLower()))
                return true;
        }
        
        // Try parsing - if it fails with "unexpected end" or similar, need more
        try
        {
            var lexer = new Lexer(code);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            parser.Parse();
            return false; // Parsed successfully, input is complete
        }
        catch (Exception ex)
        {
            // If error suggests incomplete input, continue
            var errorMsg = ex.Message.ToLower();
            if (errorMsg.Contains("unexpected end") || 
                errorMsg.Contains("expected") && (errorMsg.Contains("token") || errorMsg.Contains("character")))
            {
                // Check if it's a real syntax error or just incomplete
                // If we have tokens, it might be incomplete
                try
                {
                    var lexer = new Lexer(code);
                    var tokens = lexer.Tokenize();
                    if (tokens.Count > 0)
                        return true; // Have tokens but parse failed, likely incomplete
                }
                catch
                {
                    // Lexer error, probably incomplete
                    return true;
                }
            }
            return false; // Other error, let it propagate to Run()
        }
    }
}
