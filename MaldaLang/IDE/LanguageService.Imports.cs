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
    private static string? GetImportStringPathContext(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;

            var rawLine = lines[line];
            var trimmed = rawLine.TrimStart();
            if (!trimmed.StartsWith("import", StringComparison.Ordinal))
                return null;

            var quoteIndex = trimmed.IndexOf('"');
            if (quoteIndex < 0)
                return null;

            var lineStartOffset = rawLine.Length - trimmed.Length;
            var quotePosInRaw = lineStartOffset + quoteIndex;
            if (column <= quotePosInRaw)
                return null;

            var afterQuote = trimmed.Substring(quoteIndex + 1);
            var cursorInTrimmed = column - lineStartOffset;
            var cursorAfterQuote = cursorInTrimmed - (quoteIndex + 1);
            if (cursorAfterQuote < 0 || cursorAfterQuote > afterQuote.Length)
                return null;

            var partial = afterQuote.Substring(0, Math.Min(cursorAfterQuote, afterQuote.Length));
            if (partial.Contains('"'))
                return null;

            return partial;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetModulePackageImportContext(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;

            var currentLine = lines[line].TrimStart();
            string? keyword = null;
            if (currentLine.StartsWith("import", StringComparison.Ordinal))
                keyword = "import";
            else if (currentLine.StartsWith("using", StringComparison.Ordinal))
                keyword = "using";
            else
                return null;

            if (currentLine.Contains('"'))
                return null;

            if (column < keyword.Length)
                return null;

            var afterKeyword = currentLine.Substring(keyword.Length).TrimStart();
            if (afterKeyword.StartsWith('='))
                return null;

            var cursorPosInLine = column - (lines[line].Length - currentLine.Length);
            var cursorPosInKeyword = cursorPosInLine - keyword.Length;
            if (cursorPosInKeyword < 0 || cursorPosInKeyword > afterKeyword.Length)
                return null;

            var partialName = afterKeyword.Substring(0, Math.Min(cursorPosInKeyword, afterKeyword.Length));
            partialName = partialName.TrimEnd(' ', '\t', ';');
            if (partialName.Contains('='))
            {
                var eq = partialName.IndexOf('=');
                partialName = partialName[(eq + 1)..].TrimStart();
            }

            return partialName;
        }
        catch
        {
            return null;
        }
    }

    private static List<CompletionItem> GetRelativeMaldaFileCompletions(string? sourceFileName, string partialPath)
    {
        var completions = new List<CompletionItem>();
        try
        {
            var baseDir = !string.IsNullOrWhiteSpace(sourceFileName)
                ? Path.GetDirectoryName(Path.GetFullPath(sourceFileName))
                : Environment.CurrentDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Environment.CurrentDirectory;

            var searchDir = baseDir;
            if (!string.IsNullOrWhiteSpace(partialPath))
            {
                var dirPart = Path.GetDirectoryName(partialPath);
                if (!string.IsNullOrWhiteSpace(dirPart))
                    searchDir = Path.GetFullPath(Path.Combine(baseDir, dirPart));
            }

            if (!Directory.Exists(searchDir))
                return completions;

            foreach (var file in Directory.EnumerateFiles(searchDir, "*.malda"))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(partialPath) &&
                    !name.StartsWith(Path.GetFileName(partialPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                completions.Add(new CompletionItem
                {
                    Label = name,
                    Kind = "file",
                    Detail = "Malda module",
                    InsertText = name
                });
            }
        }
        catch
        {
            // ignore
        }

        return completions;
    }

    private static void ExtractImportedSymbols(ModuleSymbolResolver.ImportedSymbolSet imported, List<CompletionItem> completions)
    {
        foreach (var funcDecl in imported.Functions)
        {
            completions.Add(new CompletionItem
            {
                Label = funcDecl.Name,
                Kind = "function",
                Detail = $"imported function {funcDecl.Name}({string.Join(", ", funcDecl.Parameters)})",
                InsertText = funcDecl.Name + "()"
            });
        }

        foreach (var classDecl in imported.Classes)
        {
            completions.Add(new CompletionItem
            {
                Label = classDecl.Name,
                Kind = "class",
                Detail = "imported class",
                InsertText = classDecl.Name
            });
        }

        foreach (var varDecl in imported.Variables)
        {
            completions.Add(new CompletionItem
            {
                Label = varDecl.Name,
                Kind = "variable",
                Detail = "imported variable",
                InsertText = varDecl.Name
            });
        }
    }
    
    private List<CompletionItem> GetPackageCompletions(string partialName)
    {
        var completions = new List<CompletionItem>();
        
        try
        {
            var storage = new PackageStorage();
            var packages = storage.GetInstalledPackages();
            
            // Filter packages that match partial name
            var matchingPackages = packages.Where(p => 
                string.IsNullOrEmpty(partialName) || 
                p.StartsWith(partialName, StringComparison.OrdinalIgnoreCase));
            
            foreach (var packageName in matchingPackages)
            {
                var versions = storage.GetInstalledVersions(packageName);
                if (versions.Length > 0)
                {
                    var metadata = storage.LoadPackageMetadata(packageName, versions[0]);
                    var description = metadata?.Description ?? "Package";
                    
                    completions.Add(new CompletionItem
                    {
                        Label = packageName,
                        Kind = "package",
                        Detail = description,
                        InsertText = packageName
                    });
                }
            }
            
            // Also add common .NET namespaces
            var dotNetNamespaces = new[]
            {
                "System", "System.Collections.Generic", "System.Linq", 
                "System.IO", "System.Text", "System.Threading"
            };
            
            foreach (var ns in dotNetNamespaces)
            {
                if (string.IsNullOrEmpty(partialName) || 
                    ns.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                {
                    completions.Add(new CompletionItem
                    {
                        Label = ns,
                        Kind = "namespace",
                        Detail = ".NET namespace",
                        InsertText = ns
                    });
                }
            }
        }
        catch
        {
            // If package storage fails, return empty list
        }
        
        return completions;
    }
}
