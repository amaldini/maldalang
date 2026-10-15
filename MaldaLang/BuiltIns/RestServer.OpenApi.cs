// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;

public partial class RestServerInstance
{
    private string GenerateSwaggerJson()
    {
        var routes = _routeRegistry.GetAllRoutes();
        var swagger = new Dictionary<string, object>
        {
            ["openapi"] = "3.0.0",
            ["info"] = new Dictionary<string, object>
            {
                ["title"] = "MaldaLang REST API",
                ["version"] = "1.0.0",
                ["description"] = "Auto-generated API documentation"
            },
            ["servers"] = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["url"] = $"http://{_host}:{_port}",
                    ["description"] = "API Server"
                }
            },
            ["paths"] = new Dictionary<string, object>()
        };
        
        var paths = (Dictionary<string, object>)swagger["paths"];
        
        // Group routes by path
        var routesByPath = routes.GroupBy(r => r.PathPattern);
        
        foreach (var pathGroup in routesByPath)
        {
            var pathPattern = pathGroup.Key;
            // Convert MaldaLang path pattern to OpenAPI path: /api/users/{id}
            var openApiPath = pathPattern;
            
            var pathItem = new Dictionary<string, object>();
            
            foreach (var route in pathGroup)
            {
                var method = route.Method.ToLower();
                var operation = new Dictionary<string, object>
                {
                    ["operationId"] = route.FunctionName,
                    ["summary"] = $"{route.Method} {pathPattern}",
                    ["tags"] = new List<string> { ExtractTagFromPath(pathPattern) }
                };
                
                // Parameters
                var parameters = new List<Dictionary<string, object>>();
                
                // Path parameters
                foreach (var pathParamName in route.PathParameterNames)
                {
                    var pathParamSchema = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    };
                    if (TryGetValidationRule(route, "path", pathParamName, out var pathRuleSchema, out _))
                    {
                        MergeOpenApiSchema(pathParamSchema, pathRuleSchema);
                    }

                    parameters.Add(new Dictionary<string, object>
                    {
                        ["name"] = pathParamName,
                        ["in"] = "path",
                        ["required"] = true,
                        ["schema"] = pathParamSchema
                    });
                }
                
                // Query parameters
                var hasBodyParam = false;
                foreach (var paramName in route.ParameterNames)
                {
                    // Skip path parameters (already added)
                    if (route.PathParameterNames.Contains(paramName))
                        continue;
                    
                    // Check if it's a body parameter
                    if (paramName == "body" || IsBodyParameter(route, paramName))
                    {
                        hasBodyParam = true;
                        continue;
                    }
                    
                    // It's a query parameter
                    var queryParamSchema = new Dictionary<string, object>
                    {
                        ["type"] = "string"
                    };
                    var required = false;
                    if (TryGetValidationRule(route, "query", paramName, out var queryRuleSchema, out var queryRequired))
                    {
                        MergeOpenApiSchema(queryParamSchema, queryRuleSchema);
                        required = queryRequired;
                    }

                    parameters.Add(new Dictionary<string, object>
                    {
                        ["name"] = paramName,
                        ["in"] = "query",
                        ["required"] = required,
                        ["schema"] = queryParamSchema
                    });
                }
                
                if (parameters.Count > 0)
                {
                    operation["parameters"] = parameters;
                }
                
                // Request body
                if (hasBodyParam && (method == "post" || method == "put" || method == "patch"))
                {
                    var bodySchema = BuildOpenApiBodySchema(route);
                    operation["requestBody"] = new Dictionary<string, object>
                    {
                        ["required"] = bodySchema.ContainsKey("required") || HasRequiredBodyFields(route),
                        ["content"] = new Dictionary<string, object>
                        {
                            ["application/json"] = new Dictionary<string, object>
                            {
                                ["schema"] = bodySchema
                            }
                        }
                    };
                }
                
                // Responses
                operation["responses"] = new Dictionary<string, object>
                {
                    ["200"] = new Dictionary<string, object>
                    {
                        ["description"] = "Successful response",
                        ["content"] = new Dictionary<string, object>
                        {
                            ["application/json"] = new Dictionary<string, object>
                            {
                                ["schema"] = new Dictionary<string, object>
                                {
                                    ["type"] = "object"
                                }
                            }
                        }
                    }
                };
                
                pathItem[method] = operation;
            }
            
            paths[openApiPath] = pathItem;
        }
        
        return JsonSerializer.Serialize(swagger, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }
    
    private string ExtractTagFromPath(string path)
    {
        // Extract a tag from the path, e.g., /api/users -> "users"
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return parts[1]; // e.g., "users" from "/api/users"
        }
        return "default";
    }
    
    private bool IsBodyParameter(Route route, string paramName)
    {
        if (route.ParameterDecorators == null || route.ParameterDecorators.Count == 0)
        {
            // Default mode: "body" is the body parameter
            return paramName == "body";
        }
        
        // Decorator mode: check if parameter has @Body decorator
        var paramIndex = route.ParameterNames.IndexOf(paramName);
        if (paramIndex >= 0 && paramIndex < route.ParameterDecorators.Count)
        {
            var decorator = route.ParameterDecorators[paramIndex];
            return decorator?.Name == "Body";
        }
        
        return false;
    }

    private Dictionary<string, object> BuildOpenApiBodySchema(Route route)
    {
        var bodySchema = new Dictionary<string, object>
        {
            ["type"] = "object"
        };

        var section = GetValidationSection(route, "body");
        if (section == null)
        {
            return bodySchema;
        }

        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var field in section.GetProperties())
        {
            var fieldSchema = new Dictionary<string, object>
            {
                ["type"] = "string"
            };
            if (TryGetValidationRule(route, "body", field.Key, out var ruleSchema, out var isRequired))
            {
                MergeOpenApiSchema(fieldSchema, ruleSchema);
                if (isRequired)
                {
                    required.Add(field.Key);
                }
            }
            properties[field.Key] = fieldSchema;
        }

        if (properties.Count > 0)
        {
            bodySchema["properties"] = properties;
        }
        if (required.Count > 0)
        {
            bodySchema["required"] = required;
        }

        return bodySchema;
    }

    private bool HasRequiredBodyFields(Route route)
    {
        var section = GetValidationSection(route, "body");
        if (section == null)
        {
            return false;
        }

        foreach (var field in section.GetProperties())
        {
            if (TryGetValidationRule(route, "body", field.Key, out _, out var isRequired) && isRequired)
            {
                return true;
            }
        }

        return false;
    }

    private JsonObject? GetValidationSection(Route route, string sectionName)
    {
        var schema = NormalizeValidationSchema(route);
        if (schema == null)
        {
            return null;
        }

        var section = schema.Get(sectionName, null);
        if (section.Type == ValueType.Object && section.AsObject() is JsonObject sectionObj)
        {
            return sectionObj;
        }

        return null;
    }

    private JsonObject? NormalizeValidationSchema(Route route)
    {
        var schema = route.Metadata.ValidationSchema;
        if (schema.Type == ValueType.Null)
        {
            return null;
        }

        RuntimeValue normalizedSchema = schema;
        if (schema.Type == ValueType.String)
        {
            var raw = schema.AsString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                normalizedSchema = JsonToRuntimeValue(doc.RootElement);
            }
            catch
            {
                return null;
            }
        }

        if (normalizedSchema.Type == ValueType.Object && normalizedSchema.AsObject() is JsonObject schemaObj)
        {
            return schemaObj;
        }

        return null;
    }

    private bool TryGetValidationRule(
        Route route,
        string sectionName,
        string field,
        out Dictionary<string, object> openApiSchema,
        out bool required)
    {
        openApiSchema = new Dictionary<string, object>();
        required = false;

        var section = GetValidationSection(route, sectionName);
        if (section == null)
        {
            return false;
        }

        var ruleValue = section.Get(field, null);
        if (ruleValue.Type == ValueType.Null)
        {
            return false;
        }

        if (ruleValue.Type == ValueType.String)
        {
            ParseValidationRuleDsl(ruleValue.AsString(), openApiSchema, ref required);
            return true;
        }

        if (ruleValue.Type == ValueType.Object && ruleValue.AsObject() is JsonObject ruleObj)
        {
            ParseValidationRuleObject(ruleObj, openApiSchema, ref required);
            return true;
        }

        return false;
    }

    private static void ParseValidationRuleDsl(string rule, Dictionary<string, object> schema, ref bool required)
    {
        var parts = rule.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (part.Equals("required", StringComparison.OrdinalIgnoreCase))
            {
                required = true;
                continue;
            }

            if (part.StartsWith("min=", StringComparison.OrdinalIgnoreCase) && double.TryParse(part["min=".Length..], out var min))
            {
                schema["minimum"] = min;
                continue;
            }

            if (part.StartsWith("max=", StringComparison.OrdinalIgnoreCase) && double.TryParse(part["max=".Length..], out var max))
            {
                schema["maximum"] = max;
                continue;
            }

            if (part.StartsWith("minLength=", StringComparison.OrdinalIgnoreCase) && int.TryParse(part["minLength=".Length..], out var minLength))
            {
                schema["minLength"] = minLength;
                continue;
            }

            if (part.StartsWith("maxLength=", StringComparison.OrdinalIgnoreCase) && int.TryParse(part["maxLength=".Length..], out var maxLength))
            {
                schema["maxLength"] = maxLength;
                continue;
            }

            if (part.StartsWith("pattern=", StringComparison.OrdinalIgnoreCase))
            {
                schema["pattern"] = part["pattern=".Length..];
                continue;
            }

            schema["type"] = MapValidationTypeToOpenApiType(part);
        }
    }

    private static void ParseValidationRuleObject(JsonObject ruleObj, Dictionary<string, object> schema, ref bool required)
    {
        var type = ruleObj.Get("type", null);
        if (type.Type == ValueType.String)
        {
            schema["type"] = MapValidationTypeToOpenApiType(type.AsString());
        }

        var requiredValue = ruleObj.Get("required", null);
        if (requiredValue.Type == ValueType.Boolean)
        {
            required = requiredValue.AsBoolean();
        }

        var min = ruleObj.Get("min", null);
        if (min.Type == ValueType.Integer)
        {
            schema["minimum"] = min.AsInteger();
        }
        else if (min.Type == ValueType.Float)
        {
            schema["minimum"] = min.AsFloat();
        }

        var max = ruleObj.Get("max", null);
        if (max.Type == ValueType.Integer)
        {
            schema["maximum"] = max.AsInteger();
        }
        else if (max.Type == ValueType.Float)
        {
            schema["maximum"] = max.AsFloat();
        }

        var minLength = ruleObj.Get("minLength", null);
        if (minLength.Type == ValueType.Integer)
        {
            schema["minLength"] = minLength.AsInteger();
        }

        var maxLength = ruleObj.Get("maxLength", null);
        if (maxLength.Type == ValueType.Integer)
        {
            schema["maxLength"] = maxLength.AsInteger();
        }

        var pattern = ruleObj.Get("pattern", null);
        if (pattern.Type == ValueType.String)
        {
            schema["pattern"] = pattern.AsString();
        }
    }

    private static string MapValidationTypeToOpenApiType(string validationType)
    {
        var type = validationType.Trim().ToLowerInvariant();
        return type switch
        {
            "int" => "integer",
            "integer" => "integer",
            "float" => "number",
            "double" => "number",
            "number" => "number",
            "bool" => "boolean",
            "boolean" => "boolean",
            "object" => "object",
            "array" => "array",
            _ => "string"
        };
    }

    private static void MergeOpenApiSchema(Dictionary<string, object> target, Dictionary<string, object> source)
    {
        foreach (var kvp in source)
        {
            target[kvp.Key] = kvp.Value;
        }
    }
}
