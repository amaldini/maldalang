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
    private string TranspileLambdaExpression(LambdaExpression lambda)
    {
        var parameters = string.Join(", ", lambda.Parameters.Select(EscapeIdentifier));
        return InLocalScope(lambda.Parameters, () =>
        {
            if (lambda.ExpressionBody != null)
            {
                return $"(({parameters}) => ({TranspileExpression(lambda.ExpressionBody)}))";
            }

            if (lambda.BlockBody == null)
            {
                throw new NotSupportedException("Lambda expression requires a body.");
            }

            var builder = new StringBuilder();
            builder.Append("((");
            builder.Append(parameters);
            builder.Append(") => {");
            var inlineTranspiler = new JsTranspiler();
            inlineTranspiler.CopyTranspileStateFrom(this);
            foreach (var statement in lambda.BlockBody.Statements)
            {
                builder.Append(inlineTranspiler.TranspileStatementInline(statement));
            }
            builder.Append(" return null; })");
            return builder.ToString();
        });
    }

    private static readonly HashSet<string> ArrayPipelineMethods = new(StringComparer.Ordinal)
    {
        "append", "pop", "shift", "concat", "except", "popOrNull", "shiftOrNull", "get", "at",
        "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
        "sort", "reverse", "slice", "indexOf", "includes", "join", "sum", "average", "min", "max"
    };

    private static readonly HashSet<string> DictionaryMethods = new(StringComparer.Ordinal)
    {
        "keys", "values", "containsKey", "set", "remove", "entries"
    };

    private string TranspilePipeExpression(PipeExpression pipe)
    {
        var left = pipe.Left;
        var right = pipe.Right;

        switch (right)
        {
            // A user binding named like a built-in: `x |> f(a)` is the plain call f(x, a).
            case FunctionCallExpression { Callee: IdentifierExpression { ShadowsBuiltIn: true } shadowed } call:
                return TranspileFunctionCall(new FunctionCallExpression(shadowed, [left, ..call.Arguments], pipe.Line, pipe.Column));
            case IdentifierExpression { ShadowsBuiltIn: true } shadowed:
                return TranspileFunctionCall(new FunctionCallExpression(shadowed, [left], pipe.Line, pipe.Column));
            case FunctionCallExpression call when call.Callee is IdentifierExpression identifier:
                if (ArrayPipelineMethods.Contains(identifier.Name))
                {
                    var args = string.Join(", ", call.Arguments.Select(TranspileExpression));
                    if (args.Length == 0)
                    {
                        return $"mlRuntime.callArrayMethod({TranspileExpression(left)}, {TranspileLiteral(identifier.Name)})";
                    }

                    return $"mlRuntime.callArrayMethod({TranspileExpression(left)}, {TranspileLiteral(identifier.Name)}, [{args}])";
                }

                return TranspilePipedIdentifierCall(identifier.Name, left, call.Arguments);
            case FunctionCallExpression call:
            {
                var args = new List<Expression> { left };
                args.AddRange(call.Arguments);
                return TranspileFunctionCall(new FunctionCallExpression(call.Callee, args, pipe.Line, pipe.Column));
            }
            case IdentifierExpression identifier:
                return TranspilePipedIdentifierCall(identifier.Name, left, []);
            case LambdaExpression lambda:
                return $"({TranspileLambdaExpression(lambda)})({TranspileExpression(left)})";
            default:
                throw new NotSupportedException(
                    $"Right side of |> must be a function call, identifier, or lambda (got {right.GetType().Name}).");
        }
    }

    private string TranspilePipedIdentifierCall(string name, Expression left, List<Expression> tailArgs)
    {
        if (TryTranspileBuiltInCall(name, [left, ..tailArgs], out var builtInCall))
        {
            return builtInCall;
        }

        var args = string.Join(", ", new[] { TranspileExpression(left) }.Concat(tailArgs.Select(TranspileExpression)));
        return $"{EscapeIdentifier(name)}({args})";
    }

    private string TranspileListComprehensionExpression(ListComprehensionExpression comprehension)
    {
        var builder = new StringBuilder();
        builder.Append("(() => { const __list = []; for (const ");
        builder.Append(EscapeIdentifier(comprehension.Variable));
        builder.Append(" of mlRuntime.getArray(");
        builder.Append(TranspileExpression(comprehension.Iterable));
        builder.Append(")) {");
        if (comprehension.Filter != null)
        {
            builder.Append(" if (!mlRuntime.isTruthy(");
            builder.Append(TranspileExpression(comprehension.Filter));
            builder.Append(")) continue;");
        }

        builder.Append(" __list.push(");
        builder.Append(TranspileExpression(comprehension.Element));
        builder.Append("); } return __list; })()");
        return builder.ToString();
    }

    private string TranspileDictComprehensionExpression(DictComprehensionExpression comprehension)
    {
        var builder = new StringBuilder();
        builder.Append("(() => { const __dict = mlRuntime.markDict({}); for (const ");
        builder.Append(EscapeIdentifier(comprehension.Variable));
        builder.Append(" of mlRuntime.getArray(");
        builder.Append(TranspileExpression(comprehension.Iterable));
        builder.Append(")) {");
        if (comprehension.Filter != null)
        {
            builder.Append(" if (!mlRuntime.isTruthy(");
            builder.Append(TranspileExpression(comprehension.Filter));
            builder.Append(")) continue;");
        }

        builder.Append(" __dict[mlRuntime.coerceToString(");
        builder.Append(TranspileExpression(comprehension.Key));
        builder.Append(")] = ");
        builder.Append(TranspileExpression(comprehension.Value));
        builder.Append("; } return __dict; })()");
        return builder.ToString();
    }

    private bool ExpressionProducesPromise(Expression expression)
    {
        return expression switch
        {
            AwaitExpression => true,
            ReceiveExpression => true,
            AsyncExpression => true,
            FunctionCallExpression functionCall => FunctionCallProducesPromise(functionCall),
            BinaryExpression binary => ExpressionProducesPromise(binary.Left) || ExpressionProducesPromise(binary.Right),
            UnaryExpression unary => ExpressionProducesPromise(unary.Right),
            PostfixExpression postfix => ExpressionProducesPromise(postfix.Left),
            TernaryExpression ternary =>
                ExpressionProducesPromise(ternary.Condition) ||
                ExpressionProducesPromise(ternary.ThenBranch) ||
                ExpressionProducesPromise(ternary.ElseBranch),
            MatchExpression match =>
                ExpressionProducesPromise(match.Value) ||
                match.Cases.Any(c =>
                    (c.Guard != null && ExpressionProducesPromise(c.Guard)) ||
                    StatementRequiresAsync(c.Body)) ||
                (match.DefaultCase != null && StatementRequiresAsync(match.DefaultCase)),
            MemberAccessExpression member => ExpressionProducesPromise(member.Object),
            ArrayAccessExpression arrayAccess =>
                ExpressionProducesPromise(arrayAccess.Array) ||
                ExpressionProducesPromise(arrayAccess.Index),
            ArrayLiteralExpression arrayLiteral => arrayLiteral.Elements.Any(ExpressionProducesPromise),
            ObjectLiteralExpression objectLiteral =>
                objectLiteral.Properties.Any(p => ExpressionProducesPromise(p.Key) || ExpressionProducesPromise(p.Value)),
            DictionaryLiteralExpression dictionaryLiteral =>
                dictionaryLiteral.Entries.Any(p => ExpressionProducesPromise(p.Key) || ExpressionProducesPromise(p.Value)),
            SpawnExpression spawn => spawn.Arguments.Any(ExpressionProducesPromise),
            NewExpression newExpression => newExpression.Arguments.Any(ExpressionProducesPromise),
            PipeExpression pipe => ExpressionProducesPromise(pipe.Left) || ExpressionProducesPromise(pipe.Right),
            InterpolatedStringExpression interpolated => interpolated.Segments.Any(
                segment => segment.IsExpression && segment.Expression != null && ExpressionProducesPromise(segment.Expression)),
            ListComprehensionExpression list =>
                ExpressionProducesPromise(list.Element) ||
                ExpressionProducesPromise(list.Iterable) ||
                (list.Filter != null && ExpressionProducesPromise(list.Filter)),
            DictComprehensionExpression dict =>
                ExpressionProducesPromise(dict.Key) ||
                ExpressionProducesPromise(dict.Value) ||
                ExpressionProducesPromise(dict.Iterable) ||
                (dict.Filter != null && ExpressionProducesPromise(dict.Filter)),
            LambdaExpression lambda =>
                lambda.ExpressionBody != null
                    ? ExpressionProducesPromise(lambda.ExpressionBody)
                    : lambda.BlockBody?.Statements.Any(StatementRequiresAsync) ?? false,
            _ => false
        };
    }

    private bool FunctionCallProducesPromise(FunctionCallExpression functionCall)
    {
        if (functionCall.Callee is IdentifierExpression identifier)
        {
            if (AsyncBuiltInNames.Contains(identifier.Name))
            {
                return true;
            }

            if (_asyncFunctions.Contains(identifier.Name))
            {
                return true;
            }

            // A call to an async helper of the enclosing actor is awaited.
            if (_currentActor != null
                && !IsLocalName(identifier.Name)
                && _asyncActorMethods.Contains(identifier.Name)
                && TryFindActorMember(identifier.Name, out var actorMember)
                && actorMember.Type == MemberType.Method)
            {
                return true;
            }
        }

        return ExpressionProducesPromise(functionCall.Callee) ||
               functionCall.Arguments.Any(ExpressionProducesPromise);
    }

    private string TranspileInterpolatedString(InterpolatedStringExpression interpolated)
    {
        if (interpolated.Segments.Count == 0)
        {
            return "\"\"";
        }

        var parts = new List<string>(interpolated.Segments.Count);
        foreach (var segment in interpolated.Segments)
        {
            if (segment.IsExpression && segment.Expression != null)
            {
                parts.Add($"__maldaFormat({TranspileExpression(segment.Expression)})");
            }
            else
            {
                parts.Add(TranspileLiteral(segment.Text ?? string.Empty));
            }
        }

        return parts.Count == 1 ? parts[0] : $"({string.Join(" + ", parts)})";
    }

    private string TranspileTernaryExpression(TernaryExpression ternary)
    {
        var condition = TranspileCondition(ternary.Condition);
        var thenBranch = TranspileExpression(ternary.ThenBranch);
        var elseBranch = TranspileExpression(ternary.ElseBranch);
        return $"({condition} ? {thenBranch} : {elseBranch})";
    }

    private string TranspileCondition(Expression expression)
    {
        return Truthy(expression, TranspileExpression(expression));
    }

    /// <summary>
    /// Wraps <paramref name="transpiled"/> in <c>mlRuntime.isTruthy</c> unless the expression
    /// already yields a JS boolean (comparisons, <c>!</c>, <c>&amp;&amp;</c>/<c>||</c>, boolean
    /// literals), where the call is the identity and only costs time in hot loops.
    /// </summary>
    private static string Truthy(Expression expression, string transpiled)
    {
        return IsStaticallyBoolean(expression) ? transpiled : $"mlRuntime.isTruthy({transpiled})";
    }

    private static bool IsStaticallyBoolean(Expression expression)
    {
        return expression switch
        {
            LiteralExpression { Value: bool } => true,
            UnaryExpression { Operator: TokenType.Not } => true,
            BinaryExpression binary => binary.Operator is
                TokenType.Equal or TokenType.NotEqual or TokenType.And or TokenType.Or or
                TokenType.LessThan or TokenType.GreaterThan or
                TokenType.LessThanOrEqual or TokenType.GreaterThanOrEqual,
            _ => false
        };
    }

    /// <summary>
    /// Coerces an arithmetic/comparison operand to a number. <c>coerceToFloat</c> (bound to the
    /// local <c>__maldaFloat</c>) returns a JS number unchanged, so operands that are numbers by
    /// construction are emitted bare: numeric literals, <c>- * / %</c> results, unary
    /// minus/plus, and numeric variables (<see cref="InferNumericNames"/>), with <c>+</c> of two
    /// of them. <c>+</c> of anything else may concatenate strings.
    /// </summary>
    private string Number(Expression expression, string transpiled)
    {
        return IsStaticallyNumber(expression) ? transpiled : $"__maldaFloat({transpiled})";
    }

    private bool IsStaticallyNumber(Expression expression)
    {
        return expression switch
        {
            LiteralExpression { Value: int or long or double } => true,
            UnaryExpression { Operator: TokenType.Minus or TokenType.Plus } => true,
            IdentifierExpression identifier => IsNumericName(identifier.Name),
            // An element of a buffer variable: getIndex returns a number or throws.
            ArrayAccessExpression { IsNullConditional: false, Array: IdentifierExpression buffer } => _bufferNames.Contains(buffer.Name),
            BinaryExpression { Operator: TokenType.Plus } plus => IsStaticallyNumber(plus.Left) && IsStaticallyNumber(plus.Right),
            BinaryExpression binary => binary.Operator is
                TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo,
            _ => false
        };
    }

    /// <summary>
    /// A numeric variable. Parameters, fields and imports never are (the analysis excludes the
    /// name everywhere), so a name that shadows one cannot be mistaken for it.
    /// </summary>
    private bool IsNumericName(string name) => _numericNames.Contains(name);

    /// <summary><c>a[i]++</c> / <c>++a[i]</c>: through getIndex and setIndex, so range checks and buffers apply.</summary>
    private string TranspileIndexIncrement(ArrayAccessExpression element, int delta, bool returnOld) =>
        $"mlRuntime.incrementIndex({TranspileExpression(element.Array)}, {TranspileExpression(element.Index)}, {delta}, {(returnOld ? "true" : "false")})";

    /// <summary><c>b[i]</c> where <c>b</c> always holds a <c>buf.*</c> buffer (<see cref="InferNumericNames"/>).</summary>
    private bool IsBufferVariable(ArrayAccessExpression access) =>
        !access.IsNullConditional && access.Array is IdentifierExpression identifier && _bufferNames.Contains(identifier.Name);

    private string TranspileUnaryExpression(UnaryExpression unary)
    {
        if (unary.Operator is TokenType.Increment or TokenType.Decrement && unary.Right is ArrayAccessExpression element)
            return TranspileIndexIncrement(element, unary.Operator == TokenType.Increment ? 1 : -1, returnOld: false);

        var right = TranspileExpression(unary.Right);
        return unary.Operator switch
        {
            TokenType.Not => $"(!{Truthy(unary.Right, right)})",
            TokenType.Minus => $"(-{Number(unary.Right, right)})",
            TokenType.Plus => $"({Number(unary.Right, right)})",
            TokenType.Increment => $"(++{right})",
            TokenType.Decrement => $"(--{right})",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support unary operator '{unary.Operator}'.")
        };
    }

    /// <summary>
    /// MALDA <c>+</c> formats a non-string operand like <c>print</c> (<c>"x" + [1]</c> is
    /// <c>x[1]</c>, not JS <c>x1</c>). Plain JS <c>+</c> already agrees when one side is a
    /// number by construction (number + string concatenates the same text; number + array
    /// is an error on the interpreter), so hot loops such as <c>i + 1</c> stay plain. Next to
    /// a known string only the other side needs formatting; otherwise the runtime decides.
    /// </summary>
    private string Plus(BinaryExpression binary, string left, string right)
    {
        if (IsStaticallyNumber(binary.Left) || IsStaticallyNumber(binary.Right))
            return $"({left} + {right})";

        var leftIsString = IsStaticallyString(binary.Left);
        var rightIsString = IsStaticallyString(binary.Right);
        if (leftIsString && rightIsString)
            return $"({left} + {right})";
        if (leftIsString)
            return $"({left} + __maldaFormat({right}))";
        if (rightIsString)
            return $"(__maldaFormat({left}) + {right})";
        return $"__maldaAdd({left}, {right})";
    }

    private static bool IsStaticallyString(Expression expression)
    {
        return expression switch
        {
            LiteralExpression { Value: string } => true,
            InterpolatedStringExpression => true,
            FunctionCallExpression { Callee: IdentifierExpression { Name: "string" } } => true,
            BinaryExpression { Operator: TokenType.Plus } plus =>
                IsStaticallyString(plus.Left) || IsStaticallyString(plus.Right),
            _ => false
        };
    }

    private string TranspileBinaryExpression(BinaryExpression binary)
    {
        var left = TranspileExpression(binary.Left);
        var right = TranspileExpression(binary.Right);

        return binary.Operator switch
        {
            TokenType.Equal => Equality(binary, left, right),
            TokenType.NotEqual => $"(!{Equality(binary, left, right)})",
            TokenType.And => $"({Truthy(binary.Left, left)} && {Truthy(binary.Right, right)})",
            TokenType.Or => $"({Truthy(binary.Left, left)} || {Truthy(binary.Right, right)})",
            TokenType.NullCoalesce => $"mlRuntime.nullCoalesce({left}, () => {right})",
            TokenType.Minus => $"({Number(binary.Left, left)} - {Number(binary.Right, right)})",
            TokenType.Multiply => $"({Number(binary.Left, left)} * {Number(binary.Right, right)})",
            TokenType.Divide => Division("/", "__maldaDivide", binary.Right, Number(binary.Left, left), Number(binary.Right, right)),
            TokenType.Modulo => Division("%", "__maldaModulo", binary.Right, Number(binary.Left, left), Number(binary.Right, right)),
            TokenType.LessThan => $"({Number(binary.Left, left)} < {Number(binary.Right, right)})",
            TokenType.GreaterThan => $"({Number(binary.Left, left)} > {Number(binary.Right, right)})",
            TokenType.LessThanOrEqual => $"({Number(binary.Left, left)} <= {Number(binary.Right, right)})",
            TokenType.GreaterThanOrEqual => $"({Number(binary.Left, left)} >= {Number(binary.Right, right)})",
            TokenType.Plus => Plus(binary, left, right),
            _ => throw new NotSupportedException($"JavaScript transpilation does not support binary operator '{binary.Operator}'.")
        };
    }

    /// <summary>
    /// MALDA <c>/</c> and <c>%</c> throw on a zero divisor; JavaScript gives Infinity or NaN.
    /// A non-zero numeric literal divisor stays a plain operator.
    /// </summary>
    private static string Division(string op, string helper, Expression divisor, string left, string right)
    {
        return divisor is LiteralExpression { Value: int or long or double } literal && Convert.ToDouble(literal.Value, CultureInfo.InvariantCulture) != 0
            ? $"({left} {op} {right})"
            : $"{helper}({left}, {right})";
    }

    /// <summary>
    /// Assignment with the interpreter's semantics: a write past the end of an array pads with
    /// null, and a compound assignment reads the element first (out of range throws) and
    /// evaluates the target twice, as the interpreter does. <c>/=</c> throws on a zero divisor.
    /// </summary>
    private string TranspileAssignment(AssignmentStatement assignment)
    {
        var value = TranspileExpressionAwaited(assignment.Value);
        if (assignment.Target is ArrayAccessExpression access)
        {
            var array = TranspileExpression(access.Array);
            var index = TranspileExpression(access.Index);
            var (getter, setter) = IsBufferVariable(access) ? ("__maldaBufGet", "__maldaBufSet") : ("__maldaGetIndex", "__maldaSetIndex");
            if (assignment.Operator == TokenType.Assign)
                return $"{setter}({array}, {index}, {value});";
            var current = $"{getter}({array}, {index})";
            return $"{setter}({array}, {index}, {CompoundValue(assignment, current, value)});";
        }

        var target = TranspileAssignmentTarget(assignment.Target);
        if (assignment.Operator == TokenType.DivideAssign)
            return $"{target} = {CompoundValue(assignment, target, value)};";
        return $"{target} {MapAssignmentOperator(assignment.Operator)} {value};";
    }

    private string CompoundValue(AssignmentStatement assignment, string current, string value)
    {
        return assignment.Operator == TokenType.DivideAssign
            ? Division("/", "__maldaDivide", assignment.Value, $"__maldaFloat({current})", Number(assignment.Value, value))
            : $"({current} {MapAssignmentOperator(assignment.Operator)[..1]} {value})";
    }

    /// <summary>
    /// <c>mlRuntime.equals</c> is strict equality that also treats null and undefined as equal.
    /// Against a literal that reduces to plain JS: <c>=== literal</c>, or <c>== null</c>.
    /// </summary>
    private string Equality(BinaryExpression binary, string left, string right)
    {
        // Two numbers: equals() is === for them (NaN differs from itself either way).
        if (IsStaticallyNumber(binary.Left) && IsStaticallyNumber(binary.Right))
        {
            return $"({left} === {right})";
        }

        if (binary.Right is LiteralExpression rightLiteral)
        {
            return rightLiteral.Value == null ? $"({left} == null)" : $"({left} === {right})";
        }

        if (binary.Left is LiteralExpression leftLiteral)
        {
            return leftLiteral.Value == null ? $"({right} == null)" : $"({left} === {right})";
        }

        return $"mlRuntime.equals({left}, {right})";
    }

    private string JoinArguments(List<Expression> arguments)
    {
        return string.Join(", ", arguments.Select(TranspileExpression));
    }

    private static string TranspileLiteral(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        return value switch
        {
            bool booleanValue => booleanValue ? "true" : "false",
            string stringValue => $"\"{EscapeString(stringValue)}\"",
            int intValue => intValue.ToString(CultureInfo.InvariantCulture),
            long longValue => longValue.ToString(CultureInfo.InvariantCulture),
            float floatValue => floatValue.ToString(CultureInfo.InvariantCulture),
            double doubleValue => doubleValue.ToString(CultureInfo.InvariantCulture),
            decimal decimalValue => decimalValue.ToString(CultureInfo.InvariantCulture),
            _ => throw new NotSupportedException($"JavaScript transpilation for literal '{value.GetType().Name}' is not supported yet.")
        };
    }

    private static string EscapeString(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
    }

    private static string EscapeIdentifier(string value)
    {
        return value;
    }

    private string TranspileAssignmentTarget(Expression expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => TranspileIdentifierReference(identifier.Name),
            ThisExpression => "this",
            MemberAccessExpression memberAccess => $"{TranspileAssignmentTarget(memberAccess.Object)}.{EscapeIdentifier(memberAccess.Member)}",
            ArrayAccessExpression arrayAccess => $"{TranspileExpression(arrayAccess.Array)}[{TranspileExpression(arrayAccess.Index)}]",
            _ => throw new NotSupportedException($"JavaScript transpilation for assignment target '{expression.GetType().Name}' is not supported yet.")
        };
    }

    private static string TranspileExpressionStatic(Expression expression)
    {
        return expression switch
        {
            IdentifierExpression identifier => EscapeIdentifier(identifier.Name),
            ThisExpression => "this",
            LiteralExpression literal => TranspileLiteral(literal.Value),
            MemberAccessExpression memberAccess => $"{TranspileExpressionStatic(memberAccess.Object)}.{EscapeIdentifier(memberAccess.Member)}",
            ArrayAccessExpression arrayAccess => $"{TranspileExpressionStatic(arrayAccess.Array)}[{TranspileExpressionStatic(arrayAccess.Index)}]",
            _ => throw new NotSupportedException($"JavaScript transpilation for expression '{expression.GetType().Name}' in assignment target is not supported yet.")
        };
    }

    private static string MapAssignmentOperator(TokenType tokenType)
    {
        return tokenType switch
        {
            TokenType.Assign => "=",
            TokenType.PlusAssign => "+=",
            TokenType.MinusAssign => "-=",
            TokenType.MultiplyAssign => "*=",
            TokenType.DivideAssign => "/=",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support assignment operator '{tokenType}'.")
        };
    }

    private static string MapPostfixOperator(TokenType tokenType)
    {
        return tokenType switch
        {
            TokenType.Increment => "++",
            TokenType.Decrement => "--",
            _ => throw new NotSupportedException($"JavaScript transpilation does not support postfix operator '{tokenType}'.")
        };
    }

    private string TranspileForInitializer(Statement? initializer)
    {
        if (initializer == null)
        {
            return string.Empty;
        }

        return initializer switch
        {
            VarDeclStatement varDecl => $"let {EscapeIdentifier(varDecl.Name)} = {TranspileExpression(varDecl.Initializer)}",
            ExpressionStatement expressionStatement => TranspileExpression(expressionStatement.Expression),
            _ => throw new NotSupportedException($"JavaScript transpilation does not support for-loop initializer '{initializer.GetType().Name}'.")
        };
    }

    private string TranspileForCondition(Expression? condition)
    {
        return condition == null ? string.Empty : TranspileExpression(condition);
    }

    private string TranspileForIncrement(Expression? increment)
    {
        return increment == null ? string.Empty : TranspileExpression(increment);
    }
}
