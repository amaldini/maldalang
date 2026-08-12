// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MaldaLang;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Statements;
using Environment = MaldaLang.Interpreter.Environment;

public class ModuleLoader
{
    private readonly ModuleResolver _resolver;
    private readonly Dictionary<string, ModuleLoadResult> _loadedModules = new();
    private readonly HashSet<string> _loadingModules = new();
    
    public ModuleLoader(ModuleResolver? resolver = null)
    {
        _resolver = resolver ?? new ModuleResolver();
    }
    
    public async Task<ModuleLoadResult> LoadModuleAsync(string packageName, string? subModule = null)
    {
        var moduleKey = subModule != null ? $"{packageName}.{subModule}" : packageName;
        
        if (_loadedModules.TryGetValue(moduleKey, out var cached))
            return cached;
        
        if (_loadingModules.Contains(moduleKey))
            throw new InvalidOperationException($"Circular dependency detected: {moduleKey}");
        
        _loadingModules.Add(moduleKey);
        
        try
        {
            var modulePath = _resolver.ResolveModulePath(packageName, subModule);
            if (modulePath == null)
            {
                throw new FileNotFoundException(
                    $"Module not found: {packageName}" + (subModule != null ? $".{subModule}" : ""));
            }

            var source = ReadModuleSource(modulePath, packageName, subModule);
            var result = await ExecuteModuleSourceAsync(source, modulePath, moduleKey);
            _loadedModules[moduleKey] = result;
            return result;
        }
        finally
        {
            _loadingModules.Remove(moduleKey);
        }
    }

    public async Task<ModuleLoadResult> LoadFileModuleAsync(string modulePath, string? sourceFileName)
    {
        var resolvedPath = ModulePathResolver.ResolveRelativeModulePath(modulePath, sourceFileName);
        var moduleKey = "file:" + resolvedPath;

        if (_loadedModules.TryGetValue(moduleKey, out var cached))
            return cached;

        if (_loadingModules.Contains(moduleKey))
            throw new InvalidOperationException($"Circular dependency detected: {resolvedPath}");

        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Module file not found: {resolvedPath}");

        _loadingModules.Add(moduleKey);

        try
        {
            var source = File.ReadAllText(resolvedPath);
            var result = await ExecuteModuleSourceAsync(source, resolvedPath, moduleKey);
            _loadedModules[moduleKey] = result;
            return result;
        }
        finally
        {
            _loadingModules.Remove(moduleKey);
        }
    }
    
    public bool IsModuleLoaded(string packageName, string? subModule = null)
    {
        var moduleKey = subModule != null ? $"{packageName}.{subModule}" : packageName;
        return _loadedModules.ContainsKey(moduleKey);
    }
    
    public void ClearCache()
    {
        _loadedModules.Clear();
    }
    
    public Environment? GetLoadedModule(string packageName, string? subModule = null)
    {
        var moduleKey = subModule != null ? $"{packageName}.{subModule}" : packageName;
        return _loadedModules.TryGetValue(moduleKey, out var module) ? module.Environment : null;
    }

    private string ReadModuleSource(string modulePath, string packageName, string? subModule)
    {
        if (modulePath.StartsWith("embedded:", StringComparison.Ordinal))
        {
            var parts = modulePath.Substring("embedded:".Length).Split(':', 3);
            if (parts.Length != 3)
                throw new InvalidOperationException($"Invalid embedded resource path: {modulePath}");

            var embeddedPackageName = parts[0];
            var embeddedVersion = parts[1];
            var relativePath = parts[2];

            var storage = _resolver._storage;
            if (!storage.TryReadPackageFile(embeddedPackageName, embeddedVersion, relativePath, out var source))
                throw new FileNotFoundException($"Embedded module not found: {modulePath}");

            return source;
        }

        if (!File.Exists(modulePath))
        {
            throw new FileNotFoundException(
                $"Module not found: {packageName}" + (subModule != null ? $".{subModule}" : ""));
        }

        return File.ReadAllText(modulePath);
    }

    private static async Task<ModuleLoadResult> ExecuteModuleSourceAsync(string source, string sourceFileName, string moduleKey)
    {
        var lexer = new Lexer(source, sourceFileName);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens, sourceFileName);
        var statements = parser.Parse();

        if (parser.Errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Parse errors in module {moduleKey}: {string.Join(", ", parser.Errors.Select(e => e.Message))}");
        }

        var explicitExports = ModuleExports.CollectExplicitExports(statements);

        // Fresh interpreter registers math/str/io (and other builtins) on _globals.
        // Module bindings live in a child environment so lookups see stdlib, while
        // export merge uses GetOwnVariables and does not re-export those parents.
        var moduleInterpreter = new Interpreter();
        var moduleEnvironment = new Environment(moduleInterpreter._globals);
        moduleInterpreter._environment = moduleEnvironment;
        moduleInterpreter._globals = moduleEnvironment;

        await moduleInterpreter.InterpretAsync(statements);

        return new ModuleLoadResult(moduleEnvironment, explicitExports, statements);
    }
}
