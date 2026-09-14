// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Interpreter.Debug;
using MaldaLang.Parser.AST.Declarations;

/// <summary>
/// REPL session catalog: lists user-defined bindings and hides host/stdlib names.
/// Commands: <c>vars</c>, <c>defs</c>, <c>who</c>, optionally filtered by kind.
/// </summary>
public static class ReplSessionInventory
{
    public enum Kind
    {
        All,
        Variables,
        Functions,
        Classes,
        Prompts,
        Actors,
        Workflows
    }

    private const int PreviewLimit = 72;

    /// <summary>
    /// Fallback hide-list when the caller did not snapshot host names at session
    /// start. Prefer <see cref="SnapshotHostNames"/>.
    /// </summary>
    private static readonly HashSet<string> DefaultHiddenNames = new(StringComparer.Ordinal)
    {
        StdLibNamespaces.MathModule,
        StdLibNamespaces.StrModule,
        StdLibNamespaces.IoModule,
        StdLibNamespaces.PdfModule,
        StdLibNamespaces.DocModule,
        StdLibNamespaces.ResultModule,
        StdLibNamespaces.OptionModule,
        StdLibNamespaces.GroundedModule,
        StdLibNamespaces.CapModule,
        StdLibNamespaces.AgentsModule,
        StdLibNamespaces.TraceModule,
        StdLibNamespaces.DeprecatedMathModuleAlias,
        "ui",
        "AnsiConsole",
        "VectorDB",
        "GraphMemory",
        AgentErrorStdLib.TypeName,
        "Refused",
        "Unparsable",
        "SchemaMismatch",
        "BudgetExceeded",
        "ToolDenied",
        "Timeout",
        "Upstream"
    };

    public static HashSet<string> SnapshotHostNames(Interpreter interpreter)
    {
        ArgumentNullException.ThrowIfNull(interpreter);
        return new HashSet<string>(
            interpreter.GlobalsEnvironment.GetOwnVariables().Keys,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="line"/> is a REPL inventory command
    /// (<c>vars</c> / <c>defs</c> / <c>who</c>), not an assignment such as
    /// <c>vars = 1</c>. <paramref name="filter"/> is the optional kind word(s).
    /// </summary>
    public static bool TryParseCommand(string? line, out string filter)
    {
        filter = "";
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !IsVarsVerb(parts[0]))
            return false;

        if (parts.Length >= 2 && !LooksLikeFilterToken(parts[1]))
            return false;

        if (parts.Length > 1)
            filter = string.Join(" ", parts.Skip(1));
        return true;
    }

    public static bool TryParseKind(string? filter, out Kind kind, out string? error)
    {
        kind = Kind.All;
        error = null;
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        switch (filter.Trim().ToLowerInvariant())
        {
            case "variables":
            case "variable":
            case "vars":
            case "var":
                kind = Kind.Variables;
                return true;
            case "functions":
            case "function":
            case "fn":
                kind = Kind.Functions;
                return true;
            case "classes":
            case "class":
                kind = Kind.Classes;
                return true;
            case "prompts":
            case "prompt":
                kind = Kind.Prompts;
                return true;
            case "actors":
            case "actor":
                kind = Kind.Actors;
                return true;
            case "workflows":
            case "workflow":
                kind = Kind.Workflows;
                return true;
            default:
                error = "Unknown vars filter '" + filter.Trim()
                    + "'. Use variables, functions, classes, prompts, actors, or workflows.";
                return false;
        }
    }

    public static string Format(
        Interpreter interpreter,
        ISet<string>? hostNames,
        Kind kind = Kind.All)
    {
        ArgumentNullException.ThrowIfNull(interpreter);

        var hidden = hostNames ?? DefaultHiddenNames;
        var globals = interpreter.GlobalsEnvironment;
        var variables = new List<string>();
        var functions = new List<string>();
        var classes = new List<string>();
        var prompts = new List<string>();

        foreach (var kvp in globals.GetOwnVariables().OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (hidden.Contains(kvp.Key))
                continue;

            switch (kvp.Value.Type)
            {
                case ValueType.Function:
                    functions.Add("  " + FormatFunction(kvp.Key, kvp.Value.AsFunction()));
                    break;
                case ValueType.Class:
                    classes.Add(FormatClassBlock(kvp.Key, kvp.Value.AsClass()));
                    break;
                case ValueType.Prompt:
                    prompts.Add("  " + FormatPrompt(kvp.Value.AsPrompt()));
                    break;
                case ValueType.Actor:
                    break;
                default:
                    variables.Add("  " + FormatVariable(kvp.Key, kvp.Value, globals.IsConst(kvp.Key)));
                    break;
            }
        }

        var actors = interpreter.DefinedActors
            .OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(kvp => FormatActorBlock(kvp.Value))
            .ToList();
        var workflows = interpreter.DefinedWorkflows
            .OrderBy(k => k.Key, StringComparer.Ordinal)
            .Select(kvp => "  " + FormatCallable(kvp.Value.Name, kvp.Value.Parameters, null))
            .ToList();

        var sb = new StringBuilder();
        var any = false;
        any |= AppendSection(sb, kind, Kind.Variables, "variables", variables);
        any |= AppendSection(sb, kind, Kind.Functions, "functions", functions);
        any |= AppendSection(sb, kind, Kind.Classes, "classes", classes);
        any |= AppendSection(sb, kind, Kind.Prompts, "prompts", prompts);
        any |= AppendSection(sb, kind, Kind.Actors, "actors", actors);
        any |= AppendSection(sb, kind, Kind.Workflows, "workflows", workflows);

        if (!any)
            return kind == Kind.All
                ? "(no user definitions)"
                : "(no " + SectionTitle(kind) + ")";

        return sb.ToString().TrimEnd();
    }

    public static void Write(
        TextWriter writer,
        Interpreter interpreter,
        ISet<string>? hostNames,
        Kind kind = Kind.All)
    {
        writer.WriteLine(Format(interpreter, hostNames, kind));
    }

    private static bool AppendSection(
        StringBuilder sb,
        Kind requested,
        Kind section,
        string title,
        List<string> lines)
    {
        if (requested != Kind.All && requested != section)
            return false;
        if (lines.Count == 0)
            return false;

        if (sb.Length > 0)
            sb.AppendLine();
        sb.Append(title);
        sb.Append(':');
        sb.AppendLine();
        foreach (var line in lines)
            sb.AppendLine(line);
        return true;
    }

    private static string SectionTitle(Kind kind) => kind switch
    {
        Kind.Variables => "variables",
        Kind.Functions => "functions",
        Kind.Classes => "classes",
        Kind.Prompts => "prompts",
        Kind.Actors => "actors",
        Kind.Workflows => "workflows",
        _ => "user definitions"
    };

    private static bool IsVarsVerb(string word) =>
        word.Equals("vars", StringComparison.OrdinalIgnoreCase)
        || word.Equals("defs", StringComparison.OrdinalIgnoreCase)
        || word.Equals("who", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeFilterToken(string token) =>
        token.Length > 0 && char.IsLetter(token[0]);

    private static string FormatVariable(string name, RuntimeValue value, bool isConst)
    {
        var prefix = isConst ? "const " : "";
        return prefix + name + " = " + FormatValue(value);
    }

    private static string FormatValue(RuntimeValue value)
    {
        if (value.Type == ValueType.String)
            return Truncate(QuoteString(value.AsString()));
        return Truncate(DebugValueFormatter.FormatPreview(value));
    }

    private static string FormatFunction(string name, FunctionValue function)
    {
        var decl = function.Declaration;
        if (decl != null)
            return FormatCallable(decl.Name, decl.Parameters, decl.ReturnType);
        return name;
    }

    private static string FormatPrompt(PromptValue prompt)
    {
        var decl = prompt.Declaration;
        return FormatCallable(decl.Name, decl.Parameters, decl.ReturnType);
    }

    private static string FormatClassBlock(string name, ClassDefinition klass)
    {
        var sb = new StringBuilder();
        var ctorParams = klass.Constructor?.Declaration?.Parameters;
        var line = FormatCallable(name, ctorParams, null);
        if (klass.Superclass != null)
            line += " extends " + klass.Superclass.Name;
        sb.Append("  ");
        sb.Append(line);

        var methods = klass.Methods
            .Where(m => !klass.MethodAccess.TryGetValue(m.Key, out var access) || access != AccessModifier.Private)
            .Select(m => m.Key)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (methods.Count > 0)
        {
            sb.AppendLine();
            sb.Append("    methods: ");
            sb.Append(string.Join(", ", methods));
        }

        return sb.ToString();
    }

    private static string FormatActorBlock(ActorDefinition actor)
    {
        var sb = new StringBuilder();
        var ctorParams = actor.Constructor?.Declaration?.Parameters;
        sb.Append("  ");
        sb.Append(FormatCallable(actor.Name, ctorParams, null));

        var handlers = actor.MessageHandlers.Keys
            .Concat(actor.Messages.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (handlers.Count > 0)
        {
            sb.AppendLine();
            sb.Append("    handlers: ");
            sb.Append(string.Join(", ", handlers));
        }

        return sb.ToString();
    }

    private static string FormatCallable(string name, IReadOnlyList<string>? parameters, string? returnType)
    {
        var args = parameters == null || parameters.Count == 0
            ? ""
            : string.Join(", ", parameters);
        var sig = name + "(" + args + ")";
        if (!string.IsNullOrEmpty(returnType))
            sig += " -> " + returnType;
        return sig;
    }

    private static string QuoteString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string Truncate(string value)
    {
        if (value.Length <= PreviewLimit)
            return value;
        return value.Substring(0, PreviewLimit - 3) + "...";
    }
}
