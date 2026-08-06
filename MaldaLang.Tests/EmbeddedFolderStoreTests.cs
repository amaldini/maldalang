// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class EmbeddedFolderStoreTests : IDisposable
{
    public EmbeddedFolderStoreTests()
    {
        EmbeddedFolderStore.ResetForTests();
        EmbeddedFolderStore.RegisterForTests("fixture", new Dictionary<string, string>
        {
            ["hello.txt"] = "hello embedded",
            ["notes/a.md"] = "# Note A\nneedle here",
            ["notes/b.md"] = "# Note B"
        });
    }

    public void Dispose()
    {
        EmbeddedFolderStore.ResetForTests();
    }

    [Fact]
    public void HasAlias_And_ReadText_Work()
    {
        Assert.True(EmbeddedFolderStore.HasAlias("fixture"));
        Assert.Equal("hello embedded", EmbeddedFolderStore.ReadText("embed:fixture/hello.txt"));
        Assert.True(EmbeddedFolderStore.HasFile("embed:fixture/notes/a.md"));
        Assert.False(EmbeddedFolderStore.HasFile("embed:fixture/missing.md"));
    }

    [Fact]
    public void List_Returns_TopLevel_Entries()
    {
        var entries = EmbeddedFolderStore.List("embed:fixture");
        Assert.Contains(entries, e => e.Name == "hello.txt" && !e.IsDirectory);
        Assert.Contains(entries, e => e.Name == "notes" && e.IsDirectory);
    }

    [Fact]
    public void PathJoin_Preserves_Embed_Scheme()
    {
        var joined = BuiltInFunctions.CallBuiltIn(
            "pathJoin",
            new List<RuntimeValue>
            {
                RuntimeValue.String("embed:fixture"),
                RuntimeValue.String("notes"),
                RuntimeValue.String("a.md")
            },
            null);

        Assert.Equal("embed:fixture/notes/a.md", joined.AsString());
    }

    [Fact]
    public void ReadFile_Builtin_Reads_Embed_Path()
    {
        var value = BuiltInFunctions.CallBuiltIn(
            "readFile",
            new List<RuntimeValue> { RuntimeValue.String("embed:fixture/hello.txt") },
            null);
        Assert.Equal("hello embedded", value.AsString());
    }

    [Fact]
    public void HasEmbeddedFolder_And_Root_Helpers()
    {
        var has = BuiltInFunctions.CallBuiltIn(
            "hasEmbeddedFolder",
            new List<RuntimeValue> { RuntimeValue.String("fixture") },
            null);
        Assert.True(has.AsBoolean());

        var root = BuiltInFunctions.CallBuiltIn(
            "embeddedFolderRoot",
            new List<RuntimeValue> { RuntimeValue.String("fixture") },
            null);
        Assert.Equal("embed:fixture", root.AsString());

        var missing = BuiltInFunctions.CallBuiltIn(
            "embeddedFolderRoot",
            new List<RuntimeValue> { RuntimeValue.String("nope") },
            null);
        Assert.Equal(MaldaLang.Interpreter.ValueType.Null, missing.Type);
    }

    [Fact]
    public void Grep_Searches_Embedded_Files()
    {
        var result = BuiltInFunctions.CallBuiltIn(
            "grep",
            new List<RuntimeValue>
            {
                RuntimeValue.String("needle"),
                RuntimeValue.String("embed:fixture"),
                RuntimeValue.Boolean(false),
                RuntimeValue.Boolean(false),
                RuntimeValue.Boolean(true),
                RuntimeValue.Integer(0),
                RuntimeValue.Boolean(false),
                RuntimeValue.Boolean(true),
                RuntimeValue.String("embed:fixture")
            },
            null);

        Assert.Equal(MaldaLang.Interpreter.ValueType.Array, result.Type);
        Assert.True(result.AsArray().Count >= 1);
    }

    [Fact]
    public void WriteFile_Rejects_Embed_Paths()
    {
        var ex = Assert.Throws<Exception>(() => BuiltInFunctions.CallBuiltIn(
            "writeFile",
            new List<RuntimeValue>
            {
                RuntimeValue.String("embed:fixture/hello.txt"),
                RuntimeValue.String("nope")
            },
            null));
        Assert.Contains("cannot write", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_WorkingDirectory_Resolves_Embed_Paths()
    {
        var tool = BuiltInTools.CreateReadFileTool("embed:fixture").AsObject() as ToolInstance;
        Assert.NotNull(tool);
        Assert.Equal("embed:fixture/notes/a.md", tool!.ResolvePathAgainstWorkingDirectory("notes/a.md"));
        Assert.Null(tool.NormalizePathForWorkingDirectory("../outside.txt"));
    }
}
