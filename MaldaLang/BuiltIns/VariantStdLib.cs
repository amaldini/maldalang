// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Phase 4.4: helpers for <c>result.*</c> and <c>option.*</c> stdlib (variant tags Ok/Err/Some/None).
/// </summary>
public static class VariantStdLib
{
    public const string OkTag = "Ok";
    public const string ErrTag = "Err";
    public const string SomeTag = "Some";
    public const string NoneTag = "None";

    public static RuntimeValue ResultOk(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("result.ok() expects 1 argument");
        return ResultOk(args[0]);
    }

    public static RuntimeValue ResultErr(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("result.err() expects 1 argument");
        return ResultErr(args[0]);
    }

    public static RuntimeValue OptionSome(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("option.some() expects 1 argument");
        return OptionSome(args[0]);
    }

    public static RuntimeValue OptionNone(List<RuntimeValue> args)
    {
        if (args.Count != 0) throw new Exception("option.none() expects 0 arguments");
        return OptionNone();
    }

    public static RuntimeValue ResultOk(RuntimeValue value) => RuntimeValue.Variant(OkTag, new List<RuntimeValue> { value });
    public static RuntimeValue ResultErr(RuntimeValue value) => RuntimeValue.Variant(ErrTag, new List<RuntimeValue> { value });
    public static RuntimeValue OptionSome(RuntimeValue value) => RuntimeValue.Variant(SomeTag, new List<RuntimeValue> { value });
    public static RuntimeValue OptionNone() => RuntimeValue.Variant(NoneTag, new List<RuntimeValue>());

    public static RuntimeValue ResultMap(List<RuntimeValue> args, Interpreter interpreter) =>
        MapVariant(args, interpreter, OkTag, ErrTag);

    public static RuntimeValue OptionMap(List<RuntimeValue> args, Interpreter interpreter) =>
        MapVariant(args, interpreter, SomeTag, NoneTag);

    public static RuntimeValue ResultAndThen(List<RuntimeValue> args, Interpreter interpreter) =>
        AndThenVariant(args, interpreter, OkTag, ErrTag, "result");

    public static RuntimeValue OptionAndThen(List<RuntimeValue> args, Interpreter interpreter) =>
        AndThenVariant(args, interpreter, SomeTag, NoneTag, "option");

    public static RuntimeValue ResultUnwrapOr(List<RuntimeValue> args) =>
        UnwrapOr(args, OkTag);

    public static RuntimeValue OptionUnwrapOr(List<RuntimeValue> args) =>
        UnwrapOr(args, SomeTag);

    public static RuntimeValue ResultIsOk(List<RuntimeValue> args) =>
        RuntimeValue.Boolean(IsTag(args, OkTag));

    public static RuntimeValue ResultIsErr(List<RuntimeValue> args) =>
        RuntimeValue.Boolean(IsTag(args, ErrTag));

    public static RuntimeValue OptionIsSome(List<RuntimeValue> args) =>
        RuntimeValue.Boolean(IsTag(args, SomeTag));

    public static RuntimeValue OptionIsNone(List<RuntimeValue> args) =>
        RuntimeValue.Boolean(IsTag(args, NoneTag));

    private static RuntimeValue MapVariant(
        List<RuntimeValue> args,
        Interpreter interpreter,
        string successTag,
        string failureTag)
    {
        if (args.Count != 2)
            throw new Exception("map() expects 2 arguments: (value, function)");
        var value = RequireVariant(args[0]);
        var mapper = RequireFunction(args[1], "map");

        if (value.Tag == failureTag)
            return args[0];

        if (value.Tag != successTag)
            throw new Exception($"map() expected variant tag '{successTag}' or '{failureTag}', got '{value.Tag}'");

        var mapped = interpreter.CallFunctionAsync(mapper, value.Payload).GetAwaiter().GetResult();
        return RuntimeValue.Variant(successTag, PayloadFromSingleOrList(mapped));
    }

    private static RuntimeValue AndThenVariant(
        List<RuntimeValue> args,
        Interpreter interpreter,
        string successTag,
        string failureTag,
        string moduleName)
    {
        BuiltInArity.Require("andThen", args, 2, 2, "value, function");
        var value = RequireVariant(args[0]);
        var binder = RequireFunction(args[1], "andThen");

        if (value.Tag == failureTag)
            return args[0];

        if (value.Tag != successTag)
            throw new Exception($"andThen() expected variant tag '{successTag}' or '{failureTag}', got '{value.Tag}'");

        var bound = interpreter.CallFunctionAsync(binder, value.Payload).GetAwaiter().GetResult();
        return RequireSameFamilyResult(bound, successTag, failureTag, moduleName);
    }

    private static RuntimeValue UnwrapOr(List<RuntimeValue> args, string successTag)
    {
        if (args.Count != 2)
            throw new Exception("unwrapOr() expects 2 arguments: (value, default)");
        var value = RequireVariant(args[0]);
        if (value.Tag == successTag && value.Payload.Count > 0)
            return value.Payload[0];
        return args[1];
    }

    private static bool IsTag(List<RuntimeValue> args, string tag)
    {
        if (args.Count != 1)
            throw new Exception("is*() expects 1 argument");
        return RequireVariant(args[0]).Tag == tag;
    }

    private static VariantValue RequireVariant(RuntimeValue value)
    {
        if (value.Type != ValueType.Variant)
            throw new Exception("Expected a variant value (Ok/Err/Some/None)");
        return value.AsVariant();
    }

    private static FunctionValue RequireFunction(RuntimeValue value, string methodName)
    {
        if (value.Type != ValueType.Function)
            throw new Exception($"Expected a function as second argument to {methodName}()");
        return value.AsFunction();
    }

    public static RuntimeValue RequireSameFamilyResult(
        RuntimeValue bound,
        string successTag,
        string failureTag,
        string moduleName)
    {
        if (bound.Type != ValueType.Variant)
        {
            throw new Exception(
                $"andThen() expected fn to return {successTag}/{failureTag}; got {bound.Type}. Use {moduleName}.map to transform a payload.");
        }

        var tag = bound.AsVariant().Tag;
        if (tag != successTag && tag != failureTag)
        {
            throw new Exception(
                $"andThen() expected fn to return {successTag}/{failureTag}; got '{tag}'. Use {moduleName}.map to transform a payload.");
        }

        return bound;
    }

    private static List<RuntimeValue> PayloadFromSingleOrList(RuntimeValue mapped)
    {
        if (mapped.Type == ValueType.Array)
            return mapped.AsArray();
        return new List<RuntimeValue> { mapped };
    }
}
