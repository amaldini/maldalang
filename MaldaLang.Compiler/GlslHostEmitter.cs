// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using MaldaLang;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>One line of an emitted host twin: the source node it maps to, its indent and its text.</summary>
internal readonly record struct GlslHostLine(Node? Source, int Indent, string Text);

/// <summary>
/// Emits the JavaScript twin of an <c>@shader()</c> function that host code calls. The emit follows
/// the types of <see cref="GlslTranspiler.AnalyzeTypes"/>: scalar code stays native JavaScript,
/// vector operators, swizzles and built-ins go through <c>mlRuntime.glsl</c> (bound to
/// <see cref="RuntimeAlias"/>), <c>int</c> division truncates. Vectors are plain arrays; a twin
/// copies one where GLSL would, so it never writes into a list the host passed in.
/// </summary>
internal sealed class GlslHostEmitter
{
    /// <summary>The local name the generated file binds to <c>mlRuntime.glsl</c>.</summary>
    public const string RuntimeAlias = "__maldaGlsl";

    /// <summary>The hidden first parameter of a twin that reads its material.</summary>
    public const string MaterialParameter = "__maldaMaterial";

    /// <summary>The JavaScript name of the twin of a function that takes the material.</summary>
    public static string BoundName(string function) => "__maldaBound_" + function;

    // All-scalar calls that are one Math function with the same result as the GLSL built-in.
    private static readonly Dictionary<string, string> NativeScalarCalls = new(StringComparer.Ordinal)
    {
        ["sin"] = "Math.sin", ["cos"] = "Math.cos", ["tan"] = "Math.tan",
        ["asin"] = "Math.asin", ["acos"] = "Math.acos",
        ["sinh"] = "Math.sinh", ["cosh"] = "Math.cosh", ["tanh"] = "Math.tanh",
        ["exp"] = "Math.exp", ["log"] = "Math.log", ["log2"] = "Math.log2", ["sqrt"] = "Math.sqrt",
        ["pow"] = "Math.pow", ["abs"] = "Math.abs", ["sign"] = "Math.sign",
        ["floor"] = "Math.floor", ["ceil"] = "Math.ceil", ["trunc"] = "Math.trunc",
        ["min"] = "Math.min", ["max"] = "Math.max"
    };

    private static readonly HashSet<string> NoHostMeaning = new(StringComparer.Ordinal)
    {
        "dFdx", "dFdy", "fwidth", "texelFetch", "texture", "texture2D"
    };

    private readonly FunctionDeclaration _function;
    private readonly GlslFunctionTypes _types;
    private readonly List<GlslHostLine> _lines = new();
    private int _indent;
    private bool _writesComponents;
    private IReadOnlyDictionary<string, GlslLiteralConst>? _consts;
    private GlslHostAnalysis? _binding;
    private readonly HashSet<string> _boxed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _outVectors = new(StringComparer.Ordinal);
    private bool _passesOut;

    private GlslHostEmitter(FunctionDeclaration function, GlslFunctionTypes types)
    {
        _function = function;
        _types = types;
    }

    /// <summary>The lines of <c>function name(params) { … }</c> for an analyzed shader function.</summary>
    /// <param name="types">The analyzed function.</param>
    /// <param name="consts">The top-level MALDA consts the type pass was given as globals, by name.</param>
    /// <param name="binding">For a function reached through <c>glsl.host</c>: what it reads from its material.</param>
    public static IReadOnlyList<GlslHostLine> Emit(
        GlslFunctionTypes types,
        IReadOnlyDictionary<string, GlslLiteralConst>? consts = null,
        GlslHostAnalysis? binding = null)
    {
        var emitter = new GlslHostEmitter(types.Function, types) { _consts = consts, _binding = binding };
        emitter.EmitFunction();
        return emitter._lines;
    }

    private void EmitFunction()
    {
        foreach (var parameter in _types.Parameters)
        {
            if (parameter.Qualifier == "inout")
                throw Error(_function, $"'inout' parameter '{parameter.Name}' is not supported in a host-called function.");
            RequireHostType(parameter.Type, _function, $"parameter '{parameter.Name}'");
            if (parameter.Type.IsArray && parameter.Qualifier == "out")
                throw Error(_function, $"'out' array parameter '{parameter.Name}' is not supported in a host-called function.");
        }

        FindOutParameters();

        if (_types.ReturnType.Base != GlslBaseType.Void)
            RequireHostType(_types.ReturnType, _function, "the return value");

        var written = ComponentWrittenNames();
        // A callee writes an `out` vector in place, so a vector must never alias another one here.
        _writesComponents = written.Count > 0 || _passesOut || _outVectors.Count > 0;

        var takesMaterial = _binding != null && _binding.NeedsBinding(_function.Name);
        if (takesMaterial)
        {
            var parameters = new[] { MaterialParameter }.Concat(_function.Parameters);
            Line(_function, $"function {BoundName(_function.Name)}({string.Join(", ", parameters)}) {{");
        }
        else
        {
            Line(_function, $"function {_function.Name}({string.Join(", ", _function.Parameters)}) {{");
        }

        _indent++;
        if (takesMaterial)
            EmitUniformReads();
        // GLSL passes by value: a parameter the body writes into is copied first.
        foreach (var parameter in _types.Parameters)
        {
            if (parameter.Qualifier == "out")
                continue;
            // With an `out` vector around, an input may be the same list as an output (f(v, v)):
            // GLSL copies inputs in, so the twin does too.
            if (parameter.Type.IsVector && (written.Contains(parameter.Name) || _passesOut || _outVectors.Count > 0))
                Line(_function, $"{parameter.Name} = {parameter.Name}.slice();");
            else if (_boxed.Contains(parameter.Name))
                Line(_function, $"{parameter.Name} = [{parameter.Name}];");
        }

        EmitStatements(_function.Body.Statements);
        _indent--;
        Line(_function, "}");
    }

    /// <summary>
    /// How `out` reaches JavaScript, which passes numbers by value and lists by reference.
    /// A vector `out` parameter is the caller's list: the callee writes its components in place
    /// (<c>col = v</c> copies into it). A scalar `out` parameter is a one-slot box, <c>[value]</c>:
    /// every local or parameter that is passed as a scalar `out` argument lives in a box for the
    /// whole function, by name, so the call can hand the box over.
    /// </summary>
    private void FindOutParameters()
    {
        foreach (var parameter in _types.Parameters)
        {
            if (parameter.Qualifier != "out")
                continue;
            if (parameter.Type.IsScalar)
                _boxed.Add(parameter.Name);
            else
                _outVectors.Add(parameter.Name);
        }

        foreach (var call in AstNodeWalker.Descendants(_function.Body).OfType<FunctionCallExpression>())
        {
            if (call.Callee is not IdentifierExpression callee ||
                !_types.CalleeParameters.TryGetValue(callee.Name, out var parameters))
            {
                continue;
            }

            for (var i = 0; i < parameters.Count && i < call.Arguments.Count; i++)
            {
                if (parameters[i].Qualifier != "out")
                    continue;

                _passesOut = true;
                var argument = call.Arguments[i];
                var isName = argument is IdentifierExpression name && !_types.IsGlobal(name);
                if (parameters[i].Type.IsScalar)
                {
                    if (!isName)
                    {
                        throw Error(argument,
                            $"the 'out' argument '{parameters[i].Name}' of {callee.Name}() must be a local variable or a parameter in a host-called function, not a component or an array element.");
                    }

                    _boxed.Add(((IdentifierExpression)argument).Name);
                }
                else if (!isName && argument is not ArrayAccessExpression)
                {
                    throw Error(argument,
                        $"the 'out' argument '{parameters[i].Name}' of {callee.Name}() must be a vector variable or an array element in a host-called function, not a swizzle or an expression.");
                }
            }
        }
    }

    /// <summary>
    /// The uniforms the function reads, once at entry: a uniform cannot change during a call, and a
    /// vector read allocates. Each is a copy of the material's value now.
    /// </summary>
    private void EmitUniformReads()
    {
        var declared = new HashSet<string>(_function.Parameters, StringComparer.Ordinal);
        foreach (var local in AstNodeWalker.Descendants(_function.Body).OfType<VarDeclStatement>())
            declared.Add(local.Name);

        foreach (var name in _binding!.UniformReads(_function.Name))
        {
            if (declared.Contains(name))
            {
                throw Error(_function,
                    $"'{name}' is both a uniform the function reads and one of its parameters or locals: rename the parameter or local.");
            }

            var read = $"{RuntimeAlias}.uniform({MaterialParameter}, \"{name}\")";
            // A bool uniform set as 0 / 1 must compare as a boolean.
            if (_binding.UniformTypes[name] == GlslType.Bool)
                read = $"{RuntimeAlias}.bool({read})";
            Line(_function, $"const {name} = {read};");
        }
    }

    /// <summary>Names written through a component, swizzle or index (<c>v.x = …</c>, <c>a[i] = …</c>, <c>v.y++</c>).</summary>
    private HashSet<string> ComponentWrittenNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in AstNodeWalker.Descendants(_function.Body))
        {
            var target = node switch
            {
                AssignmentStatement assignment => assignment.Target,
                UnaryExpression { Operator: TokenType.Increment or TokenType.Decrement } unary => unary.Right,
                PostfixExpression { Operator: TokenType.Increment or TokenType.Decrement } postfix => postfix.Left,
                _ => null
            };
            if (target is null or IdentifierExpression)
                continue;

            while (true)
            {
                if (target is MemberAccessExpression member)
                    target = member.Object;
                else if (target is ArrayAccessExpression arrayAccess)
                    target = arrayAccess.Array;
                else
                    break;
            }

            if (target is IdentifierExpression root)
                names.Add(root.Name);
        }

        return names;
    }

    // ---- Statements -----------------------------------------------------------------------

    private void EmitStatements(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
            EmitStatement(statement);
    }

    private void EmitStatement(Statement statement)
    {
        switch (statement)
        {
            case BlockStatement { Statements: [VarDeclStatement loopVar, WhileStatement { IsDesugaredFor: true } loop] }
                when loop.Body is BlockStatement { Statements: [var loopBody, var increment] }:
                // The parser desugars a typed `for` into { var; while { body; i++ } }. A real `for`
                // again, so `continue` still runs the increment.
                Line(loop, $"for ({VarDeclText(loopVar)}; {Expr(loop.Condition)}; {InlineStatement(increment)})");
                EmitEmbedded(loopBody);
                break;
            case BlockStatement block:
                Line(block, "{");
                _indent++;
                EmitStatements(block.Statements);
                _indent--;
                Line(block, "}");
                break;
            case VarDeclStatement varDecl:
                Line(varDecl, VarDeclText(varDecl) + ";");
                break;
            case AssignmentStatement assignment:
                Line(assignment, AssignmentText(assignment) + ";");
                break;
            case IfStatement ifStatement:
                Line(ifStatement, $"if ({Expr(ifStatement.Condition)})");
                EmitEmbedded(ifStatement.ThenBranch);
                if (ifStatement.ElseBranch != null)
                {
                    Line(ifStatement, "else");
                    EmitEmbedded(ifStatement.ElseBranch);
                }
                break;
            case WhileStatement whileStatement:
                Line(whileStatement, $"while ({Expr(whileStatement.Condition)})");
                EmitEmbedded(whileStatement.Body);
                break;
            case ForStatement forStatement:
            {
                var initializer = forStatement.Initializer == null ? "" : InlineStatement(forStatement.Initializer);
                var condition = forStatement.Condition == null ? "" : Expr(forStatement.Condition);
                var step = forStatement.Increment == null ? "" : Expr(forStatement.Increment);
                Line(forStatement, $"for ({initializer}; {condition}; {step})");
                EmitEmbedded(forStatement.Body);
                break;
            }
            case ReturnStatement returnStatement:
                // A returned parameter or local must not alias what the caller still holds.
                Line(returnStatement, returnStatement.Value == null
                    ? "return;"
                    : $"return {ValueCopy(returnStatement.Value, always: true)};");
                break;
            case ExpressionStatement expressionStatement:
                Line(expressionStatement, Expr(expressionStatement.Expression) + ";");
                break;
            case BreakStatement:
                Line(statement, "break;");
                break;
            case ContinueStatement:
                Line(statement, "continue;");
                break;
            default:
                throw Error(statement, $"statement '{statement.GetType().Name}' is not part of the GLSL shader subset.");
        }
    }

    private void EmitEmbedded(Statement statement)
    {
        if (statement is BlockStatement)
        {
            EmitStatement(statement);
            return;
        }

        Line(statement, "{");
        _indent++;
        EmitStatement(statement);
        _indent--;
        Line(statement, "}");
    }

    private string InlineStatement(Statement statement) => statement switch
    {
        VarDeclStatement varDecl => VarDeclText(varDecl),
        AssignmentStatement assignment => AssignmentText(assignment),
        ExpressionStatement expressionStatement => Expr(expressionStatement.Expression),
        _ => throw Error(statement, $"'{statement.GetType().Name}' is not valid in a for header of an @shader() function.")
    };

    private string VarDeclText(VarDeclStatement varDecl)
    {
        var type = _types.TypeOf(varDecl);
        RequireHostType(type, varDecl, $"local '{varDecl.Name}'");
        var value = varDecl.Initializer is LiteralExpression { Value: null }
            ? DefaultValue(type, varDecl)
            : ValueCopy(varDecl.Initializer, always: false);
        return _boxed.Contains(varDecl.Name) ? $"let {varDecl.Name} = [{value}]" : $"let {varDecl.Name} = {value}";
    }

    /// <summary>What an uninitialized local holds. GLSL leaves it undefined; zero keeps the twin deterministic.</summary>
    private string DefaultValue(GlslType type, Node at)
    {
        if (type.IsArray)
        {
            if (type.ArrayLength == GlslType.UnknownArrayLength)
                throw Error(at, "an array sized by a const name has no host size yet (consts are not bound to host-called functions).");
            var length = type.ArrayLength.ToString(CultureInfo.InvariantCulture);
            return type.Element.IsVector
                ? $"Array.from({{ length: {length} }}, () => {DefaultValue(type.Element, at)})"
                : $"new Array({length}).fill({DefaultValue(type.Element, at)})";
        }

        var zero = type.Base == GlslBaseType.Bool ? "false" : "0";
        return type.IsScalar ? zero : $"[{string.Join(", ", Enumerable.Repeat(zero, type.Components))}]";
    }

    private string AssignmentText(AssignmentStatement assignment)
    {
        var targetType = _types.TypeOf(assignment.Target);
        var op = assignment.Operator;

        // v.xz = …: the only target that is not a JavaScript lvalue.
        if (assignment.Target is MemberAccessExpression swizzle && targetType.IsVector)
        {
            var indexes = SwizzleIndexes(swizzle.Member);
            var value = op == TokenType.Assign
                ? Expr(assignment.Value)
                : Arithmetic(BinaryOf(op, assignment), targetType, Expr(assignment.Target), _types.TypeOf(assignment.Value), Expr(assignment.Value));
            return $"{RuntimeAlias}.setSwizzle({Expr(swizzle.Object)}, {indexes}, {value})";
        }

        var target = Expr(assignment.Target);
        // An `out` vector is the caller's list: assigning it copies into that list.
        if (assignment.Target is IdentifierExpression outName && _outVectors.Contains(outName.Name) && !_types.IsGlobal(outName))
        {
            var value = op == TokenType.Assign
                ? Expr(assignment.Value)
                : Arithmetic(BinaryOf(op, assignment), targetType, target, _types.TypeOf(assignment.Value), Expr(assignment.Value));
            return $"{RuntimeAlias}.assign({target}, {value})";
        }

        if (op == TokenType.Assign)
            return $"{target} = {ValueCopy(assignment.Value, always: false)}";

        var binary = BinaryOf(op, assignment);
        var valueType = _types.TypeOf(assignment.Value);
        // Float scalars keep the native compound operator; everything else is spelled out.
        if (targetType == GlslType.Float || (targetType == GlslType.Int && binary != TokenType.Divide))
            return $"{target} {OperatorText(binary)}= {Expr(assignment.Value)}";
        return $"{target} = {Arithmetic(binary, targetType, target, valueType, Expr(assignment.Value))}";
    }

    private TokenType BinaryOf(TokenType assignmentOperator, Node at) => assignmentOperator switch
    {
        TokenType.PlusAssign => TokenType.Plus,
        TokenType.MinusAssign => TokenType.Minus,
        TokenType.MultiplyAssign => TokenType.Multiply,
        TokenType.DivideAssign => TokenType.Divide,
        _ => throw Error(at, $"assignment operator '{assignmentOperator}' is not supported in GLSL.")
    };

    // ---- Expressions ----------------------------------------------------------------------

    /// <summary>
    /// An expression stored or returned as a value. A bare vector or array name (or element) is the
    /// same JavaScript array as its source, so it is copied when the function writes components
    /// anywhere (<paramref name="always"/>: at a return, whatever the body does).
    /// </summary>
    private string ValueCopy(Expression expression, bool always)
    {
        var text = Expr(expression);
        var type = _types.TypeOf(expression);
        if (type.IsScalar || !(always || _writesComponents) || !IsAlias(expression))
            return text;
        return type.IsArray && type.Element.IsVector
            ? $"{text}.map((element) => element.slice())"
            : $"{text}.slice()";
    }

    private static bool IsAlias(Expression expression) => expression switch
    {
        IdentifierExpression => true,
        ArrayAccessExpression => true,
        TernaryExpression ternary => IsAlias(ternary.ThenBranch) || IsAlias(ternary.ElseBranch),
        _ => false
    };

    private string Expr(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return LiteralText(literal);
            case IdentifierExpression identifier:
                if (!_types.IsGlobal(identifier))
                    return _boxed.Contains(identifier.Name) ? identifier.Name + "[0]" : identifier.Name;
                // A MALDA const is inlined: the twin does not depend on where the const lives in the
                // generated file (a main() local, a module binding, a @worker() scope).
                if (_consts != null && _consts.TryGetValue(identifier.Name, out var constant))
                    return constant.GlslValue.StartsWith('-') ? $"({constant.GlslValue})" : constant.GlslValue;
                // A uniform: read into a const of that name at the top of a bound twin.
                if (_binding != null && _binding.UniformTypes.ContainsKey(identifier.Name))
                    return identifier.Name;
                throw Error(identifier, $"'{identifier.Name}' has no host value.");
            case UnaryExpression unary:
            {
                var operandType = _types.TypeOf(unary.Right);
                var operand = Expr(unary.Right);
                switch (unary.Operator)
                {
                    case TokenType.Not:
                        return $"(!{operand})";
                    case TokenType.Plus:
                        return operand;
                    case TokenType.Minus:
                        return operandType.IsScalar ? $"(-{operand})" : $"{RuntimeAlias}.neg({operand})";
                    case TokenType.Increment:
                    case TokenType.Decrement:
                        RequireScalarStep(unary, operandType);
                        return (unary.Operator == TokenType.Increment ? "++" : "--") + operand;
                    default:
                        throw Error(unary, $"unary operator '{unary.Operator}' is not supported in GLSL.");
                }
            }
            case PostfixExpression postfix:
                RequireScalarStep(postfix, _types.TypeOf(postfix.Left));
                return Expr(postfix.Left) + (postfix.Operator == TokenType.Increment ? "++" : "--");
            case BinaryExpression binary:
                return BinaryText(binary);
            case TernaryExpression ternary:
                return $"({Expr(ternary.Condition)} ? {Expr(ternary.ThenBranch)} : {Expr(ternary.ElseBranch)})";
            case MemberAccessExpression member:
            {
                var target = Expr(member.Object);
                return member.Member.Length == 1
                    ? $"{target}[{SwizzleIndex(member.Member[0])}]"
                    : $"{RuntimeAlias}.swizzle({target}, {SwizzleIndexes(member.Member)})";
            }
            case ArrayAccessExpression arrayAccess:
                return $"{Expr(arrayAccess.Array)}[{Expr(arrayAccess.Index)}]";
            case FunctionCallExpression call:
                return CallText(call);
            default:
                throw Error(expression, $"expression '{expression.GetType().Name}' is not part of the GLSL shader subset.");
        }
    }

    private void RequireScalarStep(Expression node, GlslType type)
    {
        if (!type.IsScalar)
            throw Error(node, $"++ / -- on {type} is not supported in a host-called function; write v += 1.0.");
    }

    private string BinaryText(BinaryExpression binary)
    {
        var leftType = _types.TypeOf(binary.Left);
        var rightType = _types.TypeOf(binary.Right);
        var left = Expr(binary.Left);
        var right = Expr(binary.Right);
        switch (binary.Operator)
        {
            case TokenType.Plus:
            case TokenType.Minus:
            case TokenType.Multiply:
            case TokenType.Divide:
            case TokenType.Modulo:
                return Arithmetic(binary.Operator, leftType, left, rightType, right);
            case TokenType.LessThan:
            case TokenType.GreaterThan:
            case TokenType.LessThanOrEqual:
            case TokenType.GreaterThanOrEqual:
            case TokenType.And:
            case TokenType.Or:
                return $"({left} {OperatorText(binary.Operator)} {right})";
            case TokenType.Equal:
            case TokenType.NotEqual:
            {
                var equal = binary.Operator == TokenType.Equal;
                return leftType.IsScalar
                    ? $"({left} {(equal ? "===" : "!==")} {right})"
                    : $"{RuntimeAlias}.{(equal ? "eq" : "ne")}({left}, {right})";
            }
            default:
                throw Error(binary, $"binary operator '{binary.Operator}' is not supported in GLSL.");
        }
    }

    /// <summary><c>+ - * / %</c> by operand type: native on scalars, a runtime helper with a vector, truncating on <c>int</c>.</summary>
    private static string Arithmetic(TokenType op, GlslType leftType, string left, GlslType rightType, string right)
    {
        var isInt = leftType.Base == GlslBaseType.Int;
        if (leftType.IsScalar && rightType.IsScalar)
        {
            return isInt && op == TokenType.Divide
                ? $"Math.trunc({left} / {right})"
                : $"({left} {OperatorText(op)} {right})";
        }

        var helper = op switch
        {
            TokenType.Plus => "add",
            TokenType.Minus => "sub",
            TokenType.Multiply => "mul",
            TokenType.Divide => isInt ? "idiv" : "div",
            _ => "imod"
        };
        return $"{RuntimeAlias}.{helper}({left}, {right})";
    }

    private string CallText(FunctionCallExpression call)
    {
        if (call.Callee is MemberAccessExpression { Member: "length" } lengthOf && call.Arguments.Count == 0)
            return $"{Expr(lengthOf.Object)}.length";

        var name = call.Callee switch
        {
            IdentifierExpression identifier => identifier.Name,
            MemberAccessExpression math => math.Member,
            _ => throw Error(call, "only bare GLSL calls (and math.* aliases) are allowed in @shader() functions.")
        };
        var args = call.Arguments.Select(Expr).ToList();
        var argTypes = call.Arguments.Select(_types.TypeOf).ToList();
        if (call.Callee is IdentifierExpression && _types.CalleeParameters.TryGetValue(name, out var calleeParameters))
        {
            // A scalar `out` argument is handed over as its box, not as the number in it.
            for (var i = 0; i < args.Count && i < calleeParameters.Count; i++)
            {
                if (calleeParameters[i].Qualifier == "out" && calleeParameters[i].Type.IsScalar)
                    args[i] = ((IdentifierExpression)call.Arguments[i]).Name;
            }
        }

        var joined = string.Join(", ", args);

        if (call.Callee is IdentifierExpression)
        {
            if (_types.Callees.Contains(name, StringComparer.Ordinal))
            {
                // A callee that reads its material gets it passed on.
                return _binding != null && _binding.NeedsBinding(name)
                    ? $"{BoundName(name)}({string.Join(", ", new[] { MaterialParameter }.Concat(args))})"
                    : $"{name}({joined})";
            }

            if (GlslType.TryParse(name, out var constructed) && constructed.Base is not (GlslBaseType.Void or GlslBaseType.Opaque))
            {
                // float(f), float(i) and int(i) are the number itself; every other conversion is the runtime's.
                var isIdentity = constructed.IsScalar &&
                    (argTypes[0] == constructed || (constructed == GlslType.Float && argTypes[0] == GlslType.Int));
                return isIdentity ? args[0] : $"{RuntimeAlias}.{name}({joined})";
            }
        }

        if (NoHostMeaning.Contains(name) && !(name == "texelFetch" && _binding != null))
            throw Error(call, $"{name}() has no host meaning: it needs a fragment or a texture bound to a material.");

        if (argTypes.All(type => type.IsScalar))
        {
            if (name == "atan")
                return args.Count == 2 ? $"Math.atan2({joined})" : $"Math.atan({joined})";
            if (NativeScalarCalls.TryGetValue(name, out var native))
                return $"{native}({joined})";
        }

        return $"{RuntimeAlias}.{name}({joined})";
    }

    private string LiteralText(LiteralExpression literal) => literal.Value switch
    {
        bool value => value ? "true" : "false",
        int value => value.ToString(CultureInfo.InvariantCulture),
        long value => value.ToString(CultureInfo.InvariantCulture),
        float value => FloatText(value),
        double value => FloatText(value),
        decimal value => FloatText((double)value),
        _ => throw Error(literal, "this literal is not valid in a shader.")
    };

    // The shortest text that reads back as the same double, with a decimal point so a float stays
    // recognizable in the generated code.
    private static string FloatText(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.') || text.Contains('E') || text.Contains('e') ? text : text + ".0";
    }

    private string SwizzleIndexes(string swizzle) => $"[{string.Join(", ", swizzle.Select(SwizzleIndex))}]";

    private static int SwizzleIndex(char component) => component switch
    {
        'x' or 'r' or 's' => 0,
        'y' or 'g' or 't' => 1,
        'z' or 'b' or 'p' => 2,
        _ => 3
    };

    private static string OperatorText(TokenType op) => op switch
    {
        TokenType.Plus => "+",
        TokenType.Minus => "-",
        TokenType.Multiply => "*",
        TokenType.Divide => "/",
        TokenType.Modulo => "%",
        TokenType.LessThan => "<",
        TokenType.GreaterThan => ">",
        TokenType.LessThanOrEqual => "<=",
        TokenType.GreaterThanOrEqual => ">=",
        TokenType.And => "&&",
        TokenType.Or => "||",
        _ => throw new NotSupportedException($"operator '{op}' has no host emit.")
    };

    // ---- Lines and errors -----------------------------------------------------------------

    private void RequireHostType(GlslType type, Node at, string what)
    {
        if (type.Base == GlslBaseType.Opaque)
            throw Error(at, $"type {type} of {what} has no host value.");
    }

    private void Line(Node? source, string text) => _lines.Add(new GlslHostLine(source, _indent, text));

    private NotSupportedException Error(Node node, string message) => GlslHostDiagnostics.Error(_function, node, message);
}
