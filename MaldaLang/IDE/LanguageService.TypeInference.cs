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
    private MaldaLang.Parser.AST.Declarations.ClassDeclaration? FindClassAtPosition(
        List<MaldaLang.Parser.AST.Statements.Statement> statements, int line)
    {
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
            {
                // Check if we're inside this class (simple heuristic: check if line is after class declaration)
                if (classDecl.Line <= line + 1)
                {
                    return classDecl;
                }
            }
        }
        return null;
    }
    
    private string? FindVariableType(List<MaldaLang.Parser.AST.Statements.Statement> statements, 
        string varName, int line)
    {
        // Look for variable declaration before the current line
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Statements.VarDeclStatement varDecl && 
                varDecl.Name == varName && stmt.Line <= line + 1)
            {
                return InferExpressionType(statements, varDecl.Initializer, line);
            }
        }
        return null;
    }

    private string? InferExpressionType(List<MaldaLang.Parser.AST.Statements.Statement> statements, Expression? expression, int line)
    {
        if (expression == null)
            return null;

        switch (expression)
        {
            case NewExpression newExpr:
                return newExpr.ClassName;
            case ArrayLiteralExpression:
                return "Array";
            case IdentifierExpression identifier:
                return FindVariableType(statements, identifier.Name, line);
            case FunctionCallExpression functionCall:
                return InferFunctionCallType(statements, functionCall, line);
            default:
                return null;
        }
    }

    private string? InferFunctionCallType(List<MaldaLang.Parser.AST.Statements.Statement> statements, FunctionCallExpression functionCall, int line)
    {
        if (functionCall.Callee is IdentifierExpression identifier)
        {
            if (IsArrayReturningBuiltInFunction(identifier.Name))
                return "Array";

            if (AgentToolCompletionCatalog.IsCreateToolFactory(identifier.Name))
                return "Tool";

            if (statements.OfType<PromptDeclaration>().Any(p => p.Name == identifier.Name))
                return "PromptInstance";

            var functionDecl = statements.OfType<FunctionDeclaration>().FirstOrDefault(f => f.Name == identifier.Name);
            if (functionDecl != null)
            {
                if (string.Equals(functionDecl.ReturnType, "Array", StringComparison.OrdinalIgnoreCase))
                    return "Array";

                return InferFunctionReturnType(statements, functionDecl, line);
            }
        }

        if (functionCall.Callee is MemberAccessExpression memberAccess)
        {
            if (memberAccess.Object is IdentifierExpression moduleName &&
                moduleName.Name == StdLibNamespaces.NnModule &&
                memberAccess.Member == "sequential")
            {
                return "Sequential";
            }

            var receiverType = InferExpressionType(statements, memberAccess.Object, line);
            if (receiverType == "Array" && IsArrayReturningArrayMethod(memberAccess.Member))
                return "Array";
        }

        return null;
    }

    private string? InferFunctionReturnType(List<MaldaLang.Parser.AST.Statements.Statement> statements, FunctionDeclaration functionDecl, int line)
    {
        foreach (var stmt in functionDecl.Body.Statements)
        {
            if (stmt is ReturnStatement returnStmt)
            {
                var returnType = InferExpressionType(statements, returnStmt.Value, line);
                if (returnType != null)
                    return returnType;
            }
        }

        return null;
    }

    private static bool IsArrayReturningBuiltInFunction(string name)
    {
        return name is "split" or "regexFind" or "listDirectory" or "range" or "reverse" or "sort" or "softmax" or "matmul" or "transpose" or "relu" or "sigmoid" or "tanh" or "getSkillNames";
    }

    private static bool IsArrayReturningArrayMethod(string name)
    {
        return name is "concat" or "except" or "map" or "filter" or "sort" or "reverse" or "slice";
    }

    private MaldaLang.Parser.AST.Declarations.ClassDeclaration? FindClassDeclaration(
        List<MaldaLang.Parser.AST.Statements.Statement> statements, string className)
    {
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl && 
                classDecl.Name == className)
            {
                return classDecl;
            }
        }
        return null;
    }
}
