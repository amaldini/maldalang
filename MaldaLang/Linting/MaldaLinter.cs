// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaldaLang.Formatting;
using MaldaLang.IDE;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

/// <summary>Findings for one source, after suppressions.</summary>
public sealed class LintResult
{
    public List<LintFinding> Findings { get; } = new();
    /// <summary>Findings removed by <c>malda-lint-disable-*</c> comments.</summary>
    public int SuppressedCount { get; internal set; }
    /// <summary>False when the source did not parse (one <see cref="LintRules.ParseFailed"/> finding).</summary>
    public bool Parsed { get; internal set; } = true;

    public int Count(LintSeverity severity) => Findings.Count(f => f.Severity == severity);
    /// <summary>True when a finding is a warning or an error (the CLI then exits 1).</summary>
    public bool HasFailures => Findings.Any(f => f.Severity >= LintSeverity.Warning);
}

/// <summary>
/// <c>malda lint</c> (I6): gotchas from <c>docs/llm/malda-gotchas.md</c> that a tool can detect
/// reliably, as stable <c>ML0xxx</c> findings. Rules run on the parser AST (walked with
/// <see cref="AstNodeWalker"/>) and, for constructs the parser drops, on the lossless
/// <see cref="SourceTriviaScanner"/> tokens, which also carry the suppression comments.
/// Lint does not repeat what <c>malda check</c> already reports (malda-interp, malda-gotcha,
/// WF1001/WF1002, UI1001–UI1003, …); see <see cref="LintRules"/>.
/// </summary>
public static partial class MaldaLinter
{
    public static LintResult Lint(string source, string? sourceFileName = null)
    {
        var result = new LintResult();
        List<Statement> statements;
        try
        {
            var tokens = new Lexer(source, sourceFileName).Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            statements = parser.Parse();
            if (parser.Errors.Count > 0)
            {
                var first = parser.Errors[0];
                AddParseFailure(result, first.Line, first.Column, parser.Errors.Count, IncludedFile(first.SourceFileName, sourceFileName));
                return result;
            }
        }
        catch (ParseException ex)
        {
            AddParseFailure(result, ex.Line, ex.Column, 1, IncludedFile(ex.SourceFileName, sourceFileName));
            return result;
        }
        catch (Exception ex)
        {
            result.Parsed = false;
            result.Findings.Add(new LintFinding(
                LintRules.ParseFailed,
                LintSeverity.Error,
                $"The source does not tokenize ({ex.Message}); lint rules did not run. Run malda check for details.",
                1,
                1,
                1));
            return result;
        }

        var context = new LintContext(source, statements, sourceFileName);
        RunRules(context);

        var suppressions = LintSuppressions.Parse(source);
        // Findings on included code honour the suppression comments of the file they are in.
        var includedSuppressions = new Dictionary<string, LintSuppressions>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in context.Findings
                     .GroupBy(f => (f.File, f.Code, f.Line, f.Column))
                     .Select(g => g.First())
                     .OrderBy(f => f.File != null)
                     .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(f => f.Line)
                     .ThenBy(f => f.Column)
                     .ThenBy(f => f.Code, StringComparer.Ordinal))
        {
            var fileSuppressions = finding.File == null
                ? suppressions
                : SuppressionsFor(finding.File, includedSuppressions);
            if (fileSuppressions.IsSuppressed(finding.Code, finding.Line))
            {
                result.SuppressedCount++;
                continue;
            }

            result.Findings.Add(finding);
        }

        return result;
    }

    private static void AddParseFailure(LintResult result, int line, int column, int errorCount, string? includedFile)
    {
        result.Parsed = false;
        var message = includedFile == null
            ? $"The file has {errorCount} parse error(s); lint rules did not run. Run malda check for the parser diagnostics."
            : "An included file has a parse error; lint rules did not run. Run malda check for the parser diagnostics.";
        result.Findings.Add(new LintFinding(
            LintRules.ParseFailed,
            LintSeverity.Error,
            message,
            Math.Max(1, line),
            Math.Max(1, column),
            1,
            File: includedFile));
    }

    /// <summary><paramref name="file"/> when it is not the linted source itself (an included file), else null.</summary>
    internal static string? IncludedFile(string? file, string? sourceFileName) =>
        string.IsNullOrWhiteSpace(file) || DiagnosticSourceFiles.IsSameFile(file, sourceFileName) ? null : file;

    private static LintSuppressions SuppressionsFor(string file, Dictionary<string, LintSuppressions> cache)
    {
        if (cache.TryGetValue(file, out var cached))
            return cached;

        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch
        {
            text = "";
        }

        var parsed = LintSuppressions.Parse(text);
        cache[file] = parsed;
        return parsed;
    }

    private static void RunRules(LintContext context)
    {
        CheckPlainStringInterpolation(context);
        CheckUncheckedModelJson(context);
        CheckModeBPrompts(context);
        CheckWorkflowEffects(context);
        CheckLambdaAssignments(context);
        CheckStepsInLoops(context);
        CheckCaseBindsVar(context);
        CheckAlwaysTruthyConditions(context);
        CheckMapReturnsWrapper(context);
        CheckStringFunctionOnGetEnv(context);
        CheckRepeatFractionalCount(context);
        CheckDispatchEventSession(context);
        CheckUnreachableCases(context);
    }

    /// <summary>"name" for <c>name(...)</c>, "ns.member" for <c>ns.member(...)</c>, ".member" for other receivers.</summary>
    internal static string CalleeName(FunctionCallExpression call) => call.Callee switch
    {
        IdentifierExpression id => id.Name,
        MemberAccessExpression { Object: IdentifierExpression ns } member => ns.Name + "." + member.Member,
        MemberAccessExpression member => "." + member.Member,
        _ => ""
    };

    /// <summary>Shared per-file state: the AST, declared names, and the findings collected so far.</summary>
    internal sealed class LintContext
    {
        public LintContext(string source, List<Statement> statements, string? sourceFileName = null)
        {
            Source = source;
            Statements = statements;
            SourceFileName = sourceFileName;
            Collect();
        }

        public string Source { get; }
        /// <summary>The linted file; nodes from an <c>include</c>d file carry another <see cref="Node.SourceFile"/>.</summary>
        public string? SourceFileName { get; }
        public List<Statement> Statements { get; }
        public List<LintFinding> Findings { get; } = new();

        /// <summary>Top-level functions by name (the same-file call graph used by workflow rules).</summary>
        public Dictionary<string, FunctionDeclaration> Functions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, PromptDeclaration> Prompts { get; } = new(StringComparer.Ordinal);
        public HashSet<string> VarNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ConstNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ConstructorNames { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ImportedNames { get; } = new(StringComparer.Ordinal);
        /// <summary>Every name the code uses or declares (identifiers, variables, parameters, loop variables).</summary>
        public HashSet<string> CodeNames { get; } = new(StringComparer.Ordinal);

        /// <summary>Every node of the program, optionally pruned below nodes <paramref name="descendInto"/> rejects.</summary>
        public IEnumerable<Node> Nodes(Func<Node, bool>? descendInto = null)
        {
            foreach (var statement in Statements)
            {
                foreach (var node in AstNodeWalker.Descendants(statement, descendInto))
                    yield return node;
            }
        }

        /// <summary>Report at a position in <paramref name="file"/> (null: the linted source).</summary>
        public void Report(string code, int line, int column, int length, string message, string? hint = null, string? file = null)
        {
            var rule = LintRules.Get(code);
            Findings.Add(new LintFinding(code, rule.Severity, message, Math.Max(1, line), Math.Max(1, column), Math.Max(1, length), hint, file));
        }

        /// <summary>Report at <paramref name="node"/>, or at <paramref name="fallback"/> when the parser left the node without a position.</summary>
        public void Report(string code, Node node, int length, string message, string? hint = null, Node? fallback = null)
        {
            var at = node.Line <= 0 && fallback != null ? fallback : node;
            Report(code, at.Line, at.Column, length, message, hint, IncludedFile(at.SourceFile, SourceFileName));
        }

        private void Collect()
        {
            foreach (var statement in Statements)
            {
                switch (statement)
                {
                    case FunctionDeclaration function:
                        Functions[function.Name] = function;
                        break;
                    case PromptDeclaration prompt:
                        Prompts[prompt.Name] = prompt;
                        break;
                    case ImportStatement { SelectedNames: not null } import:
                        ImportedNames.UnionWith(import.SelectedNames);
                        break;
                }
            }

            foreach (var node in Nodes())
            {
                switch (node)
                {
                    case VarDeclStatement varDecl:
                        (varDecl.IsConst ? ConstNames : VarNames).Add(varDecl.Name);
                        CodeNames.Add(varDecl.Name);
                        break;
                    case TypeDeclaration typeDecl:
                        foreach (var constructor in typeDecl.Constructors)
                            ConstructorNames.Add(constructor.Name);
                        break;
                    case IdentifierExpression identifier:
                        CodeNames.Add(identifier.Name);
                        break;
                    case FunctionDeclaration function:
                        CodeNames.Add(function.Name);
                        CodeNames.UnionWith(function.Parameters);
                        break;
                    case LambdaExpression lambda:
                        CodeNames.UnionWith(lambda.Parameters);
                        break;
                    case ForInStatement forIn:
                        CodeNames.Add(forIn.VariableName);
                        break;
                    case WorkflowDeclaration workflow:
                        CodeNames.UnionWith(workflow.Parameters);
                        break;
                }
            }
        }
    }
}
