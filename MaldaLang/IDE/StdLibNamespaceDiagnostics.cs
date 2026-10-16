// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Services;

using MaldaLang.BuiltIns;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

internal static class StdLibNamespaceDiagnostics
{
    /// <summary>Source of the info diagnostic for a function that shadows a flat built-in.</summary>
    public const string ShadowSource = "malda-shadow";

    public static void Validate(List<Statement> statements, List<Diagnostic> diagnostics)
    {
        // A call whose name a user binding shadows is the user's function, not a deprecated alias.
        BuiltInShadowAnalysis.Analyze(statements);
        foreach (var statement in statements)
        {
            var start = diagnostics.Count;
            VisitStatement(statement, diagnostics);
            ReportShadowingFunctions(statement, diagnostics);
            DiagnosticSourceFiles.Stamp(diagnostics, start, statement);
        }
    }

    /// <summary>
    /// Info: <c>function zeros(n)</c> shadows the flat built-in <c>zeros</c> wherever it is in
    /// scope. Class methods are skipped (they are not unqualified bindings), and so are
    /// <c>@shader()</c> functions (GLSL).
    /// </summary>
    private static void ReportShadowingFunctions(Statement statement, List<Diagnostic> diagnostics)
    {
        var methods = new HashSet<FunctionDeclaration>(ReferenceEqualityComparer.Instance);
        foreach (var node in MaldaLang.Parser.AST.AstNodeWalker.Descendants(statement))
        {
            if (node is ClassDeclaration cls)
            {
                foreach (var member in cls.Members)
                {
                    if (member.Value is FunctionDeclaration method)
                        methods.Add(method);
                }
            }

            if (node is not FunctionDeclaration function || methods.Contains(function) ||
                ShaderFunction.IsMarked(function) || !BuiltInShadowAnalysis.IsFlatBuiltIn(function.Name))
                continue;

            var qualified = StdLibNamespaces.TryGetFlatAliasModule(function.Name, out var module)
                ? $" Call `{module}.{function.Name}(...)` for the built-in."
                : string.Empty;
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Info,
                Message = $"Function `{function.Name}` shadows the built-in `{function.Name}`: calls to `{function.Name}(...)` in its scope run this function.{qualified}",
                Line = Math.Max(0, function.Line - 1),
                Column = Math.Max(0, function.Column - 1),
                Length = Math.Max(1, function.Name.Length),
                Source = ShadowSource
            });
        }
    }

    private static void VisitStatement(Statement statement, List<Diagnostic> diagnostics)
    {
        switch (statement)
        {
            case FunctionDeclaration func:
                // @shader() bodies are GLSL. Bare cos/dot/length are GPU intrinsics, not host aliases.
                if (!ShaderFunction.IsMarked(func))
                    VisitBlock(func.Body, diagnostics);
                break;
            case ClassDeclaration cls:
                foreach (var member in cls.Members)
                {
                    if (member.Type == MemberType.Method && member.Value is FunctionDeclaration method &&
                        !ShaderFunction.IsMarked(method))
                        VisitBlock(method.Body, diagnostics);
                }
                break;
            case BlockStatement block:
                VisitBlock(block, diagnostics);
                break;
            case IfStatement ifStmt:
                VisitExpression(ifStmt.Condition, diagnostics);
                VisitStatement(ifStmt.ThenBranch, diagnostics);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, diagnostics);
                break;
            case WhileStatement whileStmt:
                VisitExpression(whileStmt.Condition, diagnostics);
                VisitStatement(whileStmt.Body, diagnostics);
                break;
            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, diagnostics);
                if (forStmt.Condition != null)
                    VisitExpression(forStmt.Condition, diagnostics);
                if (forStmt.Increment != null)
                    VisitExpression(forStmt.Increment, diagnostics);
                VisitStatement(forStmt.Body, diagnostics);
                break;
            case ForInStatement forIn:
                VisitExpression(forIn.Collection, diagnostics);
                VisitStatement(forIn.Body, diagnostics);
                break;
            case TryStatement tryStmt:
                VisitBlock(tryStmt.TryBlock, diagnostics);
                foreach (var clause in tryStmt.CatchClauses)
                    VisitBlock(clause.Body, diagnostics);
                if (tryStmt.FinallyBlock != null)
                    VisitBlock(tryStmt.FinallyBlock, diagnostics);
                break;
            case UsingStatement:
            case ImportStatement:
                break;
            case ExpressionStatement exprStmt:
                VisitExpression(exprStmt.Expression, diagnostics);
                break;
            case ReturnStatement ret when ret.Value != null:
                VisitExpression(ret.Value, diagnostics);
                break;
            case VarDeclStatement varDecl when varDecl.Initializer != null:
                VisitExpression(varDecl.Initializer, diagnostics);
                break;
            case AssignmentStatement assign:
                VisitExpression(assign.Target, diagnostics);
                VisitExpression(assign.Value, diagnostics);
                break;
            case ThrowStatement thr:
                VisitExpression(thr.Exception, diagnostics);
                break;
            case PrintStatement print:
                WarnDeprecatedFlatCall("print", print.Line, print.Column, diagnostics);
                VisitExpression(print.Expression, diagnostics);
                break;
        }
    }

    private static void VisitBlock(BlockStatement block, List<Diagnostic> diagnostics)
    {
        foreach (var statement in block.Statements)
            VisitStatement(statement, diagnostics);
    }

    private static void VisitExpression(Expression expression, List<Diagnostic> diagnostics)
    {
        switch (expression)
        {
            case FunctionCallExpression call:
                if (call.Callee is IdentifierExpression { ShadowsBuiltIn: false } id &&
                    StdLibNamespaces.TryGetDeprecatedFlatAliasMessage(id.Name, out var flatMessage))
                {
                    AddStyleWarning(flatMessage, id.Line, id.Column, id.Name.Length, diagnostics);
                }
                else if (call.Callee is MemberAccessExpression memberAccess &&
                         memberAccess.Object is IdentifierExpression moduleId &&
                         moduleId.Name == StdLibNamespaces.DeprecatedMathModuleAlias &&
                         StdLibNamespaces.IsStdLibModuleMethod(moduleId.Name, memberAccess.Member))
                {
                    AddStyleWarning(
                        StdLibNamespaces.DeprecatedMathModuleAliasMessage(memberAccess.Member),
                        moduleId.Line,
                        moduleId.Column,
                        moduleId.Name.Length,
                        diagnostics);
                }

                VisitExpression(call.Callee, diagnostics);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, diagnostics);
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, diagnostics);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, diagnostics);
                VisitExpression(binary.Right, diagnostics);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, diagnostics);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, diagnostics);
                VisitExpression(ternary.ThenBranch, diagnostics);
                VisitExpression(ternary.ElseBranch, diagnostics);
                break;
            case AwaitExpression awaitExpr:
                VisitExpression(awaitExpr.Expression, diagnostics);
                break;
            case ArrayAccessExpression arrayAccess:
                VisitExpression(arrayAccess.Array, diagnostics);
                VisitExpression(arrayAccess.Index, diagnostics);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var element in arrayLit.Elements)
                    VisitExpression(element, diagnostics);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (key, value) in objectLit.Properties)
                {
                    VisitExpression(key, diagnostics);
                    VisitExpression(value, diagnostics);
                }
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (key, value) in dictLit.Entries)
                {
                    VisitExpression(key, diagnostics);
                    VisitExpression(value, diagnostics);
                }
                break;
            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    VisitExpression(lambda.ExpressionBody, diagnostics);
                if (lambda.BlockBody != null)
                    VisitBlock(lambda.BlockBody, diagnostics);
                break;
            case MatchExpression match:
                VisitExpression(match.Value, diagnostics);
                foreach (var arm in match.Cases)
                {
                    if (arm.Guard != null)
                        VisitExpression(arm.Guard, diagnostics);
                    VisitStatement(arm.Body, diagnostics);
                }
                if (match.DefaultCase != null)
                    VisitStatement(match.DefaultCase, diagnostics);
                break;
            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.Expression != null)
                        VisitExpression(segment.Expression, diagnostics);
                }
                break;
            case NewExpression newExpr:
                foreach (var arg in newExpr.Arguments)
                    VisitExpression(arg, diagnostics);
                break;
            case AsyncExpression asyncExpr:
                VisitExpression(asyncExpr.Expression, diagnostics);
                break;
            case SpawnExpression spawn:
                foreach (var arg in spawn.Arguments)
                    VisitExpression(arg, diagnostics);
                break;
        }
    }

    private static void WarnDeprecatedFlatCall(string name, int line, int column, List<Diagnostic> diagnostics)
    {
        if (StdLibNamespaces.TryGetDeprecatedFlatAliasMessage(name, out var message))
            AddStyleWarning(message, line, column, name.Length, diagnostics);
    }

    private static void AddStyleWarning(string message, int line, int column, int length, List<Diagnostic> diagnostics)
    {
        diagnostics.Add(new Diagnostic
        {
            Severity = DiagnosticSeverity.Warning,
            Message = message,
            Line = line - 1,
            Column = column - 1,
            Length = length,
            Source = "malda-style"
        });
    }
}
