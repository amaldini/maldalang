// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;

public class DependencyAnalyzer
{
    private readonly PackageStorage _storage;
    private readonly ModuleResolver _resolver;
    private readonly HashSet<string> _analyzedPackages = new();
    
    public DependencyAnalyzer()
    {
        _storage = new PackageStorage();
        _resolver = new ModuleResolver(_storage);
    }
    
    public List<PackageDependency> AnalyzeDependencies(string sourcePath)
    {
        _analyzedPackages.Clear();
        var dependencies = new List<PackageDependency>();
        
        // Parse source to find using statements
        var source = File.ReadAllText(sourcePath);
        var lexer = new Lexer(source, sourcePath);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, sourcePath);
        var statements = parser.Parse();
        
        var moduleImports = ExtractModuleImports(statements);
        
        foreach (var import in moduleImports)
        {
            if (!_analyzedPackages.Contains(import.PackageName))
            {
                var dep = ResolveDependency(import.PackageName, import.SubModule);
                if (dep != null)
                {
                    dependencies.Add(dep);
                    _analyzedPackages.Add(import.PackageName);
                }
            }
        }
        
        return dependencies;
    }
    
    private sealed record ModuleImportRef(string PackageName, string? SubModule);

    private List<ModuleImportRef> ExtractModuleImports(List<Statement> statements)
    {
        var imports = new List<ModuleImportRef>();
        
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case UsingStatement usingStmt:
                    imports.Add(new ModuleImportRef(usingStmt.PackageName, usingStmt.SubModule));
                    break;
                case ImportStatement importStmt when !importStmt.IsFileImport:
                    imports.Add(new ModuleImportRef(importStmt.PackageName!, importStmt.SubModule));
                    break;
                case BlockStatement block:
                    imports.AddRange(ExtractModuleImports(block.Statements));
                    break;
            }
        }
        
        return imports;
    }
    
    private PackageDependency? ResolveDependency(string packageName, string? subModule)
    {
        // Check if package is installed
        if (!_resolver.IsPackageInstalled(packageName))
        {
            return null; // Package not found - will be reported as error
        }
        
        var version = _resolver.GetInstalledVersion(packageName);
        if (version == null)
        {
            return null;
        }
        
        var metadata = _storage.LoadPackageMetadata(packageName, version);
        if (metadata == null)
        {
            return null;
        }
        
        // Resolve module path
        var modulePath = _resolver.ResolveModulePath(packageName, subModule);
        
        return new PackageDependency
        {
            PackageName = packageName,
            Version = version,
            SubModule = subModule,
            ModulePath = modulePath,
            Metadata = metadata
        };
    }
    
    public List<PackageDependency> GetAllDependencies(List<PackageDependency> directDependencies)
    {
        var allDependencies = new List<PackageDependency>(directDependencies);
        var processed = new HashSet<string>();
        
        foreach (var dep in directDependencies)
        {
            processed.Add($"{dep.PackageName}@{dep.Version}");
        }
        
        // Resolve transitive dependencies
        var queue = new Queue<PackageDependency>(directDependencies);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Metadata?.Dependencies != null)
            {
                foreach (var transitiveDep in current.Metadata.Dependencies)
                {
                    var depKey = $"{transitiveDep.Key}@{transitiveDep.Value}";
                    if (!processed.Contains(depKey))
                    {
                        // Resolve transitive dependency
                        var resolved = ResolveDependency(transitiveDep.Key, null);
                        if (resolved != null)
                        {
                            allDependencies.Add(resolved);
                            processed.Add(depKey);
                            queue.Enqueue(resolved);
                        }
                    }
                }
            }
        }
        
        return allDependencies;
    }
}

public class PackageDependency
{
    public string PackageName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? SubModule { get; set; }
    public string? ModulePath { get; set; }
    public PackageMetadata? Metadata { get; set; }
}
