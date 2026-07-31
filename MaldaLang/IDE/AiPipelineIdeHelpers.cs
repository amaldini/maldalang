// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

internal static class AiPipelineIdeHelpers
{
    internal enum BlockKind
    {
        None,
        Chain,
        Workflow
    }

    internal static BlockKind GetInnermostOpenBlockKind(string source, int zeroBasedLine, int zeroBasedColumn)
    {
        var prefix = GetSourcePrefix(source, zeroBasedLine, zeroBasedColumn);
        var chainPos = FindLastKeyword(prefix, "chain ");
        var workflowPos = FindLastKeyword(prefix, "workflow ");

        var chainOpen = chainPos >= 0 && IsBlockOpenFrom(prefix, chainPos);
        var workflowOpen = workflowPos >= 0 && IsBlockOpenFrom(prefix, workflowPos);

        if (!chainOpen && !workflowOpen)
            return BlockKind.None;
        if (chainOpen && !workflowOpen)
            return BlockKind.Chain;
        if (workflowOpen && !chainOpen)
            return BlockKind.Workflow;

        return chainPos > workflowPos ? BlockKind.Chain : BlockKind.Workflow;
    }

    internal static bool IsInsideChainBlock(
        string source,
        int zeroBasedLine,
        int zeroBasedColumn,
        IReadOnlyList<Statement>? statements = null)
    {
        var targetLine = zeroBasedLine + 1;
        if (statements != null)
        {
            foreach (var stmt in statements)
            {
                if (stmt is ChainDeclaration chain && PositionInChainBody(chain, targetLine))
                    return true;
            }
        }

        return GetInnermostOpenBlockKind(source, zeroBasedLine, zeroBasedColumn) == BlockKind.Chain;
    }

    internal static IEnumerable<string> GetInScopeChainStepNames(
        IReadOnlyList<Statement> statements,
        string source,
        int zeroBasedLine,
        int zeroBasedColumn)
    {
        if (!IsInsideChainBlock(source, zeroBasedLine, zeroBasedColumn, statements))
            yield break;

        var targetLine = zeroBasedLine + 1;
        var targetColumn = zeroBasedColumn + 1;

        foreach (var stmt in statements)
        {
            if (stmt is not ChainDeclaration chain || !PositionInChainBody(chain, targetLine))
                continue;

            foreach (var bodyStmt in chain.Body.Statements)
            {
                if (bodyStmt is not VarDeclStatement varDecl)
                    continue;

                if (bodyStmt.Line > targetLine)
                    break;
                if (bodyStmt.Line == targetLine && bodyStmt.Column > targetColumn)
                    break;

                yield return varDecl.Name;
            }
        }
    }

    private static bool PositionInChainBody(ChainDeclaration chain, int line1Based)
    {
        if (chain.Body.Statements.Count == 0)
            return line1Based >= chain.Line;

        var minLine = chain.Body.Statements.Min(s => s.Line);
        var maxLine = chain.Body.Statements.Max(GetStatementEndLine);
        return line1Based >= minLine && line1Based <= maxLine;
    }

    private static int GetStatementEndLine(Statement statement)
    {
        var line = statement.Line;
        if (statement is BlockStatement block)
        {
            foreach (var inner in block.Statements)
                line = Math.Max(line, GetStatementEndLine(inner));
        }

        return line;
    }

    private static string GetSourcePrefix(string source, int zeroBasedLine, int zeroBasedColumn)
    {
        var lines = source.Split('\n');
        if (zeroBasedLine < 0 || zeroBasedLine >= lines.Length)
            return source;

        var prefixLines = lines.Take(zeroBasedLine).ToList();
        var current = lines[zeroBasedLine];
        var clampedColumn = Math.Clamp(zeroBasedColumn, 0, current.Length);
        prefixLines.Add(current[..clampedColumn]);
        return string.Join('\n', prefixLines);
    }

    private static int FindLastKeyword(string text, string keyword)
    {
        var index = -1;
        var start = 0;
        while (true)
        {
            var found = text.IndexOf(keyword, start, StringComparison.Ordinal);
            if (found < 0)
                break;
            if (IsWordBoundaryBefore(text, found) && IsWordBoundaryAfter(text, found + keyword.Length))
                index = found;
            start = found + keyword.Length;
        }

        return index;
    }

    private static bool IsWordBoundaryBefore(string text, int index)
    {
        if (index == 0)
            return true;
        return !char.IsLetterOrDigit(text[index - 1]) && text[index - 1] != '_';
    }

    private static bool IsWordBoundaryAfter(string text, int index)
    {
        if (index >= text.Length)
            return true;
        return !char.IsLetterOrDigit(text[index]) && text[index] != '_';
    }

    private static bool IsBlockOpenFrom(string text, int keywordStart)
    {
        var slice = text[keywordStart..];
        var depth = 0;
        var foundOpen = false;
        foreach (var ch in slice)
        {
            if (ch == '{')
            {
                depth++;
                foundOpen = true;
            }
            else if (ch == '}')
            {
                depth--;
            }
        }

        return foundOpen && depth > 0;
    }
}
