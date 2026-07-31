// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class VariantValue
{
    public string Tag { get; }
    public List<RuntimeValue> Payload { get; }

    public VariantValue(string tag, List<RuntimeValue> payload)
    {
        Tag = tag;
        Payload = payload ?? new List<RuntimeValue>();
    }

    public override string ToString() => Tag + "(" + string.Join(", ", Payload.Select(v => v.ToString())) + ")";
}

public class RuntimeValue
{
    public ValueType Type { get; }
    public object? Value { get; }
    
    private RuntimeValue(ValueType type, object? value)
    {
        Type = type;
        Value = value;
    }
    
    public static RuntimeValue Integer(int value) => new(ValueType.Integer, value);
    public static RuntimeValue Float(double value) => new(ValueType.Float, value);
    public static RuntimeValue String(string value) => new(ValueType.String, value);
    public static RuntimeValue Boolean(bool value) => new(ValueType.Boolean, value);
    public static RuntimeValue Object(ObjectInstance value) => new(ValueType.Object, value);
    public static RuntimeValue Array(List<RuntimeValue> value) => new(ValueType.Array, new ArrayInstance(value));
    public static RuntimeValue Array(ArrayInstance value) => new(ValueType.Array, value);
    public static RuntimeValue Null() => new(ValueType.Null, null);
    public static RuntimeValue Function(FunctionValue value) => new(ValueType.Function, value);
    public static RuntimeValue Prompt(PromptValue value) => new(ValueType.Prompt, value);
    public static RuntimeValue Class(ClassDefinition value) => new(ValueType.Class, value);
    public static RuntimeValue ActorReference(ActorReference value) => new(ValueType.ActorReference, value);
    public static RuntimeValue Actor(ActorDefinition value) => new(ValueType.Actor, value);
    public static RuntimeValue Variant(string tag, List<RuntimeValue> payload) => new(ValueType.Variant, new VariantValue(tag, payload));
    public static RuntimeValue Variant(VariantValue value) => new(ValueType.Variant, value);
    public static RuntimeValue Task(Task<RuntimeValue> task) => new(ValueType.Task, task ?? throw new System.ArgumentNullException(nameof(task)));

    public int AsInteger() => (int)(Value ?? 0);
    public double AsFloat() => (double)(Value ?? 0.0);
    public string AsString() => (string)(Value ?? "");
    public bool AsBoolean() => (bool)(Value ?? false);
    public ObjectInstance AsObject() => (ObjectInstance)(Value ?? throw new RuntimeException("Value is not an object"));
    public List<RuntimeValue> AsArray() => Value switch
    {
        List<RuntimeValue> list => list,
        ArrayInstance arrayInstance => arrayInstance.Elements,
        _ => throw new RuntimeException("Value is not an array")
    };
    public ArrayInstance AsArrayInstance() => Value switch
    {
        ArrayInstance arrayInstance => arrayInstance,
        List<RuntimeValue> list => new ArrayInstance(list),
        _ => throw new RuntimeException("Value is not an array")
    };
    public FunctionValue AsFunction() => (FunctionValue)(Value ?? throw new RuntimeException("Value is not a function"));
    public PromptValue AsPrompt() => (PromptValue)(Value ?? throw new RuntimeException("Value is not a prompt"));
    public ClassDefinition AsClass() => (ClassDefinition)(Value ?? throw new RuntimeException("Value is not a class"));
    public ActorReference AsActorReference() => (ActorReference)(Value ?? throw new RuntimeException("Value is not an actor reference"));
    public ActorDefinition AsActor() => (ActorDefinition)(Value ?? throw new RuntimeException("Value is not an actor"));
    public VariantValue AsVariant() => (VariantValue)(Value ?? throw new RuntimeException("Value is not a variant"));
    public Task<RuntimeValue> AsTask() => (Task<RuntimeValue>)(Value ?? throw new RuntimeException("Value is not a task"));

    public bool IsTruthy()
    {
        return Type switch
        {
            ValueType.Null => false,
            ValueType.Boolean => AsBoolean(),
            _ => true
        };
    }
    
    public override string ToString()
    {
        return Type switch
        {
            ValueType.Integer => AsInteger().ToString(),
            ValueType.Float => AsFloat().ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            ValueType.String => AsString(),
            ValueType.Boolean => AsBoolean().ToString().ToLower(),
            ValueType.Null => "null",
            ValueType.Object => AsObject().ToString(),
            ValueType.Array => "[" + string.Join(", ", AsArray().Select(v => v.ToString())) + "]",
            ValueType.Function => "<function>",
            ValueType.Class => "<class>",
            ValueType.Variant => AsVariant().ToString(),
            ValueType.Task => "<task>",
            _ => "unknown"
        };
    }
}

public enum ValueType
{
    Integer,
    Float,
    String,
    Boolean,
    Object,
    Array,
    Null,
    Function,
    Prompt,
    Class,
    ActorReference,
    Actor,
    Variant,
    Task
}