// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.PackageManager;
using MaldaLang.Parser;
using MaldaLang.Parser.AST.Declarations;
using Xunit;

namespace MaldaLang.Tests;

public class SchemaNestedTests : TestBase
{
    public SchemaNestedTests()
    {
        SchemaRegistry.ClearForTesting();
        SumTypeRegistry.ClearForTesting();
    }

    [Fact]
    public void NestedSchema_Validate_AcceptsMatchingObject()
    {
        var source = """
            schema Address {
                city: string;
            }
            schema Person {
                name: string;
                address: Address;
            }
            var good = dict {
                "name": "Ada",
                "address": dict { "city": "London" }
            };
            var check = validate("Person", good);
            print(check.ok);
            """;
        var result = RunProgram(source);
        Assert.Contains("true", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedSchema_Validate_RejectsBadNestedField()
    {
        var source = """
            schema Address {
                city: string;
            }
            schema Person {
                name: string;
                address: Address;
            }
            var bad = dict {
                "name": "Ada",
                "address": dict { "city": 42 }
            };
            var check = validate("Person", bad);
            print(check.ok);
            print(check.error);
            """;
        var result = RunProgram(source);
        Assert.Contains("false", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("city", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedSchema_ArrayOfSchema_ValidatesItems()
    {
        var source = """
            schema Tag {
                label: string;
            }
            schema Bundle {
                tags: Tag[];
            }
            var good = dict {
                "tags": [dict { "label": "a" }, dict { "label": "b" }]
            };
            var bad = dict {
                "tags": [dict { "label": 1 }]
            };
            print(validate("Bundle", good).ok);
            print(validate("Bundle", bad).ok);
            """;
        var result = RunProgram(source);
        Assert.Contains("true", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("false", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedSchema_UnknownFieldType_ThrowsOnResolve()
    {
        SchemaRegistry.ClearForTesting();
        var source = """
            schema Broken {
                x: NotAType;
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        foreach (var stmt in statements)
        {
            if (stmt is SchemaDeclaration schemaDecl)
                SchemaRegistry.Register(schemaDecl);
        }

        var ex = Assert.Throws<Exception>(() =>
        {
            _ = SchemaRegistry.TryResolve("Broken", out _);
        });
        Assert.Contains("Unknown schema field type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedSchema_Cycle_ThrowsOnResolve()
    {
        SchemaRegistry.ClearForTesting();
        var source = """
            schema A {
                b: B;
            }
            schema B {
                a: A;
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);
        foreach (var stmt in statements)
        {
            if (stmt is SchemaDeclaration schemaDecl)
                SchemaRegistry.Register(schemaDecl);
        }

        var ex = Assert.Throws<Exception>(() =>
        {
            _ = SchemaRegistry.TryResolve("A", out _);
        });
        Assert.Contains("Cyclic schema reference", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NestedSchema_ImportedSchema_ValidateWorks()
    {
        var dir = CreateTempDirectory("schema_import_");
        try
        {
            File.WriteAllText(Path.Combine(dir, "types.malda"), """
                schema Contact {
                    name: string;
                }
                """);

            var mainPath = Path.Combine(dir, "main.malda");
            File.WriteAllText(mainPath, """
                import "types.malda";
                var good = dict { "name": "Ada" };
                var check = validate("Contact", good);
                print(check.ok);
                """);

            var source = File.ReadAllText(mainPath);
            var lexer = new Lexer(source, mainPath);
            var parser = new Parser.Parser(lexer.Tokenize(), mainPath);
            var statements = parser.Parse();
            Assert.Empty(parser.Errors);

            var interpreter = new Interpreter.Interpreter(currentFile: mainPath);
            var moduleLoaderField = typeof(Interpreter.Interpreter).GetField(
                "_moduleLoader",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            moduleLoaderField!.SetValue(interpreter, new ModuleLoader());

            RedirectConsole();
            try
            {
                await interpreter.InterpretAsync(statements);
                Assert.Contains("true", GetOutput(), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                RestoreConsole();
            }
        }
        finally
        {
            SafeDeleteDirectory(dir);
        }
    }

    [Fact]
    public void NestedSchema_TypedPromptResolver_ExpandsNested()
    {
        SchemaRegistry.ClearForTesting();
        var source = """
            schema Address {
                city: string;
            }
            schema Person {
                name: string;
                address: Address;
            }
            """;
        var lexer = new Lexer(source);
        var parser = new Parser.Parser(lexer.Tokenize());
        var statements = parser.Parse();
        foreach (var stmt in statements)
        {
            if (stmt is SchemaDeclaration schemaDecl)
                SchemaRegistry.Register(schemaDecl);
        }

        Assert.True(
            TypedPromptSchemaResolver.TryResolve("Person", null, out var schema, out var error),
            error);

        var address = new JsonObject();
        address.Set("city", RuntimeValue.String("London"));
        var person = new JsonObject();
        person.Set("name", RuntimeValue.String("Ada"));
        person.Set("address", RuntimeValue.Object(address));

        Assert.True(
            TypedPromptValidator.TryValidateReturnType(
                RuntimeValue.Object(person),
                schema,
                out _,
                out var validationError),
            validationError);

        var badAddress = new JsonObject();
        badAddress.Set("city", RuntimeValue.Integer(1));
        var badPerson = new JsonObject();
        badPerson.Set("name", RuntimeValue.String("Ada"));
        badPerson.Set("address", RuntimeValue.Object(badAddress));
        Assert.False(
            TypedPromptValidator.TryValidateReturnType(
                RuntimeValue.Object(badPerson),
                schema,
                out _,
                out _));
    }
}
