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
    private void EmitVariantConstructors(TypeDeclaration declaration)
    {
        foreach (var ctor in declaration.Constructors)
        {
            var functionName = EscapeIdentifier(ctor.Name);
            var parameterList = string.Join(", ", ctor.ParameterNames.Select(EscapeIdentifier));
            var escapedTag = EscapeString(ctor.Name);

            EmitLine($"var {functionName} = function({parameterList}) {{");
            _indentLevel++;
            EmitLine($"if (arguments.length !== {ctor.ParameterNames.Count}) {{");
            _indentLevel++;
            EmitLine($"throw new Error(\"Variant constructor {escapedTag} expects {ctor.ParameterNames.Count} argument(s) but got \" + arguments.length + \".\");");
            _indentLevel--;
            EmitLine("}");
            EmitLine("return mlRuntime.variant(\"" + escapedTag + "\", Array.from(arguments));");
            _indentLevel--;
            EmitLine("};");
        }

        var nsName = EscapeIdentifier(declaration.TypeName);
        var nsMembers = string.Join(
            ", ",
            declaration.Constructors.Select(c =>
                $"{EscapeIdentifier(c.Name)}: {EscapeIdentifier(c.Name)}"));
        EmitLine($"var {nsName} = {{ {nsMembers} }};");

        var constructorSpecs = declaration.Constructors
            .Select(c => new Dictionary<string, object?>
            {
                ["tag"] = c.Name,
                ["params"] = c.ParameterNames.ToArray()
            })
            .ToList();
        var ctorJson = JsonSerializer.Serialize(constructorSpecs);
        EmitLine($"mlRuntime.schema.registerSumType({TranspileLiteral(declaration.TypeName)}, {ctorJson});");
    }

    private string TranspileObjectLikeLiteral(List<(Expression Key, Expression Value)> entries)
    {
        var properties = entries.Select(entry =>
            $"[{TranspileExpression(entry.Key)}]: {TranspileExpression(entry.Value)}");
        return "{ " + string.Join(", ", properties) + " }";
    }

    private string TranspileMatchExpression(MatchExpression match)
    {
        var valueVar = $"__matchValue{_matchTempCounter}";
        _matchTempCounter++;

        var builder = new StringBuilder();
        builder.Append("(() => { ");
        builder.Append("const ");
        builder.Append(valueVar);
        builder.Append(" = ");
        builder.Append(TranspileExpression(match.Value));
        builder.Append("; ");

        for (int i = 0; i < match.Cases.Count; i++)
        {
            var matchCase = match.Cases[i];
            var pattern = ResolveBareConstructorPattern(matchCase.Pattern);
            var resultVar = $"__matchResult{_matchTempCounter}_{i}";
            builder.Append("{ const ");
            builder.Append(resultVar);
            builder.Append(" = mlRuntime.matchPattern(");
            builder.Append(TranspilePatternDescriptor(pattern));
            builder.Append(", ");
            builder.Append(valueVar);
            builder.Append("); if (");
            builder.Append(resultVar);
            builder.Append(".matched) { ");

            foreach (var binding in CollectPatternBindings(pattern))
            {
                builder.Append("const ");
                builder.Append(EscapeIdentifier(binding));
                builder.Append(" = ");
                builder.Append(resultVar);
                builder.Append(".bindings[\"");
                builder.Append(EscapeString(binding));
                builder.Append("\"]; ");
            }

            if (matchCase.Guard != null)
            {
                builder.Append("if (");
                builder.Append(TranspileCondition(matchCase.Guard));
                builder.Append(") { ");
                builder.Append(TranspileMatchBody(matchCase.Body));
                builder.Append(" }");
            }
            else
            {
                builder.Append(TranspileMatchBody(matchCase.Body));
            }

            builder.Append(" } } ");
        }

        if (match.DefaultCase != null)
        {
            builder.Append(TranspileMatchBody(match.DefaultCase));
        }
        else
        {
            builder.Append("throw new Error(\"Match expression had no matching case and no default case.\");");
        }

        builder.Append(" })()");
        return builder.ToString();
    }

    private string TranspileMatchBody(Statement body)
    {
        if (body is ExpressionStatement expressionStatement)
        {
            return "return " + TranspileExpression(expressionStatement.Expression) + ";";
        }

        if (body is BlockStatement blockStatement)
        {
            if (blockStatement.Statements.Count == 0)
            {
                return "return null;";
            }

            var builder = new StringBuilder();
            for (int i = 0; i < blockStatement.Statements.Count - 1; i++)
            {
                builder.Append(TranspileStatementInline(blockStatement.Statements[i]));
                builder.Append(' ');
            }

            var lastStatement = blockStatement.Statements[^1];
            if (lastStatement is ExpressionStatement lastExpressionStatement)
            {
                builder.Append("return ");
                builder.Append(TranspileExpression(lastExpressionStatement.Expression));
                builder.Append(';');
            }
            else
            {
                builder.Append(TranspileStatementInline(lastStatement));
                builder.Append(" return null;");
            }

            return builder.ToString();
        }

        return TranspileStatementInline(body) + " return null;";
    }

    private string TranspileStatementInline(Statement statement)
    {
        var inlineTranspiler = new JsTranspiler();
        inlineTranspiler.CopyTranspileStateFrom(this);
        inlineTranspiler._indentLevel = 0;
        inlineTranspiler._matchTempCounter = _matchTempCounter;
        inlineTranspiler.TranspileStatement(statement);
        _matchTempCounter = inlineTranspiler._matchTempCounter;
        return NormalizeInlineCode(inlineTranspiler._output.ToString());
    }

    private static string NormalizeInlineCode(string code)
    {
        return string.Join(" ", code
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()));
    }

    private Pattern ResolveBareConstructorPattern(Pattern pattern)
    {
        if (pattern is IdentifierPattern id &&
            _variantConstructorArities.TryGetValue(id.Name, out var arity))
        {
            return VariantPattern.WithImplicitWildcards(id.Name, arity, id.Line, id.Column);
        }

        return pattern;
    }

    private string TranspilePatternDescriptor(Pattern pattern)
    {
        switch (pattern)
        {
            case LiteralPattern literalPattern:
                return "{ type: \"Literal\", value: " + TranspileLiteral(literalPattern.Value) + " }";
            case IdentifierPattern identifierPattern:
                if (IsConstIdentifierPattern(identifierPattern.Name))
                    return "{ type: \"Equals\", value: " + EscapeIdentifier(identifierPattern.Name) + " }";
                return "{ type: \"Identifier\", name: \"" + EscapeString(identifierPattern.Name) + "\" }";
            case WildcardPattern:
                return "{ type: \"Wildcard\" }";
            case VariantPattern variantPattern:
                return "{ type: \"Variant\", tag: \"" + EscapeString(variantPattern.Tag) + "\", payloadPatterns: [" +
                       string.Join(", ", variantPattern.PayloadPatterns.Select(TranspilePatternDescriptor)) + "] }";
            case ArrayPattern arrayPattern:
                var rest = arrayPattern.Rest == null
                    ? "null"
                    : "{ type: \"Rest\", name: " + (arrayPattern.Rest.Name == null ? "null" : "\"" + EscapeString(arrayPattern.Rest.Name) + "\"") + " }";
                return "{ type: \"Array\", elements: [" +
                       string.Join(", ", arrayPattern.Elements.Select(TranspilePatternDescriptor)) +
                       "], rest: " + rest + " }";
            case ObjectPattern objectPattern:
                return "{ type: \"Object\", properties: [" + string.Join(", ", objectPattern.Properties.Select(prop =>
                    "{ key: \"" + EscapeString(prop.Key) + "\", pattern: " +
                    (prop.Pattern == null ? "null" : TranspilePatternDescriptor(prop.Pattern)) +
                    ", bindingName: " + (prop.BindingName == null ? "null" : "\"" + EscapeString(prop.BindingName) + "\"") + " }")) +
                    "] }";
            case RestPattern restPattern:
                return "{ type: \"Rest\", name: " + (restPattern.Name == null ? "null" : "\"" + EscapeString(restPattern.Name) + "\"") + " }";
            default:
                throw new NotSupportedException($"JavaScript transpilation does not support pattern '{pattern.GetType().Name}'.");
        }
    }

    private List<string> CollectPatternBindings(Pattern pattern)
    {
        var bindings = new List<string>();
        CollectPatternBindings(pattern, bindings);
        return bindings.Distinct(StringComparer.Ordinal).ToList();
    }

    private void CollectPatternBindings(Pattern pattern, List<string> bindings)
    {
        switch (pattern)
        {
            case IdentifierPattern identifierPattern:
                if (!IsConstIdentifierPattern(identifierPattern.Name))
                    bindings.Add(identifierPattern.Name);
                break;
            case VariantPattern variantPattern:
                foreach (var payloadPattern in variantPattern.PayloadPatterns)
                {
                    CollectPatternBindings(payloadPattern, bindings);
                }
                break;
            case ArrayPattern arrayPattern:
                foreach (var element in arrayPattern.Elements)
                {
                    CollectPatternBindings(element, bindings);
                }
                if (arrayPattern.Rest?.Name != null)
                {
                    bindings.Add(arrayPattern.Rest.Name);
                }
                break;
            case ObjectPattern objectPattern:
                foreach (var property in objectPattern.Properties)
                {
                    if (property.Pattern != null)
                    {
                        CollectPatternBindings(property.Pattern, bindings);
                    }
                    else if (property.BindingName != null)
                    {
                        bindings.Add(property.BindingName);
                    }
                }
                break;
            case RestPattern restPattern:
                if (restPattern.Name != null)
                {
                    bindings.Add(restPattern.Name);
                }
                break;
        }
    }
}
