// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Runtime.Profiling;

/// <summary>
/// Synchronous fast path for expressions that never await: literals, variables, and
/// arithmetic / comparison / logical operators over them. Every await costs an async state
/// machine, and loops evaluate these expressions millions of times.
/// <para>
/// Whether a node has that shape is decided once and cached on the node
/// (<see cref="Expression.SyncEvalShape"/>). At run time an object operand may still need an
/// operator overload (<c>__add__</c>, <c>__neg__</c>, ...), which is an async call: the sync
/// evaluator then gives up and the caller re-evaluates on the async path. That is safe
/// because everything evaluated so far is a side-effect-free read.
/// </para>
/// </summary>
public partial class Interpreter
{
    private const byte SyncShapeUnknown = 0;
    private const byte SyncShapeYes = 1;
    private const byte SyncShapeNo = 2;

    internal Task<RuntimeValue> EvaluateAsync(Expression expr, bool returnTask = false)
    {
        try
        {
            if (TryEvaluateSync(expr, out var value))
                return Task.FromResult(value);
        }
        catch (Exception ex)
        {
            // Keep the async contract: errors surface when the task is awaited.
            return Task.FromException<RuntimeValue>(ex);
        }
        return EvaluateCoreAsync(expr, returnTask);
    }

    /// <summary>
    /// Evaluates <paramref name="expr"/> without awaiting. False means nothing observable
    /// happened and the caller must use <see cref="EvaluateAsync"/>.
    /// </summary>
    private bool TryEvaluateSync(Expression expr, out RuntimeValue value)
    {
        if (!HasSyncShape(expr))
        {
            value = null!;
            return false;
        }
        return TryEvaluateSyncCore(expr, out value);
    }

    private static bool HasSyncShape(Expression expr)
    {
        var shape = expr.SyncEvalShape;
        if (shape == SyncShapeUnknown)
        {
            shape = ComputeSyncShape(expr) ? SyncShapeYes : SyncShapeNo;
            expr.SyncEvalShape = shape;
        }
        return shape == SyncShapeYes;
    }

    private static bool ComputeSyncShape(Expression expr) => expr switch
    {
        LiteralExpression => true,
        IdentifierExpression => true,
        UnaryExpression unary => unary.Operator is TokenType.Minus or TokenType.Not
            && HasSyncShape(unary.Right),
        BinaryExpression binary => IsSyncBinaryOperator(binary.Operator)
            && HasSyncShape(binary.Left)
            && HasSyncShape(binary.Right),
        _ => false
    };

    private static bool IsSyncBinaryOperator(TokenType op) => op is
        TokenType.And or TokenType.Or or TokenType.NullCoalesce
        or TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo
        or TokenType.Equal or TokenType.NotEqual
        or TokenType.GreaterThan or TokenType.GreaterThanOrEqual
        or TokenType.LessThan or TokenType.LessThanOrEqual;

    /// <summary>Mirrors <see cref="EvaluateCoreAsync"/>, <see cref="EvaluateBinaryAsync"/> and <see cref="EvaluateUnaryAsync"/>.</summary>
    private bool TryEvaluateSyncCore(Expression expr, out RuntimeValue value)
    {
        switch (expr)
        {
            case LiteralExpression literal:
                value = RuntimeValueFromLiteral(literal);
                return true;

            case IdentifierExpression identifier:
                value = LookUpVariable(identifier);
                return true;

            case UnaryExpression unary:
            {
                if (!TryEvaluateSyncCore(unary.Right, out var right))
                {
                    value = null!;
                    return false;
                }
                if (unary.Operator == TokenType.Not)
                {
                    value = RuntimeValue.Boolean(!right.IsTruthy());
                    return true;
                }
                if (right.Type == ValueType.Object)
                {
                    value = null!;
                    return false; // may have __neg__
                }
                value = NegateValue(right, unary);
                return true;
            }

            case BinaryExpression binary:
                return TryEvaluateBinarySync(binary, out value);

            default:
                value = null!;
                return false;
        }
    }

    private bool TryEvaluateBinarySync(BinaryExpression expr, out RuntimeValue value)
    {
        value = null!;
        if (!TryEvaluateSyncCore(expr.Left, out var left))
            return false;

        switch (expr.Operator)
        {
            case TokenType.And:
                if (!left.IsTruthy())
                {
                    value = RuntimeValue.Boolean(false);
                    return true;
                }
                if (!TryEvaluateSyncCore(expr.Right, out var andRight))
                    return false;
                value = RuntimeValue.Boolean(andRight.IsTruthy());
                return true;

            case TokenType.Or:
                if (left.IsTruthy())
                {
                    value = RuntimeValue.Boolean(true);
                    return true;
                }
                if (!TryEvaluateSyncCore(expr.Right, out var orRight))
                    return false;
                value = RuntimeValue.Boolean(orRight.IsTruthy());
                return true;

            case TokenType.NullCoalesce:
                if (left.Type != ValueType.Null)
                {
                    value = left;
                    return true;
                }
                return TryEvaluateSyncCore(expr.Right, out value);
        }

        if (!TryEvaluateSyncCore(expr.Right, out var right))
            return false;
        if (left.Type == ValueType.Object || right.Type == ValueType.Object)
            return false; // may have an operator overload
        value = ApplyBinaryOperator(expr, left, right);
        return true;
    }

    private static readonly Task<RuntimeValue?> CompletedNullStatement = Task.FromResult<RuntimeValue?>(null);

    /// <summary>
    /// Signal-aware statement dispatch. Without a debugger, a profiling session or a change of
    /// source file, <see cref="ExecuteStatementSlowAsync"/>'s bookkeeping does nothing, so the
    /// common statements go straight to their executor and skip one async frame each.
    /// </summary>
    private Task<RuntimeValue?> ExecuteStatementAsync(Statement stmt)
    {
        if (_debuggerHook != null || MaldaProfiler.IsEnabled
            || (stmt.SourceFile != null && stmt.SourceFile != _currentFile))
            return ExecuteStatementSlowAsync(stmt);

        switch (stmt)
        {
            case IfStatement ifStmt:
                return ExecuteIfAsync(ifStmt);
            case BlockStatement block:
                return ExecuteBlockStatementsAsync(block, new Environment(_environment));
            case WhileStatement whileStmt:
                return ExecuteWhileAsync(whileStmt);
            case ForStatement forStmt:
                return ExecuteForAsync(forStmt);
            case ForInStatement forIn:
                return ExecuteForInAsync(forIn);
            case ReturnStatement returnStmt:
                return ExecuteReturnAsync(returnStmt);
            case BreakStatement:
                SignalControl(PendingControl.Break);
                return CompletedNullStatement;
            case ContinueStatement:
                SignalControl(PendingControl.Continue);
                return CompletedNullStatement;
            default:
                return ExecuteStatementSlowAsync(stmt);
        }
    }

    /// <summary>
    /// Runs a variable declaration, plain or compound assignment to a variable, or expression
    /// statement without awaiting. False means nothing ran and the caller must use
    /// <see cref="ExecuteStatementAsync"/>, which also owns the debugger and profiler hooks.
    /// </summary>
    private bool TryExecuteSync(Statement stmt, out RuntimeValue? result)
    {
        result = null;
        if (_debuggerHook != null || MaldaProfiler.IsEnabled)
            return false;
        if (stmt.SourceFile != null && stmt.SourceFile != _currentFile)
            return false;

        switch (stmt)
        {
            case VarDeclStatement varDecl:
                if (varDecl.Initializer == null || !TryEvaluateSync(varDecl.Initializer, out var initial))
                    return false;
                _environment.Define(varDecl.Name, initial, varDecl.IsConst);
                return true;

            case AssignmentStatement { Target: IdentifierExpression target } assign:
            {
                if (!HasSyncShape(assign.Value))
                    return false;
                RuntimeValue value;
                if (assign.Operator == TokenType.Assign)
                {
                    if (!TryEvaluateSyncCore(assign.Value, out value))
                        return false;
                }
                else
                {
                    if (assign.Operator is not (TokenType.PlusAssign or TokenType.MinusAssign
                        or TokenType.MultiplyAssign or TokenType.DivideAssign))
                        return false;
                    var current = ReadIdentifierLvalue(target);
                    if (!TryEvaluateSyncCore(assign.Value, out var rightValue))
                        return false;
                    value = ApplyCompoundAssignment(assign, current, rightValue);
                }
                AssignIdentifier(target, value);
                return true;
            }

            case ExpressionStatement expression:
                if (!TryEvaluateSync(expression.Expression, out var exprValue))
                    return false;
                result = exprValue;
                return true;

            default:
                return false;
        }
    }
}
