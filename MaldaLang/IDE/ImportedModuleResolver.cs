// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System;
using System.Collections.Generic;
using System.IO;
using MaldaLang.PackageManager;

/// <summary>
/// Locates <c>include "…";</c> and file-form <c>import "…";</c> module references so
/// tooling can jump to the target file, and so unresolved modules stop being silent
/// (see <see cref="ModuleSymbolResolver"/>'s best-effort catch).
/// </summary>
public static class ImportedModuleResolver
{
    /// <summary>How far ahead of <c>include</c> / <c>import</c> the path literal may sit.</summary>
    private const int MaxTokensToPathLiteral = 16;

    public readonly record struct ImportedModuleSpan(int Line, int Column, int Length);

    public sealed class ImportedModuleReference
    {
        /// <summary><c>include "x";</c> (parse-time splice) vs file-form <c>import "x";</c>.</summary>
        public bool IsInclude { get; init; }

        /// <summary>Literal module path exactly as written in the source.</summary>
        public string ModulePath { get; init; } = string.Empty;

        /// <summary>Span of the quoted path literal (0-based line / column).</summary>
        public ImportedModuleSpan Span { get; init; }

        public int Line => Span.Line;
        public int Column => Span.Column;
        public int Length => Span.Length;
    }

    /// <summary>
    /// Module references in a single file, in source order, without parsing successfully.
    /// A file that half-parses still yields the includes / imports above the error.
    /// </summary>
    public static IReadOnlyList<ImportedModuleReference> CollectModules(
        string source,
        string? sourceFileName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(source) ||
            !TryGetTokens(source, sourceFileName, cancellationToken, out var tokens))
        {
            return Array.Empty<ImportedModuleReference>();
        }

        var modules = new List<ImportedModuleReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < tokens.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = tokens[i];
            var isInclude = token.Type == TokenType.Include;
            if (!isInclude && token.Type != TokenType.Import)
            {
                continue;
            }

            var pathToken = FindPathLiteral(tokens, i);
            if (pathToken == null)
            {
                continue;
            }

            var modulePath = pathToken.Literal as string;
            if (string.IsNullOrWhiteSpace(modulePath))
            {
                modulePath = pathToken.Lexeme?.Trim('"', '\'');
            }

            if (string.IsNullOrWhiteSpace(modulePath))
            {
                continue;
            }

            var key = (isInclude ? "include:" : "import:") + modulePath;
            if (!seen.Add(key))
            {
                continue;
            }

            modules.Add(new ImportedModuleReference
            {
                IsInclude = isInclude,
                ModulePath = modulePath!,
                Span = BuildSpan(pathToken)
            });
        }

        return modules;
    }

    /// <summary>Module reference whose path literal covers the caret, if any.</summary>
    public static bool TryGetModuleAt(
        IReadOnlyList<ImportedModuleReference> modules,
        int zeroBasedLine,
        int zeroBasedColumn,
        out ImportedModuleReference module)
    {
        foreach (var candidate in modules)
        {
            if (candidate.Line == zeroBasedLine &&
                zeroBasedColumn >= candidate.Column &&
                zeroBasedColumn <= candidate.Column + candidate.Length)
            {
                module = candidate;
                return true;
            }
        }

        module = null!;
        return false;
    }

    /// <summary>Absolute path of the module target, or null when it cannot be resolved.</summary>
    public static string? ResolvePath(ImportedModuleReference module, string? sourceFileName)
    {
        return ModulePathResolver.TryResolveRelativeModulePath(module.ModulePath, sourceFileName, out var resolvedPath)
            ? resolvedPath
            : null;
    }

    private static Token? FindPathLiteral(List<Token> tokens, int keywordIndex)
    {
        var limit = Math.Min(tokens.Count, keywordIndex + 1 + MaxTokensToPathLiteral);
        for (var i = keywordIndex + 1; i < limit; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.String)
            {
                return token;
            }

            // A new declaration / statement before any string means this was not a file import.
            if (token.Type is TokenType.Semicolon or TokenType.Include or TokenType.Import)
            {
                return null;
            }
        }

        return null;
    }

    private static ImportedModuleSpan BuildSpan(Token token)
    {
        var line = Math.Max(0, token.Line - 1);
        var column = Math.Max(0, token.Column - 1);
        var length = Math.Max(1, (token.Lexeme ?? string.Empty).Length);
        return new ImportedModuleSpan(line, column, length);
    }

    private static bool TryGetTokens(
        string source,
        string? sourceFileName,
        CancellationToken cancellationToken,
        out List<Token> tokens)
    {
        tokens = new List<Token>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            tokens = new Lexer(source, sourceFileName).Tokenize();
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Modules whose target file is absent. A host key that is not a real file (an unsaved
    /// buffer such as <c>main.malda</c>) is skipped, so untitled documents stay quiet.
    /// </summary>
    public static IReadOnlyList<(ImportedModuleReference Module, string ResolvedPath)> CollectUnresolvedModules(
        string source,
        string? sourceFileName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
        {
            return Array.Empty<(ImportedModuleReference, string)>();
        }

        var hostIsReal = File.Exists(sourceFileName) || Path.IsPathRooted(sourceFileName);
        if (!hostIsReal)
        {
            return Array.Empty<(ImportedModuleReference, string)>();
        }

        var unresolved = new List<(ImportedModuleReference, string)>();
        foreach (var module in CollectModules(source, sourceFileName, cancellationToken))
        {
            var resolvedPath = ResolvePath(module, sourceFileName);
            if (resolvedPath == null || File.Exists(resolvedPath))
            {
                continue;
            }

            unresolved.Add((module, resolvedPath));
        }

        return unresolved;
    }
}
