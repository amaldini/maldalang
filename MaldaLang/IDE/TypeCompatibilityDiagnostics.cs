// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Generic;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using TokenType = MaldaLang.TokenType;

/// <summary>
/// Checks type hints vs known values (literals, identifiers with hints, operators,
/// selected Tier-1 builtins, and typed call returns) on var/field initializers,
/// assignments, call arguments, and returns.
/// IDE/LSP default elevates mismatches to Error (<see cref="StrictTypesOptions.Default"/>);
/// use <see cref="StrictTypesOptions.Lenient"/> for Warning. Does not enforce hints at runtime.
/// </summary>
public static class TypeCompatibilityDiagnostics
{
    private sealed record FunctionHints(
        IReadOnlyList<string?> ParameterHints,
        string? ReturnType);

    private sealed class HintEnv
    {
        private readonly Stack<Dictionary<string, string>> _scopes = new();
        private readonly TypeHintNameIndex _index;

        public HintEnv(TypeHintNameIndex index)
        {
            _index = index;
            PushScope();
        }

        public TypeHintNameIndex Index => _index;

        public void PushScope() =>
            _scopes.Push(new Dictionary<string, string>(StringComparer.Ordinal));

        public void PopScope()
        {
            if (_scopes.Count > 1)
                _scopes.Pop();
        }

        public void Declare(string name, string typeHint)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(typeHint))
                return;
            if (string.Equals(typeHint, "any", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeHint, "void", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Only bind identifiers whose hints are known (Tier 0, declared, or host).
            if (!_index.IsKnown(typeHint))
                return;

            _scopes.Peek()[name] = typeHint;
        }

        public bool TryLookup(string name, out string typeHint)
        {
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out typeHint!))
                    return true;
            }

            typeHint = null!;
            return false;
        }
    }

    public static void Validate(
        IEnumerable<Statement> statements,
        List<Diagnostic> diagnostics,
        StrictTypesOptions? options = null,
        string? sourceFileName = null)
    {
        options ??= StrictTypesOptions.Default;
        var list = statements as IList<Statement> ?? statements.ToList();
        var index = TypeHintNameIndex.Build(list);
        var functions = new Dictionary<string, FunctionHints>(StringComparer.Ordinal);
        CollectFunctions(list, functions);
        MergeImportedHints(list, sourceFileName, index, functions);
        var env = new HintEnv(index);

        foreach (var stmt in list)
            VisitStatement(stmt, functions, env, currentReturn: null, diagnostics, options);
    }

    private static void MergeImportedHints(
        IList<Statement> hostStatements,
        string? sourceFileName,
        TypeHintNameIndex index,
        Dictionary<string, FunctionHints> functions)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName))
            return;

        try
        {
            var imported = ModuleSymbolResolver.LoadImportedSymbols(hostStatements, sourceFileName);
            index.MergeImported(imported);
            foreach (var func in imported.Functions)
            {
                if (!functions.ContainsKey(func.Name))
                {
                    functions[func.Name] = new FunctionHints(
                        func.ParameterTypeHints ?? new List<string?>(),
                        func.ReturnType);
                }
            }

            foreach (var classDecl in imported.Classes)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Value is FunctionDeclaration method &&
                        !functions.ContainsKey(method.Name))
                    {
                        functions[method.Name] = new FunctionHints(
                            method.ParameterTypeHints ?? new List<string?>(),
                            method.ReturnType);
                    }
                }
            }
        }
        catch
        {
            // Best-effort: broken imports are reported elsewhere.
        }
    }

    private static void CollectFunctions(
        IEnumerable<Statement> statements,
        Dictionary<string, FunctionHints> functions)
    {
        foreach (var stmt in statements)
        {
            switch (stmt)
            {
                case FunctionDeclaration funcDecl:
                    functions[funcDecl.Name] = new FunctionHints(
                        funcDecl.ParameterTypeHints ?? new List<string?>(),
                        funcDecl.ReturnType);
                    break;
                case ClassDeclaration classDecl:
                    foreach (var member in classDecl.Members)
                    {
                        if (member.Value is FunctionDeclaration method)
                        {
                            // Index unqualified method name for same-file simple calls; first wins.
                            if (!functions.ContainsKey(method.Name))
                            {
                                functions[method.Name] = new FunctionHints(
                                    method.ParameterTypeHints ?? new List<string?>(),
                                    method.ReturnType);
                            }
                        }
                    }
                    break;
                case BlockStatement block:
                    CollectFunctions(block.Statements, functions);
                    break;
            }
        }
    }

    private static bool TryGetCalleeHints(
        Expression callee,
        Dictionary<string, FunctionHints> functions,
        out string calleeName,
        out FunctionHints hints)
    {
        if (callee is IdentifierExpression id &&
            functions.TryGetValue(id.Name, out hints!))
        {
            calleeName = id.Name;
            return true;
        }

        if (callee is MemberAccessExpression member &&
            functions.TryGetValue(member.Member, out hints!))
        {
            calleeName = member.Member;
            return true;
        }

        calleeName = "";
        hints = null!;
        return false;
    }

    private static void BindFunctionParameters(FunctionDeclaration funcDecl, HintEnv env)
    {
        var hints = funcDecl.ParameterTypeHints;
        if (hints == null)
            return;

        for (var i = 0; i < funcDecl.Parameters.Count && i < hints.Count; i++)
        {
            if (hints[i] != null)
                env.Declare(funcDecl.Parameters[i], hints[i]!);
        }
    }

    private static void VisitStatement(
        Statement stmt,
        Dictionary<string, FunctionHints> functions,
        HintEnv env,
        string? currentReturn,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        switch (stmt)
        {
            case FunctionDeclaration funcDecl:
                env.PushScope();
                BindFunctionParameters(funcDecl, env);
                foreach (var inner in funcDecl.Body.Statements)
                    VisitStatement(inner, functions, env, funcDecl.ReturnType, diagnostics, options);
                env.PopScope();
                break;
            case VarDeclStatement varDecl:
                if (varDecl.TypeHint != null)
                {
                    CheckValueCompatibility(
                        varDecl.TypeHint,
                        varDecl.Initializer,
                        $"variable '{varDecl.Name}'",
                        varDecl.Line,
                        varDecl.Column,
                        env,
                        functions,
                        diagnostics,
                        options);
                    env.Declare(varDecl.Name, varDecl.TypeHint);
                }
                VisitExpression(varDecl.Initializer, functions, env, diagnostics, options);
                break;
            case ClassDeclaration classDecl:
                foreach (var member in classDecl.Members)
                {
                    if (member.TypeHint != null && member.Value is Expression fieldInit)
                    {
                        CheckValueCompatibility(
                            member.TypeHint,
                            fieldInit,
                            $"field '{member.Name}'",
                            classDecl.Line,
                            classDecl.Column,
                            env,
                            functions,
                            diagnostics,
                            options);
                        VisitExpression(fieldInit, functions, env, diagnostics, options);
                    }
                    if (member.Value is FunctionDeclaration method)
                    {
                        env.PushScope();
                        BindFunctionParameters(method, env);
                        foreach (var inner in method.Body.Statements)
                            VisitStatement(inner, functions, env, method.ReturnType, diagnostics, options);
                        env.PopScope();
                    }
                }
                break;
            case BlockStatement block:
                env.PushScope();
                foreach (var inner in block.Statements)
                    VisitStatement(inner, functions, env, currentReturn, diagnostics, options);
                env.PopScope();
                break;
            case ExpressionStatement exprStmt:
                VisitExpression(exprStmt.Expression, functions, env, diagnostics, options);
                break;
            case ReturnStatement returnStmt:
                if (currentReturn != null && returnStmt.Value != null)
                {
                    CheckValueCompatibility(
                        currentReturn,
                        returnStmt.Value,
                        "return value",
                        returnStmt.Line,
                        returnStmt.Column,
                        env,
                        functions,
                        diagnostics,
                        options);
                }
                if (returnStmt.Value != null)
                    VisitExpression(returnStmt.Value, functions, env, diagnostics, options);
                break;
            case IfStatement ifStmt:
                VisitExpression(ifStmt.Condition, functions, env, diagnostics, options);
                env.PushScope();
                VisitStatement(ifStmt.ThenBranch, functions, env, currentReturn, diagnostics, options);
                env.PopScope();
                if (ifStmt.ElseBranch != null)
                {
                    env.PushScope();
                    VisitStatement(ifStmt.ElseBranch, functions, env, currentReturn, diagnostics, options);
                    env.PopScope();
                }
                break;
            case WhileStatement whileStmt:
                VisitExpression(whileStmt.Condition, functions, env, diagnostics, options);
                env.PushScope();
                VisitStatement(whileStmt.Body, functions, env, currentReturn, diagnostics, options);
                env.PopScope();
                break;
            case ForStatement forStmt:
                env.PushScope();
                if (forStmt.Initializer != null)
                    VisitStatement(forStmt.Initializer, functions, env, currentReturn, diagnostics, options);
                if (forStmt.Condition != null)
                    VisitExpression(forStmt.Condition, functions, env, diagnostics, options);
                if (forStmt.Increment != null)
                    VisitExpression(forStmt.Increment, functions, env, diagnostics, options);
                VisitStatement(forStmt.Body, functions, env, currentReturn, diagnostics, options);
                env.PopScope();
                break;
            case ForInStatement forInStmt:
                VisitExpression(forInStmt.Collection, functions, env, diagnostics, options);
                env.PushScope();
                VisitStatement(forInStmt.Body, functions, env, currentReturn, diagnostics, options);
                env.PopScope();
                break;
            case TryStatement tryStmt:
                env.PushScope();
                foreach (var inner in tryStmt.TryBlock.Statements)
                    VisitStatement(inner, functions, env, currentReturn, diagnostics, options);
                env.PopScope();
                foreach (var catchClause in tryStmt.CatchClauses)
                {
                    env.PushScope();
                    VisitStatement(catchClause.Body, functions, env, currentReturn, diagnostics, options);
                    env.PopScope();
                }
                if (tryStmt.FinallyBlock != null)
                {
                    env.PushScope();
                    foreach (var inner in tryStmt.FinallyBlock.Statements)
                        VisitStatement(inner, functions, env, currentReturn, diagnostics, options);
                    env.PopScope();
                }
                break;
            case AssignmentStatement assign:
                if (assign.Target is IdentifierExpression targetId &&
                    env.TryLookup(targetId.Name, out var targetHint))
                {
                    CheckValueCompatibility(
                        targetHint,
                        assign.Value,
                        $"variable '{targetId.Name}'",
                        assign.Line > 0 ? assign.Line : assign.Value.Line,
                        assign.Column > 0 ? assign.Column : assign.Value.Column,
                        env,
                        functions,
                        diagnostics,
                        options);
                }
                VisitExpression(assign.Value, functions, env, diagnostics, options);
                break;
        }
    }

    private static void VisitExpression(
        Expression expression,
        Dictionary<string, FunctionHints> functions,
        HintEnv env,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        switch (expression)
        {
            case FunctionCallExpression call:
                if (TryGetCalleeHints(call.Callee, functions, out var calleeName, out var hints))
                {
                    for (var i = 0; i < call.Arguments.Count; i++)
                    {
                        string? paramHint = null;
                        if (hints.ParameterHints != null && i < hints.ParameterHints.Count)
                            paramHint = hints.ParameterHints[i];
                        if (paramHint == null)
                            continue;

                        var paramName = $"argument {i + 1} of '{calleeName}'";
                        CheckValueCompatibility(
                            paramHint,
                            call.Arguments[i],
                            paramName,
                            call.Arguments[i].Line > 0 ? call.Arguments[i].Line : call.Line,
                            call.Arguments[i].Column > 0 ? call.Arguments[i].Column : call.Column,
                            env,
                            functions,
                            diagnostics,
                            options);
                    }
                }
                foreach (var arg in call.Arguments)
                    VisitExpression(arg, functions, env, diagnostics, options);
                VisitExpression(call.Callee, functions, env, diagnostics, options);
                break;
            case BinaryExpression binary:
                VisitExpression(binary.Left, functions, env, diagnostics, options);
                VisitExpression(binary.Right, functions, env, diagnostics, options);
                break;
            case UnaryExpression unary:
                VisitExpression(unary.Right, functions, env, diagnostics, options);
                break;
            case TernaryExpression ternary:
                VisitExpression(ternary.Condition, functions, env, diagnostics, options);
                VisitExpression(ternary.ThenBranch, functions, env, diagnostics, options);
                VisitExpression(ternary.ElseBranch, functions, env, diagnostics, options);
                break;
            case ArrayLiteralExpression array:
                foreach (var el in array.Elements)
                    VisitExpression(el, functions, env, diagnostics, options);
                break;
            case ObjectLiteralExpression obj:
                foreach (var (_, value) in obj.Properties)
                    VisitExpression(value, functions, env, diagnostics, options);
                break;
            case DictionaryLiteralExpression dict:
                foreach (var (key, value) in dict.Entries)
                {
                    VisitExpression(key, functions, env, diagnostics, options);
                    VisitExpression(value, functions, env, diagnostics, options);
                }
                break;
            case ArrayAccessExpression access:
                VisitExpression(access.Array, functions, env, diagnostics, options);
                VisitExpression(access.Index, functions, env, diagnostics, options);
                break;
            case MemberAccessExpression member:
                VisitExpression(member.Object, functions, env, diagnostics, options);
                break;
            case InterpolatedStringExpression interpolated:
                foreach (var segment in interpolated.Segments)
                {
                    if (segment.IsExpression && segment.Expression != null)
                        VisitExpression(segment.Expression, functions, env, diagnostics, options);
                }
                break;
            case NewExpression newExpr:
                foreach (var arg in newExpr.Arguments)
                    VisitExpression(arg, functions, env, diagnostics, options);
                break;
            case AwaitExpression awaitExpr:
                VisitExpression(awaitExpr.Expression, functions, env, diagnostics, options);
                break;
            case AsyncExpression asyncExpr:
                VisitExpression(asyncExpr.Expression, functions, env, diagnostics, options);
                break;
        }
    }

    private static void CheckValueCompatibility(
        string typeHint,
        Expression value,
        string bindingName,
        int line,
        int column,
        HintEnv env,
        Dictionary<string, FunctionHints> functions,
        List<Diagnostic> diagnostics,
        StrictTypesOptions options)
    {
        if (string.Equals(typeHint, "any", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "void", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var expected = env.Index.NormalizeKnown(typeHint);
        if (expected == null)
            return; // unknown hint names are handled by TypeHintDiagnostics

        var actual = InferKnownType(value, env, functions);
        if (actual == null)
            return; // not a literal, new, known identifier, or typed call — out of scope

        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return;

        // int/float: a float hint accepts int values.
        if (expected == "float" && actual == "int")
            return;

        // Tier 0 aliases that NormalizeKnown left as written (void/any already returned).
        var expectedCanonical = Tier0TypeTags.NormalizeToCanonical(expected) ?? expected;
        var actualCanonical = Tier0TypeTags.NormalizeToCanonical(actual) ?? actual;
        if (string.Equals(expectedCanonical, actualCanonical, StringComparison.Ordinal))
            return;
        if (expectedCanonical == "float" && actualCanonical == "int")
            return;

        var elevate = options.ElevateTypeSeverity;
        diagnostics.Add(new Diagnostic
        {
            Severity = elevate ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
            Message = elevate
                ? $"Type hint '{typeHint}' on {bindingName} does not match value (got {actual})."
                : $"Type hint '{typeHint}' on {bindingName} does not match value (got {actual}). Hints are not enforced at runtime.",
            Line = line,
            Column = column,
            Length = Math.Max(1, typeHint.Length),
            Source = "malda-types"
        });
    }

    /// <summary>
    /// Returns a Tier 0 tag, declared/host class name, call return hint, or null when out of scope.
    /// </summary>
    private static string? InferKnownType(
        Expression expression,
        HintEnv env,
        Dictionary<string, FunctionHints> functions)
    {
        var literal = InferLiteralTag(expression);
        if (literal != null)
            return literal;

        if (expression is NewExpression newExpr &&
            env.Index.IsKnown(newExpr.ClassName) &&
            !Tier0TypeHints.IsKnown(newExpr.ClassName))
        {
            // Prefer the declared/host spelling (ordinal) when known.
            return newExpr.ClassName;
        }

        if (expression is IdentifierExpression id &&
            env.TryLookup(id.Name, out var hint))
        {
            return env.Index.NormalizeKnown(hint);
        }

        if (expression is AwaitExpression awaitExpr)
            return InferKnownType(awaitExpr.Expression, env, functions);

        if (expression is BinaryExpression binary)
            return InferOperatorResultType(binary, env, functions);

        if (expression is UnaryExpression unary)
            return InferUnaryResultType(unary, env, functions);

        if (expression is FunctionCallExpression call)
        {
            if (TryGetCalleeHints(call.Callee, functions, out _, out var calleeHints) &&
                !string.IsNullOrWhiteSpace(calleeHints.ReturnType))
            {
                return env.Index.NormalizeKnown(calleeHints.ReturnType);
            }

            var builtin = Tier1BuiltinReturnHints.TryGetReturnType(call.Callee);
            if (builtin != null)
                return env.Index.NormalizeKnown(builtin) ?? builtin;
        }

        if (expression is InterpolatedStringExpression)
            return "string";

        return null;
    }

    private static string? InferOperatorResultType(
        BinaryExpression binary,
        HintEnv env,
        Dictionary<string, FunctionHints> functions)
    {
        var left = InferKnownType(binary.Left, env, functions);
        var right = InferKnownType(binary.Right, env, functions);

        switch (binary.Operator)
        {
            case TokenType.Equal:
            case TokenType.NotEqual:
            case TokenType.LessThan:
            case TokenType.GreaterThan:
            case TokenType.LessThanOrEqual:
            case TokenType.GreaterThanOrEqual:
            case TokenType.And:
            case TokenType.Or:
                // Comparisons/logicals yield bool when both sides are at least present as expressions;
                // only require inferable sides for consistency with numeric ops.
                if (left == null || right == null)
                    return null;
                return "bool";

            case TokenType.Plus:
                if (left == null || right == null)
                    return null;
                if (left == "string" || right == "string")
                    return "string";
                return PromoteNumeric(left, right);

            case TokenType.Minus:
            case TokenType.Multiply:
            case TokenType.Modulo:
                if (left == null || right == null)
                    return null;
                return PromoteNumeric(left, right);

            case TokenType.Divide:
                if (left == null || right == null)
                    return null;
                if (IsNumericTag(left) && IsNumericTag(right))
                    return "float";
                return null;

            default:
                return null;
        }
    }

    private static string? InferUnaryResultType(
        UnaryExpression unary,
        HintEnv env,
        Dictionary<string, FunctionHints> functions)
    {
        var operand = InferKnownType(unary.Right, env, functions);
        if (operand == null)
            return null;

        return unary.Operator switch
        {
            TokenType.Minus when IsNumericTag(operand) => operand,
            TokenType.Not => "bool",
            _ => null
        };
    }

    private static string? PromoteNumeric(string left, string right)
    {
        if (!IsNumericTag(left) || !IsNumericTag(right))
            return null;
        if (left == "float" || right == "float")
            return "float";
        return "int";
    }

    private static bool IsNumericTag(string tag) =>
        tag is "int" or "float";

    /// <summary>
    /// Returns a canonical Tier 0 tag for a literal expression, or null if not a checked literal.
    /// </summary>
    internal static string? InferLiteralTag(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression lit:
                if (lit.Value is null)
                    return "null";
                if (lit.Value is bool)
                    return "bool";
                if (lit.Value is string)
                    return "string";
                if (lit.Value is int or long or byte or sbyte or short or ushort or uint)
                    return "int";
                if (lit.Value is float or double or decimal)
                    return "float";
                if (lit.Value is IConvertible convertible && lit.Value is not string)
                {
                    var typeCode = convertible.GetTypeCode();
                    if (typeCode is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16
                        or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64)
                        return "int";
                    if (typeCode is TypeCode.Single or TypeCode.Double or TypeCode.Decimal)
                        return "float";
                }
                return null;
            case ArrayLiteralExpression arr when arr.Elements.Count == 0:
                return "array";
            case DictionaryLiteralExpression dict when dict.Entries.Count == 0:
                return "dict";
            default:
                return null;
        }
    }
}
