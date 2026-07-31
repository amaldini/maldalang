// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public class ProgressContextWrapper : ObjectInstance
{
    private readonly ProgressContext _ctx;
    private readonly Dictionary<string, ProgressTask> _taskDict;
    
    public ProgressContextWrapper(ProgressContext ctx, Dictionary<string, ProgressTask> taskDict) : base(null)
    {
        _ctx = ctx;
        _taskDict = taskDict;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle method access - create a FunctionValue wrapper
        if (name == "addTask" || name == "increment" || name == "isFinished")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on ProgressContext.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        return methodName switch
        {
            "addTask" => CallAddTask(args),
            "increment" => CallIncrement(args),
            "isFinished" => CallIsFinished(args),
            _ => throw new Exception($"ProgressContext has no method '{methodName}'.")
        };
    }
    
    private RuntimeValue CallAddTask(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("addTask() expects 2 arguments: (name: string, maxValue: number)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("addTask() first argument must be a string");
        
        var taskName = args[0].AsString();
        
        // Handle maxValue - can be int or float
        int maxValue;
        if (args[1].Type == ValueType.Integer)
        {
            maxValue = args[1].AsInteger();
        }
        else if (args[1].Type == ValueType.Float)
        {
            maxValue = (int)args[1].AsFloat();
        }
        else
        {
            throw new Exception("addTask() second argument must be a number");
        }
        
        var progressTask = _ctx.AddTask(taskName, maxValue: maxValue);
        _taskDict[taskName] = progressTask;
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallIncrement(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("increment() expects 2 arguments: (taskName: string, value: number)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("increment() first argument must be a string");
        
        var taskName = args[0].AsString();
        
        if (!_taskDict.ContainsKey(taskName))
            throw new Exception($"Task '{taskName}' not found. Call addTask() first.");
        
        // Handle value - can be int or float
        double incrementValue;
        if (args[1].Type == ValueType.Integer)
        {
            incrementValue = args[1].AsInteger();
        }
        else if (args[1].Type == ValueType.Float)
        {
            incrementValue = args[1].AsFloat();
        }
        else
        {
            throw new Exception("increment() second argument must be a number");
        }
        
        var task = _taskDict[taskName];
        task.Increment(incrementValue);
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallIsFinished(List<RuntimeValue> args)
    {
        if (_taskDict.Count == 0)
            return RuntimeValue.Boolean(true);
        
        foreach (var task in _taskDict.Values)
        {
            if (!task.IsFinished)
                return RuntimeValue.Boolean(false);
        }
        
        return RuntimeValue.Boolean(true);
    }
}
