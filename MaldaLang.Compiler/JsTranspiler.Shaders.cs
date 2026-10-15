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
            if (!_shaderFunctions.TryGetValue(name, out var shaderFunction))
            {
                throw new NotSupportedException($"glsl.compile references '{name}', which is not an @shader() function in this file.");
            }

            functions.Add(shaderFunction);
        }

        var glsl = GlslTranspiler.Compile(new GlslCompileRequest
        {
            Header = header,
            Varyings = varyings,
            Uniforms = uniforms,
            Consts = consts,
            Functions = functions,
            MainFunctionName = mainName
        });
        return TranspileLiteral(glsl);
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
