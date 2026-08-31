// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Models;

namespace MaldaLang.DesktopIDE.Services;

/// <summary>
/// Insertable MALDA syntax snippets for the Desktop IDE Syntax Helper panel.
/// Templates use <see cref="CaretMarker"/> for the post-insert caret.
/// </summary>
public static class SyntaxSnippetCatalog
{
    public const string CaretMarker = "__CARET__";

    public static IReadOnlyList<SyntaxSnippet> CreateDefault()
    {
        return new List<SyntaxSnippet>
        {
            Snippet(
                "class",
                "OOP",
                "Class",
                "Define a class with constructor and method.",
                $"class ClassName {{\n\tvar value;\n\n\tfunction ClassName(value) {{\n\t\tthis.value = {CaretMarker}value;\n\t}}\n\n\tfunction methodName() {{\n\t\t\n\t}}\n}}"),
            Snippet(
                "class-primary",
                "OOP",
                "Class (Primary Constructor)",
                "Parameter list after the class name becomes public fields. Do not mix with extends.",
                $"class Point(x, y) {{\n\tfunction total() {{\n\t\treturn this.x + this.y;\n\t}}\n}}\n\nvar p = new Point({CaretMarker}3, 4);"),
            Snippet(
                "class-extends",
                "OOP",
                "Class Extends",
                "Subclass with super() in the constructor.",
                $"class Animal {{\n\tvar name;\n\n\tfunction Animal(name) {{\n\t\tthis.name = name;\n\t}}\n}}\n\nclass Dog extends Animal {{\n\tfunction Dog(name) {{\n\t\tsuper({CaretMarker}name);\n\t}}\n}}"),
            Snippet(
                "function",
                "Declarations",
                "Function",
                "Define a reusable function.",
                $"function functionName(param1, param2) {{\n\t{CaretMarker}\n}}"),
            Snippet(
                "prompt",
                "Prompts",
                "Prompt",
                "Define an AI prompt. Parameters are name-only; prompt bodies interpolate without $.",
                $"prompt greet(name) {{\n\tuser: \"Hello, {{{CaretMarker}name}}!\"\n}}"),
            Snippet(
                "prompt-structured",
                "Prompts",
                "Prompt (Structured / Mode A)",
                "Typed prompt: schema return + await. No tools or gather.",
                "schema Item {\n\tid: int;\n\tlabel: string;\n}\n\nprompt makeItem(label) -> Item {\n\tsystem: \"Return JSON only.\";\n\tuser: \"Make an item labeled {label}.\";\n}\n\nvar item = await makeItem(\"" + CaretMarker + "widget\");"),
            Snippet(
                "prompt-tools",
                "Prompts",
                "Prompt (Tools / Mode B)",
                "One LLM call with tools: plus optional -> Type. Not two rounds.",
                "schema Note {\n\ttopic: string;\n\tsummary: string;\n}\n\nprompt research(topic) -> Note {\n\tsystem: \"Research assistant.\";\n\tuser: \"Investigate: {topic}\";\n\ttools: [\"read_file\", \"grep\"];\n}\n\nvar inst = research(\"" + CaretMarker + "topic\");"),
            Snippet(
                "prompt-gather",
                "Prompts",
                "Prompt (Gather / Mode C)",
                "gather: then a typed extract. Cannot combine with tools:. Requires -> Type.",
                "schema Answer {\n\tsummary: string;\n\tsources: string[];\n}\n\nprompt research(question) -> Answer {\n\tgather: [\"read_file\", \"grep\"];\n\tsystem: \"Use tools, then extract a structured answer.\";\n\tuser: question;\n}\n\nvar inst = research(\"" + CaretMarker + "What is Mode C?\");"),
            Snippet(
                "within-budget",
                "Prompts",
                "@within + @budget",
                "Wall-clock bound plus token/tool budget on a prompt.",
                "schema Answer {\n\ttext: string;\n}\n\n@within(5000)\n@budget(tokens: 4000, tools: 8)\nprompt answer(q) -> Answer {\n\tuser: \"Question: {q}\"\n}\n\nvar inst = answer(\"" + CaretMarker + "What is @budget?\");"),
            Snippet(
                "if",
                "Statements",
                "If",
                "Conditional block.",
                $"if (condition) {{\n\t{CaretMarker}\n}}"),
            Snippet(
                "if-else",
                "Statements",
                "If / Else",
                "Two-way conditional block.",
                $"if (condition) {{\n\t{CaretMarker}\n}} else {{\n\t\n}}"),
            Snippet(
                "while",
                "Loops",
                "While Loop",
                "Repeat while condition is true.",
                $"while (condition) {{\n\t{CaretMarker}\n}}"),
            Snippet(
                "for-in",
                "Loops",
                "For-In Loop",
                "Iterate items in a collection.",
                $"for (var item in collection) {{\n\t{CaretMarker}\n}}"),
            Snippet(
                "foreach",
                "Loops",
                "Foreach Loop",
                "Alternative foreach syntax over a collection.",
                $"foreach (var item in collection) {{\n\t{CaretMarker}\n}}"),
            Snippet(
                "lambda-expression",
                "Functional",
                "Lambda Expression",
                "Inline function with expression body.",
                $"var transform = (x) => {CaretMarker}x;"),
            Snippet(
                "lambda-block",
                "Functional",
                "Lambda Block",
                "Inline function with block body.",
                $"var transform = (x) => {{\n\t{CaretMarker}\n}};"),
            Snippet(
                "var-declaration",
                "Basics",
                "Variable Declaration",
                "Declare and initialize a variable.",
                $"var variableName = {CaretMarker}value;"),
            Snippet(
                "const",
                "Basics",
                "Const",
                "Declare an immutable binding.",
                $"const limit = {CaretMarker}42;"),
            Snippet(
                "null-safe",
                "Basics",
                "Null-Conditional / Coalesce",
                "obj?.field and a ?? b (null only; keeps 0 / false / \"\").",
                $"var label = response?.content ?? {CaretMarker}\"\";"),
            Snippet(
                "new-instance",
                "OOP",
                "Create Object",
                "Instantiate a class with constructor args.",
                $"var instance = new ClassName({CaretMarker}value);"),
            Snippet(
                "new-instance-no-args",
                "OOP",
                "Create Object (No Args)",
                "Instantiate a class with a parameterless constructor.",
                $"var instance = new {CaretMarker}ClassName();"),
            Snippet(
                "method-call",
                "OOP",
                "Method Call",
                "Call an instance method.",
                $"var result = instance.{CaretMarker}methodName();"),
            Snippet(
                "field-access",
                "OOP",
                "Field Access",
                "Read or write an instance field.",
                $"instance.{CaretMarker}value = 42;"),
            Snippet(
                "try-catch",
                "Statements",
                "Try / Catch",
                "Handle runtime errors.",
                $"try {{\n\t{CaretMarker}\n}} catch (err) {{\n\tio.print(err);\n}}"),
            Snippet(
                "try-finally",
                "Statements",
                "Try / Catch / Finally",
                "Handle errors and always run cleanup.",
                $"try {{\n\t{CaretMarker}\n}} catch (err) {{\n\tio.print(err);\n}} finally {{\n\tio.print(\"cleanup\");\n}}"),
            Snippet(
                "throw",
                "Statements",
                "Throw",
                "Raise a runtime error.",
                $"throw {CaretMarker}\"something went wrong\";"),
            Snippet(
                "defer",
                "Statements",
                "Defer",
                "Run a block when the current function returns (LIFO).",
                $"function run() {{\n\tdefer {{ io.print(\"cleanup\"); }}\n\t{CaretMarker}\n}}\nrun();"),
            Snippet(
                "for-classic",
                "Loops",
                "For Loop (Classic)",
                "Traditional counter-based loop.",
                $"for (var i = 0; i < count; i = i + 1) {{\n\t{CaretMarker}\n}}"),
            Snippet(
                "break",
                "Statements",
                "Break",
                "Exit current loop.",
                $"{CaretMarker}break;"),
            Snippet(
                "continue",
                "Statements",
                "Continue",
                "Skip to next loop iteration.",
                $"{CaretMarker}continue;"),
            Snippet(
                "return",
                "Statements",
                "Return",
                "Return from function.",
                $"return {CaretMarker}value;"),
            Snippet(
                "array-literal",
                "Basics",
                "Array",
                "Create an array literal.",
                $"var items = [{CaretMarker}1, 2, 3];"),
            Snippet(
                "object-literal",
                "Basics",
                "Object",
                "Create an object literal.",
                $"var obj = {{\n\t\"name\": {CaretMarker}\"value\"\n}};"),
            Snippet(
                "dict",
                "Collections",
                "Dict Literal",
                "Create a dict with dict { ... }.",
                $"var bag = dict {{ \"name\": {CaretMarker}\"Ada\", \"age\": 36 }};"),
            Snippet(
                "graph",
                "Collections",
                "Graph",
                "Directed or undirected graph literal.",
                $"var g = graph directed {{\n\tnodes: [\"A\", \"B\", \"C\"],\n\tedges: [\n\t\t{{ from: \"A\", to: \"B\", weight: 5 }},\n\t\t{{ from: \"B\", to: \"C\", weight: 3 }}\n\t]\n}};\nio.print(g.nodeCount());{CaretMarker}"),
            Snippet(
                "string-interpolation",
                "Strings",
                "String Interpolation",
                "Interpolate variables with $\"...{expr}...\".",
                "var name = \"Alice\";\nvar total = 42;\nvar message = $\"Hello {name}, total={total}\";\nio.print(" + CaretMarker + "message);"),
            Snippet(
                "multiline-string",
                "Strings",
                "Multiline String",
                "Triple-quoted multiline string literal.",
                "var text = \"\"\"\n" + CaretMarker + "Line 1\nLine 2\n\"\"\";\nio.print(text);"),
            Snippet(
                "multiline-interpolated-string",
                "Strings",
                "Multiline Interpolated String",
                "Interpolated triple-quoted string: $\"\"\"...\"\"\".",
                "var name = \"MALDA\";\nvar greeting = $\"\"\"\nHello {name}!\n" + CaretMarker + "Welcome to multiline interpolation.\n\"\"\";\nio.print(greeting);"),
            Snippet(
                "match-value",
                "Pattern Matching",
                "Match Value",
                "Match a scalar with case / default.",
                "var status = 404;\nvar label = match status {\n\tcase 200: \"ok\";\n\tcase 404: \"not found\";\n\tdefault: \"other\";\n};\nio.print(" + CaretMarker + "label);"),
            Snippet(
                "match-guard",
                "Pattern Matching",
                "Match With Guard",
                "case x if condition: — failed guards try the next arm.",
                "var n = 3;\nvar result = match n {\n\tcase x if x > 10: \"big\";\n\tcase x: \"small\";\n};\nio.print(" + CaretMarker + "result);"),
            Snippet(
                "match-array-rest-pattern",
                "Pattern Matching",
                "Match Array + Rest Pattern",
                "Advanced match with nested object pattern and ...rest.",
                "var data = [{ type: \"A\", value: 1 }, { type: \"B\", value: 2 }];\nvar result = match data {\n\tcase [{ type: \"A\", value: v }, ...rest]: \"first=\" + v;\n\tdefault: \"none\";\n};\nio.print(" + CaretMarker + "result);"),
            Snippet(
                "match-object-pattern",
                "Pattern Matching",
                "Match Object Pattern",
                "Advanced match with object shorthand and wildcard.",
                "var profile = { name: \"Alice\", age: 30, city: \"Rome\" };\nvar result = match profile {\n\tcase { name, age }: name + \" is \" + age;\n\tcase { name }: name;\n\tcase _: \"unknown\";\n};\nio.print(" + CaretMarker + "result);"),
            Snippet(
                "match-variant-pattern",
                "Pattern Matching",
                "Match Variant Pattern",
                "Sum-type variant matching with constructor patterns.",
                "type Result = Ok(value) | Err(message);\nvar r = Ok(42);\nvar result = match r {\n\tcase Ok(v): \"ok: \" + v;\n\tcase Err(msg): \"error: \" + msg;\n};\nio.print(" + CaretMarker + "result);"),
            Snippet(
                "schema",
                "Types",
                "Schema",
                "Declare an object schema for validate() and typed prompts.",
                $"schema Item {{\n\tid: int;\n\tlabel: string;\n}}{CaretMarker}"),
            Snippet(
                "type-sum",
                "Types",
                "Sum Type",
                "Variant constructors. Optional payload types; JSON wire uses tag plus fields.",
                $"type Intent = Search(query: string) | Buy(sku: string, qty: int) | Help();{CaretMarker}"),
            Snippet(
                "validate",
                "Types",
                "Validate Schema",
                "validate() returns { ok, data } or { ok: false, error } — it does not throw.",
                "schema Item {\n\tid: int;\n\tlabel: string;\n}\n\nvar candidate = dict { \"id\": 1, \"label\": \"widget\" };\nvar checked = validate(\"Item\", candidate);\nif (checked.ok) {\n\tio.print(checked.data.label);\n} else {\n\tio.print(checked.error);\n}" + CaretMarker),
            Snippet(
                "api",
                "Types",
                "API + runProgram",
                "Closed api plan. Host-only (not JS). Pair with prompt … -> program(Api).",
                "api Calc {\n\tfunction add(a: number, b: number);\n\tfunction mul(a: number, b: number);\n}\n\nfunction add(a, b) { return a + b; }\nfunction mul(a, b) { return a * b; }\n\nvar prog = parseJSON(\"\"\"\n{\"@api\":\"Calc\",\"steps\":[{\"call\":\"add\",\"args\":[2,3],\"as\":\"t0\"}],\"return\":\"$t0\"}\n\"\"\");\nio.print(runProgram(" + CaretMarker + "prog));"),
            Snippet(
                "actor",
                "Actors",
                "Actor",
                "Define an actor with on handlers. Inside on, use flat print (not io.print).",
                $"actor Worker {{\n\ton start() {{\n\t\tprint(\"Worker started\");\n\t}}\n\n\ton compute(value) {{\n\t\t{CaretMarker}reply(value * 2);\n\t}}\n}}"),
            Snippet(
                "spawn",
                "Actors",
                "Spawn Actor",
                "Create a new actor instance.",
                $"var worker = spawn {CaretMarker}Worker();"),
            Snippet(
                "send",
                "Actors",
                "Send Message",
                "Send async message to actor.",
                $"send {CaretMarker}worker.start();"),
            Snippet(
                "send-then-timeout",
                "Actors",
                "Send With Callback + Timeout",
                "Handle actor response with timeout fallback.",
                $"send worker.compute(42) then (result) {{\n\t{CaretMarker}io.print(result);\n}} timeout 500 catch (error) {{\n\tio.print(error);\n}};"),
            Snippet(
                "workflow",
                "Workflows",
                "Workflow + Step",
                "Durable workflow. Put effects inside step. Replay is keyed on the step name.",
                $"function doWork(input) {{\n\treturn input;\n}}\n\nworkflow Job(input) {{\n\tstep result = doWork(input)\n\t\tretry 2 backoff \"fixed\" delay 1;\n\treturn result;\n}}{CaretMarker}"),
            Snippet(
                "workflow-approval",
                "Workflows",
                "Workflow Approval / Wait / Compensate",
                "Human approval, signal wait, and compensate on a later step.",
                "workflow Onboard(input) {\n\tapproval approved = approval(\"manager\", {\"id\": input.id})\n\t\ttimeout 86400000\n\t\tonReject notifyRejected(input.id);\n\n\twait docs = awaitSignal(\"docs_uploaded\", {\"id\": input.id})\n\t\ttimeout 259200000;\n\n\tstep account = createAccount(input)\n\t\tcompensate deleteAccount(account.id);\n\n\treturn account;\n}" + CaretMarker),
            Snippet(
                "using",
                "Declarations",
                "Using",
                "Import a package or namespace.",
                $"using {CaretMarker}Package.Name;"),
            Snippet(
                "include",
                "Modules",
                "Include",
                "Splice another MALDA file into this one (shared globals).",
                $"include {CaretMarker}\"shared.malda\";"),
            Snippet(
                "import",
                "Modules",
                "Import File",
                "Import a module. Only export bindings merge into this file.",
                $"import {CaretMarker}\"helpers.malda\";"),
            Snippet(
                "import-selective",
                "Modules",
                "Import Selective",
                "Merge only named exports. Error if missing or not exported.",
                $"import {{ {CaretMarker}add, VERSION }} from \"helpers.malda\";"),
            Snippet(
                "export",
                "Modules",
                "Export Function",
                "Export a binding. Also use export type / export schema when those should leave the module.",
                $"export function add(a, b) {{\n\treturn a + {CaretMarker}b;\n}}"),
            Snippet(
                "get-route",
                "Web",
                "@GET Route",
                "HTTP GET handler. Decorators attach to function declarations.",
                $"@GET(\"/api/health\")\nfunction health() {{\n\treturn parseJSON(\"{{\\\"ok\\\": true}}\");\n}}{CaretMarker}"),
            Snippet(
                "post-route",
                "Web",
                "@POST Route",
                "HTTP POST handler. Use a parameter named body for the request body.",
                $"@POST(\"/api/items\")\nfunction createItem(body) {{\n\treturn {CaretMarker}body;\n}}"),
            Snippet(
                "page-route",
                "Web",
                "@PAGE Route",
                "Server-rendered HTML page for HttpServer.",
                $"@PAGE(\"/\")\nfunction home() {{\n\treturn {CaretMarker}\"<h1>Hello</h1>\";\n}}"),
            Snippet(
                "component",
                "Web",
                "Component",
                "Server-rendered component that returns HTML.",
                $"component Board() {{\n\treturn {CaretMarker}\"<div>Board</div>\";\n}}"),
            Snippet(
                "ui-tree",
                "Web",
                "UI Tree",
                "Server-driven ui.* tree. Controls take (props, children?, key?) — not HTML strings.",
                $"var tree = ui.column(\n\t{{\"componentId\": \"Root\"}},\n\t[\n\t\tui.heading({{\"value\": \"Hello\"}}),\n\t\tui.button({{\"label\": \"OK\", \"onClick\": \"ok\"}})\n\t]\n);{CaretMarker}"),
            Snippet(
                "client",
                "Web",
                "@client / @server",
                "Compile-time partition. Unmarked top-level calls go to both backends.",
                $"@client()\nfunction renderFrame() {{\n\t{CaretMarker}\n}}\n\n@server()\nfunction handleScore(body) {{\n\treturn body;\n}}"),
            Snippet(
                "shader",
                "Games",
                "@shader Kernel",
                "GLSL kernel for JS compile via glsl.compile. Not callable from host MALDA.",
                $"@shader()\nfunction hitSphere(center: vec3, radius: float, origin: vec3, dir: vec3) -> float {{\n\tvar oc: vec3 = origin - center;\n\treturn {CaretMarker}length(oc) - radius;\n}}"),
            Snippet(
                "game-loop",
                "Games",
                "Game Loop",
                "Canvas plus game.startFixed. Nested functions so JS can close over locals.",
                $"function update(dtMs) {{\n\t{CaretMarker}\n}}\n\nfunction render() {{\n\tgame.clear();\n}}\n\ngame.createCanvas(800, 450, \"#app\");\ngame.startFixed(update, render);"),
            Snippet(
                "tool",
                "Decorators",
                "@Tool",
                "Register a function as an LLM tool.",
                $"@Tool(\"greet\", \"Greets someone by name\")\nfunction greet(name) {{\n\treturn \"Hello, \" + {CaretMarker}name;\n}}"),
            Snippet(
                "pure-effects",
                "Decorators",
                "@pure / @effects",
                "@pure helpers must not do I/O. @effects is a name allow-list, not a path sandbox.",
                $"@pure()\nfunction normalizeName(name) {{\n\treturn str.upper(str.trim(name));\n}}\n\n@effects(\"print\")\nfunction handle(raw) {{\n\tprint(normalizeName({CaretMarker}raw));\n}}"),
            Snippet(
                "cap-read",
                "Decorators",
                "Capability Token",
                "Host-mint a file token. cap.read rejects strings and forged dicts.",
                $"var notes = cap.fileRead(\"{CaretMarker}notes.md\");\nvar text = cap.read(notes);"),
            Snippet(
                "property",
                "Testing",
                "Property Test",
                "Property-based check. Declaring property switches the runner to property mode.",
                $"property intIdentity(x) {{\n\treturn (x + 0) == {CaretMarker}x;\n}}"),
            Snippet(
                "async-await",
                "Statements",
                "Async / Await",
                "async call() starts a task; await waits for it. Also used with typed prompts.",
                $"function compute() {{\n\treturn 99;\n}}\nvar t = async compute();\nvar v = await t;{CaretMarker}"),
            Snippet(
                "comment-block",
                "Utilities",
                "Comment Block",
                "Insert a multi-line comment block.",
                $"/*\n\t{CaretMarker}Notes\n*/"),
            Snippet(
                "print",
                "Utilities",
                "Print",
                "Print a value. Prefer io.print; use flat print inside actor on handlers.",
                $"io.print({CaretMarker}\"text\");"),
            Snippet(
                "input",
                "Utilities",
                "Input",
                "Read a line. Empty string is EOF — not null. Treat \"\" as quit in loops.",
                $"var name = io.input(\"{CaretMarker}Name: \");\nif (str.trim(name) == \"\") {{\n\t\n}}"),
            Snippet(
                "sleep",
                "Utilities",
                "Sleep",
                "Pause execution for milliseconds.",
                $"sleep({CaretMarker}100);")
        };
    }

    private static SyntaxSnippet Snippet(string id, string category, string label, string description, string templateText)
    {
        return new SyntaxSnippet
        {
            Id = id,
            Category = category,
            Label = label,
            Description = description,
            TemplateText = templateText,
            Preview = templateText.Replace(CaretMarker, "", StringComparison.Ordinal)
        };
    }
}
