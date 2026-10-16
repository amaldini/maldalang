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
    // Hints are informational in the interpreter. Only numeric hints change the emitted CLR type;
    // a class, schema, host class (Sequential, Dense, ...), array, or any other name stays object.
    private TranspiledClrType ResolveTranspiledTypeHint(string? typeHint)
    {
        if (!TypedTranspileEnabled)
            return TranspiledClrType.Object;

        if (string.Equals(typeHint, "float", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(typeHint, "double", StringComparison.OrdinalIgnoreCase))
            return TranspiledClrType.Double;
        if (AggressiveTypedTranspileEnabled &&
            (string.Equals(typeHint, "floatArray", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(typeHint, "doubleArray", StringComparison.OrdinalIgnoreCase)))
            return TranspiledClrType.DoubleArray;
        return TranspiledClrType.Object;
    }

    private static string GetClrTypeName(TranspiledClrType type)
    {
        return type switch
        {
            TranspiledClrType.Double => "double",
            TranspiledClrType.DoubleArray => "System.Collections.Generic.List<double>",
            TranspiledClrType.Int => "int",
            _ => "object"
        };
    }

    private static string GetCoercionExpressionPrefix(TranspiledClrType type)
    {
        return type switch
        {
            TranspiledClrType.Double => "(double)RuntimeHelpers.CoerceToFloat(",
            TranspiledClrType.DoubleArray => "RuntimeHelpers.CoerceToDoubleList(",
            TranspiledClrType.Int => "(int)RuntimeHelpers.CoerceToInt(",
            _ => string.Empty
        };
    }

    private static string GetCoercionExpressionSuffix(TranspiledClrType type)
    {
        return type switch
        {
            TranspiledClrType.Double => ")",
            TranspiledClrType.DoubleArray => ")",
            TranspiledClrType.Int => ")",
            _ => string.Empty
        };
    }

    private void PushTypedScope()
    {
        _typedScopeStack.Push(new Dictionary<string, TranspiledClrType>(StringComparer.Ordinal));
    }

    private void PopTypedScope()
    {
        if (_typedScopeStack.Count > 0)
            _typedScopeStack.Pop();
    }

    private void PushConstScope()
    {
        _constScopeStack.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private void PopConstScope()
    {
        if (_constScopeStack.Count > 0)
            _constScopeStack.Pop();
    }

    private void RegisterConstBinding(string name)
    {
        if (_constScopeStack.Count > 0)
            _constScopeStack.Peek().Add(name);
    }

    private bool IsConstBinding(string name)
    {
        foreach (var scope in _constScopeStack)
        {
            if (scope.Contains(name))
                return true;
        }

        return _moduleConstNames.Contains(name);
    }

    private bool IsConstIdentifierPattern(string name)
    {
        if (_variantConstructorNames.Contains(name))
            return false;
        return IsConstBinding(name);
    }

    private void EmitConstAssignGuard(string name)
    {
        if (!IsConstBinding(name))
            return;

        WriteIndent();
        _output.Append("throw new System.Exception(\"Cannot assign to const '");
        _output.Append(name);
        _output.AppendLine("'.\");");
    }

    private void RegisterTypedVariable(string name, TranspiledClrType type)
    {
        if (_typedScopeStack.Count == 0)
            return;
        _typedScopeStack.Peek()[name] = type;
    }

    private TranspiledClrType ResolveVariableTypeOrDefault(string name)
    {
        foreach (var scope in _typedScopeStack)
        {
            if (scope.TryGetValue(name, out var type))
                return type;
        }
        // Inferred names are bound nowhere else in the program, so a miss here is the global.
        return _inferredGlobalTypes.TryGetValue(name, out var globalType) ? globalType : TranspiledClrType.Object;
    }

    /// <summary>The CLR type a <c>var</c> declaration is emitted with: its hint, or an inferred int/double.</summary>
    private TranspiledClrType ResolveVarDeclType(VarDeclStatement varDecl)
    {
        if (string.IsNullOrWhiteSpace(varDecl.TypeHint))
        {
            if (_activeLocals.Declarations.TryGetValue(varDecl, out var localKind))
                return localKind == NumericVariableInference.Kind.Int ? TranspiledClrType.Int : TranspiledClrType.Double;
            if (_inferredNumerics.Ints.Contains(varDecl.Name))
                return TranspiledClrType.Int;
            if (_inferredNumerics.Doubles.Contains(varDecl.Name))
                return TranspiledClrType.Double;
        }
        return ResolveTranspiledTypeHint(varDecl.TypeHint);
    }

    /// <summary>
    /// Emits <paramref name="value"/> converted to <paramref name="targetType"/>, skipping the
    /// boxing coercion helper when the value is already a plain C# double.
    /// </summary>
    private void EmitCoercedValue(Expression value, TranspiledClrType targetType)
    {
        var alreadyTyped = targetType switch
        {
            TranspiledClrType.Double => IsDoubleTypedValue(value),
            // Plain int literals are typed Double for the float paths but emit as C# ints.
            TranspiledClrType.Int =>
                value is LiteralExpression { Value: int } || ResolveExpressionType(value) == TranspiledClrType.Int,
            _ => false
        };
        if (alreadyTyped)
        {
            TranspileExpression(value);
            return;
        }

        _output.Append(GetCoercionExpressionPrefix(targetType));
        TranspileExpression(value);
        _output.Append(GetCoercionExpressionSuffix(targetType));
    }

    /// <summary>
    /// Emits the <c>accessingClass</c> argument for the generated member helpers, mirroring
    /// the interpreter's <c>_currentClass</c> rule: the runtime type of <c>this</c> inside an
    /// instance member, the declaring type inside a static member, and <c>null</c> at top level.
    /// </summary>
    private void EmitAccessingClassArg()
    {
        if (_currentClassName == null)
        {
            _output.Append("null");
        }
        else if (_currentMemberIsStatic)
        {
            _output.Append("typeof(");
            _output.Append(EscapeIdentifier(_currentClassName));
            _output.Append(")");
        }
        else
        {
            _output.Append("this.GetType()");
        }
    }

    /// <summary>
    /// True when <paramref name="name"/> is bound as a local/parameter in the current
    /// function (i.e. it shadows any top-level class of the same name).
    /// </summary>
    private bool IsDeclaredLocal(string name)
    {
        foreach (var scope in _typedScopeStack)
        {
            if (scope.ContainsKey(name))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="expression"/> is a bare reference to a transpiled class
    /// (i.e. a static access receiver such as <c>Counter</c> in <c>Counter.count</c>).
    /// </summary>
    private bool IsClassReference(Expression? expression)
    {
        return expression is IdentifierExpression id
            && _classDeclarations.ContainsKey(id.Name)
            && !IsDeclaredLocal(id.Name);
    }

    /// <summary>
    /// True when <c>this.member</c> can be emitted as a plain C# field access instead of the
    /// reflective Get/SetObjectMember helpers: an untyped instance field declared on the
    /// current class and not redeclared by any subclass (reflection starts at the runtime
    /// type, so a shadowing subclass field would otherwise win).
    /// </summary>
    private bool IsDirectThisField(Expression receiver, string memberName)
    {
        if (receiver is not ThisExpression ||
            _currentClassName == null ||
            _currentMemberIsStatic ||
            !_classDeclarations.TryGetValue(_currentClassName, out var currentDecl))
        {
            return false;
        }

        var field = currentDecl.Members.FirstOrDefault(m =>
            m.Type == MemberType.Field && !m.IsStatic && string.Equals(m.Name, memberName, StringComparison.Ordinal));
        if (field == null || ResolveTranspiledTypeHint(field.TypeHint) != TranspiledClrType.Object)
        {
            return false;
        }

        foreach (var decl in _classDeclarations.Values)
        {
            if (decl == currentDecl || !InheritsFrom(decl, currentDecl.Name))
            {
                continue;
            }

            if (decl.Members.Any(m => m.Type == MemberType.Field && string.Equals(m.Name, memberName, StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Classes whose <paramref name="methodName"/> a call on <paramref name="receiver"/> may be
    /// dispatched to directly (a C# type test plus a plain call) before falling back to the
    /// reflective CallObjectMethod. Only public, untyped, synchronous instance methods with the
    /// call's arity that no subclass redeclares qualify, which is exactly what the reflection
    /// path would pick for an instance of that class.
    /// </summary>
    private List<string> FindDirectMethodTargets(Expression receiver, string methodName, int argumentCount)
    {
        var targets = new List<string>();
        var simpleReceiver = receiver is ThisExpression ||
            (receiver is IdentifierExpression id && !IsClassReference(receiver) && !IsKnownUserFunction(id));
        if (!simpleReceiver)
        {
            return targets;
        }

        foreach (var decl in _classDeclarations.Values)
        {
            var method = decl.Members.FirstOrDefault(m =>
                m.Type == MemberType.Method && string.Equals(m.Name, methodName, StringComparison.Ordinal));
            if (method == null ||
                method.IsStatic ||
                method.Access == AccessModifier.Private ||
                method.Value is not FunctionDeclaration func ||
                func.Parameters.Count != argumentCount ||
                (func.Decorators != null && func.Decorators.Count > 0) ||
                ResolveTranspiledTypeHint(func.ReturnType) != TranspiledClrType.Object ||
                (func.ParameterTypeHints != null && func.ParameterTypeHints.Any(h => ResolveTranspiledTypeHint(h) != TranspiledClrType.Object)))
            {
                continue;
            }

            var redeclared = _classDeclarations.Values.Any(other =>
                other != decl &&
                InheritsFrom(other, decl.Name) &&
                other.Members.Any(m => string.Equals(m.Name, methodName, StringComparison.Ordinal)));
            if (redeclared)
            {
                continue;
            }

            targets.Add(decl.Name);
        }

        // A long type-test chain stops paying for itself; keep the reflective call.
        return targets.Count <= 3 ? targets : new List<string>();
    }

    private void EmitObjectMethodArguments(List<Expression> arguments)
    {
        for (int i = 0; i < arguments.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            EmitObjectMethodArgument(arguments[i]);
        }
    }

    private void EmitObjectMethodArgument(Expression argument)
    {
        if (argument is IdentifierExpression argIdExpr && IsKnownUserFunction(argIdExpr))
        {
            // Unary user functions → callable delegate (GraphMemory.initialize embedders, etc.).
            // Multi-arg (e.g. HttpServer middleware) stay as name strings for runtime resolve.
            if (TryGetKnownFunctionParameterTypes(argIdExpr.Name, out var paramTypes)
                && paramTypes.Count == 1)
            {
                _output.Append("(System.Func<object, System.Threading.Tasks.Task<object>>)");
                _output.Append(EscapeIdentifier(argIdExpr.Name));
            }
            else
            {
                _output.Append("\"");
                _output.Append(argIdExpr.Name);
                _output.Append("\"");
            }
        }
        else
        {
            TranspileExpression(argument);
        }
    }

    private bool InheritsFrom(ClassDeclaration decl, string ancestorName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        for (var current = decl.Superclass;
             current != null && visited.Add(current);
             current = _classDeclarations.TryGetValue(current, out var parent) ? parent.Superclass : null)
        {
            if (string.Equals(current, ancestorName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Emits a receiver for a member access/call. Bare class names become
    /// <c>typeof(Class)</c> so the static runtime helpers can resolve them; anything
    /// else is emitted normally.
    /// </summary>
    private void TranspileReceiver(Expression expression)
    {
        if (IsClassReference(expression))
        {
            _output.Append("typeof(");
            _output.Append(EscapeIdentifier(((IdentifierExpression)expression).Name));
            _output.Append(")");
            return;
        }
        TranspileExpression(expression);
    }

    private TranspiledClrType ResolveExpressionType(Expression expression)
    {
        if (!TypedTranspileEnabled)
            return TranspiledClrType.Object;

        return expression switch
        {
            LiteralExpression literal when literal.Value is double => TranspiledClrType.Double,
            LiteralExpression literal when literal.Value is int => TranspiledClrType.Double,
            IdentifierExpression identifier => ResolveVariableTypeOrDefault(identifier.Name),
            ArrayAccessExpression arrayAccess when ResolveExpressionType(arrayAccess.Array) == TranspiledClrType.DoubleArray => TranspiledClrType.Double,
            PostfixExpression postfix when (postfix.Operator == TokenType.Increment || postfix.Operator == TokenType.Decrement) &&
                                             ResolveExpressionType(postfix.Left) == TranspiledClrType.Double => TranspiledClrType.Double,
            FunctionCallExpression typedCall when typedCall.Callee is IdentifierExpression { ResolvesToLocalValue: false } typedId &&
                                            TryGetTypedTwinCall(typedId.Name, typedCall.Arguments, out var typedSignature) => typedSignature.Return,
            FunctionCallExpression methodCall when methodCall.Callee is MemberAccessExpression &&
                                            TryGetTypedMethodCall(methodCall, out var methodSignature) => methodSignature.Return,
            FunctionCallExpression call when call.Callee is IdentifierExpression { ResolvesToLocalValue: false } id &&
                                            _functionReturnTypes.TryGetValue(id.Name, out var returnType) => returnType,
            FunctionCallExpression call when call.Callee is IdentifierExpression { ShadowsBuiltIn: false } builtInId &&
                                            ResolveTypedBuiltInReturnType(builtInId.Name, call.Arguments) is var builtInReturnType &&
                                            builtInReturnType != TranspiledClrType.Object => builtInReturnType,
            UnaryExpression { Operator: TokenType.Minus } intNegation when
                                       ResolveExpressionType(intNegation.Right) == TranspiledClrType.Int => TranspiledClrType.Int,
            UnaryExpression unary when unary.Operator == TokenType.Minus &&
                                       unary.Right is not LiteralExpression { Value: int } &&
                                       ResolveExpressionType(unary.Right) == TranspiledClrType.Double => TranspiledClrType.Double,
            BinaryExpression binary => ResolveBinaryType(binary),
            TernaryExpression ternary when !IsIntLiteralOnly(ternary) &&
                                          ResolveExpressionType(ternary.ThenBranch) == TranspiledClrType.Double &&
                                          ResolveExpressionType(ternary.ElseBranch) == TranspiledClrType.Double => TranspiledClrType.Double,
            AwaitExpression awaitExpr => ResolveExpressionType(awaitExpr.Expression),
            _ => TranspiledClrType.Object
        };
    }

    private TranspiledClrType ResolveTypedBuiltInReturnType(string name, List<Expression> arguments)
    {
        if (!TypedTranspileEnabled)
            return TranspiledClrType.Object;

        bool FirstArgIsDouble() => arguments.Count > 0 && ResolveExpressionType(arguments[0]) == TranspiledClrType.Double;
        bool FirstTwoArgsAreDouble() => arguments.Count > 1 &&
                                        ResolveExpressionType(arguments[0]) == TranspiledClrType.Double &&
                                        ResolveExpressionType(arguments[1]) == TranspiledClrType.Double;

        return name switch
        {
            // Numeric built-ins that preserve/return double when inputs are proven doubles.
            "float" when FirstArgIsDouble() => TranspiledClrType.Double,
            "abs" when FirstArgIsDouble() => TranspiledClrType.Double,
            "sqrt" when FirstArgIsDouble() => TranspiledClrType.Double,
            "floor" when FirstArgIsDouble() => TranspiledClrType.Double,
            "ceil" when FirstArgIsDouble() => TranspiledClrType.Double,
            "round" when FirstArgIsDouble() => TranspiledClrType.Double,
            "trunc" when FirstArgIsDouble() => TranspiledClrType.Double,
            "sign" when FirstArgIsDouble() => TranspiledClrType.Double,
            "exp" when FirstArgIsDouble() => TranspiledClrType.Double,
            "log" when FirstArgIsDouble() => TranspiledClrType.Double,
            "log10" when FirstArgIsDouble() => TranspiledClrType.Double,
            "log2" when FirstArgIsDouble() => TranspiledClrType.Double,
            "sin" when FirstArgIsDouble() => TranspiledClrType.Double,
            "cos" when FirstArgIsDouble() => TranspiledClrType.Double,
            "tan" when FirstArgIsDouble() => TranspiledClrType.Double,
            "asin" when FirstArgIsDouble() => TranspiledClrType.Double,
            "acos" when FirstArgIsDouble() => TranspiledClrType.Double,
            "atan" when FirstArgIsDouble() => TranspiledClrType.Double,
            "pow" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            "min" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            "max" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            "atan2" when FirstTwoArgsAreDouble() => TranspiledClrType.Double,
            _ => TranspiledClrType.Object
        };
    }

    /// <summary>
    /// Int literals resolve as Double only so they can join a real double operand. An
    /// expression built from int literals alone must keep MALDA int semantics: emitted as raw
    /// C# it would divide as integers (<c>7 / 2</c> is 3) and fold overflow at compile time.
    /// </summary>
    private static bool IsIntLiteralOnly(Expression expression)
    {
        return expression switch
        {
            LiteralExpression { Value: int } => true,
            UnaryExpression { Operator: TokenType.Minus } unary => IsIntLiteralOnly(unary.Right),
            BinaryExpression binary when IsNumericBinaryOperator(binary.Operator) =>
                IsIntLiteralOnly(binary.Left) && IsIntLiteralOnly(binary.Right),
            TernaryExpression ternary => IsIntLiteralOnly(ternary.ThenBranch) && IsIntLiteralOnly(ternary.ElseBranch),
            _ => false
        };
    }

    /// <summary>
    /// Type of a binary expression in the typed emission. Int with int (an inferred int name
    /// or a plain int literal, at least one of them typed) stays Int for <c>+ - * %</c> and is a
    /// Double for <c>/</c>; any other mix of Int/Double operands is a Double. Each operand is
    /// resolved once, so nested arithmetic stays linear.
    /// </summary>
    private TranspiledClrType ResolveBinaryType(BinaryExpression binary)
    {
        if (!IsNumericBinaryOperator(binary.Operator) || IsIntLiteralOnly(binary))
            return TranspiledClrType.Object;

        var left = ResolveExpressionType(binary.Left);
        var right = ResolveExpressionType(binary.Right);
        var leftInt = left == TranspiledClrType.Int || binary.Left is LiteralExpression { Value: int };
        var rightInt = right == TranspiledClrType.Int || binary.Right is LiteralExpression { Value: int };
        if (leftInt && rightInt)
            return binary.Operator == TokenType.Divide ? TranspiledClrType.Double : TranspiledClrType.Int;

        return left is TranspiledClrType.Int or TranspiledClrType.Double &&
               right is TranspiledClrType.Int or TranspiledClrType.Double
            ? TranspiledClrType.Double
            : TranspiledClrType.Object;
    }

    /// <summary>Int for an inferred int name or plain int literal, Double for a typed double, else Object.</summary>
    private TranspiledClrType ResolveNumericOperandKind(Expression expression)
    {
        if (expression is LiteralExpression { Value: int })
            return TranspiledClrType.Int;
        var type = ResolveExpressionType(expression);
        return type is TranspiledClrType.Int or TranspiledClrType.Double ? type : TranspiledClrType.Object;
    }

    private static bool IsNumericBinaryOperator(TokenType op)
    {
        return op is TokenType.Plus or TokenType.Minus or TokenType.Multiply or TokenType.Divide or TokenType.Modulo;
    }

    private void EmitTopLevelFieldDeclarations(List<VarDeclStatement> topLevelVariables)
    {
        var emittedNames = new HashSet<string>();
        foreach (var varDecl in topLevelVariables)
        {
            if (!emittedNames.Add(varDecl.Name))
                continue;

            WriteIndent();
            var declaredType = ResolveVarDeclType(varDecl);
            _output.Append("public static ");
            _output.Append(GetClrTypeName(declaredType));
            _output.Append(" ");
            _output.Append(EscapeIdentifier(varDecl.Name));
            _output.Append(" = ");
            _output.Append(declaredType switch
            {
                TranspiledClrType.Double => "0d;",
                TranspiledClrType.Int => "0;",
                _ => "null!;"
            });
            AppendComment(nameof(Transpile) + " (top-level field)");
            _output.AppendLine();
        }

        if (emittedNames.Count > 0)
        {
            _output.AppendLine();
        }
    }

    private void TranspileTopLevelVarInitialization(VarDeclStatement varDecl)
    {
        if (varDecl.IsConst)
            RegisterConstBinding(varDecl.Name);
        WriteIndent();
        string? profileVariable = null;
        if (ProfilingEnabled)
        {
            profileVariable = EmitStatementProfileStart(varDecl);
            WriteIndent();
        }
        var declaredType = ResolveVarDeclType(varDecl);
        _output.Append(EscapeIdentifier(varDecl.Name));
        _output.Append(" = ");
        EmitCoercedValue(varDecl.Initializer, declaredType);
        _output.Append(";");
        AppendComment(nameof(Transpile) + " (top-level var init)");
        _output.AppendLine();
        if (profileVariable != null)
        {
            EmitStatementProfileExit(profileVariable);
        }
    }
}
