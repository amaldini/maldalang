// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lexer = MaldaLang.Lexer;
using Token = MaldaLang.Token;
using TokenType = MaldaLang.TokenType;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;
using Parser = MaldaLang.Parser.Parser;

/// <summary>
/// REPL session source dump: keeps the original text of successfully executed
/// definitions so <c>source</c> can reprint them as MALDA code. Complements
/// <see cref="ReplSessionInventory"/>, which lists live signatures and values.
/// Commands: <c>source</c>, <c>src</c>, optionally filtered by kind or name.
/// </summary>
public sealed class ReplSessionSource
{
    public enum Kind
    {
        All,
        Variables,
        Functions,
        Classes,
        Prompts,
        Actors,
        Workflows,
        Types,
        Schemas,
        Apis,
        Imports,
        Properties,
        Suites,
        Contexts,
        Policies
    }

    private readonly List<RecordedDefinition> _definitions = new();

    /// <summary>
    /// True when <paramref name="line"/> is a REPL source command
    /// (<c>source</c> / <c>src</c>), not an assignment such as <c>source = 1</c>.
    /// <paramref name="filter"/> is the optional kind or definition name.
    /// </summary>
    public static bool TryParseCommand(string? line, out string filter)
    {
        filter = "";
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !IsSourceVerb(parts[0]))
            return false;

        if (parts.Length >= 2 && !LooksLikeFilterToken(parts[1]))
            return false;

        if (parts.Length > 1)
            filter = string.Join(" ", parts.Skip(1));
        return true;
    }

    public void Record(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        List<Statement> statements;
        try
        {
            var tokens = new Lexer(source).Tokenize();
            var parser = new Parser(tokens);
            statements = parser.Parse();
            if (parser.Errors.Count > 0)
                return;
        }
        catch
        {
            return;
        }

        Record(source, statements);
    }

    public void Record(string source, IReadOnlyList<Statement> statements)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(statements);

        foreach (var snippet in ExtractDefinitions(source, statements))
            Upsert(snippet);
    }

    public string? Peek(string name)
    {
        var matches = _definitions
            .Where(d => string.Equals(d.Name, name, StringComparison.Ordinal))
            .ToList();
        return matches.Count == 0 ? null : JoinSources(matches);
    }

    public string? TryRemove(string name)
    {
        var previous = Peek(name);
        if (previous == null)
            return null;
        _definitions.RemoveAll(d => string.Equals(d.Name, name, StringComparison.Ordinal));
        return previous;
    }

    public string Format(string? filter)
    {
        TryParseKindOrName(filter, out var kind, out var name);

        IEnumerable<RecordedDefinition> items = _definitions;
        if (name != null)
        {
            items = _definitions.Where(d =>
                string.Equals(d.Name, name, StringComparison.Ordinal));
            var named = items.ToList();
            if (named.Count == 0)
                return "(no definition named '" + name + "')";
            return JoinSources(named);
        }

        if (kind != Kind.All)
            items = _definitions.Where(d => d.Kind == kind);

        var list = items.ToList();
        if (list.Count == 0)
        {
            return kind == Kind.All
                ? "(no user definitions)"
                : "(no " + SectionTitle(kind) + ")";
        }

        return JoinSources(list);
    }

    public void Write(TextWriter writer, string? filter)
    {
        writer.WriteLine(Format(filter));
    }

    public IReadOnlyList<RecordedDefinition> Definitions => _definitions;

    public readonly struct RecordedDefinition
    {
        public RecordedDefinition(Kind kind, string name, string source)
        {
            Kind = kind;
            Name = name;
            Source = source;
        }

        public Kind Kind { get; }
        public string Name { get; }
        public string Source { get; }
    }

    internal static IReadOnlyList<RecordedDefinition> ExtractDefinitions(
        string source,
        IReadOnlyList<Statement> statements)
    {
        var tokens = new Lexer(source).Tokenize();
        var found = new List<(int Index, Statement Stmt, Kind Kind, string Name)>();
        for (var i = 0; i < statements.Count; i++)
        {
            var stmt = statements[i];
            if (!string.IsNullOrEmpty(stmt.SourceFile))
                continue;
            if (!TryDescribe(stmt, out var kind, out var name))
                continue;
            found.Add((i, stmt, kind, name));
        }

        var result = new List<RecordedDefinition>(found.Count);
        for (var f = 0; f < found.Count; f++)
        {
            var current = found[f];
            var startIndex = ExpandStart(tokens, current.Stmt);
            int endOffset;
            if (current.Index + 1 < statements.Count)
            {
                var next = statements[current.Index + 1];
                var nextStart = ExpandStart(tokens, next);
                endOffset = OffsetAt(source, tokens[nextStart].Line, tokens[nextStart].Column);
            }
            else
            {
                endOffset = source.Length;
            }

            var startOffset = current.Index == 0
                ? 0
                : OffsetAt(source, tokens[startIndex].Line, tokens[startIndex].Column);
            startOffset = Math.Clamp(startOffset, 0, source.Length);
            endOffset = Math.Clamp(endOffset, startOffset, source.Length);
            var text = source.Substring(startOffset, endOffset - startOffset).TrimEnd();
            if (text.Length == 0)
                continue;
            result.Add(new RecordedDefinition(current.Kind, current.Name, text));
        }

        return result;
    }

    private void Upsert(RecordedDefinition def)
    {
        var existing = _definitions.FindIndex(d =>
            d.Kind == def.Kind && string.Equals(d.Name, def.Name, StringComparison.Ordinal));
        if (existing < 0)
        {
            _definitions.Add(def);
            return;
        }

        if (def.Kind is Kind.Classes or Kind.Actors)
        {
            var prev = _definitions[existing];
            _definitions[existing] = new RecordedDefinition(
                prev.Kind,
                prev.Name,
                prev.Source + "\n\n" + def.Source);
            return;
        }

        _definitions[existing] = def;
    }

    private static void TryParseKindOrName(string? filter, out Kind kind, out string? name)
    {
        kind = Kind.All;
        name = null;
        if (string.IsNullOrWhiteSpace(filter))
            return;

        if (TryParseKind(filter, out kind, out _))
            return;

        name = filter.Trim();
        kind = Kind.All;
    }

    public static bool TryParseKind(string? filter, out Kind kind, out string? error)
    {
        kind = Kind.All;
        error = null;
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        if (ReplSessionInventory.TryParseKind(filter, out var inventoryKind, out var inventoryError))
        {
            kind = inventoryKind switch
            {
                ReplSessionInventory.Kind.All => Kind.All,
                ReplSessionInventory.Kind.Variables => Kind.Variables,
                ReplSessionInventory.Kind.Functions => Kind.Functions,
                ReplSessionInventory.Kind.Classes => Kind.Classes,
                ReplSessionInventory.Kind.Prompts => Kind.Prompts,
                ReplSessionInventory.Kind.Actors => Kind.Actors,
                ReplSessionInventory.Kind.Workflows => Kind.Workflows,
                _ => Kind.All
            };
            return true;
        }

        switch (filter.Trim().ToLowerInvariant())
        {
            case "types":
            case "type":
                kind = Kind.Types;
                return true;
            case "schemas":
            case "schema":
                kind = Kind.Schemas;
                return true;
            case "apis":
            case "api":
                kind = Kind.Apis;
                return true;
            case "imports":
            case "import":
            case "usings":
            case "using":
                kind = Kind.Imports;
                return true;
            case "properties":
            case "property":
                kind = Kind.Properties;
                return true;
            case "suites":
            case "suite":
                kind = Kind.Suites;
                return true;
            case "contexts":
            case "context":
                kind = Kind.Contexts;
                return true;
            case "policies":
            case "policy":
                kind = Kind.Policies;
                return true;
            default:
                error = inventoryError;
                return false;
        }
    }

    private static string JoinSources(IReadOnlyList<RecordedDefinition> items)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (sb.Length > 0)
                sb.AppendLine().AppendLine();
            sb.Append(item.Source);
        }
        return sb.ToString();
    }

    private static string SectionTitle(Kind kind) => kind switch
    {
        Kind.Variables => "variables",
        Kind.Functions => "functions",
        Kind.Classes => "classes",
        Kind.Prompts => "prompts",
        Kind.Actors => "actors",
        Kind.Workflows => "workflows",
        Kind.Types => "types",
        Kind.Schemas => "schemas",
        Kind.Apis => "apis",
        Kind.Imports => "imports",
        Kind.Properties => "properties",
        Kind.Suites => "suites",
        Kind.Contexts => "contexts",
        Kind.Policies => "policies",
        _ => "user definitions"
    };

    private static bool IsSourceVerb(string word) =>
        word.Equals("source", StringComparison.OrdinalIgnoreCase)
        || word.Equals("src", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeFilterToken(string token) =>
        token.Length > 0 && char.IsLetter(token[0]);

    private static bool TryDescribe(Statement stmt, out Kind kind, out string name)
    {
        switch (stmt)
        {
            case VarDeclStatement varDecl:
                kind = Kind.Variables;
                name = varDecl.Name;
                return true;
            case DestructuringVarDecl destructure:
                kind = Kind.Variables;
                name = "destructure@" + destructure.Line + ":" + destructure.Column;
                return true;
            case FunctionDeclaration function:
                kind = Kind.Functions;
                name = function.Name;
                return true;
            case ClassDeclaration klass:
                kind = Kind.Classes;
                name = klass.Name;
                return true;
            case PromptDeclaration prompt:
                kind = Kind.Prompts;
                name = prompt.Name;
                return true;
            case ActorDeclaration actor:
                kind = Kind.Actors;
                name = actor.Name;
                return true;
            case WorkflowDeclaration workflow:
                kind = Kind.Workflows;
                name = workflow.Name;
                return true;
            case TypeDeclaration type:
                kind = Kind.Types;
                name = type.TypeName;
                return true;
            case SchemaDeclaration schema:
                kind = Kind.Schemas;
                name = schema.Name;
                return true;
            case ApiDeclaration api:
                kind = Kind.Apis;
                name = api.Name;
                return true;
            case ImportStatement import:
                kind = Kind.Imports;
                name = ImportKey(import);
                return true;
            case UsingStatement usingStmt:
                kind = Kind.Imports;
                name = UsingKey(usingStmt);
                return true;
            case PropertyDeclaration property:
                kind = Kind.Properties;
                name = property.Name;
                return true;
            case SuiteDeclaration suite:
                kind = Kind.Suites;
                name = suite.Title;
                return true;
            case ContextDeclaration context:
                kind = Kind.Contexts;
                name = context.Name;
                return true;
            case PolicyDeclaration:
                kind = Kind.Policies;
                name = "policy";
                return true;
            default:
                kind = Kind.All;
                name = "";
                return false;
        }
    }

    private static string ImportKey(ImportStatement import)
    {
        if (!string.IsNullOrEmpty(import.FilePath))
            return import.Alias ?? import.FilePath;
        var package = import.PackageName ?? "";
        if (!string.IsNullOrEmpty(import.SubModule))
            package += "." + import.SubModule;
        return import.Alias ?? package;
    }

    private static string UsingKey(UsingStatement usingStmt)
    {
        var package = usingStmt.PackageName ?? "";
        if (!string.IsNullOrEmpty(usingStmt.SubModule))
            package += "." + usingStmt.SubModule;
        return usingStmt.Alias ?? package;
    }

    private static int ExpandStart(List<Token> tokens, Statement stmt)
    {
        var index = FindTokenIndex(tokens, stmt.Line, stmt.Column);
        if (index < 0)
            return 0;

        while (true)
        {
            while (index > 0 && IsPrefixKeyword(tokens[index - 1]))
                index--;

            if (index > 0 && tokens[index - 1].Type == TokenType.RightParen)
            {
                var left = FindMatchingLeftParen(tokens, index - 1);
                if (left >= 2
                    && tokens[left - 2].Type == TokenType.At
                    && IsDecoratorName(tokens[left - 1]))
                {
                    index = left - 2;
                    continue;
                }
            }

            break;
        }

        return index;
    }

    private static int FindTokenIndex(List<Token> tokens, int line, int column)
    {
        var fallback = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Line != line)
                continue;
            if (token.Column == column)
                return i;
            if (token.Column < column)
                fallback = i;
        }

        if (fallback >= 0)
            return fallback;

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Line > line || (tokens[i].Line == line && tokens[i].Column >= column))
                return i;
        }

        return tokens.Count == 0 ? -1 : tokens.Count - 1;
    }

    private static int FindMatchingLeftParen(List<Token> tokens, int rightIndex)
    {
        var depth = 0;
        for (var i = rightIndex; i >= 0; i--)
        {
            if (tokens[i].Type == TokenType.RightParen)
                depth++;
            else if (tokens[i].Type == TokenType.LeftParen)
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static bool IsPrefixKeyword(Token token)
    {
        switch (token.Type)
        {
            case TokenType.Export:
            case TokenType.Function:
            case TokenType.Class:
            case TokenType.Actor:
            case TokenType.Prompt:
            case TokenType.Workflow:
            case TokenType.Var:
            case TokenType.Const:
            case TokenType.Type:
            case TokenType.Schema:
            case TokenType.Api:
            case TokenType.Import:
            case TokenType.Include:
            case TokenType.Component:
            case TokenType.Property:
            case TokenType.Using:
                return true;
            case TokenType.Identifier:
                return token.Lexeme.Equals("suite", StringComparison.Ordinal)
                    || token.Lexeme.Equals("context", StringComparison.Ordinal)
                    || token.Lexeme.Equals("policy", StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private static bool IsDecoratorName(Token token) =>
        token.Type == TokenType.Identifier
        || (token.Type >= TokenType.If && token.Type <= TokenType.OnReject);

    private static int OffsetAt(string source, int line, int column)
    {
        var currentLine = 1;
        var i = 0;
        while (i < source.Length && currentLine < line)
        {
            if (source[i] == '\n')
                currentLine++;
            i++;
        }

        if (column <= 1)
            return i;
        return Math.Min(source.Length, i + column - 1);
    }
}
