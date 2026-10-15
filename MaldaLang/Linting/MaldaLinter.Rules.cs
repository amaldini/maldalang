// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Linting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MaldaLang.BuiltIns;
using MaldaLang.Formatting;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

public static partial class MaldaLinter
{
    // ML0001 — {name.field}, {name[i]}, {f(x)} in a plain string. Plain {name} is already the
    // malda-interp warning of malda check, so it is not repeated here. No spaces inside the braces,
    // not {{…}} (mustache) and not ${…} (JavaScript template literal); the root must be a name the
    // code declares or uses, so CSS / JSON / other-language text in strings does not match.
    private static readonly Regex InterpolationLike = new(
        @"(?<![{$\\])\{(?<root>[A-Za-z_][A-Za-z0-9_]*)(?:\.[A-Za-z_][A-Za-z0-9_]*|\[[^\[\]{}""\s]+\]|\([^(){}""]*\))+\}(?!\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Calls whose string arguments are templates with their own brace syntax.</summary>
    private static bool IsTemplateCall(FunctionCallExpression call)
    {
        var name = CalleeName(call);
        var member = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
        return member.Contains("template", StringComparison.OrdinalIgnoreCase)
               || member is "withExamples" or "format";
    }

    /// <summary>JavaScript / CSS / C-like source embedded in a string uses braces for blocks, not holes.</summary>
    private static bool LooksLikeCode(string text) =>
        text.Contains(';') || text.Contains("=>", StringComparison.Ordinal) || text.Contains("function", StringComparison.Ordinal);

    private static void CheckPlainStringInterpolation(LintContext context)
    {
        var templateArguments = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        var nodes = context.Nodes(n => n is not PromptDeclaration).ToList();
        foreach (var call in nodes.OfType<FunctionCallExpression>())
        {
            if (IsTemplateCall(call))
                templateArguments.UnionWith(call.Arguments);
        }

        foreach (var literal in nodes.OfType<LiteralExpression>())
        {
            if (literal.Value is not string text || templateArguments.Contains(literal) || LooksLikeCode(text))
                continue;

            foreach (Match match in InterpolationLike.Matches(text))
            {
                if (!context.CodeNames.Contains(match.Groups["root"].Value))
                    continue;

                context.Report(
                    LintRules.PlainStringInterpolation,
                    literal,
                    text.Length + 2,
                    $"A plain string does not interpolate: \"{match.Value}\" prints literally. Prefix the string with $ to interpolate.",
                    $"$\"…{match.Value}…\" or string concatenation");
                break;
            }
        }
    }

    // ML0002 — parseJSON(<model output>) whose result is never validated.
    private static readonly HashSet<string> ModelMethodNames = new(StringComparer.Ordinal) { "think", "chat", "ask" };

    private static bool IsModelOutput(LintContext context, Expression? expression, HashSet<string> modelVariables)
    {
        while (true)
        {
            switch (expression)
            {
                case AwaitExpression awaitExpr:
                    expression = awaitExpr.Expression;
                    continue;
                case MemberAccessExpression { Member: "content" or "text" } member:
                    expression = member.Object;
                    continue;
                case IdentifierExpression id:
                    return modelVariables.Contains(id.Name);
                case FunctionCallExpression call:
                    if (call.Callee is IdentifierExpression callee)
                    {
                        if (callee.Name == "runPrompt")
                            return true;
                        return context.Prompts.TryGetValue(callee.Name, out var prompt)
                               && string.IsNullOrWhiteSpace(prompt.ReturnType);
                    }

                    return call.Callee is MemberAccessExpression m && ModelMethodNames.Contains(m.Member);
                default:
                    return false;
            }
        }
    }

    private static void CheckUncheckedModelJson(LintContext context)
    {
        var nodes = context.Nodes().ToList();
        var modelVariables = new HashSet<string>(StringComparer.Ordinal);
        var empty = new HashSet<string>(StringComparer.Ordinal);
        foreach (var varDecl in nodes.OfType<VarDeclStatement>())
        {
            if (IsModelOutput(context, varDecl.Initializer, empty))
                modelVariables.Add(varDecl.Name);
        }

        // Values that reach a checker: direct arguments, or variables passed to one anywhere in the file.
        var checkedCalls = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        var checkedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in nodes.OfType<FunctionCallExpression>())
        {
            if (CalleeName(call) is not ("validate" or "asVariant" or "parseJson"))
                continue;
            foreach (var argument in call.Arguments)
            {
                checkedCalls.Add(argument);
                if (argument is IdentifierExpression id)
                    checkedNames.Add(id.Name);
            }
        }

        var assignedTo = new Dictionary<Node, string>(ReferenceEqualityComparer.Instance);
        foreach (var varDecl in nodes.OfType<VarDeclStatement>())
            assignedTo[varDecl.Initializer] = varDecl.Name;

        foreach (var call in nodes.OfType<FunctionCallExpression>())
        {
            if (CalleeName(call) != "parseJSON" || call.Arguments.Count != 1)
                continue;
            if (!IsModelOutput(context, call.Arguments[0], modelVariables))
                continue;
            if (checkedCalls.Contains(call))
                continue;
            if (assignedTo.TryGetValue(call, out var variable) && checkedNames.Contains(variable))
                continue;

            context.Report(
                LintRules.UncheckedModelJson,
                call,
                "parseJSON".Length,
                "parseJSON of model output is not checked: the reply can be any JSON shape and nothing validates it before use.",
                "Declare a schema and use validate(\"Name\", data) (check .ok), parseJson(text, \"Name\") (throws on mismatch), or a typed prompt -> Name");
        }
    }

    // ML0003 — Mode B prompt: tools: plus -> Type without gather:.
    private static void CheckModeBPrompts(LintContext context)
    {
        foreach (var prompt in context.Nodes().OfType<PromptDeclaration>())
        {
            if (string.IsNullOrWhiteSpace(prompt.ReturnType))
                continue;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (prompt.StatementBody != null)
            {
                foreach (var body in prompt.StatementBody.OfType<PromptBodyStatement>())
                    keys.Add(body.Keyword);
            }

            if (prompt.ObjectBody != null)
            {
                foreach (var (key, _) in prompt.ObjectBody.Properties)
                {
                    switch (key)
                    {
                        case LiteralExpression { Value: string s }:
                            keys.Add(s);
                            break;
                        case IdentifierExpression id:
                            keys.Add(id.Name);
                            break;
                    }
                }
            }

            if (!keys.Contains("tools") || keys.Contains("gather"))
                continue;

            context.Report(
                LintRules.ModeBPrompt,
                prompt,
                "prompt".Length,
                $"Prompt '{prompt.Name}' combines tools: with -> {prompt.ReturnType} (Mode B). It is one call: response_format is sent only when the backend accepts it together with tools, otherwise the reply relies on validation and repair.",
                "For a guaranteed typed result after tool rounds use Mode C (gather: plus -> Type); without tools use Mode A");
        }
    }

    // ML0005 — x => x.done = true: the parser keeps only the right-hand side.
    private static readonly HashSet<string> AssignmentOperators = new(StringComparer.Ordinal)
    {
        "=", "+=", "-=", "*=", "/=", "%=", "??="
    };

    private static void CheckLambdaAssignments(LintContext context)
    {
        List<TriviaToken> tokens;
        try
        {
            tokens = SourceTriviaScanner.Scan(context.Source).Where(t => t.IsCode).ToList();
        }
        catch
        {
            return;
        }

        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (!tokens[i].Is("=>"))
                continue;

            var j = i + 1;
            if (tokens[j].Kind != TriviaKind.Word)
                continue;
            j++;
            while (j < tokens.Count)
            {
                if ((tokens[j].Is(".") || tokens[j].Is("?.")) && j + 1 < tokens.Count && tokens[j + 1].Kind == TriviaKind.Word)
                {
                    j += 2;
                    continue;
                }

                if (tokens[j].Is("["))
                {
                    var depth = 0;
                    for (; j < tokens.Count; j++)
                    {
                        if (tokens[j].Is("[")) depth++;
                        else if (tokens[j].Is("]") && --depth == 0) break;
                    }

                    j++;
                    continue;
                }

                break;
            }

            if (j >= tokens.Count || tokens[j].Kind != TriviaKind.Punct || !AssignmentOperators.Contains(tokens[j].Text))
                continue;

            var target = tokens[i + 1];
            context.Report(
                LintRules.LambdaAssignmentDropped,
                target.Line,
                target.Column,
                Math.Max(1, tokens[j].End - target.Start),
                "Assignment in an expression-bodied lambda is dropped: the parser keeps only the right-hand side, so nothing is assigned.",
                "Use a block body: x => { x.done = true; }");
        }
    }

    // ML0007 — case BUY: where BUY is a var: an identifier pattern binds and always matches.
    private static readonly Regex ConstantLikeName = new(@"^[A-Z][A-Z0-9_]*$", RegexOptions.CultureInvariant);

    private static void CheckCaseBindsVar(LintContext context)
    {
        foreach (var matchCase in context.Nodes().OfType<MatchCase>())
        {
            if (matchCase.Pattern is not IdentifierPattern pattern)
                continue;
            var name = pattern.Name;
            if (!ConstantLikeName.IsMatch(name) || !context.VarNames.Contains(name)
                || context.ConstNames.Contains(name) || context.ConstructorNames.Contains(name))
                continue;

            context.Report(
                LintRules.CaseBindsVar,
                pattern,
                name.Length,
                $"case {name}: binds a new name and always matches; only a const (or a variant constructor) is compared. '{name}' is declared with var.",
                $"const {name} = …; or case x if x == {name}:");
        }
    }

    // ML0008 — if (0), while (""), if (items.length): always true in MALDA.
    private static void CheckAlwaysTruthyConditions(LintContext context)
    {
        foreach (var node in context.Nodes())
        {
            var condition = node switch
            {
                IfStatement s => s.Condition,
                WhileStatement s => s.Condition,
                ForStatement s => s.Condition,
                TernaryExpression t => t.Condition,
                _ => null
            };
            if (condition != null)
                CheckCondition(context, condition, node);
        }
    }

    private static void CheckCondition(LintContext context, Expression condition, Node owner)
    {
        switch (condition)
        {
            case UnaryExpression { Operator: TokenType.Not } not:
                CheckCondition(context, not.Right, owner);
                return;
            case BinaryExpression { Operator: TokenType.And or TokenType.Or } logical:
                CheckCondition(context, logical.Left, owner);
                CheckCondition(context, logical.Right, owner);
                return;
        }

        string? what = condition switch
        {
            LiteralExpression { Value: int or long or double or float or decimal } number when Convert.ToDouble(number.Value) == 0 => "0",
            LiteralExpression { Value: string s } when s.Length == 0 => "\"\"",
            ArrayLiteralExpression { Elements.Count: 0 } => "[]",
            MemberAccessExpression { Member: "length", IsNullConditional: false } => ".length",
            FunctionCallExpression call when CalleeName(call) is "str.length" or "length" => "str.length(…)",
            _ => null
        };
        if (what == null)
            return;

        context.Report(
            LintRules.AlwaysTruthyCondition,
            condition,
            1,
            $"This condition is always true: {what} is truthy in MALDA (only null and false are falsy).",
            what is ".length" or "str.length(…)" ? "Compare explicitly: items.length > 0" : "Write the comparison you mean, e.g. n != 0",
            owner);
    }

    // ML0009 — result.map / option.map whose callback returns Ok / Err / Some / None.
    private static bool IsWrapperCall(Expression? expression) => expression is FunctionCallExpression call
        && (CalleeName(call) is "result.ok" or "result.err" or "option.some" or "option.none" or "Ok" or "Err" or "Some" or "None");

    private static void CheckMapReturnsWrapper(LintContext context)
    {
        foreach (var call in context.Nodes().OfType<FunctionCallExpression>())
        {
            var name = CalleeName(call);
            if (name is not ("result.map" or "option.map"))
                continue;
            if (call.Arguments.LastOrDefault() is not LambdaExpression lambda)
                continue;

            var returnsWrapper = IsWrapperCall(lambda.ExpressionBody);
            if (!returnsWrapper && lambda.BlockBody != null)
            {
                returnsWrapper = AstNodeWalker
                    .Descendants(lambda.BlockBody, n => n is not LambdaExpression and not FunctionDeclaration)
                    .OfType<ReturnStatement>()
                    .Any(r => IsWrapperCall(r.Value));
            }

            if (!returnsWrapper)
                continue;

            var ns = name[..name.IndexOf('.')];
            context.Report(
                LintRules.MapReturnsWrapper,
                call,
                name.Length,
                $"{name} wraps the callback's return value, so returning {(ns == "result" ? "Ok / Err" : "Some / None")} gives a nested {(ns == "result" ? "Ok(Ok(…))" : "Some(Some(…))")}.",
                $"Use {ns}.andThen(…) when the callback itself returns a {(ns == "result" ? "result" : "option")}");
        }
    }

    // ML0010 — str.trim(io.getEnv("X")): getEnv is null when unset and str.* rejects null.
    private static void CheckStringFunctionOnGetEnv(LintContext context)
    {
        foreach (var call in context.Nodes().OfType<FunctionCallExpression>())
        {
            var name = CalleeName(call);
            var member = name.StartsWith("str.", StringComparison.Ordinal) ? name[4..] : name;
            if (member is "text" or "trimText" || !StdLibNamespaces.StrMethodNames.Contains(member))
                continue;
            if (call.Arguments.Count == 0 || call.Arguments[0] is not FunctionCallExpression inner
                || CalleeName(inner) is not ("io.getEnv" or "getEnv"))
                continue;

            context.Report(
                LintRules.StringFunctionOnGetEnv,
                call,
                name.Length,
                $"io.getEnv returns null when the variable is unset, and {name}(null) is a run-time error.",
                "io.getEnvOr(\"NAME\", \"\") or str.trimText(io.getEnv(\"NAME\"))");
        }
    }

    // ML0011 — str.repeat(s, n / 2): `/` yields a float; a fractional count is rejected.
    private static void CheckRepeatFractionalCount(LintContext context)
    {
        foreach (var call in context.Nodes().OfType<FunctionCallExpression>())
        {
            if (CalleeName(call) is not ("str.repeat" or "repeat") || call.Arguments.Count != 2)
                continue;
            if (call.Arguments[1] is not BinaryExpression { Operator: TokenType.Divide } division)
                continue;

            context.Report(
                LintRules.RepeatFractionalCount,
                division,
                1,
                "The repeat count is a division: / always yields a float, and a fractional count (odd numerator) is a run-time error.",
                "str.repeat(s, int(n / 2)) or math.floor(n / 2)");
        }
    }

    // ML0012 — ui.dispatchEvent(event, 1): the second argument is the session id.
    private static void CheckDispatchEventSession(LintContext context)
    {
        foreach (var call in context.Nodes().OfType<FunctionCallExpression>())
        {
            if (CalleeName(call) is not ("ui.dispatchEvent" or "uiDispatchEvent") || call.Arguments.Count < 2)
                continue;
            if (call.Arguments[1] is not LiteralExpression { Value: int or long or double or float or decimal } literal)
                continue;

            context.Report(
                LintRules.DispatchEventNumericSession,
                literal,
                1,
                "The second argument of ui.dispatchEvent is the session id (a string). A number is ignored and the event goes to \"default\" unsequenced.",
                "ui.dispatchEvent(event, sessionId) or ui.dispatchEvent(event, sessionId, sequence)");
        }
    }

    // ML0013 — arms after an unguarded catch-all (case x: / case _:) never run.
    private static void CheckUnreachableCases(LintContext context)
    {
        foreach (var match in context.Nodes().OfType<MatchExpression>())
        {
            for (var i = 0; i < match.Cases.Count - 1; i++)
            {
                var arm = match.Cases[i];
                if (arm.Guard != null || !IsCatchAll(context, arm.Pattern, out var label))
                    continue;

                var next = match.Cases[i + 1];
                context.Report(
                    LintRules.UnreachableCaseAfterCatchAll,
                    next.Pattern.Line > 0 ? next.Pattern : next,
                    "case".Length,
                    $"Unreachable case: the earlier 'case {label}:' (line {arm.Pattern.Line}) matches every value, and an if inside its body does not fall through.",
                    $"Use a guard: case {label} if <condition>: and keep the catch-all last");
                break;
            }
        }
    }

    private static bool IsCatchAll(LintContext context, Pattern pattern, out string label)
    {
        switch (pattern)
        {
            case WildcardPattern:
                label = "_";
                return true;
            case IdentifierPattern id when id.Name == "_":
                label = "_";
                return true;
            case IdentifierPattern id when id.Name.Length > 0 && char.IsLower(id.Name[0])
                                            && !context.ConstNames.Contains(id.Name)
                                            && !context.ConstructorNames.Contains(id.Name)
                                            && !context.ImportedNames.Contains(id.Name):
                label = id.Name;
                return true;
            default:
                label = "";
                return false;
        }
    }
}
