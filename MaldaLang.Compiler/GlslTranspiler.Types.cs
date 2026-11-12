// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using MaldaLang;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public enum GlslBaseType
{
    Void,
    Bool,
    Int,
    Float,
    /// <summary>A type the pass can name but not compute with: <c>mat3</c>, <c>sampler2D</c>, <c>uint</c>.</summary>
    Opaque
}

/// <summary>
/// A type of the <c>@shader()</c> subset: a scalar, a <c>vec*</c> / <c>ivec*</c> / <c>bvec*</c>,
/// a fixed array of those, or an opaque GLSL type kept by name.
/// </summary>
public readonly record struct GlslType(GlslBaseType Base, int Components, int ArrayLength = 0, string? OpaqueName = null)
{
    /// <summary><see cref="ArrayLength"/> of an array whose size is not an integer literal (<c>float[COUNT]</c>).</summary>
    public const int UnknownArrayLength = -1;

    public static readonly GlslType Void = new(GlslBaseType.Void, 0);
    public static readonly GlslType Bool = new(GlslBaseType.Bool, 1);
    public static readonly GlslType Int = new(GlslBaseType.Int, 1);
    public static readonly GlslType Float = new(GlslBaseType.Float, 1);

    public static GlslType Vec(int components) => new(GlslBaseType.Float, components);
    public static GlslType IVec(int components) => new(GlslBaseType.Int, components);
    public static GlslType BVec(int components) => new(GlslBaseType.Bool, components);
    public static GlslType Opaque(string name) => new(GlslBaseType.Opaque, 0, 0, name);

    public bool IsArray => ArrayLength != 0;
    public bool IsScalar => !IsArray && Components == 1;
    public bool IsVector => !IsArray && Components >= 2;
    public bool IsNumeric => !IsArray && Base is GlslBaseType.Float or GlslBaseType.Int && Components >= 1;

    /// <summary>The element type of an array, or the type itself.</summary>
    public GlslType Element => this with { ArrayLength = 0 };

    /// <summary>The scalar of the same base type (<c>vec3</c> → <c>float</c>).</summary>
    public GlslType Scalar => new(Base, 1);

    public GlslType WithComponents(int components) => new(Base, components);

    public static bool TryParse(string? text, out GlslType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var name = text.Trim();
        var arrayLength = 0;
        var bracket = name.IndexOf('[');
        if (bracket >= 0)
        {
            if (!name.EndsWith(']') || name.IndexOf('[', bracket + 1) >= 0)
                return false;
            var size = name[(bracket + 1)..^1].Trim();
            arrayLength = int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : UnknownArrayLength;
            name = name[..bracket].Trim();
        }

        GlslType element;
        switch (name)
        {
            case "void": element = Void; break;
            case "bool": element = Bool; break;
            case "int": element = Int; break;
            case "float": element = Float; break;
            case "vec2" or "vec3" or "vec4": element = Vec(name[^1] - '0'); break;
            case "ivec2" or "ivec3" or "ivec4": element = IVec(name[^1] - '0'); break;
            case "bvec2" or "bvec3" or "bvec4": element = BVec(name[^1] - '0'); break;
            default:
                if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') ||
                    !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
                element = Opaque(name);
                break;
        }

        if (arrayLength != 0 && element.Base == GlslBaseType.Void)
            return false;

        type = element with { ArrayLength = arrayLength };
        return true;
    }

    public override string ToString()
    {
        var name = Base switch
        {
            GlslBaseType.Void => "void",
            GlslBaseType.Opaque => OpaqueName ?? "?",
            GlslBaseType.Bool => Components == 1 ? "bool" : $"bvec{Components}",
            GlslBaseType.Int => Components == 1 ? "int" : $"ivec{Components}",
            _ => Components == 1 ? "float" : $"vec{Components}"
        };
        return ArrayLength switch
        {
            0 => name,
            UnknownArrayLength => name + "[]",
            _ => $"{name}[{ArrayLength.ToString(CultureInfo.InvariantCulture)}]"
        };
    }
}

/// <summary>A typed <c>@shader()</c> parameter. <see cref="Qualifier"/> is <c>""</c>, <c>"out"</c> or <c>"inout"</c>.</summary>
public readonly record struct GlslParameter(string Name, GlslType Type, string Qualifier);

/// <summary>The result of the type pass over one <c>@shader()</c> function.</summary>
public sealed class GlslFunctionTypes
{
    private readonly Dictionary<Expression, GlslType> _expressions;
    private readonly Dictionary<VarDeclStatement, GlslType> _locals;

    internal GlslFunctionTypes(
        FunctionDeclaration function,
        GlslType returnType,
        IReadOnlyList<GlslParameter> parameters,
        Dictionary<Expression, GlslType> expressions,
        Dictionary<VarDeclStatement, GlslType> locals,
        IReadOnlyList<string> callees)
    {
        Function = function;
        ReturnType = returnType;
        Parameters = parameters;
        _expressions = expressions;
        _locals = locals;
        Callees = callees;
    }

    public FunctionDeclaration Function { get; }
    public GlslType ReturnType { get; }
    public IReadOnlyList<GlslParameter> Parameters { get; }

    /// <summary>Other <c>@shader()</c> functions this one calls, in first-call order.</summary>
    public IReadOnlyList<string> Callees { get; }

    public GlslType TypeOf(Expression expression) =>
        _expressions.TryGetValue(expression, out var type)
            ? type
            : throw new KeyNotFoundException("The expression is not part of the analyzed @shader() function.");

    public GlslType TypeOf(VarDeclStatement local) =>
        _locals.TryGetValue(local, out var type)
            ? type
            : throw new KeyNotFoundException("The declaration is not part of the analyzed @shader() function.");
}

public static partial class GlslTranspiler
{
    /// <summary>
    /// Infers the type of every expression of an <c>@shader()</c> function from its declared hints.
    /// The rules are those of GLSL ES: no implicit <c>int</c> → <c>float</c> conversion, scalar
    /// broadcast on vector arithmetic, <c>int / int</c> stays <c>int</c>.
    /// </summary>
    /// <param name="function">The function to analyze.</param>
    /// <param name="shaderFunctions">The <c>@shader()</c> functions it may call, by name.</param>
    /// <param name="globals">Names visible outside the function (uniforms, consts), when the caller has them.</param>
    /// <exception cref="NotSupportedException">The body does not type-check; the message names the function.</exception>
    public static GlslFunctionTypes AnalyzeTypes(
        FunctionDeclaration function,
        IReadOnlyDictionary<string, FunctionDeclaration>? shaderFunctions = null,
        IReadOnlyDictionary<string, GlslType>? globals = null)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new TypePass(function, shaderFunctions, globals).Run();
    }

    private sealed class TypePass
    {
        private static readonly string[] SwizzleSets = ["xyzw", "rgba", "stpq"];

        private readonly FunctionDeclaration _function;
        private readonly IReadOnlyDictionary<string, FunctionDeclaration>? _shaderFunctions;
        private readonly IReadOnlyDictionary<string, GlslType>? _globals;
        private readonly Dictionary<Expression, GlslType> _expressions = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<VarDeclStatement, GlslType> _locals = new(ReferenceEqualityComparer.Instance);
        private readonly List<Dictionary<string, GlslType>> _scopes = new();
        private readonly List<string> _callees = new();
        private GlslType _returnType;

        public TypePass(
            FunctionDeclaration function,
            IReadOnlyDictionary<string, FunctionDeclaration>? shaderFunctions,
            IReadOnlyDictionary<string, GlslType>? globals)
        {
            _function = function;
            _shaderFunctions = shaderFunctions;
            _globals = globals;
        }

        public GlslFunctionTypes Run()
        {
            _returnType = ReadReturnType(_function, _function);
            var parameters = ReadParameters(_function, _function);

            PushScope();
            foreach (var parameter in parameters)
                Declare(parameter.Name, parameter.Type, _function);
            CheckStatements(_function.Body.Statements);
            PopScope();

            return new GlslFunctionTypes(_function, _returnType, parameters, _expressions, _locals, _callees);
        }

        // ---- Signatures -------------------------------------------------------------------

        private GlslType ReadReturnType(FunctionDeclaration function, Node at)
        {
            if (string.IsNullOrWhiteSpace(function.ReturnType))
                return GlslType.Void;
            if (!GlslType.TryParse(function.ReturnType, out var type))
                throw Error(at, $"'{function.ReturnType.Trim()}' is not a GLSL return type (function '{function.Name}').");
            return type;
        }

        private List<GlslParameter> ReadParameters(FunctionDeclaration function, Node at)
        {
            var parameters = new List<GlslParameter>(function.Parameters.Count);
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var name = function.Parameters[i];
                var hint = function.ParameterTypeHints != null && i < function.ParameterTypeHints.Count
                    ? function.ParameterTypeHints[i]?.Trim()
                    : null;
                if (string.IsNullOrEmpty(hint))
                    throw Error(at, $"parameter '{name}' of '{function.Name}' needs a GLSL type hint.");

                var qualifier = "";
                foreach (var candidate in new[] { "inout", "out", "in" })
                {
                    if (hint.StartsWith(candidate + " ", StringComparison.Ordinal))
                    {
                        qualifier = candidate == "in" ? "" : candidate;
                        hint = hint[candidate.Length..].Trim();
                        break;
                    }
                }

                if (!GlslType.TryParse(hint, out var type) || type.Base == GlslBaseType.Void)
                    throw Error(at, $"'{hint}' is not a GLSL type (parameter '{name}' of '{function.Name}').");
                parameters.Add(new GlslParameter(name, type, qualifier));
            }

            return parameters;
        }

        // ---- Statements -------------------------------------------------------------------

        private void CheckStatements(IEnumerable<Statement> statements)
        {
            foreach (var statement in statements)
                CheckStatement(statement);
        }

        private void CheckScoped(Statement statement)
        {
            PushScope();
            CheckStatement(statement);
            PopScope();
        }

        private void CheckStatement(Statement statement)
        {
            switch (statement)
            {
                case BlockStatement block:
                    PushScope();
                    CheckStatements(block.Statements);
                    PopScope();
                    break;
                case VarDeclStatement varDecl:
                    CheckVarDecl(varDecl);
                    break;
                case AssignmentStatement assignment:
                    CheckAssignment(assignment);
                    break;
                case IfStatement ifStatement:
                    RequireBool(ifStatement.Condition, "an if condition");
                    CheckScoped(ifStatement.ThenBranch);
                    if (ifStatement.ElseBranch != null)
                        CheckScoped(ifStatement.ElseBranch);
                    break;
                case WhileStatement whileStatement:
                    RequireBool(whileStatement.Condition, "a loop condition");
                    CheckScoped(whileStatement.Body);
                    break;
                case ForStatement forStatement:
                    PushScope();
                    if (forStatement.Initializer != null)
                        CheckStatement(forStatement.Initializer);
                    if (forStatement.Condition != null)
                        RequireBool(forStatement.Condition, "a loop condition");
                    if (forStatement.Increment != null)
                        Infer(forStatement.Increment);
                    CheckScoped(forStatement.Body);
                    PopScope();
                    break;
                case ReturnStatement returnStatement:
                    CheckReturn(returnStatement);
                    break;
                case ExpressionStatement expressionStatement:
                    Infer(expressionStatement.Expression);
                    break;
                case BreakStatement:
                case ContinueStatement:
                    break;
                default:
                    throw Error(statement, $"statement '{statement.GetType().Name}' is not part of the GLSL shader subset.");
            }
        }

        private void CheckVarDecl(VarDeclStatement varDecl)
        {
            if (string.IsNullOrWhiteSpace(varDecl.TypeHint))
                throw Error(varDecl, $"local '{varDecl.Name}' needs a GLSL type hint.");
            if (!GlslType.TryParse(varDecl.TypeHint, out var declared) || declared.Base == GlslBaseType.Void)
                throw Error(varDecl, $"'{varDecl.TypeHint.Trim()}' is not a GLSL type (local '{varDecl.Name}').");

            // `var s: float[8];` has no initializer: the parser stores a null literal.
            if (varDecl.Initializer is not LiteralExpression { Value: null })
            {
                var value = Infer(varDecl.Initializer);
                if (!Matches(value, declared))
                    throw Error(varDecl, $"cannot initialize '{varDecl.Name}' ({declared}) with {Describe(value)}.");
            }

            _locals[varDecl] = declared;
            Declare(varDecl.Name, declared, varDecl);
        }

        private void CheckAssignment(AssignmentStatement assignment)
        {
            var target = Infer(assignment.Target);
            var value = Infer(assignment.Value);
            GlslType result;
            switch (assignment.Operator)
            {
                case TokenType.Assign:
                    result = value;
                    break;
                case TokenType.PlusAssign:
                case TokenType.MinusAssign:
                case TokenType.MultiplyAssign:
                case TokenType.DivideAssign:
                    result = Arithmetic(assignment, target, value, assignment.Operator.ToString());
                    break;
                default:
                    throw Error(assignment, $"assignment operator '{assignment.Operator}' is not supported in GLSL.");
            }

            if (!Matches(result, target))
                throw Error(assignment, $"cannot assign {Describe(result)} to {Describe(target)}.");
        }

        private void CheckReturn(ReturnStatement returnStatement)
        {
            if (returnStatement.Value == null)
            {
                if (_returnType != GlslType.Void)
                    throw Error(returnStatement, $"return needs a {_returnType} value.");
                return;
            }

            var value = Infer(returnStatement.Value);
            if (!Matches(value, _returnType))
                throw Error(returnStatement, $"returns {Describe(value)}, but the function is declared -> {_returnType}.");
        }

        private void RequireBool(Expression condition, string what)
        {
            var type = Infer(condition);
            if (type != GlslType.Bool)
                throw Error(condition, $"{what} must be bool, not {Describe(type)}.");
        }

        // ---- Expressions ------------------------------------------------------------------

        private GlslType Infer(Expression expression)
        {
            var type = InferCore(expression);
            _expressions[expression] = type;
            return type;
        }

        private GlslType InferCore(Expression expression)
        {
            switch (expression)
            {
                case LiteralExpression literal:
                    return literal.Value switch
                    {
                        bool => GlslType.Bool,
                        int or long => GlslType.Int,
                        float or double or decimal => GlslType.Float,
                        null => throw Error(literal, "null literals are not valid in shaders."),
                        string => throw Error(literal, "string literals are not valid in shaders."),
                        _ => throw Error(literal, $"literal '{literal.Value.GetType().Name}' is not supported.")
                    };
                case IdentifierExpression identifier:
                    return Lookup(identifier);
                case UnaryExpression unary:
                    return InferUnary(unary, unary.Operator, Infer(unary.Right));
                case PostfixExpression postfix:
                    return InferUnary(postfix, postfix.Operator, Infer(postfix.Left));
                case BinaryExpression binary:
                    return InferBinary(binary);
                case TernaryExpression ternary:
                {
                    RequireBool(ternary.Condition, "a ?: condition");
                    var thenType = Infer(ternary.ThenBranch);
                    var elseType = Infer(ternary.ElseBranch);
                    if (thenType != elseType)
                        throw Error(ternary, $"the two branches of ?: differ: {Describe(thenType)} and {Describe(elseType)}.");
                    return thenType;
                }
                case MemberAccessExpression member:
                    if (member.IsNullConditional)
                        throw Error(member, "null-conditional access is not valid in GLSL.");
                    return InferSwizzle(member, Infer(member.Object));
                case ArrayAccessExpression arrayAccess:
                {
                    if (arrayAccess.IsNullConditional)
                        throw Error(arrayAccess, "null-conditional index is not valid in GLSL.");
                    var target = Infer(arrayAccess.Array);
                    var index = Infer(arrayAccess.Index);
                    if (index != GlslType.Int)
                        throw Error(arrayAccess, $"an index must be int, not {Describe(index)}.");
                    if (target.IsArray)
                        return target.Element;
                    if (target.IsVector)
                        return target.Scalar;
                    throw Error(arrayAccess, $"{Describe(target)} cannot be indexed.");
                }
                case FunctionCallExpression call:
                    return InferCall(call);
                default:
                    throw Error(expression, $"expression '{expression.GetType().Name}' is not part of the GLSL shader subset.");
            }
        }

        private GlslType InferUnary(Expression node, TokenType op, GlslType operand)
        {
            switch (op)
            {
                case TokenType.Not:
                    if (operand != GlslType.Bool)
                        throw Error(node, $"'!' needs a bool, not {Describe(operand)}.");
                    return GlslType.Bool;
                case TokenType.Minus:
                case TokenType.Plus:
                case TokenType.Increment:
                case TokenType.Decrement:
                    if (!operand.IsNumeric)
                        throw Error(node, $"'{OperatorText(op)}' needs an int or float value, not {Describe(operand)}.");
                    return operand;
                default:
                    throw Error(node, $"operator '{op}' is not supported in GLSL.");
            }
        }

        private GlslType InferBinary(BinaryExpression binary)
        {
            var left = Infer(binary.Left);
            var right = Infer(binary.Right);
            var op = OperatorText(binary.Operator);
            switch (binary.Operator)
            {
                case TokenType.Plus:
                case TokenType.Minus:
                case TokenType.Multiply:
                case TokenType.Divide:
                    return Arithmetic(binary, left, right, op);
                case TokenType.Modulo:
                    if (left.Base != GlslBaseType.Int || right.Base != GlslBaseType.Int)
                        throw Error(binary, $"'%' needs int operands, not {Describe(left)} and {Describe(right)}; use mod() for float.");
                    return Arithmetic(binary, left, right, op);
                case TokenType.LessThan:
                case TokenType.GreaterThan:
                case TokenType.LessThanOrEqual:
                case TokenType.GreaterThanOrEqual:
                    if (left != right || !left.IsScalar || !left.IsNumeric)
                        throw Error(binary, $"'{op}' needs two int or two float scalars, not {Describe(left)} and {Describe(right)}.");
                    return GlslType.Bool;
                case TokenType.Equal:
                case TokenType.NotEqual:
                    if (left != right || left.IsArray || left.Base is GlslBaseType.Void or GlslBaseType.Opaque)
                        throw Error(binary, $"'{op}' needs two values of the same type, not {Describe(left)} and {Describe(right)}.");
                    return GlslType.Bool;
                case TokenType.And:
                case TokenType.Or:
                    if (left != GlslType.Bool || right != GlslType.Bool)
                        throw Error(binary, $"'{op}' needs bool operands, not {Describe(left)} and {Describe(right)}.");
                    return GlslType.Bool;
                default:
                    throw Error(binary, $"binary operator '{binary.Operator}' is not supported in GLSL.");
            }
        }

        /// <summary><c>+ - * / %</c>: same type, or a vector with a scalar of the same base type.</summary>
        private GlslType Arithmetic(Node node, GlslType left, GlslType right, string op)
        {
            if (!left.IsNumeric || !right.IsNumeric)
                throw Error(node, $"'{op}' needs int or float operands, not {Describe(left)} and {Describe(right)}.");
            if (left.Base != right.Base)
            {
                throw Error(node,
                    $"'{op}' mixes {Describe(left)} and {Describe(right)}: GLSL ES has no implicit int to float conversion; write float(...) or int(...).");
            }

            if (left.Components == right.Components)
                return left;
            if (left.IsScalar)
                return right;
            if (right.IsScalar)
                return left;
            throw Error(node, $"'{op}' cannot combine {Describe(left)} and {Describe(right)}.");
        }

        private GlslType InferSwizzle(MemberAccessExpression member, GlslType target)
        {
            if (!target.IsVector)
                throw Error(member, $"'.{member.Member}' needs a vector, not {Describe(target)}.");

            var name = member.Member;
            if (name.Length is >= 1 and <= 4)
            {
                foreach (var set in SwizzleSets)
                {
                    var valid = true;
                    foreach (var c in name)
                    {
                        var index = set.IndexOf(c);
                        if (index < 0 || index >= target.Components)
                        {
                            valid = false;
                            break;
                        }
                    }

                    if (valid)
                        return target.WithComponents(name.Length);
                }
            }

            throw Error(member, $"'.{name}' is not a swizzle of {target}.");
        }

        private GlslType InferCall(FunctionCallExpression call)
        {
            string name;
            if (call.Callee is IdentifierExpression identifier)
            {
                name = identifier.Name;
            }
            else if (call.Callee is MemberAccessExpression { Object: IdentifierExpression { Name: "math" or "Math" } } math)
            {
                if (!MathMembers.Contains(math.Member))
                    throw Error(call, $"math.{math.Member} has no GLSL mapping in the shader subset.");
                name = math.Member;
            }
            else if (call.Callee is MemberAccessExpression { Member: "length" } lengthOf && call.Arguments.Count == 0)
            {
                var target = Infer(lengthOf.Object);
                if (!target.IsArray)
                    throw Error(call, $".length() needs an array, not {Describe(target)}.");
                return GlslType.Int;
            }
            else
            {
                throw Error(call, "only bare GLSL calls (and math.* aliases) are allowed in @shader() functions.");
            }

            var args = new List<GlslType>(call.Arguments.Count);
            foreach (var argument in call.Arguments)
                args.Add(Infer(argument));

            if (call.Callee is IdentifierExpression)
            {
                if (GlslType.TryParse(name, out var constructed) && constructed.Base is not (GlslBaseType.Void or GlslBaseType.Opaque))
                    return InferConstructor(call, constructed, args);

                if (_shaderFunctions != null && _shaderFunctions.TryGetValue(name, out var callee))
                    return InferUserCall(call, callee, args);
            }

            if (!GlslBuiltIns.IsKnown(name))
                throw Error(call, $"'{name}' is not an @shader() function of this file or a GLSL built-in the subset knows.");

            return GlslBuiltIns.Resolve(name, args)
                ?? throw Error(call, $"no overload of {name}() takes ({string.Join(", ", args)}).");
        }

        private GlslType InferConstructor(FunctionCallExpression call, GlslType constructed, List<GlslType> args)
        {
            if (args.Count == 0)
                throw Error(call, $"{constructed}() needs at least one argument.");

            foreach (var arg in args)
            {
                if (arg.IsArray || arg.Components < 1 || arg.Base is GlslBaseType.Void or GlslBaseType.Opaque)
                    throw Error(call, $"{constructed}() cannot take {Describe(arg)}.");
            }

            // One scalar fills every component; a scalar constructor reads the first component.
            if (constructed.IsScalar)
            {
                if (args.Count != 1)
                    throw Error(call, $"{constructed}() takes one argument.");
                return constructed;
            }

            if (args.Count == 1 && args[0].IsScalar)
                return constructed;

            var before = 0;
            for (var i = 0; i < args.Count; i++)
            {
                if (before >= constructed.Components)
                    throw Error(call, $"{constructed}() has too many arguments: ({string.Join(", ", args)}).");
                before += args[i].Components;
            }

            if (before < constructed.Components)
            {
                throw Error(call,
                    $"{constructed}() needs {constructed.Components} components, ({string.Join(", ", args)}) gives {before}.");
            }

            return constructed;
        }

        private GlslType InferUserCall(FunctionCallExpression call, FunctionDeclaration callee, List<GlslType> args)
        {
            var parameters = ReadParameters(callee, call);
            if (parameters.Count != args.Count)
                throw Error(call, $"{callee.Name}() takes {parameters.Count} argument(s), not {args.Count}.");

            for (var i = 0; i < args.Count; i++)
            {
                if (!Matches(args[i], parameters[i].Type))
                {
                    throw Error(call.Arguments[i],
                        $"argument '{parameters[i].Name}' of {callee.Name}() is {parameters[i].Type}, not {Describe(args[i])}.");
                }
            }

            if (!_callees.Contains(callee.Name, StringComparer.Ordinal))
                _callees.Add(callee.Name);
            return ReadReturnType(callee, call);
        }

        // ---- Scopes and errors ------------------------------------------------------------

        private void PushScope() => _scopes.Add(new Dictionary<string, GlslType>(StringComparer.Ordinal));

        private void PopScope() => _scopes.RemoveAt(_scopes.Count - 1);

        private void Declare(string name, GlslType type, Node at)
        {
            if (!_scopes[^1].TryAdd(name, type))
                throw Error(at, $"'{name}' is declared twice in the same scope.");
        }

        private GlslType Lookup(IdentifierExpression identifier)
        {
            for (var i = _scopes.Count - 1; i >= 0; i--)
            {
                if (_scopes[i].TryGetValue(identifier.Name, out var local))
                    return local;
            }

            if (_globals != null && _globals.TryGetValue(identifier.Name, out var global))
                return global;

            throw Error(identifier,
                $"'{identifier.Name}' is not a parameter or local of the function (uniforms, consts, varyings and gl_* names have no type here).");
        }

        private static string Describe(GlslType type) => type.ToString();

        /// <summary>Type equality, where an array sized by a const name matches any length of the same element.</summary>
        private static bool Matches(GlslType actual, GlslType expected)
        {
            if (actual == expected)
                return true;
            return actual.IsArray && expected.IsArray && actual.Element == expected.Element &&
                (actual.ArrayLength == GlslType.UnknownArrayLength || expected.ArrayLength == GlslType.UnknownArrayLength);
        }

        private static string OperatorText(TokenType op) => op switch
        {
            TokenType.Plus or TokenType.PlusAssign => "+",
            TokenType.Minus or TokenType.MinusAssign => "-",
            TokenType.Multiply or TokenType.MultiplyAssign => "*",
            TokenType.Divide or TokenType.DivideAssign => "/",
            TokenType.Modulo => "%",
            TokenType.LessThan => "<",
            TokenType.GreaterThan => ">",
            TokenType.LessThanOrEqual => "<=",
            TokenType.GreaterThanOrEqual => ">=",
            TokenType.Equal => "==",
            TokenType.NotEqual => "!=",
            TokenType.And => "&&",
            TokenType.Or => "||",
            TokenType.Increment => "++",
            TokenType.Decrement => "--",
            _ => op.ToString()
        };

        private NotSupportedException Error(Node node, string message) =>
            new($"GLSL type pass ({node.Line}:{node.Column}) in @shader() function '{_function.Name}': {message}");
    }
}

/// <summary>
/// Signatures of the GLSL built-ins the <c>@shader()</c> subset maps. <c>genF</c> is
/// <c>float</c> / <c>vec2..4</c>, <c>genI</c> is <c>int</c> / <c>ivec2..4</c>.
/// </summary>
public static class GlslBuiltIns
{
    private static readonly HashSet<string> FloatUnary = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "asin", "acos", "sinh", "cosh", "tanh", "radians", "degrees",
        "exp", "log", "exp2", "log2", "sqrt", "inversesqrt",
        "floor", "ceil", "fract", "trunc", "round", "normalize",
        "dFdx", "dFdy", "fwidth"
    };

    private static readonly HashSet<string> Other = new(StringComparer.Ordinal)
    {
        "abs", "sign", "atan", "pow", "mod", "min", "max", "clamp", "mix", "step", "smoothstep",
        "length", "distance", "dot", "cross", "reflect", "refract", "faceforward",
        "lessThan", "lessThanEqual", "greaterThan", "greaterThanEqual", "equal", "notEqual",
        "any", "all", "not",
        "texelFetch", "texture", "texture2D"
    };

    public static bool IsKnown(string name) => FloatUnary.Contains(name) || Other.Contains(name);

    /// <summary>The result type of <paramref name="name"/> for those argument types, or null when no overload fits.</summary>
    public static GlslType? Resolve(string name, IReadOnlyList<GlslType> args)
    {
        var n = args.Count;
        GlslType A(int i) => args[i];

        if (FloatUnary.Contains(name))
            return n == 1 && IsGenF(A(0)) ? A(0) : null;

        switch (name)
        {
            case "abs":
            case "sign":
                return n == 1 && (IsGenF(A(0)) || IsGenI(A(0))) ? A(0) : null;
            case "atan":
                if (n == 1)
                    return IsGenF(A(0)) ? A(0) : null;
                return n == 2 && IsGenF(A(0)) && A(1) == A(0) ? A(0) : null;
            case "pow":
            case "reflect":
                return n == 2 && IsGenF(A(0)) && A(1) == A(0) ? A(0) : null;
            case "mod":
                return n == 2 && IsGenF(A(0)) && SameOrScalar(A(1), A(0)) ? A(0) : null;
            case "min":
            case "max":
                return n == 2 && (IsGenF(A(0)) || IsGenI(A(0))) && SameOrScalar(A(1), A(0)) ? A(0) : null;
            case "clamp":
                if (n != 3 || !(IsGenF(A(0)) || IsGenI(A(0))) || A(1) != A(2))
                    return null;
                return SameOrScalar(A(1), A(0)) ? A(0) : null;
            case "mix":
                return n == 3 && IsGenF(A(0)) && A(1) == A(0) && SameOrScalar(A(2), A(0)) ? A(0) : null;
            case "step":
                return n == 2 && IsGenF(A(1)) && SameOrScalar(A(0), A(1)) ? A(1) : null;
            case "smoothstep":
                return n == 3 && IsGenF(A(2)) && A(0) == A(1) && SameOrScalar(A(0), A(2)) ? A(2) : null;
            case "length":
                return n == 1 && IsGenF(A(0)) ? GlslType.Float : null;
            case "distance":
            case "dot":
                return n == 2 && IsGenF(A(0)) && A(1) == A(0) ? GlslType.Float : null;
            case "cross":
                return n == 2 && A(0) == GlslType.Vec(3) && A(1) == A(0) ? A(0) : null;
            case "refract":
                return n == 3 && IsGenF(A(0)) && A(1) == A(0) && A(2) == GlslType.Float ? A(0) : null;
            case "faceforward":
                return n == 3 && IsGenF(A(0)) && A(1) == A(0) && A(2) == A(0) ? A(0) : null;
            case "lessThan":
            case "lessThanEqual":
            case "greaterThan":
            case "greaterThanEqual":
                return n == 2 && A(0).IsVector && A(0).IsNumeric && A(1) == A(0) ? GlslType.BVec(A(0).Components) : null;
            case "equal":
            case "notEqual":
                return n == 2 && A(0).IsVector && A(0).Base != GlslBaseType.Opaque && A(1) == A(0)
                    ? GlslType.BVec(A(0).Components)
                    : null;
            case "any":
            case "all":
                return n == 1 && IsBVec(A(0)) ? GlslType.Bool : null;
            case "not":
                return n == 1 && IsBVec(A(0)) ? A(0) : null;
            case "texelFetch":
                return n == 3 && IsSampler2D(A(0)) && A(1) == GlslType.IVec(2) && A(2) == GlslType.Int ? GlslType.Vec(4) : null;
            case "texture":
            case "texture2D":
                return n == 2 && IsSampler2D(A(0)) && A(1) == GlslType.Vec(2) ? GlslType.Vec(4) : null;
            default:
                return null;
        }
    }

    private static bool IsGenF(GlslType type) => !type.IsArray && type.Base == GlslBaseType.Float && type.Components >= 1;

    private static bool IsGenI(GlslType type) => !type.IsArray && type.Base == GlslBaseType.Int && type.Components >= 1;

    private static bool IsBVec(GlslType type) => type.IsVector && type.Base == GlslBaseType.Bool;

    private static bool IsSampler2D(GlslType type) => type.Base == GlslBaseType.Opaque && type.OpaqueName == "sampler2D";

    /// <summary>The scalar-broadcast rule: the same type as <paramref name="full"/>, or its scalar.</summary>
    private static bool SameOrScalar(GlslType candidate, GlslType full) => candidate == full || candidate == full.Scalar;
}
