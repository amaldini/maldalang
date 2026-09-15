// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class ClassAugmentationTests : TestBase
{
    private static List<ClassDeclaration> ParseClasses(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return statements.OfType<ClassDeclaration>().ToList();
    }

    private static string FirstParseError(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        parser.Parse();
        Assert.NotEmpty(parser.Errors);
        return parser.Errors[0].Message;
    }

    [Fact]
    public void Parse_LaterClass_MergesMethodsOntoFirst()
    {
        var classes = ParseClasses("""
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            """);

        var classDecl = Assert.Single(classes);
        Assert.Equal("Point", classDecl.Name);
        Assert.True(classDecl.HasPrimaryConstructor);
        Assert.Contains(classDecl.Members, m => m.Type == MemberType.Method && m.Name == "total");
    }

    [Fact]
    public void Parse_ThreeDeclarations_MergeInSourceOrder()
    {
        var classes = ParseClasses("""
            class Counter {
                var value = 0;
            }
            class Counter {
                function inc() {
                    this.value = this.value + 1;
                    return this.value;
                }
            }
            class Counter {
                function get() {
                    return this.value;
                }
            }
            """);

        var classDecl = Assert.Single(classes);
        Assert.Equal(new[] { "value", "inc", "get" }, classDecl.Members.Select(m => m.Name).ToArray());
    }

    [Fact]
    public void Parse_LaterExport_MarksOriginalExported()
    {
        var classDecl = Assert.Single(ParseClasses("""
            class Point(x);
            export class Point {
                function doubled() {
                    return this.x * 2;
                }
            }
            """));
        Assert.True(classDecl.IsExported);
    }

    [Fact]
    public void Parse_DuplicateMember_IsRejected()
    {
        var message = FirstParseError("""
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            class Point {
                function total() {
                    return 0;
                }
            }
            """);
        Assert.Contains("already defined on class 'Point'", message);
    }

    [Fact]
    public void Parse_LaterPrimaryConstructor_IsRejected()
    {
        var message = FirstParseError("""
            class Point {
                var x;
            }
            class Point(x);
            """);
        Assert.Contains("cannot use a primary constructor", message);
    }

    [Fact]
    public void Parse_LaterPrimaryConstructorWithBracelessMethod_IsRejected()
    {
        var message = FirstParseError("""
            class Point {
                var x;
            }
            class Point(x) function doubled() this.x * 2;
            """);
        Assert.Contains("cannot use a primary constructor", message);
    }

    [Fact]
    public void Parse_LaterExtends_IsRejected()
    {
        var message = FirstParseError("""
            class Animal { }
            class Dog { }
            class Dog extends Animal { }
            """);
        Assert.Contains("cannot add 'extends'", message);
    }

    [Fact]
    public void Parse_LaterConstructorWhenOneExists_IsRejected()
    {
        var message = FirstParseError("""
            class Box {
                function Box() { }
            }
            class Box {
                function Box(n) {
                    this.n = n;
                }
            }
            """);
        Assert.Contains("already has a constructor", message);
    }

    [Fact]
    public void Interpret_AddedMethod_CanReadOriginalFields()
    {
        var output = RunProgram("""
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            print(new Point(3, 4).total());
            """);
        Assert.Equal("7", output);
    }

    [Fact]
    public void Interpret_AddedInstanceMethod_MutatesOriginalField()
    {
        var output = RunProgram("""
            class Counter {
                var value = 0;

                function Counter() {
                }
            }
            class Counter {
                function inc() {
                    this.value = this.value + 1;
                    return this.value;
                }
            }
            var c = new Counter();
            print(c.inc());
            print(c.inc());
            """);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
    }

    [Fact]
    public void Interpret_AddedStaticMethod_CanReadStaticField()
    {
        var output = RunProgram("""
            class MathUtils {
                static var PI = 3;
            }
            class MathUtils {
                static function doublePi() {
                    return MathUtils.PI * 2;
                }
            }
            print(MathUtils.doublePi());
            """);
        Assert.Equal("6", output);
    }

    [Fact]
    public void Interpret_LaterConstructor_IsAllowedWhenOriginalHasNone()
    {
        var output = RunProgram("""
            class Box {
                var n;
            }
            class Box {
                function Box(n) {
                    this.n = n;
                }
            }
            print(new Box(4).n);
            """);
        Assert.Equal("4", output);
    }

    [Fact]
    public void Interpret_SameExtendsOnLater_IsAllowed()
    {
        var output = RunProgram("""
            class Animal {
                var name;
                function Animal(name) {
                    this.name = name;
                }
            }
            class Dog extends Animal {
                function Dog(name) {
                    super(name);
                }
            }
            class Dog extends Animal {
                function speak() {
                    return this.name + " woof";
                }
            }
            print(new Dog("Rex").speak());
            """);
        Assert.Equal("Rex woof", output);
    }

    [Fact]
    public async Task Include_CanAddMethodToClassFromOtherFile()
    {
        var tempDir = CreateTempDirectory("class_aug_include_");
        try
        {
            var mainPath = Path.Combine(tempDir, "main.malda");
            var libPath = Path.Combine(tempDir, "point.malda");

            await File.WriteAllTextAsync(libPath, "class Point(x, y);\n");
            await File.WriteAllTextAsync(mainPath, """
                include "point.malda";
                class Point {
                    function total() {
                        return this.x + this.y;
                    }
                }
                print(new Point(3, 4).total());
                """);

            var output = await CaptureInterpretAsync(await File.ReadAllTextAsync(mainPath), mainPath);
            Assert.Equal("7", output);
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void Transpile_AddedMethod_MatchesInterpreter()
    {
        var source = """
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            print(new Point(3, 4).total());
            """;
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.True(result.ExitCode == 0, $"ExitCode={result.ExitCode}\nStdErr={result.StdErr}\nStdOut={result.StdOut}");
        Assert.Equal("7", result.StdOut.Trim());
    }

    [Fact]
    public void Parse_CompactMethodInBraces_MergesOntoFirst()
    {
        var classDecl = Assert.Single(ParseClasses("""
            class Point(x, y);
            class Point { function total() this.x + this.y; }
            """));
        Assert.Contains(classDecl.Members, m => m.Type == MemberType.Method && m.Name == "total");
    }

    [Fact]
    public void Parse_BracelessMethod_MergesOntoFirst()
    {
        var classDecl = Assert.Single(ParseClasses("""
            class Point(x, y);
            class Point function total() this.x + this.y;
            """));
        Assert.Contains(classDecl.Members, m => m.Type == MemberType.Method && m.Name == "total");
    }

    [Fact]
    public void Parse_BracelessAsFirstDeclaration_IsSingleMethodClass()
    {
        var classDecl = Assert.Single(ParseClasses("class Greeter function hello() \"hi\";"));
        Assert.Equal("Greeter", classDecl.Name);
        Assert.False(classDecl.HasPrimaryConstructor);
        var method = Assert.Single(classDecl.Members);
        Assert.Equal(MemberType.Method, method.Type);
        Assert.Equal("hello", method.Name);
    }

    [Fact]
    public void Parse_BracelessStaticExport_MarksOriginalExported()
    {
        var classDecl = Assert.Single(ParseClasses("""
            class MathUtils {
                static var PI = 3;
            }
            export class MathUtils static function doublePi() MathUtils.PI * 2;
            """));
        Assert.True(classDecl.IsExported);
        Assert.Contains(classDecl.Members, m => m.Type == MemberType.Method && m.Name == "doublePi" && m.IsStatic);
    }

    [Fact]
    public void Parse_BracelessField_IsRejected()
    {
        var message = FirstParseError("class Point var x = 1;");
        Assert.Contains("function", message);
    }

    [Fact]
    public void Parse_BracelessDuplicateMember_IsRejected()
    {
        var message = FirstParseError("""
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            class Point function total() 0;
            """);
        Assert.Contains("already defined on class 'Point'", message);
    }

    [Fact]
    public void Parse_ConstructorExpressionBody_IsRejected()
    {
        var message = FirstParseError("class Box function Box() 1;");
        Assert.Contains("constructor body", message);
    }

    [Fact]
    public void Interpret_CompactMethodInBraces_ReturnsExpression()
    {
        var output = RunProgram("""
            class Point(x, y);
            class Point { function total() this.x + this.y; }
            print(new Point(3, 4).total());
            """);
        Assert.Equal("7", output);
    }

    [Fact]
    public void Interpret_BracelessMethod_ReturnsExpression()
    {
        var output = RunProgram("""
            class Point(x, y);
            class Point function doubled() this.x * 2;
            print(new Point(3, 4).doubled());
            """);
        Assert.Equal("6", output);
    }

    [Fact]
    public void Interpret_BracelessAsFirstDeclaration_CanCallMethod()
    {
        var output = RunProgram("""
            class Greeter function hello() "hi";
            print(new Greeter().hello());
            """);
        Assert.Equal("hi", output);
    }

    [Fact]
    public void Interpret_BracelessStatic_CanReadStaticField()
    {
        var output = RunProgram("""
            class MathUtils {
                static var PI = 3;
            }
            class MathUtils static function doublePi() MathUtils.PI * 2;
            print(MathUtils.doublePi());
            """);
        Assert.Equal("6", output);
    }

    [Fact]
    public void Interpret_SecondPass_AugmentsExistingClass()
    {
        var output = InterpretInSession(
            "class Point(x, y);",
            """
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            print(new Point(3, 4).total());
            """);
        Assert.Equal("7", output);
    }

    [Fact]
    public void Interpret_SecondPass_BracelessMethod_AugmentsExistingClass()
    {
        var output = InterpretInSession(
            "class Point(x, y);",
            """
            class Point function doubled() this.x * 2;
            print(new Point(3, 4).doubled());
            """);
        Assert.Equal("6", output);
    }

    [Fact]
    public void Interpret_SecondPass_ExistingInstance_SeesAddedMethod()
    {
        var output = InterpretInSession(
            """
            class Point(x, y);
            var p = new Point(3, 4);
            """,
            """
            class Point function total() this.x + this.y;
            print(p.total());
            """);
        Assert.Equal("7", output);
    }

    [Fact]
    public void Interpret_SecondPass_DuplicateMember_IsRejected()
    {
        var ex = Assert.Throws<RuntimeException>(() => InterpretInSession(
            """
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            """,
            """
            class Point {
                function total() {
                    return 0;
                }
            }
            """));
        Assert.Contains("already defined on class 'Point'", ex.Message);
    }

    [Fact]
    public void Interpret_SecondPass_LaterPrimaryConstructor_IsRejected()
    {
        var ex = Assert.Throws<RuntimeException>(() => InterpretInSession(
            "class Point { var x; }",
            "class Point(x);"));
        Assert.Contains("cannot use a primary constructor", ex.Message);
    }

    [Fact]
    public void Interpret_SecondPass_LaterExtends_IsRejected()
    {
        var ex = Assert.Throws<RuntimeException>(() => InterpretInSession(
            """
            class Animal { }
            class Dog { }
            """,
            "class Dog extends Animal { }"));
        Assert.Contains("cannot add 'extends'", ex.Message);
    }

    private string InterpretInSession(params string[] entries)
    {
        RedirectConsole();
        try
        {
            var interpreter = new Interpreter.Interpreter();
            foreach (var source in entries)
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                var parser = new Parser.Parser(tokens);
                var statements = parser.Parse();
                Assert.Empty(parser.Errors);
                interpreter.InterpretAsync(statements).GetAwaiter().GetResult();
            }
            return GetOutput();
        }
        finally
        {
            RestoreConsole();
        }
    }

    [Fact]
    public void JsTranspile_EmitsSingleClassWithAddedMethod()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            class Point(x, y);
            class Point {
                function total() {
                    return this.x + this.y;
                }
            }
            print(new Point(3, 4).total());
            """);
        Assert.Equal(1, CountOccurrences(js, "class Point {"));
        Assert.Contains("total()", js, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
                return count;
            count++;
            start = index + value.Length;
        }
    }
}
