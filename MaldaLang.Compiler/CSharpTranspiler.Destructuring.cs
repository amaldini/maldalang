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
    private string NextDestructureTemp(string prefix) => $"{prefix}_{++_destructureTempCounter}";

    private void TranspileDestructuringVarDecl(DestructuringVarDecl stmt)
    {
        var valueTemp = NextDestructureTemp("__destructureValue");
        WriteIndent();
        _output.Append("var ");
        _output.Append(valueTemp);
        _output.Append(" = ");
        TranspileExpression(stmt.Initializer);
        _output.Append(";");
        _output.AppendLine();
        
        if (stmt.Pattern is ArrayDestructuringPattern arrayPattern)
        {
            TranspileInlineArrayDestructuring(arrayPattern, valueTemp);
        }
        else if (stmt.Pattern is ObjectDestructuringPattern objectPattern)
        {
            TranspileInlineObjectDestructuring(objectPattern, valueTemp);
        }
        else
        {
            // Fallback: throw at runtime (unsupported pattern in transpiled code)
            WriteIndent();
            _output.AppendLine("throw new System.Exception(\"Destructuring pattern did not match value.\");");
        }
    }

    
    private void TranspileInlineArrayDestructuring(ArrayDestructuringPattern arrayPattern, string valueTemp)
    {
        var arrTemp = NextDestructureTemp("__destructureArr");
        var requiredCount = arrayPattern.Rest != null ? arrayPattern.Elements.Count : arrayPattern.Elements.Count;
        WriteIndent();
        _output.Append("var ");
        _output.Append(arrTemp);
        _output.Append(" = RuntimeHelpers.GetArray(");
        _output.Append(valueTemp);
        _output.Append(");");
        _output.AppendLine();
        WriteIndent();
        _output.Append("if (");
        _output.Append(arrTemp);
        _output.Append(".Count ");
        _output.Append(arrayPattern.Rest != null ? "< " : "!= ");
        _output.Append(requiredCount);
        _output.Append(") throw new System.Exception(\"Destructuring pattern did not match value.\");");
        _output.AppendLine();
        for (int i = 0; i < arrayPattern.Elements.Count; i++)
        {
            if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
            {
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(idPattern.Name));
                _output.Append(" = ");
                _output.Append(arrTemp);
                _output.Append("[");
                _output.Append(i);
                _output.Append("];");
                _output.AppendLine();
            }
        }
        if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
        {
            var restTemp = NextDestructureTemp("__restList");
            var indexTemp = NextDestructureTemp("__ri");
            WriteIndent();
            _output.Append("var ");
            _output.Append(restTemp);
            _output.Append(" = new List<object>();");
            _output.AppendLine();
            WriteIndent();
            _output.Append("for (int ");
            _output.Append(indexTemp);
            _output.Append(" = ");
            _output.Append(arrayPattern.Elements.Count);
            _output.Append("; ");
            _output.Append(indexTemp);
            _output.Append(" < ");
            _output.Append(arrTemp);
            _output.Append(".Count; ");
            _output.Append(indexTemp);
            _output.Append("++) ");
            _output.Append(restTemp);
            _output.Append(".Add(");
            _output.Append(arrTemp);
            _output.Append("[");
            _output.Append(indexTemp);
            _output.Append("]);");
            _output.AppendLine();
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
            _output.Append(" = ");
            _output.Append(restTemp);
            _output.Append(";");
            _output.AppendLine();
        }
    }

    
    private void TranspileInlineObjectDestructuring(ObjectDestructuringPattern objectPattern, string valueTemp)
    {
        WriteIndent();
        _output.Append("if (");
        _output.Append(valueTemp);
        _output.Append(" == null || !RuntimeHelpers.IsObject(");
        _output.Append(valueTemp);
        _output.Append(")) throw new System.Exception(\"Destructuring pattern did not match value.\");");
        _output.AppendLine();
        foreach (var prop in objectPattern.Properties)
        {
            var varName = prop.BindingName ?? prop.Key;
            WriteIndent();
            _output.Append("if (!RuntimeHelpers.ObjectHasKey(");
            _output.Append(valueTemp);
            _output.Append(", \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\")) throw new System.Exception(\"Destructuring pattern did not match value.\");");
            _output.AppendLine();
            WriteIndent();
            _output.Append("object ");
            _output.Append(EscapeIdentifier(varName));
            _output.Append(" = RuntimeHelpers.GetObjectMember(");
            _output.Append(valueTemp);
            _output.Append(", \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\");");
            _output.AppendLine();
        }
    }

    
    private void TranspileDestructuringAssignment(DestructuringAssignment stmt)
    {
        var valueTemp = NextDestructureTemp("__destructureValue");
        WriteIndent();
        _output.Append("var ");
        _output.Append(valueTemp);
        _output.Append(" = ");
        TranspileExpression(stmt.Value);
        _output.Append(";");
        _output.AppendLine();
        
        if (stmt.Pattern is ArrayDestructuringPattern arrayPattern)
        {
            TranspileInlineArrayDestructuringAssignment(arrayPattern, valueTemp);
        }
        else if (stmt.Pattern is ObjectDestructuringPattern objectPattern)
        {
            TranspileInlineObjectDestructuringAssignment(objectPattern, valueTemp);
        }
    }

    
    private void TranspileInlineArrayDestructuringAssignment(ArrayDestructuringPattern arrayPattern, string valueTemp)
    {
        var arrTemp = NextDestructureTemp("__destructureArr");
        var requiredCount = arrayPattern.Rest != null ? arrayPattern.Elements.Count : arrayPattern.Elements.Count;
        WriteIndent();
        _output.Append("var ");
        _output.Append(arrTemp);
        _output.Append(" = RuntimeHelpers.GetArray(");
        _output.Append(valueTemp);
        _output.Append(");");
        _output.AppendLine();
        WriteIndent();
        _output.Append("if (");
        _output.Append(arrTemp);
        _output.Append(".Count ");
        _output.Append(arrayPattern.Rest != null ? "< " : "!= ");
        _output.Append(requiredCount);
        _output.Append(") throw new System.Exception(\"Destructuring pattern did not match value.\");");
        _output.AppendLine();
        for (int i = 0; i < arrayPattern.Elements.Count; i++)
        {
            if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
            {
                WriteIndent();
                _output.Append(EscapeIdentifier(idPattern.Name));
                _output.Append(" = ");
                _output.Append(arrTemp);
                _output.Append("[");
                _output.Append(i);
                _output.Append("];");
                _output.AppendLine();
            }
        }
        if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
        {
            var restTemp = NextDestructureTemp("__restList");
            var indexTemp = NextDestructureTemp("__ri");
            WriteIndent();
            _output.Append("var ");
            _output.Append(restTemp);
            _output.Append(" = new List<object>();");
            _output.AppendLine();
            WriteIndent();
            _output.Append("for (int ");
            _output.Append(indexTemp);
            _output.Append(" = ");
            _output.Append(arrayPattern.Elements.Count);
            _output.Append("; ");
            _output.Append(indexTemp);
            _output.Append(" < ");
            _output.Append(arrTemp);
            _output.Append(".Count; ");
            _output.Append(indexTemp);
            _output.Append("++) ");
            _output.Append(restTemp);
            _output.Append(".Add(");
            _output.Append(arrTemp);
            _output.Append("[");
            _output.Append(indexTemp);
            _output.Append("]);");
            _output.AppendLine();
            WriteIndent();
            _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
            _output.Append(" = ");
            _output.Append(restTemp);
            _output.Append(";");
            _output.AppendLine();
        }
    }

    
    private void TranspileInlineObjectDestructuringAssignment(ObjectDestructuringPattern objectPattern, string valueTemp)
    {
        WriteIndent();
        _output.Append("if (");
        _output.Append(valueTemp);
        _output.Append(" == null || !RuntimeHelpers.IsObject(");
        _output.Append(valueTemp);
        _output.Append(")) throw new System.Exception(\"Destructuring pattern did not match value.\");");
        _output.AppendLine();
        foreach (var prop in objectPattern.Properties)
        {
            var varName = prop.BindingName ?? prop.Key;
            WriteIndent();
            _output.Append("if (!RuntimeHelpers.ObjectHasKey(");
            _output.Append(valueTemp);
            _output.Append(", \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\")) throw new System.Exception(\"Destructuring pattern did not match value.\");");
            _output.AppendLine();
            WriteIndent();
            _output.Append(EscapeIdentifier(varName));
            _output.Append(" = RuntimeHelpers.GetObjectMember(");
            _output.Append(valueTemp);
            _output.Append(", \"");
            _output.Append(EscapeIdentifier(prop.Key));
            _output.Append("\");");
            _output.AppendLine();
        }
    }

    
    private void TranspileDestructuringPattern(DestructuringPattern pattern)
    {
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            _output.Append("new { Type = \"ArrayDestructuring\", Elements = new List<object> { ");
            for (int i = 0; i < arrayPattern.Elements.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                TranspilePatternForMatch(arrayPattern.Elements[i]);
            }
            if (arrayPattern.Rest != null)
            {
                if (arrayPattern.Elements.Count > 0) _output.Append(", ");
                _output.Append("new { Type = \"Rest\", Name = ");
                if (arrayPattern.Rest.Name != null)
                {
                    _output.Append("\"");
                    _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                    _output.Append("\"");
                }
                else
                {
                    _output.Append("null");
                }
                _output.Append(" }");
            }
            _output.Append(" } }");
        }
        else if (pattern is ObjectDestructuringPattern objectPattern)
        {
            _output.Append("new { Type = \"ObjectDestructuring\", Properties = new Dictionary<string, object> { ");
            for (int i = 0; i < objectPattern.Properties.Count; i++)
            {
                if (i > 0) _output.Append(", ");
                var prop = objectPattern.Properties[i];
                _output.Append("{ \"");
                _output.Append(EscapeIdentifier(prop.Key));
                _output.Append("\", ");
                if (prop.Pattern != null)
                {
                    TranspilePatternForMatch(prop.Pattern);
                }
                else if (prop.BindingName != null)
                {
                    _output.Append("new { Type = \"Identifier\", Name = \"");
                    _output.Append(EscapeIdentifier(prop.BindingName));
                    _output.Append("\" }");
                }
                else
                {
                    _output.Append("null");
                }
                _output.Append(" }");
            }
            _output.Append(" } }");
        }
    }

    
    private void ExtractAndDeclareVariables(DestructuringPattern pattern, string bindingsVar)
    {
        // Extract variable names and generate declarations
        // This is simplified - full implementation would handle nested patterns
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            for (int i = 0; i < arrayPattern.Elements.Count; i++)
            {
                if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
                {
                    WriteIndent();
                    _output.Append("object ");
                    _output.Append(EscapeIdentifier(idPattern.Name));
                    _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                    _output.Append(bindingsVar);
                    _output.Append("[\"");
                    _output.Append(idPattern.Name);
                    _output.Append("\"]);");
                    _output.AppendLine();
                }
            }
            if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
            {
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(arrayPattern.Rest.Name);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
        else if (pattern is ObjectDestructuringPattern objectPattern)
        {
            foreach (var prop in objectPattern.Properties)
            {
                var varName = prop.BindingName ?? prop.Key;
                WriteIndent();
                _output.Append("object ");
                _output.Append(EscapeIdentifier(varName));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(varName);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
    }

    
    private void ExtractAndAssignVariables(DestructuringPattern pattern, string bindingsVar)
    {
        // Similar to ExtractAndDeclareVariables but uses assignment
        if (pattern is ArrayDestructuringPattern arrayPattern)
        {
            for (int i = 0; i < arrayPattern.Elements.Count; i++)
            {
                if (arrayPattern.Elements[i] is IdentifierPattern idPattern)
                {
                    WriteIndent();
                    _output.Append(EscapeIdentifier(idPattern.Name));
                    _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                    _output.Append(bindingsVar);
                    _output.Append("[\"");
                    _output.Append(idPattern.Name);
                    _output.Append("\"]);");
                    _output.AppendLine();
                }
            }
            if (arrayPattern.Rest != null && arrayPattern.Rest.Name != null)
            {
                WriteIndent();
                _output.Append(EscapeIdentifier(arrayPattern.Rest.Name));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(arrayPattern.Rest.Name);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
        else if (pattern is ObjectDestructuringPattern objectPattern)
        {
            foreach (var prop in objectPattern.Properties)
            {
                var varName = prop.BindingName ?? prop.Key;
                WriteIndent();
                _output.Append(EscapeIdentifier(varName));
                _output.Append(" = RuntimeHelpers.FromRuntimeValue(");
                _output.Append(bindingsVar);
                _output.Append("[\"");
                _output.Append(varName);
                _output.Append("\"]);");
                _output.AppendLine();
            }
        }
    }
}
