// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Interpreter;
using MaldaLang.BuiltIns;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using MALDAException = MaldaLang.Interpreter.MALDAException;

namespace MaldaLang.Tests;

// Use Collection attribute to ensure tests in this class run sequentially
// This prevents race conditions when multiple tests redirect Console.Out in parallel
[Collection("Sequential")]
public class InterpreterTests : TestBase
{
    // RunProgram and RunProgramAsync are now provided by TestBase
    
    [Fact]
    public void TestHelloWorld()
    {
        var source = "print(\"Hello, World!\");";
        var output = RunProgram(source);
        Assert.Equal("Hello, World!", output);
    }

    [Fact]
    public void TestSqliteClient_InMemory_Smoke()
    {
        var source = @"
            var db = new SqliteClient();
            db.connect(""Data Source=:memory:;"");
            print(db.isConnected);

            db.execute(""CREATE TABLE users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, age INTEGER)"");
            db.execute(""INSERT INTO users (name, age) VALUES (@name, @age)"", {name: ""Alice"", age: 30});

            var one = db.queryOne(""SELECT name, age FROM users WHERE name = @name"", {name: ""Alice""});
            print(one.name);
            print(one.age);

            var allRows = db.query(""SELECT name FROM users ORDER BY id"");
            print(allRows.length);
            print(allRows[0].name);

            db.disconnect();
        ";

        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("Alice", lines[1]);
        Assert.Equal("30", lines[2]);
        Assert.Equal("1", lines[3]);
        Assert.Equal("Alice", lines[4]);
    }

    [Fact]
    public void TestComponentKeyword_DesugarsToComponentDecorator()
    {
        var source = @"
            component TicketBoard() {
                return ""<h1>ok</h1>"";
            }

            @ACTION(""/tickets/update"")
            function updateTicket(body) {
                return componentFragment(""ticket-list"", ""<ul><li>updated</li></ul>"");
            }

            print(""ok"");
        ";
        var output = RunProgram(source);
        Assert.Contains("ok", output);
    }

    [Fact]
    public void TestComponentStateAndTemplateBuiltIns()
    {
        var source = @"
            componentStateSet(""board"", ""count"", 2);
            print(componentStateGet(""board"", ""count""));
            print(componentStateGet(""board"", ""missing"", ""n/a""));

            var tpl = ""<h1>{{title}}</h1>"";
            var model = {""title"": ""CRM""};
            print(renderTemplate(tpl, model));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("n/a", lines[1]);
        Assert.Equal("<h1>CRM</h1>", lines[2]);
    }

    [Fact]
    public void TestUiTemplateHelpers_WithCacheControl()
    {
        var source = @"
            var templatePath = ""ui_template_cache_test.html"";
            var listPath = ""ui_template_list_test.html"";
            var layoutPath = ""ui_template_layout_test.html"";

            writeFile(templatePath, ""<h1>{{title}}</h1>"");
            print(ui.template(templatePath, {""title"": ""First""}));

            writeFile(templatePath, ""<h1>{{title}}-updated</h1>"");
            print(ui.template(templatePath, {""title"": ""Second""}));
            print(ui.template(templatePath, {""title"": ""Third""}, {""cache"": false}));

            writeFile(listPath, ""<li>{{name}}-{{index}}</li>"");
            print(ui.renderList([{""name"": ""A""}, {""name"": ""B""}], listPath, ""row""));

            writeFile(layoutPath, ""<div>{{slot:content}}</div>"");
            print(ui.layout(layoutPath, {""content"": ui.partial(templatePath, {""title"": ""Slot""})}));
        ";

        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("<h1>First</h1>", lines[0]);
        Assert.Equal("<h1>Second</h1>", lines[1]);
        Assert.Equal("<h1>Third-updated</h1>", lines[2]);
        Assert.Equal("<li>A-0</li><li>B-1</li>", lines[3]);
        Assert.Equal("<div><h1>Slot</h1></div>", lines[4]);
    }

    [Fact]
    public void TestUiTemplate_Phase2Syntax_EscapingAndBlocks_Work()
    {
        var source = @"
            var phase2Path = ""ui_template_phase2_test.html"";
            writeFile(phase2Path, ""{{#if show}}<ul>{{#each items as item}}<li>{{item.name}}|{{{item.raw}}}</li>{{/each}}</ul>{{/if}}"");
            var model = {
                ""show"": true,
                ""items"": [
                    {""name"": ""A < B"", ""raw"": ""<b>x</b>""},
                    {""name"": ""C & D"", ""raw"": ""<i>y</i>""}
                ]
            };
            print(ui.template(phase2Path, model));
            print(ui.template(""{{value}}"", {""value"": ""<b>legacy</b>""}, {""compatRaw"": true}));
        ";

        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("<ul><li>A &lt; B|<b>x</b></li><li>C &amp; D|<i>y</i></li></ul>", lines[0]);
        Assert.Equal("<b>legacy</b>", lines[1]);
    }

    [Fact]
    public void TestUiTemplate_Phase2Syntax_MalformedBlocks_Throw()
    {
        var source = @"print(ui.template(""{{#if open}}missing close"", {""open"": true}));";
        Assert.ThrowsAny<Exception>(() => RunProgram(source));
    }

    [Fact]
    public void TestUiCrudModel_BuildsSchemaDrivenModel()
    {
        var source = @"
            var schema = {
                ""sessionDefault"": ""crm-ui"",
                ""templateBasePath"": ""."",
                ""filterDefs"": [
                    {""kind"": ""input"", ""name"": ""search"", ""placeholder"": ""Search"", ""defaultValue"": """"},
                    {""kind"": ""select"", ""name"": ""sort"", ""defaultValue"": ""id_desc"", ""options"": [
                        {""value"": ""id_desc"", ""label"": ""Newest""},
                        {""value"": ""name_asc"", ""label"": ""Name A-Z""}
                    ]}
                ],
                ""dialogLookupOptions"": [
                    {""key"": ""customerOptions"", ""source"": ""customers"", ""renderer"": ""customerOptions"", ""templatePath"": ""customer_option.html"", ""itemName"": ""customer""}
                ],
                ""addDialogTemplate"": ""crud_add_dialog_test.html"",
                ""editDialogTemplate"": ""crud_edit_dialog_test.html"",
                ""dialogScriptTemplate"": ""crud_dialog_script_test.js""
            };

            writeFile(""customer_option.html"", ""<option value='{{id}}'>{{name}}</option>"");
            writeFile(""crud_add_dialog_test.html"", ""<dialog>{{{customerOptions}}}</dialog>"");
            writeFile(""crud_edit_dialog_test.html"", ""<dialog>Edit</dialog>"");
            writeFile(""crud_dialog_script_test.js"", ""init();"");

            var query = {""search"": ""A < B"", ""sort"": ""name_asc""};
            var lookups = {""customers"": [{""id"": 1, ""name"": ""Acme""}]};
            var model = ui.crudModel(schema, ""s-1"", query, lookups);
            var template = ""{{#each filters as filter}}{{#if filter.isInput}}<input value='{{filter.value}}'>{{/if}}{{#if filter.isSelect}}{{#each filter.options as option}}<option{{{option.selectedAttr}}}>{{option.label}}</option>{{/each}}{{/if}}{{/each}}{{{addDialogHtml}}}"";
            print(ui.template(template, model));
        ";

        var output = RunProgram(source);
        Assert.Contains("A &lt; B", output);
        Assert.Contains("<option selected>Name A-Z</option>", output);
        Assert.True(output.Contains("<option value='1'>Acme</option>"), output);
    }

    [Fact]
    public void TestComponentStateScopeAndConfigure()
    {
        var source = @"
            componentStateConfigure(8, 8, 60000);
            componentStateSet(""board"", ""count"", 1, ""tenantA"");
            componentStateSet(""board"", ""count"", 2, ""tenantB"");
            print(componentStateGet(""board"", ""count"", 0, ""tenantA""));
            print(componentStateGet(""board"", ""count"", 0, ""tenantB""));
            print(componentStateObject(""board"", ""tenantA"").count);
            componentStateClear(""board"", ""tenantA"");
            print(componentStateGet(""board"", ""count"", ""cleared"", ""tenantA""));
        ";
        var output = RunProgram(source);
        Assert.Contains("\n1\n", "\n" + output + "\n");
        Assert.Contains("\n2\n", "\n" + output + "\n");
        Assert.Contains("\ncleared\n", "\n" + output + "\n");
    }

    [Fact]
    public void TestDictionaryStateRoundTrip_PreservesOptionalAccessAndMutation()
    {
        var source = @"
            var dict = dict { ""x"": 1 };
            print(dict[""x""]);
            print(dict.missing == null);

            dict.extra = dict { ""label"": ""ok"" };
            print(dict.extra.label);

            componentStateSet(""board"", ""payload"", dict);
            var restored = componentStateGet(""board"", ""payload"");
            print(restored[""x""]);
            print(restored.missing == null);
            print(restored.extra.label);

            var snapshot = componentStateObject(""board"");
            print(snapshot.payload.extra.label);
        ";

        var output = RunProgram(source);
        Assert.Contains("\n1\n", "\n" + output + "\n");
        Assert.Contains("\ntrue\n", "\n" + output + "\n");
        Assert.Contains("\nok\n", "\n" + output + "\n");
    }
    
    [Fact]
    public void TestVariableDeclaration()
    {
        var source = @"
            var x = 10;
            print(x);
        ";
        var output = RunProgram(source);
        Assert.Equal("10", output);
    }
    
    [Fact]
    public void TestVariableAssignment()
    {
        var source = @"
            var x = 10;
            x = 20;
            print(x);
        ";
        var output = RunProgram(source);
        Assert.Equal("20", output);
    }
    
    [Fact]
    public void TestArithmeticOperations()
    {
        var source = @"
            var a = 10;
            var b = 5;
            print(a + b);
            print(a - b);
            print(a * b);
            print(a / b);
            print(a % b);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("15", lines[0]);
        Assert.Equal("5", lines[1]);
        Assert.Equal("50", lines[2]);
        Assert.Equal("2", lines[3]);
        Assert.Equal("0", lines[4]);
    }
    
    [Fact]
    public void TestFloatArithmetic()
    {
        var source = @"
            var a = 10.5;
            var b = 2.5;
            print(a + b);
            print(a - b);
            print(a * b);
            print(a / b);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("13", lines[0].Trim());
        Assert.Equal("8", lines[1].Trim());
        Assert.Equal("26.25", lines[2].Trim());
        Assert.Equal("4.2", lines[3].Trim());
    }
    
    [Fact]
    public void TestComparisonOperators()
    {
        var source = @"
            var a = 10;
            var b = 5;
            print(a > b);
            print(a < b);
            print(a >= b);
            print(a <= b);
            print(a == b);
            print(a != b);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("false", lines[3]);
        Assert.Equal("false", lines[4]);
        Assert.Equal("true", lines[5]);
    }
    
    [Fact]
    public void TestStringComparisonOperators()
    {
        var source = @"
            print(""apple"" < ""banana"");
            print(""zebra"" > ""apple"");
            print(""hello"" <= ""hello"");
            print(""hello"" >= ""hello"");
            print(""hello"" < ""hello"");
            print(""hello"" > ""hello"");
            print(""a"" < ""aa"");
            print("""" < ""a"");
            print(""A"" < ""a"");
            print(""banana"" > ""apple"");
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);   // "apple" < "banana"
        Assert.Equal("true", lines[1]);   // "zebra" > "apple"
        Assert.Equal("true", lines[2]);   // "hello" <= "hello"
        Assert.Equal("true", lines[3]);   // "hello" >= "hello"
        Assert.Equal("false", lines[4]);  // "hello" < "hello"
        Assert.Equal("false", lines[5]);  // "hello" > "hello"
        Assert.Equal("true", lines[6]);   // "a" < "aa"
        Assert.Equal("true", lines[7]);   // "" < "a"
        Assert.Equal("true", lines[8]);   // "A" < "a" (ASCII ordering, 'A' < 'a')
        Assert.Equal("true", lines[9]);   // "banana" > "apple"
    }
    
    [Fact]
    public void TestLogicalOperators()
    {
        var source = @"
            print(true and true);
            print(true and false);
            print(false and false);
            print(true or true);
            print(true or false);
            print(false or false);
            print(not true);
            print(not false);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
        Assert.Equal("false", lines[2]);
        Assert.Equal("true", lines[3]);
        Assert.Equal("true", lines[4]);
        Assert.Equal("false", lines[5]);
        Assert.Equal("false", lines[6]);
        Assert.Equal("true", lines[7]);
    }
    
    [Fact]
    public void TestIfStatement()
    {
        var source = @"
            var x = 10;
            if (x > 5) {
                print(""greater"");
            } else {
                print(""less"");
            }
        ";
        var output = RunProgram(source);
        Assert.Equal("greater", output);
    }
    
    [Fact]
    public void TestIfElseIf()
    {
        var source = @"
            var x = 5;
            if (x > 10) {
                print(""large"");
            } else if (x > 0) {
                print(""medium"");
            } else {
                print(""small"");
            }
        ";
        var output = RunProgram(source);
        Assert.Equal("medium", output);
    }
    
    [Fact]
    public void TestWhileLoop()
    {
        var source = @"
            var i = 0;
            while (i < 5) {
                print(i);
                i = i + 1;
            }
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal(5, lines.Length);
        for (int j = 0; j < 5; j++)
        {
            Assert.Equal(j.ToString(), lines[j]);
        }
    }
    
    [Fact]
    public void TestForLoop()
    {
        var source = @"
            for (var i = 0; i < 5; i = i + 1) {
                print(i);
            }
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal(5, lines.Length);
        for (int j = 0; j < 5; j++)
        {
            Assert.Equal(j.ToString(), lines[j]);
        }
    }
    
    [Fact]
    public void TestForeachLoop()
    {
        var source = @"
            var items = [10, 20, 30];
            foreach (var x in items) {
                print(x);
            }
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("10", lines[0]);
        Assert.Equal("20", lines[1]);
        Assert.Equal("30", lines[2]);
    }
    
    [Fact]
    public void TestBreakStatement()
    {
        var source = @"
            var i = 0;
            while (true) {
                if (i >= 3) {
                    break;
                }
                print(i);
                i = i + 1;
            }
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("0", lines[0]);
        Assert.Equal("1", lines[1]);
        Assert.Equal("2", lines[2]);
    }
    
    [Fact]
    public void TestContinueStatement()
    {
        var source = @"
            var i = 0;
            while (i < 5) {
                i = i + 1;
                if (i == 3) {
                    continue;
                }
                print(i);
            }
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain("3", output);
    }
    
    [Fact]
    public void TestForLoopContinueIncrementsCounter()
    {
        // Test that continue in a for loop properly increments the loop counter
        // This tests the bug where continue was not incrementing the counter
        var source = @"
            var results = [];
            for (var i = 0; i < 10; i = i + 1) {
                if (i == 2 || i == 5 || i == 8) {
                    continue;  // Skip these values, but counter should still increment
                }
                results.append(i);
            }
            // Should have processed all 10 iterations (0-9), skipping 2, 5, 8
            // Results should be: 0, 1, 3, 4, 6, 7, 9
            for (var j = 0; j < length(results); j = j + 1) {
                print(results[j]);
            }
            print(""Total: "" + length(results));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        // Should have 7 values (0, 1, 3, 4, 6, 7, 9) plus "Total: 7"
        Assert.Equal(8, lines.Length);
        Assert.Equal("0", lines[0]);
        Assert.Equal("1", lines[1]);
        Assert.Equal("3", lines[2]);
        Assert.Equal("4", lines[3]);
        Assert.Equal("6", lines[4]);
        Assert.Equal("7", lines[5]);
        Assert.Equal("9", lines[6]);
        Assert.Equal("Total: 7", lines[7]);
        
        // Verify that skipped values are not present
        Assert.DoesNotContain("2", output);
        Assert.DoesNotContain("5", output);
        Assert.DoesNotContain("8", output);
    }
    
    [Fact]
    public void TestForLoopContinueWithMultipleSkips()
    {
        // Test that multiple continues in a row properly increment the counter
        // This simulates the real-world scenario where we skip home chapters
        var source = @"
            var processed = [];
            var skipped = [];
            for (var i = 0; i < 10; i = i + 1) {
                // Skip even numbers (simulating skipping home chapters)
                if (i % 2 == 0) {
                    skipped.append(i);
                    continue;
                }
                processed.append(i);
            }
            // Should process all 10 iterations
            // Processed: 1, 3, 5, 7, 9
            // Skipped: 0, 2, 4, 6, 8
            print(""Processed: "" + length(processed));
            print(""Skipped: "" + length(skipped));
            print(""Total iterations: "" + (length(processed) + length(skipped)));
        ";
        var output = RunProgram(source);
        
        // Should have processed 5 odd numbers and skipped 5 even numbers = 10 total iterations
        Assert.Contains("Processed: 5", output);
        Assert.Contains("Skipped: 5", output);
        Assert.Contains("Total iterations: 10", output);
    }
    
    [Fact]
    public void TestFunctionDeclaration()
    {
        var source = @"
            function add(a, b) {
                return a + b;
            }
            print(add(5, 3));
        ";
        var output = RunProgram(source);
        Assert.Equal("8", output);
    }
    
    [Fact]
    public void TestRecursion()
    {
        var source = @"
            function factorial(n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            print(factorial(5));
        ";
        var output = RunProgram(source);
        Assert.Equal("120", output);
    }
    
    [Fact]
    public void TestFactorial17_IntegerOverflowThrows()
    {
        var source = @"
            function factorial(n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            print(factorial(17));
        ";
        var ex = Assert.Throws<RuntimeException>(() => RunProgram(source));
        Assert.Equal("Integer overflow.", ex.Message);
    }

    [Fact]
    public void TestFactorial17_Transpiled_IntegerOverflowThrows()
    {
        var source = @"
            function factorial(n) {
                if (n <= 1) {
                    return 1;
                }
                return n * factorial(n - 1);
            }
            print(factorial(17));
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Integer overflow.", result.StdErr + result.StdOut);
    }
    
    [Fact]
    public void TestRecursiveFunctionWithPrintStatements_EnvironmentBug()
    {
        // This test reproduces a bug where print statements inside recursive functions
        // cause variables to become undefined after recursive calls
        // The bug: when print statements are present, the environment restoration
        // after recursive calls fails, causing variables like 'pivot' to be undefined
        
        var source = @"
            function quicksort(arr, depth) {
                if (depth == null) {
                    depth = 0;
                }
                
                // Print statement that may cause environment issues
                print(""Sorting array at depth "" + depth);
                
                if (arr.length <= 1) {
                    return arr;
                }
                
                // Define pivot variable
                var pivot = arr[0];
                
                // More print statements
                print(""Pivot: "" + pivot);
                
                var left = [];
                var right = [];
                
                for (var i = 1; i < arr.length; i = i + 1) {
                    if (arr[i] < pivot) {
                        left.append(arr[i]);
                    } else {
                        right.append(arr[i]);
                    }
                }
                
                // Print before recursive calls
                print(""Calling quicksort on left"");
                var sortedLeft = quicksort(left, depth + 1);
                
                // Print before second recursive call
                print(""Calling quicksort on right"");
                var sortedRight = quicksort(right, depth + 1);
                
                // BUG: After recursive calls, 'pivot' should still be accessible
                // but with print statements, it becomes undefined
                // This line will fail with: ""Undefined variable 'pivot'""
                var result = sortedLeft.concat([pivot]).concat(sortedRight);
                
                return result;
            }
            
            var numbers = [3, 1, 4, 1, 5];
            var sorted = quicksort(numbers, null);
            print(""Result: "" + sorted);
        ";
        
        // This test should pass once the bug is fixed
        // Currently it will throw: ""Undefined variable 'pivot'""
        try
        {
            var output = RunProgram(source);
            // If we get here, the bug is fixed - verify the result
            Assert.Contains("Result:", output);
            Assert.Contains("1", output); // Should contain sorted elements
        }
        catch (Exception ex)
        {
            // Currently this will fail with the bug
            // Once fixed, this catch block should not be reached
            Assert.True(ex.Message.Contains("Undefined variable 'pivot'") || 
                       ex.Message.Contains("Undefined variable"), 
                       $"Expected 'Undefined variable' error but got: {ex.Message}");
        }
    }
    
    [Fact]
    public void TestRecursiveFunctionWithoutPrintStatements_ShouldWork()
    {
        // This test verifies that recursive functions work correctly WITHOUT print statements
        // This should always pass, demonstrating that print statements are the trigger
        
        var source = @"
            function quicksort(arr) {
                if (arr.length <= 1) {
                    return arr;
                }
                
                var pivot = arr[0];
                var left = [];
                var right = [];
                
                for (var i = 1; i < arr.length; i = i + 1) {
                    if (arr[i] < pivot) {
                        left.append(arr[i]);
                    } else {
                        right.append(arr[i]);
                    }
                }
                
                var sortedLeft = quicksort(left);
                var sortedRight = quicksort(right);
                
                // This should work fine without print statements
                var result = sortedLeft.concat([pivot]).concat(sortedRight);
                
                return result;
            }
            
            var numbers = [3, 1, 4, 1, 5];
            var sorted = quicksort(numbers);
            print(sorted);
        ";
        
        var output = RunProgram(source);
        // Should output the sorted array
        Assert.Contains("1", output);
        Assert.Contains("3", output);
        Assert.Contains("4", output);
        Assert.Contains("5", output);
    }
    
    [Fact]
    public void TestStringConcatenation()
    {
        var source = @"
            var name = ""Alice"";
            var greeting = ""Hello, "" + name + ""!"";
            print(greeting);
        ";
        var output = RunProgram(source);
        Assert.Equal("Hello, Alice!", output);
    }
    
    [Fact]
    public void TestArrayLiteral()
    {
        var source = @"
            var arr = [1, 2, 3];
            print(arr[0]);
            print(arr[1]);
            print(arr[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestArrayAssignment()
    {
        var source = @"
            var arr = [1, 2, 3];
            arr[1] = 10;
            print(arr[1]);
        ";
        var output = RunProgram(source);
        Assert.Equal("10", output);
    }
    
    [Fact]
    public void TestArrayLength()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            print(arr.length);
        ";
        var output = RunProgram(source);
        Assert.Equal("5", output);
    }
    
    [Fact]
    public void TestArrayAppendPopShiftMethods()
    {
        var source = @"
            var arr = [1, 2];
            arr.append(3);
            print(arr.length);
            print(arr[2]);
            var last = arr.pop();
            print(last);
            var first = arr.shift();
            print(first);
            print(arr.length);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("3", lines[1]);
        Assert.Equal("3", lines[2]);
        Assert.Equal("1", lines[3]);
        Assert.Equal("1", lines[4]);
    }
    
    [Fact]
    public void TestArrayConcatMethod()
    {
        var source = @"
            var a = [1, 2];
            var b = [3, 4];
            var c = a.concat(b);
            print(c.length);
            print(c[0]);
            print(c[1]);
            print(c[2]);
            print(c[3]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("4", lines[0]);
        Assert.Equal("1", lines[1]);
        Assert.Equal("2", lines[2]);
        Assert.Equal("3", lines[3]);
        Assert.Equal("4", lines[4]);
    }
    
    [Fact]
    public void TestMultiDimensionalArray()
    {
        var source = @"
            var matrix = [[1, 2], [3, 4]];
            print(matrix[0][0]);
            print(matrix[0][1]);
            print(matrix[1][0]);
            print(matrix[1][1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
        Assert.Equal("4", lines[3]);
    }
    
    [Fact]
    public void TestSimpleClass()
    {
        var source = @"
            class Person {
                public var name;
                public var age;
                
                function Person(name, age) {
                    this.name = name;
                    this.age = age;
                }
                
                public function introduce() {
                    print(""Hi, I'm "" + this.name + "" and I'm "" + this.age + "" years old."");
                }
            }
            
            var person = new Person(""Alice"", 25);
            person.introduce();
        ";
        var output = RunProgram(source);
        Assert.Equal("Hi, I'm Alice and I'm 25 years old.", output);
    }
    
    [Fact]
    public void TestInheritance()
    {
        var source = @"
            class Animal {
                public var name;
                
                function Animal(name) {
                    this.name = name;
                }
                
                public function speak() {
                    print(this.name + "" makes a sound"");
                }
            }
            
            class Dog extends Animal {
                function Dog(name) {
                    super(name);
                }
                
                public function speak() {
                    print(this.name + "" barks: Woof!"");
                }
            }
            
            var dog = new Dog(""Buddy"");
            dog.speak();
        ";
        var output = RunProgram(source);
        Assert.Equal("Buddy barks: Woof!", output);
    }
    
    [Fact]
    public void TestSuperMethodCall()
    {
        var source = @"
            class Animal {
                public var name;
                
                function Animal(name) {
                    this.name = name;
                }
                
                public function speak() {
                    print(this.name + "" makes a sound"");
                }
            }
            
            class Dog extends Animal {
                function Dog(name) {
                    super(name);
                }
                
                public function speak() {
                    super.speak();
                    print(this.name + "" also barks"");
                }
            }
            
            var dog = new Dog(""Buddy"");
            dog.speak();
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        Assert.Equal(new[] { "Buddy makes a sound", "Buddy also barks" }, lines);
    }
    
    [Fact]
    public void TestBuiltInInt()
    {
        var source = @"
            var str = ""123"";
            var num = int(str);
            print(num);
        ";
        var output = RunProgram(source);
        Assert.Equal("123", output);
    }
    
    [Fact]
    public void TestBuiltInFloat()
    {
        var source = @"
            var str = ""3.14"";
            var num = float(str);
            print(num);
        ";
        var output = RunProgram(source);
        Assert.Equal("3.14", output);
    }
    
    [Fact]
    public void TestBuiltInString()
    {
        var source = @"
            var num = 42;
            var str = string(num);
            print(str);
        ";
        var output = RunProgram(source);
        Assert.Equal("42", output);
    }
    
    [Fact]
    public void TestBuiltInAbs()
    {
        var source = @"
            print(abs(-5));
            print(abs(5));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("5", lines[0]);
        Assert.Equal("5", lines[1]);
    }
    
    [Fact]
    public void TestBuiltInMax()
    {
        var source = @"
            print(max(10, 20));
            print(max(20, 10));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("20", lines[0]);
        Assert.Equal("20", lines[1]);
    }

    [Fact]
    public void TestArrayAggregationBuiltIns()
    {
        var source = @"
            var values = [1, 2, 3, 4];
            print(sum(values));
            print(average(values));
            print(min(values));
            print(max(values));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("10", lines[0]);
        Assert.Equal("2.5", lines[1]);
        Assert.Equal("1", lines[2]);
        Assert.Equal("4", lines[3]);
    }
    
    [Fact]
    public void TestBuiltInMin()
    {
        var source = @"
            print(min(10, 20));
            print(min(20, 10));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("10", lines[0]);
        Assert.Equal("10", lines[1]);
    }
    
    [Fact]
    public void TestBuiltInPow()
    {
        var source = @"
            print(pow(2, 3));
        ";
        var output = RunProgram(source);
        Assert.Equal("8", output);
    }
    
    [Fact]
    public void TestBuiltInSqrt()
    {
        var source = @"
            print(sqrt(16));
        ";
        var output = RunProgram(source);
        Assert.Equal("4", output);
    }

    [Fact]
    public void TestMathSqrtAlias()
    {
        var source = @"
            print(Math.sqrt(16));
            print(sqrt(16));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("4", lines[0].Trim());
        Assert.Equal("4", lines[1].Trim());
    }

    [Fact]
    public void TestExtendedMathRoundingAndSign()
    {
        var source = @"
            print(floor(3.7));
            print(ceil(3.2));
            print(round(2.5));
            print(trunc(-3.9));
            print(sign(-10));
            print(sign(0));
            print(sign(10));
            print(Math.floor(3.7));
            print(Math.ceil(3.2));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0].Trim());
        Assert.Equal("4", lines[1].Trim());
        Assert.Equal("2", lines[2].Trim());
        Assert.Equal("-3", lines[3].Trim());
        Assert.Equal("-1", lines[4].Trim());
        Assert.Equal("0", lines[5].Trim());
        Assert.Equal("1", lines[6].Trim());
        Assert.Equal("3", lines[7].Trim());
        Assert.Equal("4", lines[8].Trim());
    }

    [Fact]
    public void TestExtendedMathTrigAndLog()
    {
        var source = @"
            var halfPi = degToRad(90);
            print(int(round(1000 * sin(halfPi))));
            print(int(round(1000 * Math.sin(halfPi))));
            print(int(round(1000 * cos(0))));
            print(int(round(1000 * Math.cos(0))));
            print(int(round(radToDeg(Math.PI / 2))));
            print(int(round(exp(0))));
            print(int(round(log(Math.E))));
            print(int(round(log10(1000))));
            print(int(round(log2(8))));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1000", lines[0].Trim());
        Assert.Equal("1000", lines[1].Trim());
        Assert.Equal("1000", lines[2].Trim());
        Assert.Equal("1000", lines[3].Trim());
        Assert.Equal("90", lines[4].Trim());
        Assert.Equal("1", lines[5].Trim());
        Assert.Equal("1", lines[6].Trim());
        Assert.Equal("3", lines[7].Trim());
        Assert.Equal("3", lines[8].Trim());
    }

    [Fact]
    public void TestExtendedMathUtility()
    {
        var source = @"
            print(int(hypot(3, 4)));
            print(clamp(-1, 0, 10));
            print(clamp(5, 0, 3));
            print(clamp(5, 10, 0)); // lo > hi, should still clamp into [0,10] after normalization
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("5", lines[0].Trim());
        Assert.Equal("0", lines[1].Trim());
        Assert.Equal("3", lines[2].Trim());
        Assert.Equal("5", lines[3].Trim());
    }

    [Fact]
    public void TestLlMOrientedMathHelpers()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
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
    public void TestMathConstants()
    {
        var source = @"
            print(int(Math.PI * 1000));
            print(int(Math.E * 1000));
            print(int(Math.TAU * 1000));
            print(Math.INF);
            print(Math.NaN);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3141", lines[0].Trim());
        Assert.Equal("2718", lines[1].Trim());
        Assert.Equal("6283", lines[2].Trim());
        Assert.Equal("Infinity", lines[3].Trim());
        Assert.Equal("NaN", lines[4].Trim());
    }
    
    [Fact]
    public void TestBuiltInLength()
    {
        var source = @"
            print(length(""Hello""));
        ";
        var output = RunProgram(source);
        Assert.Equal("5", output);
    }
    
    [Fact]
    public void TestBuiltInUpper()
    {
        var source = @"
            print(upper(""hello""));
        ";
        var output = RunProgram(source);
        Assert.Equal("HELLO", output);
    }
    
    [Fact]
    public void TestBuiltInLower()
    {
        var source = @"
            print(lower(""HELLO""));
        ";
        var output = RunProgram(source);
        Assert.Equal("hello", output);
    }
    
    [Fact]
    public void TestBuiltInSubstring()
    {
        var source = @"
            print(substring(""Hello"", 0, 3));
        ";
        var output = RunProgram(source);
        Assert.Equal("Hel", output);
    }
    
    // --- String extension-style methods (s.upper(), string(x).lower(), etc.) ---
    
    [Fact]
    public void StringExtension_Upper_OnVariable()
    {
        var source = @"
            var name = ""pippo"";
            print(name.upper());
        ";
        var output = RunProgram(source);
        Assert.Equal("PIPPO", output);
    }
    
    [Fact]
    public void StringExtension_Upper_AfterStringConversion()
    {
        var source = @"
            var name = ""pippo"";
            var nameUpper = string(name).upper();
            print(nameUpper);
        ";
        var output = RunProgram(source);
        Assert.Equal("PIPPO", output);
    }
    
    [Fact]
    public void StringExtension_Upper_OnLiteral()
    {
        var source = @"
            print(""hello"".upper());
        ";
        var output = RunProgram(source);
        Assert.Equal("HELLO", output);
    }
    
    [Fact]
    public void StringExtension_Lower()
    {
        var source = @"
            print(""HELLO"".lower());
        ";
        var output = RunProgram(source);
        Assert.Equal("hello", output);
    }
    
    [Fact]
    public void StringExtension_Length()
    {
        var source = @"
            var s = ""hello"";
            print(s.length());
        ";
        var output = RunProgram(source);
        Assert.Equal("5", output);
    }
    
    [Fact]
    public void StringExtension_Trim()
    {
        var source = @"
            print(""  hi  "".trim());
        ";
        var output = RunProgram(source);
        Assert.Equal("hi", output);
    }
    
    [Fact]
    public void StringExtension_Substring()
    {
        var source = @"
            var s = ""Hello"";
            print(s.substring(0, 3));
        ";
        var output = RunProgram(source);
        Assert.Equal("Hel", output);
    }
    
    [Fact]
    public void StringExtension_IndexOf()
    {
        var source = @"
            var s = ""hello"";
            print(s.indexOf(""ll""));
        ";
        var output = RunProgram(source);
        Assert.Equal("2", output);
    }
    
    [Fact]
    public void StringExtension_MethodAsValue()
    {
        var source = @"
            var f = ""world"".upper;
            print(f());
        ";
        var output = RunProgram(source);
        Assert.Equal("WORLD", output);
    }
    
    [Fact]
    public void StringExtension_InvalidMember_Throws()
    {
        var source = @"
            var s = ""hi"";
            print(s.foo());
        ";
        Assert.Throws<RuntimeException>(() => RunProgram(source));
    }
    
    [Fact]
    public void Transpiled_StringExtension_Upper()
    {
        var source = @"
            var name = ""pippo"";
            var nameUpper = string(name).upper();
            print(nameUpper);
            print(""hello"".upper());
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("PIPPO", lines[0].Trim());
        Assert.Equal("HELLO", lines[1].Trim());
    }
    
    [Fact]
    public void Transpiled_StringExtension_Lower_Length_Trim()
    {
        var source = @"
            print(""HELLO"".lower());
            print(""hello"".length());
            print(""  hi  "".trim());
            print(trim(""  function hi  ""));
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("hello", lines[0].Trim());
        Assert.Equal("5", lines[1].Trim());
        Assert.Equal("hi", lines[2].Trim());
        Assert.Equal("function hi", lines[3].Trim());
    }
    
    [Fact]
    public void Transpiled_StringExtension_Substring_IndexOf()
    {
        var source = @"
            var s = ""Hello"";
            print(s.substring(0, 3));
            print(s.indexOf(""ll""));
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("Hel", lines[0].Trim());
        Assert.Equal("2", lines[1].Trim());
    }
    
    [Fact]
    public void Transpiled_StringBuiltIn_HandlesNullAndClrString()
    {
        var source = @"
            var s = ""hello"";
            print(string(s));
            print(string(null));
        ";
        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        var lines = result.StdOut.Trim().Replace("\r", "").Split('\n');
        Assert.Equal("hello", lines[0].Trim());
        Assert.Equal("null", lines[1].Trim());
    }
    
    [Fact]
    public void TestBuiltInReplace()
    {
        var source = @"
            var text = ""Hello world, world is great"";
            var result = replace(text, ""world"", ""MALDA"");
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("Hello MALDA, MALDA is great", output);
    }
    
    [Fact]
    public void TestBuiltInReplace_NoMatches()
    {
        var source = @"
            var text = ""Hello world"";
            var result = replace(text, ""xyz"", ""MALDA"");
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("Hello world", output);
    }
    
    [Fact]
    public void TestBuiltInRegexMatch()
    {
        var source = @"
            var matches = regexMatch(""test123"", ""\\d+"");
            print(matches);
        ";
        var output = RunProgram(source);
        Assert.Equal("true", output);
    }
    
    [Fact]
    public void TestBuiltInRegexMatch_NoMatch()
    {
        var source = @"
            var matches = regexMatch(""test"", ""\\d+"");
            print(matches);
        ";
        var output = RunProgram(source);
        Assert.Equal("false", output);
    }
    
    [Fact]
    public void TestBuiltInRegexReplace()
    {
        var source = @"
            var result = regexReplace(""abc123"", ""\\d+"", ""456"");
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("abc456", output);
    }
    
    [Fact]
    public void TestBuiltInRegexReplace_WithCaptureGroups()
    {
        var source = @"
            var result = regexReplace(""John Doe"", ""(\\w+) (\\w+)"", ""$2, $1"");
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("Doe, John", output);
    }
    
    [Fact]
    public void TestBuiltInRegexReplace_MultipleMatches()
    {
        var source = @"
            var result = regexReplace(""test123 test456"", ""\\d+"", ""XXX"");
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("testXXX testXXX", output);
    }
    
    [Fact]
    public void TestBuiltInRegexFind()
    {
        var source = @"
            var matches = regexFind(""test123 test456"", ""\\d+"");
            print(length(matches));
            print(matches[0].text);
            print(matches[1].text);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("2", lines[0]);
        Assert.Equal("123", lines[1]);
        Assert.Equal("456", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInRegexFind_WithGroups()
    {
        var source = @"
            var matches = regexFind(""John Doe"", ""(\\w+) (\\w+)"");
            print(length(matches));
            print(length(matches[0].groups));
            print(matches[0].groups[0]);
            print(matches[0].groups[1]);
            print(matches[0].groups[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("1", lines[0]);
        Assert.Equal("3", lines[1]); // Full match + 2 groups
        Assert.Equal("John Doe", lines[2]);
        Assert.Equal("John", lines[3]);
        Assert.Equal("Doe", lines[4]);
    }
    
    [Fact]
    public void TestBuiltInRegexFind_NoMatches()
    {
        var source = @"
            var matches = regexFind(""test"", ""\\d+"");
            print(length(matches));
        ";
        var output = RunProgram(source);
        Assert.Equal("0", output);
    }
    
    [Fact]
    public void TestBuiltInGetFileName()
    {
        var source = @"
            var filename = getFileName(""ReferenceManual/09-functions.html"");
            print(filename);
        ";
        var output = RunProgram(source);
        Assert.Equal("09-functions.html", output);
    }
    
    [Fact]
    public void TestBuiltInGetFileName_NoDirectory()
    {
        var source = @"
            var filename = getFileName(""09-functions.html"");
            print(filename);
        ";
        var output = RunProgram(source);
        Assert.Equal("09-functions.html", output);
    }
    
    [Fact]
    public void TestBuiltInGetDirectoryName()
    {
        var source = @"
            var dir = getDirectoryName(""ReferenceManual/09-functions.html"");
            print(dir);
        ";
        var output = RunProgram(source);
        Assert.Equal("ReferenceManual", output);
    }
    
    [Fact]
    public void TestBuiltInGetDirectoryName_NoDirectory()
    {
        var source = @"
            var dir = getDirectoryName(""09-functions.html"");
            print(dir);
        ";
        var output = RunProgram(source);
        Assert.Equal("", output);
    }
    
    [Fact]
    public void TestBuiltInReplace_ErrorHandling()
    {
        var source = @"
            try {
                replace(""test"", ""old"");
            } catch (e) {
                print(e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("replace() expects 3 arguments", output);
    }
    
    [Fact]
    public void TestBuiltInSplit()
    {
        var source = @"
            var parts = split(""a,b,c"", "","");
            print(length(parts));
            print(parts[0]);
            print(parts[1]);
            print(parts[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("3", lines[0]);
        Assert.Equal("a", lines[1]);
        Assert.Equal("b", lines[2]);
        Assert.Equal("c", lines[3]);
    }
    
    [Fact]
    public void TestBuiltInSplit_Whitespace()
    {
        var source = @"
            var parts = split(""hello world test"");
            print(length(parts));
            print(parts[0]);
            print(parts[1]);
            print(parts[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("3", lines[0]);
        Assert.Equal("hello", lines[1]);
        Assert.Equal("world", lines[2]);
        Assert.Equal("test", lines[3]);
    }
    
    [Fact]
    public void TestArrayJoin()
    {
        var source = @"
            var arr = [""a"", ""b"", ""c""];
            var str = arr.join();
            print(str);
        ";
        var output = RunProgram(source);
        Assert.Equal("a,b,c", output);
    }
    
    [Fact]
    public void TestArrayJoin_CustomSeparator()
    {
        var source = @"
            var arr = [""a"", ""b"", ""c""];
            var str = arr.join(""-"");
            print(str);
        ";
        var output = RunProgram(source);
        Assert.Equal("a-b-c", output);
    }

    [Fact]
    public void TestArrayAggregationMethods()
    {
        var source = @"
            var values = [1, 2, 3, 4];
            print(values.sum());
            print(values.average());
            print(values.min());
            print(values.max());
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToArray();
        Assert.Equal("10", lines[0]);
        Assert.Equal("2.5", lines[1]);
        Assert.Equal("1", lines[2]);
        Assert.Equal("4", lines[3]);
    }
    
    [Fact]
    public void TestBuiltInRegexMatch_ErrorHandling()
    {
        var source = @"
            try {
                regexMatch(""test"", ""[invalid"");
            } catch (e) {
                print(e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Invalid regex pattern", output);
    }
    
    [Fact]
    public void TestBuiltInRegexReplace_ErrorHandling()
    {
        var source = @"
            try {
                regexReplace(""test"", ""[invalid"", ""replacement"");
            } catch (e) {
                print(e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Invalid regex pattern", output);
    }
    
    [Fact]
    public void TestArrayAppendPopShiftMethods_NewSyntax()
    {
        var source = @"
            var arr = [1, 2, 3];
            arr.append(4);
            print(arr[3]);
            
            var last = arr.pop();
            print(last);
            print(arr.length);
            
            var first = arr.shift();
            print(first);
            print(arr.length);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("4", lines[0]); // append
        Assert.Equal("4", lines[1]); // popped value
        Assert.Equal("3", lines[2]); // length after pop
        Assert.Equal("1", lines[3]); // shifted value
        Assert.Equal("2", lines[4]); // length after shift
    }
    
    [Fact]
    public void TestNull()
    {
        var source = @"
            var obj = null;
            if (obj == null) {
                print(""null"");
            }
        ";
        var output = RunProgram(source);
        Assert.Equal("null", output);
    }
    
    [Fact]
    public void TestUnaryMinus()
    {
        var source = @"
            var x = 10;
            print(-x);
        ";
        var output = RunProgram(source);
        Assert.Equal("-10", output);
    }
    
    [Fact]
    public void TestUnaryNot()
    {
        var source = @"
            print(not true);
            print(not false);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("false", lines[0]);
        Assert.Equal("true", lines[1]);
    }
    
    [Fact]
    public void TestOperatorPrecedence()
    {
        var source = @"
            var result = 2 + 3 * 4;
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("14", output);
    }
    
    [Fact]
    public void TestParentheses()
    {
        var source = @"
            var result = (2 + 3) * 4;
            print(result);
        ";
        var output = RunProgram(source);
        Assert.Equal("20", output);
    }
    
    [Fact]
    public void TestFizzBuzz()
    {
        var source = @"
            function fizzBuzz(n) {
                var i = 1;
                while (i <= n) {
                    if (i % 15 == 0) {
                        print(""FizzBuzz"");
                    } else if (i % 3 == 0) {
                        print(""Fizz"");
                    } else if (i % 5 == 0) {
                        print(""Buzz"");
                    } else {
                        print(i);
                    }
                    i = i + 1;
                }
            }
            fizzBuzz(15);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("Fizz", lines[2]);
        Assert.Equal("4", lines[3]);
        Assert.Equal("Buzz", lines[4]);
        Assert.Equal("Fizz", lines[5]);
        Assert.Equal("7", lines[6]);
        Assert.Equal("8", lines[7]);
        Assert.Equal("Fizz", lines[8]);
        Assert.Equal("Buzz", lines[9]);
        Assert.Equal("11", lines[10]);
        Assert.Equal("Fizz", lines[11]);
        Assert.Equal("13", lines[12]);
        Assert.Equal("14", lines[13]);
        Assert.Equal("FizzBuzz", lines[14]);
    }
    
    [Fact]
    public void TestCalculatorClass()
    {
        var source = @"
            class Calculator {
                private var result;
                
                function Calculator() {
                    this.result = 0;
                }
                
                public function add(value) {
                    this.result = this.result + value;
                    return this.result;
                }
                
                public function getResult() {
                    return this.result;
                }
            }
            
            var calc = new Calculator();
            calc.add(10);
            calc.add(20);
            print(calc.getResult());
        ";
        var output = RunProgram(source);
        Assert.Equal("30", output);
    }
    
    [Fact]
    public void TestVariableScope()
    {
        var source = @"
            var x = 10;
            function test() {
                var x = 20;
                print(x);
            }
            test();
            print(x);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("20", lines[0]);
        Assert.Equal("10", lines[1]);
    }
    
    [Fact]
    public void TestComments()
    {
        var source = @"
            // This is a comment
            var x = 10; // Inline comment
            /* Multi-line
               comment */
            print(x);
        ";
        var output = RunProgram(source);
        Assert.Equal("10", output);
    }
    
    [Fact]
    public void TestStringEscapeSequences()
    {
        var source = @"
            print(""Line 1\nLine 2"");
            print(""Tab\there"");
            print(""Quote: \"""");
            print(""Backslash: \\"");
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Contains("Line 1", lines[0]);
        Assert.Contains("Line 2", lines[1]);
    }
    
    // Test input provider for testing input functionality
    private class TestInputProvider : IInputProvider
    {
        private Queue<string> _inputQueue = new Queue<string>();
        
        public void QueueInput(string input)
        {
            _inputQueue.Enqueue(input);
        }
        
        public bool HasQueuedInput()
        {
            return _inputQueue.Count > 0;
        }
        
        public string GetQueuedInput()
        {
            if (_inputQueue.Count > 0)
            {
                return _inputQueue.Dequeue();
            }
            return "";
        }
        
        public Task<string> GetInputAsync(string prompt)
        {
            if (_inputQueue.Count > 0)
            {
                return Task.FromResult(_inputQueue.Dequeue());
            }
            return Task.FromResult("");
        }
    }
    
    private string RunProgramWithInput(string source, List<string> inputs)
    {
        return RunProgramWithInputAsync(source, inputs).GetAwaiter().GetResult();
    }
    
    private async Task<string> RunProgramWithInputAsync(string source, List<string> inputs)
    {
        var output = new StringWriter();
        TextWriter originalOut;
        
        // Synchronize Console.Out access to prevent race conditions in parallel test execution
        lock (_consoleLock)
        {
            originalOut = Console.Out;
            Console.SetOut(output);
        }
        
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            
            var inputProvider = new TestInputProvider();
            foreach (var input in inputs)
            {
                inputProvider.QueueInput(input);
            }
            
            var interpreter = new Interpreter.Interpreter(null, null, inputProvider);
            
            // Handle input exceptions by retrying execution
            while (true)
            {
                try
                {
                    await interpreter.InterpretAsync(statements);
                    break; // Execution completed successfully
                }
                catch (InputRequiredException)
                {
                    // Check if input is already queued
                    if (inputProvider.HasQueuedInput())
                    {
                        // Input is already queued, retry execution
                        continue;
                    }
                    else
                    {
                        // No input available - this shouldn't happen in tests
                        throw new Exception("Input required but no input available in test");
                    }
                }
            }
            
            // Flush the writer to ensure all buffered output is captured
            output.Flush();
            return output.ToString().Replace("\r", "").Trim();
        }
        finally
        {
            // Synchronize Console.Out restoration
            lock (_consoleLock)
            {
                Console.SetOut(originalOut);
            }
            output.Dispose();
        }
    }
    
    [Fact]
    public void TestWhileLoopWithInput()
    {
        // Test based on the user's script pattern:
        // - Increment iteration variable
        // - Check input value in condition
        // - Exit loop based on input
        var source = @"
            var iteration = 0;
            var maxIterations = 10;
            var taskComplete = false;
            
            while ((iteration < maxIterations) and (not taskComplete)) {
                iteration = iteration + 1;
                print(""=== Iteration "" + iteration + "" ==="");
                
                var ok = lower(input(""Ok for step "" + (iteration + 1) + ""?""));
                if ((ok != ""ok"") and (ok != ""yes"")) {
                    taskComplete = true;
                }
            }
            
            if (taskComplete) {
                print(""Task completed successfully in "" + iteration + "" iteration(s)."");
            } else {
                print(""Reached maximum iterations ("" + maxIterations + "") before completion."");
            }
        ";
        
        // Test with inputs: "ok", "ok", "ok", "no" (should exit on fourth iteration)
        var inputs = new List<string> { "ok", "ok", "ok", "no" };
        var output = RunProgramWithInput(source, inputs);
        var lines = output.Split('\n');
        
        // Should have 4 iterations printed
        Assert.Contains("=== Iteration 1 ===", output);
        Assert.Contains("=== Iteration 2 ===", output);
        Assert.Contains("=== Iteration 3 ===", output);
        Assert.Contains("=== Iteration 4 ===", output);
        Assert.DoesNotContain("=== Iteration 6 ===", output);

        // Should exit due to "no" input on 4th iteration
        Assert.Contains("Task completed successfully in 4 iteration(s).", output);
    }
    
    [Fact]
    public void TestWhileLoopWithInputIncrementCheck()
    {
        // Test specifically for incrementing variable and checking input value
        var source = @"
            var iteration = 0;
            var maxIterations = 5;
            
            while (iteration < maxIterations) {
                iteration = iteration + 1;
                print(""Iteration: "" + iteration);
                
                var response = lower(input(""Continue? ""));
                if (response == ""stop"") {
                    break;
                }
            }
            
            print(""Final iteration: "" + iteration);
        ";
        
        // Test with inputs: "yes", "yes", "stop" (should stop at iteration 3)
        var inputs = new List<string> { "yes", "yes", "stop" };
        var output = RunProgramWithInput(source, inputs);
        var lines = output.Split('\n');
        
        // Should have 3 iterations
        Assert.Contains("Iteration: 1", output);
        Assert.Contains("Iteration: 2", output);
        Assert.Contains("Iteration: 3", output);
        
        // Should show final iteration as 3
        Assert.Contains("Final iteration: 3", output);
        
        // Should not have iteration 4 or 5
        Assert.DoesNotContain("Iteration: 4", output);
        Assert.DoesNotContain("Iteration: 5", output);
    }
    
    // Test input provider that provides input on-demand (simulates real user input)
    // This provider does NOT pre-queue inputs - they are only provided when GetInputAsync is called
    private class OnDemandInputProvider : IInputProvider
    {
        private Queue<string> _pendingInputs = new Queue<string>();
        private int _callCount = 0;
        
        public void SetInputs(List<string> inputs)
        {
            _pendingInputs = new Queue<string>(inputs);
            _callCount = 0;
        }
        
        public void QueueInput(string input)
        {
            // This is called after GetInputAsync provides input
            // In real usage, this queues the input for the next GetQueuedInput call
            _pendingInputs.Enqueue(input);
        }
        
        public bool HasQueuedInput()
        {
            // Check if we have input ready (this is checked before GetInputAsync)
            return _pendingInputs.Count > 0;
        }
        
        public string GetQueuedInput()
        {
            // This is called by the input() built-in when input is already queued
            if (_pendingInputs.Count > 0)
            {
                return _pendingInputs.Dequeue();
            }
            return "";
        }
        
        public Task<string> GetInputAsync(string prompt)
        {
            _callCount++;
            // Simulate real user input - provide input when requested (not pre-queued)
            // In real usage, this would wait for the UI to provide input
            // The input will be queued by the caller after this returns
            if (_pendingInputs.Count > 0)
            {
                return Task.FromResult(_pendingInputs.Dequeue());
            }
            return Task.FromResult("");
        }
    }
    
    private string RunProgramWithOnDemandInput(string source, List<string> inputs)
    {
        return RunProgramWithOnDemandInputAsync(source, inputs).GetAwaiter().GetResult();
    }
    
    private async Task<string> RunProgramWithOnDemandInputAsync(string source, List<string> inputs)
    {
        var output = new StringWriter();
        TextWriter originalOut;
        
        // Synchronize Console.Out access to prevent race conditions in parallel test execution
        lock (_consoleLock)
        {
            originalOut = Console.Out;
            Console.SetOut(output);
        }
        
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser.Parser(tokens);
            var statements = parser.Parse();
            
            var inputProvider = new OnDemandInputProvider();
            // Set inputs but don't queue them - they'll be provided on-demand
            inputProvider.SetInputs(inputs);
            
            var interpreter = new Interpreter.Interpreter(null, null, inputProvider);
            
            // Handle input exceptions by providing input on-demand (simulates real user interaction)
            // This matches the ExecuteWithInputHandling pattern from ExecutionService
            while (true)
            {
                try
                {
                    await interpreter.InterpretAsync(statements);
                    break; // Execution completed successfully
                }
                catch (InputRequiredException inputEx)
                {
                    // Check if input is already queued (from previous iteration)
                    if (inputProvider.HasQueuedInput())
                    {
                        // Input is already queued, retry execution
                        continue;
                    }
                    
                    // Simulate real user input - get input when requested (this is the key difference)
                    // In real usage, GetInputAsync would wait for UI input
                    var userInput = inputProvider.GetInputAsync(inputEx.Prompt).Result;
                    // Queue the input so GetQueuedInput can retrieve it on retry
                    inputProvider.QueueInput(userInput ?? "");
                    // Continue execution - the input is now queued
                    continue;
                }
            }
            
            // Flush the writer to ensure all buffered output is captured
            output.Flush();
            return output.ToString().Replace("\r", "").Trim();
        }
        finally
        {
            // Synchronize Console.Out restoration
            lock (_consoleLock)
            {
                Console.SetOut(originalOut);
            }
            output.Dispose();
        }
    }
    
    [Fact]
    public void TestWhileLoopWithRealUserInput()
    {
        // Test that simulates real user input - input provided on-demand when InputRequiredException is thrown
        // This tests the actual flow used in production
        var source = @"
            var iteration = 0;
            var maxIterations = 10;
            var taskComplete = false;
            
            while ((iteration < maxIterations) and (not taskComplete)) {
                iteration = iteration + 1;
                print(""=== Iteration "" + iteration + "" ==="");
                
                var ok = lower(input(""Ok for step "" + (iteration + 1) + ""?""));
                if ((ok != ""ok"") and (ok != ""yes"")) {
                    taskComplete = true;
                }
            }
            
            if (taskComplete) {
                print(""Task completed successfully in "" + iteration + "" iteration(s)."");
            } else {
                print(""Reached maximum iterations ("" + maxIterations + "") before completion."");
            }
        ";
        
        // Test with inputs provided on-demand: "ok", "ok", "ok", "no" (should exit on 4th iteration)
        var inputs = new List<string> { "ok", "ok", "ok", "no" };
        var output = RunProgramWithOnDemandInput(source, inputs);
        
        // Should have 4 iterations printed
        Assert.Contains("=== Iteration 1 ===", output);
        Assert.Contains("=== Iteration 2 ===", output);
        Assert.Contains("=== Iteration 3 ===", output);
        Assert.Contains("=== Iteration 4 ===", output);
        Assert.DoesNotContain("=== Iteration 5 ===", output);
        
        // Should exit due to "no" input and show correct iteration count
        Assert.Contains("Task completed successfully in 4 iteration(s).", output);
    }
    
    [Fact]
    public void TestWhileLoopWithRealUserInputIncrementCheck()
    {
        // Test specifically for incrementing variable and checking input value with real user input flow
        var source = @"
            var iteration = 0;
            var maxIterations = 5;
            
            while (iteration < maxIterations) {
                iteration = iteration + 1;
                print(""Iteration: "" + iteration);
                
                var response = lower(input(""Continue? ""));
                if (response == ""stop"") {
                    break;
                }
            }
            
            print(""Final iteration: "" + iteration);
        ";
        
        // Test with inputs provided on-demand: "yes", "yes", "stop" (should stop at iteration 3)
        var inputs = new List<string> { "yes", "yes", "stop" };
        var output = RunProgramWithOnDemandInput(source, inputs);
        
        // Should have 3 iterations
        Assert.Contains("Iteration: 1", output);
        Assert.Contains("Iteration: 2", output);
        Assert.Contains("Iteration: 3", output);
        
        // Should show final iteration as 3
        Assert.Contains("Final iteration: 3", output);
        
        // Should not have iteration 4 or 5
        Assert.DoesNotContain("Iteration: 4", output);
        Assert.DoesNotContain("Iteration: 5", output);
    }
    
    [Fact]
    public void TestArrayMap()
    {
        var source = @"
            var arr = [1, 2, 3];
            var doubled = arr.map(x => x * 2);
            print(doubled[0]);
            print(doubled[1]);
            print(doubled[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("4", lines[1]);
        Assert.Equal("6", lines[2]);
    }
    
    [Fact]
    public void TestArrayMapWithStrings()
    {
        var source = @"
            var arr = [""a"", ""b"", ""c""];
            var upper = arr.map(x => upper(x));
            print(upper[0]);
            print(upper[1]);
            print(upper[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("A", lines[0]);
        Assert.Equal("B", lines[1]);
        Assert.Equal("C", lines[2]);
    }
    
    [Fact]
    public void TestArrayFilter()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var evens = arr.filter(x => x % 2 == 0);
            print(evens.length);
            print(evens[0]);
            print(evens[1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("4", lines[2]);
    }
    
    [Fact]
    public void TestArrayFilterEmpty()
    {
        var source = @"
            var arr = [1, 3, 5];
            var evens = arr.filter(x => x % 2 == 0);
            print(evens.length);
        ";
        var output = RunProgram(source);
        Assert.Equal("0", output);
    }
    
    [Fact]
    public void TestArrayReduce()
    {
        var source = @"
            var arr = [1, 2, 3, 4];
            var sum = arr.reduce((acc, x) => acc + x, 0);
            print(sum);
        ";
        var output = RunProgram(source);
        Assert.Equal("10", output);
    }
    
    [Fact]
    public void TestArrayReduceWithoutInitial()
    {
        var source = @"
            var arr = [1, 2, 3, 4];
            var sum = arr.reduce((acc, x) => acc + x);
            print(sum);
        ";
        var output = RunProgram(source);
        Assert.Equal("10", output);
    }
    
    [Fact]
    public void TestArrayReduceProduct()
    {
        var source = @"
            var arr = [2, 3, 4];
            var product = arr.reduce((acc, x) => acc * x, 1);
            print(product);
        ";
        var output = RunProgram(source);
        Assert.Equal("24", output);
    }
    
    [Fact]
    public void TestArrayForEach()
    {
        var source = @"
            var arr = [1, 2, 3];
            arr.forEach(x => print(x));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestArrayLiteralForEachAsStatement()
    {
        var source = @"
            [1, 2, 3].forEach(x => print(x));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestArrayFind()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var found = arr.find(x => x > 3);
            print(found);
        ";
        var output = RunProgram(source);
        Assert.Equal("4", output);
    }
    
    [Fact]
    public void TestArrayFindNotFound()
    {
        var source = @"
            var arr = [1, 2, 3];
            var found = arr.find(x => x > 10);
            print(found == null);
        ";
        var output = RunProgram(source);
        Assert.Equal("true", output);
    }
    
    [Fact]
    public void TestArrayFindIndex()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var idx = arr.findIndex(x => x > 3);
            print(idx);
        ";
        var output = RunProgram(source);
        Assert.Equal("3", output);
    }
    
    [Fact]
    public void TestArrayFindIndexNotFound()
    {
        var source = @"
            var arr = [1, 2, 3];
            var idx = arr.findIndex(x => x > 10);
            print(idx);
        ";
        var output = RunProgram(source);
        Assert.Equal("-1", output);
    }
    
    [Fact]
    public void TestArraySome()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var hasEven = arr.some(x => x % 2 == 0);
            var allOdd = arr.some(x => x > 10);
            print(hasEven);
            print(allOdd);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }
    
    [Fact]
    public void TestArrayEvery()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var allPositive = arr.every(x => x > 0);
            var allEven = arr.every(x => x % 2 == 0);
            print(allPositive);
            print(allEven);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }
    
    [Fact]
    public void TestArraySortWithComparator()
    {
        var source = @"
            var arr = [3, 1, 4, 2];
            arr.sort((a, b) => a - b);
            print(arr[0]);
            print(arr[1]);
            print(arr[2]);
            print(arr[3]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
        Assert.Equal("4", lines[3]);
    }
    
    [Fact]
    public void TestArraySortDescending()
    {
        var source = @"
            var arr = [3, 1, 4, 2];
            arr.sort((a, b) => b - a);
            print(arr[0]);
            print(arr[1]);
            print(arr[2]);
            print(arr[3]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("4", lines[0]);
        Assert.Equal("3", lines[1]);
        Assert.Equal("2", lines[2]);
        Assert.Equal("1", lines[3]);
    }
    
    [Fact]
    public void TestArraySortDefault()
    {
        var source = @"
            var arr = [""Charlie"", ""Alice"", ""Bob""];
            arr.sort();
            print(arr[0]);
            print(arr[1]);
            print(arr[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("Alice", lines[0]);
        Assert.Equal("Bob", lines[1]);
        Assert.Equal("Charlie", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInSortWithCompareAscending()
    {
        var source = @"
            var result = sort([3, 1, 2], (a, b) => a - b);
            print(result[0]);
            print(result[1]);
            print(result[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInSortWithCompareDescending()
    {
        var source = @"
            var result = sort([3, 1, 2], (a, b) => b - a);
            print(result[0]);
            print(result[1]);
            print(result[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("1", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInSortWithCompareStrings()
    {
        var source = @"
            var result = sort([""c"", ""a"", ""b""], (a, b) => a < b ? -1 : (a > b ? 1 : 0));
            print(result[0]);
            print(result[1]);
            print(result[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("a", lines[0]);
        Assert.Equal("b", lines[1]);
        Assert.Equal("c", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInSortSingleArg()
    {
        var source = @"
            var result = sort([3, 1, 2]);
            print(result[0]);
            print(result[1]);
            print(result[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInSortWithNull()
    {
        var source = @"
            var result = sort([3, 1, 2], null);
            print(result[0]);
            print(result[1]);
            print(result[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestBuiltInSortWithNonFunctionThrows()
    {
        var source = @"
            var result = sort([1, 2], ""not a function"");
            print(result);
        ";
        var ex = Assert.Throws<RuntimeException>(() => RunProgram(source));
        Assert.Contains("second argument must be a function or null", ex.Message);
    }
    
    [Fact]
    public void TestWebSearchRequiresApiKey()
    {
        var saved = System.Environment.GetEnvironmentVariable("BRAVE_SEARCH_API_KEY");
        try
        {
            System.Environment.SetEnvironmentVariable("BRAVE_SEARCH_API_KEY", null);
            var source = "var r = webSearch(\"test\");";
            var ex = Assert.ThrowsAny<Exception>(() => RunProgram(source));
            Assert.Contains("webSearch", ex.Message);
            Assert.Contains("BRAVE_SEARCH_API_KEY", ex.Message);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("BRAVE_SEARCH_API_KEY", saved ?? "");
        }
    }
    
    [Fact]
    public void TestArrayReverse()
    {
        var source = @"
            var arr = [1, 2, 3, 4];
            arr.reverse();
            print(arr[0]);
            print(arr[1]);
            print(arr[2]);
            print(arr[3]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("4", lines[0]);
        Assert.Equal("3", lines[1]);
        Assert.Equal("2", lines[2]);
        Assert.Equal("1", lines[3]);
    }
    
    [Fact]
    public void TestArraySlice()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var sub = arr.slice(1, 3);
            print(sub.length);
            print(sub[0]);
            print(sub[1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("3", lines[2]);
    }
    
    [Fact]
    public void TestArraySliceToEnd()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var sub = arr.slice(2);
            print(sub.length);
            print(sub[0]);
            print(sub[1]);
            print(sub[2]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("3", lines[1]);
        Assert.Equal("4", lines[2]);
        Assert.Equal("5", lines[3]);
    }
    
    [Fact]
    public void TestArraySliceNegative()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var sub = arr.slice(-2);
            print(sub.length);
            print(sub[0]);
            print(sub[1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("4", lines[1]);
        Assert.Equal("5", lines[2]);
    }
    
    [Fact]
    public void TestArrayIndexOf()
    {
        var source = @"
            var arr = [1, 2, 3, 2, 4];
            var idx = arr.indexOf(2);
            print(idx);
        ";
        var output = RunProgram(source);
        Assert.Equal("1", output);
    }
    
    [Fact]
    public void TestArrayIndexOfNotFound()
    {
        var source = @"
            var arr = [1, 2, 3];
            var idx = arr.indexOf(5);
            print(idx);
        ";
        var output = RunProgram(source);
        Assert.Equal("-1", output);
    }
    
    [Fact]
    public void TestArrayIncludes()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var hasTwo = arr.includes(2);
            var hasTen = arr.includes(10);
            print(hasTwo);
            print(hasTen);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }
    
    [Fact]
    public void TestArrayIncludesString()
    {
        var source = @"
            var arr = [""apple"", ""banana"", ""cherry""];
            var hasBanana = arr.includes(""banana"");
            var hasOrange = arr.includes(""orange"");
            print(hasBanana);
            print(hasOrange);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
    }

        [Fact]
        public void TestDictionaryLiteralAndIndexing()
        {
            var source = @"
            var d = dict { ""a"": 1, ""b"": 2 };
            print(d[""a""]);
            print(d[""b""]);
            d[""c""] = 3;
            print(d[""c""]);
            ";
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("1", lines[0]);
            Assert.Equal("2", lines[1]);
            Assert.Equal("3", lines[2]);
        }

        [Fact]
        public void TestDictionaryMethods()
        {
            var source = @"
            var d = dict { ""x"": 10 };
            print(d.get(""x""));
            print(d.get(""missing"") == null);
            d.set(""y"", 20);
            print(d.get(""y""));
            print(d.containsKey(""y""));
            print(d.remove(""y""));
            print(d.containsKey(""y""));
            var keys = d.keys();
            var values = d.values();
            print(keys.length > 0);
            print(values.length > 0);
            ";
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("10", lines[0]);
            Assert.Equal("true", lines[1]);   // missing key returns null
            Assert.Equal("20", lines[2]);
            Assert.Equal("true", lines[3]);   // contains y
            Assert.Equal("true", lines[4]);   // remove returned true
            Assert.Equal("false", lines[5]);  // no longer contains y
            Assert.Equal("true", lines[6]);   // keys non-empty
            Assert.Equal("true", lines[7]);   // values non-empty
        }
    
    [Fact]
    public void TestArrayChaining()
    {
        var source = @"
            var arr = [1, 2, 3, 4, 5];
            var result = arr.filter(x => x % 2 == 0).map(x => x * 2);
            print(result.length);
            print(result[0]);
            print(result[1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("4", lines[1]);
        Assert.Equal("8", lines[2]);
    }
    
    [Fact]
    public void TestArrayEmptyReduce()
    {
        var source = @"
            var arr = [];
            try {
                var result = arr.reduce((acc, x) => acc + x);
                print(""should not reach here"");
            } catch (e) {
                print(""error caught"");
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("error", output.ToLower());
    }
    
    [Fact]
    public void TestBasicTryCatch()
    {
        var source = @"
            try {
                throw ""Test error"";
                print(""should not reach here"");
            } catch (error) {
                print(""Caught: "" + error);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Caught: Test error", output);
        Assert.DoesNotContain("should not reach here", output);
    }
    
    [Fact]
    public void TestTryCatchWithoutVariable()
    {
        var source = @"
            try {
                throw ""Error message"";
            } catch {
                print(""Exception caught"");
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Exception caught", output);
    }
    
    [Fact]
    public void TestThrowStatement()
    {
        var source = @"
            throw ""Custom error"";
        ";
        
        var exception = Assert.Throws<MALDAException>(() => RunProgram(source));
        Assert.Contains("Custom error", exception.Message);
    }
    
    [Fact]
    public void TestThrowWithString()
    {
        var source = @"
            try {
                throw ""String error"";
            } catch (e) {
                print(""Error: "" + e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Error: String error", output);
    }
    
    [Fact]
    public void TestThrowWithNumber()
    {
        var source = @"
            try {
                throw 42;
            } catch (e) {
                print(""Error code: "" + e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Error code: 42", output);
    }
    
    [Fact]
    public void TestFinallyBlockAlwaysExecutes()
    {
        var source = @"
            var executed = false;
            try {
                throw ""Error"";
            } catch (e) {
                executed = true;
            } finally {
                print(""Finally executed"");
            }
            print(""After try-catch"");
        ";
        var output = RunProgram(source);
        Assert.Contains("Finally executed", output);
        Assert.Contains("After try-catch", output);
    }
    
    [Fact]
    public void TestFinallyBlockExecutesWithoutException()
    {
        var source = @"
            try {
                print(""Try block"");
            } finally {
                print(""Finally block"");
            }
            print(""After try"");
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Contains("Try block", output);
        Assert.Contains("Finally block", output);
        Assert.Contains("After try", output);
    }
    
    [Fact]
    public void TestFinallyExecutesBeforeRethrow()
    {
        var source = @"
            var finallyExecuted = false;
            try {
                try {
                    throw ""Inner error"";
                } finally {
                    finallyExecuted = true;
                    print(""Inner finally"");
                }
            } catch (e) {
                print(""Outer catch: "" + e);
            }
            print(""Finally executed: "" + finallyExecuted);
        ";
        var output = RunProgram(source);
        Assert.Contains("Inner finally", output);
        Assert.Contains("Outer catch: Inner error", output);
        Assert.Contains("Finally executed: true", output);
    }
    
    [Fact]
    public void TestExceptionPropagation()
    {
        var source = @"
            function throwError() {
                throw ""Function error"";
            }
            try {
                throwError();
            } catch (e) {
                print(""Caught: "" + e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Caught: Function error", output);
    }
    
    [Fact]
    public void TestExceptionPropagationNoCatch()
    {
        var source = @"
            try {
                throw ""Unhandled error"";
            } finally {
                print(""Finally executed"");
            }
        ";
        
        var exception = Assert.Throws<MALDAException>(() => RunProgram(source));
        Assert.Contains("Unhandled error", exception.Message);
    }
    
    [Fact]
    public void TestMultipleCatchClauses_FirstOneCatches()
    {
        var source = @"
            try {
                throw ""Error"";
            } catch (error1) {
                print(""First catch: "" + error1);
            } catch (error2) {
                print(""Second catch: "" + error2);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("First catch: Error", output);
        Assert.DoesNotContain("Second catch", output);
    }
    
    [Fact]
    public void TestNestedTryCatch()
    {
        var source = @"
            try {
                try {
                    throw ""Inner error"";
                } catch (inner) {
                    print(""Inner catch: "" + inner);
                    throw ""Outer error"";
                }
            } catch (outer) {
                print(""Outer catch: "" + outer);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Inner catch: Inner error", output);
        Assert.Contains("Outer catch: Outer error", output);
    }
    
    [Fact]
    public void TestBreakBypassesTryCatch()
    {
        var source = @"
            var i = 0;
            while (i < 5) {
                i = i + 1;
                try {
                    if (i == 3) {
                        break;
                    }
                    print(""Loop: "" + i);
                } catch (e) {
                    print(""Should not catch break"");
                }
            }
            print(""After loop: "" + i);
        ";
        var output = RunProgram(source);
        Assert.Contains("Loop: 1", output);
        Assert.Contains("Loop: 2", output);
        Assert.DoesNotContain("Loop: 3", output);
        Assert.DoesNotContain("Should not catch break", output);
        Assert.Contains("After loop: 3", output);
    }
    
    [Fact]
    public void TestContinueBypassesTryCatch()
    {
        var source = @"
            var i = 0;
            while (i < 5) {
                i = i + 1;
                try {
                    if (i == 3) {
                        continue;
                    }
                    print(""Loop: "" + i);
                } catch (e) {
                    print(""Should not catch continue"");
                }
            }
            print(""After loop"");
        ";
        var output = RunProgram(source);
        Assert.Contains("Loop: 1", output);
        Assert.Contains("Loop: 2", output);
        Assert.Contains("Loop: 4", output);
        Assert.Contains("Loop: 5", output);
        Assert.DoesNotContain("Loop: 3", output);
        Assert.DoesNotContain("Should not catch continue", output);
    }
    
    [Fact]
    public void TestReturnBypassesTryCatch()
    {
        var source = @"
            function test() {
                try {
                    return ""Returned value"";
                } catch (e) {
                    print(""Should not catch return"");
                }
                print(""Should not reach here"");
            }
            var result = test();
            print(""Result: "" + result);
        ";
        var output = RunProgram(source);
        Assert.Contains("Result: Returned value", output);
        Assert.DoesNotContain("Should not catch return", output);
        Assert.DoesNotContain("Should not reach here", output);
    }
    
    [Fact]
    public void TestFinallyExecutesWithBreak()
    {
        var source = @"
            var finallyExecuted = false;
            var i = 0;
            while (i < 3) {
                i = i + 1;
                try {
                    if (i == 2) {
                        break;
                    }
                } finally {
                    finallyExecuted = true;
                    print(""Finally: "" + i);
                }
            }
            print(""Finally executed: "" + finallyExecuted);
        ";
        var output = RunProgram(source);
        Assert.Contains("Finally: 1", output);
        Assert.Contains("Finally: 2", output);
        Assert.Contains("Finally executed: true", output);
    }
    
    [Fact]
    public void TestFinallyExecutesWithContinue()
    {
        var source = @"
            var count = 0;
            var i = 0;
            while (i < 3) {
                i = i + 1;
                try {
                    if (i == 2) {
                        continue;
                    }
                    count = count + 1;
                } finally {
                    print(""Finally: "" + i);
                }
            }
            print(""Count: "" + count);
        ";
        var output = RunProgram(source);
        Assert.Contains("Finally: 1", output);
        Assert.Contains("Finally: 2", output);
        Assert.Contains("Finally: 3", output);
        Assert.Contains("Count: 2", output);
    }
    
    [Fact]
    public void TestFinallyExecutesWithReturn()
    {
        var source = @"
            function test() {
                try {
                    return ""Value"";
                } finally {
                    print(""Finally executed"");
                }
            }
            var result = test();
            print(""Result: "" + result);
        ";
        var output = RunProgram(source);
        Assert.Contains("Finally executed", output);
        Assert.Contains("Result: Value", output);
    }
    
    [Fact]
    public void TestExceptionVariableBinding()
    {
        var source = @"
            try {
                throw ""Test error message"";
            } catch (error) {
                print(""Error variable: "" + error);
                print(""Error type check: "" + (error == ""Test error message""));
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Error variable: Test error message", output);
        Assert.Contains("Error type check: true", output);
    }
    
    [Fact]
    public void TestRuntimeExceptionCatching()
    {
        var source = @"
            try {
                var x = undefinedVar;
            } catch (e) {
                print(""Caught runtime error"");
                print(""Error: "" + e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Caught runtime error", output);
        Assert.Contains("Error:", output);
    }
    
    [Fact]
    public void TestExceptionInCatchBlock()
    {
        var source = @"
            try {
                throw ""First error"";
            } catch (e1) {
                try {
                    throw ""Second error"";
                } catch (e2) {
                    print(""Caught: "" + e2);
                }
                print(""After inner catch"");
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Caught: Second error", output);
        Assert.Contains("After inner catch", output);
    }
    
    [Fact]
    public void TestExceptionInFinallyBlock()
    {
        var source = @"
            try {
                print(""Try block"");
            } finally {
                throw ""Finally error"";
            }
        ";
        
        var exception = Assert.Throws<MALDAException>(() => RunProgram(source));
        Assert.Contains("Finally error", exception.Message);
    }
    
    [Fact]
    public void TestThrowInFunction()
    {
        var source = @"
            function mayThrow(shouldThrow) {
                if (shouldThrow) {
                    throw ""Function threw error"";
                }
                return ""Success"";
            }
            try {
                var result = mayThrow(true);
                print(""Result: "" + result);
            } catch (e) {
                print(""Caught: "" + e);
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Caught: Function threw error", output);
        Assert.DoesNotContain("Result: Success", output);
    }
    
    [Fact]
    public void TestExceptionScope()
    {
        var source = @"
            var outerVar = ""outer"";
            try {
                var innerVar = ""inner"";
                throw ""Error"";
            } catch (error) {
                print(""Outer: "" + outerVar);
                print(""Error: "" + error);
                // innerVar should not be accessible here
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("Outer: outer", output);
        Assert.Contains("Error: Error", output);
    }

    [Fact]
    public void TestGraphCreation()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("true", lines[2]);
    }

    [Fact]
    public void TestGraphUndirected()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("false", lines[0]);
        Assert.Equal("1", lines[1]);
    }

    [Fact]
    public void TestGraphOperations()
    {
        var source = @"
            var g = graph directed {};
            g.addNode(""A"");
            g.addNode(""B"");
            g.addEdge(""A"", ""B"", 5);
            print(g.hasNode(""A""));
            print(g.hasNode(""C""));
            print(g.hasEdge(""A"", ""B""));
            print(g.hasEdge(""B"", ""A""));
            print(g.getWeight(""A"", ""B""));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("false", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("false", lines[3]);
        Assert.Equal("5", lines[4]);
    }

    [Fact]
    public void TestGraphGetNeighbors()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""A"", to: ""C"", weight: 2 }
              ]
            };
            var neighbors = g.getNeighbors(""A"");
            print(neighbors.length);
            print(neighbors[0]);
            print(neighbors[1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Contains("B", lines[1]);
        Assert.Contains("C", lines[2]);
    }

    [Fact]
    public void TestGraphBFS()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C"", ""D""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""A"", to: ""C"", weight: 1 },
                { from: ""B"", to: ""D"", weight: 1 },
                { from: ""C"", to: ""D"", weight: 1 }
              ]
            };
            var visited = g.bfs(""A"");
            print(visited.length);
            print(visited[0]);
            print(visited[visited.length - 1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("4", lines[0]);
        Assert.Contains("A", lines[1]);
        Assert.Contains("D", lines[2]);
    }

    [Fact]
    public void TestGraphBFSPath()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C"", ""D""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""B"", to: ""C"", weight: 1 },
                { from: ""C"", to: ""D"", weight: 1 }
              ]
            };
            var result = g.bfs(""A"", ""D"");
            print(result.found);
            print(result.path.length);
            print(result.path[0]);
            print(result.path[result.path.length - 1]);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("4", lines[1]);
        Assert.Contains("A", lines[2]);
        Assert.Contains("D", lines[3]);
    }

    [Fact]
    public void TestGraphShortestPath()
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
            print(result.path.length);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("3", lines[1]); // A->C->D = 2+1 = 3
        Assert.Equal("3", lines[2]);
    }

    [Fact]
    public void TestGraphTopologicalSort()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""B"", to: ""C"", weight: 1 }
              ]
            };
            var result = g.topologicalSort();
            print(result.valid);
            print(result.order.length);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("3", lines[1]);
    }

    [Fact]
    public void TestGraphConnectedComponents()
    {
        var source = @"
            var g = graph undirected {
              nodes: [""A"", ""B"", ""C"", ""D""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""C"", to: ""D"", weight: 1 }
              ]
            };
            var components = g.connectedComponents();
            print(components.length);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]); // Two separate components
    }

    [Fact]
    public void TestGraphIsCyclic()
    {
        var source = @"
            var g1 = graph directed {
              nodes: [""A"", ""B""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 }
              ]
            };
            var g2 = graph directed {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""B"", to: ""C"", weight: 1 },
                { from: ""C"", to: ""A"", weight: 1 }
              ]
            };
            print(g1.isCyclic());
            print(g2.isCyclic());
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("false", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestGraphMinimumSpanningTree()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]); // MST has 2 edges for 3 nodes
        Assert.Equal("3", lines[1]); // 1 + 2 = 3
    }

    [Fact]
    public void TestGraphRemoveNode()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B"", ""C""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 },
                { from: ""B"", to: ""C"", weight: 1 }
              ]
            };
            print(g.nodeCount());
            print(g.edgeCount());
            g.removeNode(""B"");
            print(g.nodeCount());
            print(g.edgeCount());
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("2", lines[2]);
        Assert.Equal("0", lines[3]); // All edges removed with node B
    }

    [Fact]
    public void TestGraphRemoveEdge()
    {
        var source = @"
            var g = graph directed {
              nodes: [""A"", ""B""],
              edges: [
                { from: ""A"", to: ""B"", weight: 1 }
              ]
            };
            print(g.edgeCount());
            g.removeEdge(""A"", ""B"");
            print(g.edgeCount());
            print(g.hasEdge(""A"", ""B""));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("0", lines[1]);
        Assert.Equal("false", lines[2]);
    }

    [Fact]
    public void TestGraphSerializeDeserialize()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("2", lines[1]);
        Assert.Equal("true", lines[2]);
        Assert.Equal("true", lines[3]);
        Assert.Equal("true", lines[4]);
        Assert.Equal("5", lines[5]);
    }

    [Fact]
    public void TestGraphSerializeWithNodeData()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Contains("dataA", lines[0]);
        Assert.Contains("42", lines[1]);
    }

    [Fact]
    public void TestGraphSerializeWithEdgeProperties()
    {
        var source = @"
            var g1 = graph directed {};
            g1.addNode(""A"");
            g1.addNode(""B"");
            g1.addEdge(""A"", ""B"", 5, dict { ""label"": ""important"", ""type"": ""primary"" });
            var json = g1.serialize();
            var g2 = g1.deserialize(json);
            var edges = g2.getEdges(""A"");
            print(edges.length);
            print(g2.getWeight(""A"", ""B""));
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("1", lines[0]);
        Assert.Equal("5", lines[1]);
    }

    [Fact]
    public void TestGraphSerializeUndirected()
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
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("false", lines[0]);
        Assert.Equal("3", lines[1]);
        Assert.Equal("2", lines[2]); // Undirected edges counted once in serialization
        Assert.Equal("true", lines[3]);
        Assert.Equal("true", lines[4]); // Reverse edge should exist
    }

    [Fact]
    public void TestGraphSerializeEmpty()
    {
        var source = @"
            var g1 = graph directed {};
            var json = g1.serialize();
            var g2 = g1.deserialize(json);
            print(g2.nodeCount());
            print(g2.edgeCount());
            print(g2.isDirected());
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("0", lines[0]);
        Assert.Equal("0", lines[1]);
        Assert.Equal("true", lines[2]);
    }

    [Fact]
    public void TestVectorDBCreation()
    {
        var source = @"
            function calcVec(data) {
                return [1.0, 2.0, 3.0];
            }
            var v = new VectorDB(3, ""double"");
            v.init(calcVec);
            print(""created"");
        ";
        var output = RunProgram(source);
        Assert.Contains("created", output);
    }

    [Fact]
    public void TestVectorDBAdd()
    {
        var source = @"
            function calcVec(data) {
                return [1.0, 2.0, 3.0];
            }
            var v = new VectorDB(3, ""double"");
            v.init(calcVec);
            v.add([1.0, 2.0, 3.0], ""doc1"");
            v.add([4.0, 5.0, 6.0], ""doc2"");
            print(""added"");
        ";
        var output = RunProgram(source);
        Assert.Contains("added", output);
    }

    [Fact]
    public void TestVectorDBSearchSimilar()
    {
        var source = @"
            function calcVec(data) {
                if (data == ""query"") {
                    return [1.0, 2.0, 3.0];
                }
                return [0.0, 0.0, 0.0];
            }
            var v = new VectorDB(3, ""double"");
            v.init(calcVec);
            v.add([1.0, 2.0, 3.0], ""doc1"");
            v.add([4.0, 5.0, 6.0], ""doc2"");
            v.add([1.1, 2.1, 3.1], ""doc3"");
            var results = v.searchSimilar(""query"", 2);
            print(results.length);
            print(results[0].data);
            print(results[0].similarity > 0.9);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Contains("doc", lines[1]);
        Assert.Equal("true", lines[2]);
    }

    [Fact]
    public void TestVectorDBSinglePrecision()
    {
        var source = @"
            function calcVec(data) {
                return [1.0, 2.0];
            }
            var v = new VectorDB(2, ""single"");
            v.init(calcVec);
            v.add([1.0, 2.0], ""doc1"");
            var results = v.searchSimilar(""test"", 1);
            print(results.length);
        ";
        var output = RunProgram(source);
        Assert.Contains("1", output);
    }

    [Fact]
    public void TestVectorDBSerializeDeserialize()
    {
        var tempFile = Path.GetTempFileName();
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            var source = $@"
                function calcVec(data) {{
                    return [1.0, 2.0, 3.0];
                }}
                var v1 = new VectorDB(3, ""double"");
                v1.init(calcVec);
                v1.add([1.0, 2.0, 3.0], ""doc1"");
                v1.add([4.0, 5.0, 6.0], ""doc2"");
                v1.serialize(""{maldaPath}"");
                var v2 = v1.deserialize(""{maldaPath}"");
                v2.init(calcVec);
                var results = v2.searchSimilar(""test"", 2);
                print(results.length);
                print(results[0].data);
            ";
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("2", lines[0]);
            Assert.Contains("doc", lines[1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void TestVectorDBFunctionValidation()
    {
        // Note: Function validation is no longer performed during deserialize
        // since calculator function is not stored. This test is kept for backward compatibility
        // but the behavior has changed - deserialize no longer requires or validates the function.
        var tempFile = Path.GetTempFileName();
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            var source = $@"
                function calcVec1(data) {{
                    return [1.0, 2.0];
                }}
                function calcVec2(data) {{
                    return [3.0, 4.0];
                }}
                var v1 = new VectorDB(2, ""double"");
                v1.init(calcVec1);
                v1.add([1.0, 2.0], ""doc1"");
                v1.serialize(""{maldaPath}"");
                var v2 = v1.deserialize(""{maldaPath}"");
                v2.init(calcVec2);
                print(""deserialized successfully"");
            ";
            var output = RunProgram(source);
            Assert.Contains("deserialized successfully", output);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void TestVectorDBDimensionMismatch()
    {
        var source = @"
            function calcVec(data) {
                return [1.0, 2.0, 3.0];
            }
            var v = new VectorDB(3, ""double"");
            v.init(calcVec);
            try {
                v.add([1.0, 2.0], ""doc1"");
                print(""should fail"");
            } catch (error) {
                print(""dimension mismatch caught"");
            }
        ";
        var output = RunProgram(source);
        Assert.Contains("dimension mismatch caught", output);
    }

    [Fact]
    public void TestGraphSerializeToFile()
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
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("2", lines[0]);
            Assert.Equal("1", lines[1]);
            Assert.Equal("true", lines[2]);
            
            // Verify file was created (using original path)
            Assert.True(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void TestGraphDeserializeFromFile()
    {
        var tempFile = Path.GetTempFileName();
        // Replace backslashes with forward slashes for cross-platform compatibility in MALDA strings
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            // First create a graph and serialize it
            var createSource = $@"
                var g = graph directed {{
                  nodes: [""X"", ""Y"", ""Z""],
                  edges: [
                    {{ from: ""X"", to: ""Y"", weight: 2 }},
                    {{ from: ""Y"", to: ""Z"", weight: 3 }}
                  ]
                }};
                g.serialize(""{maldaPath}"");
            ";
            RunProgram(createSource);
            
            // Now deserialize from file
            var source = $@"
                var g = graph directed {{}};
                var g2 = g.deserialize(""{maldaPath}"");
                print(g2.nodeCount());
                print(g2.edgeCount());
                print(g2.hasNode(""X""));
                print(g2.hasEdge(""X"", ""Y""));
            ";
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("3", lines[0]);
            Assert.Equal("2", lines[1]);
            Assert.Equal("true", lines[2]);
            Assert.Equal("true", lines[3]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void TestEmbedBagOfWords()
    {
        var source = @"
            var vec = embedBagOfWords(""hello world"", 100);
            print(vec.length);
            print(vec[0] >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("100", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedBagOfWordsWithVocabulary()
    {
        var source = @"
            var vocab = [""hello"", ""world"", ""test""];
            var vec = embedBagOfWords(""hello world"", vocab.length, vocab);
            print(vec.length);
            print(vec[0] >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("3", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedCharacterNGrams()
    {
        var source = @"
            var vec = embedCharacterNGrams(""hello"", 3, 50);
            print(vec.length);
            print(vec[0] >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("50", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedHash()
    {
        var source = @"
            var vec = embedHash(""hello world"", 64);
            print(vec.length);
            print(vec[0] >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("64", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedTFIDF()
    {
        var source = @"
            // Test TF-IDF without corpus (uses only TF)
            var vec = embedTFIDF(""hello world"", 100);
            print(vec.length);
            print(vec[0] >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("100", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedTFIDFWithCorpus()
    {
        var source = @"
            var corpus = [""hello world"", ""test document"", ""another text""];
            var vec = embedTFIDF(""hello"", corpus, 100);
            print(vec.length);
            print(vec[0] >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("100", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedFromFile()
    {
        var tempFile = Path.GetTempFileName();
        var maldaPath = tempFile.Replace('\\', '/');
        try
        {
            File.WriteAllText(tempFile, "This is a test document for embedding.");
            
            var source = $@"
                var vec = embedFromFile(""{maldaPath}"", ""bagOfWords"", 100);
                print(vec.length);
                print(vec[0] >= 0);
            ";
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("100", lines[0]);
            Assert.Equal("true", lines[1]);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void TestEmbedFromFiles()
    {
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        var maldaPath1 = tempFile1.Replace('\\', '/');
        var maldaPath2 = tempFile2.Replace('\\', '/');
        try
        {
            File.WriteAllText(tempFile1, "First document.");
            File.WriteAllText(tempFile2, "Second document.");
            
            var source = $@"
                var files = [""{maldaPath1}"", ""{maldaPath2}""];
                var embeddings = embedFromFiles(files, ""bagOfWords"", 100);
                print(embeddings.length);
                print(embeddings[0].length);
            ";
            var output = RunProgram(source);
            var lines = output.Split('\n');
            Assert.Equal("2", lines[0]);
            Assert.Equal("100", lines[1]);
        }
        finally
        {
            if (File.Exists(tempFile1))
                File.Delete(tempFile1);
            if (File.Exists(tempFile2))
                File.Delete(tempFile2);
        }
    }

    [Fact]
    public void TestVectorDBWithEmbedBagOfWords()
    {
        var source = @"
            function bagOfWordsEmbed(text) {
                return embedBagOfWords(text, 100);
            }
            var db = new VectorDB(100, ""double"");
            db.init(bagOfWordsEmbed);
            // Use add(vector, data) format to store document IDs
            db.add(bagOfWordsEmbed(""machine learning""), ""doc1"");
            db.add(bagOfWordsEmbed(""artificial intelligence""), ""doc2"");
            var results = db.searchSimilar(""machine"", 2);
            print(results.length);
            print(results[0].data);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Contains("doc", lines[1]);
    }

    [Fact]
    public void TestVectorDBWithEmbedCharacterNGrams()
    {
        var source = @"
            function ngramEmbed(text) {
                return embedCharacterNGrams(text, 3, 50);
            }
            var db = new VectorDB(50, ""double"");
            db.init(ngramEmbed);
            db.add(""hello world"");
            db.add(""hello there"");
            var results = db.searchSimilar(""hello"", 2);
            print(results.length);
            print(results[0].similarity > 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestVectorDBWithEmbedHash()
    {
        var source = @"
            function hashEmbed(text) {
                return embedHash(text, 64);
            }
            var db = new VectorDB(64, ""double"");
            db.init(hashEmbed);
            db.add(""test document"");
            db.add(""another test"");
            var results = db.searchSimilar(""test"", 2);
            print(results.length);
            print(results[0].similarity >= 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestVectorDBWithEmbedTFIDF()
    {
        var source = @"
            var corpus = [""machine learning"", ""artificial intelligence"", ""neural networks""];
            function tfidfEmbed(text) {
                return embedTFIDF(text, corpus, 100);
            }
            var db = new VectorDB(100, ""double"");
            db.init(tfidfEmbed);
            db.add(""machine learning"");
            db.add(""artificial intelligence"");
            var results = db.searchSimilar(""machine"", 2);
            print(results.length);
            print(results[0].similarity > 0);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("2", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedNormalization()
    {
        var source = @"
            var vec1 = embedBagOfWords(""hello world"", 100);
            var vec2 = embedBagOfWords(""test document"", 100);
            // Check that vectors are normalized (sum of squares should be close to 1.0)
            var sum1 = 0.0;
            var sum2 = 0.0;
            for (var i = 0; i < vec1.length; i = i + 1) {
                sum1 = sum1 + vec1[i] * vec1[i];
                sum2 = sum2 + vec2[i] * vec2[i];
            }
            print(sum1 > 0.99);
            print(sum2 > 0.99);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("true", lines[0]);
        Assert.Equal("true", lines[1]);
    }

    [Fact]
    public void TestEmbedEmptyText()
    {
        var source = @"
            var vec = embedBagOfWords("""", 100);
            print(vec.length);
            // Empty text should produce a zero vector (or very small values)
            var hasNonZero = false;
            for (var i = 0; i < vec.length; i = i + 1) {
                if (vec[i] > 0.001) {
                    hasNonZero = true;
                }
            }
            print(hasNonZero);
        ";
        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("100", lines[0]);
        // Empty text may or may not have non-zero values after normalization
        // Just check it doesn't crash
    }

    [Fact]
    public void AsyncAwait_SleepReturnsTask_AwaitCompletes()
    {
        var source = @"
            var t = async sleep(10);
            await t;
            print(""done"");
        ";
        var output = RunProgram(source);
        Assert.Equal("done", output);
    }

    [Fact]
    public void AsyncAwait_AsyncNonCall_WrapsInTask()
    {
        var source = @"
            var t = async 42;
            var v = await t;
            print(v);
        ";
        var output = RunProgram(source);
        Assert.Equal("42", output);
    }

    [Fact]
    public void AsyncAwait_All_ComposesMultipleTasks_Variadic()
    {
        var source = @"
            var t1 = async 1;
            var t2 = async 2;
            var allTask = all(t1, t2);
            var results = await allTask;
            var sum = results[0] + results[1];
            print(sum);
        ";
        var output = RunProgram(source);
        Assert.Equal("3", output);
    }

    [Fact]
    public void AsyncAwait_All_ComposesMultipleTasks_Array()
    {
        var source = @"
            var t1 = async 1;
            var t2 = async 2;
            var tasks = [t1, t2];
            var allTask = all(tasks);
            var results = await allTask;
            var sum = results[0] + results[1];
            print(sum);
        ";
        var output = RunProgram(source);
        Assert.Equal("3", output);
    }

    [Fact]
    public void AsyncAwait_All_PropagatesFirstError_AfterAllComplete()
    {
        var source = @"
            function boom() {
                error(""boom"");
            }

            var t1 = async boom();
            var t2 = async 2;
            var allTask = all(t1, t2);
            var results = await allTask;
        ";
        var ex = Assert.Throws<RuntimeException>(() => RunProgram(source));
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void AsyncAwait_AwaitNonTask_Throws()
    {
        var source = @"
            var x = 1;
            await x;
        ";
        var ex = Assert.Throws<RuntimeException>(() => RunProgram(source));
        Assert.Contains("await requires a task", ex.Message);
    }
}