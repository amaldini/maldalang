// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class UiFrameworkTests : TestBase
{
    [Fact]
    public void Interpreter_UiMountAndRender_ReturnsProtocolEnvelope()
    {
        var source = @"
            var root1 = ui.column({""className"": ""page""}, [
                ui.heading({""value"": ""Dashboard""}),
                ui.button({""label"": ""Save""})
            ]);
            var mounted = ui.mount(root1, ""sessionA"");
            print(mounted.type);
            print(mounted.sessionId);

            var root2 = ui.column({""className"": ""page changed""}, [
                ui.heading({""value"": ""Dashboard""}),
                ui.button({""label"": ""Save""})
            ]);
            var patched = ui.render(root2, ""sessionA"");
            print(patched.type);
        ";

        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("mount", lines[0]);
        Assert.Equal("sessionA", lines[1]);
        Assert.Equal("patch", lines[2]);
    }

    [Fact]
    public void Interpreter_UiMountAndRender_WithStyleProp_ReturnsProtocolEnvelope()
    {
        var source = @"
            var root1 = ui.panel({""className"": ""card"", ""style"": ""padding:8px; color:red;""}, [
                ui.text({""value"": ""Styled""})
            ]);
            var mounted = ui.mount(root1, ""styleSession"");
            print(mounted.type);

            var root2 = ui.panel({""className"": ""card"", ""style"": {""padding"": ""12px"", ""color"": ""blue"", ""backgroundColor"": ""#f5f7fb""}}, [
                ui.text({""value"": ""Styled""})
            ]);
            var patched = ui.render(root2, ""styleSession"");
            print(patched.type);
        ";

        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("mount", lines[0]);
        Assert.Equal("patch", lines[1]);
    }

    [Fact]
    public void Interpreter_UiState_InitializesAndUpdates()
    {
        var source = @"
            print(ui.state(""Counter"", ""value"", 10, ""tenant01""));
            print(ui.state(""Counter"", ""value"", 999, ""tenant01""));
            ui.setState(""Counter"", ""value"", 25, ""tenant01"");
            print(componentStateGet(""Counter"", ""value"", 0, ""tenant01""));
        ";

        var output = RunProgram(source);
        var lines = output.Split('\n');
        Assert.Equal("10", lines[0]);
        Assert.Equal("10", lines[1]);
        Assert.Equal("25", lines[2]);
    }

    [Fact]
    public void Transpiled_UiCalls_WorkInCompiledMode()
    {
        var source = @"
            var root = ui.row({""id"": ""main""}, [ui.text({""value"": ""ok""})]);
            var mounted = ui.mount(root, ""compiled"");
            print(mounted.type);

            var rerender = ui.render(root, ""compiled"");
            print(rerender.type);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("mount", result.StdOut);
        Assert.Contains("patch", result.StdOut);
    }

    [Fact]
    public void Interpreter_UiLifecycleAndResync_AreAvailable()
    {
        var source = @"
            ui.onMount(""CounterRoot"", ""sessionLifecycle"");
            ui.onUpdate(""CounterRoot"", ""sessionLifecycle"");
            ui.onUnmount(""CounterRoot"", ""sessionLifecycle"");

            var first = ui.column({""componentId"": ""CounterRoot""}, [ui.text({""value"": ""v1""})]);
            var m = ui.mount(first, ""sessionLifecycle"");
            print(m.type);

            var second = ui.column({""componentId"": ""CounterRoot""}, [ui.text({""value"": ""v2""})]);
            var p = ui.render(second, ""sessionLifecycle"");
            print(p.type);

            var e = ui.pullEvent(""sessionLifecycle"");
            print(e != null);

            var rs = ui.resync(""sessionLifecycle"");
            print(rs.type);
            var snap = ui.snapshot(""sessionLifecycle"");
            print(snap.version);
        ";

        var output = RunProgram(source);
        Assert.Contains("mount", output);
        Assert.Contains("patch", output);
        Assert.Contains("true", output);
        Assert.Contains("resync", output);
        Assert.Contains("1.0", output);
    }

    [Fact]
    public void Interpreter_NewUiLifecycleHooks_AreAvailable()
    {
        var source = @"
            ui.onInit(""CounterRoot"", ""sessionLifecycle2"");
            ui.onPreRender(""CounterRoot"", ""sessionLifecycle2"");
            ui.onLoad(""CounterRoot"", ""sessionLifecycle2"");
            ui.onDispose(""CounterRoot"", ""sessionLifecycle2"");
            ui.onMount(""CounterRoot"", ""sessionLifecycle2"");
            ui.onUpdate(""CounterRoot"", ""sessionLifecycle2"");
            ui.onUnmount(""CounterRoot"", ""sessionLifecycle2"");

            var first = ui.column({""componentId"": ""CounterRoot""}, [ui.text({""value"": ""v1""})]);
            ui.mount(first, ""sessionLifecycle2"");

            var second = ui.column({""componentId"": ""CounterRoot""}, [ui.text({""value"": ""v2""})]);
            ui.render(second, ""sessionLifecycle2"");

            var third = ui.column({""componentId"": ""OtherRoot""}, [ui.text({""value"": ""v3""})]);
            ui.render(third, ""sessionLifecycle2"");

            var i = 0;
            while (i < 20) {
                var evt = ui.pullEvent(""sessionLifecycle2"");
                if (evt == null) {
                    break;
                }
                if (evt.type == ""lifecycle"" && evt.payload.componentId == ""CounterRoot"") {
                    print(evt.payload.eventName);
                }
                i = i + 1;
            }
        ";

        var output = RunProgram(source);
        Assert.Contains("onInit", output);
        Assert.Contains("onMount", output);
        Assert.Contains("onPreRender", output);
        Assert.Contains("onUpdate", output);
        Assert.Contains("onUnmount", output);
        Assert.Contains("onDispose", output);
    }

    [Fact]
    public void Interpreter_UiOnError_IsEmittedForErrorEvents()
    {
        var source = @"
            ui.onError(""CounterRoot"", ""sessionErrorLifecycle"");
            ui.dispatchEvent({
                ""type"": ""error"",
                ""targetPath"": ""/components/CounterRoot"",
                ""payload"": {""componentId"": ""CounterRoot"", ""code"": ""RenderFailure"", ""message"": ""boom""}
            }, ""sessionErrorLifecycle"");

            var found = false;
            var i = 0;
            while (i < 10) {
                var evt = ui.pullEvent(""sessionErrorLifecycle"");
                if (evt == null) {
                    break;
                }
                if (evt.type == ""lifecycle"" && evt.payload.eventName == ""onError"") {
                    print(evt.payload.payload.code);
                    print(evt.payload.payload.message);
                    found = true;
                    break;
                }
                i = i + 1;
            }
            print(found);
        ";

        var output = RunProgram(source);
        Assert.Contains("RenderFailure", output);
        Assert.Contains("boom", output);
        Assert.Contains("true", output);
    }

    [Fact]
    public void Transpiled_UiProtocolHelpers_WorkInCompiledMode()
    {
        var source = @"
            var root = ui.column({""componentId"": ""Root""}, [ui.text({""value"": ""ok""})]);
            ui.mount(root, ""compiled2"");
            ui.configure(""maxPatchCount"", 100, ""compiled2"");
            var snap = ui.snapshot(""compiled2"");
            print(snap.version);
            var rs = ui.resync(""compiled2"");
            print(rs.type);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1.0", result.StdOut);
        Assert.Contains("resync", result.StdOut);
    }

    [Fact]
    public void Interpreter_UiCompositionAndCanonicalControls_Work()
    {
        var source = @"
            var show = true;
            var n1 = ui.when(show, ui.text({""value"": ""shown""}), ui.text({""value"": ""hidden""}));
            print(n1.type);

            var picked = ui.choose(""a"", {""a"": ui.badge({""value"": ""A""}), ""b"": ui.badge({""value"": ""B""})}, ui.badge({""value"": ""D""}));
            print(picked.type);

            var eachItems = ui.each([1, 2, 3], ""value"");
            print(eachItems.length);

            var control1 = ui.textField({""name"": ""email"", ""value"": ""a@b.com""});
            var control2 = ui.dataGrid({
                ""className"": ""g"",
                ""columns"": [{""key"": ""name"", ""title"": ""Name"", ""sortable"": true}],
                ""rows"": [{""id"": 1, ""name"": ""Alice""}],
                ""selectionMode"": ""single"",
                ""onSort"": ""sortHandler"",
                ""onSelectionChange"": ""selectionHandler"",
                ""virtualize"": true,
                ""rowHeight"": 32,
                ""overscan"": 4
            });
            var controlTree = ui.treeView({
                ""nodes"": [
                    {""id"": ""root"", ""label"": ""Root"", ""children"": [{""id"": ""child"", ""label"": ""Child""}]}
                ],
                ""expandedKeys"": [""root""],
                ""onNodeToggle"": ""toggleHandler"",
                ""onNodeSelect"": ""selectHandler""
            });
            var control3 = ui.switch({""checked"": true});
            print(control1.type);
            print(control2.type);
            print(controlTree.type);
            print(control3.type);
        ";

        var output = RunProgram(source);
        Assert.Contains("text", output);
        Assert.Contains("badge", output);
        Assert.Contains("\n3\n", "\n" + output + "\n");
        Assert.Contains("textField", output);
        Assert.Contains("dataGrid", output);
        Assert.Contains("treeView", output);
        Assert.Contains("switch", output);
    }

    [Fact]
    public void Transpiled_UiTreeViewAndDataGrid_WorkInCompiledMode()
    {
        var source = @"
            var tree = ui.treeView({
                ""nodes"": [
                    {""id"": ""root"", ""label"": ""Root"", ""children"": [{""id"": ""child"", ""label"": ""Child""}]}
                ],
                ""expandedKeys"": [""root""],
                ""onNodeSelect"": ""onSelect""
            });
            var grid = ui.dataGrid({
                ""columns"": [{""key"": ""name"", ""title"": ""Name""}],
                ""rows"": [{""id"": 1, ""name"": ""Andrea""}],
                ""onRowClick"": ""onRowClick"",
                ""onSelectionChange"": ""onSelection""
            });
            print(tree.type);
            print(grid.type);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("treeView", result.StdOut);
        Assert.Contains("dataGrid", result.StdOut);
    }

    [Fact]
    public void Interpreter_UiTemplateHelpers_RenderExternalTemplates()
    {
        var source = @"
            var templatePath = ""ui_framework_template_test.html"";
            var listPath = ""ui_framework_list_test.html"";
            var layoutPath = ""ui_framework_layout_test.html"";

            writeFile(templatePath, ""<span>{{value}}</span>"");
            writeFile(listPath, ""<li>{{name}}-{{index}}</li>"");
            writeFile(layoutPath, ""<section>{{slot:body}}</section>"");

            print(ui.template(templatePath, {""value"": ""ok""}));
            print(ui.partial(templatePath, {""value"": ""partial""}));
            print(ui.renderList([{""name"": ""A""}, {""name"": ""B""}], listPath, ""row""));
            print(ui.layout(layoutPath, {""body"": ""<p>slot</p>""}));
        ";

        var output = RunProgram(source);
        Assert.Contains("<span>ok</span>", output);
        Assert.Contains("<span>partial</span>", output);
        Assert.Contains("<li>A-0</li><li>B-1</li>", output);
        Assert.Contains("<section><p>slot</p></section>", output);
    }

    [Fact]
    public void Interpreter_CrmSharedEntityControlsTemplate_Renders()
    {
        var source = @"
            var templatePath = ""entity_controls_smoke.html"";
            writeFile(templatePath, ""<section><button>{{openAddLabel}}</button>{{#each filters as filter}}{{#if filter.isInput}}<input name='{{filter.name}}' value='{{filter.value}}'>{{/if}}{{#if filter.isSelect}}<select name='{{filter.name}}'>{{#each filter.options as option}}<option value='{{option.value}}'{{{option.selectedAttr}}}>{{option.label}}</option>{{/each}}</select>{{/if}}{{/each}}{{{addDialogHtml}}}</section>"");
            var html = ui.template(templatePath, {
                ""sessionId"": ""s1"",
                ""entityPluralLower"": ""tickets"",
                ""entitySingularLower"": ""ticket"",
                ""listAction"": ""/web/crm/tickets"",
                ""filterGridColumns"": ""2fr 1fr auto"",
                ""filters"": [
                    {""isInput"": true, ""isSelect"": false, ""name"": ""search"", ""placeholder"": ""Search"", ""value"": ""abc""},
                    {""isInput"": false, ""isSelect"": true, ""name"": ""sort"", ""options"": [{""value"": ""id_desc"", ""label"": ""Newest"", ""selectedAttr"": "" selected""}]}
                ],
                ""openAddButtonId"": ""openA"",
                ""openEditButtonId"": ""openE"",
                ""openAddLabel"": ""Add ticket"",
                ""openEditLabel"": ""Edit selected ticket"",
                ""addDialogHtml"": ""<dialog id='d1'></dialog>"",
                ""editDialogHtml"": ""<dialog id='d2'></dialog>"",
                ""dialogScript"": ""console.log('x');""
            });
            print(html);
        ";

        var output = RunProgram(source);
        Assert.Contains("Add ticket", output);
        Assert.Contains("<dialog id='d1'></dialog>", output);
        Assert.Contains("selected", output);
    }

    [Fact]
    public void Interpreter_CrmSharedEntityControlsTemplate_StaysConsistentAcrossEntities()
    {
        var source = @"
            writeFile(""entity_controls_dual_smoke.html"", ""<section><button>{{openAddLabel}}</button>{{#each filters as filter}}{{#if filter.isInput}}<input name='{{filter.name}}' value='{{filter.value}}'>{{/if}}{{#if filter.isSelect}}<select name='{{filter.name}}'>{{#each filter.options as option}}<option value='{{option.value}}'{{{option.selectedAttr}}}>{{option.label}}</option>{{/each}}</select>{{/if}}{{/each}}{{{addDialogHtml}}}<script>{{{dialogScript}}}</script></section>"");
            writeFile(""ticket_add_dialog_smoke.html"", ""<dialog id='ticket'></dialog>"");
            writeFile(""ticket_edit_dialog_smoke.html"", ""<dialog id='ticket-edit'></dialog>"");
            writeFile(""ticket_dialog_smoke.js"", ""initTicketDialog();"");
            writeFile(""customer_add_dialog_smoke.html"", ""<dialog id='customer'></dialog>"");
            writeFile(""customer_edit_dialog_smoke.html"", ""<dialog id='customer-edit'></dialog>"");
            writeFile(""customer_dialog_smoke.js"", ""initCustomerDialog();"");

            var ticketSchema = {
                ""openAddLabel"": ""Add ticket"",
                ""openEditLabel"": ""Edit selected ticket"",
                ""openAddButtonId"": ""openA"",
                ""openEditButtonId"": ""openE"",
                ""listAction"": ""/tickets"",
                ""filterGridColumns"": ""2fr 1fr auto"",
                ""templateBasePath"": ""."",
                ""controlsTemplatePath"": ""entity_controls_dual_smoke.html"",
                ""addDialogTemplate"": ""ticket_add_dialog_smoke.html"",
                ""editDialogTemplate"": ""ticket_edit_dialog_smoke.html"",
                ""dialogScriptTemplate"": ""ticket_dialog_smoke.js"",
                ""filterDefs"": [
                    {""kind"": ""input"", ""name"": ""search"", ""placeholder"": ""Search"", ""defaultValue"": """"},
                    {""kind"": ""select"", ""name"": ""status"", ""defaultValue"": ""open"", ""options"": [{""value"": ""open"", ""label"": ""open""}]}
                ],
                ""dialogLookupOptions"": []
            };

            var customerSchema = {
                ""openAddLabel"": ""Add customer"",
                ""openEditLabel"": ""Edit selected customer"",
                ""openAddButtonId"": ""openCA"",
                ""openEditButtonId"": ""openCE"",
                ""listAction"": ""/customers"",
                ""filterGridColumns"": ""2fr 1fr auto"",
                ""templateBasePath"": ""."",
                ""controlsTemplatePath"": ""entity_controls_dual_smoke.html"",
                ""addDialogTemplate"": ""customer_add_dialog_smoke.html"",
                ""editDialogTemplate"": ""customer_edit_dialog_smoke.html"",
                ""dialogScriptTemplate"": ""customer_dialog_smoke.js"",
                ""filterDefs"": [
                    {""kind"": ""input"", ""name"": ""search"", ""placeholder"": ""Search"", ""defaultValue"": """"},
                    {""kind"": ""select"", ""name"": ""sort"", ""defaultValue"": ""name_asc"", ""options"": [{""value"": ""name_asc"", ""label"": ""Name A-Z""}]}
                ],
                ""dialogLookupOptions"": []
            };

            var ticketsHtml = ui.crudControls(ticketSchema, ""t-session"", {""search"": ""alpha"", ""status"": ""open""}, {});
            var customersHtml = ui.crudControls(customerSchema, ""c-session"", {""search"": ""beta"", ""sort"": ""name_asc""}, {});

            print(ticketsHtml);
            print(customersHtml);
        ";

        var output = RunProgram(source);
        Assert.Contains("Add ticket", output);
        Assert.Contains("Add customer", output);
        Assert.Contains("<dialog id='ticket'></dialog>", output);
        Assert.Contains("<dialog id='customer'></dialog>", output);
        Assert.Contains("selected", output);
    }

    [Fact]
    public void Interpreter_UiCrudSchema_DefaultsAndPreserves()
    {
        var source = @"
            var normalized = ui.crudSchema({
                ""entitySingularLower"": ""ticket"",
                ""openAddLabel"": ""Create ticket""
            }, {
                ""templateBasePath"": ""/tmp/templates"",
                ""controlsTemplatePath"": ""/tmp/templates/entity_controls.html""
            });
            print(normalized.templateBasePath);
            print(normalized.controlsTemplatePath);
            print(normalized.openAddLabel);
            print(normalized.openEditLabel);
            print(normalized.filterDefs.length);
            print(normalized.dialogLookupOptions.length);
        ";

        var output = RunProgram(source);
        Assert.Contains("/tmp/templates", output);
        Assert.Contains("/tmp/templates/entity_controls.html", output);
        Assert.Contains("Create ticket", output);
        Assert.Contains("Edit selected ticket", output);
        Assert.Contains("\n0\n", "\n" + output + "\n");
    }

    [Fact]
    public void Transpiled_UiMountEnvelopeAndCrudSchema_WorkInCompiledMode()
    {
        var source = @"
            var schema = ui.crudSchema({""entitySingularLower"": ""ticket""});
            print(schema.openAddLabel);
            var root = ui.column({""componentId"": ""Root""}, [ui.text({""value"": ""ok""})]);
            var envelope = ui.mountEnvelope(root, ""compiled-mount"");
            print(envelope.status);
            print(envelope.mount.type);
            print(envelope.snapshot.version);
            print(envelope.resync.type);
        ";

        var result = TranspiledTestRunner.CompileAndRunFromSource(source);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Add ticket", result.StdOut);
        Assert.Contains("200", result.StdOut);
        Assert.Contains("mount", result.StdOut);
        Assert.Contains("1.0", result.StdOut);
        Assert.Contains("resync", result.StdOut);
    }

    [Fact]
    public void Interpreter_DispatchEvent_ThreeArgFormUsesNamedSession()
    {
        var source = @"
            var sid = ""named-dispatch"";
            var tree = ui.column({""componentId"": ""Root""}, [
                ui.button({""label"": ""Inc"", ""onClick"": ""inc""})
            ]);
            ui.mount(tree, sid);
            while (ui.pullEvent(sid) != null) { }

            ui.dispatchEvent({
                ""type"": ""click"",
                ""targetPath"": ""/0/"",
                ""payload"": {""action"": ""inc""}
            }, sid, 1);

            var named = ui.pullEvent(sid);
            var leftover = ui.pullEvent(""default"");
            print(named != null);
            print(named.payload.action);
            print(leftover == null);
        ";

        var output = RunProgram(source);
        Assert.Contains("true", output);
        Assert.Contains("inc", output);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("true", lines[0]);
        Assert.Equal("inc", lines[1]);
        Assert.Equal("true", lines[2]);
    }
}
