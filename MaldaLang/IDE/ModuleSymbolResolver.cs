// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Statements;

public static class ModuleSymbolResolver
{
    public sealed record ResolvedImport(
        string ModuleKey,
        string? ResolvedPath,
        string? Alias,
        bool IsFileImport,
        string? PackageName,
        string? SubModule);

    public sealed class ImportedSymbolSet
    {
        public List<ClassDeclaration> Classes { get; } = new();
        public List<FunctionDeclaration> Functions { get; } = new();
        public List<VarDeclStatement> Variables { get; } = new();
        public List<ResolvedImport> Imports { get; } = new();
    }

    public static List<ResolvedImport> CollectImports(IEnumerable<Statement> statements)
    {
        var imports = new List<ResolvedImport>();
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case ImportStatement importStmt when importStmt.IsFileImport:
                    imports.Add(new ResolvedImport(
                        importStmt.FilePath!,
                        null,
                        importStmt.Alias,
                        true,
                        null,
                        null));
                    break;
                case ImportStatement importStmt:
                    imports.Add(new ResolvedImport(
                        importStmt.SubModule != null
                            ? $"{importStmt.PackageName}.{importStmt.SubModule}"
                            : importStmt.PackageName!,
                        null,
                        importStmt.Alias,
                        false,
                        importStmt.PackageName,
                        importStmt.SubModule));
                    break;
                case UsingStatement usingStmt:
                    imports.Add(new ResolvedImport(
                        usingStmt.SubModule != null
                            ? $"{usingStmt.PackageName}.{usingStmt.SubModule}"
                            : usingStmt.PackageName,
                        null,
                        usingStmt.Alias,
                        false,
                        usingStmt.PackageName,
                        usingStmt.SubModule));
                    break;
            }
        }

        return imports;
    }

    public static ImportedSymbolSet LoadImportedSymbols(
        IEnumerable<Statement> hostStatements,
        string? hostSourceFile)
    {
        var result = new ImportedSymbolSet();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new ModuleResolver();

        foreach (var import in CollectImports(hostStatements))
        {
            string? resolvedPath = null;

            if (import.IsFileImport && !string.IsNullOrWhiteSpace(import.ModuleKey))
            {
                try
                {
                    resolvedPath = ModulePathResolver.ResolveRelativeModulePath(import.ModuleKey, hostSourceFile);
                }
                catch
                {
                    result.Imports.Add(import);
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(import.PackageName))
            {
                resolvedPath = resolver.ResolveModulePath(import.PackageName, import.SubModule);
                if (resolvedPath != null && resolvedPath.StartsWith("embedded:", StringComparison.Ordinal))
                {
                    result.Imports.Add(import with { ResolvedPath = resolvedPath });
                    continue;
                }
            }
            else
            {
                result.Imports.Add(import);
                continue;
            }

            result.Imports.Add(import with { ResolvedPath = resolvedPath });

            if (string.IsNullOrWhiteSpace(resolvedPath) ||
                !visited.Add(resolvedPath) ||
                !File.Exists(resolvedPath))
            {
                continue;
            }

            try
            {
                var moduleStatements = ParseModuleStatements(resolvedPath);
                var expanded = ExpandFileImportsForTranspile(moduleStatements, resolvedPath, visited);
                AppendExportedStatements(result, expanded, import.Alias ?? Path.GetFileNameWithoutExtension(resolvedPath));
            }
            catch
            {
                // Best-effort for IDE tooling
            }
        }

        return result;
    }

    public static List<Statement> ExpandFileImportsForTranspile(
        List<Statement> statements,
        string? hostSourceFile,
        HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Statement>();

        foreach (var stmt in statements)
        {
            if (stmt is ImportStatement importStmt && importStmt.IsFileImport)
            {
                try
                {
                    var resolvedPath = ModulePathResolver.ResolveRelativeModulePath(
                        importStmt.FilePath!,
                        hostSourceFile ?? importStmt.SourceFile);
                    if (!visited.Add(resolvedPath) || !File.Exists(resolvedPath))
                        continue;

                    var moduleStatements = ParseModuleStatements(resolvedPath);
                    result.AddRange(ExpandFileImportsForTranspile(moduleStatements, resolvedPath, visited));
                }
                catch
                {
                    // Skip broken imports; runtime will report
                }

                continue;
            }

            if (stmt is ImportStatement or UsingStatement)
                continue;

            result.Add(stmt);
        }

        return result;
    }

    public static List<Statement> GetExportedStatements(IEnumerable<Statement> statements)
    {
        var statementList = statements as IList<Statement> ?? statements.ToList();
        var explicitExports = ModuleExports.CollectExplicitExports(statementList);
        var exported = new List<Statement>();

        foreach (var stmt in statementList)
        {
            switch (stmt)
            {
                case FunctionDeclaration fd when explicitExports == null || fd.IsExported:
                    exported.Add(fd);
                    break;
                case ClassDeclaration cd when explicitExports == null || cd.IsExported:
                    exported.Add(cd);
                    break;
                case VarDeclStatement vd when explicitExports == null || vd.IsExported:
                    exported.Add(vd);
                    break;
            }
        }

        return exported;
    }

    private static void AppendExportedStatements(
        ImportedSymbolSet result,
        IEnumerable<Statement> statements,
        string moduleLabel)
    {
        foreach (var stmt in GetExportedStatements(statements))
        {
            switch (stmt)
            {
                case FunctionDeclaration fd:
                    result.Functions.Add(fd);
                    break;
                case ClassDeclaration cd:
                    result.Classes.Add(cd);
                    break;
                case VarDeclStatement vd:
                    result.Variables.Add(vd);
                    break;
            }
        }
    }

    private static List<Statement> ParseModuleStatements(string resolvedPath)
    {
        var source = File.ReadAllText(resolvedPath);
        var lexer = new Lexer(source, resolvedPath);
        var parser = new MaldaLang.Parser.Parser(lexer.Tokenize(), resolvedPath);
        return parser.Parse();
    }
}
