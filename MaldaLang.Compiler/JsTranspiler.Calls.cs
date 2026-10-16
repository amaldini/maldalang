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
    private string TranspileFunctionCall(FunctionCallExpression functionCall)
    {
        if (functionCall.Callee is MemberAccessExpression stopCall &&
            stopCall.Member == "stop" &&
            functionCall.Arguments.Count == 0 &&
            !IsGameLoopStop(stopCall))
        {
            return $"mlRuntime.actors.callActorOrVoidStop({TranspileExpression(stopCall.Object)})";
        }

        // A user binding named like a built-in (ShadowsBuiltIn) is called as written.
        if (functionCall.Callee is IdentifierExpression { ShadowsBuiltIn: false } identifier &&
            TryTranspileBuiltInCall(identifier.Name, functionCall.Arguments, out var builtInCall))
        {
            return builtInCall;
        }

        if (functionCall.Callee is MemberAccessExpression memberCall)
        {
            if (memberCall.Object is IdentifierExpression { Name: "glsl" })
            {
                if (memberCall.Member == "compile")
                    return TranspileGlslCompile(functionCall);

                throw new NotSupportedException($"Unknown glsl helper 'glsl.{memberCall.Member}'. Use glsl.compile({{ ... }}) in JavaScript mode.");
            }

            if (memberCall.Member == "append" && functionCall.Arguments.Count == 1)
            {
                return $"mlRuntime.arrayAppend({TranspileExpression(memberCall.Object)}, {TranspileExpression(functionCall.Arguments[0])})";
            }

            if (memberCall.Object is IdentifierExpression memberObjectIdentifier &&
                IsUnshadowedRuntimeModule(memberObjectIdentifier.Name))
            {
                if (memberObjectIdentifier.Name == "dom" || memberObjectIdentifier.Name == "game" || memberObjectIdentifier.Name == "three" ||
                    memberObjectIdentifier.Name == StdLibNamespaces.MathModule ||
                    memberObjectIdentifier.Name == StdLibNamespaces.DeprecatedMathModuleAlias ||
                    memberObjectIdentifier.Name == StdLibNamespaces.StrModule ||
                    memberObjectIdentifier.Name == StdLibNamespaces.IoModule ||
                    memberObjectIdentifier.Name == StdLibNamespaces.NnModule ||
                    memberObjectIdentifier.Name == "schema")
                {
                    return $"mlRuntime.{MapRuntimeModuleName(memberObjectIdentifier.Name)}.{EscapeIdentifier(memberCall.Member)}({JoinArguments(functionCall.Arguments)})";
                }

                if (TryTranspileVariantStdLibCall(memberObjectIdentifier.Name, memberCall.Member, functionCall.Arguments, out var variantCall))
                {
                    return variantCall;
                }

                if (memberObjectIdentifier.Name == "grounded" && memberCall.Member == "wrap")
                {
                    return $"mlRuntime.grounded.wrap({JoinArguments(functionCall.Arguments)})";
                }

                if (memberObjectIdentifier.Name == "cap")
                {
                    return $"mlRuntime.cap.{EscapeIdentifier(memberCall.Member)}({JoinArguments(functionCall.Arguments)})";
                }

                if (memberObjectIdentifier.Name == StdLibNamespaces.AgentsModule)
                {
                    throw new NotSupportedException(
                        "agents.define / agents.team are host-only (interpreter and C# transpile). JavaScript does not support agents.");
                }
            }

            if (ArrayPipelineMethods.Contains(memberCall.Member))
            {
                var args = string.Join(", ", functionCall.Arguments.Select(TranspileExpression));
                if (args.Length == 0)
                {
                    return $"mlRuntime.callArrayMethod({TranspileExpression(memberCall.Object)}, {TranspileLiteral(memberCall.Member)})";
                }

                return $"mlRuntime.callArrayMethod({TranspileExpression(memberCall.Object)}, {TranspileLiteral(memberCall.Member)}, [{args}])";
            }

            // Dict literals are plain JS objects, so `d.keys()` needs the runtime; a receiver
            // with its own method of that name (class instance) is still called directly.
            if (DictionaryMethods.Contains(memberCall.Member))
            {
                return $"mlRuntime.callDictMethod({TranspileExpression(memberCall.Object)}, {TranspileLiteral(memberCall.Member)}, [{JoinArguments(functionCall.Arguments)}])";
            }
        }

        return $"{TranspileExpression(functionCall.Callee)}({JoinArguments(functionCall.Arguments)})";
    }

    private bool TryTranspileBuiltInCall(string name, List<Expression> arguments, out string transpiled)
    {
        transpiled = string.Empty;

        switch (name)
        {
            case "reply":
                transpiled = arguments.Count == 0
                    ? "mlRuntime.actors.reply(null)"
                    : $"mlRuntime.actors.reply({TranspileExpression(arguments[0])})";
                return true;
            case "print":
                transpiled = $"mlRuntime.builtins.print({JoinArguments(arguments)})";
                return true;
            case "println":
                transpiled = $"mlRuntime.builtins.println({JoinArguments(arguments)})";
                return true;
            case "sleep":
                transpiled = $"mlRuntime.builtins.sleep({JoinArguments(arguments)})";
                return true;
            case "string":
                transpiled = arguments.Count == 0
                    ? "mlRuntime.coerceToString(\"\")"
                    : $"__maldaFormat({TranspileExpression(arguments[0])})";
                return true;
            case "typeOf":
                transpiled = $"mlRuntime.typeOf({JoinArguments(arguments)})";
                return true;
            case "isTag":
                transpiled = $"mlRuntime.isTag({JoinArguments(arguments)})";
                return true;
            case "isNumber":
                transpiled = $"mlRuntime.isNumber({JoinArguments(arguments)})";
                return true;
            case "all":
                transpiled = $"mlRuntime.all({JoinArguments(arguments)})";
                return true;
            case "error":
                transpiled = $"mlRuntime.errorBuiltin({JoinArguments(arguments)})";
                return true;
            case "range":
                transpiled = $"mlRuntime.rangeBuiltin({JoinArguments(arguments)})";
                return true;
            case "join":
                transpiled = $"mlRuntime.joinBuiltin({JoinArguments(arguments)})";
                return true;
            case "sort":
                transpiled = $"mlRuntime.sortBuiltin({JoinArguments(arguments)})";
                return true;
            case "runProperty":
                transpiled = $"mlRuntime.runProperty(__propertyRegistry, {JoinArguments(arguments)})";
                return true;
            case "receive":
                transpiled = "mlRuntime.actors.receiveAsync()";
                return true;
            case "int":
                transpiled = arguments.Count == 0
                    ? "mlRuntime.coerceToInt(0)"
                    : $"mlRuntime.coerceToInt({TranspileExpression(arguments[0])})";
                return true;
            case "float":
                transpiled = arguments.Count == 0
                    ? "mlRuntime.coerceToFloat(0)"
                    : $"mlRuntime.coerceToFloat({TranspileExpression(arguments[0])})";
                return true;
            case "parseJSON":
                transpiled = $"mlRuntime.parseJSON({JoinArguments(arguments)})";
                return true;
            case "parseJson":
                transpiled = $"mlRuntime.parseJson({JoinArguments(arguments)})";
                return true;
            case "toJSON":
                transpiled = $"mlRuntime.toJSON({JoinArguments(arguments)})";
                return true;
            case "toIntOr":
            case "toIntOrNull":
            case "formatNumber":
            case "isString":
            case "isArray":
            case "toCsv":
            case "reverse":
            case "urlEncode":
            case "urlDecode":
            case "assert":
            case "expect":
                transpiled = $"mlRuntime.{name}({JoinArguments(arguments)})";
                return true;
            case "isObject":
                // mlRuntime.isObject is the destructuring test; the built-in excludes tasks.
                transpiled = $"mlRuntime.isObjectBuiltin({JoinArguments(arguments)})";
                return true;
            case "input":
                transpiled = $"mlRuntime.io.input({JoinArguments(arguments)})";
                return true;
            case "validate":
                transpiled = $"mlRuntime.schema.validate({JoinArguments(arguments)})";
                return true;
            case "asVariant":
                transpiled = $"mlRuntime.schema.asVariant({JoinArguments(arguments)})";
                return true;
            case "now":
                transpiled = "mlRuntime.now()";
                return true;
            case "formatDate":
                transpiled = $"mlRuntime.formatDate({JoinArguments(arguments)})";
                return true;
            case "parseDate":
                transpiled = $"mlRuntime.parseDate({JoinArguments(arguments)})";
                return true;
            case "addDays":
                transpiled = $"mlRuntime.addDays({JoinArguments(arguments)})";
                return true;
            case "addHours":
                transpiled = $"mlRuntime.addHours({JoinArguments(arguments)})";
                return true;
            case "getEnv":
                transpiled = $"mlRuntime.getEnv({JoinArguments(arguments)})";
                return true;
            case "getEnvOr":
                transpiled = $"mlRuntime.getEnvOr({JoinArguments(arguments)})";
                return true;
            case "hasEnv":
                transpiled = $"mlRuntime.hasEnv({JoinArguments(arguments)})";
                return true;
            case "httpGet":
            case "httpPost":
            case "httpPut":
            case "httpDelete":
            case "httpPatch":
                transpiled = $"mlRuntime.http.{EscapeIdentifier(name[4..].ToLowerInvariant())}({JoinArguments(arguments)})";
                return true;
            default:
                if (StdLibNamespaces.MathMethodNames.Contains(name))
                {
                    transpiled = $"mlRuntime.math.{EscapeIdentifier(name)}({JoinArguments(arguments)})";
                    return true;
                }

                if (StdLibNamespaces.StrMethodNames.Contains(name))
                {
                    transpiled = $"mlRuntime.str.{EscapeIdentifier(name)}({JoinArguments(arguments)})";
                    return true;
                }

                return false;
        }
    }

    private bool TryTranspileVariantStdLibCall(string moduleName, string memberName, List<Expression> arguments, out string transpiled)
    {
        transpiled = string.Empty;
        if (moduleName is not ("result" or "option"))
        {
            return false;
        }

        transpiled = $"mlRuntime.{moduleName}.{EscapeIdentifier(memberName)}({JoinArguments(arguments)})";
        return true;
    }

    private static string MapRuntimeModuleName(string name) =>
        name == StdLibNamespaces.DeprecatedMathModuleAlias ? StdLibNamespaces.MathModule : name;
}
