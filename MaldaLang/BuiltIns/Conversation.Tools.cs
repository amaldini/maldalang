// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance
{
    private RuntimeValue ExecuteSPLToolFunction(FunctionValue function, Interpreter interpreter, RuntimeValue arguments)
    {
        return ToolInstance.InvokeMaldaToolFunction(function, interpreter, arguments);
    }
    
    private RuntimeValue ExecuteTranspiledToolMethod(System.Reflection.MethodInfo method, RuntimeValue arguments)
    {
        try
        {
            // Extract arguments from the JSON object
            if (arguments.Type != ValueType.Object)
            {
                return RuntimeValue.String("Error: Tool arguments must be an object");
            }
            
            var argsObj = arguments.AsObject();
            var methodParams = method.GetParameters();
            var methodArgs = new List<object?>();
            
            // Map JSON object properties to method parameters
            foreach (var param in methodParams)
            {
                try
                {
                    var paramValue = argsObj.Get(param.Name ?? "", null);
                    if (paramValue == null || paramValue.Type == ValueType.Null)
                    {
                        // Parameter not provided - use default value or null
                        methodArgs.Add(param.HasDefaultValue ? param.DefaultValue : null);
                    }
                    else
                    {
                        // Convert RuntimeValue to appropriate type
                        var convertedValue = ConvertRuntimeValueToObject(paramValue, param.ParameterType);
                        methodArgs.Add(convertedValue);
                    }
                }
                catch
                {
                    // Parameter not found - use default value or null
                    methodArgs.Add(null);
                }
            }
            
            // Call the transpiled method (it returns Task<object>)
            var task = (Task<object>)method.Invoke(null, methodArgs.ToArray())!;
            var result = task.GetAwaiter().GetResult();
            
            // Convert result back to RuntimeValue
            return ConvertObjectToRuntimeValue(result);
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing transpiled tool method: {ex.Message}");
        }
    }
    
    private object? ConvertRuntimeValueToObject(RuntimeValue value, Type targetType)
    {
        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        
        return value.Type switch
        {
            ValueType.Integer => Convert.ChangeType(value.AsInteger(), underlyingType),
            ValueType.Float => Convert.ChangeType(value.AsFloat(), underlyingType),
            ValueType.String => value.AsString(),
            ValueType.Boolean => value.AsBoolean(),
            ValueType.Array => value.AsArray(),
            ValueType.Object => value.AsObject(),
            ValueType.Null => null,
            _ => null
        };
    }
    
    private RuntimeValue ConvertObjectToRuntimeValue(object? result)
    {
        return result switch
        {
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            MaldaLang.Interpreter.ObjectInstance oi => RuntimeValue.Object(oi),
            List<object> list => RuntimeValue.Array(list.Select(ConvertObjectToRuntimeValue).ToList()),
            _ => RuntimeValue.Null()
        };
    }
    
    private string? GetStringProperty(ObjectInstance obj, string name)
    {
        try
        {
            var prop = obj.Get(name, null);
            return prop?.AsString();
        }
        catch
        {
            return null;
        }
    }
    
    private RuntimeValue? GetProperty(ObjectInstance obj, string name)
    {
        try
        {
            return obj.Get(name, null);
        }
        catch
        {
            return null;
        }
    }

    private RuntimeValue ExecuteToolOperation(ToolInstance tool, RuntimeValue arguments)
    {
        try
        {
            // Check if tool is an AgentToolInstance (agent wrapped as tool)
            if (tool is AgentToolInstance agentTool)
            {
                // Execute agent tool directly
                return agentTool.Execute(arguments, null);
            }
            
            if (tool is MemoryProgressToolInstance memoryProgressTool)
            {
                return memoryProgressTool.ExecuteMemoryTool(arguments);
            }

            if (tool is DevAgentCodeMemoryToolInstance codeMemoryTool)
            {
                return codeMemoryTool.ExecuteCodeMemoryTool(arguments);
            }

            var rejected = tool.RejectIfAttachedSchemaFails(arguments);
            if (rejected != null)
                return rejected;
            
            // Check if tool has a transpiled method handler
            var transpiledMethod = tool.GetTranspiledMethod();
            if (transpiledMethod != null)
            {
                return ExecuteTranspiledToolMethod(transpiledMethod, arguments);
            }
            
            // Check if tool has a MALDA function handler
            var functionHandler = tool.GetFunctionHandler();
            var interpreter = tool.GetInterpreter();
            
            if (functionHandler != null && interpreter != null)
            {
                return ExecuteSPLToolFunction(functionHandler, interpreter, arguments);
            }
            
            // Extract arguments
            if (arguments.Type != ValueType.Object)
                return RuntimeValue.String("Error: Tool arguments must be an object");
            
            var argsObj = arguments.AsObject();
            
            // Validate path if tool has a working directory
            string? filePath = null;
            string? dirPath = null;
            
            try
            {
                var filePathVal = argsObj.Get("filePath", null);
                if (filePathVal != null && filePathVal.Type == ValueType.String)
                    filePath = filePathVal.AsString();
            }
            catch { }
            
            try
            {
                var dirPathVal = argsObj.Get("dirPath", null);
                if (dirPathVal != null && dirPathVal.Type == ValueType.String)
                    dirPath = dirPathVal.AsString();
            }
            catch { }
            
            var pathToCheck = filePath ?? dirPath;
            if (pathToCheck != null && !string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                var normalizedPath = tool.NormalizePathForWorkingDirectory(pathToCheck);
                if (normalizedPath == null)
                {
                    return RuntimeValue.String($"Error: Path '{pathToCheck}' is outside the allowed working directory '{tool.WorkingDirectory}'. Use a relative path (e.g. \"PRD.md\", \"snake.html\").");
                }
                if (filePath != null)
                    filePath = normalizedPath;
                else
                    dirPath = normalizedPath;
                pathToCheck = normalizedPath;
            }
            else if (pathToCheck != null && !tool.IsPathAllowed(pathToCheck))
            {
                return RuntimeValue.String($"Error: Path '{pathToCheck}' is outside the allowed working directory '{tool.WorkingDirectory}'");
            }
            
            // Resolve relative paths relative to working directory (disk or embed:)
            string? resolvedFilePath = null;
            string? resolvedDirPath = null;
            
            if (filePath != null && !string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                resolvedFilePath = tool.ResolvePathAgainstWorkingDirectory(filePath) ?? filePath;
            }
            else if (filePath != null)
            {
                resolvedFilePath = filePath;
            }
            
            if (dirPath != null && !string.IsNullOrEmpty(tool.WorkingDirectory))
            {
                resolvedDirPath = tool.ResolvePathAgainstWorkingDirectory(dirPath) ?? dirPath;
            }
            else if (dirPath != null)
            {
                resolvedDirPath = dirPath;
            }
            
            // Execute the appropriate built-in function based on tool name
            var canonicalName = CanonicalToolName(tool.Name);
            return ExecuteFileTool(canonicalName, tool, arguments, argsObj, resolvedFilePath, resolvedDirPath)
                ?? ExecuteWorkspaceTool(canonicalName, tool, arguments, argsObj, resolvedFilePath, resolvedDirPath)
                ?? ExecuteGitTool(canonicalName, tool, arguments, argsObj, resolvedFilePath, resolvedDirPath)
                ?? ExecuteMaldaTool(canonicalName, tool, arguments, argsObj, resolvedFilePath, resolvedDirPath)
                ?? ExecutePlanTool(canonicalName, tool, arguments, argsObj, resolvedFilePath, resolvedDirPath)
                ?? RuntimeValue.String($"Error: Unknown tool '{tool.Name}'");
        }
        catch (InputRequiredException)
        {
            // Re-throw InputRequiredException to let execution service handle it
            throw;
        }
        catch (Exception ex)
        {
            return RuntimeValue.String($"Error executing tool: {SafeDirectoryWalk.ShortMessage(ex.Message)}");
        }
    }
}
