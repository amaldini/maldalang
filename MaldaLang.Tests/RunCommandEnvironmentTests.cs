// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.IO;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class RunCommandEnvironmentTests : TestBase
{
    [Fact]
    public void RunCommand_TrailingObject_PassesEnvironmentToChildProcess()
    {
        var tempDir = CreateTempDirectory("run_cmd_env_");
        try
        {
            var scriptPath = Path.Combine(tempDir, "print-env.cmd");
            File.WriteAllText(scriptPath, "@echo off\r\necho RALPH_CHANGED_FILES=%RALPH_CHANGED_FILES%\r\n");

            var env = new JsonObject();
            env.Set("RALPH_CHANGED_FILES", RuntimeValue.String("src/a.cs,src/b.cs"));

            using (CommandExecutionContext.EnterUserApprovedScope())
            {
                var result = BuiltInFunctions.CallBuiltIn(
                    "runCommand",
                    new List<RuntimeValue>
                    {
                        RuntimeValue.String("cmd.exe"),
                        RuntimeValue.Array(new List<RuntimeValue>
                        {
                            RuntimeValue.String("/c"),
                            RuntimeValue.String(scriptPath)
                        }),
                        RuntimeValue.String(tempDir),
                        RuntimeValue.Object(env)
                    },
                    null);

                var obj = result.AsObject();
                Assert.Equal(0, (int)obj.Get("exitCode").AsInteger());
                var stdout = obj.Get("stdout").AsString();
                Assert.Contains("src/a.cs,src/b.cs", stdout);
            }
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
