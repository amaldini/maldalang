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
    private static string ToQuotedString(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private void EmitPromptReturnTypeLiteral(string? returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType))
            _output.Append("null");
        else
            _output.Append(ToQuotedString(returnType.Trim()));
    }

    private void EmitParseJsonSchemaLiteral(MaldaLang.Interpreter.RuntimeValue schema)
    {
        var schemaJson = BuiltInFunctions.SerializeToJson(schema);
        var escaped = schemaJson.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _output.Append("(MaldaLang.Interpreter.RuntimeValue)MaldaLang.BuiltIns.BuiltInFunctions.CallBuiltIn(\"parseJSON\", new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { MaldaLang.Interpreter.RuntimeValue.String(\"");
        _output.Append(escaped);
        _output.Append("\") }, null)");
    }

    private static int? TryGetPromptWithinTimeoutMs(PromptDeclaration promptDecl)
    {
        var decorator = promptDecl.Decorators.FirstOrDefault(d =>
            string.Equals(d.Name, "within", StringComparison.OrdinalIgnoreCase));
        if (decorator == null || decorator.Arguments.Count != 1)
            return null;

        if (decorator.Arguments[0] is not LiteralExpression literal)
            return null;

        var ms = literal.Value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => 0
        };

        return ms > 0 ? ms : null;
    }

    private void EmitResourceBudgetPush(ResourceBudget budget, string label)
    {
        _output.Append("MaldaLang.Interpreter.ResourceBoundsContext.Push(");
        EmitResourceBudgetNew(budget);
        _output.Append(", \"");
        _output.Append(label.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("\");");
    }

    private void EmitResourceBudgetNew(ResourceBudget budget)
    {
        _output.Append("new MaldaLang.Interpreter.ResourceBudget(");
        if (budget.MaxTokens is int tokens)
            _output.Append(tokens);
        else
            _output.Append("null");
        _output.Append(", ");
        if (budget.MaxTools is int tools)
            _output.Append(tools);
        else
            _output.Append("null");
        _output.Append(", ");
        if (budget.MaxCost is double cost)
        {
            _output.Append(cost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _output.Append("d");
        }
        else
            _output.Append("null");
        _output.Append(")");
    }

    private static bool IsBuiltInPromptReturnType(string? returnType)
    {
        if (string.IsNullOrWhiteSpace(returnType)) return true;
        var n = returnType.Trim();
        return n is "string" or "String" or "int" or "Int" or "integer" or "Integer"
            or "float" or "Float" or "double" or "Double" or "number" or "Number"
            or "bool" or "Bool" or "boolean" or "Boolean"
            or "array" or "Array" or "list" or "List"
            or "object" or "Object" or "json" or "Json"
            or "Plan";
    }

    private void TranspilePrompt(PromptDeclaration promptDecl, List<ClassDeclaration> classes, List<SchemaDeclaration> schemas)
    {
        WriteIndent();
        _output.Append("static object ");
        _output.Append(EscapeIdentifier(promptDecl.Name));
        _output.Append("(");
        
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
        }
        
        _output.Append(")");
        AppendComment(nameof(TranspilePrompt));
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        
        if (promptDecl.BodyType == PromptBodyType.ObjectLiteral)
        {
            // Evaluate the prompt body (object literal)
            WriteIndent();
            _output.Append("var bodyValue = ");
            TranspileExpression(promptDecl.ObjectBody!);
            _output.AppendLine(";");
        
        // Extract fields from body
        WriteIndent();
        _output.AppendLine("if (bodyValue == null || !RuntimeHelpers.IsObject(bodyValue))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt body must evaluate to an object.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("var bodyObj = RuntimeHelpers.UnwrapRuntimeValue(bodyValue);");
        WriteIndent();
        _output.AppendLine("if (bodyObj is not MaldaLang.BuiltIns.JsonObject and not MaldaLang.Interpreter.DictionaryInstance and not System.Collections.Generic.Dictionary<string, object?>)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt body must be a JSON object.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        // Extract fields
        WriteIndent();
        _output.AppendLine("var systemValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"system\");");
        WriteIndent();
        _output.AppendLine("var userValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"user\");");
        WriteIndent();
        _output.AppendLine("var modelValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"model\");");
        WriteIndent();
        _output.AppendLine("var temperatureValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"temperature\");");
        WriteIndent();
        _output.AppendLine("var toolsValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"tools\");");
        WriteIndent();
        _output.AppendLine("var gatherValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"gather\");");
        WriteIndent();
        _output.AppendLine("var maxTokensValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"maxTokens\");");
        WriteIndent();
        _output.AppendLine("var examplesValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"examples\");");
        WriteIndent();
        _output.AppendLine("var examples = MaldaLang.BuiltIns.PromptExampleHelpers.ParseExamplesOrNull(examplesValue);");
        WriteIndent();
        _output.AppendLine("var attachmentsValue = RuntimeHelpers.GetPromptObjectField(bodyValue, \"attachments\");");
        WriteIndent();
        _output.AppendLine("var attachments = MaldaLang.BuiltIns.PromptAttachment.ParseListOrNull(attachmentsValue);");
        
        // Validate user is required
        WriteIndent();
        _output.AppendLine("if (userValue.Type == MaldaLang.Interpreter.ValueType.Null || userValue.Type != MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt body must have a 'user' field of type string.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        // Extract values
        WriteIndent();
        _output.AppendLine("string? system = null;");
        WriteIndent();
        _output.AppendLine("if (systemValue.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("system = RuntimeHelpers.CoerceToString(systemValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("string user = RuntimeHelpers.CoerceToString(userValue);");
        
        WriteIndent();
        _output.AppendLine("string? model = null;");
        WriteIndent();
        _output.AppendLine("if (modelValue.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("model = RuntimeHelpers.CoerceToString(modelValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("double? temperature = null;");
        WriteIndent();
        _output.AppendLine("if (temperatureValue.Type != MaldaLang.Interpreter.ValueType.Null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (temperatureValue.Type == MaldaLang.Interpreter.ValueType.Float)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("temperature = (double)RuntimeHelpers.CoerceToFloat(temperatureValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (temperatureValue.Type == MaldaLang.Interpreter.ValueType.Integer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("temperature = (double)RuntimeHelpers.CoerceToInt(temperatureValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        WriteIndent();
        _output.AppendLine("System.Collections.Generic.List<string>? tools = null;");
        WriteIndent();
        _output.AppendLine("if (toolsValue.Type == MaldaLang.Interpreter.ValueType.Array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("tools = new System.Collections.Generic.List<string>();");
        WriteIndent();
        _output.AppendLine("foreach (var tool in RuntimeHelpers.UnwrapRuntimeValue(toolsValue) as System.Collections.Generic.List<RuntimeValue>)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (tool.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("tools.Add(RuntimeHelpers.CoerceToString(tool));");
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
        _output.AppendLine("System.Collections.Generic.List<string>? gather = null;");
        WriteIndent();
        _output.AppendLine("if (gatherValue.Type == MaldaLang.Interpreter.ValueType.Array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("gather = new System.Collections.Generic.List<string>();");
        WriteIndent();
        _output.AppendLine("foreach (var tool in RuntimeHelpers.UnwrapRuntimeValue(gatherValue) as System.Collections.Generic.List<RuntimeValue>)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (tool.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("gather.Add(RuntimeHelpers.CoerceToString(tool));");
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
        _output.AppendLine("int? maxTokens = null;");
        WriteIndent();
        _output.AppendLine("if (maxTokensValue.Type != MaldaLang.Interpreter.ValueType.Null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (maxTokensValue.Type == MaldaLang.Interpreter.ValueType.Integer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("maxTokens = (int)RuntimeHelpers.CoerceToInt(maxTokensValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (maxTokensValue.Type == MaldaLang.Interpreter.ValueType.Float)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("maxTokens = (int)RuntimeHelpers.CoerceToFloat(maxTokensValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        
        }
        else
        {
            // Statement-based body: evaluate statements and extract values
            WriteIndent();
            _output.AppendLine("string? system = null;");
            WriteIndent();
            _output.AppendLine("string? user = null;");
            WriteIndent();
            _output.AppendLine("string? model = null;");
            WriteIndent();
            _output.AppendLine("double? temperature = null;");
            WriteIndent();
            _output.AppendLine("System.Collections.Generic.List<string>? tools = null;");
            WriteIndent();
            _output.AppendLine("System.Collections.Generic.List<string>? gather = null;");
            WriteIndent();
            _output.AppendLine("int? maxTokens = null;");
            WriteIndent();
            _output.AppendLine("System.Collections.Generic.List<MaldaLang.BuiltIns.PromptExample>? examples = null;");
            WriteIndent();
            _output.AppendLine("System.Collections.Generic.List<MaldaLang.BuiltIns.PromptAttachment>? attachments = null;");
            _output.AppendLine();
            
            if (promptDecl.StatementBody != null)
            {
                foreach (var stmt in promptDecl.StatementBody)
                {
                    if (stmt is PromptBodyStatement bodyStmt)
                    {
                        WriteIndent();
                        var keyword = bodyStmt.Keyword;
                        _output.Append("var ");
                        _output.Append(keyword);
                        _output.Append("Value = ");
                        TranspileExpression(bodyStmt.Expression);
                        _output.AppendLine(";");
                        
                        WriteIndent();
                        _output.Append("if (");
                        _output.Append(keyword);
                        _output.AppendLine("Value != null)");
                        WriteIndent();
                        _output.AppendLine("{");
                        _indentLevel++;
                        WriteIndent();
                        
                        switch (keyword)
                        {
                            case "system":
                                _output.AppendLine("system = RuntimeHelpers.CoerceToString(" + keyword + "Value);");
                                break;
                            case "user":
                                _output.AppendLine("user = RuntimeHelpers.CoerceToString(" + keyword + "Value);");
                                break;
                            case "model":
                                _output.AppendLine("model = RuntimeHelpers.CoerceToString(" + keyword + "Value);");
                                break;
                            case "temperature":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Float) temperature = (double)RuntimeHelpers.CoerceToFloat(" + keyword + "Value);");
                                WriteIndent();
                                _output.AppendLine("else if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Integer) temperature = (double)RuntimeHelpers.CoerceToInt(" + keyword + "Value);");
                                break;
                            case "tools":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Array)");
                                WriteIndent();
                                _output.AppendLine("{");
                                _indentLevel++;
                                WriteIndent();
                                _output.AppendLine("tools = new System.Collections.Generic.List<string>();");
                                WriteIndent();
                                _output.AppendLine("foreach (var tool in RuntimeHelpers.UnwrapRuntimeValue(" + keyword + "Value) as System.Collections.Generic.List<RuntimeValue>)");
                                WriteIndent();
                                _output.AppendLine("{");
                                _indentLevel++;
                                WriteIndent();
                                _output.AppendLine("if (tool.Type == MaldaLang.Interpreter.ValueType.String) tools.Add(RuntimeHelpers.CoerceToString(tool));");
                                _indentLevel--;
                                WriteIndent();
                                _output.AppendLine("}");
                                _indentLevel--;
                                WriteIndent();
                                _output.AppendLine("}");
                                break;
                            case "gather":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Array)");
                                WriteIndent();
                                _output.AppendLine("{");
                                _indentLevel++;
                                WriteIndent();
                                _output.AppendLine("gather = new System.Collections.Generic.List<string>();");
                                WriteIndent();
                                _output.AppendLine("foreach (var tool in RuntimeHelpers.UnwrapRuntimeValue(" + keyword + "Value) as System.Collections.Generic.List<RuntimeValue>)");
                                WriteIndent();
                                _output.AppendLine("{");
                                _indentLevel++;
                                WriteIndent();
                                _output.AppendLine("if (tool.Type == MaldaLang.Interpreter.ValueType.String) gather.Add(RuntimeHelpers.CoerceToString(tool));");
                                _indentLevel--;
                                WriteIndent();
                                _output.AppendLine("}");
                                _indentLevel--;
                                WriteIndent();
                                _output.AppendLine("}");
                                break;
                            case "maxTokens":
                                _output.AppendLine("if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Integer) maxTokens = (int)RuntimeHelpers.CoerceToInt(" + keyword + "Value);");
                                WriteIndent();
                                _output.AppendLine("else if (" + keyword + "Value.Type == MaldaLang.Interpreter.ValueType.Float) maxTokens = (int)RuntimeHelpers.CoerceToFloat(" + keyword + "Value);");
                                break;
                            case "examples":
                                _output.AppendLine("examples = MaldaLang.BuiltIns.PromptExampleHelpers.ParseExamplesOrNull(" + keyword + "Value)?.ToList();");
                                break;
                            case "attachments":
                                _output.AppendLine("attachments = MaldaLang.BuiltIns.PromptAttachment.ParseListOrNull(" + keyword + "Value);");
                                break;
                        }
                        
                        _indentLevel--;
                        WriteIndent();
                        _output.AppendLine("}");
                    }
                }
            }
            
        WriteIndent();
        _output.AppendLine("if (user == null) throw new Exception(\"Prompt body must have a 'user' field.\");");
        }

        WriteIndent();
        _output.AppendLine("if (gather != null && gather.Count > 0 && tools != null && tools.Count > 0)");
        WriteIndent();
        _output.Append("throw new Exception(\"Prompt '");
        _output.Append(promptDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("' cannot list both gather: and tools:. Use gather: with -> Type for two-phase extract, or tools: for Mode B.\");");
        WriteIndent();
        _output.AppendLine("if (gather != null && gather.Count > 0 && string.IsNullOrWhiteSpace(\"" + (promptDecl.ReturnType ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"))");
        WriteIndent();
        _output.Append("throw new Exception(\"Prompt '");
        _output.Append(promptDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("' uses gather: which requires a -> Type extract target (schema, sum type, or program(Api)).\");");
        WriteIndent();
        _output.AppendLine("if (gather != null && gather.Count == 0)");
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Prompt 'gather' field must be a non-empty array of tool name strings.\");");

        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            var paramName = promptDecl.Parameters[i];
            WriteIndent();
            _output.Append("var __placeholder_");
            _output.Append(i);
            _output.Append(" = \"{");
            _output.Append(paramName);
            _output.Append("}\";");
            _output.AppendLine();
            WriteIndent();
            _output.Append("var __replacement_");
            _output.Append(i);
            _output.Append(" = RuntimeHelpers.CoerceToString(");
            _output.Append(EscapeIdentifier(paramName));
            _output.Append(");");
            _output.AppendLine();
            WriteIndent();
            _output.AppendLine("if (system != null && system.Contains(__placeholder_" + i + "))");
            WriteIndent();
            _output.AppendLine("system = system.Replace(__placeholder_" + i + ", __replacement_" + i + ");");
            WriteIndent();
            _output.AppendLine("if (user != null && user.Contains(__placeholder_" + i + "))");
            WriteIndent();
            _output.AppendLine("user = user.Replace(__placeholder_" + i + ", __replacement_" + i + ");");
        }

        WriteIndent();
        _output.AppendLine("if (examples != null && examples.Count > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.Append("MaldaLang.BuiltIns.PromptExampleHelpers.ApplyParameterInterpolation(examples, new System.Collections.Generic.List<string> { ");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("\"");
            _output.Append(promptDecl.Parameters[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\"");
        }
        _output.Append(" }, new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
            _output.Append(")");
        }
        _output.AppendLine(" });");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (attachments != null && attachments.Count > 0)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.Append("MaldaLang.BuiltIns.PromptAttachment.ApplyParameterInterpolation(attachments, new System.Collections.Generic.List<string> { ");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("\"");
            _output.Append(promptDecl.Parameters[i].Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.Append("\"");
        }
        _output.Append(" }, new System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> { ");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("RuntimeHelpers.ToRuntimeValue(");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
            _output.Append(")");
        }
        _output.AppendLine(" });");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        // Build response_format + appendix when ReturnType is set and this is not the Mode C
        // gather instance (tools are allowed — Mode B structured-if-supported).
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __responseFormatSchema = null;");
        WriteIndent();
        _output.Append("if (!string.IsNullOrWhiteSpace(\"");
        _output.Append((promptDecl.ReturnType ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.Append("\") && (gather == null || gather.Count == 0))");
        _output.AppendLine();
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        bool isCustomReturnType = !string.IsNullOrWhiteSpace(promptDecl.ReturnType) && !IsBuiltInPromptReturnType(promptDecl.ReturnType);
        SchemaDeclaration? schemaDecl = null;
        ClassDeclaration? schemaClassDecl = null;
        TypeDeclaration? sumTypeDecl = null;
        ApiDeclaration? programApiDecl = null;
        if (isCustomReturnType)
        {
            var returnName = promptDecl.ReturnType!.Trim();
            if (ApiRegistry.TryParseProgramReturnType(returnName, out var programApiName))
            {
                programApiDecl = _apiDeclarations.FirstOrDefault(a =>
                    string.Equals(a.Name, programApiName, StringComparison.Ordinal));
            }
            else
            {
                schemaDecl = schemas.FirstOrDefault(s => string.Equals(s.Name, returnName, StringComparison.Ordinal));
                if (schemaDecl == null)
                {
                    sumTypeDecl = _typeDeclarations.FirstOrDefault(t =>
                        string.Equals(t.TypeName, returnName, StringComparison.Ordinal));
                }
                if (schemaDecl == null && sumTypeDecl == null)
                {
                    var classesByName = classes.ToDictionary(c => c.Name, c => c);
                    if (classesByName.TryGetValue(returnName, out var cd))
                        schemaClassDecl = cd;
                }
            }
        }
        if (schemaDecl != null)
        {
            var knownSchemas = schemas.ToDictionary(s => s.Name, StringComparer.Ordinal);
            var knownSumTypes = _typeDeclarations.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
            WriteIndent();
            _output.Append("var __schema = ");
            EmitParseJsonSchemaLiteral(SchemaRegistry.BuildSchema(schemaDecl, knownSchemas, knownSumTypes));
            _output.AppendLine(";");
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
            WriteIndent();
            _output.Append("system = MaldaLang.BuiltIns.TypedPromptValidator.ApplySchemaAppendix(system, \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", __schema);");
        }
        else if (programApiDecl != null)
        {
            var programSchema = ApiRegistry.BuildProgramSchema(
                new ApiRegistry.ApiDefinition(programApiDecl.Name, programApiDecl.Methods));
            WriteIndent();
            _output.Append("var __schema = ");
            EmitParseJsonSchemaLiteral(programSchema);
            _output.AppendLine(";");
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
            WriteIndent();
            _output.Append("system = MaldaLang.BuiltIns.TypedPromptValidator.ApplySchemaAppendix(system, \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", __schema);");
        }
        else if (sumTypeDecl != null)
        {
            WriteIndent();
            _output.Append("var __schema = ");
            EmitParseJsonSchemaLiteral(BuildEmittedSumTypeSchema(sumTypeDecl, schemas));
            _output.AppendLine(";");
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
            WriteIndent();
            _output.Append("system = MaldaLang.BuiltIns.TypedPromptValidator.ApplySchemaAppendix(system, \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", __schema);");
        }
        else if (schemaClassDecl != null)
        {
            var schema = TypedPromptSchemaResolver.BuildSchemaFromClassDeclaration(schemaClassDecl, classes.ToDictionary(c => c.Name, c => c));
            WriteIndent();
            _output.Append("var __schema = ");
            EmitParseJsonSchemaLiteral(schema);
            _output.AppendLine(";");
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
            WriteIndent();
            _output.Append("system = MaldaLang.BuiltIns.TypedPromptValidator.ApplySchemaAppendix(system, \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", __schema);");
        }
        else if (!string.IsNullOrWhiteSpace(promptDecl.ReturnType))
        {
            WriteIndent();
            _output.Append("if (MaldaLang.BuiltIns.TypedPromptSchemaResolver.TryResolve(\"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", null, out var __schema, out _))");
            WriteIndent();
            _output.AppendLine("{");
            _indentLevel++;
            WriteIndent();
            _output.AppendLine("__responseFormatSchema = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__schema);");
            WriteIndent();
            _output.Append("system = MaldaLang.BuiltIns.TypedPromptValidator.ApplySchemaAppendix(system, \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\", __schema);");
            _indentLevel--;
            WriteIndent();
            _output.AppendLine("}");
        }
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        var promptWithinMs = TryGetPromptWithinTimeoutMs(promptDecl);
        if (promptWithinMs is > 0)
        {
            WriteIndent();
            _output.Append("int? __withinTimeoutMs = ");
            _output.Append(promptWithinMs.Value);
            _output.AppendLine(";");
        }
        else
        {
            WriteIndent();
            _output.AppendLine("int? __withinTimeoutMs = null;");
        }

        var promptBudget = DeclarationBounds.TryGetResourceBudget(promptDecl);
        WriteIndent();
        if (promptBudget != null && promptBudget.HasAnyBound)
        {
            _output.Append("MaldaLang.Interpreter.ResourceBudget? __resourceBudget = ");
            EmitResourceBudgetNew(promptBudget);
            _output.AppendLine(";");
        }
        else
        {
            _output.AppendLine("MaldaLang.Interpreter.ResourceBudget? __resourceBudget = null;");
        }

        // Create and return PromptInstance
        WriteIndent();
        _output.Append("return RuntimeHelpers.ToRuntimeValue(new MaldaLang.BuiltIns.PromptInstance(system, user, model, temperature, tools, maxTokens, __responseFormatSchema, examples, __withinTimeoutMs, gather, __resourceBudget, ");
        EmitPromptReturnTypeLiteral(promptDecl.ReturnType);
        _output.Append(", attachments));");
        _output.AppendLine();
        
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        // Emit async execution helper so transpiled `await prompt(...)` matches interpreter behavior.
        WriteIndent();
        _output.Append("static async Task<object> ");
        _output.Append(EscapeIdentifier(promptDecl.Name));
        _output.Append("__ExecuteAsync(");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append("object ");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
        }
        _output.AppendLine(")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        WriteIndent();
        _output.Append("var __promptRuntimeValue = RuntimeHelpers.ToRuntimeValue(");
        _output.Append(EscapeIdentifier(promptDecl.Name));
        _output.Append("(");
        for (int i = 0; i < promptDecl.Parameters.Count; i++)
        {
            if (i > 0) _output.Append(", ");
            _output.Append(EscapeIdentifier(promptDecl.Parameters[i]));
        }
        _output.AppendLine("));");

        WriteIndent();
        _output.AppendLine("if (__promptRuntimeValue.Type != MaldaLang.Interpreter.ValueType.Object || RuntimeHelpers.UnwrapRuntimeValue(__promptRuntimeValue) is not MaldaLang.BuiltIns.PromptInstance __promptInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("throw new Exception(\"Failed to create PromptInstance.\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("var __pushedBudget = __promptInstance.Budget != null && __promptInstance.Budget.HasAnyBound;");
        WriteIndent();
        _output.Append("if (__pushedBudget) MaldaLang.Interpreter.ResourceBoundsContext.Push(__promptInstance.Budget!, \"");
        _output.Append(promptDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("\");");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        WriteIndent();
        _output.AppendLine("var __defaultClient = MaldaLang.BuiltIns.DefaultLocalLlm.GetDefaultLocalClient();");
        WriteIndent();
        _output.AppendLine("var __agent = new MaldaLang.BuiltIns.AgentInstance();");
        WriteIndent();
        _output.AppendLine("__agent.Initialize(\"PromptAgent\", \"AI Assistant\", \"You are a helpful AI assistant.\", null, __defaultClient, null, null);");

        WriteIndent();
        if (string.IsNullOrWhiteSpace(promptDecl.ReturnType))
        {
            _output.AppendLine("string? __typedReturnType = null;");
            _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = null;");
        }
        else
        {
            _output.Append("string? __typedReturnType = \"");
            _output.Append(promptDecl.ReturnType!.Replace("\\", "\\\\").Replace("\"", "\\\""));
            _output.AppendLine("\";");
            bool customReturnType = !IsBuiltInPromptReturnType(promptDecl.ReturnType);
            SchemaDeclaration? returnSchemaDecl = null;
            ClassDeclaration? returnClassDecl = null;
            TypeDeclaration? returnSumTypeDecl = null;
            ApiDeclaration? returnProgramApiDecl = null;
            if (customReturnType)
            {
                var returnName = promptDecl.ReturnType!.Trim();
                if (ApiRegistry.TryParseProgramReturnType(returnName, out var programApiName))
                {
                    returnProgramApiDecl = _apiDeclarations.FirstOrDefault(a =>
                        string.Equals(a.Name, programApiName, StringComparison.Ordinal));
                }
                else
                {
                    returnSchemaDecl = schemas.FirstOrDefault(s => string.Equals(s.Name, returnName, StringComparison.Ordinal));
                    if (returnSchemaDecl == null)
                    {
                        returnSumTypeDecl = _typeDeclarations.FirstOrDefault(t =>
                            string.Equals(t.TypeName, returnName, StringComparison.Ordinal));
                    }
                    if (returnSchemaDecl == null && returnSumTypeDecl == null)
                    {
                        var classesByName = classes.ToDictionary(c => c.Name, c => c);
                        if (classesByName.TryGetValue(returnName, out var classDecl))
                            returnClassDecl = classDecl;
                    }
                }
            }
            if (returnSchemaDecl != null)
            {
                var knownSchemas = schemas.ToDictionary(s => s.Name, StringComparer.Ordinal);
                var knownSumTypes = _typeDeclarations.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
                WriteIndent();
                _output.Append("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = ");
                EmitParseJsonSchemaLiteral(SchemaRegistry.BuildSchema(returnSchemaDecl, knownSchemas, knownSumTypes));
                _output.AppendLine(";");
            }
            else if (returnProgramApiDecl != null)
            {
                var programSchema = ApiRegistry.BuildProgramSchema(
                    new ApiRegistry.ApiDefinition(returnProgramApiDecl.Name, returnProgramApiDecl.Methods));
                WriteIndent();
                _output.Append("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = ");
                EmitParseJsonSchemaLiteral(programSchema);
                _output.AppendLine(";");
            }
            else if (returnSumTypeDecl != null)
            {
                WriteIndent();
                _output.Append("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = ");
                EmitParseJsonSchemaLiteral(BuildEmittedSumTypeSchema(returnSumTypeDecl, schemas));
                _output.AppendLine(";");
            }
            else if (returnClassDecl != null)
            {
                var schema = TypedPromptSchemaResolver.BuildSchemaFromClassDeclaration(returnClassDecl, classes.ToDictionary(c => c.Name, c => c));
                WriteIndent();
                _output.Append("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = ");
                EmitParseJsonSchemaLiteral(schema);
                _output.AppendLine(";");
            }
            else
            {
                WriteIndent();
                _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __resolvedSchema = null;");
            }
        }

        WriteIndent();
        _output.AppendLine("int __maxAttempts = __typedReturnType != null ? 3 : 1;");
        WriteIndent();
        _output.AppendLine("string __baseUser = __promptInstance.User;");
        WriteIndent();
        _output.AppendLine("if (__promptInstance.HasGather)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
            _output.AppendLine("var __gatherInstance = new MaldaLang.BuiltIns.PromptInstance(__promptInstance.System, __promptInstance.User, __promptInstance.Model, __promptInstance.Temperature, __promptInstance.Gather, __promptInstance.MaxTokens, null, __promptInstance.Examples, __promptInstance.WithinTimeoutMs, __promptInstance.Gather, __promptInstance.Budget, __promptInstance.ReturnType, __promptInstance.Attachments);");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue __gatherResponse;");
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__gatherResponse = __agent.Think(RuntimeHelpers.ToRuntimeValue(__gatherInstance));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("catch (Exception __gatherEx)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__gatherEx is MaldaLang.Interpreter.RuntimeException) throw;");
        WriteIndent();
        _output.Append("throw new Exception(\"Gather step of prompt '");
        _output.Append(promptDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("' failed: \" + __gatherEx.Message);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("string? __gatherNotes = null;");
        WriteIndent();
        _output.AppendLine("if (__gatherResponse.Type == MaldaLang.Interpreter.ValueType.Object && RuntimeHelpers.UnwrapRuntimeValue(__gatherResponse) is MaldaLang.BuiltIns.JsonObject __gatherResponseObj)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __gatherContentValue = __gatherResponseObj.Get(\"content\");");
        WriteIndent();
        _output.AppendLine("if (__gatherContentValue.Type == MaldaLang.Interpreter.ValueType.String) __gatherNotes = RuntimeHelpers.CoerceToString(__gatherContentValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (__gatherResponse.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__gatherNotes = RuntimeHelpers.CoerceToString(__gatherResponse);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.Append("if (string.IsNullOrWhiteSpace(__gatherNotes)) throw new Exception(\"Gather step of prompt '");
        _output.Append(promptDecl.Name.Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("' failed: no string content in LLM response.\");");
        WriteIndent();
        _output.AppendLine("var __extractSystem = __promptInstance.System;");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? __extractFormat = null;");
        WriteIndent();
        _output.AppendLine("if (__resolvedSchema != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__extractFormat = MaldaLang.BuiltIns.TypedPromptValidator.BuildResponseFormat(__resolvedSchema);");
        WriteIndent();
        _output.Append("__extractSystem = MaldaLang.BuiltIns.TypedPromptValidator.ApplySchemaAppendix(__extractSystem, \"");
        _output.Append((promptDecl.ReturnType ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""));
        _output.AppendLine("\", __resolvedSchema);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.Append("__extractSystem = (__extractSystem ?? \"\") + \"");
        _output.Append(MaldaLang.Interpreter.PromptValue.GatherExtractSystemSuffix.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n"));
        _output.AppendLine("\";");
        WriteIndent();
        _output.Append("var __extractUser = __baseUser + \"");
        _output.Append(MaldaLang.Interpreter.PromptValue.GatherNotesMarker.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n"));
        _output.AppendLine("\" + __gatherNotes;");
        WriteIndent();
            _output.AppendLine("__promptInstance = new MaldaLang.BuiltIns.PromptInstance(__extractSystem, __extractUser, __promptInstance.Model, __promptInstance.Temperature, null, __promptInstance.MaxTokens, __extractFormat, __promptInstance.Examples, __promptInstance.WithinTimeoutMs, null, __promptInstance.Budget, __promptInstance.ReturnType, __promptInstance.Attachments);");
        WriteIndent();
        _output.AppendLine("__baseUser = __promptInstance.User;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("string __lastError = \"Unknown validation error.\";");
        WriteIndent();
        _output.AppendLine("for (int __attempt = 1; __attempt <= __maxAttempts; __attempt++)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;

        WriteIndent();
        _output.AppendLine("if (__attempt > 1)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __repair = MaldaLang.BuiltIns.TypedPromptValidator.BuildRepairInstruction(__typedReturnType!, __lastError);");
        WriteIndent();
            _output.AppendLine("__promptInstance = new MaldaLang.BuiltIns.PromptInstance(__promptInstance.System, __baseUser + \"\\n\\n\" + __repair, __promptInstance.Model, __promptInstance.Temperature, __promptInstance.Tools, __promptInstance.MaxTokens, __promptInstance.ResponseFormatSchema, __promptInstance.Examples, __promptInstance.WithinTimeoutMs, __promptInstance.Gather, __promptInstance.Budget, __promptInstance.ReturnType, __promptInstance.Attachments);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("var __response = __agent.Think(RuntimeHelpers.ToRuntimeValue(__promptInstance));");
        WriteIndent();
        _output.AppendLine("string? __content = null;");
        WriteIndent();
        _output.AppendLine("if (__response.Type == MaldaLang.Interpreter.ValueType.Object && RuntimeHelpers.UnwrapRuntimeValue(__response) is MaldaLang.BuiltIns.JsonObject __responseObj)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __contentValue = __responseObj.Get(\"content\");");
        WriteIndent();
        _output.AppendLine("if (__contentValue.Type == MaldaLang.Interpreter.ValueType.String) __content = RuntimeHelpers.CoerceToString(__contentValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (__response.Type == MaldaLang.Interpreter.ValueType.String)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__content = RuntimeHelpers.CoerceToString(__response);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (__typedReturnType == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__content != null) return __content;");
        WriteIndent();
        _output.AppendLine("return __response.ToString() ?? \"\";");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (__content == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = \"No string content in LLM response.\";");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryExtractJsonCandidate(__content, out var __jsonCandidate, out var __extractError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __extractError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryParseJson(__jsonCandidate, out var __parsed, out var __parseError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __parseError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("string __validationError;");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue __validated;");
        WriteIndent();
        _output.AppendLine("if (__resolvedSchema != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryValidateReturnType(__parsed, __resolvedSchema!, out __validated, out __validationError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __validationError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (!MaldaLang.BuiltIns.TypedPromptValidator.TryValidateReturnType(__parsed, __typedReturnType!, null, out __validated, out __validationError))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("__lastError = __validationError;");
        WriteIndent();
        _output.AppendLine("continue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("return RuntimeHelpers.UnwrapRuntimeValue(__validated);");

        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        WriteIndent();
        _output.AppendLine("throw new Exception($\"Typed prompt output validation failed after {__maxAttempts} attempts. Return type: {__typedReturnType}. Last error: {__lastError}\");");

        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (__pushedBudget) MaldaLang.Interpreter.ResourceBoundsContext.Pop();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");

        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }
}
