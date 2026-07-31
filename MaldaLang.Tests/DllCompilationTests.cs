// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using MaldaLang.Compiler;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class DllCompilationTests
{
    [Fact]
    public void CompileToDll_BasicFunction_Success()
    {
        var source = @"
            function add(a, b) {
                return a + b;
            }
            
            function multiply(a, b) {
                return a * b;
            }
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            var outputDll = Path.Combine(tempDir, "test.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), $"DLL not found at: {result.OutputPath}");
            Assert.True(result.OutputPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Output should be a DLL file");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CompileToDll_WithTopLevelStatements_Success()
    {
        var source = @"
            var globalVar = 42;
            
            function getGlobal() {
                return globalVar;
            }
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            var outputDll = Path.Combine(tempDir, "test.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), $"DLL not found at: {result.OutputPath}");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CompileToDll_WithClasses_Success()
    {
        var source = @"
            class Calculator {
                function add(a, b) {
                    return a + b;
                }
                
                function subtract(a, b) {
                    return a - b;
                }
            }
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            var outputDll = Path.Combine(tempDir, "test.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), $"DLL not found at: {result.OutputPath}");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CompileToDll_GeneratesMaldaLangApiClass()
    {
        var source = @"
            function testFunction() {
                return 123;
            }
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            // Use unique DLL name to avoid assembly loading conflicts
            var uniqueName = Guid.NewGuid().ToString("N");
            var outputDll = Path.Combine(tempDir, $"test_{uniqueName}.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), $"DLL not found at: {result.OutputPath}");

            // Try to load the DLL and verify MaldaLangApi class exists
            var assembly = Assembly.LoadFrom(result.OutputPath);
            var apiType = assembly.GetType("GeneratedCode.MaldaLangApi");
            Assert.NotNull(apiType);
            Assert.True(apiType.IsClass);
            Assert.True(apiType.IsPublic);
            Assert.True(apiType.IsAbstract && apiType.IsSealed); // static class

            // Verify Initialize method exists
            var initializeMethod = apiType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(initializeMethod);
            Assert.True(initializeMethod.ReturnType == typeof(Task));

            // Verify ShutdownAsync method exists
            var shutdownMethod = apiType.GetMethod("ShutdownAsync", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(shutdownMethod);
            Assert.True(shutdownMethod.ReturnType == typeof(Task));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CompileToDll_NoMainMethod()
    {
        var source = @"
            function testFunction() {
                return 456;
            }
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            // Use unique DLL name to avoid assembly loading conflicts
            var uniqueName = Guid.NewGuid().ToString("N");
            var outputDll = Path.Combine(tempDir, $"test_{uniqueName}.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), $"DLL not found at: {result.OutputPath}");

            // Verify that Main method does NOT exist (DLLs shouldn't have Main)
            var assembly = Assembly.LoadFrom(result.OutputPath);
            var programType = assembly.GetType("GeneratedCode.Program");
            Assert.NotNull(programType);

            var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
            Assert.Null(mainMethod); // Main should not exist in DLL mode

            // But Initialize should exist
            var initializeMethod = programType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(initializeMethod);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task CompileToDll_CanCallInitialize()
    {
        var source = @"
            var initialized = false;
            
            function isInitialized() {
                return initialized;
            }
            
            initialized = true;
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            // Use unique DLL name to avoid assembly loading conflicts
            var uniqueName = Guid.NewGuid().ToString("N");
            var outputDll = Path.Combine(tempDir, $"test_{uniqueName}.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.True(result.Success, $"Compilation failed: {result.ErrorMessage}");
            Assert.NotNull(result.OutputPath);
            Assert.True(File.Exists(result.OutputPath), $"DLL not found at: {result.OutputPath}");

            // Load the DLL and call Initialize
            var assembly = Assembly.LoadFrom(result.OutputPath);
            var apiType = assembly.GetType("GeneratedCode.MaldaLangApi");
            Assert.NotNull(apiType);

            var initializeMethod = apiType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(initializeMethod);

            // Call Initialize - should not throw
            var initializeTask = (Task)initializeMethod.Invoke(null, null)!;
            await initializeTask;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void CompileToDll_InvalidSyntax_Fails()
    {
        var source = @"
            function broken() {
                return // missing value
            }
        ";

        var tempDir = Path.Combine(Path.GetTempPath(), "malda_dll_tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var sourcePath = Path.Combine(tempDir, "test.malda");
            File.WriteAllText(sourcePath, source);

            var outputDll = Path.Combine(tempDir, "test.dll");
            var compiler = new Compiler.Compiler();
            var result = compiler.Compile(sourcePath, outputDll, CompilationMode.TranspileToDll, includeLLamaSharp: false);

            Assert.False(result.Success);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Transpiled_DictionaryLiteralAndIndexing_Works()
    {
        var source = @"
            var d = dict { ""a"": 1, ""b"": 2 };
            print(d[""a""]);
            print(d[""b""]);
            d[""c""] = 3;
            print(d[""c""]);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("1", lines[0].Trim());
        Assert.Equal("2", lines[1].Trim());
        Assert.Equal("3", lines[2].Trim());
    }

    [Fact]
    public void Transpiled_SortWithCompare_Works()
    {
        var source = @"
            var result = sort([3, 1, 2], (a, b) => a - b);
            print(result[0]);
            print(result[1]);
            print(result[2]);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("1", lines[0].Trim());
        Assert.Equal("2", lines[1].Trim());
        Assert.Equal("3", lines[2].Trim());
    }

    [Fact]
    public void Transpiled_ArrayAggregationBuiltIns_Work()
    {
        var source = @"
            var values = [1, 2, 3, 4];
            print(sum(values));
            print(average(values));
            print(min(values));
            print(max(values));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("10", lines[0].Trim());
        Assert.Equal("2.5", lines[1].Trim());
        Assert.Equal("1", lines[2].Trim());
        Assert.Equal("4", lines[3].Trim());
    }

    [Fact]
    public void Transpiled_ArrayAggregationMethods_Work()
    {
        var source = @"
            var values = [1, 2, 3, 4];
            print(values.sum());
            print(values.average());
            print(values.min());
            print(values.max());
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("10", lines[0].Trim());
        Assert.Equal("2.5", lines[1].Trim());
        Assert.Equal("1", lines[2].Trim());
        Assert.Equal("4", lines[3].Trim());
    }

    [Fact]
    public void Transpiled_Math_Object_Works()
    {
        var source = @"
            print(Math.sqrt(16));
            print(Math.pow(2, 3));
            print(Math.PI > 3 && Math.PI < 4);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("4", lines[0].Trim());
        Assert.Equal("8", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
    }

    [Fact]
    public void Transpiled_ExtendedMathFunctions_Work()
    {
        var source = @"
            print(floor(3.7));
            print(ceil(3.2));
            print(round(2.5));
            print(trunc(-3.9));
            print(sign(-10));
            print(sign(0));
            print(sign(10));

            var halfPi = degToRad(90);
            print(int(round(1000 * sin(halfPi))));
            print(int(round(1000 * Math.sin(halfPi))));
            print(int(round(1000 * cos(0))));
            print(int(round(1000 * Math.cos(0))));
            print(int(round(radToDeg(Math.PI / 2))));

            print(int(hypot(3, 4)));
            print(clamp(-1, 0, 10));
            print(clamp(5, 0, 3));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("3", lines[0].Trim());
        Assert.Equal("4", lines[1].Trim());
        Assert.Equal("2", lines[2].Trim());
        Assert.Equal("-3", lines[3].Trim());
        Assert.Equal("-1", lines[4].Trim());
        Assert.Equal("0", lines[5].Trim());
        Assert.Equal("1", lines[6].Trim());
        Assert.Equal("1000", lines[7].Trim());
        Assert.Equal("1000", lines[8].Trim());
        Assert.Equal("1000", lines[9].Trim());
        Assert.Equal("1000", lines[10].Trim());
        Assert.Equal("90", lines[11].Trim());
        Assert.Equal("5", lines[12].Trim());
        Assert.Equal("0", lines[13].Trim());
        Assert.Equal("3", lines[14].Trim());
    }

    [Fact]
    public void Transpiled_LlmOrientedMathHelpers_Work()
    {
        var source = @"
            Math.seed(42);
            print(Math.rsqrt(16));
            print(Math.argmax([0.1, 0.7, 0.2]));
            print(Math.argmin([0.1, 0.7, 0.2]));
            print(int(round(Math.logSumExp([2.0, 1.0, 0.0]) * 1000)));
            var probs = Math.softmax([2.0, 1.0, 0.0]);
            print(int(round(probs[0] * 1000)));
            print(int(round(Math.crossEntropyFromLogits([2.0, 1.0, 0.0], 0) * 1000)));
            print(Math.randomChoiceWeighted([0.0, 1.0, 0.0]));
            print(int(round(Math.randn(0.1) * 1000)));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("0.25", lines[0].Trim());
        Assert.Equal("1", lines[1].Trim());
        Assert.Equal("0", lines[2].Trim());
        Assert.Equal("2408", lines[3].Trim());
        Assert.Equal("665", lines[4].Trim());
        Assert.Equal("408", lines[5].Trim());
        Assert.Equal("1", lines[6].Trim());
        Assert.Equal("140", lines[7].Trim());
    }

    [Fact]
    public void Transpiled_GraphCreation_Works()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 5 },
                { from: ""B"", to: ""C"", weight: 3 }
              ]
            };
            print(g.nodeCount());
            print(g.edgeCount());
            print(g.isDirected());
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("3", lines[0].Trim());
        Assert.Equal("2", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
    }

    [Fact]
    public void Transpiled_GraphOperations_Works()
    {
        var source = @"
            var g = graph directed {};
            g.addNode(""A"");
            g.addNode(""B"");
            g.addEdge(""A"", ""B"", 5);
            print(g.hasNode(""A""));
            print(g.hasEdge(""A"", ""B""));
            print(g.getWeight(""A"", ""B""));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("true", lines[1].Trim());
        Assert.Equal("5", lines[2].Trim());
    }

    [Fact]
    public void Transpiled_GraphShortestPath_Works()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C"", ""D""],
              edges: [
                { from: ""A"", to: ""B"", weight: 4 },
                { from: ""A"", to: ""C"", weight: 2 },
                { from: ""B"", to: ""D"", weight: 5 },
                { from: ""C"", to: ""D"", weight: 1 }
              ]
            };
            var result = g.shortestPath(""A"", ""D"");
            print(result.found);
            print(result.distance);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("true", lines[0].Trim());
        Assert.Equal("3", lines[1].Trim()); // A->C->D = 2+1 = 3
    }

    [Fact]
    public void Transpiled_Foreach_Works()
    {
        var source = @"
            var items = [10, 20, 30];
            foreach (var x in items) {
                print(x);
            }
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("10", lines[0].Trim());
        Assert.Equal("20", lines[1].Trim());
        Assert.Equal("30", lines[2].Trim());
    }

    [Fact]
    public void Transpiled_GraphBFS_Works()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C"", ""D""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""A"", to: ""C"", weight: 1 },
                { from: ""B"", to: ""D"", weight: 1 }
              ]
            };
            var visited = g.bfs(""A"");
            print(visited.length);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("4", lines[0].Trim());
    }

    [Fact]
    public void Transpiled_GraphUndirected_Works()
    {
        var source = @"
            var g = graph undirected {
              nodes: [""X"", ""Y""],
              edges: [
                { from: ""X"", to: ""Y"", weight: 10 }
              ]
            };
            print(g.isDirected());
            print(g.edgeCount());
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("false", lines[0].Trim());
        Assert.Equal("1", lines[1].Trim());
    }

    [Fact]
    public void Transpiled_GraphMinimumSpanningTree_Works()
    {
        var source = @"
            var g = graph undirected {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""B"", to: ""C"", weight: 2 },
                { from: ""A"", to: ""C"", weight: 4 }
              ]
            };
            var mst = g.minimumSpanningTree();
            print(mst.edges.length);
            print(mst.totalWeight);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("2", lines[0].Trim());
        Assert.Equal("3", lines[1].Trim());
    }

    [Fact]
    public void Transpiled_GraphSerializeDeserialize_Works()
    {
        var source = @"
            var g1 = graph directed {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 5 },
                { from: ""B"", to: ""C"", weight: 3 }
              ]
            };
            var json = g1.serialize();
            var g2 = g1.deserialize(json);
            print(g2.nodeCount());
            print(g2.edgeCount());
            print(g2.isDirected());
            print(g2.hasNode(""A""));
            print(g2.hasEdge(""A"", ""B""));
            print(g2.getWeight(""A"", ""B""));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("3", lines[0].Trim());
        Assert.Equal("2", lines[1].Trim());
        Assert.Equal("true", lines[2].Trim());
        Assert.Equal("true", lines[3].Trim());
        Assert.Equal("true", lines[4].Trim());
        Assert.Equal("5", lines[5].Trim());
    }

    [Fact]
    public void Transpiled_GraphSerializeWithNodeData_Works()
    {
        var source = @"
            var g1 = graph directed {};
            g1.addNode(""A"", ""dataA"");
            g1.addNode(""B"", 42);
            g1.addEdge(""A"", ""B"", 1);
            var json = g1.serialize();
            var g2 = g1.deserialize(json);
            print(g2.getNode(""A""));
            print(g2.getNode(""B""));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Contains("dataA", lines[0]);
        Assert.Contains("42", lines[1]);
    }

    [Fact]
    public void Transpiled_GraphSerializeUndirected_Works()
    {
        var source = @"
            var g1 = graph undirected {
              nodes: [""X"", ""Y"", ""Z""],
              edges: [
                { from: ""X"", to: ""Y"", weight: 10 },
                { from: ""Y"", to: ""Z"", weight: 5 }
              ]
            };
            var json = g1.serialize();
            var g2 = g1.deserialize(json);
            print(g2.isDirected());
            print(g2.nodeCount());
            print(g2.edgeCount());
            print(g2.hasEdge(""X"", ""Y""));
            print(g2.hasEdge(""Y"", ""X""));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("false", lines[0].Trim());
        Assert.Equal("3", lines[1].Trim());
        Assert.Equal("2", lines[2].Trim());
        Assert.Equal("true", lines[3].Trim());
        Assert.Equal("true", lines[4].Trim());
    }

    [Fact]
    public void Transpiled_GraphSerializeToFile_Works()
    {
        var tempFile = Path.GetTempFileName();
        // Replace backslashes with forward slashes for cross-platform compatibility in MALDA strings
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            var source = $@"
                var g1 = graph directed {{
                  nodes: [""A"", ""B""],
                  edges: [
                    {{ from: ""A"", to: ""B"", weight: 7 }}
                  ]
                }};
                var filePath = g1.serialize(""{maldaPath}"");
                var g2 = g1.deserialize(filePath);
                print(g2.nodeCount());
                print(g2.edgeCount());
                print(g2.hasEdge(""A"", ""B""));
            ";

            var result = TranspiledTestRunner.CompileAndRunFromSource(source);

            Assert.Equal(0, result.ExitCode);
            var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
            Assert.Equal("2", lines[0].Trim());
            Assert.Equal("1", lines[1].Trim());
            Assert.Equal("true", lines[2].Trim());
            
            // Verify file was created
            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
