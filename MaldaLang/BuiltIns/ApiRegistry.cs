// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Registers <c>api</c> declarations for <c>prompt … -&gt; program(ApiName)</c> and <c>runProgram</c>.
/// </summary>
public static class ApiRegistry
{
    public sealed class ApiDefinition
    {
        public ApiDefinition(string name, IReadOnlyList<ApiMethodSignature> methods)
        {
            Name = name;
            Methods = methods;
        }

        public string Name { get; }
        public IReadOnlyList<ApiMethodSignature> Methods { get; }

        public bool TryGetMethod(string methodName, out ApiMethodSignature method)
        {
            foreach (var m in Methods)
            {
                if (string.Equals(m.Name, methodName, StringComparison.Ordinal))
                {
                    method = m;
                    return true;
                }
            }

            method = null!;
            return false;
        }

        /// <summary>
        /// Resolves a model-emitted call name onto a declared method.
        /// Exact match wins; otherwise a unique match after stripping underscores,
        /// an <c>Api.</c> prefix, case, and operator synonyms (<c>+</c> → add, <c>*</c> → mul).
        /// </summary>
        public bool TryResolveMethod(string methodName, out ApiMethodSignature method)
        {
            if (TryGetMethod(methodName, out method))
                return true;

            var incoming = CanonicalMethodKey(methodName, Name);
            if (string.IsNullOrEmpty(incoming))
            {
                method = null!;
                return false;
            }

            ApiMethodSignature? found = null;
            foreach (var m in Methods)
            {
                var declared = CanonicalMethodKey(m.Name, Name);
                if (!string.Equals(declared, incoming, StringComparison.Ordinal))
                    continue;

                if (found != null && !string.Equals(found.Name, m.Name, StringComparison.Ordinal))
                {
                    method = null!;
                    return false;
                }

                found = m;
            }

            if (found == null)
            {
                method = null!;
                return false;
            }

            method = found;
            return true;
        }

        internal static string CanonicalMethodKey(string methodName, string apiName)
        {
            var key = StripUnderscores(methodName.Trim().ToLowerInvariant());
            if (string.IsNullOrEmpty(key))
                return key;

            var apiKey = StripUnderscores(apiName.Trim().ToLowerInvariant());
            if (!string.IsNullOrEmpty(apiKey) && key.StartsWith(apiKey + ".", StringComparison.Ordinal))
                key = key.Substring(apiKey.Length + 1);

            return MapOperatorSynonym(key);
        }

        private static string StripUnderscores(string name)
        {
            if (name.IndexOf('_') < 0)
                return name;
            return name.Replace("_", "", StringComparison.Ordinal);
        }

        private static string MapOperatorSynonym(string key) => key switch
        {
            "+" or "plus" or "addition" or "sum" => "add",
            "-" or "minus" or "subtract" or "subtraction" or "difference" => "sub",
            "*" or "times" or "multiply" or "multiplication" or "product" => "mul",
            "/" or "divide" or "division" or "quotient" => "div",
            "%" or "mod" or "modulo" or "remainder" => "mod",
            _ => key
        };
    }

    private static readonly Dictionary<string, ApiDefinition> Definitions = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, RuntimeValue> Schemas = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Func<List<RuntimeValue>, RuntimeValue>> BoundImplementations =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Drops all registered apis. Top-level interpret runs this so re-running a
    /// program in the same process (Desktop/Web IDE) does not throw "already registered".
    /// Nested interpret (imports, runMALDA) must not call this.
    /// </summary>
    public static void Clear()
    {
        Definitions.Clear();
        Schemas.Clear();
        BoundImplementations.Clear();
    }

    public static void ClearForTesting() => Clear();

    public static bool IsRegistered(string name) => Definitions.ContainsKey(name);

    public static void Register(ApiDeclaration decl)
    {
        EnsureNameAvailable(decl.Name);
        var def = new ApiDefinition(decl.Name, decl.Methods.ToList());
        Definitions[decl.Name] = def;
        Schemas[decl.Name] = BuildProgramSchema(def);
    }

    public static void RegisterCompiled(string name, IReadOnlyList<ApiMethodSignature> methods)
    {
        EnsureNameAvailable(name);
        var def = new ApiDefinition(name, methods.ToList());
        Definitions[name] = def;
        Schemas[name] = BuildProgramSchema(def);
    }

    public static void BindImplementation(string methodName, Func<List<RuntimeValue>, RuntimeValue> impl)
    {
        BoundImplementations[methodName] = impl;
    }

    public static bool TryInvokeBound(string methodName, List<RuntimeValue> args, out RuntimeValue result)
    {
        if (BoundImplementations.TryGetValue(methodName, out var impl))
        {
            result = impl(args);
            return true;
        }

        result = RuntimeValue.Null();
        return false;
    }

    public static bool TryGet(string name, out ApiDefinition definition)
    {
        if (Definitions.TryGetValue(name, out definition!))
            return true;
        definition = null!;
        return false;
    }

    public static bool TryResolveProgramSchema(string apiName, out RuntimeValue schema)
    {
        if (Schemas.TryGetValue(apiName, out schema!))
            return true;
        schema = RuntimeValue.Null();
        return false;
    }

    public static bool TryResolveApiNameFromProgramJson(RuntimeValue programValue, out string apiName)
    {
        apiName = "";
        if (programValue.Type != ValueType.Object)
            return false;

        if (programValue.AsObject() is JsonObject json)
        {
            var tagged = json.Get("@api");
            if (tagged.Type == ValueType.String && !string.IsNullOrWhiteSpace(tagged.AsString()))
            {
                apiName = tagged.AsString();
                return true;
            }
        }

        if (Definitions.Count == 1)
        {
            apiName = Definitions.Keys.First();
            return true;
        }

        return false;
    }

    public static bool TryParseProgramReturnType(string returnType, out string apiName)
    {
        apiName = "";
        var trimmed = (returnType ?? "").Trim();
        const string prefix = "program(";
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal) || !trimmed.EndsWith(")", StringComparison.Ordinal))
            return false;
        var inner = trimmed.Substring(prefix.Length, trimmed.Length - prefix.Length - 1).Trim();
        if (string.IsNullOrEmpty(inner) || inner.Contains('(', StringComparison.Ordinal) || inner.Contains(')', StringComparison.Ordinal))
            return false;
        apiName = inner;
        return true;
    }

    public static RuntimeValue BuildProgramSchema(ApiDefinition def)
    {
        var methodNames = def.Methods.Select(m => RuntimeValue.String(m.Name)).ToList();
        var callEnum = new JsonObject();
        callEnum.Set("type", RuntimeValue.String("string"));
        callEnum.Set("enum", RuntimeValue.Array(methodNames));

        var valueSchema = MakeProgramValueSchema(def);

        var stepProps = new JsonObject();
        stepProps.Set("call", RuntimeValue.Object(callEnum));
        var argsSchema = new JsonObject();
        argsSchema.Set("type", RuntimeValue.String("array"));
        argsSchema.Set("items", RuntimeValue.Object(valueSchema));
        stepProps.Set("args", RuntimeValue.Object(argsSchema));
        stepProps.Set("as", RuntimeValue.Object(MakeTypeObject("string")));

        var stepItem = new JsonObject();
        stepItem.Set("type", RuntimeValue.String("object"));
        stepItem.Set("properties", RuntimeValue.Object(stepProps));
        stepItem.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("call"),
            RuntimeValue.String("args"),
            RuntimeValue.String("as")
        }));
        stepItem.Set("additionalProperties", RuntimeValue.Boolean(false));

        var stepsSchema = new JsonObject();
        stepsSchema.Set("type", RuntimeValue.String("array"));
        stepsSchema.Set("items", RuntimeValue.Object(stepItem));

        var apiConst = new JsonObject();
        apiConst.Set("type", RuntimeValue.String("string"));
        apiConst.Set("const", RuntimeValue.String(def.Name));

        var rootProps = new JsonObject();
        rootProps.Set("@api", RuntimeValue.Object(apiConst));
        rootProps.Set("steps", RuntimeValue.Object(stepsSchema));
        rootProps.Set("return", RuntimeValue.Object(valueSchema));

        var root = new JsonObject();
        root.Set("type", RuntimeValue.String("object"));
        root.Set("x-malda-kind", RuntimeValue.String("program"));
        root.Set("x-malda-api", RuntimeValue.String(def.Name));
        root.Set("properties", RuntimeValue.Object(rootProps));
        root.Set("required", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("@api"),
            RuntimeValue.String("steps"),
            RuntimeValue.String("return")
        }));
        root.Set("additionalProperties", RuntimeValue.Boolean(false));
        return RuntimeValue.Object(root);
    }

    private static void EnsureNameAvailable(string name)
    {
        if (SchemaRegistry.IsRegistered(name))
        {
            throw new Exception(
                $"Name '{name}' is already registered as a schema; cannot also declare an api.");
        }

        if (SumTypeRegistry.IsRegistered(name))
        {
            throw new Exception(
                $"Name '{name}' is already registered as a sum type; cannot also declare an api.");
        }

        if (Definitions.ContainsKey(name))
        {
            throw new Exception($"Api '{name}' is already registered.");
        }
    }

    private static JsonObject MakeProgramValueSchema(ApiDefinition def)
    {
        // No bare "object" unless a parameter is typed object/schema: structured-output
        // models otherwise emit {type,value} wrappers in args.
        var jsonTypes = new HashSet<string>(StringComparer.Ordinal);
        var anyUntyped = false;
        foreach (var method in def.Methods)
        {
            for (int i = 0; i < method.ParameterNames.Count; i++)
            {
                var typeName = method.ParameterTypeAt(i);
                if (string.IsNullOrEmpty(typeName))
                {
                    anyUntyped = true;
                    break;
                }

                jsonTypes.Add(JsonTypeForDeclaredParam(typeName));
            }

            if (anyUntyped)
                break;
        }

        if (anyUntyped || jsonTypes.Count == 0)
            return MakePermissiveProgramValueSchema();

        jsonTypes.Add("string");
        if (jsonTypes.Contains("number"))
            jsonTypes.Add("integer");

        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.Array(jsonTypes.Select(RuntimeValue.String).ToList()));
        return schema;
    }

    private static string JsonTypeForDeclaredParam(string typeName)
    {
        var trimmed = typeName.Trim();
        if (trimmed.EndsWith("[]", StringComparison.Ordinal))
            return "array";
        if (SchemaRegistry.TryMapPrimitiveJsonType(trimmed, out var jsonType))
            return jsonType;
        return "object";
    }

    private static JsonObject MakePermissiveProgramValueSchema()
    {
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.Array(new List<RuntimeValue>
        {
            RuntimeValue.String("string"),
            RuntimeValue.String("number"),
            RuntimeValue.String("integer"),
            RuntimeValue.String("boolean"),
            RuntimeValue.String("array"),
            RuntimeValue.String("null")
        }));
        return schema;
    }

    private static JsonObject MakeTypeObject(string typeName)
    {
        var obj = new JsonObject();
        obj.Set("type", RuntimeValue.String(typeName));
        return obj;
    }
}
