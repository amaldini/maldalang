// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Globalization;
using MaldaLang.Interpreter;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Canonicalizes LLM program JSON before schema validation.
/// Models often emit TypeChat <c>@func</c>/<c>@ref</c>, nested calls in
/// <c>args</c>, numeric strings, or <c>{type,value}</c> wrappers; those would
/// otherwise be passed through as the wrong runtime types.
/// </summary>
public static class ProgramJsonNormalizer
{
    public static bool TryNormalize(
        RuntimeValue value,
        string expectedApiName,
        ApiRegistry.ApiDefinition apiDef,
        out RuntimeValue normalized,
        out string error)
    {
        normalized = RuntimeValue.Null();
        error = "";

        if (!TryAsJsonObject(value, out var root))
        {
            error = "$. must be a JSON program object.";
            return false;
        }

        var apiName = ReadString(root, "@api", "api");
        if (string.IsNullOrEmpty(apiName))
            apiName = expectedApiName;
        if (string.IsNullOrEmpty(apiName))
        {
            error = "$.@api is required and must be a string.";
            return false;
        }

        if (!TryGetRawSteps(root, apiDef, out var rawSteps, out error))
            return false;

        var usedAliases = new HashSet<string>(StringComparer.Ordinal);
        var originalAliases = new List<string>();
        var emitted = new List<JsonObject>();
        var nestedSeq = 0;
        var tSeq = 0;

        for (int i = 0; i < rawSteps.Count; i++)
        {
            var raw = rawSteps[i];
            if (!TryAsJsonObject(raw, out var stepObj))
            {
                error = $"$.steps[{i}] must be an object.";
                return false;
            }

            if (!TryReadCallName(stepObj, $"$.steps[{i}]", out var call, out error))
                return false;

            if (!apiDef.TryGetMethod(call, out var method))
            {
                error = $"$.steps[{i}].call '{call}' is not a method on api '{apiName}'.";
                return false;
            }

            if (!TryReadArgs(stepObj, method, $"$.steps[{i}]", out var rawArgs, out error))
                return false;

            var resolvedArgs = new List<RuntimeValue>();
            for (int a = 0; a < rawArgs.Count; a++)
            {
                if (!TryNormalizeArg(
                        rawArgs[a],
                        apiDef,
                        apiName,
                        originalAliases,
                        usedAliases,
                        emitted,
                        ref nestedSeq,
                        $"$.steps[{i}].args[{a}]",
                        out var arg,
                        out error))
                {
                    return false;
                }

                resolvedArgs.Add(arg);
            }

            var alias = ReadString(stepObj, "as", "@as");
            if (string.IsNullOrWhiteSpace(alias))
                alias = NextAlias("t", ref tSeq, usedAliases);
            else if (!usedAliases.Add(alias))
            {
                error = $"$.steps[{i}].as '{alias}' is duplicated.";
                return false;
            }

            emitted.Add(MakeStep(call, resolvedArgs, alias));
            originalAliases.Add(alias);
        }

        if (emitted.Count == 0)
        {
            error = "$.steps must contain at least one call.";
            return false;
        }

        var returnVal = ReadFirst(root, "return", "@return");
        if (returnVal.Type == ValueType.Null)
            returnVal = RuntimeValue.String("$" + originalAliases[originalAliases.Count - 1]);
        else if (!TryNormalizeArg(
                     returnVal,
                     apiDef,
                     apiName,
                     originalAliases,
                     usedAliases,
                     emitted,
                     ref nestedSeq,
                     "$.return",
                     out returnVal,
                     out error))
        {
            return false;
        }

        var canonical = new JsonObject();
        canonical.Set("@api", RuntimeValue.String(apiName));
        canonical.Set("steps", RuntimeValue.Array(emitted.Select(RuntimeValue.Object).ToList()));
        canonical.Set("return", returnVal);
        normalized = RuntimeValue.Object(canonical);
        return true;
    }

    /// <summary>
    /// Coerces a JSON string that is a decimal number into int/float.
    /// Leaves <c>$alias</c> references and non-numeric strings unchanged.
    /// </summary>
    public static RuntimeValue CoerceNumericString(RuntimeValue value)
    {
        if (value.Type != ValueType.String)
            return value;

        var s = value.AsString();
        if (string.IsNullOrWhiteSpace(s) || s.StartsWith("$", StringComparison.Ordinal))
            return value;

        var trimmed = s.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return RuntimeValue.Integer(i);
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            && !double.IsNaN(d) && !double.IsInfinity(d))
            return RuntimeValue.Float(d);

        return value;
    }

    public static bool IsAliasRef(RuntimeValue value, out string alias)
    {
        alias = "";
        if (value.Type != ValueType.String)
            return false;
        var s = value.AsString();
        if (!s.StartsWith("$", StringComparison.Ordinal) || s.Length < 2)
            return false;
        alias = s.Substring(1);
        return !string.IsNullOrEmpty(alias);
    }

    private static bool TryGetRawSteps(
        JsonObject root,
        ApiRegistry.ApiDefinition apiDef,
        out List<RuntimeValue> rawSteps,
        out string error)
    {
        rawSteps = new List<RuntimeValue>();
        error = "";
        var stepsVal = ReadFirst(root, "steps", "@steps");
        if (stepsVal.Type == ValueType.Array)
        {
            rawSteps.AddRange(stepsVal.AsArray());
            return true;
        }

        if (stepsVal.Type == ValueType.Object)
        {
            rawSteps.Add(stepsVal);
            return true;
        }

        if (LooksLikeCall(root, apiDef))
        {
            rawSteps.Add(RuntimeValue.Object(root));
            return true;
        }

        error = "$.steps must be an array.";
        return false;
    }

    private static bool LooksLikeCall(JsonObject obj, ApiRegistry.ApiDefinition apiDef)
    {
        var call = ReadString(obj, "call", "@func", "func");
        return !string.IsNullOrEmpty(call) && apiDef.TryGetMethod(call, out _);
    }

    private static bool TryReadCallName(JsonObject stepObj, string path, out string call, out string error)
    {
        call = ReadString(stepObj, "call", "@func", "func");
        error = "";
        if (string.IsNullOrEmpty(call))
        {
            error = $"{path}.call must be a string.";
            return false;
        }

        return true;
    }

    private static bool TryReadArgs(
        JsonObject stepObj,
        MaldaLang.Parser.AST.Declarations.ApiMethodSignature method,
        string path,
        out List<RuntimeValue> args,
        out string error)
    {
        args = new List<RuntimeValue>();
        error = "";
        var argsVal = UnwrapTypeWrapper(ReadFirst(stepObj, "args", "@args"));

        if (argsVal.Type == ValueType.Null)
        {
            if (TryNamedArgsFromObject(stepObj, method, out args)
                && args.Count == method.ParameterNames.Count)
                return true;
            if (method.ParameterNames.Count == 0)
                return true;
            error = $"{path}.args must be an array.";
            return false;
        }

        if (argsVal.Type == ValueType.Array)
        {
            args.AddRange(argsVal.AsArray());
            if (args.Count == 1
                && TryAsJsonObject(args[0], out var named)
                && TryNamedArgsFromObject(named, method, out var expanded)
                && expanded.Count == method.ParameterNames.Count)
            {
                args = expanded;
            }

            return true;
        }

        if (argsVal.Type == ValueType.Object && TryAsJsonObject(argsVal, out var obj)
            && TryNamedArgsFromObject(obj, method, out args))
        {
            return true;
        }

        error = $"{path}.args must be an array.";
        return false;
    }

    private static bool TryNamedArgsFromObject(
        JsonObject obj,
        MaldaLang.Parser.AST.Declarations.ApiMethodSignature method,
        out List<RuntimeValue> args)
    {
        args = new List<RuntimeValue>();
        if (method.ParameterNames.Count == 0)
            return false;

        foreach (var name in method.ParameterNames)
        {
            var val = obj.Get(name);
            if (val.Type == ValueType.Null && !HasKey(obj, name))
                return false;
            args.Add(val);
        }

        return true;
    }

    private static bool TryNormalizeArg(
        RuntimeValue arg,
        ApiRegistry.ApiDefinition apiDef,
        string apiName,
        List<string> originalAliases,
        HashSet<string> usedAliases,
        List<JsonObject> emitted,
        ref int nestedSeq,
        string path,
        out RuntimeValue normalized,
        out string error)
    {
        error = "";
        arg = UnwrapTypeWrapper(arg);
        arg = CoerceNumericString(arg);

        if (TryReadResultRef(arg, originalAliases, path, out normalized, out error))
            return string.IsNullOrEmpty(error);

        if (TryAsJsonObject(arg, out var obj) && LooksLikeCall(obj, apiDef))
        {
            if (!TryReadCallName(obj, path, out var call, out error))
                return false;
            if (!apiDef.TryGetMethod(call, out var method))
            {
                error = $"{path} nested call '{call}' is not a method on api '{apiName}'.";
                return false;
            }

            if (!TryReadArgs(obj, method, path, out var rawArgs, out error))
                return false;

            var nestedArgs = new List<RuntimeValue>();
            for (int a = 0; a < rawArgs.Count; a++)
            {
                if (!TryNormalizeArg(
                        rawArgs[a],
                        apiDef,
                        apiName,
                        originalAliases,
                        usedAliases,
                        emitted,
                        ref nestedSeq,
                        $"{path}.args[{a}]",
                        out var nestedArg,
                        out error))
                {
                    return false;
                }

                nestedArgs.Add(nestedArg);
            }

            var alias = NextAlias("n", ref nestedSeq, usedAliases);
            emitted.Add(MakeStep(call, nestedArgs, alias));
            normalized = RuntimeValue.String("$" + alias);
            return true;
        }

        if (arg.Type == ValueType.Object)
        {
            error = $"{path} must be a JSON primitive, \"$alias\", or a nested {{call, args}} — not an object. Do not wrap values as {{type, value}}.";
            return false;
        }

        normalized = arg;
        return true;
    }

    private static bool TryReadResultRef(
        RuntimeValue arg,
        List<string> originalAliases,
        string path,
        out RuntimeValue normalized,
        out string error)
    {
        normalized = arg;
        error = "";

        if (IsAliasRef(arg, out _))
            return true;

        if (!TryAsJsonObject(arg, out var obj))
            return false;

        var refVal = ReadFirst(obj, "@ref", "ref");
        if (refVal.Type == ValueType.Null)
            return false;

        if (refVal.Type == ValueType.Integer)
        {
            var index = refVal.AsInteger();
            if (index < 0 || index >= originalAliases.Count)
            {
                error = $"{path} @ref {index} does not refer to a prior step.";
                return true;
            }

            normalized = RuntimeValue.String("$" + originalAliases[index]);
            return true;
        }

        if (refVal.Type == ValueType.String)
        {
            var s = refVal.AsString();
            if (s.StartsWith("$", StringComparison.Ordinal))
            {
                normalized = RuntimeValue.String(s);
                return true;
            }

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            {
                if (index < 0 || index >= originalAliases.Count)
                {
                    error = $"{path} @ref {index} does not refer to a prior step.";
                    return true;
                }

                normalized = RuntimeValue.String("$" + originalAliases[index]);
                return true;
            }

            normalized = RuntimeValue.String("$" + s);
            return true;
        }

        return false;
    }

    private static RuntimeValue UnwrapTypeWrapper(RuntimeValue value)
    {
        if (!TryAsJsonObject(value, out var obj))
            return value;

        var keys = obj.GetAllKeys().ToList();
        if (keys.Count == 0 || keys.Count > 3)
            return value;

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "type", "value", "kind", "data"
        };
        foreach (var key in keys)
        {
            if (!allowed.Contains(key))
                return value;
        }

        var inner = ReadFirst(obj, "value", "data");
        if (inner.Type == ValueType.Null)
            return value;

        var typeName = ReadString(obj, "type", "kind");
        if (!string.IsNullOrEmpty(typeName)
            && !IsJsonTypeName(typeName)
            && keys.Count > 1)
        {
            return value;
        }

        return UnwrapTypeWrapper(CoerceNumericString(inner));
    }

    private static bool IsJsonTypeName(string typeName)
    {
        return typeName.Equals("number", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("integer", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("int", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("float", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("double", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("string", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("boolean", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("bool", StringComparison.OrdinalIgnoreCase)
               || typeName.Equals("null", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonObject MakeStep(string call, List<RuntimeValue> args, string alias)
    {
        var step = new JsonObject();
        step.Set("call", RuntimeValue.String(call));
        step.Set("args", RuntimeValue.Array(new List<RuntimeValue>(args)));
        step.Set("as", RuntimeValue.String(alias));
        return step;
    }

    private static string NextAlias(string prefix, ref int seq, HashSet<string> used)
    {
        while (true)
        {
            var alias = prefix + seq.ToString(CultureInfo.InvariantCulture);
            seq++;
            if (used.Add(alias))
                return alias;
        }
    }

    private static bool TryAsJsonObject(RuntimeValue value, out JsonObject obj)
    {
        obj = null!;
        if (value.Type != ValueType.Object)
            return false;
        if (value.AsObject() is JsonObject json)
        {
            obj = json;
            return true;
        }

        if (value.AsObject() is DictionaryInstance dict)
        {
            obj = new JsonObject();
            foreach (var key in dict.Entries.Keys)
                obj.Set(key, dict.TryGetEntry(key, out var entry) ? entry : RuntimeValue.Null());
            return true;
        }

        return false;
    }

    private static bool HasKey(JsonObject obj, string name)
    {
        foreach (var key in obj.GetAllKeys())
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static RuntimeValue ReadFirst(JsonObject obj, params string[] names)
    {
        foreach (var name in names)
        {
            var val = obj.Get(name);
            if (val.Type != ValueType.Null || HasKey(obj, name))
                return val;
        }

        return RuntimeValue.Null();
    }

    private static string ReadString(JsonObject obj, params string[] names)
    {
        var val = ReadFirst(obj, names);
        return val.Type == ValueType.String ? val.AsString() : "";
    }
}
