// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Wrapper object for the old progress syntax that allows reading and updating progress values.
/// When a value is set on this object, it updates the corresponding ProgressTask.
/// </summary>
public class ProgressWrapperObjectInstance : ObjectInstance
{
    private readonly Dictionary<string, ProgressTask> _taskDict;
    
    public ProgressWrapperObjectInstance(Dictionary<string, ProgressTask> taskDict) : base(null)
    {
        _taskDict = taskDict;
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Return the current value of the progress task
        if (_taskDict.ContainsKey(name))
        {
            var task = _taskDict[name];
            return RuntimeValue.Integer((int)task.Value);
        }
        
        return RuntimeValue.Null();
    }
    
    public override void Set(string name, RuntimeValue value)
    {
        // Update the progress task when a value is set
        if (_taskDict.ContainsKey(name))
        {
            var task = _taskDict[name];
            
            // Convert value to double
            double newValue;
            if (value.Type == ValueType.Integer)
            {
                newValue = value.AsInteger();
            }
            else if (value.Type == ValueType.Float)
            {
                newValue = value.AsFloat();
            }
            else
            {
                throw new Exception($"Cannot set progress value: expected number, got {value.Type}");
            }
            
            // Update the task value
            task.Value = newValue;
        }
        else
        {
            throw new Exception($"Progress task '{name}' not found.");
        }
    }
    
    public override IEnumerable<string> GetAllKeys()
    {
        return _taskDict.Keys;
    }
}
