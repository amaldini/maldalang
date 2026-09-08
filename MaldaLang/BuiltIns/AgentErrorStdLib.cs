// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Built-in <c>AgentError</c> sum type. Additive: existing <c>{ ok, error }</c> strings stay;
/// the variant is attached as <c>agentError</c>.
/// </summary>
public static class AgentErrorStdLib
{
    public const string TypeName = "AgentError";

    public static readonly TypeDeclaration Declaration = new(
        TypeName,
        new List<VariantConstructor>
        {
            Ctor("Refused", "reason"),
            Ctor("Unparsable", "raw", "attempts"),
            Ctor("SchemaMismatch", "schema", "error"),
            Ctor("BudgetExceeded", "kind", "limit"),
            Ctor("ToolDenied", "tool", "reason"),
            Ctor("Timeout", "ms"),
            Ctor("Upstream", "status", "body")
        });

    public static void EnsureRegistered()
    {
        if (!SumTypeRegistry.IsRegistered(TypeName))
            SumTypeRegistry.Register(Declaration);
    }

    public static void BindGlobals(MaldaLang.Interpreter.Environment env)
    {
        EnsureRegistered();
        var ns = new JsonObject();
        foreach (var ctor in Declaration.Constructors)
        {
            var fv = new FunctionValue(null, null)
            {
                VariantConstructorTag = ctor.Name,
                VariantConstructorArity = ctor.ParameterNames.Count
            };
            var fn = RuntimeValue.Function(fv);
            env.Define(ctor.Name, fn);
            ns.Set(ctor.Name, fn);
        }

        env.Define(TypeName, RuntimeValue.Object(ns));
    }

    public static RuntimeValue Refused(string reason) =>
        RuntimeValue.Variant("Refused", new List<RuntimeValue> { RuntimeValue.String(reason) });

    public static RuntimeValue Unparsable(string raw, int attempts) =>
        RuntimeValue.Variant("Unparsable", new List<RuntimeValue> { RuntimeValue.String(raw), RuntimeValue.Integer(attempts) });

    public static RuntimeValue SchemaMismatch(string schema, string error) =>
        RuntimeValue.Variant("SchemaMismatch", new List<RuntimeValue> { RuntimeValue.String(schema), RuntimeValue.String(error) });

    public static RuntimeValue BudgetExceeded(string kind, string limit) =>
        RuntimeValue.Variant("BudgetExceeded", new List<RuntimeValue> { RuntimeValue.String(kind), RuntimeValue.String(limit) });

    public static RuntimeValue ToolDenied(string tool, string reason) =>
        RuntimeValue.Variant("ToolDenied", new List<RuntimeValue> { RuntimeValue.String(tool), RuntimeValue.String(reason) });

    public static RuntimeValue Timeout(int ms) =>
        RuntimeValue.Variant("Timeout", new List<RuntimeValue> { RuntimeValue.Integer(ms) });

    public static RuntimeValue Upstream(int status, string body) =>
        RuntimeValue.Variant("Upstream", new List<RuntimeValue> { RuntimeValue.Integer(status), RuntimeValue.String(body) });

    public static RuntimeValue Attach(RuntimeValue result, RuntimeValue agentError)
    {
        if (result.Type == ValueType.Object && result.AsObject() is JsonObject obj)
        {
            obj.Set("agentError", agentError);
            return result;
        }

        return result;
    }

    private static VariantConstructor Ctor(string name, params string[] args) =>
        new(name, args.ToList(), args.Select(_ => (string?)"string").ToList(), null);
}
