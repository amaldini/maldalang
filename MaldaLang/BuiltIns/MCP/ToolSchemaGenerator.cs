// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.MCP;

using System.Text.Json;
using System.Reflection;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using ValueType = MaldaLang.Interpreter.ValueType;

public class MCPToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public FunctionValue? Function { get; set; } // Null for transpiled tools
    public string FunctionName { get; set; } = "";
    public JsonElement? CustomSchema { get; set; }
    public MethodInfo? TranspiledMethod { get; set; } // For transpiled tools
}

public static class ToolSchemaGenerator
{
    public static MCPToolDefinition? CreateToolDefinition(
        FunctionValue function,
        string functionName,
        Interpreter interpreter)
    {
        if (function.Declaration == null)
            return null;

        var decorator = function.Decorators?.FirstOrDefault(d => d.Name == "MCPTool");
        if (decorator == null || decorator.Arguments == null || decorator.Arguments.Count < 2)
            return null;

        // Extract tool name (first argument)
        var toolNameValue = EvaluateDecoratorArgument(decorator.Arguments[0], interpreter);
        if (toolNameValue.Type != ValueType.String)
            return null;
        var toolName = toolNameValue.AsString();

        // Extract description (second argument)
        var descriptionValue = EvaluateDecoratorArgument(decorator.Arguments[1], interpreter);
        if (descriptionValue.Type != ValueType.String)
            return null;
        var description = descriptionValue.AsString();

        // Extract optional custom schema (third argument)
        JsonElement? customSchema = null;
        if (decorator.Arguments.Count >= 3)
        {
            var schemaValue = EvaluateDecoratorArgument(decorator.Arguments[2], interpreter);
            if (schemaValue.Type == ValueType.String)
            {
                try
                {
                    var schemaJson = schemaValue.AsString();
                    customSchema = JsonDocument.Parse(schemaJson).RootElement;
                }
                catch
                {
                    // Invalid JSON schema, ignore
                }
            }
        }

        return new MCPToolDefinition
        {
            Name = toolName,
            Description = description,
            Function = function,
            FunctionName = functionName,
            CustomSchema = customSchema
        };
    }

    public static JsonElement GenerateToolSchema(MCPToolDefinition tool)
    {
        // If custom schema provided, use it
        if (tool.CustomSchema.HasValue)
        {
            return tool.CustomSchema.Value;
        }

        // Otherwise, generate schema from function parameters
        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>()
        };

        var properties = (Dictionary<string, object>)schema["properties"]!;
        var required = new List<string>();

        if (tool.TranspiledMethod != null)
        {
            // Generate schema from transpiled method
            foreach (var param in tool.TranspiledMethod.GetParameters())
            {
                var paramName = param.Name ?? "";
                properties[paramName] = new Dictionary<string, object>
                {
                    ["type"] = "string", // Default to string for simplicity
                    ["description"] = $"Parameter {paramName}"
                };
                required.Add(paramName);
            }
        }
        else if (tool.Function?.Declaration != null)
        {
            foreach (var paramName in tool.Function.Declaration.Parameters)
            {
                // Default: all parameters are strings
                properties[paramName] = new Dictionary<string, object>
                {
                    ["type"] = "string",
                    ["description"] = $"Parameter {paramName}"
                };
                required.Add(paramName);
            }
        }

        schema["required"] = required;

        return JsonDocument.Parse(JsonSerializer.Serialize(schema)).RootElement;
    }

    public static JsonElement GenerateMCPToolSchema(MCPToolDefinition tool)
    {
        var parameterSchema = GenerateToolSchema(tool);

        var toolSchema = new Dictionary<string, object>
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["inputSchema"] = parameterSchema
        };

        return JsonDocument.Parse(JsonSerializer.Serialize(toolSchema)).RootElement;
    }

    private static RuntimeValue EvaluateDecoratorArgument(Expression expr, Interpreter interpreter)
    {
        // For now, we'll evaluate simple literals
        // Similar to RestServer's EvaluateDecoratorArgument
        if (expr is LiteralExpression literal)
        {
            return interpreter.RuntimeValueFromLiteral(literal);
        }

        // For more complex expressions, we'd need to evaluate them
        // For now, throw an error for non-literal decorator arguments
        throw new Exception("Decorator arguments must be literals");
    }
}