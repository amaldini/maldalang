// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using Xunit;
using ValueType = MaldaLang.Interpreter.ValueType;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class FileLifecycleToolTests
{
    [Fact]
    public void DeleteFile_ExistingFile_SucceedsAndRemovesFile()
    {
        var workDir = CreateTempDirectory("malda-lifecycle-del-");
        try
        {
            File.WriteAllText(Path.Combine(workDir, "gone.txt"), "remove me");
            var result = ExecuteTool(BuiltInTools.CreateDeleteFileTool(workDir), Args(("filePath", "gone.txt")));
            AssertTrueSuccess(result);
            Assert.False(File.Exists(Path.Combine(workDir, "gone.txt")));
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    [Fact]
    public void DeleteFile_MissingFile_Succeeds()
    {
        var workDir = CreateTempDirectory("malda-lifecycle-missing-");
        try
        {
            var result = ExecuteTool(BuiltInTools.CreateDeleteFileTool(workDir), Args(("filePath", "nope.txt")));
            AssertTrueSuccess(result);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    [Fact]
    public void DeleteFile_OutsideWorkingDirectory_ErrorStringAndFileUntouched()
    {
        var root = CreateTempDirectory("malda-lifecycle-jail-");
        var workDir = Path.Combine(root, "work");
        var outsideDir = Path.Combine(root, "outside");
        Directory.CreateDirectory(workDir);
        Directory.CreateDirectory(outsideDir);
        var victim = Path.Combine(outsideDir, "keep.txt");
        File.WriteAllText(victim, "keep");

        try
        {
            var result = ExecuteTool(
                BuiltInTools.CreateDeleteFileTool(workDir),
                Args(("filePath", victim)));
            Assert.Contains("outside", ToolErrorText(result), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(victim));
            Assert.Equal("keep", File.ReadAllText(victim));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CopyFile_InsideWorkingDirectory_DestHasSameContent()
    {
        var workDir = CreateTempDirectory("malda-lifecycle-copy-");
        try
        {
            File.WriteAllText(Path.Combine(workDir, "src.txt"), "payload");
            var result = ExecuteTool(
                BuiltInTools.CreateCopyFileTool(workDir),
                Args(("srcPath", "src.txt"), ("destPath", "dest.txt")));
            AssertTrueSuccess(result);
            Assert.True(File.Exists(Path.Combine(workDir, "dest.txt")));
            Assert.Equal("payload", File.ReadAllText(Path.Combine(workDir, "dest.txt")));
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    [Fact]
    public void CopyFile_DestOutsideWorkingDirectory_Error()
    {
        var workDir = CreateTempDirectory("malda-lifecycle-copyjail-");
        try
        {
            File.WriteAllText(Path.Combine(workDir, "src.txt"), "payload");
            var destOutside = Path.GetFullPath(Path.Combine(workDir, "..", "stolen.txt"));
            var result = ExecuteTool(
                BuiltInTools.CreateCopyFileTool(workDir),
                Args(("srcPath", "src.txt"), ("destPath", destOutside)));
            Assert.Contains("outside", ToolErrorText(result), StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destOutside));
        }
        finally
        {
            TryDeleteDirectory(workDir);
            try
            {
                var leftover = Path.GetFullPath(Path.Combine(workDir, "..", "stolen.txt"));
                if (File.Exists(leftover))
                    File.Delete(leftover);
            }
            catch
            {
                // ignore leftover cleanup
            }
        }
    }

    [Fact]
    public void EnsureDir_CreatesNestedDirectory()
    {
        var workDir = CreateTempDirectory("malda-lifecycle-mkdir-");
        try
        {
            var result = ExecuteTool(
                BuiltInTools.CreateEnsureDirTool(workDir),
                Args(("dirPath", "nested/dir")));
            AssertTrueSuccess(result);
            Assert.True(Directory.Exists(Path.Combine(workDir, "nested", "dir")));
            var pathVal = result.AsObject().Get("path", null);
            Assert.NotNull(pathVal);
            Assert.Equal(ValueType.String, pathVal!.Type);
            Assert.Contains("nested", pathVal.AsString().Replace('\\', '/'), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    [Fact]
    public void ToolExecute_NotOnlyConversation_WorksForAllThree()
    {
        var workDir = CreateTempDirectory("malda-lifecycle-execute-");
        try
        {
            File.WriteAllText(Path.Combine(workDir, "src.txt"), "via-execute");

            var copy = BuiltInTools.CreateCopyFileTool(workDir);
            var copyTool = Assert.IsType<ToolInstance>(copy.AsObject());
            var copied = copyTool.Execute(Args(("srcPath", "src.txt"), ("destPath", "out.txt")));
            AssertTrueSuccess(copied);
            Assert.Equal("via-execute", File.ReadAllText(Path.Combine(workDir, "out.txt")));

            var ensure = BuiltInTools.CreateEnsureDirTool(workDir);
            var ensureTool = Assert.IsType<ToolInstance>(ensure.AsObject());
            var ensured = ensureTool.Execute(Args(("dirPath", "a/b")));
            AssertTrueSuccess(ensured);
            Assert.True(Directory.Exists(Path.Combine(workDir, "a", "b")));

            var delete = BuiltInTools.CreateDeleteFileTool(workDir);
            var deleteTool = Assert.IsType<ToolInstance>(delete.AsObject());
            var deleted = deleteTool.Execute(Args(("filePath", "src.txt")));
            AssertTrueSuccess(deleted);
            Assert.False(File.Exists(Path.Combine(workDir, "src.txt")));
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static RuntimeValue ExecuteTool(RuntimeValue toolValue, RuntimeValue arguments)
    {
        var tool = Assert.IsType<ToolInstance>(toolValue.AsObject());
        return tool.Execute(arguments);
    }

    private static RuntimeValue Args(params (string Name, string Value)[] fields)
    {
        var obj = new JsonObject();
        foreach (var (name, value) in fields)
            obj.Set(name, RuntimeValue.String(value));
        return RuntimeValue.Object(obj);
    }

    private static void AssertTrueSuccess(RuntimeValue result)
    {
        Assert.Equal(ValueType.Object, result.Type);
        var success = result.AsObject().Get("success", null);
        Assert.NotNull(success);
        Assert.Equal(ValueType.Boolean, success!.Type);
        Assert.True(success.AsBoolean(), result.ToString());
    }

    private static string ToolErrorText(RuntimeValue result)
    {
        if (result.Type == ValueType.String)
            return result.AsString();
        if (result.Type == ValueType.Object)
        {
            var err = result.AsObject().Get("error", null);
            if (err != null && err.Type == ValueType.String)
                return err.AsString();
        }
        return result.ToString();
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
