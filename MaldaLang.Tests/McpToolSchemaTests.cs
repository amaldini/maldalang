// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Interpreter;
using MaldaLang.Parser;
using MaldaLang.Tests.Planning;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class McpToolSchemaTests : TestBase
{
    [Fact]
    public void Example_McpSchemaTool_PrintsIntegerAndOptionalRequired()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "MCP", "mcp_schema_tool.malda");
        var output = RunProgram(File.ReadAllText(path));
        Assert.Contains("2", output, StringComparison.Ordinal);
        Assert.Contains("integer", output, StringComparison.Ordinal);
        Assert.Contains("string", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FewShot_McpToolSchema_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "llm", "few-shot", "31_mcptool_schema.malda");
        var output = RunProgram(File.ReadAllText(path));
        Assert.Contains("add", output, StringComparison.Ordinal);
        Assert.Contains("integer", output, StringComparison.Ordinal);
        Assert.Contains("2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonSchemaString_StillUsedWhenNotARegisteredName()
    {
        var output = RunProgram("""
            @MCPTool("get_weather", "Weather", "{\"type\":\"object\",\"properties\":{\"location\":{\"type\":\"string\"},\"unit\":{\"type\":\"string\"}},\"required\":[\"location\"]}")
            function getWeather(location, unit) {
                return location;
            }
            var tools = new MCPServer().getTools();
            io.print(tools[0].inputSchema.properties.location.type);
            io.print(tools[0].inputSchema.required.length);
            """);
        Assert.Contains("string", output, StringComparison.Ordinal);
        Assert.Contains("1", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OmittedSchema_DefaultsToStringProperties()
    {
        var output = RunProgram("""
            @MCPTool("add", "Adds")
            function add(a, b) {
                return a;
            }
            var tools = new MCPServer().getTools();
            io.print(tools[0].inputSchema.properties.a.type);
            """);
        Assert.Contains("string", output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownSchemaName_Throws()
    {
        var ex = Assert.ThrowsAny<Exception>(() => RunProgram("""
            @MCPTool("add", "Adds", "NotASchema")
            function add(a, b) {
                return a;
            }
            new MCPServer().getTools();
            """));
        Assert.Contains("NotASchema", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolDecorator_ResolvesSchemaName()
    {
        RunProgram("""
            schema NoteArgs {
                relativePath: string;
            }
            @Tool("read_note", "Read a note", "NoteArgs")
            function readNote(relativePath) {
                return relativePath;
            }
            """);
        var tool = ToolRegistry.Instance.GetTool("read_note");
        Assert.NotNull(tool);
        var schema = tool!.GetParametersSchema();
        Assert.Equal(ValueType.Object, schema.Type);
        var props = schema.AsObject().Get("properties");
        var relative = props.AsObject().Get("relativePath");
        Assert.Equal("string", relative.AsObject().Get("type").AsString());
    }

    [Fact]
    public void UnknownSchemaName_EmitsMaldaSchemaDiagnostic()
    {
        var source = """
            @MCPTool("add", "Adds", "NotASchema")
            function add(a, b) {
                return a;
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.Contains(diagnostics, d =>
            d.Source == "malda-schema" &&
            d.Severity == DiagnosticSeverity.Error &&
            d.Message.Contains("NotASchema", StringComparison.Ordinal));
    }

    [Fact]
    public void KnownSchemaName_NoDiagnostic()
    {
        var source = """
            schema AddArgs {
                a: int;
                b: int;
            }
            @MCPTool("add", "Adds", "AddArgs")
            function add(a, b) {
                return a;
            }
            @Tool("sum", "Sums", AddArgs)
            function sum(a, b) {
                return a;
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var diagnostics = new List<Diagnostic>();
        StrictTypesAnalysis.Analyze(statements, StrictTypesOptions.Default, diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Source == "malda-schema");
    }

    [Fact]
    public void CallTool_ValidArgs_ReturnsData()
    {
        var output = RunProgram("""
            schema AddArgs {
                a: int;
                b: int;
            }
            @MCPTool("add", "Adds", "AddArgs")
            function add(a, b) {
                return int(a) + int(b);
            }
            var r = new MCPServer().callTool("add", dict { "a": 4, "b": 5 });
            io.print(r.ok);
            io.print(r.data);
            """);
        Assert.Contains("true", output, StringComparison.Ordinal);
        Assert.Contains("9", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CallTool_WrongType_ReturnsErrorWithoutCallingBody()
    {
        var output = RunProgram("""
            schema AddArgs {
                a: int;
                b: int;
            }
            @MCPTool("add", "Adds", "AddArgs")
            function add(a, b) {
                io.print("body-ran");
                return int(a) + int(b);
            }
            var r = new MCPServer().callTool("add", dict { "a": "x", "b": 2 });
            io.print(r.ok);
            io.print(r.error);
            """);
        Assert.DoesNotContain("body-ran", output, StringComparison.Ordinal);
        Assert.Contains("false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CallTool_MissingRequired_ReturnsError()
    {
        var output = RunProgram("""
            schema WeatherArgs {
                location: string;
                unit: string?;
            }
            @MCPTool("get_weather", "Weather", "WeatherArgs")
            function getWeather(location, unit) {
                return location;
            }
            var r = new MCPServer().callTool("get_weather", dict { "unit": "celsius" });
            io.print(r.ok);
            """);
        Assert.Contains("false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CallTool_OmittedSchema_DoesNotValidateTypes()
    {
        var output = RunProgram("""
            @MCPTool("add", "Adds")
            function add(a, b) {
                return string(a) + string(b);
            }
            var r = new MCPServer().callTool("add", dict { "a": 1, "b": 2 });
            io.print(r.ok);
            io.print(r.data);
            """);
        Assert.Contains("true", output, StringComparison.Ordinal);
        Assert.Contains("12", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolExecute_AttachedSchema_RejectsBadArgs()
    {
        RunProgram("""
            schema NoteArgs {
                relativePath: string;
            }
            @Tool("read_note", "Read a note", "NoteArgs")
            function readNote(relativePath) {
                return relativePath;
            }
            """);
        var tool = ToolRegistry.Instance.GetTool("read_note");
        Assert.NotNull(tool);
        Assert.True(tool!.HasAttachedSchema);
        var args = new JsonObject();
        args.Set("relativePath", RuntimeValue.Integer(1));
        var result = tool.Execute(RuntimeValue.Object(args));
        Assert.Equal(ValueType.String, result.Type);
        Assert.Contains("failed schema", result.AsString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToolExecute_AttachedSchema_AcceptsGoodArgs()
    {
        RunProgram("""
            schema NoteArgs {
                relativePath: string;
            }
            @Tool("read_note", "Read a note", "NoteArgs")
            function readNote(relativePath) {
                return "ok:" + relativePath;
            }
            """);
        var tool = ToolRegistry.Instance.GetTool("read_note");
        Assert.NotNull(tool);
        var args = new JsonObject();
        args.Set("relativePath", RuntimeValue.String("welcome.txt"));
        var result = tool.Execute(RuntimeValue.Object(args));
        Assert.Equal("ok:welcome.txt", result.AsString());
    }

    [Fact]
    public void Example_McpSchemaTool_HostValidatesCallTool()
    {
        var path = PlanningPaths.ResolveRepoFile("Examples", "MCP", "mcp_schema_tool.malda");
        var output = RunProgram(File.ReadAllText(path));
        Assert.Contains("true", output, StringComparison.Ordinal);
        Assert.Contains("3", output, StringComparison.Ordinal);
        Assert.Contains("false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void FewShot_McpToolValidate_RunsUnderInterpreter()
    {
        var path = PlanningPaths.ResolveRepoFile("docs", "llm", "few-shot", "32_mcptool_validate.malda");
        var output = RunProgram(File.ReadAllText(path));
        Assert.Contains("true", output, StringComparison.Ordinal);
        Assert.Contains("3", output, StringComparison.Ordinal);
        Assert.Contains("false", output, StringComparison.Ordinal);
    }
}
