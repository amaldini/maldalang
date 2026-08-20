// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Globalization;
using System.Text;
using MaldaLang;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

public sealed class GlslCompileRequest
{
    public string? Header { get; init; }
    public IReadOnlyList<string> Varyings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Uniforms { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Consts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<FunctionDeclaration> Functions { get; init; } = Array.Empty<FunctionDeclaration>();
    public string? MainFunctionName { get; init; }
}

/// <summary>
/// Compiles a typed MALDA subset used by <c>@shader()</c> functions into GLSL source.
/// This is a JavaScript-backend compile-time path, not a new execution backend.
/// </summary>
public static class GlslTranspiler
{
    private static readonly HashSet<string> MathMembers = new(StringComparer.Ordinal)
    {
        "sin", "cos", "tan", "asin", "acos", "atan",
        "sqrt", "abs", "min", "max", "floor", "ceil",
        "log", "exp", "pow", "clamp"
    };

    public static string Compile(GlslCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.Header))
        {
            output.AppendLine(request.Header.TrimEnd());
        }

        AppendPrefixedDeclarations(output, "varying", request.Varyings);
        AppendPrefixedDeclarations(output, "uniform", request.Uniforms);
        AppendPrefixedDeclarations(output, "const", request.Consts);

        if (output.Length > 0)
            output.AppendLine();

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in request.Functions)
        {
            var emitName = string.Equals(function.Name, request.MainFunctionName, StringComparison.Ordinal)
                ? "main"
                : function.Name;
            if (!emitted.Add(emitName))
                continue;

            TranspileFunction(function, output, emitName);
            output.AppendLine();
        }

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    public static string TranspileFunction(FunctionDeclaration function, string? emitName = null)
    {
        var output = new StringBuilder();
        TranspileFunction(function, output, emitName ?? function.Name);
        return output.ToString();
    }

    private static void AppendPrefixedDeclarations(StringBuilder output, string prefix, IReadOnlyList<string> declarations)
    {
        foreach (var declaration in declarations)
        {
            var text = declaration.Trim();
            if (text.Length == 0)
                continue;
            if (!text.EndsWith(';'))
                text += ";";
            if (text.StartsWith(prefix + " ", StringComparison.Ordinal))
                output.AppendLine(text);
            else
                output.AppendLine($"{prefix} {text}");
        }
    }

    private static void TranspileFunction(FunctionDeclaration function, StringBuilder output, string emitName)
    {
        var returnType = string.IsNullOrWhiteSpace(function.ReturnType) ? "void" : function.ReturnType.Trim();
        output.Append(returnType);
        output.Append(' ');
        output.Append(emitName);
        output.Append('(');

        for (var i = 0; i < function.Parameters.Count; i++)
        {
            if (i > 0)
                output.Append(", ");

            var hint = (function.ParameterTypeHints != null && i < function.ParameterTypeHints.Count)
                ? function.ParameterTypeHints[i]
                : null;
            output.Append(FormatParameterType(hint, function.Parameters[i], function));
            output.Append(' ');
            output.Append(function.Parameters[i]);
        }

        output.AppendLine(") {");
        TranspileStatements(function.Body.Statements, output, indent: 1);
        output.AppendLine("}");
    }

    private static string FormatParameterType(string? hint, string parameterName, FunctionDeclaration function)
    {
        if (string.IsNullOrWhiteSpace(hint))
        {
            throw Unsupported(function, $"@shader() parameter '{parameterName}' needs a GLSL type hint.");
        }

        var type = hint.Trim();
        if (type.StartsWith("out ", StringComparison.Ordinal))
            return type;

        return type;
    }

    private static void TranspileStatements(IEnumerable<Statement> statements, StringBuilder output, int indent)
    {
        foreach (var statement in statements)
            TranspileStatement(statement, output, indent);
    }

    private static void TranspileStatement(Statement statement, StringBuilder output, int indent)
    {
        switch (statement)
        {
            case BlockStatement block:
                WriteIndent(output, indent);
                output.AppendLine("{");
                TranspileStatements(block.Statements, output, indent + 1);
                WriteIndent(output, indent);
                output.AppendLine("}");
                break;
            case VarDeclStatement varDecl:
                WriteIndent(output, indent);
                output.Append(FormatLocalDeclaration(varDecl));
                output.Append(" = ");
                output.Append(TranspileExpression(varDecl.Initializer));
                output.AppendLine(";");
                break;
            case AssignmentStatement assignment:
                WriteIndent(output, indent);
                output.Append(TranspileExpression(assignment.Target));
                output.Append(' ');
                output.Append(MapAssignmentOperator(assignment.Operator, assignment));
                output.Append(' ');
                output.Append(TranspileExpression(assignment.Value));
                output.AppendLine(";");
                break;
            case IfStatement ifStatement:
                WriteIndent(output, indent);
                output.Append("if (");
                output.Append(TranspileExpression(ifStatement.Condition));
                output.AppendLine(")");
                TranspileEmbedded(ifStatement.ThenBranch, output, indent);
                if (ifStatement.ElseBranch != null)
                {
                    WriteIndent(output, indent);
                    output.AppendLine("else");
                    TranspileEmbedded(ifStatement.ElseBranch, output, indent);
                }
                break;
            case WhileStatement whileStatement:
                WriteIndent(output, indent);
                output.Append("while (");
                output.Append(TranspileExpression(whileStatement.Condition));
                output.AppendLine(")");
                TranspileEmbedded(whileStatement.Body, output, indent);
                break;
            case ForStatement forStatement:
                WriteIndent(output, indent);
                output.Append("for (");
                output.Append(TranspileForInitializer(forStatement.Initializer));
                output.Append("; ");
                output.Append(forStatement.Condition == null ? "" : TranspileExpression(forStatement.Condition));
                output.Append("; ");
                output.Append(forStatement.Increment == null ? "" : TranspileExpression(forStatement.Increment));
                output.AppendLine(")");
                TranspileEmbedded(forStatement.Body, output, indent);
                break;
            case ReturnStatement returnStatement:
                WriteIndent(output, indent);
                if (returnStatement.Value == null)
                {
                    output.AppendLine("return;");
                }
                else
                {
                    output.Append("return ");
                    output.Append(TranspileExpression(returnStatement.Value));
                    output.AppendLine(";");
                }
                break;
            case ExpressionStatement expressionStatement:
                WriteIndent(output, indent);
                output.Append(TranspileExpression(expressionStatement.Expression));
                output.AppendLine(";");
                break;
            case BreakStatement:
                WriteIndent(output, indent);
                output.AppendLine("break;");
                break;
            case ContinueStatement:
                WriteIndent(output, indent);
                output.AppendLine("continue;");
                break;
            default:
                throw Unsupported(statement, $"statement '{statement.GetType().Name}' is not part of the GLSL shader subset.");
        }
    }

    private static void TranspileEmbedded(Statement statement, StringBuilder output, int indent)
    {
        if (statement is BlockStatement)
        {
            TranspileStatement(statement, output, indent);
            return;
        }

        WriteIndent(output, indent);
        output.AppendLine("{");
        TranspileStatement(statement, output, indent + 1);
        WriteIndent(output, indent);
        output.AppendLine("}");
    }

    private static string FormatLocalDeclaration(VarDeclStatement varDecl)
    {
        if (string.IsNullOrWhiteSpace(varDecl.TypeHint))
        {
            throw Unsupported(varDecl, $"@shader() local '{varDecl.Name}' needs a GLSL type hint.");
        }

        var type = varDecl.TypeHint.Trim();
        return varDecl.IsConst ? $"const {type} {varDecl.Name}" : $"{type} {varDecl.Name}";
    }

    private static string TranspileForInitializer(Statement? initializer)
    {
        if (initializer == null)
            return "";
        if (initializer is VarDeclStatement varDecl)
            return $"{FormatLocalDeclaration(varDecl)} = {TranspileExpression(varDecl.Initializer)}";
        if (initializer is AssignmentStatement assignment)
            return $"{TranspileExpression(assignment.Target)} {MapAssignmentOperator(assignment.Operator, assignment)} {TranspileExpression(assignment.Value)}";
        throw Unsupported(initializer, "for-initializer must be a typed var or assignment in @shader() functions.");
    }

    private static string TranspileExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return TranspileLiteral(literal.Value);
            case IdentifierExpression identifier:
                return identifier.Name;
            case UnaryExpression unary:
                return unary.Operator switch
                {
                    TokenType.Not => $"(!{TranspileExpression(unary.Right)})",
                    TokenType.Minus => $"(-{TranspileExpression(unary.Right)})",
                    TokenType.Plus => TranspileExpression(unary.Right),
                    _ => throw Unsupported(unary, $"unary operator '{unary.Operator}' is not supported in GLSL.")
                };
            case BinaryExpression binary:
                return $"({TranspileExpression(binary.Left)} {MapBinaryOperator(binary)} {TranspileExpression(binary.Right)})";
            case TernaryExpression ternary:
                return $"({TranspileExpression(ternary.Condition)} ? {TranspileExpression(ternary.ThenBranch)} : {TranspileExpression(ternary.ElseBranch)})";
            case MemberAccessExpression member:
                if (member.IsNullConditional)
                    throw Unsupported(member, "null-conditional access is not valid in GLSL.");
                return $"{TranspileExpression(member.Object)}.{member.Member}";
            case ArrayAccessExpression arrayAccess:
                if (arrayAccess.IsNullConditional)
                    throw Unsupported(arrayAccess, "null-conditional index is not valid in GLSL.");
                return $"{TranspileExpression(arrayAccess.Array)}[{TranspileExpression(arrayAccess.Index)}]";
            case FunctionCallExpression call:
                return TranspileCall(call);
            default:
                throw Unsupported(expression, $"expression '{expression.GetType().Name}' is not part of the GLSL shader subset.");
        }
    }

    private static string TranspileCall(FunctionCallExpression call)
    {
        var args = string.Join(", ", call.Arguments.Select(TranspileExpression));
        if (call.Callee is IdentifierExpression identifier)
            return $"{identifier.Name}({args})";

        if (call.Callee is MemberAccessExpression member &&
            member.Object is IdentifierExpression module &&
            (module.Name == "math" || module.Name == "Math"))
        {
            if (!MathMembers.Contains(member.Member))
            {
                throw Unsupported(call, $"math.{member.Member} has no GLSL mapping in the shader subset.");
            }

            return $"{member.Member}({args})";
        }

        throw Unsupported(call, "only bare GLSL calls (and math.* aliases) are allowed in @shader() functions.");
    }

    private static string MapBinaryOperator(BinaryExpression binary)
    {
        return binary.Operator switch
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
            TokenType.Equal => "==",
            TokenType.NotEqual => "!=",
            TokenType.And => "&&",
            TokenType.Or => "||",
            _ => throw Unsupported(binary, $"binary operator '{binary.Operator}' is not supported in GLSL.")
        };
    }

    private static string MapAssignmentOperator(TokenType op, Node node)
    {
        return op switch
        {
            TokenType.Assign => "=",
            TokenType.PlusAssign => "+=",
            TokenType.MinusAssign => "-=",
            TokenType.MultiplyAssign => "*=",
            TokenType.DivideAssign => "/=",
            _ => throw Unsupported(node, $"assignment operator '{op}' is not supported in GLSL.")
        };
    }

    private static string TranspileLiteral(object? value)
    {
        return value switch
        {
            null => throw new NotSupportedException("GLSL transpile: null literals are not valid in shaders."),
            bool booleanValue => booleanValue ? "true" : "false",
            string => throw new NotSupportedException("GLSL transpile: string literals are not valid in shaders."),
            int intValue => intValue.ToString(CultureInfo.InvariantCulture),
            long longValue => longValue.ToString(CultureInfo.InvariantCulture),
            float floatValue => FormatFloat(floatValue),
            double doubleValue => FormatFloat(doubleValue),
            decimal decimalValue => FormatFloat(decimalValue),
            _ => throw new NotSupportedException($"GLSL transpile: literal '{value.GetType().Name}' is not supported.")
        };
    }

    private static string FormatFloat(IFormattable value)
    {
        var text = value.ToString("G17", CultureInfo.InvariantCulture) ?? "0.0";
        if (text.Contains('e', StringComparison.OrdinalIgnoreCase) || text.Contains('.'))
            return text;
        return text + ".0";
    }

    private static NotSupportedException Unsupported(Node node, string message)
    {
        return new NotSupportedException($"GLSL transpile ({node.Line}:{node.Column}): {message}");
    }

    private static void WriteIndent(StringBuilder output, int indent)
    {
        output.Append(' ', indent * 4);
    }
}
