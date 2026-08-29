// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.Interpreter;
using MaldaLang.Parser;
using System.IO;
using System.Text;
using MaldaLang.TestLib;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class DotNetInteropTests : TestBase
{
    // RunProgramAsync is now provided by TestBase

    private static string GetFooAssemblyPath()
    {
        // Use the location of the Foo type to get the compiled DLL path for MaldaLang.TestLib
        var asm = typeof(Foo).Assembly;
        return asm.Location;
    }

    [Fact]
    public async Task DotNetInterop_CanCallMethod_AndAccessProperty()
    {
        var asmPath = GetFooAssemblyPath().Replace("\\", "\\\\");

        var source = $@"
var asm = loadAssembly(""{asmPath}"");
var t = getDotNetType(asm, ""MaldaLang.TestLib.Foo"");
var foo = dotnetNew(t);

var sum = foo.Add(2, 3);
print(sum);

foo.Name = ""from MALDA"";
print(foo.Name);
";
        var output = await RunProgramAsync(source);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("5", lines[0]);
        Assert.Contains("from MALDA", lines[1]);
    }
}
