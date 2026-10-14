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
    private bool TryGetVariantConstructorCallName(Expression callee, out string constructorName)
    {
        if (callee is IdentifierExpression variantCtorId &&
            _variantConstructorNames.Contains(variantCtorId.Name))
        {
            constructorName = variantCtorId.Name;
            return true;
        }

        if (callee is MemberAccessExpression member &&
            member.Object is IdentifierExpression typeId &&
            IsSumTypeConstructor(typeId.Name, member.Member))
        {
            constructorName = member.Member;
            return true;
        }

        constructorName = "";
        return false;
    }

    private bool IsSumTypeConstructor(string typeName, string constructorName)
    {
        foreach (var typeDecl in _typeDeclarations)
        {
            if (!string.Equals(typeDecl.TypeName, typeName, StringComparison.Ordinal))
                continue;
            foreach (var ctor in typeDecl.Constructors)
            {
                if (string.Equals(ctor.Name, constructorName, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    private void EmitSumTypeNamespaceField(TypeDeclaration typeDecl)
    {
        // Avoid a C# field/method clash when the type is named like a constructor.
        if (_variantConstructorNames.Contains(typeDecl.TypeName))
            return;

        var field = EscapeIdentifier(typeDecl.TypeName);
        var helper = "__sumNs_" + field;
        WriteIndent();
        _output.Append("private static MaldaLang.Interpreter.RuntimeValue ");
        _output.Append(helper);
        _output.Append("() { var obj = new MaldaLang.BuiltIns.JsonObject(); ");
        foreach (var ctor in typeDecl.Constructors)
        {
            var tag = ctor.Name.Replace("\\", "\\\\").Replace("\"", "\\\"");
            _output.Append("obj.Set(\"");
            _output.Append(tag);
            _output.Append("\", MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue(null, null) { VariantConstructorTag = \"");
            _output.Append(tag);
            _output.Append("\", VariantConstructorArity = ");
            _output.Append(ctor.ParameterNames.Count);
            _output.Append(" })); ");
        }

        _output.Append("return MaldaLang.Interpreter.RuntimeValue.Object(obj); }");
        _output.AppendLine();

        WriteIndent();
        _output.Append("private static readonly MaldaLang.Interpreter.RuntimeValue ");
        _output.Append(field);
        _output.Append(" = ");
        _output.Append(helper);
        _output.Append("();");
        _output.AppendLine();
    }

    private void EmitVariantConstructorMethod(VariantConstructor ctor)
    {
        WriteIndent();
        _output.Append("private static MaldaLang.Interpreter.RuntimeValue ");
        _output.Append(EscapeIdentifier(ctor.Name));
        _output.Append("(");
        var n = ctor.ParameterNames.Count;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("MaldaLang.Interpreter.RuntimeValue a");
            _output.Append(i);
        }
        _output.Append(") => MaldaLang.Interpreter.RuntimeValue.Variant(\"");
        _output.Append(ctor.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.Append("\", new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < n; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("a");
            _output.Append(i);
        }
        _output.Append(" });");
        _output.AppendLine();
    }

    private void TranspileMatch(MatchExpression match)
    {
        _matchBindCounter = 0;
        var valueVar = "__matchValue";
        _output.Append("((");
        _output.Append("Func<MaldaLang.Interpreter.RuntimeValue>)(() => { var ");
        _output.Append(valueVar);
        _output.Append(" = RuntimeHelpers.ToRuntimeValue(");
        TranspileExpression(match.Value);
        _output.Append("); ");
        for (int i = 0; i < match.Cases.Count; i++)
        {
            var matchCase = match.Cases[i];
            TranspileMatchCase(valueVar, matchCase.Pattern, matchCase.Body, matchCase.Guard);
        }
        if (match.DefaultCase != null)
        {
            _output.Append(" { ");
            TranspileMatchBody(match.DefaultCase);
            _output.Append("}");
        }
        else
        {
            _output.Append(" { throw new RuntimeException(\"Match expression had no matching case and no default case.\"); }");
        }
        _output.Append(" }))()");
    }

    private bool TryGetVariantConstructorArity(string name, out int arity)
    {
        foreach (var typeDecl in _typeDeclarations)
        {
            foreach (var ctor in typeDecl.Constructors)
            {
                if (string.Equals(ctor.Name, name, StringComparison.Ordinal))
                {
                    arity = ctor.ParameterNames.Count;
                    return true;
                }
            }
        }

        arity = 0;
        return false;
    }

    private Pattern ResolveBareConstructorPattern(Pattern pattern)
    {
        if (pattern is IdentifierPattern id && TryGetVariantConstructorArity(id.Name, out var arity))
            return VariantPattern.WithImplicitWildcards(id.Name, arity, id.Line, id.Column);
        return pattern;
    }

    private void TranspileMatchCase(string valueVar, Pattern pattern, Statement body, Expression? guard)
    {
        pattern = ResolveBareConstructorPattern(pattern);
        _output.Append("if (");
        TranspileMatchCondition(valueVar, pattern);
        _output.Append(") { ");
        EmitMatchPatternBindings(valueVar, pattern);
        if (guard != null)
        {
            _output.Append("if (RuntimeHelpers.CoerceToBool(");
            TranspileExpression(guard);
            _output.Append(")) { ");
            TranspileMatchBody(body);
            _output.Append(" }");
        }
        else
        {
            TranspileMatchBody(body);
        }
        _output.Append(" }");
    }

    private void TranspileMatchCondition(string valueVar, Pattern pattern)
    {
        switch (pattern)
        {
            case LiteralPattern literal:
                if (literal.Value == null)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Null");
                }
                else if (literal.Value is int iVal)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Integer && ");
                    _output.Append(valueVar);
                    _output.Append(".AsInteger() == ");
                    _output.Append(iVal);
                }
                else if (literal.Value is long lVal)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Integer && ");
                    _output.Append(valueVar);
                    _output.Append(".AsInteger() == ");
                    _output.Append((int)lVal);
                }
                else if (literal.Value is double d)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Float && Math.Abs(");
                    _output.Append(valueVar);
                    _output.Append(".AsFloat() - ");
                    _output.Append(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _output.Append(") < 0.0001");
                }
                else if (literal.Value is float f)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Float && Math.Abs(");
                    _output.Append(valueVar);
                    _output.Append(".AsFloat() - ");
                    _output.Append(((double)f).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    _output.Append(") < 0.0001");
                }
                else if (literal.Value is string s)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.String && string.Equals(");
                    _output.Append(valueVar);
                    _output.Append(".AsString(), ");
                    _output.Append("\"");
                    _output.Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\r", "\\r"));
                    _output.Append("\")");
                }
                else if (literal.Value is bool b)
                {
                    _output.Append(valueVar);
                    _output.Append(".Type == MaldaLang.Interpreter.ValueType.Boolean && ");
                    _output.Append(valueVar);
                    _output.Append(".AsBoolean() == ");
                    _output.Append(b ? "true" : "false");
                }
                else
                {
                    _output.Append("false");
                }
                break;
            case IdentifierPattern identifier:
                if (IsConstIdentifierPattern(identifier.Name))
                {
                    _output.Append("RuntimeHelpers.OperatorEqual(");
                    _output.Append(valueVar);
                    _output.Append(", ");
                    _output.Append(EscapeIdentifier(identifier.Name));
                    _output.Append(")");
                }
                else
                {
                    _output.Append("true");
                }
                break;
            case WildcardPattern _:
                _output.Append("true");
                break;
            case VariantPattern variantPattern:
                _output.Append("RuntimeHelpers.IsVariant(");
                _output.Append(valueVar);
                _output.Append(") && string.Equals(RuntimeHelpers.GetVariantTag(");
                _output.Append(valueVar);
                _output.Append("), \"");
                _output.Append(variantPattern.Tag.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\") && RuntimeHelpers.GetVariantPayload(");
                _output.Append(valueVar);
                _output.Append(").Count == ");
                _output.Append(variantPattern.PayloadPatterns.Count);
                break;
            case ArrayPattern arrayPattern:
                EmitMatchArrayPatternCondition(valueVar, arrayPattern);
                break;
            case ObjectPattern objectPattern:
                EmitMatchObjectPatternCondition(valueVar, objectPattern);
                break;
            default:
                _output.Append("true");
                break;
        }
    }

    private void EmitMatchArrayPatternCondition(string valueVar, ArrayPattern arrayPattern)
    {
        _output.Append("RuntimeHelpers.IsArray(");
        _output.Append(valueVar);
        _output.Append(") && RuntimeHelpers.GetArray(");
        _output.Append(valueVar);
        _output.Append(").Count ");
        _output.Append(arrayPattern.Rest == null ? "== " : ">= ");
        _output.Append(arrayPattern.Elements.Count);
        for (int i = 0; i < arrayPattern.Elements.Count; i++)
        {
            _output.Append(" && (");
            TranspileMatchCondition(
                $"RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetArray({valueVar})[{i}])",
                arrayPattern.Elements[i]);
            _output.Append(")");
        }
    }

    private void EmitMatchObjectPatternCondition(string valueVar, ObjectPattern objectPattern)
    {
        _output.Append("RuntimeHelpers.IsObject(");
        _output.Append(valueVar);
        _output.Append(")");
        var objExpr = $"RuntimeHelpers.UnwrapRuntimeValue({valueVar})";
        foreach (var prop in objectPattern.Properties)
        {
            var escapedKey = prop.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
            _output.Append(" && RuntimeHelpers.ObjectHasKey(");
            _output.Append(objExpr);
            _output.Append(", \"");
            _output.Append(escapedKey);
            _output.Append("\")");
            if (prop.Pattern != null)
            {
                _output.Append(" && (");
                TranspileMatchCondition(
                    $"RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetObjectMember({objExpr}, \"{escapedKey}\"))",
                    prop.Pattern);
                _output.Append(")");
            }
        }
    }

    private void EmitMatchPatternBindings(string valueVar, Pattern pattern)
    {
        switch (pattern)
        {
            case IdentifierPattern identifier:
                if (IsConstIdentifierPattern(identifier.Name))
                    break;
                _output.Append("var ");
                _output.Append(EscapeIdentifier(identifier.Name));
                _output.Append(" = ");
                _output.Append(valueVar);
                _output.Append("; ");
                break;
            case VariantPattern variantPattern:
                _output.Append("var __payload = RuntimeHelpers.GetVariantPayload(");
                _output.Append(valueVar);
                _output.Append("); ");
                for (int i = 0; i < variantPattern.PayloadPatterns.Count; i++)
                {
                    var sub = variantPattern.PayloadPatterns[i];
                    if (sub is IdentifierPattern idp)
                    {
                        if (IsConstIdentifierPattern(idp.Name))
                            continue;
                        _output.Append("var ");
                        _output.Append(EscapeIdentifier(idp.Name));
                        _output.Append(" = __payload[");
                        _output.Append(i);
                        _output.Append("]; ");
                    }
                    else if (sub is not WildcardPattern)
                    {
                        _output.Append("var __sub");
                        _output.Append(i);
                        _output.Append(" = __payload[");
                        _output.Append(i);
                        _output.Append("]; ");
                    }
                }
                break;
            case ArrayPattern arrayPattern:
                _output.Append("var __arr = RuntimeHelpers.GetArray(");
                _output.Append(valueVar);
                _output.Append("); ");
                for (int i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    EmitMatchPatternBindings(
                        $"RuntimeHelpers.ToRuntimeValue(__arr[{i}])",
                        arrayPattern.Elements[i]);
                }
                if (arrayPattern.Rest?.Name != null)
                {
                    _output.Append("var ");
                    _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                    _output.Append(" = __arr.Skip(");
                    _output.Append(arrayPattern.Elements.Count);
                    _output.Append(").ToList(); ");
                }
                break;
            case ObjectPattern objectPattern:
            {
                var objVar = $"__obj{_matchBindCounter++}";
                _output.Append("var ");
                _output.Append(objVar);
                _output.Append(" = RuntimeHelpers.UnwrapRuntimeValue(");
                _output.Append(valueVar);
                _output.Append("); ");
                foreach (var prop in objectPattern.Properties)
                {
                    var escapedKey = prop.Key.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    if (prop.Pattern != null)
                    {
                        EmitMatchPatternBindings(
                            $"RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetObjectMember({objVar}, \"{escapedKey}\"))",
                            prop.Pattern);
                    }
                    else if (prop.BindingName != null)
                    {
                        _output.Append("var ");
                        _output.Append(EscapeIdentifier(prop.BindingName));
                        _output.Append(" = RuntimeHelpers.ToRuntimeValue(RuntimeHelpers.GetObjectMember(");
                        _output.Append(objVar);
                        _output.Append(", \"");
                        _output.Append(escapedKey);
                        _output.Append("\")); ");
                    }
                }
                break;
            }
        }
    }

    
    /// <summary>
    /// Emits code for a match case body. "Last expression wins": for blocks, the last statement's value is returned if it's an expression.
    /// </summary>
    private void TranspileMatchBody(Statement body)
    {
        if (body is ExpressionStatement exprStmt)
        {
            if (IsStatementOnlyMatchExpression(exprStmt.Expression))
            {
                var previousEmitLineDirectives = _emitLineDirectives;
                _emitLineDirectives = false;
                try
                {
                    TranspileStatement(exprStmt);
                }
                finally
                {
                    _emitLineDirectives = previousEmitLineDirectives;
                }

                _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
            }
            else
            {
                _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                TranspileExpression(exprStmt.Expression);
                _output.Append("); ");
            }
        }
        else if (body is BlockStatement block)
        {
            if (block.Statements.Count == 0)
            {
                _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
            }
            else
            {
                for (int i = 0; i < block.Statements.Count - 1; i++)
                {
                    var previousEmitLineDirectives = _emitLineDirectives;
                    _emitLineDirectives = false;
                    try
                    {
                        TranspileStatement(block.Statements[i]);
                    }
                    finally
                    {
                        _emitLineDirectives = previousEmitLineDirectives;
                    }
                }
                var last = block.Statements[block.Statements.Count - 1];
                if (last is ExpressionStatement lastExpr)
                {
                    if (IsStatementOnlyMatchExpression(lastExpr.Expression))
                    {
                        var previousEmitLineDirectives = _emitLineDirectives;
                        _emitLineDirectives = false;
                        try
                        {
                            TranspileStatement(lastExpr);
                        }
                        finally
                        {
                            _emitLineDirectives = previousEmitLineDirectives;
                        }

                        _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
                    }
                    else
                    {
                        _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                        TranspileExpression(lastExpr.Expression);
                        _output.Append("); ");
                    }
                }
                else
                {
                    var previousEmitLineDirectives = _emitLineDirectives;
                    _emitLineDirectives = false;
                    try
                    {
                        TranspileStatement(last);
                    }
                    finally
                    {
                        _emitLineDirectives = previousEmitLineDirectives;
                    }
                    _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
                }
            }
        }
        else
        {
            var previousEmitLineDirectives = _emitLineDirectives;
            _emitLineDirectives = false;
            try
            {
                TranspileStatement(body);
            }
            finally
            {
                _emitLineDirectives = previousEmitLineDirectives;
            }
            _output.Append("return MaldaLang.Interpreter.RuntimeValue.Null(); ");
        }
    }

    private static bool IsStatementOnlyMatchExpression(Expression expression)
    {
        // print / io.print emit void Console.WriteLine; wrapping them in
        // ToRuntimeValue() is CS1503. Same last-expression-wins rule as functions.
        if (ExpressionTranspilesToVoid(expression))
            return true;

        if (expression is FunctionCallExpression call &&
            call.Callee is IdentifierExpression identifier)
        {
            return identifier.Name == "reply";
        }

        return false;
    }

    
    private void TranspilePatternForMatch(Pattern pattern)
    {
        // Generate pattern representation for runtime matching
        // This will be used by RuntimeHelpers.MatchPattern
        _output.Append("new { Type = \"");
        _output.Append(pattern.GetType().Name.Replace("Pattern", ""));
        _output.Append("\", ");
        
        switch (pattern)
        {
            case LiteralPattern literal:
                _output.Append("Value = ");
                TranspileLiteral(new LiteralExpression(literal.Value, literal.Line, literal.Column));
                break;
                
            case IdentifierPattern identifier:
                _output.Append("Name = \"");
                _output.Append(EscapeIdentifier(identifier.Name));
                _output.Append("\"");
                break;
                
            case WildcardPattern:
                _output.Append("Wildcard = true");
                break;
                
            case ArrayPattern arrayPattern:
                _output.Append("Elements = new List<object> { ");
                for (int i = 0; i < arrayPattern.Elements.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspilePatternForMatch(arrayPattern.Elements[i]);
                }
                _output.Append(" }, Rest = ");
                if (arrayPattern.Rest != null)
                {
                    _output.Append("new { Name = ");
                    if (arrayPattern.Rest.Name != null)
                    {
                        _output.Append("\"");
                        _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                        _output.Append("\"");
                    }
                    else
                    {
                        _output.Append("null");
                    }
                    _output.Append(" }");
                }
                else
                {
                    _output.Append("null");
                }
                break;
                
            case ObjectPattern objectPattern:
                _output.Append("Properties = new Dictionary<string, object> { ");
                for (int i = 0; i < objectPattern.Properties.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    var prop = objectPattern.Properties[i];
                    _output.Append("{ \"");
                    _output.Append(EscapeIdentifier(prop.Key));
                    _output.Append("\", ");
                    if (prop.Pattern != null)
                    {
                        TranspilePatternForMatch(prop.Pattern);
                    }
                    else if (prop.BindingName != null)
                    {
                        _output.Append("new { Type = \"Identifier\", Name = \"");
                        _output.Append(EscapeIdentifier(prop.BindingName));
                        _output.Append("\" }");
                    }
                    else
                    {
                        _output.Append("null");
                    }
                    _output.Append(" }");
                }
                _output.Append(" }");
                break;

            case VariantPattern variantPattern:
                _output.Append("Tag = \"");
                _output.Append(EscapeIdentifier(variantPattern.Tag));
                _output.Append("\", PayloadPatterns = new List<object> { ");
                for (int i = 0; i < variantPattern.PayloadPatterns.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    TranspilePatternForMatch(variantPattern.PayloadPatterns[i]);
                }
                _output.Append(" }");
                break;
        }
        
        _output.Append(" }");
    }

    
    private void TranspilePattern(Pattern pattern)
    {
        TranspilePatternForMatch(pattern);
    }
}
