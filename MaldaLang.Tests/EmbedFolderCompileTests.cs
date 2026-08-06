// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MaldaLang.Compiler;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class EmbedFolderCompileTests
{
    [Fact]
    public void Transpile_EmbedFolder_Readable_Without_Disk_Copy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "malda_embed_folder_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var embedDir = Path.Combine(tempDir, "fixture");
        Directory.CreateDirectory(embedDir);
        File.WriteAllText(Path.Combine(embedDir, "hello.txt"), "from-embed", Encoding.UTF8);
        Directory.CreateDirectory(Path.Combine(embedDir, "notes"));
        File.WriteAllText(Path.Combine(embedDir, "notes", "a.md"), "note body", Encoding.UTF8);

        var sourcePath = Path.Combine(tempDir, "program.malda");
        File.WriteAllText(sourcePath,
            "print(io.readFile(\"embed:fixture/hello.txt\"));\n" +
            "print(io.hasEmbeddedFolder(\"fixture\"));\n" +
            "print(io.hasFile(io.pathJoin(io.embeddedFolderRoot(\"fixture\"), \"notes\", \"a.md\")));\n",
            Encoding.UTF8);

        var outputExe = Path.Combine(tempDir, "program.exe");
        try
        {
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(
                sourcePath,
                outputExe,
                CompilationMode.TranspileToCSharp,
                includeLLamaSharp: false,
                includeUiHost: false,
                profilingOptions: null,
                typedTranspileLevel: 1,
                includeOptionalPacks: false,
                embedFolderArgs: new[] { embedDir });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.True(File.Exists(result.OutputPath), "compiled exe missing");

            // Remove the source embed folder so runtime cannot fall back to disk.
            Directory.Delete(embedDir, recursive: true);

            var psi = new ProcessStartInfo
            {
                FileName = result.OutputPath!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
            };
            using var process = Process.Start(psi);
            Assert.NotNull(process);
            var stdout = process!.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(60000);
            Assert.True(process.ExitCode == 0, $"exit={process.ExitCode}\nstdout={stdout}\nstderr={stderr}");
            Assert.Contains("from-embed", stdout, StringComparison.Ordinal);
            Assert.Contains("true", stdout, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
