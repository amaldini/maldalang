// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.BuiltIns;

public class ArrayInstance : ObjectInstance
{
    private readonly List<RuntimeValue> _elements;
    
    public ArrayInstance(List<RuntimeValue> elements)
        : base(ArrayClassDefinition.Instance)
    {
        _elements = elements;
    }
    
    public List<RuntimeValue> Elements => _elements;
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Support length property
        if (name == "length")
        {
            return RuntimeValue.Integer(_elements.Count);
        }
        
        // Support built-in array methods via the built-in method dispatch pipeline
        if (name is "append" or "pop" or "shift" or "concat" or
            "popOrNull" or "shiftOrNull" or "get" or "at" or
            "map" or "filter" or "reduce" or "forEach" or 
            "find" or "findIndex" or "some" or "every" or 
            "sort" or "reverse" or "slice" or "indexOf" or "includes" or "join" or
            "sum" or "average" or "min" or "max")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = name
            };
            return RuntimeValue.Function(wrapper);
        }
        
        throw new RuntimeException($"Undefined property '{name}' on Array.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> arguments, Interpreter? interpreter = null)
    {
        switch (methodName)
        {
            case "append":
                return CallAppend(arguments);
            case "pop":
                return CallPop(arguments);
            case "shift":
                return CallShift(arguments);
            case "concat":
                return CallConcat(arguments);
            case "popOrNull":
                return CallPopOrNull(arguments);
            case "shiftOrNull":
                return CallShiftOrNull(arguments);
            case "get":
                return CallGet(arguments);
            case "at":
                return CallAt(arguments);
            case "map":
                return CallMap(arguments, interpreter);
            case "filter":
                return CallFilter(arguments, interpreter);
            case "reduce":
                return CallReduce(arguments, interpreter);
            case "forEach":
                return CallForEach(arguments, interpreter);
            case "find":
                return CallFind(arguments, interpreter);
            case "findIndex":
                return CallFindIndex(arguments, interpreter);
            case "some":
                return CallSome(arguments, interpreter);
            case "every":
                return CallEvery(arguments, interpreter);
            case "sort":
                return CallSort(arguments, interpreter);
            case "reverse":
                return CallReverse(arguments);
            case "slice":
                return CallSlice(arguments);
            case "indexOf":
                return CallIndexOf(arguments);
            case "includes":
                return CallIncludes(arguments);
            case "join":
                return CallJoin(arguments);
            case "sum":
                return CallAggregateBuiltIn("sum", arguments, interpreter);
            case "average":
                return CallAggregateBuiltIn("average", arguments, interpreter);
            case "min":
                return CallAggregateBuiltIn("min", arguments, interpreter);
            case "max":
                return CallAggregateBuiltIn("max", arguments, interpreter);
            default:
                throw new RuntimeException($"Array has no method '{methodName}'.");
        }
    }
    
    private RuntimeValue CallAppend(List<RuntimeValue> arguments)
    {
        BuiltInArity.Require("append", arguments, 1, 1, "item");
        
        _elements.Add(arguments[0]);
        // Return the same array instance, matching existing append() semantics
        return RuntimeValue.Array(this);
    }
    
    private RuntimeValue CallPop(List<RuntimeValue> arguments)
    {
        BuiltInArity.Require("pop", arguments, 0, 0);
        
        if (_elements.Count == 0)
            throw new RuntimeException("Cannot pop from empty array");
        
        var lastIndex = _elements.Count - 1;
        var last = _elements[lastIndex];
        _elements.RemoveAt(lastIndex);
        return last;
    }
    
    private RuntimeValue CallShift(List<RuntimeValue> arguments)
    {
        BuiltInArity.Require("shift", arguments, 0, 0);
        
        if (_elements.Count == 0)
            throw new RuntimeException("Cannot shift from empty array");
        
        var first = _elements[0];
        _elements.RemoveAt(0);
        return first;
    }
    
    private RuntimeValue CallConcat(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("concat() expects 1 argument");
        
        var other = arguments[0];
        if (other.Type != ValueType.Array)
            throw new RuntimeException("concat() expects an array argument");
        
        var otherElements = other.AsArray();
        var combined = new List<RuntimeValue>(_elements.Count + otherElements.Count);
        combined.AddRange(_elements);
        combined.AddRange(otherElements);
        
        var newInstance = new ArrayInstance(combined);
        return RuntimeValue.Array(newInstance);
    }

    private RuntimeValue CallPopOrNull(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("popOrNull() expects 0 arguments");

        if (_elements.Count == 0)
            return RuntimeValue.Null();

        return CallPop(arguments);
    }

    private RuntimeValue CallShiftOrNull(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("shiftOrNull() expects 0 arguments");

        if (_elements.Count == 0)
            return RuntimeValue.Null();

        return CallShift(arguments);
    }

    private RuntimeValue CallGet(List<RuntimeValue> arguments)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("get() expects 1 or 2 arguments");

        if (!NumericCoercion.TryAsInteger(arguments[0], out var rawIndex))
            throw new RuntimeException("get() index must be an integer");

        var index = NormalizeIndex(rawIndex);
        if (index < 0 || index >= _elements.Count)
            return arguments.Count == 2 ? arguments[1] : RuntimeValue.Null();

        return _elements[index];
    }

    private RuntimeValue CallAt(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("at() expects 1 argument");

        if (!NumericCoercion.TryAsInteger(arguments[0], out var rawIndex))
            throw new RuntimeException("at() index must be an integer");

        var index = NormalizeIndex(rawIndex);
        if (index < 0 || index >= _elements.Count)
            return RuntimeValue.Null();

        return _elements[index];
    }
    
    private RuntimeValue CallLambda(FunctionValue lambda, List<RuntimeValue> args, Interpreter interpreter)
    {
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for lambda-based array methods");
        
        // Call the lambda function synchronously
        var task = interpreter.CallFunctionAsync(lambda, args);
        return task.GetAwaiter().GetResult();
    }
    
    private RuntimeValue CallMap(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("map() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for map()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("map() expects a function argument");
        
        var lambda = fn.AsFunction();
        var result = new List<RuntimeValue>(_elements.Count);
        
        foreach (var element in _elements)
        {
            var mapped = CallLambda(lambda, new List<RuntimeValue> { element }, interpreter);
            result.Add(mapped);
        }
        
        return RuntimeValue.Array(new ArrayInstance(result));
    }
    
    private RuntimeValue CallFilter(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("filter() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for filter()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("filter() expects a function argument");
        
        var lambda = fn.AsFunction();
        var result = new List<RuntimeValue>();
        
        foreach (var element in _elements)
        {
            var predicate = CallLambda(lambda, new List<RuntimeValue> { element }, interpreter);
            if (predicate.IsTruthy())
            {
                result.Add(element);
            }
        }
        
        return RuntimeValue.Array(new ArrayInstance(result));
    }
    
    private RuntimeValue CallReduce(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("reduce() expects 1 or 2 arguments");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for reduce()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("reduce() expects a function as first argument");
        
        var lambda = fn.AsFunction();
        
        if (_elements.Count == 0 && arguments.Count == 1)
            throw new RuntimeException("reduce() on empty array requires initial value");
        
        RuntimeValue accumulator;
        int startIndex;
        
        if (arguments.Count == 2)
        {
            accumulator = arguments[1];
            startIndex = 0;
        }
        else
        {
            accumulator = _elements[0];
            startIndex = 1;
        }
        
        for (int i = startIndex; i < _elements.Count; i++)
        {
            accumulator = CallLambda(lambda, new List<RuntimeValue> { accumulator, _elements[i] }, interpreter);
        }
        
        return accumulator;
    }
    
    private RuntimeValue CallForEach(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("forEach() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for forEach()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("forEach() expects a function argument");
        
        var lambda = fn.AsFunction();
        
        foreach (var element in _elements)
        {
            CallLambda(lambda, new List<RuntimeValue> { element }, interpreter);
        }
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallFind(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("find() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for find()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("find() expects a function argument");
        
        var lambda = fn.AsFunction();
        
        foreach (var element in _elements)
        {
            var predicate = CallLambda(lambda, new List<RuntimeValue> { element }, interpreter);
            if (predicate.IsTruthy())
            {
                return element;
            }
        }
        
        return RuntimeValue.Null();
    }
    
    private RuntimeValue CallFindIndex(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("findIndex() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for findIndex()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("findIndex() expects a function argument");
        
        var lambda = fn.AsFunction();
        
        for (int i = 0; i < _elements.Count; i++)
        {
            var predicate = CallLambda(lambda, new List<RuntimeValue> { _elements[i] }, interpreter);
            if (predicate.IsTruthy())
            {
                return RuntimeValue.Integer(i);
            }
        }
        
        return RuntimeValue.Integer(-1);
    }
    
    private RuntimeValue CallSome(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("some() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for some()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("some() expects a function argument");
        
        var lambda = fn.AsFunction();
        
        foreach (var element in _elements)
        {
            var predicate = CallLambda(lambda, new List<RuntimeValue> { element }, interpreter);
            if (predicate.IsTruthy())
            {
                return RuntimeValue.Boolean(true);
            }
        }
        
        return RuntimeValue.Boolean(false);
    }
    
    private RuntimeValue CallEvery(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("every() expects 1 argument");
        
        if (interpreter == null)
            throw new RuntimeException("Interpreter is required for every()");
        
        var fn = arguments[0];
        if (fn.Type != ValueType.Function)
            throw new RuntimeException("every() expects a function argument");
        
        var lambda = fn.AsFunction();
        
        foreach (var element in _elements)
        {
            var predicate = CallLambda(lambda, new List<RuntimeValue> { element }, interpreter);
            if (!predicate.IsTruthy())
            {
                return RuntimeValue.Boolean(false);
            }
        }
        
        return RuntimeValue.Boolean(true);
    }
    
    private RuntimeValue CallSort(List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count > 1)
            throw new RuntimeException("sort() expects 0 or 1 argument");
        
        if (arguments.Count == 0)
        {
            // Default sort: compare as strings/numbers
            _elements.Sort((a, b) =>
            {
                // Try numeric comparison first
                if (a.Type == ValueType.Integer && b.Type == ValueType.Integer)
                    return a.AsInteger().CompareTo(b.AsInteger());
                if (a.Type == ValueType.Float && b.Type == ValueType.Float)
                    return a.AsFloat().CompareTo(b.AsFloat());
                if ((a.Type == ValueType.Integer || a.Type == ValueType.Float) &&
                    (b.Type == ValueType.Integer || b.Type == ValueType.Float))
                {
                    var aNum = a.Type == ValueType.Integer ? (double)a.AsInteger() : a.AsFloat();
                    var bNum = b.Type == ValueType.Integer ? (double)b.AsInteger() : b.AsFloat();
                    return aNum.CompareTo(bNum);
                }
                // Fall back to string comparison
                return a.ToString().CompareTo(b.ToString());
            });
        }
        else
        {
            // Custom comparator
            if (interpreter == null)
                throw new RuntimeException("Interpreter is required for sort() with comparator");
            
            var fn = arguments[0];
            if (fn.Type != ValueType.Function)
                throw new RuntimeException("sort() expects a function argument");
            
            var lambda = fn.AsFunction();
            
            // Create a copy of elements with indices for stable sort
            var indexed = _elements.Select((elem, idx) => new { Element = elem, Index = idx }).ToList();
            
            indexed.Sort((a, b) =>
            {
                var result = CallLambda(lambda, new List<RuntimeValue> { a.Element, b.Element }, interpreter);
                
                // Convert result to integer
                if (result.Type == ValueType.Integer)
                    return result.AsInteger();
                if (result.Type == ValueType.Float)
                    return (int)result.AsFloat();
                if (result.Type == ValueType.Boolean)
                    return result.AsBoolean() ? 1 : -1;
                
                // Default: compare as truthy/falsy
                return result.IsTruthy() ? 1 : -1;
            });
            
            // Update elements in place
            for (int i = 0; i < indexed.Count; i++)
            {
                _elements[i] = indexed[i].Element;
            }
        }
        
        return RuntimeValue.Array(this);
    }
    
    private RuntimeValue CallReverse(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 0)
            throw new RuntimeException("reverse() expects 0 arguments");
        
        _elements.Reverse();
        return RuntimeValue.Array(this);
    }
    
    private RuntimeValue CallSlice(List<RuntimeValue> arguments)
    {
        if (arguments.Count < 1 || arguments.Count > 2)
            throw new RuntimeException("slice() expects 1 or 2 arguments");
        
        if (!NumericCoercion.TryAsInteger(arguments[0], out var startIndex))
            throw new RuntimeException("slice() start index must be an integer");

        if (startIndex < 0)
            startIndex = _elements.Count + startIndex; // Negative index support
        if (startIndex < 0)
            startIndex = 0;
        if (startIndex > _elements.Count)
            startIndex = _elements.Count;
        
        int endIndex = _elements.Count;
        if (arguments.Count == 2)
        {
            if (!NumericCoercion.TryAsInteger(arguments[1], out endIndex))
                throw new RuntimeException("slice() end index must be an integer");

            if (endIndex < 0)
                endIndex = _elements.Count + endIndex; // Negative index support
            if (endIndex < 0)
                endIndex = 0;
            if (endIndex > _elements.Count)
                endIndex = _elements.Count;
        }
        
        if (startIndex >= endIndex)
            return RuntimeValue.Array(new ArrayInstance(new List<RuntimeValue>()));
        
        var result = new List<RuntimeValue>(endIndex - startIndex);
        for (int i = startIndex; i < endIndex; i++)
        {
            result.Add(_elements[i]);
        }
        
        return RuntimeValue.Array(new ArrayInstance(result));
    }
    
    private RuntimeValue CallIndexOf(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("indexOf() expects 1 argument");
        
        var searchValue = arguments[0];
        
        for (int i = 0; i < _elements.Count; i++)
        {
            if (AreEqual(_elements[i], searchValue))
            {
                return RuntimeValue.Integer(i);
            }
        }
        
        return RuntimeValue.Integer(-1);
    }
    
    private RuntimeValue CallIncludes(List<RuntimeValue> arguments)
    {
        if (arguments.Count != 1)
            throw new RuntimeException("includes() expects 1 argument");
        
        var searchValue = arguments[0];
        
        foreach (var element in _elements)
        {
            if (AreEqual(element, searchValue))
            {
                return RuntimeValue.Boolean(true);
            }
        }
        
        return RuntimeValue.Boolean(false);
    }
    
    private RuntimeValue CallJoin(List<RuntimeValue> arguments)
    {
        var separator = arguments.Count > 0 && arguments[0].Type == ValueType.String
            ? arguments[0].AsString()
            : ",";
        
        var parts = new List<string>();
        foreach (var element in _elements)
        {
            parts.Add(element.ToString());
        }
        
        return RuntimeValue.String(string.Join(separator, parts));
    }

    private RuntimeValue CallAggregateBuiltIn(string builtInName, List<RuntimeValue> arguments, Interpreter? interpreter)
    {
        if (arguments.Count != 0)
            throw new RuntimeException($"{builtInName}() expects 0 arguments");

        return MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(
            builtInName,
            new List<RuntimeValue> { RuntimeValue.Array(this) },
            interpreter);
    }
    
    private bool AreEqual(RuntimeValue a, RuntimeValue b)
    {
        // Use same equality logic as == operator
        if (a.Type == b.Type)
        {
            return a.Type switch
            {
                ValueType.Integer => a.AsInteger() == b.AsInteger(),
                ValueType.Float => a.AsFloat() == b.AsFloat(),
                ValueType.String => a.AsString() == b.AsString(),
                ValueType.Boolean => a.AsBoolean() == b.AsBoolean(),
                ValueType.Null => true,
                ValueType.Array => a.AsArray() == b.AsArray(), // Reference equality for arrays
                ValueType.Object => a.AsObject() == b.AsObject(), // Reference equality for objects
                _ => false
            };
        }
        
        // Type coercion: int <-> float
        if ((a.Type == ValueType.Integer && b.Type == ValueType.Float) ||
            (a.Type == ValueType.Float && b.Type == ValueType.Integer))
        {
            var aNum = a.Type == ValueType.Integer ? (double)a.AsInteger() : a.AsFloat();
            var bNum = b.Type == ValueType.Integer ? (double)b.AsInteger() : b.AsFloat();
            return aNum == bNum;
        }
        
        return false;
    }

    private int NormalizeIndex(int index)
    {
        if (index < 0)
        {
            return _elements.Count + index;
        }

        return index;
    }
}
