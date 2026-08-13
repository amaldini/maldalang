// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class PrimaryConstructorTests : TestBase
{
    private static ClassDeclaration ParseClass(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser.Parser(tokens);
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        return Assert.IsType<ClassDeclaration>(Assert.Single(statements));
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
    public void Parse_DataOnly_DesugarsPublicFieldsAndConstructor()
    {
        var classDecl = ParseClass("class Point(x, y);");

        Assert.Null(classDecl.Superclass);
        Assert.Equal(3, classDecl.Members.Count);

        var x = classDecl.Members[0];
        Assert.Equal(MemberType.Field, x.Type);
        Assert.Equal("x", x.Name);
        Assert.Equal(AccessModifier.Public, x.Access);
        Assert.Null(x.TypeHint);

        var y = classDecl.Members[1];
        Assert.Equal(MemberType.Field, y.Type);
        Assert.Equal("y", y.Name);
        Assert.Equal(AccessModifier.Public, y.Access);

        var ctor = classDecl.Members[2];
        Assert.Equal(MemberType.Constructor, ctor.Type);
        var func = Assert.IsType<FunctionDeclaration>(ctor.Value);
        Assert.Equal(new[] { "x", "y" }, func.Parameters);
        Assert.Equal(2, func.Body.Statements.Count);
        var assign = Assert.IsType<AssignmentStatement>(func.Body.Statements[0]);
        var target = Assert.IsType<MemberAccessExpression>(assign.Target);
        Assert.IsType<ThisExpression>(target.Object);
        Assert.Equal("x", target.Member);
        Assert.Equal("x", Assert.IsType<IdentifierExpression>(assign.Value).Name);
    }

    [Fact]
    public void Parse_TypeHints_AreStoredOnFieldsAndConstructor()
    {
        var classDecl = ParseClass("class Point(x: float, y: float);");

        Assert.Equal("float", classDecl.Members[0].TypeHint);
        Assert.Equal("float", classDecl.Members[1].TypeHint);
        var func = Assert.IsType<FunctionDeclaration>(classDecl.Members[2].Value);
        Assert.Equal(new[] { "float", "float" }, func.ParameterTypeHints);
    }

    [Fact]
    public void Parse_BodyMethods_FollowSynthesizedMembers()
    {
        var classDecl = ParseClass("""
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            """);

        Assert.Equal(4, classDecl.Members.Count);
        Assert.Equal(MemberType.Field, classDecl.Members[0].Type);
        Assert.Equal(MemberType.Field, classDecl.Members[1].Type);
        Assert.Equal(MemberType.Constructor, classDecl.Members[2].Type);
        var method = classDecl.Members[3];
        Assert.Equal(MemberType.Method, method.Type);
        Assert.Equal("total", method.Name);
    }

    [Fact]
    public void Parse_EmptyBodyBraces_StillSynthesizesConstructor()
    {
        var classDecl = ParseClass("class Point(x, y) { }");
        Assert.Equal(3, classDecl.Members.Count);
        Assert.Equal(MemberType.Constructor, classDecl.Members[2].Type);
    }

    [Fact]
    public void Parse_Export_IsPreserved()
    {
        var classDecl = ParseClass("export class Point(x, y);");
        Assert.True(classDecl.IsExported);
        Assert.Equal("Point", classDecl.Name);
    }

    [Fact]
    public void Parse_Extends_IsRejected()
    {
        var message = FirstParseError("""
            class Point(x, y) extends Shape {
            }
            """);
        Assert.Contains("cannot be combined with 'extends'", message);
    }

    [Fact]
    public void Parse_ExplicitConstructor_IsRejected()
    {
        var message = FirstParseError("""
            class Point(x) {
                function Point(x) {
                    this.x = x;
                }
            }
            """);
        Assert.Contains("already has a primary constructor", message);
    }

    [Fact]
    public void Parse_DuplicateField_IsRejected()
    {
        var message = FirstParseError("""
            class Point(x) {
                var x;
            }
            """);
        Assert.Contains("duplicates a primary constructor parameter", message);
    }

    [Fact]
    public void Parse_DuplicateParameter_IsRejected()
    {
        var message = FirstParseError("class Point(x, x);");
        Assert.Contains("Duplicate primary constructor parameter 'x'", message);
    }

    [Fact]
    public void Interpret_DataOnly_AssignsPublicFields()
    {
        var output = RunProgram("""
            class Point(x, y);
            var p = new Point(3, 4);
            print(p.x);
            print(p.y);
            """);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("4", lines[1]);
    }

    [Fact]
    public void Interpret_Methods_CanReadPrimaryFields()
    {
        var output = RunProgram("""
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            print(new Point(3, 4).total());
            """);
        Assert.Equal("7", output);
    }

    [Fact]
    public void Interpret_ExtraBodyField_IsAllowed()
    {
        var output = RunProgram("""
            class Point(x) {
                public var z;
                function fill() {
                    this.z = this.x + 1;
                    return this.z;
                }
            }
            var p = new Point(10);
            print(p.fill());
            """);
        Assert.Equal("11", output);
    }

    [Fact]
    public void Transpile_Methods_MatchInterpreter()
    {
        var source = """
            class Point(x, y) {
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
    public void JsTranspile_EmitsConstructorAndFields()
    {
        var js = new Compiler.Compiler().TranspileToJavaScriptFromSource("""
            class Point(x, y) {
                function total() {
                    return this.x + this.y;
                }
            }
            print(new Point(3, 4).total());
            """);
        Assert.Contains("class Point {", js, StringComparison.Ordinal);
        Assert.Contains("constructor(x, y)", js, StringComparison.Ordinal);
        Assert.Contains("this.x = x", js, StringComparison.Ordinal);
        Assert.Contains("total()", js, StringComparison.Ordinal);
    }
}
