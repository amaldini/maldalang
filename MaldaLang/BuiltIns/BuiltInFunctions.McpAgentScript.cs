// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInCreateMcpAgentScript(List<RuntimeValue> args)
    {
        if (args.Count < 5)
            throw new Exception("createMcpAgentScript() expects at least 5 arguments: (agentName, agentRole, agentInstructions, tools, outputPath, model?)");
        
        // Validate required parameters
        if (args[0].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() agentName must be a string");
        if (args[1].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() agentRole must be a string");
        if (args[2].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() agentInstructions must be a string");
        if (args[3].Type != ValueType.Array)
            throw new Exception("createMcpAgentScript() tools must be an array");
        if (args[4].Type != ValueType.String)
            throw new Exception("createMcpAgentScript() outputPath must be a string");
        
        var agentName = args[0].AsString();
        var agentRole = args[1].AsString();
        var agentInstructions = args[2].AsString();
        var toolsArray = args[3].AsArray();
        var outputPath = args[4].AsString();
        var model = args.Count > 5 && args[5].Type == ValueType.String ? args[5].AsString() : "openai/gpt-4";
        
        // Validate agentName is not empty
        if (string.IsNullOrWhiteSpace(agentName))
            throw new Exception("createMcpAgentScript() agentName cannot be empty");
        
        // Validate outputPath is not empty
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new Exception("createMcpAgentScript() outputPath cannot be empty");
        
        // Validate tools array
        if (toolsArray == null || toolsArray.Count == 0)
            throw new Exception("createMcpAgentScript() tools array cannot be empty");
        
        // Validate each tool in the array
        var toolNames = new List<string>();
        foreach (var toolValue in toolsArray)
        {
            if (toolValue.Type != ValueType.Object)
                throw new Exception("createMcpAgentScript() each tool in tools array must be an object");
            
            var toolObj = toolValue.AsObject();
            var nameValue = toolObj.Get("name");
            var descValue = toolObj.Get("description");
            
            if (nameValue == null || nameValue.Type != ValueType.String || string.IsNullOrWhiteSpace(nameValue.AsString()))
                throw new Exception("createMcpAgentScript() each tool must have a non-empty 'name' string property");
            
            if (descValue == null || descValue.Type != ValueType.String || string.IsNullOrWhiteSpace(descValue.AsString()))
                throw new Exception("createMcpAgentScript() each tool must have a non-empty 'description' string property");
            
            var toolName = nameValue.AsString();
            if (toolNames.Contains(toolName))
                throw new Exception($"createMcpAgentScript() duplicate tool name: {toolName}");
            
            toolNames.Add(toolName);
        }
        
        // Check for path traversal attempts (similar to runMALDA)
        if (outputPath.Contains("..") || outputPath.Contains("~"))
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.String(""));
            errorObj.Set("scriptContent", RuntimeValue.String(""));
            errorObj.Set("error", RuntimeValue.String("Error: Path contains suspicious characters (path traversal attempt)"));
            return RuntimeValue.Object(errorObj);
        }
        
        try
        {
            // Resolve output path
            string fullPath;
            if (Path.IsPathRooted(outputPath))
            {
                fullPath = Path.GetFullPath(outputPath);
            }
            else
            {
                fullPath = Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, outputPath));
            }
            
            // Create directory if needed
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            // Generate the script
            var script = GenerateMcpAgentScript(agentName, agentRole, agentInstructions, toolsArray, model);
            
            // Write to file
            File.WriteAllText(fullPath, script);
            
            // Return success result
            var resultObj = new JsonObject();
            resultObj.Set("success", RuntimeValue.Boolean(true));
            resultObj.Set("outputPath", RuntimeValue.String(fullPath));
            resultObj.Set("scriptContent", RuntimeValue.String(script));
            resultObj.Set("error", RuntimeValue.String(""));
            
            return RuntimeValue.Object(resultObj);
        }
        catch (Exception ex)
        {
            var errorObj = new JsonObject();
            errorObj.Set("success", RuntimeValue.Boolean(false));
            errorObj.Set("outputPath", RuntimeValue.String(""));
            errorObj.Set("scriptContent", RuntimeValue.String(""));
            errorObj.Set("error", RuntimeValue.String($"Error: {ex.Message}"));
            
            return RuntimeValue.Object(errorObj);
        }
    }
    
    private static string GenerateMcpAgentScript(string agentName, string agentRole, 
        string agentInstructions, List<RuntimeValue> tools, string model)
    {
        var sb = new StringBuilder();
        
        // Header comment
        sb.AppendLine($"// MCP Server for {agentName} Agent");
        sb.AppendLine("// Auto-generated script");
        sb.AppendLine();
        
        // Client setup
        sb.AppendLine($"var client = new OpenRouterClient(\"{EscapeString(model)}\");");
        sb.AppendLine();
        
        // Agent variable name (lowercase, sanitized)
        var agentVarName = SanitizeVariableName(agentName.ToLower());
        
        // Agent creation
        sb.AppendLine($"// Create the {agentName} agent");
        sb.AppendLine($"var {agentVarName} = new Agent(");
        sb.AppendLine($"    \"{EscapeString(agentName)}\",");
        sb.AppendLine($"    \"{EscapeString(agentRole)}\",");
        sb.AppendLine($"    \"{EscapeString(agentInstructions)}\",");
        sb.AppendLine("    client");
        sb.AppendLine(");");
        sb.AppendLine();
        
        // Generate MCP tools
        sb.AppendLine("// Expose agent capabilities as MCP tools");
        var toolNameList = new List<string>();
        
        foreach (var toolValue in tools)
        {
            var toolObj = toolValue.AsObject();
            var nameValue = toolObj.Get("name");
            var descValue = toolObj.Get("description");
            var toolSchemaValue = toolObj.Get("schema");
            
            // These should already be validated, but check for safety
            if (nameValue == null || nameValue.Type != ValueType.String)
                continue; // Skip invalid tools (shouldn't happen after validation)
            
            var toolName = nameValue.AsString();
            var toolDesc = descValue != null && descValue.Type == ValueType.String 
                ? descValue.AsString() 
                : "";
            
            toolNameList.Add(toolName);
            
            sb.AppendLine();
            if (toolSchemaValue != null && toolSchemaValue.Type == ValueType.String && !string.IsNullOrWhiteSpace(toolSchemaValue.AsString()))
            {
                // Tool with custom schema
                var schemaStr = toolSchemaValue.AsString();
                sb.AppendLine($"@MCPTool(\"{EscapeString(toolName)}\", \"{EscapeString(toolDesc)}\", {schemaStr})");
            }
            else
            {
                // Tool without custom schema (auto-generated)
                sb.AppendLine($"@MCPTool(\"{EscapeString(toolName)}\", \"{EscapeString(toolDesc)}\")");
            }
            
            // Generate function - for now, all tools take a prompt parameter
            // This could be enhanced in the future to support more complex signatures
            var functionName = SanitizeFunctionName(toolName);
            sb.AppendLine($"function {functionName}(prompt) {{");
            sb.AppendLine($"    return {agentVarName}.think(prompt).content;");
            sb.AppendLine("}");
        }
        
        sb.AppendLine();
        sb.AppendLine("// Start the MCP server");
        sb.AppendLine("var server = new MCPServer();");
        sb.AppendLine("server.start();");
        sb.AppendLine();
        sb.AppendLine($"print(\"{EscapeString(agentName)} MCP Server started\");");
        sb.AppendLine($"print(\"Available tools: {string.Join(", ", toolNameList.Select(EscapeString))}\");");
        sb.AppendLine();
        sb.AppendLine("// Keep server running");
        sb.AppendLine("while (server.isRunning) {");
        sb.AppendLine("    sleep(1000);");
        sb.AppendLine("}");
        
        return sb.ToString();
    }
    
    private static string EscapeString(string str)
    {
        if (string.IsNullOrEmpty(str))
            return "";
        
        return str.Replace("\\", "\\\\")
                  .Replace("\"", "\\\"")
                  .Replace("\n", "\\n")
                  .Replace("\r", "\\r")
                  .Replace("\t", "\\t");
    }
    
    private static string SanitizeVariableName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "agent";
        
        // Remove invalid characters and ensure it starts with a letter
        var sanitized = new StringBuilder();
        var firstChar = true;
        
        foreach (var c in name)
        {
            if (char.IsLetter(c) || (!firstChar && (char.IsDigit(c) || c == '_')))
            {
                sanitized.Append(c);
                firstChar = false;
            }
            else if (!firstChar)
            {
                sanitized.Append('_');
            }
        }
        
        var result = sanitized.ToString();
        if (string.IsNullOrEmpty(result) || char.IsDigit(result[0]))
            return "agent_" + result;
        
        return result;
    }
    
    private static string SanitizeFunctionName(string name)
    {
        // Function names follow same rules as variable names
        return SanitizeVariableName(name);
    }
    
    private static RuntimeValue BuiltInCreateCreateMcpAgentScriptTool(List<RuntimeValue> args)
    {
        BuiltInArity.Require("createCreateMcpAgentScriptTool", args, 0, 1, "workingDir?");
        var workingDir = args.Count > 0 && args[0].Type == MaldaLang.Interpreter.ValueType.String 
            ? args[0].AsString() 
            : "";
        return BuiltInTools.CreateCreateMcpAgentScriptTool(workingDir);
    }
}
