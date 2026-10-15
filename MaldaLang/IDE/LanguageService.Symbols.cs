// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public partial class LanguageService
{
    private void TryExtractDocumentSymbols(
        string source,
        string? sourceFileName,
        List<CompletionItem> completions,
        int line,
        int column,
        CancellationToken cancellationToken)
    {
        var statements = ParseStatements(source, sourceFileName, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ExtractSymbols(statements, completions, line, column);
        var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
        ExtractImportedSymbols(imported, completions);
    }

    /// <summary>
    /// Completes an unfinished assignment/call on the cursor line so declared
    /// schemas, prompts, and locals still appear in IntelliSense.
    /// </summary>
    private static string RecoverIncompleteSource(string source, int line)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        if (line >= 0 && line < lines.Length)
        {
            var current = lines[line].TrimEnd();
            if (current.EndsWith('=') || current.EndsWith('(') || current.EndsWith(','))
            {
                lines[line] = current + " null";
                return string.Join('\n', lines);
            }
        }

        return source + "\nnull";
    }

    /// <summary>
    /// Drops a cursor line that is only <c>name.</c> or <c>name.partial</c> so earlier
    /// declarations still parse for member completion.
    /// </summary>
    private static string? StripUnfinishedMemberAccessLine(string source, int line)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        if (line < 0 || line >= lines.Length)
            return null;

        var current = lines[line].Trim();
        var dot = current.LastIndexOf('.');
        if (dot <= 0)
            return null;

        var after = current[(dot + 1)..];
        if (after.Length > 0 && after.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
            return null;

        var before = current[..dot].Trim();
        if (before.Length == 0 || before.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
            return null;

        lines[line] = string.Empty;
        return string.Join('\n', lines);
    }

    private void ExtractSymbols(List<MaldaLang.Parser.AST.Statements.Statement> statements, 
        List<CompletionItem> completions, int line, int column)
    {
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = classDecl.Name,
                    Kind = "class",
                    Detail = "Class",
                    InsertText = classDecl.Name
                });
            }
            else if (stmt is MaldaLang.Parser.AST.Declarations.FunctionDeclaration funcDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = funcDecl.Name,
                    Kind = "function",
                    Detail = $"function {funcDecl.Name}({string.Join(", ", funcDecl.Parameters)})",
                    InsertText = funcDecl.Name + "()"
                });
            }
            else if (stmt is MaldaLang.Parser.AST.Declarations.PromptDeclaration promptDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = promptDecl.Name,
                    Kind = "function",
                    Detail = $"prompt {promptDecl.Name}({string.Join(", ", promptDecl.Parameters)})",
                    InsertText = promptDecl.Name + "()"
                });
            }
            else if (stmt is SchemaDeclaration schemaDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = schemaDecl.Name,
                    Kind = "class",
                    Detail = "schema",
                    InsertText = schemaDecl.Name
                });
            }
            else if (stmt is WorkflowDeclaration workflowDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = workflowDecl.Name,
                    Kind = "function",
                    Detail = $"workflow {workflowDecl.Name}({string.Join(", ", workflowDecl.Parameters)})",
                    InsertText = workflowDecl.Name + "()"
                });
            }
            else if (stmt is MaldaLang.Parser.AST.Statements.VarDeclStatement varDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = varDecl.Name,
                    Kind = "variable",
                    Detail = "Variable",
                    InsertText = varDecl.Name
                });
            }
        }
    }
}
