// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MaldaLang.Tests;

public class NewToolHandlerTests
{
    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task NewTool_Execute_InvokesHandler_WithArgsObject()
    {
        var source = """
            function echoHandler(args) {
                return "got:" + string(args.message);
            }

            var tool = new Tool(
                "echo_message",
                "Echoes a message",
                {
                    "type": "object",
                    "properties": {
                        "message": { "type": "string" }
                    },
                    "required": ["message"]
                },
                echoHandler
            );

            print(tool.name);
            print(tool.execute({ "message": "hi" }));
            """;

        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var interpreter = new Interpreter.Interpreter();
        var output = CaptureStdout(() =>
        {
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
        });

        Assert.Contains("echo_message", output, StringComparison.Ordinal);
        Assert.Contains("got:hi", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondBrainSemantic_DefinesFindRelatedNotesTool()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..",
            "Examples", "Agents", "sb", "06-memory.malda"));
        Assert.True(File.Exists(path), "missing " + path);
        var source = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("new Tool(", source, StringComparison.Ordinal);
        Assert.Contains("find_related_notes", source, StringComparison.Ordinal);
        Assert.Contains("createFindRelatedNotesTool(", source, StringComparison.Ordinal);
        Assert.Contains("memory.findRelated(", source, StringComparison.Ordinal);
    }
}
