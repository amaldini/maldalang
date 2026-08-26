// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.IDE.Models;

/// <summary>
/// Phase 6.1: under <c>--strict-types</c>, reject <c>@pure</c> functions that perform IO or call impure callees.
/// </summary>
public static class PureEffectsDiagnostics
{
    public static void Validate(
        IEnumerable<Statement> statements,
        StrictTypesOptions options,
        List<Diagnostic> diagnostics)
    {
        if (!options.StrictTypes)
            return;

        var purity = BuildPurityIndex(statements);
        foreach (var func in purity.PureFunctions)
            ValidatePureFunction(func, purity, diagnostics);

        foreach (var func in purity.EffectsFunctions)
            ValidateEffectsFunction(func, purity, diagnostics);

        foreach (var func in purity.ConflictingFunctions)
        {
            diagnostics.Add(new Diagnostic
            {
                // LanguageService / LSP / Desktop IDE all use 0-based coordinates.
                Line = Math.Max(0, func.Line - 1),
                Column = Math.Max(0, func.Column - 1),
                Severity = DiagnosticSeverity.Error,
                Source = "malda-pure",
                Message = $"Function '{func.Name}' cannot declare both @pure and @effects."
            });
        }
    }

    private static void ValidatePureFunction(
        FunctionDeclaration func,
        PurityIndex purity,
        List<Diagnostic> diagnostics)
    {
        var violations = new List<(int line, int column, string message)>();
        VisitStatement(func.Body, purity, EffectCheckMode.Pure, allowedEffects: null, violations);

        foreach (var (line, column, message) in violations)
        {
            diagnostics.Add(new Diagnostic
            {
                Line = Math.Max(0, line - 1),
                Column = Math.Max(0, column - 1),
                Severity = DiagnosticSeverity.Error,
                Source = "malda-pure",
                Message = $"@pure function '{func.Name}': {message}"
            });
        }
    }

    private static void ValidateEffectsFunction(
        FunctionDeclaration func,
        PurityIndex purity,
        List<Diagnostic> diagnostics)
    {
        if (!purity.TryGetAllowedEffects(func.Name, out var allowed))
            return;

        var violations = new List<(int line, int column, string message)>();
        VisitStatement(func.Body, purity, EffectCheckMode.EffectsAllowList, allowed, violations);

        foreach (var (line, column, message) in violations)
        {
            diagnostics.Add(new Diagnostic
            {
                Line = Math.Max(0, line - 1),
                Column = Math.Max(0, column - 1),
                Severity = DiagnosticSeverity.Error,
                Source = "malda-effects",
                Message = $"@effects function '{func.Name}': {message}"
            });
        }
    }

    private enum EffectCheckMode
    {
        Pure,
        EffectsAllowList
    }

    private static void VisitStatement(
        Statement stmt,
        PurityIndex purity,
        EffectCheckMode mode,
        IReadOnlySet<string>? allowedEffects,
        List<(int line, int column, string message)> violations)
    {
        switch (stmt)
        {
            case BlockStatement block:
                foreach (var inner in block.Statements)
                    VisitStatement(inner, purity, mode, allowedEffects, violations);
                break;
            case IfStatement ifStmt:
                VisitStatement(ifStmt.ThenBranch, purity, mode, allowedEffects, violations);
                if (ifStmt.ElseBranch != null)
                    VisitStatement(ifStmt.ElseBranch, purity, mode, allowedEffects, violations);
                break;
            case WhileStatement whileStmt:
                VisitStatement(whileStmt.Body, purity, mode, allowedEffects, violations);
                break;
            case ForStatement forStmt:
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, purity, mode, allowedEffects, violations);
                VisitStatement(forStmt.Body, purity, mode, allowedEffects, violations);
                break;
            case ForInStatement forIn:
                VisitStatement(forIn.Body, purity, mode, allowedEffects, violations);
                break;
            case TryStatement tryStmt:
                VisitBlock(tryStmt.TryBlock, purity, mode, allowedEffects, violations);
                foreach (var clause in tryStmt.CatchClauses)
                    VisitBlock(clause.Body, purity, mode, allowedEffects, violations);
                if (tryStmt.FinallyBlock != null)
                    VisitBlock(tryStmt.FinallyBlock, purity, mode, allowedEffects, violations);
                break;
            case PrintStatement print:
                if (mode == EffectCheckMode.Pure)
                    violations.Add((print.Line, print.Column, "print statement is not allowed in @pure code"));
                else if (!PureEffectsBuiltIns.IsEffectAllowed(allowedEffects!, "print"))
                    violations.Add((print.Line, print.Column, "print is not listed in @effects"));
                VisitExpression(print.Expression, purity, mode, allowedEffects, violations);
                break;
            case ExpressionStatement expr:
                VisitExpression(expr.Expression, purity, mode, allowedEffects, violations);
                break;
            case ReturnStatement ret when ret.Value != null:
                VisitExpression(ret.Value, purity, mode, allowedEffects, violations);
                break;
            case ThrowStatement thr:
                VisitExpression(thr.Exception, purity, mode, allowedEffects, violations);
                break;
            case VarDeclStatement varDecl when varDecl.Initializer != null:
                VisitExpression(varDecl.Initializer, purity, mode, allowedEffects, violations);
                break;
            case AssignmentStatement assign:
                VisitExpression(assign.Target, purity, mode, allowedEffects, violations);
                VisitExpression(assign.Value, purity, mode, allowedEffects, violations);
                break;
        }
    }

    private static void VisitBlock(
        BlockStatement block,
        PurityIndex purity,
        EffectCheckMode mode,
        IReadOnlySet<string>? allowedEffects,
        List<(int line, int column, string message)> violations)
    {
        foreach (var stmt in block.Statements)
            VisitStatement(stmt, purity, mode, allowedEffects, violations);
    }

    private static void VisitExpression(
        Expression expression,
        PurityIndex purity,
        EffectCheckMode mode,
        IReadOnlySet<string>? allowedEffects,
        List<(int line, int column, string message)> violations)
    {
        switch (expression)
        {
            case FunctionCallExpression call:
                CheckCall(call, purity, mode, allowedEffects, violations);
                VisitExpression(call.Callee, purity, mode, allowedEffects, violations);
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, purity, mode, allowedEffects, violations);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, purity, mode, allowedEffects, violations);
                VisitExpression(binary.Right, purity, mode, allowedEffects, violations);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, purity, mode, allowedEffects, violations);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, purity, mode, allowedEffects, violations);
                VisitExpression(ternary.ThenBranch, purity, mode, allowedEffects, violations);
                VisitExpression(ternary.ElseBranch, purity, mode, allowedEffects, violations);
                break;
            case AwaitExpression awaitExpr:
                if (mode == EffectCheckMode.Pure)
                    violations.Add((awaitExpr.Line, awaitExpr.Column, "await is not allowed in @pure code"));
                else if (!PureEffectsBuiltIns.IsEffectAllowed(allowedEffects!, "await"))
                    violations.Add((awaitExpr.Line, awaitExpr.Column, "await is not listed in @effects"));
                VisitExpression(awaitExpr.Expression, purity, mode, allowedEffects, violations);
                break;
            case MatchExpression match:
                VisitExpression(match.Value, purity, mode, allowedEffects, violations);
                foreach (var arm in match.Cases)
                {
                    if (arm.Guard != null)
                        VisitExpression(arm.Guard, purity, mode, allowedEffects, violations);
                    VisitStatement(arm.Body, purity, mode, allowedEffects, violations);
                }
                if (match.DefaultCase != null)
                    VisitStatement(match.DefaultCase, purity, mode, allowedEffects, violations);
                break;
            case ArrayAccessExpression arrayAccess:
                VisitExpression(arrayAccess.Array, purity, mode, allowedEffects, violations);
                VisitExpression(arrayAccess.Index, purity, mode, allowedEffects, violations);
                break;
            case ArrayLiteralExpression arrayLit:
                foreach (var element in arrayLit.Elements)
                    VisitExpression(element, purity, mode, allowedEffects, violations);
                break;
            case ObjectLiteralExpression objectLit:
                foreach (var (_, value) in objectLit.Properties)
                    VisitExpression(value, purity, mode, allowedEffects, violations);
                break;
            case DictionaryLiteralExpression dictLit:
                foreach (var (_, value) in dictLit.Entries)
                    VisitExpression(value, purity, mode, allowedEffects, violations);
                break;
            case LambdaExpression lambda:
                if (lambda.ExpressionBody != null)
                    VisitExpression(lambda.ExpressionBody, purity, mode, allowedEffects, violations);
                if (lambda.BlockBody != null)
                    VisitBlock(lambda.BlockBody, purity, mode, allowedEffects, violations);
                break;
            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.Expression != null)
                        VisitExpression(segment.Expression, purity, mode, allowedEffects, violations);
                }
                break;
            case NewExpression newExpr:
                if (mode == EffectCheckMode.Pure)
                    violations.Add((newExpr.Line, newExpr.Column, "object construction may have side effects"));
                foreach (var arg in newExpr.Arguments)
                    VisitExpression(arg, purity, mode, allowedEffects, violations);
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, purity, mode, allowedEffects, violations);
                break;
        }
    }

    private static void CheckCall(
        FunctionCallExpression call,
        PurityIndex purity,
        EffectCheckMode mode,
        IReadOnlySet<string>? allowedEffects,
        List<(int line, int column, string message)> violations)
    {
        if (call.Callee is IdentifierExpression id)
        {
            if (PureEffectsBuiltIns.IsIoEffect(id.Name))
            {
                if (mode == EffectCheckMode.Pure)
                    violations.Add((call.Line, call.Column, $"IO builtin '{id.Name}' is not allowed"));
                else if (!PureEffectsBuiltIns.IsEffectAllowed(allowedEffects!, id.Name))
                    violations.Add((call.Line, call.Column, $"IO builtin '{id.Name}' is not listed in @effects"));
                return;
            }

            if (mode == EffectCheckMode.Pure &&
                purity.IsUserFunction(id.Name) &&
                !purity.IsDeclaredPure(id.Name))
                violations.Add((call.Line, call.Column, $"call to non-@pure function '{id.Name}'"));

            return;
        }

        if (call.Callee is MemberAccessExpression member &&
            member.Object is IdentifierExpression root &&
            PureEffectsBuiltIns.IsIoMemberAccess(root.Name, member.Member))
        {
            if (mode == EffectCheckMode.Pure)
                violations.Add((call.Line, call.Column, $"IO namespace call '{root.Name}.{member.Member}' is not allowed"));
            else if (!PureEffectsBuiltIns.IsEffectAllowed(allowedEffects!, member.Member, root.Name))
                violations.Add((call.Line, call.Column, $"IO namespace call '{root.Name}.{member.Member}' is not listed in @effects"));
        }
    }

    private static PurityIndex BuildPurityIndex(IEnumerable<Statement> statements)
    {
        var index = new PurityIndex();
        foreach (var stmt in statements)
            CollectFunctions(stmt, index);
        index.Finalize();
        return index;
    }

    private static void CollectFunctions(Statement stmt, PurityIndex index)
    {
        switch (stmt)
        {
            case FunctionDeclaration func:
                index.Register(func);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration method)
                        index.Register(method);
                }
                break;
        }
    }

    private static bool HasPureDecorator(FunctionDeclaration func) =>
        DecoratorArgs.HasDecorator(func, "pure");

    private static bool HasEffectsDecorator(FunctionDeclaration func) =>
        DecoratorArgs.HasDecorator(func, "effects");

    private sealed class PurityIndex
    {
        private readonly HashSet<string> _userFunctions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _pureFunctions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _allowedEffects = new(StringComparer.Ordinal);
        private readonly List<FunctionDeclaration> _pureFunctionDecls = [];
        private readonly List<FunctionDeclaration> _effectsFunctionDecls = [];
        private readonly List<FunctionDeclaration> _conflictingFunctionDecls = [];

        public IReadOnlyList<FunctionDeclaration> PureFunctions => _pureFunctionDecls;
        public IReadOnlyList<FunctionDeclaration> EffectsFunctions => _effectsFunctionDecls;
        public IReadOnlyList<FunctionDeclaration> ConflictingFunctions => _conflictingFunctionDecls;

        public void Register(FunctionDeclaration func)
        {
            _userFunctions.Add(func.Name);

            var isPure = HasPureDecorator(func);
            var isEffects = HasEffectsDecorator(func);

            if (isPure && isEffects)
            {
                _conflictingFunctionDecls.Add(func);
                return;
            }

            if (isPure)
            {
                _pureFunctions.Add(func.Name);
                _pureFunctionDecls.Add(func);
            }

            if (isEffects)
            {
                var decorator = DecoratorArgs.FindDecorator(func, "effects");
                var allowed = new HashSet<string>(StringComparer.Ordinal);
                if (decorator != null)
                {
                    foreach (var effect in DecoratorArgs.ReadStringArguments(decorator))
                        allowed.Add(effect);
                }

                _allowedEffects[func.Name] = allowed;
                _effectsFunctionDecls.Add(func);
            }
        }

        public void Finalize() { }

        public bool IsUserFunction(string name) => _userFunctions.Contains(name);

        public bool IsDeclaredPure(string name) => _pureFunctions.Contains(name);

        public bool TryGetAllowedEffects(string name, out IReadOnlySet<string> allowed)
        {
            if (_allowedEffects.TryGetValue(name, out var set))
            {
                allowed = set;
                return true;
            }

            allowed = new HashSet<string>(StringComparer.Ordinal);
            return false;
        }
    }
}
