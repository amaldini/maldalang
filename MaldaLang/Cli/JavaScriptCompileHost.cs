// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Cli;

using System;
using System.IO;
using System.Reflection;
using MaldaLang.Scaffolding;

/// <summary>
/// Invokes <c>MaldaLang.Compiler.Compiler.CompileToJavaScript</c> without a project
/// reference from the CLI (same dynamic load as <c>malda compile --mode js</c>).
/// </summary>
internal static class JavaScriptCompileHost
{
    private static readonly string[] ConfigurationPreference = { "Release", "Debug" };

    public static JavaScriptCompileResult CompileToJavaScript(string sourcePath, string outputPath)
    {
        var compilerAssemblyPath = ResolveCompilerAssemblyPath();
        if (!File.Exists(compilerAssemblyPath))
        {
            return new JavaScriptCompileResult(
                false,
                null,
                "Compiler not found. Please build MaldaLang.Compiler first. Expected at: " + compilerAssemblyPath);
        }

        try
        {
            var assembly = Assembly.LoadFrom(compilerAssemblyPath);
            var compilerType = assembly.GetType("MaldaLang.Compiler.Compiler");
            if (compilerType == null)
            {
                return new JavaScriptCompileResult(false, null, "Could not find Compiler class in MaldaLang.Compiler.");
            }

            var compileMethod = compilerType.GetMethod("CompileToJavaScript", new[] { typeof(string), typeof(string) });
            if (compileMethod == null)
            {
                return new JavaScriptCompileResult(false, null, "Could not find CompileToJavaScript on Compiler.");
            }

            var compiler = Activator.CreateInstance(compilerType);
            var result = compileMethod.Invoke(compiler, new object[] { sourcePath, outputPath });
            var resultType = result?.GetType();
            var success = (bool)(resultType?.GetProperty("Success")?.GetValue(result) ?? false);
            var resultOutputPath = resultType?.GetProperty("OutputPath")?.GetValue(result) as string;
            var errorMessage = resultType?.GetProperty("ErrorMessage")?.GetValue(result) as string;
            return new JavaScriptCompileResult(success, resultOutputPath, errorMessage);
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            return new JavaScriptCompileResult(false, null, inner.Message);
        }
        catch (Exception ex)
        {
            return new JavaScriptCompileResult(false, null, ex.Message);
        }
    }

    internal static string ResolveCompilerAssemblyPath()
    {
        var besideExe = Path.Combine(AppContext.BaseDirectory, "MaldaLang.Compiler.dll");
        if (File.Exists(besideExe))
        {
            return besideExe;
        }

        foreach (var configuration in ConfigurationPreference)
        {
            var candidate = Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "MaldaLang.Compiler", "bin", configuration, "net8.0", "MaldaLang.Compiler.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return besideExe;
    }
}
