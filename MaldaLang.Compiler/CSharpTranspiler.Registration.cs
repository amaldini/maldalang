// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private void GenerateDecoratorRegistration(List<Statement> statements)
    {
        WriteIndent();
        _output.AppendLine("private static void RegisterDecoratedFunctions()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        // Use reflection to find methods with Tool or MCPTool attributes and register them
        WriteIndent();
        _output.AppendLine("var assembly = typeof(Program).Assembly;");
        WriteIndent();
        _output.AppendLine("var methods = typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);");
        WriteIndent();
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("foreach (var method in methods)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Check for custom attributes (decorators)");
        WriteIndent();
        _output.AppendLine("var attributes = method.GetCustomAttributes(false);");
        WriteIndent();
        _output.AppendLine("string? routeGroup = null;");
        WriteIndent();
        _output.AppendLine("string? routeVersion = null;");
        WriteIndent();
        _output.AppendLine("string? routeValidationSchema = null;");
        WriteIndent();
        _output.AppendLine("var routeMiddleware = new List<string>();");
        WriteIndent();
        _output.AppendLine("// Pre-scan metadata decorators so registration is order-independent.");
        WriteIndent();
        _output.AppendLine("foreach (var routeAttr in attributes)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var routeAttrTypeName = routeAttr.GetType().Name;");
        WriteIndent();
        _output.AppendLine("if (!routeAttrTypeName.EndsWith(\"Attribute\")) continue;");
        WriteIndent();
        _output.AppendLine("var routeDecoratorName = routeAttrTypeName.Substring(0, routeAttrTypeName.Length - 9);");
        WriteIndent();
        _output.AppendLine("var routeArgsProp = routeAttr.GetType().GetProperty(\"Arguments\");");
        WriteIndent();
        _output.AppendLine("if (routeArgsProp == null) continue;");
        WriteIndent();
        _output.AppendLine("var routeArgs = routeArgsProp.GetValue(routeAttr) as object[];");
        WriteIndent();
        _output.AppendLine("if (routeDecoratorName == \"RouteGroup\" || routeDecoratorName == \"Group\" || routeDecoratorName == \"Prefix\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null && routeArgs.Length > 0) routeGroup = routeArgs[0]?.ToString();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeDecoratorName == \"Version\" || routeDecoratorName == \"ApiVersion\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null && routeArgs.Length > 0) routeVersion = routeArgs[0]?.ToString();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeDecoratorName == \"Use\" || routeDecoratorName == \"Middleware\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("foreach (var routeMiddlewareName in routeArgs)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var routeMiddlewareText = routeMiddlewareName?.ToString();");
        WriteIndent();
        _output.AppendLine("if (!string.IsNullOrWhiteSpace(routeMiddlewareText))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("routeMiddleware.Add(routeMiddlewareText);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeDecoratorName == \"Validate\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs != null && routeArgs.Length > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (routeArgs[0] is string routeValidationText)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("routeValidationSchema = routeValidationText;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (routeArgs[0] != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("routeValidationSchema = System.Text.Json.JsonSerializer.Serialize(routeArgs[0]);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("foreach (var attr in attributes)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Get attribute type name (e.g., \"ToolAttribute\" -> \"Tool\")");
        WriteIndent();
        _output.AppendLine("var attrTypeName = attr.GetType().Name;");
        WriteIndent();
        _output.AppendLine("if (attrTypeName.EndsWith(\"Attribute\"))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var decoratorName = attrTypeName.Substring(0, attrTypeName.Length - 9);");
        WriteIndent();
        _output.AppendLine("// Get Arguments property via reflection");
        WriteIndent();
        _output.AppendLine("var argsProp = attr.GetType().GetProperty(\"Arguments\");");
        WriteIndent();
        _output.AppendLine("if (argsProp != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var args = argsProp.GetValue(attr) as object[];");
        WriteIndent();
        _output.AppendLine("if (args != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Handle PAGE/COMPONENT/LIVE decorators for HttpServer routes");
        WriteIndent();
        _output.AppendLine("if (decoratorName == \"PAGE\" || decoratorName == \"COMPONENT\" || decoratorName == \"LIVE\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Extract path from first argument");
        WriteIndent();
        _output.AppendLine("var defaultPath = decoratorName == \"COMPONENT\" ? \"/components/\" + method.Name : (decoratorName == \"LIVE\" ? \"/components/\" + method.Name + \"/live\" : \"/\");");
        WriteIndent();
        _output.AppendLine("var path = args.Length > 0 ? (args[0]?.ToString() ?? defaultPath) : defaultPath;");
        WriteIndent();
        _output.AppendLine("// Get parameter names from method");
        WriteIndent();
        _output.AppendLine("var paramNames = method.GetParameters().Select(p => p.Name ?? \"\").ToList();");
        WriteIndent();
        _output.AppendLine("// Register route with all HttpServer instances");
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.HttpServerInstance.RegisterTranspiledRoute(");
        WriteIndent();
        _output.AppendLine("    \"GET\", path, method.Name, paramNames, null, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle AIPAGE decorator for HttpServer routes");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"AIPAGE\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Extract path from first argument and description from second");
        WriteIndent();
        _output.AppendLine("var path = args.Length > 0 ? (args[0]?.ToString() ?? \"/\") : \"/\";");
        WriteIndent();
        _output.AppendLine("var description = args.Length > 1 ? args[1]?.ToString() ?? \"\" : \"\";");
        WriteIndent();
        _output.AppendLine("// Get parameter names from method");
        WriteIndent();
        _output.AppendLine("var paramNames = method.GetParameters().Select(p => p.Name ?? \"\").ToList();");
        WriteIndent();
        _output.AppendLine("// Register AIPAGE route with description");
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.HttpServerInstance.RegisterTranspiledAIPage(");
        WriteIndent();
        _output.AppendLine("    path, method.Name, paramNames, description, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle REST API decorators (GET, POST, PUT, DELETE, PATCH, OPTIONS) and ACTION decorator");
        WriteIndent();
        _output.AppendLine("// Note: GET/POST and ACTION are also registered with HttpServer");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"GET\" || decoratorName == \"POST\" || decoratorName == \"PUT\" || ");
        WriteIndent();
        _output.AppendLine("         decoratorName == \"DELETE\" || decoratorName == \"PATCH\" || decoratorName == \"OPTIONS\" || decoratorName == \"ACTION\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Extract path from first argument");
        WriteIndent();
        _output.AppendLine("var defaultPath = decoratorName == \"ACTION\" ? \"/components/\" + method.Name + \"/action\" : \"/\";");
        WriteIndent();
        _output.AppendLine("var path = args.Length > 0 ? (args[0]?.ToString() ?? defaultPath) : defaultPath;");
        WriteIndent();
        _output.AppendLine("// Get parameter names from method");
        WriteIndent();
        _output.AppendLine("var paramNames = method.GetParameters().Select(p => p.Name ?? \"\").ToList();");
        WriteIndent();
        _output.AppendLine("// Register route with all RestServer instances");
        WriteIndent();
        _output.AppendLine("if (decoratorName != \"ACTION\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.RestServerInstance.RegisterTranspiledRoute(");
        WriteIndent();
        _output.AppendLine("    decoratorName, path, method.Name, paramNames, null, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Also register with HttpServer");
        WriteIndent();
        _output.AppendLine("if (decoratorName == \"GET\" || decoratorName == \"POST\" || decoratorName == \"ACTION\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var httpMethod = decoratorName == \"ACTION\" ? \"POST\" : decoratorName;");
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.HttpServerInstance.RegisterTranspiledRoute(");
        WriteIndent();
        _output.AppendLine("    httpMethod, path, method.Name, paramNames, null, routeGroup, routeVersion, routeMiddleware, routeValidationSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle Tool decorator for tool registration");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"Tool\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// @Tool decorator requires at least 2 arguments: name and description");
        WriteIndent();
        _output.AppendLine("if (args.Length >= 2)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var toolName = args[0]?.ToString() ?? method.Name;");
        WriteIndent();
        _output.AppendLine("var toolDescription = args[1]?.ToString() ?? \"\";");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? providedSchema = null;");
        WriteIndent();
        _output.AppendLine("if (args.Length >= 3 && args[2] != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("providedSchema = MaldaLang.BuiltIns.ToolSchemaResolver.Resolve(");
        WriteIndent();
        _output.AppendLine("    MaldaLang.Interpreter.RuntimeValue.String(args[2].ToString() ?? \"\"));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Register tool");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ToolSchemaGenerator.RegisterTranspiledTool(");
        WriteIndent();
        _output.AppendLine("    toolName, toolDescription, method, providedSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Handle MCPTool decorator (same as Tool for now)");
        WriteIndent();
        _output.AppendLine("else if (decoratorName == \"MCPTool\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// @MCPTool decorator requires at least 2 arguments: name and description");
        WriteIndent();
        _output.AppendLine("if (args.Length >= 2)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var toolName = args[0]?.ToString() ?? method.Name;");
        WriteIndent();
        _output.AppendLine("var toolDescription = args[1]?.ToString() ?? \"\";");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? providedSchema = null;");
        WriteIndent();
        _output.AppendLine("if (args.Length >= 3 && args[2] != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("providedSchema = MaldaLang.BuiltIns.ToolSchemaResolver.Resolve(");
        WriteIndent();
        _output.AppendLine("    MaldaLang.Interpreter.RuntimeValue.String(args[2].ToString() ?? \"\"));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// Register tool (MCPTool is registered the same way as Tool)");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ToolSchemaGenerator.RegisterTranspiledTool(");
        WriteIndent();
        _output.AppendLine("    toolName, toolDescription, method, providedSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        // Close foreach (var method in methods) loop
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        // Close RegisterDecoratedFunctions method
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private static string GetWorkflowRunnerMethodName(string workflowName) => "__workflow_" + workflowName;

    private static string GetPropertyRunnerMethodName(string propertyName) => "__property_" + propertyName;

    private void GenerateWorkflowRegistration(List<WorkflowDeclaration> workflows)
    {
        WriteIndent();
        _output.AppendLine("private static void RegisterTranspiledWorkflows()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.BuiltIns.BuiltInFunctions.ClearTranspiledWorkflowRunners();");
        foreach (var workflow in workflows)
        {
            WriteIndent();
            _output.Append("MaldaLang.BuiltIns.BuiltInFunctions.RegisterTranspiledWorkflowRunner(\"");
            _output.Append(workflow.Name);
            _output.Append("\", async (__input, __instanceId) => RuntimeHelpers.ToRuntimeValue(await ");
            _output.Append(EscapeIdentifier(GetWorkflowRunnerMethodName(workflow.Name)));
            _output.AppendLine("(RuntimeHelpers.UnwrapRuntimeValue(__input), __instanceId)));");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void GenerateTranspiledPropertyRegistry(List<PropertyDeclaration> properties)
    {
        WriteIndent();
        _output.AppendLine("public sealed class TranspiledPropertyMetadata");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("public string Name { get; init; } = string.Empty;");
        WriteIndent();
        _output.AppendLine("public string MethodName { get; init; } = string.Empty;");
        WriteIndent();
        _output.AppendLine("public string[] Parameters { get; init; } = Array.Empty<string>();");
        WriteIndent();
        _output.AppendLine("public string[] RequiredCapabilities { get; init; } = Array.Empty<string>();");
        WriteIndent();
        _output.AppendLine("public string[] TargetModes { get; init; } = Array.Empty<string>();");
        WriteIndent();
        _output.AppendLine("public int SourceLine { get; init; }");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("private static readonly System.Collections.Generic.IReadOnlyList<TranspiledPropertyMetadata> __transpiledProperties = new List<TranspiledPropertyMetadata>");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        foreach (var propertyDecl in properties)
        {
            var requiredCapabilities = propertyDecl.GetRequiredCapabilities();
            var targetModes = propertyDecl.GetTargetModes();
            if (targetModes.Count == 0)
            {
                // Keep existing default behavior: parity properties target interpreter and transpiled C#.
                targetModes = new[] { "interpreter", "csharp" };
            }

            WriteIndent();
            _output.Append("new TranspiledPropertyMetadata { Name = ");
            _output.Append(ToQuotedString(propertyDecl.Name));
            _output.Append(", MethodName = ");
            _output.Append(ToQuotedString(GetPropertyRunnerMethodName(propertyDecl.Name)));
            _output.Append(", Parameters = new[] { ");
            for (var i = 0; i < propertyDecl.Parameters.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                _output.Append(ToQuotedString(propertyDecl.Parameters[i]));
            }
            _output.Append(" }, RequiredCapabilities = ");
            if (requiredCapabilities.Count == 0)
            {
                _output.Append("Array.Empty<string>()");
            }
            else
            {
                _output.Append("new[] { ");
                for (var i = 0; i < requiredCapabilities.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append(ToQuotedString(requiredCapabilities[i]));
                }
                _output.Append(" }");
            }

            _output.Append(", TargetModes = ");
            if (targetModes.Count == 0)
            {
                _output.Append("Array.Empty<string>()");
            }
            else
            {
                _output.Append("new[] { ");
                for (var i = 0; i < targetModes.Count; i++)
                {
                    if (i > 0) _output.Append(", ");
                    _output.Append(ToQuotedString(targetModes[i]));
                }
                _output.Append(" }");
            }
            _output.Append(", SourceLine = ");
            _output.Append(propertyDecl.Line.ToString());
            _output.AppendLine(" },");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static System.Collections.Generic.IReadOnlyList<TranspiledPropertyMetadata> GetTranspiledProperties()");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return __transpiledProperties;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("public static async Task<object> InvokeTranspiledProperty(string propertyName, object[] args)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (propertyName == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new ArgumentNullException(nameof(propertyName));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("var __propertyMeta = __transpiledProperties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal));");
        WriteIndent();
        _output.AppendLine("if (__propertyMeta == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Unknown property '{propertyName}'.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("args ??= Array.Empty<object>();");
        WriteIndent();
        _output.AppendLine("if (args.Length != __propertyMeta.Parameters.Length)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new ArgumentException($\"Property '{propertyName}' expects {__propertyMeta.Parameters.Length} argument(s), got {args.Length}.\", nameof(args));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        WriteIndent();
        _output.AppendLine("switch (propertyName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        foreach (var propertyDecl in properties)
        {
            var methodName = EscapeIdentifier(GetPropertyRunnerMethodName(propertyDecl.Name));
            WriteIndent();
            _output.Append("case ");
            _output.Append(ToQuotedString(propertyDecl.Name));
            _output.AppendLine(":");
            _indentLevel++;
            WriteIndent();
            _output.Append("return await ");
            _output.Append(methodName);
            _output.Append("(");
            for (var i = 0; i < propertyDecl.Parameters.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                _output.Append("args[");
                _output.Append(i.ToString());
                _output.Append("]");
            }
            _output.AppendLine(");");
            _indentLevel--;
        }
        WriteIndent();
        _output.AppendLine("default:");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new InvalidOperationException($\"Unknown property '{propertyName}'.\");");
        _indentLevel--;
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private void TranspileProperty(PropertyDeclaration propertyDecl)
    {
        WriteIndent();
        _output.Append("public static async Task<object> ");
        _output.Append(EscapeIdentifier(GetPropertyRunnerMethodName(propertyDecl.Name)));
        _output.Append("(");
        for (var i = 0; i < propertyDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(propertyDecl.Parameters[i]));
        }
        _output.AppendLine(")");
        var previousCanAwait = _canAwait;
        _canAwait = true;
        TranspileFunctionBlock(propertyDecl.Body, GetPropertyRunnerMethodName(propertyDecl.Name), propertyDecl.Line, appendImplicitNullReturn: true);
        _canAwait = previousCanAwait;
    }

    private void GenerateSchemaRegistration(List<SchemaDeclaration> schemas)
    {
        if (schemas.Count == 0)
            return;

        var known = schemas.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var knownSumTypes = _typeDeclarations.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
        foreach (var schemaDecl in schemas)
        {
            WriteIndent();
            _output.Append("MaldaLang.BuiltIns.SchemaRegistry.RegisterCompiled(\"");
            _output.Append(schemaDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\", ");
            EmitParseJsonSchemaLiteral(SchemaRegistry.BuildSchema(schemaDecl, known, knownSumTypes));
            _output.AppendLine(");");
        }

        _output.AppendLine();
    }

    private MaldaLang.Interpreter.RuntimeValue BuildEmittedSumTypeSchema(TypeDeclaration typeDecl, List<SchemaDeclaration> schemas)
    {
        var knownSchemas = schemas.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var knownSumTypes = _typeDeclarations.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
        return SumTypeRegistry.BuildSchema(typeDecl, knownSchemas, knownSumTypes);
    }

    private void GenerateSumTypeRegistration(List<SchemaDeclaration> schemas)
    {
        if (_typeDeclarations.Count == 0)
            return;

        foreach (var typeDecl in _typeDeclarations)
        {
            WriteIndent();
            _output.Append("MaldaLang.BuiltIns.SumTypeRegistry.RegisterCompiled(\"");
            _output.Append(typeDecl.TypeName.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\", ");
            EmitParseJsonSchemaLiteral(BuildEmittedSumTypeSchema(typeDecl, schemas));
            _output.AppendLine(");");
        }

        _output.AppendLine();
    }

    private void GenerateApiRegistration()
    {
        if (_apiDeclarations.Count == 0)
            return;

        foreach (var apiDecl in _apiDeclarations)
        {
            WriteIndent();
            _output.Append("MaldaLang.BuiltIns.ApiRegistry.RegisterCompiled(\"");
            _output.Append(apiDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\", new System.Collections.Generic.List<MaldaLang.Parser.AST.Declarations.ApiMethodSignature> { ");
            for (int i = 0; i < apiDecl.Methods.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                var method = apiDecl.Methods[i];
                _output.Append("new MaldaLang.Parser.AST.Declarations.ApiMethodSignature(\"");
                _output.Append(method.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\", new System.Collections.Generic.List<string> { ");
                for (int p = 0; p < method.ParameterNames.Count; p++)
                {
                    if (p > 0) _output.Append(", ");
                    _output.Append("\"");
                    _output.Append(method.ParameterNames[p].Replace("\\", "\\\\").Replace("\"", "\\\""));
                    _output.Append("\"");
                }
                _output.Append(" }, new System.Collections.Generic.List<string?> { ");
                for (int p = 0; p < method.ParameterNames.Count; p++)
                {
                    if (p > 0) _output.Append(", ");
                    var typeName = method.ParameterTypeAt(p);
                    if (string.IsNullOrEmpty(typeName))
                        _output.Append("null");
                    else
                    {
                        _output.Append("\"");
                        _output.Append(typeName.Replace("\\", "\\\\").Replace("\"", "\\\""));
                        _output.Append("\"");
                    }
                }
                _output.Append(" }, new System.Collections.Generic.List<bool> { ");
                for (int p = 0; p < method.ParameterNames.Count; p++)
                {
                    if (p > 0) _output.Append(", ");
                    _output.Append(method.ParameterRequiredAt(p) ? "true" : "false");
                }
                _output.Append(" })");
            }
            _output.AppendLine(" });");

            foreach (var method in apiDecl.Methods)
            {
                if (!_functionNames.Contains(method.Name))
                    continue;

                WriteIndent();
                _output.Append("MaldaLang.BuiltIns.ApiRegistry.BindImplementation(\"");
                _output.Append(method.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\", __args => { ");
                _output.Append("if (__args.Count != ");
                _output.Append(method.ParameterNames.Count);
                _output.Append(") throw new System.Exception(\"runProgram: wrong arity for ");
                _output.Append(method.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
                _output.Append("\"); ");
                _output.Append("return RuntimeHelpers.ToRuntimeValue(");
                _output.Append(EscapeIdentifier(method.Name));
                _output.Append("(");
                for (int p = 0; p < method.ParameterNames.Count; p++)
                {
                    if (p > 0) _output.Append(", ");
                    _output.Append("RuntimeHelpers.UnwrapRuntimeValue(__args[");
                    _output.Append(p);
                    _output.Append("])");
                }
                _output.Append(").GetAwaiter().GetResult()); });");
                _output.AppendLine();
            }
        }

        _output.AppendLine();
    }
}
