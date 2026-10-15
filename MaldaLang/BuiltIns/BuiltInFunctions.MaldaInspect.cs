// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static string BuildFunctionSignature(string name, List<string> parameters, List<string?>? parameterTypeHints, string? returnType)
    {
        var paramParts = new List<string>();
        for (int i = 0; i < parameters.Count; i++)
        {
            var p = parameters[i];
            if (parameterTypeHints != null && i < parameterTypeHints.Count && parameterTypeHints[i] != null)
                paramParts.Add($"{p}: {parameterTypeHints[i]}");
            else
                paramParts.Add(p);
        }
        var sig = $"function {name}({string.Join(", ", paramParts)})";
        if (returnType != null)
            sig += $" -> {returnType}";
        return sig;
    }

    private static RuntimeValue BuiltInGetSymbols(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("getSymbols() expects 1 argument: sourceOrFilePath (string)");
        
        var sourceOrFilePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            throw new Exception("getSymbols() sourceOrFilePath cannot be empty");
        
        string source = sourceOrFilePath;
        string? sourceFileName = null;
        
        // Detect if argument is a file path (mirror BuiltInRunMALDA)
        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
            sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
            sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string filePath = Path.IsPathRooted(sourceOrFilePath)
                    ? Path.GetFullPath(sourceOrFilePath)
                    : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                
                if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                {
                    var errorResultObj = new JsonObject();
                    errorResultObj.Set("classes", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("functions", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("actors", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("prompts", RuntimeValue.Array(new List<RuntimeValue>()));
                    errorResultObj.Set("imports", RuntimeValue.Array(new List<RuntimeValue>()));
                    var errList = new List<RuntimeValue>();
                    var errEntry = new JsonObject();
                    errEntry.Set("message", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
                    errEntry.Set("line", RuntimeValue.Integer(0));
                    errEntry.Set("column", RuntimeValue.Integer(0));
                    errList.Add(RuntimeValue.Object(errEntry));
                    errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                    return RuntimeValue.Object(errorResultObj);
                }
                
                if (File.Exists(filePath))
                {
                    source = File.ReadAllText(filePath);
                    sourceFileName = filePath;
                }
            }
            catch (Exception ex)
            {
                var errorResultObj = new JsonObject();
                errorResultObj.Set("classes", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("functions", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("actors", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("prompts", RuntimeValue.Array(new List<RuntimeValue>()));
                errorResultObj.Set("imports", RuntimeValue.Array(new List<RuntimeValue>()));
                var errList = new List<RuntimeValue>();
                var errEntry = new JsonObject();
                errEntry.Set("message", RuntimeValue.String($"Error reading file: {ex.Message}"));
                errEntry.Set("line", RuntimeValue.Integer(0));
                errEntry.Set("column", RuntimeValue.Integer(0));
                errList.Add(RuntimeValue.Object(errEntry));
                errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                return RuntimeValue.Object(errorResultObj);
            }
        }
        
        var classes = new List<RuntimeValue>();
        var functions = new List<RuntimeValue>();
        var actors = new List<RuntimeValue>();
        var prompts = new List<RuntimeValue>();
        var parseErrors = new List<RuntimeValue>();
        var importsList = new List<RuntimeValue>();
        
        try
        {
            var lexer = new Lexer(source, sourceFileName);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            var statements = parser.Parse();
            
            // Collect parse errors
            foreach (var error in parser.Errors)
            {
                var errorEntry = new JsonObject();
                errorEntry.Set("message", RuntimeValue.String(error.Message));
                errorEntry.Set("line", RuntimeValue.Integer(error.Line));
                errorEntry.Set("column", RuntimeValue.Integer(error.Column));
                parseErrors.Add(RuntimeValue.Object(errorEntry));
            }
            
            // Extract symbols from AST
            foreach (var stmt in statements)
            {
                if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
                {
                    var classObj = new JsonObject();
                    classObj.Set("name", RuntimeValue.String(classDecl.Name));
                    classObj.Set("superclass", classDecl.Superclass != null 
                        ? RuntimeValue.String(classDecl.Superclass) 
                        : RuntimeValue.Null());
                    classObj.Set("line", RuntimeValue.Integer(classDecl.Line));
                    classObj.Set("column", RuntimeValue.Integer(classDecl.Column));
                    
                    // Extract members
                    var members = new List<RuntimeValue>();
                    foreach (var member in classDecl.Members)
                    {
                        var memberObj = new JsonObject();
                        memberObj.Set("name", RuntimeValue.String(member.Name));
                        memberObj.Set("type", RuntimeValue.String(member.Type.ToString().ToLower()));
                        memberObj.Set("access", RuntimeValue.String(member.Access.ToString().ToLower()));
                        memberObj.Set("isStatic", RuntimeValue.Boolean(member.IsStatic));
                        
                        // Get line and column from the member
                        // For methods/constructors, get from FunctionDeclaration
                        int memberLine = classDecl.Line;
                        int memberColumn = classDecl.Column;
                        string signature = "";
                        
                        if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                        {
                            if (member.Value is MaldaLang.Parser.AST.Declarations.FunctionDeclaration memberFuncDecl)
                            {
                                memberLine = memberFuncDecl.Line;
                                memberColumn = memberFuncDecl.Column;
                                signature = BuildFunctionSignature(member.Name, memberFuncDecl.Parameters, memberFuncDecl.ParameterTypeHints, memberFuncDecl.ReturnType);
                                var paramTypesList = new List<RuntimeValue>();
                                for (int i = 0; i < memberFuncDecl.Parameters.Count; i++)
                                {
                                    string? hint = (memberFuncDecl.ParameterTypeHints != null && i < memberFuncDecl.ParameterTypeHints.Count)
                                        ? memberFuncDecl.ParameterTypeHints[i]
                                        : null;
                                    paramTypesList.Add(hint != null ? RuntimeValue.String(hint) : RuntimeValue.Null());
                                }
                                memberObj.Set("parameterTypes", RuntimeValue.Array(paramTypesList));
                                memberObj.Set("returnType", memberFuncDecl.ReturnType != null ? RuntimeValue.String(memberFuncDecl.ReturnType) : RuntimeValue.Null());
                            }
                        }
                        
                        memberObj.Set("line", RuntimeValue.Integer(memberLine));
                        memberObj.Set("column", RuntimeValue.Integer(memberColumn));
                        memberObj.Set("signature", RuntimeValue.String(signature));
                        members.Add(RuntimeValue.Object(memberObj));
                    }
                    
                    classObj.Set("members", RuntimeValue.Array(members));
                    classes.Add(RuntimeValue.Object(classObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.FunctionDeclaration funcDecl)
                {
                    var funcObj = new JsonObject();
                    funcObj.Set("name", RuntimeValue.String(funcDecl.Name));
                    funcObj.Set("line", RuntimeValue.Integer(funcDecl.Line));
                    funcObj.Set("column", RuntimeValue.Integer(funcDecl.Column));
                    
                    var paramsArray = new List<RuntimeValue>();
                    foreach (var param in funcDecl.Parameters)
                    {
                        paramsArray.Add(RuntimeValue.String(param));
                    }
                    funcObj.Set("parameters", RuntimeValue.Array(paramsArray));
                    
                    var paramTypesArray = new List<RuntimeValue>();
                    for (int i = 0; i < funcDecl.Parameters.Count; i++)
                    {
                        string? hint = (funcDecl.ParameterTypeHints != null && i < funcDecl.ParameterTypeHints.Count)
                            ? funcDecl.ParameterTypeHints[i]
                            : null;
                        paramTypesArray.Add(hint != null ? RuntimeValue.String(hint) : RuntimeValue.Null());
                    }
                    funcObj.Set("parameterTypes", RuntimeValue.Array(paramTypesArray));
                    funcObj.Set("returnType", funcDecl.ReturnType != null ? RuntimeValue.String(funcDecl.ReturnType) : RuntimeValue.Null());
                    
                    var sig = BuildFunctionSignature(funcDecl.Name, funcDecl.Parameters, funcDecl.ParameterTypeHints, funcDecl.ReturnType);
                    funcObj.Set("signature", RuntimeValue.String(sig));
                    functions.Add(RuntimeValue.Object(funcObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.ActorDeclaration actorDecl)
                {
                    var actorObj = new JsonObject();
                    actorObj.Set("name", RuntimeValue.String(actorDecl.Name));
                    actorObj.Set("line", RuntimeValue.Integer(actorDecl.Line));
                    actorObj.Set("column", RuntimeValue.Integer(actorDecl.Column));
                    
                    // Extract members (same structure as class members)
                    var members = new List<RuntimeValue>();
                    foreach (var member in actorDecl.Members)
                    {
                        var memberObj = new JsonObject();
                        memberObj.Set("name", RuntimeValue.String(member.Name));
                        memberObj.Set("type", RuntimeValue.String(member.Type.ToString().ToLower()));
                        memberObj.Set("access", RuntimeValue.String(member.Access.ToString().ToLower()));
                        memberObj.Set("isStatic", RuntimeValue.Boolean(member.IsStatic));
                        
                        // Get line and column from the member
                        int memberLine = actorDecl.Line;
                        int memberColumn = actorDecl.Column;
                        string signature = "";
                        
                        if (member.Type == MemberType.Method || member.Type == MemberType.Constructor)
                        {
                            if (member.Value is MaldaLang.Parser.AST.Declarations.FunctionDeclaration actorMemberFuncDecl)
                            {
                                memberLine = actorMemberFuncDecl.Line;
                                memberColumn = actorMemberFuncDecl.Column;
                                signature = BuildFunctionSignature(member.Name, actorMemberFuncDecl.Parameters, actorMemberFuncDecl.ParameterTypeHints, actorMemberFuncDecl.ReturnType);
                                var paramTypesList = new List<RuntimeValue>();
                                for (int i = 0; i < actorMemberFuncDecl.Parameters.Count; i++)
                                {
                                    string? hint = (actorMemberFuncDecl.ParameterTypeHints != null && i < actorMemberFuncDecl.ParameterTypeHints.Count)
                                        ? actorMemberFuncDecl.ParameterTypeHints[i]
                                        : null;
                                    paramTypesList.Add(hint != null ? RuntimeValue.String(hint) : RuntimeValue.Null());
                                }
                                memberObj.Set("parameterTypes", RuntimeValue.Array(paramTypesList));
                                memberObj.Set("returnType", actorMemberFuncDecl.ReturnType != null ? RuntimeValue.String(actorMemberFuncDecl.ReturnType) : RuntimeValue.Null());
                            }
                        }
                        
                        memberObj.Set("line", RuntimeValue.Integer(memberLine));
                        memberObj.Set("column", RuntimeValue.Integer(memberColumn));
                        memberObj.Set("signature", RuntimeValue.String(signature));
                        members.Add(RuntimeValue.Object(memberObj));
                    }
                    
                    actorObj.Set("members", RuntimeValue.Array(members));
                    actors.Add(RuntimeValue.Object(actorObj));
                }
                else if (stmt is MaldaLang.Parser.AST.Declarations.PromptDeclaration promptDecl)
                {
                    var promptObj = new JsonObject();
                    promptObj.Set("name", RuntimeValue.String(promptDecl.Name));
                    promptObj.Set("line", RuntimeValue.Integer(promptDecl.Line));
                    promptObj.Set("column", RuntimeValue.Integer(promptDecl.Column));
                    var paramsArray = new List<RuntimeValue>();
                    foreach (var param in promptDecl.Parameters)
                        paramsArray.Add(RuntimeValue.String(param));
                    promptObj.Set("parameters", RuntimeValue.Array(paramsArray));
                    promptObj.Set("returnType", promptDecl.ReturnType != null ? RuntimeValue.String(promptDecl.ReturnType) : RuntimeValue.Null());
                    var sig = $"prompt {promptDecl.Name}({string.Join(", ", promptDecl.Parameters)})";
                    if (promptDecl.ReturnType != null)
                        sig += $" -> {promptDecl.ReturnType}";
                    promptObj.Set("signature", RuntimeValue.String(sig));
                    prompts.Add(RuntimeValue.Object(promptObj));
                }
            }

            var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
            var knownFunctionNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fn in functions)
            {
                var n = fn.AsObject().Get("name", null)?.AsString();
                if (n != null)
                    knownFunctionNames.Add(n);
            }

            foreach (var imp in imported.Imports)
            {
                var impObj = new JsonObject();
                impObj.Set("file", imp.IsFileImport ? RuntimeValue.String(imp.ModuleKey) : RuntimeValue.Null());
                impObj.Set("resolvedPath", imp.ResolvedPath != null ? RuntimeValue.String(imp.ResolvedPath) : RuntimeValue.Null());
                impObj.Set("package", imp.PackageName != null ? RuntimeValue.String(imp.PackageName) : RuntimeValue.Null());
                impObj.Set("alias", imp.Alias != null ? RuntimeValue.String(imp.Alias) : RuntimeValue.Null());
                importsList.Add(RuntimeValue.Object(impObj));
            }

            foreach (var funcDecl in imported.Functions)
            {
                if (knownFunctionNames.Contains(funcDecl.Name))
                    continue;
                knownFunctionNames.Add(funcDecl.Name);

                var funcObj = new JsonObject();
                funcObj.Set("name", RuntimeValue.String(funcDecl.Name));
                funcObj.Set("line", RuntimeValue.Integer(funcDecl.Line));
                funcObj.Set("column", RuntimeValue.Integer(funcDecl.Column));
                funcObj.Set("fromModule", RuntimeValue.String(Path.GetFileName(funcDecl.SourceFile ?? "imported")));

                var paramsArray = new List<RuntimeValue>();
                foreach (var param in funcDecl.Parameters)
                    paramsArray.Add(RuntimeValue.String(param));
                funcObj.Set("parameters", RuntimeValue.Array(paramsArray));

                var sig = BuildFunctionSignature(funcDecl.Name, funcDecl.Parameters, funcDecl.ParameterTypeHints, funcDecl.ReturnType);
                funcObj.Set("signature", RuntimeValue.String(sig));
                functions.Add(RuntimeValue.Object(funcObj));
            }

            var knownClassNames = new HashSet<string>(classes.Select(c => c.AsObject().Get("name", null)?.AsString() ?? ""), StringComparer.Ordinal);
            foreach (var classDecl in imported.Classes)
            {
                if (knownClassNames.Contains(classDecl.Name))
                    continue;
                knownClassNames.Add(classDecl.Name);

                var classObj = new JsonObject();
                classObj.Set("name", RuntimeValue.String(classDecl.Name));
                classObj.Set("fromModule", RuntimeValue.String(Path.GetFileName(classDecl.SourceFile ?? "imported")));
                classObj.Set("line", RuntimeValue.Integer(classDecl.Line));
                classObj.Set("column", RuntimeValue.Integer(classDecl.Column));
                classes.Add(RuntimeValue.Object(classObj));
            }
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(ex.Line));
            errorEntry.Set("column", RuntimeValue.Integer(ex.Column));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        catch (Exception ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String($"Error parsing code: {ex.Message}"));
            errorEntry.Set("line", RuntimeValue.Integer(0));
            errorEntry.Set("column", RuntimeValue.Integer(0));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        
        // Build result object
        var resultObj = new JsonObject();
        resultObj.Set("classes", RuntimeValue.Array(classes));
        resultObj.Set("functions", RuntimeValue.Array(functions));
        resultObj.Set("actors", RuntimeValue.Array(actors));
        resultObj.Set("prompts", RuntimeValue.Array(prompts));
        resultObj.Set("parseErrors", RuntimeValue.Array(parseErrors));
        resultObj.Set("imports", RuntimeValue.Array(importsList));
        
        return RuntimeValue.Object(resultObj);
    }
    
    private static RuntimeValue BuiltInCreateCompileMALDATool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createCompileMALDATool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateCompileMALDATool(workingDir);
    }
    
    private static RuntimeValue BuiltInCreateGetSymbolsTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGetSymbolsTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGetSymbolsTool(workingDir);
    }
    
    private static RuntimeValue BuiltInGetParseErrors(List<RuntimeValue> args)
    {
        if (args.Count == 0 || args[0].Type != ValueType.String)
            throw new Exception("getParseErrors() expects 1 argument: sourceOrFilePath (string)");
        
        var sourceOrFilePath = args[0].AsString();
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            throw new Exception("getParseErrors() sourceOrFilePath cannot be empty");
        
        string source = sourceOrFilePath;
        
        if (sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
            sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
            sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                string filePath = Path.IsPathRooted(sourceOrFilePath)
                    ? Path.GetFullPath(sourceOrFilePath)
                    : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                
                if (sourceOrFilePath.Contains("..") || sourceOrFilePath.Contains("~"))
                {
                    var errorResultObj = new JsonObject();
                    var errList = new List<RuntimeValue>();
                    var errEntry = new JsonObject();
                    errEntry.Set("message", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
                    errEntry.Set("line", RuntimeValue.Integer(0));
                    errEntry.Set("column", RuntimeValue.Integer(0));
                    errList.Add(RuntimeValue.Object(errEntry));
                    errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                    return RuntimeValue.Object(errorResultObj);
                }
                
                if (File.Exists(filePath))
                    source = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                var errorResultObj = new JsonObject();
                var errList = new List<RuntimeValue>();
                var errEntry = new JsonObject();
                errEntry.Set("message", RuntimeValue.String($"Error reading file: {ex.Message}"));
                errEntry.Set("line", RuntimeValue.Integer(0));
                errEntry.Set("column", RuntimeValue.Integer(0));
                errList.Add(RuntimeValue.Object(errEntry));
                errorResultObj.Set("parseErrors", RuntimeValue.Array(errList));
                return RuntimeValue.Object(errorResultObj);
            }
        }
        
        var parseErrors = new List<RuntimeValue>();
        
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens);
            parser.Parse();
            
            foreach (var error in parser.Errors)
            {
                var errorEntry = new JsonObject();
                errorEntry.Set("message", RuntimeValue.String(error.Message));
                errorEntry.Set("line", RuntimeValue.Integer(error.Line));
                errorEntry.Set("column", RuntimeValue.Integer(error.Column));
                parseErrors.Add(RuntimeValue.Object(errorEntry));
            }
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String(ex.Message));
            errorEntry.Set("line", RuntimeValue.Integer(ex.Line));
            errorEntry.Set("column", RuntimeValue.Integer(ex.Column));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        catch (Exception ex)
        {
            var errorEntry = new JsonObject();
            errorEntry.Set("message", RuntimeValue.String($"Error parsing code: {ex.Message}"));
            errorEntry.Set("line", RuntimeValue.Integer(0));
            errorEntry.Set("column", RuntimeValue.Integer(0));
            parseErrors.Add(RuntimeValue.Object(errorEntry));
        }
        
        var resultObj = new JsonObject();
        resultObj.Set("parseErrors", RuntimeValue.Array(parseErrors));
        return RuntimeValue.Object(resultObj);
    }
    
    private static RuntimeValue BuiltInCreateGetParseErrorsTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createGetParseErrorsTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateGetParseErrorsTool(workingDir);
    }

    /// <summary>
    /// Diagnose MALDA source or a file with the same LanguageService path as
    /// <c>malda check --json</c>. Used by the <c>checkMalda</c> built-in and the
    /// <c>check_malda</c> agent tool.
    /// </summary>
    public static RuntimeValue CheckMaldaSource(string sourceOrFilePath, string? typeMode = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(sourceOrFilePath))
            return CheckMaldaUsageError("sourceOrFilePath cannot be empty");

        if (!TryParseCheckMaldaTypeMode(typeMode, out var typeOptions, out var typeModeError))
            return CheckMaldaUsageError(typeModeError!);

        string source = sourceOrFilePath;
        string? fileLabel = "<eval>";

        if (LooksLikeCheckMaldaFilePath(sourceOrFilePath))
        {
            if (sourceOrFilePath.Contains("..", StringComparison.Ordinal) ||
                sourceOrFilePath.Contains('~'))
            {
                return CheckMaldaUsageError("Error: Path contains suspicious characters (path traversal attempt)");
            }

            string resolvedPath;
            try
            {
                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    var probe = new ToolInstance { WorkingDirectory = workingDirectory };
                    var normalized = probe.NormalizePathForWorkingDirectory(sourceOrFilePath);
                    if (normalized == null)
                    {
                        return CheckMaldaUsageError(
                            $"Error: Path '{sourceOrFilePath}' is outside the allowed working directory '{workingDirectory}'");
                    }

                    resolvedPath = probe.ResolvePathAgainstWorkingDirectory(normalized) ?? normalized;
                }
                else
                {
                    resolvedPath = Path.IsPathRooted(sourceOrFilePath)
                        ? Path.GetFullPath(sourceOrFilePath)
                        : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrFilePath));
                }
            }
            catch (Exception ex)
            {
                return CheckMaldaUsageError($"Error resolving path: {ex.Message}");
            }

            try
            {
                if (!File.Exists(resolvedPath))
                    return CheckMaldaUsageError($"File not found: {resolvedPath}");

                source = File.ReadAllText(resolvedPath);
                fileLabel = resolvedPath;
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                return CheckMaldaUsageError($"Error reading file: {ex.Message}");
            }
        }

        var runner = new CheckCommandRunner();
        var report = runner.Analyze(source, fileLabel, typeOptions);
        return CheckReportToRuntimeValue(report);
    }

    private static RuntimeValue BuiltInCheckMalda(List<RuntimeValue> args)
    {
        BuiltInArity.Require("checkMalda", args, 1, 3, "sourceOrFilePath, typeMode?, workingDir?");
        if (args[0].Type != ValueType.String)
            throw new Exception("checkMalda() sourceOrFilePath must be a string");

        var sourceOrFilePath = args[0].AsString();
        var typeMode = args.Count > 1 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var workingDir = args.Count > 2 && args[2].Type == ValueType.String ? args[2].AsString() : null;
        return CheckMaldaSource(sourceOrFilePath, typeMode, workingDir);
    }

    private static RuntimeValue BuiltInCreateCheckMaldaTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createCheckMaldaTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateCheckMaldaTool(workingDir);
    }

    private static RuntimeValue BuiltInCreateValidateJsonTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createValidateJsonTool", args, 0, 0);
        return BuiltInTools.CreateValidateJsonTool();
    }

    private static RuntimeValue BuiltInCreateTestMaldaTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createTestMaldaTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String
            ? args[0].AsString()
            : "";
        return BuiltInTools.CreateTestMaldaTool(workingDir);
    }

    /// <summary>
    /// Run <c>malda test</c> via <see cref="TestCommandRunner"/> and return
    /// <c>{ success, exitCode, output, report? }</c>. Used by the <c>test_malda</c> agent tool.
    /// </summary>
    public static RuntimeValue RunTestMalda(string rootPath, string? filter = null, bool listOnly = false)
    {
        var runnerArgs = new List<string> { rootPath, "--format", "ci" };
        if (!string.IsNullOrEmpty(filter))
        {
            runnerArgs.Add("--filter");
            runnerArgs.Add(filter);
        }

        if (listOnly)
            runnerArgs.Add("--list");

        var captured = new StringWriter();
        var runner = new TestCommandRunner();
        var exitCode = runner.Run(runnerArgs.ToArray(), captured, captured);
        var output = captured.ToString();

        var result = new JsonObject();
        result.Set("success", RuntimeValue.Boolean(exitCode == 0));
        result.Set("exitCode", RuntimeValue.Integer(exitCode));
        result.Set("output", RuntimeValue.String(output));

        try
        {
            using var doc = JsonDocument.Parse(output);
            result.Set("report", ToolSchemaResolver.FromJsonElement(doc.RootElement));
        }
        catch (JsonException)
        {
            // CI JSON is optional; always return success/exitCode/output.
        }

        return RuntimeValue.Object(result);
    }

    private static bool LooksLikeCheckMaldaFilePath(string sourceOrFilePath)
    {
        return sourceOrFilePath.Contains(Path.DirectorySeparatorChar) ||
               sourceOrFilePath.Contains(Path.AltDirectorySeparatorChar) ||
               sourceOrFilePath.EndsWith(".malda", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseCheckMaldaTypeMode(string? typeMode, out StrictTypesOptions typeOptions, out string? error)
    {
        error = null;
        var mode = string.IsNullOrWhiteSpace(typeMode) ? "default" : typeMode.Trim();
        if (mode.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            typeOptions = StrictTypesOptions.Default;
            return true;
        }

        if (mode.Equals("strict", StringComparison.OrdinalIgnoreCase))
        {
            typeOptions = StrictTypesOptions.Enabled;
            return true;
        }

        if (mode.Equals("lenient", StringComparison.OrdinalIgnoreCase))
        {
            typeOptions = StrictTypesOptions.Lenient;
            return true;
        }

        typeOptions = StrictTypesOptions.Default;
        error = "typeMode must be 'default', 'strict', or 'lenient'";
        return false;
    }

    private static RuntimeValue CheckMaldaUsageError(string message)
    {
        return CheckReportToRuntimeValue(CheckCommandReport.Usage(message));
    }

    private static RuntimeValue CheckReportToRuntimeValue(CheckCommandReport report)
    {
        var obj = new JsonObject();
        obj.Set("ok", RuntimeValue.Boolean(report.Ok));
        obj.Set("executed", RuntimeValue.Boolean(false));
        obj.Set("file", report.File != null ? RuntimeValue.String(report.File) : RuntimeValue.Null());
        obj.Set("errorCount", RuntimeValue.Integer(report.ErrorCount));
        obj.Set("warningCount", RuntimeValue.Integer(report.WarningCount));
        obj.Set("infoCount", RuntimeValue.Integer(report.InfoCount));
        if (!string.IsNullOrEmpty(report.Error))
            obj.Set("error", RuntimeValue.String(report.Error));

        var diagnostics = new List<RuntimeValue>(report.Diagnostics.Count);
        foreach (var diagnostic in report.Diagnostics)
        {
            var item = new JsonObject();
            item.Set("severity", RuntimeValue.String(diagnostic.Severity));
            item.Set("code", RuntimeValue.String(diagnostic.Code));
            item.Set("message", RuntimeValue.String(diagnostic.Message));
            if (diagnostic.File != null)
                item.Set("file", RuntimeValue.String(diagnostic.File));
            item.Set("line", RuntimeValue.Integer(diagnostic.Line));
            item.Set("column", RuntimeValue.Integer(diagnostic.Column));
            item.Set("length", RuntimeValue.Integer(diagnostic.Length));
            if (!string.IsNullOrEmpty(diagnostic.Hint))
                item.Set("hint", RuntimeValue.String(diagnostic.Hint));
            if (!string.IsNullOrEmpty(diagnostic.SuggestedFix))
                item.Set("suggestedFix", RuntimeValue.String(diagnostic.SuggestedFix));
            diagnostics.Add(RuntimeValue.Object(item));
        }

        obj.Set("diagnostics", RuntimeValue.Array(diagnostics));
        return RuntimeValue.Object(obj);
    }
}
