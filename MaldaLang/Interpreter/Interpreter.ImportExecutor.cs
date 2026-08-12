// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;
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
            stmt.SourceFile ?? _currentFile,
            selectedNames: null);
    }

    private async Task<RuntimeValue?> ExecuteImportViaImportExecutorAsync(ImportStatement stmt)
    {
        if (stmt.IsFileImport)
        {
            return await MergeImportedFileModuleAsync(
                stmt.FilePath!,
                stmt.Alias,
                stmt.SourceFile ?? _currentFile,
                stmt.SelectedNames);
        }

        return await MergeImportedModuleAsync(
            stmt.PackageName!,
            stmt.SubModule,
            stmt.Alias,
            stmt.SourceFile ?? _currentFile,
            stmt.SelectedNames);
    }

    private async Task<RuntimeValue?> MergeImportedFileModuleAsync(
        string filePath,
        string? alias,
        string? sourceFileName,
        IReadOnlyList<string>? selectedNames)
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

        // Selective imports always merge into the current scope (no alias form).
        var useNamespace = alias != null && selectedNames == null;
        return MergeModuleSymbols(
            loadResult,
            alias ?? Path.GetFileNameWithoutExtension(filePath),
            useNamespace,
            selectedNames);
    }

    private async Task<RuntimeValue?> MergeImportedModuleAsync(
        string packageName,
        string? subModule,
        string? alias,
        string? sourceFileName,
        IReadOnlyList<string>? selectedNames)
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

        var useNamespace = alias != null && selectedNames == null;
        if (loadResult != null)
            return MergeModuleSymbols(loadResult, alias ?? packageName, useNamespace, selectedNames);

        var dotNetSymbols = moduleEnvironment.GetAllVariables();
        if (selectedNames != null)
            dotNetSymbols = ModuleExports.FilterSelectedSymbols(dotNetSymbols, selectedNames);
        return MergeRawSymbols(dotNetSymbols, alias ?? packageName, useNamespace);
    }

    private RuntimeValue? MergeModuleSymbols(
        ModuleLoadResult loadResult,
        string targetName,
        bool useNamespaceObject,
        IReadOnlyList<string>? selectedNames)
    {
        var moduleSymbols = ModuleExports.FilterExportedSymbols(
            loadResult.Environment.GetOwnVariables(),
            loadResult.ExplicitExports);
        if (selectedNames != null)
            moduleSymbols = ModuleExports.FilterSelectedSymbols(moduleSymbols, selectedNames);
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
