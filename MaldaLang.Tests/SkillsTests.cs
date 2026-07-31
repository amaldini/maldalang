// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.PackageManager;
using MaldaLang.Interpreter;
using MaldaLang;
using MaldaLang.Parser;
using System;
using System.IO;
using System.Linq;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class SkillsTests : TestBase
{
    [Fact]
    public void ModuleResolver_ResolveModulePath_skills_withMissingFile_returns_null()
    {
        // Ensure MALDA_REGISTRY_URL is set so ModuleResolver can create a PackageRegistry if needed
        var originalValue = System.Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        System.Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", "https://test-registry.maldalang.com");
        try
        {
            var resolver = new ModuleResolver();
            var path = resolver.ResolveModulePath("skills", "nonexistent_skill_12345");
            Assert.Null(path);
        }
        finally
        {
            // Restore original value
            System.Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", originalValue);
        }
    }

    [Fact]
    public void ModuleResolver_ResolveModulePath_skills_withEmptySubModule_doesNotReturnSkillsPath()
    {
        // Ensure MALDA_REGISTRY_URL is set so ModuleResolver can create a PackageRegistry if needed
        var originalValue = System.Environment.GetEnvironmentVariable("MALDA_REGISTRY_URL");
        System.Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", "https://test-registry.maldalang.com");
        try
        {
            var resolver = new ModuleResolver();
            // Empty subModule: skills block is skipped, falls through to package registry (no "skills" package)
            var path = resolver.ResolveModulePath("skills", "");
            Assert.Null(path);
            path = resolver.ResolveModulePath("skills", null);
            Assert.Null(path);
        }
        finally
        {
            // Restore original value
            System.Environment.SetEnvironmentVariable("MALDA_REGISTRY_URL", originalValue);
        }
    }

    [Fact]
    public void Interpreter_LoadSkillModule_returns_object_with_module_globals()
    {
        var tempDir = CreateTempDirectory("skills_");
        try
        {
            var skillPath = Path.Combine(tempDir, "test_skill.malda");
            File.WriteAllText(skillPath, @"
var x = 42;
var name = ""hello"";
var tools = [];
");
            var interpreter = new Interpreter.Interpreter();
            var result = interpreter.LoadSkillModule(skillPath);
            Assert.Equal(MaldaLang.Interpreter.ValueType.Object, result.Type);
            var obj = result.AsObject();
            Assert.NotNull(obj);
            var x = obj.Get("x", null);
            Assert.Equal(MaldaLang.Interpreter.ValueType.Integer, x.Type);
            Assert.Equal(42, x.AsInteger());
            var name = obj.Get("name", null);
            Assert.Equal(MaldaLang.Interpreter.ValueType.String, name.Type);
            Assert.Equal("hello", name.AsString());
            var tools = obj.Get("tools", null);
            Assert.Equal(MaldaLang.Interpreter.ValueType.Array, tools.Type);
            Assert.Empty(tools.AsArray());
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Interpreter_LoadSkillModule_missingFile_returns_null()
    {
        var interpreter = new Interpreter.Interpreter();
        var result = interpreter.LoadSkillModule(Path.Combine(Path.GetTempPath(), "nonexistent_skill_12345.malda"));
        Assert.Equal(MaldaLang.Interpreter.ValueType.Null, result.Type);
    }

    [Fact]
    public void GetSkillNames_returns_array()
    {
        var source = @"
var names = getSkillNames();
print(typeOf(names));
print(names.length);
";
        RedirectConsole();
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            var interpreter = new Interpreter.Interpreter();
            interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
            var output = GetOutput();
            var lines = output.Split('\n');
            Assert.True(lines.Length >= 2);
            Assert.Equal("array", lines[0].Trim());
            Assert.True(int.TryParse(lines[1].Trim(), out var len));
            Assert.True(len >= 0);
        }
        finally
        {
            RestoreConsole();
        }
    }

    [Fact]
    public void LoadSkill_nonexistent_returns_null()
    {
        var source = @"
var s = loadSkill(""nonexistent_skill_12345"");
print(s == null ? ""null"" : ""obj"");
";
        var output = RunProgram(source);
        Assert.Equal("null", output);
    }

    [Fact]
    public void LoadSkillsFromDir_returns_skill_wrappers_with_tools()
    {
        var tempDir = CreateTempDirectory("skills_dir_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "alpha.malda"), @"
var tools = [];
var label = ""alpha"";
");
            File.WriteAllText(Path.Combine(tempDir, "broken.malda"), @"
syntax error here !!!
");
            var source = $@"
var skills = loadSkillsFromDir(""{tempDir.Replace("\\", "\\\\")}"");
print(skills.length);
print(skills[0].name);
print(skills[0].label);
print(skills[1].name);
print(skills[1].error != null && skills[1].error != """" ? ""err"" : ""ok"");
";
            var output = RunProgram(source);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal(5, lines.Length);
            Assert.Equal("2", lines[0]);
            Assert.Equal("alpha", lines[1]);
            Assert.Equal("alpha", lines[2]);
            Assert.Equal("broken", lines[3]);
            Assert.Equal("err", lines[4]);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
