// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.IO;
using MaldaLang.PackageManager;
using MaldaLang.Parser.AST.Statements;

public partial class Interpreter
{
    private async Task<RuntimeValue?> ExecuteUsingViaImportExecutorAsync(UsingStatement stmt)
    {
        return await MergeImportedModuleAsync(
            stmt.PackageName,
            stmt.SubModule,
            stmt.Alias,
            stmt.SourceFile ?? _currentFile);
    }

    private async Task<RuntimeValue?> ExecuteImportViaImportExecutorAsync(ImportStatement stmt)
    {
        if (stmt.IsFileImport)
        {
            return await MergeImportedFileModuleAsync(stmt.FilePath!, stmt.Alias, stmt.SourceFile ?? _currentFile);
        }

        return await MergeImportedModuleAsync(
            stmt.PackageName!,
            stmt.SubModule,
            stmt.Alias,
            stmt.SourceFile ?? _currentFile);
    }

    private async Task<RuntimeValue?> MergeImportedFileModuleAsync(string filePath, string? alias, string? sourceFileName)
    {
        if (_moduleLoader == null)
            throw new RuntimeException("Module loader not initialized");

        ModuleLoadResult loadResult;
        try
        {
            loadResult = await _moduleLoader.LoadFileModuleAsync(filePath, sourceFileName);
        }
        catch (FileNotFoundException ex)
        {
            throw new RuntimeException(ex.Message);
        }

        var moduleKey = "file:" + ModulePathResolver.ResolveRelativeModulePath(filePath, sourceFileName);
        _importedModules[moduleKey] = loadResult.Environment;

        return MergeModuleSymbols(loadResult, alias ?? Path.GetFileNameWithoutExtension(filePath), alias != null);
    }

    private async Task<RuntimeValue?> MergeImportedModuleAsync(
        string packageName,
        string? subModule,
        string? alias,
        string? sourceFileName)
    {
        Environment? moduleEnvironment = null;
        ModuleLoadResult? loadResult = null;
        string moduleKey = "";

        var isDotNetNamespace = packageName.Contains('.') &&
                               (packageName.StartsWith("System") ||
                                packageName.StartsWith("Microsoft") ||
                                packageName.StartsWith("Windows"));

        if (isDotNetNamespace && _dotNetWrapper != null)
        {
            try
            {
                moduleEnvironment = _dotNetWrapper.LoadDotNetNamespace(packageName);
                moduleKey = $"dotnet:{packageName}";
            }
            catch
            {
                isDotNetNamespace = false;
            }
        }

        if (!isDotNetNamespace)
        {
            if (_moduleLoader == null)
                throw new RuntimeException("Module loader not initialized");

            try
            {
                loadResult = await _moduleLoader.LoadModuleAsync(packageName, subModule);
                moduleEnvironment = loadResult.Environment;
                moduleKey = subModule != null ? $"{packageName}.{subModule}" : packageName;
            }
            catch (FileNotFoundException)
            {
                throw new RuntimeException($"Package or module not found: {packageName}" +
                    (subModule != null ? $".{subModule}" : ""));
            }
        }

        if (moduleEnvironment == null)
            throw new RuntimeException($"Failed to load module: {packageName}");

        _importedModules[moduleKey] = moduleEnvironment;

        if (loadResult != null)
            return MergeModuleSymbols(loadResult, alias ?? packageName, alias != null);

        var dotNetSymbols = moduleEnvironment.GetAllVariables();
        return MergeRawSymbols(dotNetSymbols, alias ?? packageName, alias != null);
    }

    private RuntimeValue? MergeModuleSymbols(ModuleLoadResult loadResult, string targetName, bool useNamespaceObject)
    {
        var moduleSymbols = ModuleExports.FilterExportedSymbols(
            loadResult.Environment.GetAllVariables(),
            loadResult.ExplicitExports);
        return MergeRawSymbols(moduleSymbols, targetName, useNamespaceObject);
    }

    private RuntimeValue? MergeRawSymbols(
        Dictionary<string, RuntimeValue> moduleSymbols,
        string targetName,
        bool useNamespaceObject)
    {
        if (useNamespaceObject)
        {
            var namespaceObj = new ObjectInstance(null);
            foreach (var symbol in moduleSymbols)
                namespaceObj.Set(symbol.Key, symbol.Value);
            _environment.Define(targetName, RuntimeValue.Object(namespaceObj));
        }
        else
        {
            foreach (var symbol in moduleSymbols)
            {
                if (!_environment.Contains(symbol.Key))
                    _environment.Define(symbol.Key, symbol.Value);
            }
        }

        return null;
    }
}
