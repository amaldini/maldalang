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
    private bool ProfilingEnabled => _profilingOptions?.Enabled == true;

    private string GetGeneratedProfileSessionName()
    {
        return _sourceFilePath ?? "program.malda";
    }

    private string GetStatementProfileName(Statement statement)
    {
        var typeName = statement.GetType().Name;
        return typeName.EndsWith("Statement", StringComparison.Ordinal)
            ? typeName[..^"Statement".Length]
            : typeName;
    }

    private void AppendProfilingOptionsLiteral()
    {
        if (!ProfilingEnabled || _profilingOptions == null)
        {
            _output.Append("ProfilingOptions.Disabled");
            return;
        }

        _output.Append("new ProfilingOptions { Enabled = true, OutputPath = ");
        _output.Append(_profilingOptions.OutputPath == null ? "null" : ToQuotedString(_profilingOptions.OutputPath));
        _output.Append(", Format = ProfilingFormat.");
        _output.Append(_profilingOptions.Format.ToString());
        _output.Append(", WriteToConsole = ");
        _output.Append(_profilingOptions.WriteToConsole ? "true" : "false");
        _output.Append(", MaxEntriesPerSection = ");
        _output.Append(_profilingOptions.MaxEntriesPerSection.ToString());
        _output.Append(", PeriodicSnapshotSeconds = ");
        _output.Append(_profilingOptions.PeriodicSnapshotSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _output.Append(" }");
    }

    private void EmitProfilingSessionStart()
    {
        WriteIndent();
        _output.Append("MaldaProfiler.StartSession(");
        AppendProfilingOptionsLiteral();
        _output.Append(", ");
        _output.Append(ToQuotedString(GetGeneratedProfileSessionName()));
        _output.AppendLine(");");
    }

    private void EmitProfilingSessionComplete()
    {
        WriteIndent();
        _output.AppendLine("MaldaProfiler.CompleteSession();");
    }

    private string NextProfileVariableName(string prefix)
    {
        return $"__malda{prefix}{++_profileTempCounter}";
    }

    private string EmitStatementProfileStart(Statement statement)
    {
        var variableName = NextProfileVariableName("StmtProfile");
        _output.Append("var ");
        _output.Append(variableName);
        _output.Append(" = MaldaProfiler.EnterStatement(");
        _output.Append(ToQuotedString(statement.SourceFile ?? _sourceFilePath ?? "program.malda"));
        _output.Append(", ");
        _output.Append(statement.Line.ToString());
        _output.Append(", ");
        _output.Append(ToQuotedString(GetStatementProfileName(statement)));
        _output.AppendLine(");");
        return variableName;
    }

    private void EmitStatementProfileExit(string variableName)
    {
        WriteIndent();
        _output.Append("MaldaProfiler.Exit(");
        _output.Append(variableName);
        _output.AppendLine(");");
    }

    private void TranspileProfiledStructuredStatement(Statement statement, Action bodyEmitter)
    {
        if (!ProfilingEnabled)
        {
            bodyEmitter();
            return;
        }

        // Wrap profile token + try/finally in a block so parents like `while (c)` / `if (c)` without
        // braces bind one compound statement (otherwise only `var token = EnterStatement(...)` is the
        // loop/if body and the following try/finally breaks C# structure).
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        var profileVariable = EmitStatementProfileStart(statement);
        WriteIndent();
        _output.AppendLine("try");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        bodyEmitter();
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("finally");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        EmitStatementProfileExit(profileVariable);
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
    }

    private string EmitFunctionProfileStart(string functionName, int line, string? file)
    {
        var variableName = NextProfileVariableName("FunctionProfile");
        WriteIndent();
        _output.Append("var ");
        _output.Append(variableName);
        _output.Append(" = MaldaProfiler.EnterFunction(");
        _output.Append(ToQuotedString(functionName));
        _output.Append(", ");
        _output.Append(ToQuotedString(file ?? _sourceFilePath ?? "program.malda"));
        _output.Append(", ");
        _output.Append(line.ToString());
        _output.AppendLine(");");
        return variableName;
    }

    private void EmitFunctionProfileExit(string variableName)
    {
        WriteIndent();
        _output.Append("MaldaProfiler.Exit(");
        _output.Append(variableName);
        _output.AppendLine(");");
    }
}
