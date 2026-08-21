// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class TranspiledAsyncTests
{
    [Fact]
    public void Transpiled_OverlappingUserSleep_BindsTasksOnCaller_AllSums()
    {
        var source = @"
            function computeA() {
                sleep(20);
                return 1;
            }
            function computeB() {
                sleep(30);
                return 2;
            }
            var tA = async computeA();
            var tB = async computeB();
            var results = await all(tA, tB);
            print(results[0] + results[1]);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_UserSleep_LocalSurvivesAcrossAwait()
    {
        var source = @"
            function compute() {
                var x = 41;
                sleep(10);
                return x + 1;
            }
            var t = async compute();
            print(await t);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("42", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_All_ComposesMultipleTasks_Variadic()
    {
        var source = @"
            var t1 = async 1;
            var t2 = async 2;
            var allTask = all(t1, t2);
            var results = await allTask;
            var sum = results[0] + results[1];
            print(sum);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_All_ComposesMultipleTasks_Array()
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

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("3", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_PhaseAComponentBuiltIns_Work()
    {
        var source = @"
            component TicketBoard() {
                return ""<h1>board</h1>"";
            }

            componentStateSet(""board"", ""count"", 4);
            print(componentStateGet(""board"", ""count""));
            print(renderTemplate(""<div>{{name}}</div>"", parseJSON(""{\""name\"": \""MALDA\""}"")));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("4\n<div>MALDA</div>", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_ToJson_ObjectLiteral_RoundTripsFields()
    {
        var source = @"
            var encoded = toJSON({""title"": ""ACME CRM"", ""subtitle"": ""Commercial""});
            var decoded = parseJSON(encoded);
            print(decoded.title);
            print(decoded.subtitle);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ACME CRM\nCommercial", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_ToJson_ObjectLiteral_PersistsThroughSqlite()
    {
        var source = @"
            var db = new SqliteClient();
            db.connect(""Data Source=:memory:;"");
            db.execute(""CREATE TABLE crm_settings (key TEXT PRIMARY KEY, value_json TEXT NOT NULL)"");

            var body = parseJSON(""{\""title\"":\""ACME CRM\"",\""subtitle\"":\""Commercial foundation\""}"");
            var title = body.title;
            if (title == null || title == """") {
                title = ""MALDA CRM"";
            }
            var subtitle = body.subtitle;
            if (subtitle == null || subtitle == """") {
                subtitle = ""Commercial foundation"";
            }
            var valueJson = toJSON({""title"": title, ""subtitle"": subtitle});

            db.execute(""INSERT INTO crm_settings (key, value_json) VALUES ('branding', @valueJson)"", {""valueJson"": valueJson});
            var row = db.queryOne(""SELECT value_json AS valueJson FROM crm_settings WHERE key = 'branding'"");
            var branding = parseJSON(row.valueJson);
            print(branding.title);
            print(branding.subtitle);

            var valueJson2 = toJSON({""title"": ""New Title"", ""subtitle"": ""New Subtitle""});
            db.execute(""INSERT INTO crm_settings (key, value_json) VALUES ('branding', @valueJson) ON CONFLICT(key) DO UPDATE SET value_json = @valueJson"", {""valueJson"": valueJson2});
            var row2 = db.queryOne(""SELECT value_json AS valueJson FROM crm_settings WHERE key = 'branding'"");
            var branding2 = parseJSON(row2.valueJson);
            print(branding2.title);
            print(branding2.subtitle);
            db.disconnect();
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ACME CRM\nCommercial foundation\nNew Title\nNew Subtitle", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_UiTemplateHelpers_Work()
    {
        var source = @"
            var templatePath = ""transpiled_ui_template_test.html"";
            var listPath = ""transpiled_ui_list_test.html"";
            var layoutPath = ""transpiled_ui_layout_test.html"";

            writeFile(templatePath, ""<h1>{{title}}</h1>"");
            writeFile(listPath, ""<li>{{name}}-{{index}}</li>"");
            writeFile(layoutPath, ""<div>{{slot:content}}</div>"");

            print(ui.template(templatePath, {""title"": ""A""}));
            writeFile(templatePath, ""<h1>{{title}}-updated</h1>"");
            print(ui.template(templatePath, {""title"": ""B""}, {""cache"": false}));
            print(ui.renderList([{""name"": ""x""}, {""name"": ""y""}], listPath, ""row""));
            print(ui.layout(layoutPath, {""content"": ui.partial(templatePath, {""title"": ""slot""})}));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("<h1>A</h1>\n<h1>B-updated</h1>\n<li>x-0</li><li>y-1</li>\n<div><h1>slot</h1></div>", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_UiTemplate_Phase2BlocksAndEscaping_Work()
    {
        var source = @"
            var path = ""transpiled_ui_phase2_block_test.html"";
            writeFile(path, ""{{#if show}}<ul>{{#each rows as row}}<li>{{row.name}}|{{{row.raw}}}</li>{{/each}}</ul>{{/if}}"");
            var model = {
                ""show"": true,
                ""rows"": [
                    {""name"": ""A < B"", ""raw"": ""<b>x</b>""},
                    {""name"": ""C & D"", ""raw"": ""<i>y</i>""}
                ]
            };
            print(ui.template(path, model));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("<ul><li>A &lt; B|<b>x</b></li><li>C &amp; D|<i>y</i></li></ul>", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_UiCrudBuiltIns_Work()
    {
        var source = @"
            writeFile(""entity_controls_transpiled_smoke.html"", ""<section><button>{{openAddLabel}}</button>{{#each filters as filter}}{{#if filter.isInput}}<input value='{{filter.value}}'>{{/if}}{{#if filter.isSelect}}{{#each filter.options as option}}<option{{{option.selectedAttr}}}>{{option.label}}</option>{{/each}}{{/if}}{{/each}}{{{addDialogHtml}}}<script>{{{dialogScript}}}</script></section>"");
            writeFile(""customer_option.html"", ""<option value='{{id}}'>{{name}}</option>"");
            writeFile(""ticket_add_dialog_transpiled.html"", ""<dialog>{{{customerOptions}}}</dialog>"");
            writeFile(""ticket_edit_dialog_transpiled.html"", ""<dialog>Edit</dialog>"");
            writeFile(""ticket_dialog_transpiled.js"", ""init();"");

            var schema = {
                ""sessionDefault"": ""crm-tickets-ui"",
                ""templateBasePath"": ""."",
                ""controlsTemplatePath"": ""entity_controls_transpiled_smoke.html"",
                ""openAddLabel"": ""Add ticket"",
                ""openEditLabel"": ""Edit selected ticket"",
                ""openAddButtonId"": ""openA"",
                ""openEditButtonId"": ""openE"",
                ""listAction"": ""/tickets"",
                ""filterGridColumns"": ""2fr 1fr auto"",
                ""addDialogTemplate"": ""ticket_add_dialog_transpiled.html"",
                ""editDialogTemplate"": ""ticket_edit_dialog_transpiled.html"",
                ""dialogScriptTemplate"": ""ticket_dialog_transpiled.js"",
                ""filterDefs"": [
                    {""kind"": ""input"", ""name"": ""search"", ""placeholder"": ""Search"", ""defaultValue"": """"},
                    {""kind"": ""select"", ""name"": ""status"", ""defaultValue"": """", ""options"": [{""value"": ""open"", ""label"": ""open""}]}
                ],
                ""dialogLookupOptions"": [
                    {""key"": ""customerOptions"", ""source"": ""customers"", ""renderer"": ""customerOptions"", ""templatePath"": ""customer_option.html"", ""itemName"": ""customer""}
                ]
            };

            var model = ui.crudModel(schema, ""t-session"", {""search"": ""A < B"", ""status"": ""open""}, {""customers"": [{""id"": 1, ""name"": ""Acme""}]});
            print(ui.template(""{{#each filters as filter}}{{#if filter.isInput}}{{filter.value}}{{/if}}{{/each}}"", model));
            print(ui.crudControls(schema, ""t-session"", {""search"": ""A < B"", ""status"": ""open""}, {""customers"": [{""id"": 1, ""name"": ""Acme""}]}));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("A &lt; B", result.StdOut);
        Assert.True(result.StdOut.Contains("<option value='1'>Acme</option>"), result.StdOut);
        Assert.Contains("Add ticket", result.StdOut);
    }

    [Fact]
    public void Transpiled_PhaseBComponentRouteRegistration_AndScopedState_Work()
    {
        var source = @"
            component TicketBoard() {
                return ""<h1>board</h1>"";
            }

            @ACTION(""/tickets/add"")
            function addTicket(body) {
                return componentFragment(""ticket-list"", ""<ul><li>ok</li></ul>"");
            }

            @LIVE(""/tickets/live"")
            function ticketsLive() {
                return {""sse"": true};
            }

            var server = new HttpServer(8123);
            var routes = server.getRoutes();
            var hasComponent = false;
            var hasAction = false;
            var hasLive = false;
            var i = 0;
            while (i < routes.length) {
                var r = routes[i];
                if (r.method == ""GET"" && r.path == ""/components/TicketBoard"") hasComponent = true;
                if (r.method == ""POST"" && r.path == ""/tickets/add"") hasAction = true;
                if (r.method == ""GET"" && r.path == ""/tickets/live"") hasLive = true;
                i = i + 1;
            }
            print(hasComponent);
            print(hasAction);
            print(hasLive);

            componentStateConfigure(8, 8, 60000);
            componentStateSet(""board"", ""count"", 1, ""tA"");
            componentStateSet(""board"", ""count"", 2, ""tB"");
            print(componentStateGet(""board"", ""count"", 0, ""tA""));
            print(componentStateGet(""board"", ""count"", 0, ""tB""));
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("true\ntrue\ntrue\n1\n2", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_ComponentState_DictionaryRoundTrip_PreservesNestedObjects()
    {
        var source = @"
            var dict = dict { ""x"": 1 };
            dict.extra = dict { ""label"": ""ok"" };
            componentStateSet(""board"", ""payload"", dict);

            var restored = componentStateGet(""board"", ""payload"");
            print(restored[""x""]);
            print(restored.missing == null);
            print(restored.extra.label);
            print(componentStateObject(""board"").payload.extra.label);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.True(result.ExitCode == 0, $"ExitCode={result.ExitCode}\nStdErr:\n{result.StdErr}\nStdOut:\n{result.StdOut}");
        Assert.Equal("1\ntrue\nok\nok", result.StdOut.Trim());
    }

    [Fact]
    public void Transpiled_SqliteClient_InMemory_Smoke()
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

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);

        Assert.True(result.ExitCode == 0, $"ExitCode={result.ExitCode}\nStdErr:\n{result.StdErr}\nStdOut:\n{result.StdOut}");
        Assert.Equal("true\nAlice\n30\n1\nAlice", result.StdOut.Trim());
    }
}

