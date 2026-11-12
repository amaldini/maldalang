// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public partial class JsTranspiler
{
    private void CollectShaderFunctions(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
            CollectShaderFunctionsFrom(statement);
    }

    private void CollectShaderFunctionsFrom(Statement statement)
    {
        switch (statement)
        {
            case FunctionDeclaration function:
                if (ShaderFunction.IsMarked(function))
                {
                    if (_shaderFunctions.ContainsKey(function.Name))
                    {
                        throw new NotSupportedException($"Duplicate @shader() function '{function.Name}'.");
                    }

                    _shaderFunctions[function.Name] = function;
                }

                foreach (var inner in function.Body.Statements)
                    CollectShaderFunctionsFrom(inner);
                break;
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    CollectShaderFunctionsFrom(inner);
                break;
            case IfStatement ifStatement:
                CollectShaderFunctionsFrom(ifStatement.ThenBranch);
                if (ifStatement.ElseBranch != null)
                    CollectShaderFunctionsFrom(ifStatement.ElseBranch);
                break;
            case WhileStatement whileStatement:
                CollectShaderFunctionsFrom(whileStatement.Body);
                break;
            case ForStatement forStatement:
                CollectShaderFunctionsFrom(forStatement.Body);
                break;
            case ForInStatement forInStatement:
                CollectShaderFunctionsFrom(forInStatement.Body);
                break;
            case TryStatement tryStatement:
                foreach (var inner in tryStatement.TryBlock.Statements)
                    CollectShaderFunctionsFrom(inner);
                foreach (var catchClause in tryStatement.CatchClauses)
                {
                    foreach (var inner in catchClause.Body.Statements)
                        CollectShaderFunctionsFrom(inner);
                }
                if (tryStatement.FinallyBlock != null)
                {
                    foreach (var inner in tryStatement.FinallyBlock.Statements)
                        CollectShaderFunctionsFrom(inner);
                }
                break;
        }
    }

    /// <summary>
    /// The top-level <c>const NAME = literal;</c> declarations of the file: the consts an
    /// <c>@shader()</c> function may name, written into the GLSL by <c>glsl.compile</c>.
    /// </summary>
    private void CollectShaderLiteralConsts(IEnumerable<Statement> statements)
    {
        _shaderLiteralConsts.Clear();
        if (_shaderFunctions.Count == 0)
            return;

        foreach (var declaration in statements.OfType<VarDeclStatement>())
        {
            if (GlslLiteralConst.TryRead(declaration) is { } constant &&
                !_shaderLiteralConsts.Any(existing => existing.Name == constant.Name))
            {
                _shaderLiteralConsts.Add(constant);
            }
        }
    }

    /// <summary>
    /// Finds, checks and types the <c>@shader()</c> functions host code reaches (by plain name or
    /// through <c>glsl.host</c>). Runs before any emit, so an error in a kernel is reported before
    /// the code that calls it is written.
    /// </summary>
    private void AnalyzeShaderHostCalls(IEnumerable<Statement> statements)
    {
        _shaderHost = GlslHostAnalysis.Run(statements, _shaderFunctions, _shaderLiteralConsts);
    }

    /// <summary>
    /// Emits the JavaScript twins of the host-called <c>@shader()</c> functions as plain function
    /// declarations, so the ordinary call emit (<c>sdBox(q, s)</c>) reaches them. Nothing is
    /// written for a program that never calls a shader function from the host.
    /// </summary>
    private void EmitShaderHostTwins()
    {
        var host = _shaderHost;
        if (host == null || (host.PlainFunctions.Count == 0 && host.BoundFunctions.Count == 0))
            return;

        EmitLine($"const {GlslHostEmitter.RuntimeAlias} = mlRuntime.glsl;");
        // One twin for each reached function, in declaration order. A bound function that reads no
        // uniform, itself or through a callee, has the same twin as a plain one.
        foreach (var function in _shaderFunctions.Values)
        {
            var name = function.Name;
            IReadOnlyList<GlslHostLine> lines;
            if (host.BoundTypes.TryGetValue(name, out var boundTypes) && host.NeedsBinding(name))
                lines = GlslHostEmitter.Emit(boundTypes, host.Consts, host);
            else if (host.PlainTypes.TryGetValue(name, out var plainTypes))
                lines = GlslHostEmitter.Emit(plainTypes, host.Consts);
            else if (boundTypes != null)
                lines = GlslHostEmitter.Emit(boundTypes, host.Consts, host);
            else
                continue;

            var baseIndent = _indentLevel;
            foreach (var line in lines)
            {
                _indentLevel = baseIndent + line.Indent;
                EmitLineWithSource(line.Source, line.Text);
            }

            _indentLevel = baseIndent;
            EmitLine(string.Empty);
        }
    }

    /// <summary>
    /// <c>glsl.host("name", material)</c>: a host function for that <c>@shader()</c> function, bound
    /// to the material its uniforms and data textures are read from. Each call makes its own
    /// function, so two materials bound to the same kernel are independent.
    /// </summary>
    private string TranspileGlslHost(FunctionCallExpression functionCall)
    {
        var name = GlslHostAnalysis.HostTarget(functionCall, _shaderFunctions);
        var needsBinding = _shaderHost != null && _shaderHost.NeedsBinding(name);
        var twin = needsBinding ? GlslHostEmitter.BoundName(name) : EscapeIdentifier(name);
        return $"mlRuntime.glsl.host({twin}, {TranspileExpression(functionCall.Arguments[1])}, {(needsBinding ? "true" : "false")})";
    }

    private string TranspileGlslCompile(FunctionCallExpression functionCall)
    {
        if (functionCall.Arguments.Count != 1 || functionCall.Arguments[0] is not ObjectLiteralExpression options)
        {
            throw new NotSupportedException("glsl.compile expects a single object literal: glsl.compile({ functions: [...], ... }).");
        }

        var varyings = new List<string>();
        var uniforms = new List<string>();
        var consts = new List<string>();
        var functionNames = new List<string>();
        string? mainName = null;
        string? header = null;

        foreach (var (keyExpr, valueExpr) in options.Properties)
        {
            var key = (keyExpr as LiteralExpression)?.Value?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                throw new NotSupportedException("glsl.compile option keys must be string literals or identifiers.");

            switch (key)
            {
                case "varyings":
                case "varying":
                    varyings.AddRange(RequireStringList(valueExpr, key));
                    break;
                case "uniforms":
                case "uniform":
                    uniforms.AddRange(RequireStringList(valueExpr, key));
                    break;
                case "consts":
                case "const":
                    consts.AddRange(RequireStringList(valueExpr, key));
                    break;
                case "functions":
                    functionNames.AddRange(RequireStringList(valueExpr, key));
                    break;
                case "main":
                    mainName = RequireString(valueExpr, key);
                    break;
                case "header":
                    header = RequireString(valueExpr, key);
                    break;
                default:
                    throw new NotSupportedException($"Unknown glsl.compile option '{key}'. Supported: varyings, uniforms, consts, functions, main, header.");
            }
        }

        if (mainName != null && !functionNames.Contains(mainName, StringComparer.Ordinal))
            functionNames.Add(mainName);

        if (functionNames.Count == 0)
            throw new NotSupportedException("glsl.compile requires a 'functions' array (and optional 'main').");

        var functions = new List<FunctionDeclaration>();
        foreach (var name in functionNames)
        {
            functions.Add(RequireShaderFunction(name, "glsl.compile"));
        }

        var glsl = GlslTranspiler.Compile(new GlslCompileRequest
        {
            Header = header,
            Varyings = varyings,
            Uniforms = uniforms,
            Consts = consts,
            Functions = functions,
            MaldaConsts = _shaderLiteralConsts,
            MainFunctionName = mainName
        });
        return TranspileLiteral(glsl);
    }

    /// <summary>
    /// <c>glsl.source("a", "b")</c>: the GLSL of those <c>@shader()</c> functions, in that order
    /// (callees first), as a string to compose at runtime with generated code.
    /// </summary>
    private string TranspileGlslSource(FunctionCallExpression functionCall)
    {
        if (functionCall.Arguments.Count == 0)
            throw new NotSupportedException("glsl.source expects the names of @shader() functions: glsl.source(\"shade\", ...).");

        var functions = functionCall.Arguments
            .Select(argument => RequireShaderFunction(RequireString(argument, "source"), "glsl.source"))
            .ToList();
        return TranspileLiteral(GlslTranspiler.Compile(new GlslCompileRequest { Functions = functions, MaldaConsts = _shaderLiteralConsts }));
    }

    /// <summary>
    /// <c>glsl.functions()</c>: a dict from each <c>@shader()</c> function name in the file to its GLSL,
    /// for shaders picked or assembled at runtime.
    /// </summary>
    private string TranspileGlslFunctions(FunctionCallExpression functionCall)
    {
        if (functionCall.Arguments.Count != 0)
            throw new NotSupportedException("glsl.functions() takes no arguments.");

        var entries = _shaderFunctions
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => $"{TranspileLiteral(entry.Key)}: {TranspileLiteral(GlslTranspiler.TranspileFunction(entry.Value).Replace("\r\n", "\n"))}");
        return $"mlRuntime.markDict({{ {string.Join(", ", entries)} }})";
    }

    private FunctionDeclaration RequireShaderFunction(string name, string helper)
    {
        if (!_shaderFunctions.TryGetValue(name, out var shaderFunction))
            throw new NotSupportedException($"{helper} references '{name}', which is not an @shader() function in this file.");
        return shaderFunction;
    }

    private static List<string> RequireStringList(Expression expression, string field)
    {
        if (expression is not ArrayLiteralExpression array)
            throw new NotSupportedException($"glsl.compile '{field}' must be an array of string literals.");

        var values = new List<string>(array.Elements.Count);
        foreach (var element in array.Elements)
            values.Add(RequireString(element, field));
        return values;
    }

    private static string RequireString(Expression expression, string field)
    {
        if (expression is LiteralExpression { Value: string value })
            return value;
        throw new NotSupportedException($"glsl.compile '{field}' values must be string literals.");
    }
}
