// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspilerGlobGrepTests
{
    [Fact]
    public void TranspiledGlob_FindsMatchingFiles()
    {
        var tempDir = CreateTempDirectory("malda_tglob_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), "alpha");
            File.WriteAllText(Path.Combine(tempDir, "src", "b.txt"), "beta");
            File.WriteAllText(Path.Combine(tempDir, "skip.cs"), "// cs");

            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
var workDir = ""{workDir}"";
var result = glob(""**/*.txt"", workDir, 50);
print(string(result.count));
var items = result.items;
var i = 0;
while (i < length(items)) {{
    print(items[i].path);
    i = i + 1;
}}
";
            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
            Assert.Contains("2", output);
            Assert.Contains("a.txt", output);
            Assert.Contains("src/b.txt", output.Replace('\\', '/'));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledGrep_FindsPatternInDirectory()
    {
        var tempDir = CreateTempDirectory("malda_tgrep_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "lib"));
            File.WriteAllText(Path.Combine(tempDir, "one.txt"), "hello Ralph");
            File.WriteAllText(Path.Combine(tempDir, "lib", "two.txt"), "no match");
            File.WriteAllText(Path.Combine(tempDir, "lib", "three.txt"), "Ralph Wiggum loop");

            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
var workDir = ""{workDir}"";
var matches = grep(""Ralph"", workDir, true, true, true, 0, false, true, workDir);
print(string(length(matches)));
";
            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
            Assert.Contains("2", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledRalphGlobPaths_HelperWorksInDistributionPath()
    {
        var tempDir = CreateTempDirectory("malda_tglob_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "PRD.md"), "# test");
            File.WriteAllText(Path.Combine(tempDir, "app.cs"), "// app");

            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
function extractGlobPaths(globResult, maxCount) {{
    var paths = [];
    if (globResult == null) {{ return paths; }}
    var items = globResult.items;
    if (items == null) {{ return paths; }}
    var i = 0;
    while (i < length(items)) {{
        if (maxCount != null && maxCount > 0 && length(paths) >= maxCount) {{ break; }}
        var item = items[i];
        if (item != null && item.path != null && string(item.path) != """") {{
            paths.append(string(item.path));
        }}
        i = i + 1;
    }}
    return paths;
}}
function ralphGlobPaths(workDir, pattern, maxResults) {{
    if (maxResults == null || maxResults <= 0) {{ maxResults = 200; }}
    var result = glob(pattern, workDir, maxResults);
    return extractGlobPaths(result, maxResults);
}}
var workDir = ""{workDir}"";
var paths = ralphGlobPaths(workDir, ""**/*.cs"", 10);
print(string(length(paths)));
print(paths[0]);
";
            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
            Assert.Contains("1", output);
            Assert.Contains("app.cs", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledIoGlob_FindsMatchingFiles()
    {
        var tempDir = CreateTempDirectory("malda_tio_glob_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), "alpha");
            File.WriteAllText(Path.Combine(tempDir, "src", "b.txt"), "beta");
            File.WriteAllText(Path.Combine(tempDir, "skip.cs"), "// cs");

            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
var workDir = ""{workDir}"";
var result = io.glob(""**/*.txt"", workDir, 50);
print(string(result.count));
var items = result.items;
var i = 0;
while (i < length(items)) {{
    print(items[i].path);
    i = i + 1;
}}
";
            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
            Assert.Contains("2", output);
            Assert.Contains("a.txt", output);
            Assert.Contains("src/b.txt", output.Replace('\\', '/'));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void TranspiledCreateGlobTool_Execute_FindsMatchingFiles()
    {
        var tempDir = CreateTempDirectory("malda_tglob_tool_");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "src"));
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), "alpha");
            File.WriteAllText(Path.Combine(tempDir, "src", "b.txt"), "beta");
            File.WriteAllText(Path.Combine(tempDir, "skip.cs"), "// cs");

            var workDir = tempDir.Replace("\\", "/");
            var source = $@"
var tool = createGlobTool(""{workDir}"");
var result = tool.execute({{ ""pattern"": ""**/*.txt"" }});
print(string(result.count));
print(string(result.truncated));
var items = result.items;
var i = 0;
while (i < length(items)) {{
    print(items[i].path);
    i = i + 1;
}}
";
            var output = TranspiledTestRunner.CompileAndRunFromSource(source).StdOut;
            Assert.Contains("2", output);
            Assert.Contains("false", output);
            Assert.Contains("a.txt", output);
            Assert.Contains("src/b.txt", output.Replace('\\', '/'));
            Assert.DoesNotContain("Tool execution validated", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures on CI/Windows file locks
        }
    }
}
