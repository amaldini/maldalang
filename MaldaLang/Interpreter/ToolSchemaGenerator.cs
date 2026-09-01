// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;
using MaldaLang.BuiltIns;
using System.Reflection;

public static class ToolSchemaGenerator
{
    public static RuntimeValue GenerateSchema(FunctionDeclaration function, RuntimeValue? providedSchema = null)
    {
        // If schema is provided, validate and use it
        if (providedSchema != null && providedSchema.Type == ValueType.Object)
        {
            // Validate that it's a valid JSON schema object
            var schemaObj = providedSchema.AsObject();
            if (schemaObj is JsonObject jsonSchema)
            {
                // Check if it has the basic structure
                var type = jsonSchema.Get("type", null);
                if (type != null && type.Type == ValueType.String && type.AsString() == "object")
                {
                    return providedSchema;
                }
            }
            // If provided schema is not valid, fall back to auto-generation
        }
        
        // Auto-generate schema from function parameters
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        
        var properties = new JsonObject();
        var required = new List<RuntimeValue>();
        
        foreach (var param in function.Parameters)
        {
            var paramSchema = new JsonObject();
            paramSchema.Set("type", RuntimeValue.String("string"));
            paramSchema.Set("description", RuntimeValue.String($"Parameter: {param}"));
            properties.Set(param, RuntimeValue.Object(paramSchema));
            required.Add(RuntimeValue.String(param));
        }
        
        schema.Set("properties", RuntimeValue.Object(properties));
        
        if (required.Count > 0)
        {
            schema.Set("required", RuntimeValue.Array(required));
        }
        
        return RuntimeValue.Object(schema);
    }
    
    /// <summary>
    /// Generate schema from a transpiled method (MethodInfo) for tool registration.
    /// </summary>
    public static RuntimeValue GenerateSchemaFromMethod(MethodInfo method, RuntimeValue? providedSchema = null)
    {
        // If schema is provided, validate and use it
        if (providedSchema != null && providedSchema.Type == ValueType.Object)
        {
            var schemaObj = providedSchema.AsObject();
            if (schemaObj is JsonObject jsonSchema)
            {
                var type = jsonSchema.Get("type", null);
                if (type != null && type.Type == ValueType.String && type.AsString() == "object")
                {
                    return providedSchema;
                }
            }
        }
        
        // Auto-generate schema from method parameters
        var schema = new JsonObject();
        schema.Set("type", RuntimeValue.String("object"));
        
        var properties = new JsonObject();
        var required = new List<RuntimeValue>();
        
        foreach (var param in method.GetParameters())
        {
            var paramSchema = new JsonObject();
            paramSchema.Set("type", RuntimeValue.String("string"));
            paramSchema.Set("description", RuntimeValue.String($"Parameter: {param.Name}"));
            properties.Set(param.Name ?? "", RuntimeValue.Object(paramSchema));
            
            // Only add to required if parameter doesn't have a default value
            if (!param.HasDefaultValue)
            {
                required.Add(RuntimeValue.String(param.Name ?? ""));
            }
        }
        
        schema.Set("properties", RuntimeValue.Object(properties));
        
        if (required.Count > 0)
        {
            schema.Set("required", RuntimeValue.Array(required));
        }
        
        return RuntimeValue.Object(schema);
    }
    
    /// <summary>
    /// Register a transpiled tool from a method decorated with @Tool.
    /// </summary>
    public static void RegisterTranspiledTool(string toolName, string toolDescription, MethodInfo method, RuntimeValue? providedSchema = null)
    {
        // Generate schema
        var attached = providedSchema != null && providedSchema.Type == ValueType.Object;
        var finalSchema = GenerateSchemaFromMethod(method, providedSchema);
        
        // Create ToolInstance
        var tool = new ToolInstance();
        tool.Initialize(toolName, toolDescription, finalSchema, null, "");
        if (attached)
            tool.MarkAttachedSchema();
        tool.SetTranspiledMethod(method);
        
        // Register in ToolRegistry
        ToolRegistry.Instance.RegisterTool(tool);
    }
}