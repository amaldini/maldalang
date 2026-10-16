// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private void TranspileExpression(Expression expr)
    {
        switch (expr)
        {
            case LiteralExpression literal:
                TranspileLiteral(literal);
                break;
            case IdentifierExpression identifier:
                if (_catchFilterRenameFrom != null &&
                    _catchFilterRenameTo != null &&
                    identifier.Name == _catchFilterRenameFrom)
                {
                    _output.Append(_catchFilterRenameTo);
                }
                else
                {
                    _output.Append(EscapeIdentifier(identifier.Name));
                }
                break;
            case NamedArgumentExpression named:
                TranspileExpression(named.Value);
                break;
            case BinaryExpression binary:
                TranspileBinary(binary);
                break;
            case UnaryExpression unary:
                TranspileUnary(unary);
                break;
            case PostfixExpression postfix:
                TranspilePostfix(postfix);
                break;
            case FunctionCallExpression call:
                TranspileFunctionCall(call);
                break;
            case MemberAccessExpression member:
                TranspileMemberAccess(member);
                break;
            case NewExpression newExpr:
                TranspileNew(newExpr);
                break;
            case ThisExpression:
                _output.Append("this");
                break;
            case SuperExpression:
                _output.Append("base");
                break;
            case SpawnExpression spawn:
                TranspileSpawn(spawn);
                break;
            case ReceiveExpression:
                // receive() in transpiled code maps to ActorsRuntime.ReceiveAsync()
                // and is synchronously awaited to match blocking semantics.
                _output.Append("ActorsRuntime.ReceiveAsync().GetAwaiter().GetResult()");
                break;
            case SelfExpression:
                // self maps to current actor reference in ActorsRuntime
                _output.Append("(object)ActorsRuntime.GetSelf()");
                break;
            case ArrayLiteralExpression array:
                TranspileArrayLiteral(array);
                break;
            case ObjectLiteralExpression obj:
                TranspileObjectLiteral(obj);
                break;
            case DictionaryLiteralExpression dict:
                TranspileDictionaryLiteral(dict);
                break;
            case GraphLiteralExpression graph:
                TranspileGraphLiteral(graph);
                break;
            case ArrayAccessExpression arrayAccess:
                TranspileArrayAccess(arrayAccess);
                break;
            case TernaryExpression ternary:
                TranspileTernary(ternary);
                break;
            case InterpolatedStringExpression interpolated:
                TranspileInterpolatedString(interpolated);
                break;
            case LambdaExpression lambda:
                TranspileLambda(lambda);
                break;
            case MatchExpression match:
                TranspileMatch(match);
                break;
            case PipeExpression pipe:
                TranspilePipe(pipe);
                break;
            case ListComprehensionExpression comprehension:
                TranspileListComprehension(comprehension);
                break;
            case DictComprehensionExpression dictComprehension:
                TranspileDictComprehension(dictComprehension);
                break;
            case AwaitExpression awaitExpr:
                if (awaitExpr.Expression is FunctionCallExpression awaitCall &&
                    awaitCall.Callee is IdentifierExpression awaitCallee &&
                    _promptNames.Contains(awaitCallee.Name))
                {
                    _output.Append("await ");
                    _output.Append(EscapeIdentifier(awaitCallee.Name));
                    _output.Append("__ExecuteAsync(");
                    for (int i = 0; i < awaitCall.Arguments.Count; i++)
                    {
                        if (i > 0) _output.Append(", ");
                        TranspileExpression(awaitCall.Arguments[i]);
                    }
                    _output.Append(")");
                }
                else if (awaitExpr.Expression is PipeExpression awaitPipe)
                {
                    _output.Append("await MaldaLang.BuiltIns.AiPipelineHelpers.CoerceAwaitResultAsync(RuntimeHelpers.ToRuntimeValue(");
                    TranspilePipe(awaitPipe);
                    _output.Append("), null)");
                }
                else
                {
                    _output.Append("await RuntimeHelpers.UnwrapTaskAsync(");
                    TranspileExpression(awaitExpr.Expression);
                    _output.Append(")");
                }
                break;
            case AsyncExpression asyncExpr:
                if (asyncExpr.Expression is FunctionCallExpression asyncCall)
                {
                    _output.Append("MaldaLang.Interpreter.RuntimeValue.Task(");
                    _transpileCallAsTask = true;
                    TranspileFunctionCall(asyncCall);
                    _transpileCallAsTask = false;
                    _output.Append(")");
                }
                else
                {
                    _output.Append("MaldaLang.Interpreter.RuntimeValue.Task(System.Threading.Tasks.Task.FromResult(RuntimeHelpers.ToRuntimeValue(");
                    TranspileExpression(asyncExpr.Expression);
                    _output.Append(")))");
                }
                break;
            default:
                throw new NotSupportedException($"Expression type {expr.GetType().Name} is not supported in transpiler");
        }
    }

    private static readonly HashSet<string> ArrayPipelineMethods = new(StringComparer.Ordinal)
    {
        "append", "pop", "shift", "concat", "except", "popOrNull", "shiftOrNull", "get", "at",
        "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
        "sort", "reverse", "slice", "indexOf", "includes", "join", "sum", "average", "min", "max"
    };

    private void TranspilePipe(PipeExpression pipe)
    {
        var left = pipe.Left;
        var right = pipe.Right;

        switch (right)
        {
            case MemberAccessExpression member:
            {
                var memberCall = new FunctionCallExpression(member, [], pipe.Line, pipe.Column);
                TranspileFunctionCall(memberCall);
                return;
            }

            // A user binding named like a built-in: `x |> f(a)` is the plain call f(x, a).
            // A local value binding named like a top-level function: also the plain call.
            case FunctionCallExpression { Callee: IdentifierExpression { ShadowsBuiltIn: true } or IdentifierExpression { ResolvesToLocalValue: true } } call:
                var shadowed = (IdentifierExpression)call.Callee;
                TranspileFunctionCall(new FunctionCallExpression(shadowed, [left, ..call.Arguments], pipe.Line, pipe.Column));
                return;

            case IdentifierExpression { ShadowsBuiltIn: true } or IdentifierExpression { ResolvesToLocalValue: true }:
                TranspileFunctionCall(new FunctionCallExpression((IdentifierExpression)right, [left], pipe.Line, pipe.Column));
                return;

            case FunctionCallExpression call when call.Callee is IdentifierExpression id:
                if (ArrayPipelineMethods.Contains(id.Name))
                {
                    var memberCall = new FunctionCallExpression(
                        new MemberAccessExpression(left, id.Name, line: pipe.Line, column: pipe.Column),
                        call.Arguments,
                        pipe.Line,
                        pipe.Column);
                    TranspileFunctionCall(memberCall);
                    return;
                }

                TranspilePipedIdentifierCall(id.Name, left, call.Arguments, pipe.Line, pipe.Column);
                return;

            case FunctionCallExpression call:
            {
                var args = new List<Expression> { left };
                args.AddRange(call.Arguments);
                var pipedCall = new FunctionCallExpression(call.Callee, args, pipe.Line, pipe.Column);
                TranspileFunctionCall(pipedCall);
                return;
            }

            case IdentifierExpression id:
                TranspilePipedIdentifierCall(id.Name, left, [], pipe.Line, pipe.Column);
                return;

            case LambdaExpression lambda:
                if (_canAwait)
                    _output.Append("await RuntimeHelpers.CallFunction(");
                else
                    _output.Append("RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
                TranspileLambda(lambda);
                _output.Append(", ");
                TranspileExpression(left);
                _output.Append(")");
                if (!_canAwait)
                    _output.Append(")");
                return;

            default:
                throw new NotSupportedException(
                    $"Right side of |> must be a function call, identifier, or lambda (got {right.GetType().Name}).");
        }
    }

    private void TranspilePipedIdentifierCall(string name, Expression left, List<Expression> tailArgs, int line, int column)
    {
        if (_promptNames.Contains(name))
        {
            _output.Append(EscapeIdentifier(name));
            _output.Append("(");
            TranspileExpression(left);
            for (int i = 0; i < tailArgs.Count; i++)
            {
                _output.Append(", ");
                TranspileExpression(tailArgs[i]);
            }
            _output.Append(")");
            return;
        }

        if (IsBuiltInFunction(name))
        {
            var args = new List<Expression> { left };
            args.AddRange(tailArgs);
            TranspileBuiltInFunction(name, args);
            return;
        }

        if (IsKnownUserFunction(name))
        {
            if (_canAwait)
                _output.Append("await ");
            else
                _output.Append("RuntimeHelpers.BlockOn(");
            _output.Append(EscapeIdentifier(name));
            _output.Append("(");
            TranspileExpression(left);
            for (int i = 0; i < tailArgs.Count; i++)
            {
                _output.Append(", ");
                TranspileExpression(tailArgs[i]);
            }
            _output.Append(")");
            if (!_canAwait)
                _output.Append(")");
            return;
        }

        if (_canAwait)
            _output.Append("await RuntimeHelpers.CallFunction(");
        else
            _output.Append("RuntimeHelpers.BlockOn(RuntimeHelpers.CallFunction(");
        _output.Append(EscapeIdentifier(name));
        _output.Append(", ");
        TranspileExpression(left);
        _output.Append(")");
        if (!_canAwait)
            _output.Append(")");
    }

    private void TranspileListComprehension(ListComprehensionExpression comp)
    {
        _output.Append("(new System.Func<List<object>>(() => { var __list = new List<object>(); foreach (object ");
        _output.Append(EscapeIdentifier(comp.Variable));
        _output.Append(" in RuntimeHelpers.GetArray(");
        TranspileExpression(comp.Iterable);
        _output.Append(")) {");

        if (comp.Filter != null)
        {
            _output.Append(" if (!RuntimeHelpers.CoerceToBool(");
            TranspileExpression(comp.Filter);
            _output.Append(")) continue;");
        }

        _output.Append(" __list.Add(");
        TranspileExpression(comp.Element);
        _output.Append("); } return __list; }))()");
    }

    private void TranspileDictComprehension(DictComprehensionExpression comp)
    {
        _output.Append("(new System.Func<System.Collections.Generic.Dictionary<string, object?>>(() => { var __dict = new System.Collections.Generic.Dictionary<string, object?>(); foreach (object ");
        _output.Append(EscapeIdentifier(comp.Variable));
        _output.Append(" in RuntimeHelpers.GetArray(");
        TranspileExpression(comp.Iterable);
        _output.Append(")) {");

        if (comp.Filter != null)
        {
            _output.Append(" if (!RuntimeHelpers.CoerceToBool(");
            TranspileExpression(comp.Filter);
            _output.Append(")) continue;");
        }

        _output.Append(" __dict[RuntimeHelpers.CoerceToString(");
        TranspileExpression(comp.Key);
        _output.Append(")] = ");
        TranspileExpression(comp.Value);
        _output.Append("; } return __dict; }))()");
    }

    private void TranspileLiteral(LiteralExpression literal)
    {
        if (literal.Value == null)
        {
            _output.Append("null");
        }
        else if (literal.Value is bool b)
        {
            _output.Append(b ? "true" : "false");
        }
        else if (literal.Value is string s)
        {
            _output.Append("\"");
            _output.Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r"));
            _output.Append("\"");
        }
        else if (literal.Value is int || literal.Value is long)
        {
            _output.Append(literal.Value);
        }
        else if (literal.Value is double d)
        {
            // StringBuilder.Append(double) uses the current culture; Italian locales emit "0,5"
            // which is invalid C# (CS1001). Always use invariant for source emission.
            _output.Append(FormatInvariantCSharpNumber(d));
        }
        else if (literal.Value is float f)
        {
            _output.Append(FormatInvariantCSharpNumber(f));
        }
        else if (literal.Value is decimal m)
        {
            _output.Append(FormatInvariantCSharpNumber(m));
        }
        else
        {
            _output.Append(Convert.ToString(literal.Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static string FormatInvariantCSharpNumber(double value)
    {
        var text = value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        // Keep a fractional form so boxed assignments stay doubles (1.0 -> "1.0", not "1").
        if (!text.Contains('.') && !text.Contains('e') && !text.Contains('E') &&
            !double.IsNaN(value) && !double.IsInfinity(value))
        {
            text += ".0";
        }
        return text;
    }

    private static string FormatInvariantCSharpNumber(float value)
        => FormatInvariantCSharpNumber((double)value);

    private static string FormatInvariantCSharpNumber(decimal value)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!text.Contains('.') && !text.Contains('e') && !text.Contains('E'))
        {
            text += ".0";
        }
        return text;
    }

    private void TranspileBinary(BinaryExpression binary)
    {
        var numericType = ResolveBinaryType(binary);
        string? typedHelper = numericType switch
        {
            // Checked int arithmetic and the interpreter's division-by-zero errors.
            TranspiledClrType.Int => binary.Operator switch
            {
                TokenType.Plus => "CheckedIntAdd",
                TokenType.Minus => "CheckedIntSubtract",
                TokenType.Multiply => "CheckedIntMultiply",
                TokenType.Modulo => "IntModulo",
                _ => null
            },
            TranspiledClrType.Double => binary.Operator switch
            {
                TokenType.Divide => "DivideDouble",
                TokenType.Modulo => "ModuloDouble",
                _ => null
            },
            _ => null
        };
        if (typedHelper != null)
        {
            _output.Append("RuntimeHelpers.");
            _output.Append(typedHelper);
            _output.Append("(");
            TranspileExpression(binary.Left);
            _output.Append(", ");
            TranspileExpression(binary.Right);
            _output.Append(")");
            return;
        }

        if (numericType == TranspiledClrType.Double || IsRawNumericComparison(binary))
        {
            _output.Append("(");
            TranspileExpression(binary.Left);
            _output.Append(" ");
            _output.Append(GetOperatorString(binary.Operator));
            _output.Append(" ");
            TranspileExpression(binary.Right);
            _output.Append(")");
            return;
        }

        string? helper = binary.Operator switch
        {
            TokenType.Plus => "OperatorAdd",
            TokenType.Minus => "OperatorSubtract",
            TokenType.Multiply => "OperatorMultiply",
            TokenType.Divide => "OperatorDivide",
            TokenType.Modulo => "OperatorModulo",
            TokenType.Equal => "OperatorEqual",
            TokenType.NotEqual => "OperatorNotEqual",
            TokenType.LessThan => "OperatorLessThan",
            TokenType.LessThanOrEqual => "OperatorLessThanOrEqual",
            TokenType.GreaterThan => "OperatorGreaterThan",
            TokenType.GreaterThanOrEqual => "OperatorGreaterThanOrEqual",
            _ => null
        };

        if (helper != null)
        {
            _output.Append("RuntimeHelpers.");
            _output.Append(helper);
            _output.Append("(");
            TranspileExpression(binary.Left);
            _output.Append(", ");
            TranspileExpression(binary.Right);
            _output.Append(")");
            return;
        }

        // Logical && / || must coerce operands: many MALDA values are `object` (e.g. dict members),
        // while comparisons use RuntimeHelpers and yield bool — raw C# &&/|| then mix types (CS0019).
        if (binary.Operator == TokenType.And)
        {
            _output.Append("(");
            EmitCondition(binary.Left);
            _output.Append(" && ");
            EmitCondition(binary.Right);
            _output.Append(")");
            return;
        }

        if (binary.Operator == TokenType.Or)
        {
            _output.Append("(");
            EmitCondition(binary.Left);
            _output.Append(" || ");
            EmitCondition(binary.Right);
            _output.Append(")");
            return;
        }

        if (binary.Operator == TokenType.NullCoalesce)
        {
            _output.Append("RuntimeHelpers.NullCoalesce(");
            TranspileExpression(binary.Left);
            _output.Append(", () => ");
            TranspileExpression(binary.Right);
            _output.Append(")");
            return;
        }

        _output.Append("(");
        TranspileExpression(binary.Left);
        _output.Append(" ");
        _output.Append(GetOperatorString(binary.Operator));
        _output.Append(" ");
        TranspileExpression(binary.Right);
        _output.Append(")");
    }

    /// <summary>
    /// Comparisons between typed numbers compile to plain C# operators: ordering of any int/double
    /// mix matches the helpers (both compare as doubles). Equality stays raw only between ints,
    /// because the helper compares boxed values (<c>1 == 1.0</c> is false, NaN equals NaN).
    /// </summary>
    private bool IsRawNumericComparison(BinaryExpression binary)
    {
        if (binary.Operator is not (TokenType.LessThan or TokenType.LessThanOrEqual or
            TokenType.GreaterThan or TokenType.GreaterThanOrEqual or TokenType.Equal or TokenType.NotEqual))
        {
            return false;
        }

        var left = ResolveNumericOperandKind(binary.Left);
        var right = ResolveNumericOperandKind(binary.Right);
        if (left == TranspiledClrType.Object || right == TranspiledClrType.Object)
            return false;

        // At least one side must be a typed variable/expression, not two constants.
        if (binary.Left is LiteralExpression && binary.Right is LiteralExpression)
            return false;

        if (binary.Operator is TokenType.Equal or TokenType.NotEqual)
            return left == TranspiledClrType.Int && right == TranspiledClrType.Int;

        return true;
    }

    /// <summary>
    /// Emits <paramref name="condition"/> as a C# bool. Comparisons, logical operators, <c>!</c> and
    /// boolean literals already produce one, so they skip the boxing CoerceToBool call.
    /// </summary>
    private void EmitCondition(Expression condition)
    {
        var alreadyBool = condition switch
        {
            LiteralExpression { Value: bool } => true,
            UnaryExpression { Operator: TokenType.Not } => true,
            BinaryExpression b => b.Operator is TokenType.Equal or TokenType.NotEqual or
                TokenType.LessThan or TokenType.LessThanOrEqual or TokenType.GreaterThan or
                TokenType.GreaterThanOrEqual or TokenType.And or TokenType.Or,
            _ => false
        };
        if (alreadyBool)
        {
            TranspileExpression(condition);
            return;
        }

        _output.Append("RuntimeHelpers.CoerceToBool(");
        TranspileExpression(condition);
        _output.Append(")");
    }

    private void TranspileUnary(UnaryExpression unary)
    {
        _output.Append("(");
        if (unary.Operator == TokenType.Not)
        {
            // For logical NOT, coerce to bool first, but don't double-wrap
            // The result is already a boolean, so we don't need CoerceToBool again
            _output.Append("!");
            EmitCondition(unary.Right);
        }
        else if (unary.Operator == TokenType.Minus &&
                 ResolveExpressionType(unary.Right) == TranspiledClrType.Int)
        {
            _output.Append("RuntimeHelpers.CheckedIntNegate(");
            TranspileExpression(unary.Right);
            _output.Append(")");
        }
        else if ((unary.Operator == TokenType.Increment || unary.Operator == TokenType.Decrement) &&
                 unary.Right is IdentifierExpression &&
                 ResolveExpressionType(unary.Right) == TranspiledClrType.Int)
        {
            var intHelper = unary.Operator == TokenType.Increment ? "CheckedIntIncrement" : "CheckedIntDecrement";
            TranspileExpression(unary.Right);
            _output.Append(" = RuntimeHelpers.").Append(intHelper).Append("(");
            TranspileExpression(unary.Right);
            _output.Append(")");
        }
        else if (unary.Operator == TokenType.Minus &&
                 unary.Right is not LiteralExpression { Value: int } &&
                 ResolveExpressionType(unary.Right) == TranspiledClrType.Double)
        {
            // ResolveExpressionType reports -x as double here, so emit a double (not the
            // object OperatorNegate returns): math.round(-2.5) otherwise fails CS1503.
            _output.Append("-");
            TranspileExpression(unary.Right);
        }
        else if (unary.Operator == TokenType.Minus)
        {
            _output.Append("RuntimeHelpers.OperatorNegate(");
            TranspileExpression(unary.Right);
            _output.Append(")");
        }
        else if (unary.Operator == TokenType.Increment || unary.Operator == TokenType.Decrement)
        {
            // Prefix ++x / --x: assign checked result and use as expression value
            var helper = unary.Operator == TokenType.Increment ? "CheckedIntIncrement" : "CheckedIntDecrement";
            TranspileExpression(unary.Right);
            _output.Append(" = RuntimeHelpers.").Append(helper).Append("((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(unary.Right);
            _output.Append("))");
        }
        else
        {
            _output.Append(GetUnaryOperatorString(unary.Operator));
            TranspileExpression(unary.Right);
        }
        _output.Append(")");
    }

    
    private void TranspilePostfix(PostfixExpression postfix)
    {
        // Static members cannot be the left-hand side of a plain C# assignment
        // (`typeof(Counter).count = ...` is invalid), so expand them through the
        // static helpers explicitly.
        if (postfix.Left is MemberAccessExpression staticMember && IsClassReference(staticMember.Object))
        {
            var op = postfix.Operator == TokenType.Increment
                ? "CheckedIntIncrement"
                : "CheckedIntDecrement";
            _output.Append("RuntimeHelpers.SetStaticMember(");
            TranspileReceiver(staticMember.Object);
            _output.Append(", \"");
            _output.Append(staticMember.Member);
            _output.Append($"\", RuntimeHelpers.{op}((int)RuntimeHelpers.CoerceToInt(RuntimeHelpers.GetStaticMember(");
            TranspileReceiver(staticMember.Object);
            _output.Append(", \"");
            _output.Append(staticMember.Member);
            _output.Append("\"))))");
            return;
        }

        // Instance members are reached through RuntimeHelpers, so `obj.field++` cannot be
        // emitted as a plain C# assignment either; expand it into get/operate/set.
        if (postfix.Left is MemberAccessExpression instanceMember)
        {
            var op = postfix.Operator == TokenType.Increment
                ? "CheckedIntIncrement"
                : "CheckedIntDecrement";
            _output.Append("RuntimeHelpers.SetObjectMember(");
            TranspileExpression(instanceMember.Object);
            _output.Append(", \"");
            _output.Append(instanceMember.Member);
            _output.Append($"\", RuntimeHelpers.{op}((int)RuntimeHelpers.CoerceToInt(RuntimeHelpers.GetObjectMember(");
            TranspileExpression(instanceMember.Object);
            _output.Append(", \"");
            _output.Append(instanceMember.Member);
            _output.Append("\"))))");
            return;
        }

        if (postfix.Left is IdentifierExpression && ResolveExpressionType(postfix.Left) == TranspiledClrType.Int &&
            (postfix.Operator == TokenType.Increment || postfix.Operator == TokenType.Decrement))
        {
            var intHelper = postfix.Operator == TokenType.Increment ? "CheckedIntIncrement" : "CheckedIntDecrement";
            TranspileExpression(postfix.Left);
            _output.Append(" = RuntimeHelpers.").Append(intHelper).Append("(");
            TranspileExpression(postfix.Left);
            _output.Append(")");
            return;
        }

        // For postfix increment/decrement on object types, we need to convert to assignment
        // i++ becomes: i = RuntimeHelpers.CheckedIntIncrement((int)RuntimeHelpers.CoerceToInt(i))
        // i-- becomes: i = RuntimeHelpers.CheckedIntDecrement((int)RuntimeHelpers.CoerceToInt(i))
        if (postfix.Operator == TokenType.Increment)
        {
            TranspileExpression(postfix.Left);
            _output.Append(" = RuntimeHelpers.CheckedIntIncrement((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(postfix.Left);
            _output.Append("))");
        }
        else if (postfix.Operator == TokenType.Decrement)
        {
            TranspileExpression(postfix.Left);
            _output.Append(" = RuntimeHelpers.CheckedIntDecrement((int)RuntimeHelpers.CoerceToInt(");
            TranspileExpression(postfix.Left);
            _output.Append("))");
        }
        else
        {
            TranspileExpression(postfix.Left);
            _output.Append(GetUnaryOperatorString(postfix.Operator));
        }
    }

    private void TranspileMemberAccess(MemberAccessExpression member)
    {
        // Special handling for Math.* members (constants)
        if (member.Object is IdentifierExpression mathIdExpr && mathIdExpr.Name == "Math")
        {
            var mathMember = member.Member;
            if (mathMember == "PI")
            {
                _output.Append("System.Math.PI");
                return;
            }
            if (mathMember == "E")
            {
                _output.Append("System.Math.E");
                return;
            }
            if (mathMember == "TAU")
            {
                _output.Append("(2 * System.Math.PI)");
                return;
            }
            if (mathMember == "INF")
            {
                _output.Append("double.PositiveInfinity");
                return;
            }
            if (mathMember == "NaN")
            {
                _output.Append("double.NaN");
                return;
            }
            // Unknown Math member - fall through to generic handling
        }

        // Check if this is an array method or property
        var memberName = member.Member;
        if (memberName == "length")
        {
            if (ResolveExpressionType(member.Object) == TranspiledClrType.DoubleArray)
            {
                _output.Append("RuntimeHelpers.CoerceToDoubleList(");
                TranspileExpression(member.Object);
                _output.Append(").Count");
            }
            else
            {
                _output.Append("RuntimeHelpers.GetArray(");
                TranspileExpression(member.Object);
                _output.Append(").Count");
            }
            return;
        }
        else if (memberName == "append" || memberName == "pop" || memberName == "shift" || memberName == "concat" || memberName == "except")
        {
            // This will be handled when the method is called
            // Note: These are built-in array methods, but we don't escape them because
            // they're accessed through ObjectInstance which uses string-based reflection
            TranspileExpression(member.Object);
            _output.Append(".");
            _output.Append(member.Member);
            return;
        }

        if (IsDirectThisField(member.Object, memberName))
        {
            _output.Append("this.");
            _output.Append(memberName);
            return;
        }

        var memberHelper = member.IsNullConditional ? "RuntimeHelpers.GetObjectMemberNullSafe" : "RuntimeHelpers.GetObjectMember";
        if (IsClassReference(member.Object))
            memberHelper = "RuntimeHelpers.GetStaticMember";
        _output.Append(memberHelper);
        _output.Append("(");
        TranspileReceiver(member.Object);
        _output.Append(", \"");
        _output.Append(memberName);
        _output.Append("\", ");
        EmitAccessingClassArg();
        _output.Append(")");
    }

    private void TranspileArrayLiteral(ArrayLiteralExpression array)
    {
        _output.Append("new List<object> { ");
        for (int i = 0; i < array.Elements.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            TranspileExpression(array.Elements[i]);
        }
        _output.Append(" }");
    }

    private void TranspileDictionaryLiteral(DictionaryLiteralExpression dict)
    {
        _output.Append("new System.Collections.Generic.Dictionary<string, object?> { ");
        for (int i = 0; i < dict.Entries.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var (key, value) = dict.Entries[i];
            _output.Append("{ ");
            // Keys must be coerced to strings at runtime to match interpreter semantics
            _output.Append("RuntimeHelpers.CoerceToString(");
            TranspileExpression(key);
            _output.Append("), ");
            if (ShouldEmitTranspiledFunctionDelegate(value))
            {
                _output.Append("(System.Func<object, System.Threading.Tasks.Task<object>>)");
                TranspileExpression(value);
            }
            else
            {
                TranspileExpression(value);
            }
            _output.Append(" }");
        }
        _output.Append(" }");
    }

    private void TranspileGraphLiteral(GraphLiteralExpression graph)
    {
        // Create graph instance and initialize it using a lambda to handle initialization
        // This matches the interpreter's EvaluateGraphLiteralAsync behavior
        _output.Append("(new System.Func<object>(() => { ");
        _output.Append($"var __graph = new MaldaLang.Interpreter.GraphInstance({(graph.IsDirected ? "true" : "false")}); ");
        
        // Process nodes if provided
        if (graph.NodesExpression != null)
        {
            _output.Append("var __nodesVal = RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(graph.NodesExpression);
            _output.Append("); ");
            _output.Append("if (__nodesVal.Type == MaldaLang.Interpreter.ValueType.Array) { ");
            _output.Append("var __nodes = __nodesVal.AsArray(); ");
            _output.Append("foreach (var __node in __nodes) { ");
            _output.Append("var __nodeId = __node.Type == MaldaLang.Interpreter.ValueType.String ? __node.AsString() : __node.ToString(); ");
            _output.Append("__graph.CallMethod(\"addNode\", new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__nodeId) }, null); ");
            _output.Append("} } ");
        }
        
        // Process edges if provided
        if (graph.EdgesExpression != null)
        {
            _output.Append("var __edgesVal = RuntimeHelpers.ToRuntimeValue(");
            TranspileExpression(graph.EdgesExpression);
            _output.Append("); ");
            _output.Append("if (__edgesVal.Type == MaldaLang.Interpreter.ValueType.Array) { ");
            _output.Append("var __edges = __edgesVal.AsArray(); ");
            _output.Append("foreach (var __edgeVal in __edges) { ");
            _output.Append("if (__edgeVal.Type == MaldaLang.Interpreter.ValueType.Object) { ");
            _output.Append("var __edgeObj = __edgeVal.AsObject(); ");
            _output.Append("var __fromVal = __edgeObj.Get(\"from\", null); ");
            _output.Append("var __toVal = __edgeObj.Get(\"to\", null); ");
            _output.Append("var __from = __fromVal.Type == MaldaLang.Interpreter.ValueType.String ? __fromVal.AsString() : \"\"; ");
            _output.Append("var __to = __toVal.Type == MaldaLang.Interpreter.ValueType.String ? __toVal.AsString() : \"\"; ");
            _output.Append("var __weightVal = MaldaLang.Interpreter.RuntimeValue.Null(); ");
            _output.Append("try { var __w = __edgeObj.Get(\"weight\", null); if (__w.Type != MaldaLang.Interpreter.ValueType.Null) __weightVal = __w; } catch { } ");
            _output.Append("var __propsVal = MaldaLang.Interpreter.RuntimeValue.Null(); ");
            _output.Append("try { var __p = __edgeObj.Get(\"properties\", null); if (__p.Type == MaldaLang.Interpreter.ValueType.Object && __p.Value is MaldaLang.Interpreter.DictionaryInstance) __propsVal = __p; } catch { } ");
            _output.Append("__propsVal = MaldaLang.Interpreter.GraphLiteralEdges.MergeProperties(__edgeObj, __propsVal); ");
            _output.Append("var __args = new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(__from), MaldaLang.Interpreter.RuntimeValue.String(__to), __weightVal, __propsVal }; ");
            _output.Append("__graph.CallMethod(\"addEdge\", __args, null); ");
            _output.Append("} } } ");
        }
        
        _output.Append("return __graph; }))()");
    }

    private void TranspileObjectLiteral(ObjectLiteralExpression obj)
    {
        // Use a native dictionary so values can preserve arbitrary CLR objects.
        // Emit a direct initializer (instead of a non-async lambda wrapper) so await
        // expressions inside object values can compile in async contexts.
        _output.Append("new System.Collections.Generic.Dictionary<string, object?> { ");
        for (int i = 0; i < obj.Properties.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            var (key, value) = obj.Properties[i];
            _output.Append("{ ");
            if (key is LiteralExpression keyLiteral && keyLiteral.Value is string keyStr)
            {
                _output.Append($"\"{keyStr}\"");
            }
            else
            {
                _output.Append("RuntimeHelpers.CoerceToString(");
                TranspileExpression(key);
                _output.Append(")");
            }
            _output.Append(", ");
            if (ShouldEmitTranspiledFunctionDelegate(value))
            {
                _output.Append("(System.Func<object, System.Threading.Tasks.Task<object>>)");
                TranspileExpression(value);
            }
            else
            {
                TranspileExpression(value);
            }
            _output.Append(" }");
        }
        _output.Append(" }");
    }

    private bool ShouldEmitTranspiledFunctionDelegate(Expression value)
    {
        if (value is not IdentifierExpression id || !IsKnownUserFunction(id))
            return false;

        foreach (var frame in _localFunctionStack)
        {
            if (!frame.ContainsKey(id.Name))
                continue;
            return frame.TryGetValue(id.Name, out var localTypes) && localTypes.Count == 1;
        }

        return _functionNames.Contains(id.Name);
    }

    /// <summary>
    /// A unary user or nested function is a method group. Pass it as a delegate so the
    /// callee can store it and call it later (interpreter passes the function value).
    /// </summary>
    private void EmitCallableArgument(Expression argument, TranspiledClrType paramType)
    {
        if (paramType == TranspiledClrType.Object
            && argument is IdentifierExpression id
            && IsKnownUserFunction(id)
            && TryGetKnownFunctionParameterTypes(id.Name, out var paramTypes)
            && paramTypes.Count == 1)
        {
            _output.Append("(System.Func<object, System.Threading.Tasks.Task<object>>)");
            _output.Append(EscapeIdentifier(id.Name));
            return;
        }

        _output.Append(GetCoercionExpressionPrefix(paramType));
        TranspileExpression(argument);
        _output.Append(GetCoercionExpressionSuffix(paramType));
    }

    private void TranspileArrayAccess(ArrayAccessExpression arrayAccess)
    {
        if (ResolveExpressionType(arrayAccess.Array) == TranspiledClrType.DoubleArray)
        {
            _output.Append("RuntimeHelpers.GetIndexedDouble(");
            _output.Append("RuntimeHelpers.CoerceToDoubleList(");
            TranspileExpression(arrayAccess.Array);
            _output.Append("), ");
            TranspileExpression(arrayAccess.Index);
            _output.Append(")");
            return;
        }
        var indexHelper = arrayAccess.IsNullConditional ? "RuntimeHelpers.GetIndexedNullSafe" : "RuntimeHelpers.GetIndexed";
        _output.Append(indexHelper);
        _output.Append("(");
        TranspileExpression(arrayAccess.Array);
        _output.Append(", ");
        TranspileExpression(arrayAccess.Index);
        _output.Append(")");
    }

    private void TranspileTernary(TernaryExpression ternary)
    {
        _output.Append("(");
        EmitCondition(ternary.Condition);
        _output.Append(" ? ");
        TranspileExpression(ternary.ThenBranch);
        _output.Append(" : ");
        TranspileExpression(ternary.ElseBranch);
        _output.Append(")");
    }

    private void TranspileInterpolatedString(InterpolatedStringExpression interpolated)
    {
        _output.Append("$\"");
        foreach (var segment in interpolated.Segments)
        {
            if (segment.IsExpression)
            {
                // Format through the runtime like print and +, so arrays, null and booleans
                // read [1, 2] / null / true instead of the CLR type name, "" and True.
                _output.Append("{RuntimeHelpers.CoerceToString(");
                TranspileExpression(segment.Expression!);
                _output.Append(")}");
            }
            else
            {
                // Match TranspileLiteral: lexer-decoded escapes (\n, \t, …) must be re-escaped
                // into the C# source. Forgetting this turns $"a\nb" into a broken multi-line
                // C# interpolated string while the interpreter still runs fine.
                var text = segment.Text ?? "";
                _output.Append(text
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\t", "\\t")
                    .Replace("\r", "\\r")
                    .Replace("{", "{{")
                    .Replace("}", "}}"));
            }
        }
        _output.Append("\"");
    }

    private void TranspileLambda(LambdaExpression lambda)
    {
        // We define the delegate as async. C# will handle the Task wrapping automatically.
        // Zero and one parameter share the one-argument shape (see below); N >= 2 parameters get
        // Func<object x N, Task<object>> so the body type-checks and CallFunctionN can invoke it.
        var delegateArity = System.Math.Max(1, lambda.Parameters.Count);
        _output.Append("new System.Func<");
        _output.Append(string.Concat(Enumerable.Repeat("object, ", delegateArity)));
        _output.Append("System.Threading.Tasks.Task<object>>(async (");

        // Parameters
        for (int i = 0; i < lambda.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(lambda.Parameters[i]));
        }

        // A zero-parameter lambda still gets the one-argument delegate shape every runtime
        // helper (CallFunction, ToRuntimeValue, ...) dispatches on; `async () => ...` would not
        // convert to Func<object, Task<object>> and fails with CS1593. The argument is ignored.
        if (lambda.Parameters.Count == 0)
            _output.Append("object __maldaNoArg");

        _output.Append(") => ");

        // Lambda bodies are emitted as async delegates, so function calls inside them
        // should transpile in await-capable mode instead of BlockOn.
        var previousCanAwaitInLambda = _canAwait;
        _canAwait = true;

        // The lambda is its own typed scope: its parameters are boxed objects, and a `var` it
        // declares is emitted with the int/double type ResolveVarDeclType infers, so later reads
        // and stores must resolve that same type. Without a scope (a lambda at top level, where
        // the stack is empty) the declaration registered nowhere: `int current = 0;` followed
        // by `current = RuntimeHelpers.OperatorAdd(current, 1);` failed with CS0266.
        PushTypedScope();
        foreach (var parameter in lambda.Parameters)
            RegisterTypedVariable(parameter, TranspiledClrType.Object);

        // Body handling
        if (lambda.BlockBody != null)
        {
            _output.Append("{");
            _output.AppendLine();
            _indentLevel++;
            PushLocalFunctionScope();

            for (int i = 0; i < lambda.BlockBody.Statements.Count; i++)
            {
                var stmt = lambda.BlockBody.Statements[i];
                WriteIndent();

                if (stmt is ReturnStatement returnStmt)
                {
                    _output.Append("return ");
                    if (returnStmt.Value != null)
                    {
                        TranspileExpression(returnStmt.Value);
                    }
                    else
                    {
                        _output.Append("null");
                    }
                    _output.AppendLine(";");
                }
                else
                {
                    var isLast = (i == lambda.BlockBody.Statements.Count - 1);
                    if (isLast && stmt is ExpressionStatement voidStmt && ExpressionTranspilesToVoid(voidStmt.Expression))
                    {
                        // `x => { io.print(x); }`: a void C# call cannot be the return value.
                        TranspileExpression(voidStmt.Expression);
                        _output.AppendLine(";");
                        WriteIndent();
                        _output.AppendLine("return null;");
                    }
                    else if (isLast && stmt is ExpressionStatement exprStmt)
                    {
                        // Last expression wins: implicit return for lambda block bodies
                        _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(exprStmt.Expression);
                        _output.AppendLine(");");
                    }
                    else
                    {
                        TranspileStatement(stmt);
                        // Ensure newlines for non-block statements
                        if (!(stmt is BlockStatement || stmt is IfStatement || stmt is WhileStatement || stmt is ForStatement))
                        {
                            _output.AppendLine();
                        }
                    }
                }
            }

            // The delegate returns Task<object>, so a body that does not end in a return or a
            // trailing expression (e.g. `x => { print(x); }` in forEach) needs an explicit null
            // result; without it C# rejects the lambda with CS1643.
            var lastStmt = lambda.BlockBody.Statements.Count > 0 ? lambda.BlockBody.Statements[^1] : null;
            if (lastStmt is not ReturnStatement and not ExpressionStatement)
            {
                WriteIndent();
                _output.AppendLine("return null;");
            }

            PopLocalFunctionScope();
            _indentLevel--;
            WriteIndent();
            _output.Append("})");
        }
        else if (lambda.ExpressionBody != null && ExpressionTranspilesToVoid(lambda.ExpressionBody))
        {
            // `x => io.print(x)`: the body is a void C# call, so it cannot be the result.
            _output.Append("{ ");
            TranspileExpression(lambda.ExpressionBody);
            _output.Append("; return null; })");
        }
        else if (lambda.ExpressionBody != null)
        {
            // For expression-bodied lambdas (no braces), C# implicitly returns the value.
            TranspileExpression(lambda.ExpressionBody);
            _output.Append(")");
        }

        PopTypedScope();
        _canAwait = previousCanAwaitInLambda;
    }

    private string GetOperatorString(TokenType op)
    {
        return op switch
        {
            TokenType.Plus => "+",
            TokenType.Minus => "-",
            TokenType.Multiply => "*",
            TokenType.Divide => "/",
            TokenType.Modulo => "%",
            TokenType.Equal => "==",
            TokenType.NotEqual => "!=",
            TokenType.LessThan => "<",
            TokenType.GreaterThan => ">",
            TokenType.LessThanOrEqual => "<=",
            TokenType.GreaterThanOrEqual => ">=",
            TokenType.And => "&&",
            TokenType.Or => "||",
            TokenType.PlusAssign => "+=",
            TokenType.MinusAssign => "-=",
            TokenType.MultiplyAssign => "*=",
            TokenType.DivideAssign => "/=",
            _ => throw new NotSupportedException($"Operator {op} not supported")
        };
    }

    /// <summary>
    /// Maps an arithmetic operator (or its compound-assignment form) to the
    /// <c>RuntimeHelpers.Operator*</c> function used when operands are boxed as
    /// <see cref="object"/>. Returns <c>null</c> for operators without such a helper.
    /// </summary>
    private static string? GetBoxedBinaryOperatorHelper(TokenType op) => op switch
    {
        TokenType.Plus or TokenType.PlusAssign => "OperatorAdd",
        TokenType.Minus or TokenType.MinusAssign => "OperatorSubtract",
        TokenType.Multiply or TokenType.MultiplyAssign => "OperatorMultiply",
        TokenType.Divide or TokenType.DivideAssign => "OperatorDivide",
        TokenType.Modulo => "OperatorModulo",
        _ => null
    };

    private string GetUnaryOperatorString(TokenType op)
    {
        return op switch
        {
            TokenType.Minus => "-",
            TokenType.Not => "!",
            TokenType.Increment => "++",
            TokenType.Decrement => "--",
            _ => throw new NotSupportedException($"Unary operator {op} not supported")
        };
    }

    private string EscapeIdentifier(string name)
    {
        // C# reserved keywords and base types that need to be escaped
        var csharpKeywords = new HashSet<string>
        {
            // C# keywords
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while",
            // Contextual keywords
            "add", "alias", "and", "ascending", "async", "await", "by", "descending", "dynamic",
            "equals", "from", "get", "global", "group", "init", "into", "join", "let", "nameof",
            "not", "notnull", "on", "or", "orderby", "partial", "record", "remove", "select",
            "set", "unmanaged", "value", "var", "when", "where", "with", "yield"
        };

        if (csharpKeywords.Contains(name))
        {
            return "@" + name;
        }

        return name;
    }
}
