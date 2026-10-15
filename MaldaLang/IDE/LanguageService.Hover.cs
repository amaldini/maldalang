// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.BuiltIns;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public partial class LanguageService
{
    public string? GetHoverInformation(
        string source,
        int line,
        int column,
        string? sourceFileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lexer = new Lexer(source, sourceFileName);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            var statements = parser.Parse();
            StoreParsedStatements(source, sourceFileName, statements);
            cancellationToken.ThrowIfCancellationRequested();
            
            // Find symbol at position
            var token = FindTokenAtPosition(tokens, line + 1, column + 1);
            if (token == null) return null;
            
            // Check if we're hovering over a decorator
            var decoratorInfo = GetDecoratorHoverInfo(source, line, column, token);
            if (decoratorInfo != null)
            {
                return decoratorInfo;
            }
            
            // Keyword hover (e.g. foreach, for, while)
            var keywordInfo = GetKeywordHoverInfo(token, source, line, column, statements);
            if (keywordInfo != null)
            {
                return keywordInfo;
            }

            var neuralHover = TryGetNeuralHover(source, token, statements, line);
            if (neuralHover != null)
            {
                return neuralHover;
            }
            
            // Look up symbol information (local + imported schemas/types when path known)
            return GetSymbolInfo(statements, token.Lexeme, sourceFileName);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
    
    private static bool IsInsidePromptBody(string source, int line, int column)
    {
        var offset = GetSourceOffset(source, line, column);
        if (offset <= 0)
            return false;

        var prefix = source.Substring(0, Math.Min(offset, source.Length));
        var lastPrompt = LastIndexOfPromptKeyword(prefix);
        if (lastPrompt < 0)
            return false;

        var brace = prefix.IndexOf('{', lastPrompt);
        if (brace < 0)
            return false;

        var depth = 0;
        for (var i = brace; i < prefix.Length; i++)
        {
            var ch = prefix[i];
            if (ch == '{')
                depth++;
            else if (ch == '}')
                depth--;
        }

        return depth > 0;
    }

    private static int LastIndexOfPromptKeyword(string prefix)
    {
        for (var i = prefix.Length - 6; i >= 0; i--)
        {
            if (i + 6 > prefix.Length)
                continue;
            if (!prefix.AsSpan(i, 6).Equals("prompt", StringComparison.Ordinal))
                continue;
            var beforeOk = i == 0 || !IsIdentifierContinue(prefix[i - 1]);
            var afterOk = i + 6 >= prefix.Length || !IsIdentifierContinue(prefix[i + 6]);
            if (beforeOk && afterOk)
                return i;
        }

        return -1;
    }

    private static bool IsIdentifierContinue(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private static int GetSourceOffset(string source, int line, int column)
    {
        var currentLine = 0;
        var i = 0;
        while (i < source.Length && currentLine < line)
        {
            if (source[i] == '\n')
                currentLine++;
            i++;
        }

        return Math.Min(i + Math.Max(column, 0), source.Length);
    }

    private Token? FindTokenAtPosition(List<Token> tokens, int line, int column)
    {
        return tokens.FirstOrDefault(t => t.Line == line && t.Column <= column &&
            column < t.Column + t.Lexeme.Length);
    }
    
    private string? TryGetNeuralHover(string source, Token token, List<Statement> statements, int line)
    {
        if (token.Type != TokenType.Identifier || string.IsNullOrEmpty(token.Lexeme))
            return null;

        var receiver = ReceiverBeforeToken(source, token);
        if (receiver == StdLibNamespaces.NnModule)
            return NeuralCompletionCatalog.TryGetHover(StdLibNamespaces.NnModule, token.Lexeme);

        if (!string.IsNullOrEmpty(receiver))
        {
            var memberHover = NeuralCompletionCatalog.TryGetHover(
                FindVariableType(statements, receiver, line),
                token.Lexeme);
            if (memberHover != null)
                return memberHover;
        }

        if (DeclaresSymbol(statements, token.Lexeme))
            return null;

        if (token.Lexeme == StdLibNamespaces.NnModule)
            return NeuralCompletionCatalog.ModuleHover;

        return NeuralCompletionCatalog.TryGetClassHover(token.Lexeme);
    }

    private static string? ReceiverBeforeToken(string source, Token token)
    {
        var lines = source.Split('\n');
        var lineIndex = token.Line - 1;
        if (lineIndex < 0 || lineIndex >= lines.Length)
            return null;

        var lineText = lines[lineIndex].TrimEnd('\r');
        var nameStart = token.Column - 1;
        if (nameStart < 0 || nameStart > lineText.Length)
            return null;

        return ReadReceiverBefore(lineText, nameStart);
    }

    private static bool DeclaresSymbol(List<Statement> statements, string name)
    {
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclaration function && function.Name == name)
                return true;
            if (stmt is ClassDeclaration type && type.Name == name)
                return true;
            if (stmt is PromptDeclaration prompt && prompt.Name == name)
                return true;
            if (stmt is VarDeclStatement variable && variable.Name == name)
                return true;
            if (stmt is WorkflowDeclaration workflow && workflow.Name == name)
                return true;
            if (stmt is SchemaDeclaration schema && schema.Name == name)
                return true;
            if (stmt is TypeDeclaration sumType && sumType.TypeName == name)
                return true;
        }

        return false;
    }

    private static string? GetKeywordHoverInfo(Token token, string source, int line, int column, List<Statement>? statements = null)
    {
        if (token.Type == TokenType.Identifier &&
            string.Equals(token.Lexeme, "gather", StringComparison.Ordinal))
        {
            return "**gather** — Mode C marker: tool round, then a typed extract (`-> Type`) without tools on `await`.\n\n`prompt p(q) -> Answer { gather: [\"read_file\"]; user: q; }`\n\nDo not combine with `tools:` (that stays Mode B).";
        }

        return token.Type switch
        {
            TokenType.Await => "**await** — Await async results, including pipe steps with `runPrompt`.\n\n`var text = await (prompt() |> runPrompt(client));`",
            TokenType.Foreach => "**foreach** — Iterate over each element in an array.\n\n`foreach (var item in collection) { ... }`\n\nSame as `for (var item in collection)`.",
            TokenType.For => "**for** — Loop: traditional (init; condition; increment) or for-in over array.\n\n`for (var i = 0; i < n; i = i + 1) { ... }`\n`for (var x in array) { ... }`",
            TokenType.While => "**while** — Loop while condition is true.\n\n`while (condition) { ... }`",
            TokenType.If => "**if** — Conditional execution.\n\n`if (condition) { ... } else { ... }`",
            TokenType.Workflow => "**workflow** — Durable workflow declaration.\n\n`workflow Name(input) { ... }`",
            TokenType.Prompt => "**prompt** — Reusable LLM prompt template.\n\n`prompt Name(params) -> Type? { gather: [\"read_file\"]; system: \"...\"; user: \"...\" }`\n\n`gather:` + `-> Type` is Mode C (tool round, then typed extract). Plain `tools:` stays Mode B (structured-if-supported: format + appendix with tools; retry without format if the backend rejects it).",
            TokenType.Step => "**step** — Durable step boundary with journaling and replay semantics.\n\n`step stepName = call() retry 2 timeout 1000;`",
            TokenType.Approval => "**approval** — Pause workflow until externally approved/rejected.\n\n`approval gate = approval(\"manager\", payload) timeout 60000;`",
            TokenType.Wait => "**wait/awaitSignal** — Pause workflow until a named signal arrives.\n\n`wait docs = awaitSignal(\"docs_uploaded\", payload) timeout 60000;`",
            TokenType.Retry => "**retry** — Number of retries after first attempt.\n\n`step x = call() retry 3;`",
            TokenType.Backoff => "**backoff** — Retry delay strategy: `fixed`, `linear`, or `exponential`.",
            TokenType.Delay => "**delay** — Base retry delay in milliseconds.",
            TokenType.MaxDelay => "**maxDelay** — Delay cap for linear/exponential backoff in milliseconds.",
            TokenType.Compensate => "**compensate** — Compensation action run if workflow fails after this step.",
            TokenType.OnReject => "**onReject** — Handler expression executed when approval decision is reject.",
            TokenType.Timeout => "**timeout** — Timeout limit in milliseconds for step/approval/signal waits.",
            TokenType.Schema => "**schema** — Declarative object shape for `validate` / structured I/O.\n\n`schema Person { name: string, age?: number }`",
            TokenType.Api => "**api** — Closed callable surface for deterministic programs.\n\n`api Calc { function add(a: number, b: number); }`\n\nOptional parameter types (same `SchemaType` form as schema fields / sum-type payloads) feed program JSON Schema. Name-only stays permissive. Use `prompt p(...) -> program(Calc)` then `runProgram(prog)`. Implementations are top-level `function`s of the same name.",
            _ => null
        };
    }
    
    private string? GetSymbolInfo(
        List<MaldaLang.Parser.AST.Statements.Statement> statements,
        string name,
        string? sourceFileName = null)
    {
        // Check for built-in functions first
        var builtInInfo = GetBuiltInFunctionInfo(name);
        if (builtInInfo != null)
        {
            return builtInInfo;
        }
        
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.FunctionDeclaration funcDecl && 
                funcDecl.Name == name)
            {
                return $"function {funcDecl.Name}({string.Join(", ", funcDecl.Parameters)})";
            }
            if (stmt is MaldaLang.Parser.AST.Declarations.PromptDeclaration promptDecl && 
                promptDecl.Name == name)
            {
                return $"prompt {promptDecl.Name}({string.Join(", ", promptDecl.Parameters)})";
            }
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
            {
                if (classDecl.Name == name)
                    return $"class {classDecl.Name}";

                foreach (var member in classDecl.Members)
                {
                    if (!string.Equals(member.Name, name, StringComparison.Ordinal))
                        continue;
                    if (member.Value is FunctionDeclaration memberFunc)
                        return $"function {memberFunc.Name}({string.Join(", ", memberFunc.Parameters)})";
                    return member.IsStatic ? $"static field {member.Name}" : $"field {member.Name}";
                }
            }
            if (stmt is WorkflowDeclaration workflowDecl &&
                workflowDecl.Name == name)
            {
                return $"workflow {workflowDecl.Name}({string.Join(", ", workflowDecl.Parameters)})";
            }
            if (stmt is SchemaDeclaration schemaDecl &&
                schemaDecl.Name == name)
            {
                return FormatSchemaHover(schemaDecl);
            }
            if (stmt is TypeDeclaration typeDecl &&
                typeDecl.TypeName == name)
            {
                return FormatTypeHover(typeDecl);
            }
            if (stmt is ApiDeclaration apiDecl &&
                apiDecl.Name == name)
            {
                return FormatApiHover(apiDecl);
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceFileName))
        {
            try
            {
                var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
                var importedSchema = imported.Schemas.FirstOrDefault(s =>
                    string.Equals(s.Name, name, StringComparison.Ordinal));
                if (importedSchema != null)
                {
                    return FormatSchemaHover(importedSchema);
                }

                var importedType = imported.Types.FirstOrDefault(t =>
                    string.Equals(t.TypeName, name, StringComparison.Ordinal));
                if (importedType != null)
                {
                    return FormatTypeHover(importedType);
                }

                var importedClass = imported.Classes.FirstOrDefault(c =>
                    string.Equals(c.Name, name, StringComparison.Ordinal));
                if (importedClass != null)
                {
                    return $"class {importedClass.Name}";
                }

                var importedFunction = imported.Functions.FirstOrDefault(f =>
                    string.Equals(f.Name, name, StringComparison.Ordinal));
                if (importedFunction != null)
                {
                    return $"function {importedFunction.Name}({string.Join(", ", importedFunction.Parameters)})";
                }
            }
            catch
            {
                // Best-effort import resolution for hover.
            }
        }

        return null;
    }

    private static string FormatSchemaHover(SchemaDeclaration schema)
    {
        var fields = schema.Fields.Count == 0
            ? "(no fields)"
            : string.Join(", ", schema.Fields.Select(f =>
                f.Required ? $"{f.Name}: {f.TypeName}" : $"{f.Name}: {f.TypeName}?"));
        return $"schema {schema.Name} {{ {fields} }}";
    }

    private static string FormatTypeHover(TypeDeclaration typeDecl)
    {
        var variants = typeDecl.Constructors.Count == 0
            ? "(no variants)"
            : string.Join(" | ", typeDecl.Constructors.Select(c =>
                c.ParameterNames.Count == 0
                    ? c.Name
                    : $"{c.Name}({string.Join(", ", c.ParameterNames.Select((_, i) => c.FormatParameter(i)))})"));
        return $"type {typeDecl.TypeName} = {variants}";
    }

    private static string FormatApiHover(ApiDeclaration api)
    {
        var methods = api.Methods.Count == 0
            ? "(no methods)"
            : string.Join("; ", api.Methods.Select(m =>
                m.ParameterNames.Count == 0
                    ? $"function {m.Name}()"
                    : $"function {m.Name}({string.Join(", ", m.ParameterNames.Select((_, i) => m.FormatParameter(i)))})"));
        return $"api {api.Name} {{ {methods} }}";
    }
    
    private string? GetBuiltInFunctionInfo(string name)
    {
        return name switch
        {
            "getSymbols" => "function getSymbols(sourceOrFilePath: string) -> object\nParses MALDA code and extracts structured symbol information (classes, functions, actors, prompts) with line numbers and signatures. Accepts file path or source string. Returns an object with 'classes', 'functions', 'actors', 'prompts', and 'parseErrors' arrays.",
            "createGetSymbolsTool" => "function createGetSymbolsTool(workingDirectory?: string) -> Tool\nCreates a tool for parsing MALDA code and extracting symbol information.",
            "getParseErrors" => "function getParseErrors(sourceOrFilePath: string) -> object\nParses MALDA code and returns only parse errors (line, column, message). Accepts file path or source string. Use to validate syntax without running or compiling.",
            "createGetParseErrorsTool" => "function createGetParseErrorsTool(workingDirectory?: string) -> Tool\nCreates a tool that parses MALDA code and returns only parse errors (line, column, message).",
            "checkMalda" => "function checkMalda(sourceOrFilePath: string, typeMode?: string, workingDir?: string) -> object\nDiagnoses MALDA code with the same LanguageService diagnostics as malda check --json (parse, types, schema). typeMode is default | strict | lenient. Returns { ok, executed, file, errorCount, warningCount, infoCount, error?, diagnostics }.",
            "createCheckMaldaTool" => "function createCheckMaldaTool(workingDirectory?: string) -> Tool\nCreates a tool that diagnoses MALDA code with the same diagnostics as malda check --json. Parameters: sourceOrFilePath, typeMode? (default | strict | lenient).",
            "createValidateJsonTool" => "function createValidateJsonTool() -> Tool\nCreates a tool that validates a value against a schema or sum-type name (same as validate()). Parameters: schema (name or inline object), value. Returns { ok, data } or { ok: false, error }. No working directory. Parallel-safe.",
            "createTestMaldaTool" => "function createTestMaldaTool(workingDirectory?: string) -> Tool\nCreates a tool that runs MALDA tests via TestCommandRunner (malda test --format ci). Parameters: path? (file or directory, default '.'), filter?, listOnly?. Returns { success, exitCode, output, report? }. Not parallel-safe.",
            "createWebFetchTool" => "function createWebFetchTool() -> Tool\nCreates a tool that fetches an HTTP or HTTPS URL (no working directory). Parameters: url, maxBytes? (default 100000, cap 500000), timeoutMs? (default 15000, cap 60000). Returns { ok, status, url, content, truncated, error? }.",
            "webFetch" => "function webFetch(url: string, maxBytes?: int, timeoutMs?: int) -> object\nFetches an HTTP or HTTPS URL via httpGet. JSON bodies are returned as a JSON string. Returns { ok, status, url, content, truncated, error? }. The agent factory is createWebFetchTool.",
            "createDeleteFileTool" => "function createDeleteFileTool(workingDirectory?: string) -> Tool\nCreates a tool that deletes a file inside the working directory. Returns { success, error? }.",
            "createCopyFileTool" => "function createCopyFileTool(workingDirectory?: string) -> Tool\nCreates a tool that copies a file inside the working directory. Both srcPath and destPath must stay inside the working directory. Returns { success, error? }.",
            "createEnsureDirTool" => "function createEnsureDirTool(workingDirectory?: string) -> Tool\nCreates a tool that creates a directory (and parents) inside the working directory. Returns { success, path?, error? }.",
            "createSubmitPlanTool" => "function createSubmitPlanTool() -> Tool\nCreates a tool that agents can call to submit a structured plan (steps with id, description, optional dependsOn). Parameters: plan or steps, optional taskSummary. Returns { accepted, planId?, stepCount?, error? }.",
            "createUpdatePlanTool" => "function createUpdatePlanTool() -> Tool\nCreates a tool that updates a stored plan. Parameters: planId (required), optional steps (re-validated; surviving ids keep status), optional taskSummary. Returns { accepted, planId?, stepCount?, steps?, error? }. Not parallel-safe.",
            "createMarkStepTool" => "function createMarkStepTool() -> Tool\nCreates a tool that sets a stored step status (pending | in_progress | done | blocked). Parameters: planId, id, status, optional note. Returns { accepted, planId?, id?, status?, error? }. Not parallel-safe.",
            "think" => "function Agent.think(prompt) -> string\nRuns one agent turn. Argument is a string or PromptInstance.",
            "addTool" => "function Agent.addTool(toolOrName) -> Agent\nRegisters a Tool instance or a built-in tool name.",
            "addToolByName" => "function Agent.addToolByName(name) -> Agent\nRegisters a built-in tool by name.",
            "addAllTools" => "function Agent.addAllTools() -> Agent\nRegisters every built-in tool on the agent.",
            "getAvailableTools" => "function Agent.getAvailableTools() -> array\nReturns the names of tools currently registered on the agent.",
            "addSubAgent" => "function Agent.addSubAgent(agent, toolDescription) -> Agent\nExposes another agent as a tool.",
            "enableMemory" => "function Agent.enableMemory(dimensionOrPath?, precision?)\nAttaches GraphMemory (new store, or shared store at a path).",
            "remember" => "function Agent.remember(fact, context?)\nWrites a fact into the agent's GraphMemory.",
            "executePlan" => "function executePlan(plan: object, agent: Agent | AgentTeam) -> object\nValidates the plan, topo-sorts steps by dependsOn, then runs think() with the step description plus prior step outputs. plan.think: false skips think() (offline). step.out is a schema/sum-type/program(Api) name: coerce think JSON (or step.fixture) through the same path as await prompt … -> Type. Failed extract marks the step failed and skips dependents. Unknown out fails the plan before think(). A team requires each step.role (or step.agent) to name a member, and a dependsOn hop between different roles must match a declared relation. A reject hop runs only when the predecessor is not approved; other hops run only when it is approved (default true; set step.approved or JSON output). Results include prompt, rel, approved, skipped, and data when out succeeds. Returns { planId, completed, failed, skipped, results }.",
            "define" => "function agents.define(spec, client?) -> Agent\nBuilds an agent from { name, role, instructions, kind?, tools?, memoryScope?, workingDirectory? }. kind is Agent (default), CodingAgent, GitAgent, HumanAgent, DevAgent, or MALDACodingAgent. Does not auto-download a local model. No flat define() alias.",
            "team" => "function agents.team(specs, topology, client?) -> AgentTeam\nBinds agent specs to a directed graph. Edge keys rel/contract become relations. Unknown nodes/rels fail before any LLM call.",
            "handoff" => "function AgentTeam.handoff(from, to, payload, think?) -> object\nAllows a hop only along a declared rel: handoff edge. If the edge has contract, runs validate(contract, payload). think: true (or { think: true }) then calls to.think(payload) and returns { ok, data, response }. Default is validate-only. Returns { ok, data } or { ok: false, error }.",
            "review" => "function AgentTeam.review(from, to, payload, think?) -> object\nSame hop rules as handoff, but the edge must have rel: review. Returns { ok, approved, data } (approved defaults true). Pass { approved: false } to record a rejection without an LLM call. think: true calls to.think(payload) and may set approved from JSON.",
            "reject" => "function AgentTeam.reject(from, to, payload, think?) -> object\nSame hop rules as handoff, but the edge must have rel: reject. Returns { ok, rejected, data } (rejected defaults true). Pass { rejected: false } or think: true with JSON { rejected: bool }. Default is validate-only.",
            "consult" => "function AgentTeam.consult(from, to, payload, think?) -> object\nSame hop rules as handoff, but the edge must have rel: consult. One-shot ask; does not transfer the plan. Returns { ok, data } or { ok, data, response }. Default is validate-only. delegate/consult edges still install addSubAgent for supervisor tool loops.",
            "members" => "function AgentTeam.members() -> array\nSorted agent names in the team.",
            "relations" => "function AgentTeam.relations() -> array\nDeclared edges as { from, to, rel, contract }.",
            "decompose" => "function AgentTeam.decompose(instruction, client?) -> object\nCalls decomposeTask(instruction, client?, team) so the plan prompt includes this team's members and relations.",
            "run" => "function AgentTeam.run(plan) -> object\nexecutePlan(plan, team). Team plans need step.role and legal hops. plan.think: false skips think(); reject hops follow the predecessor approved verdict. step.out + step.fixture coerce typed outputs offline.",
            "runProgram" => "function runProgram(program: object) -> any\nRuns a validated program from await prompt(...) -> program(Api) (or equivalent JSON). Calls top-level functions named like api methods; no LLM.",
            "decomposeTask" => "function decomposeTask(instruction: string, client?: LLMClient, team?: AgentTeam) -> object\nUses an LLM to break a high-level task into a structured plan. With a team, the prompt lists members/relations and each step must set role; hops are checked against the graph. Second argument may be the team when no client is passed. Returns { steps, planId?, taskSummary? } or { error }.",
            "runPrompt" => "function runPrompt(prompt, client?, options?) -> string\nRuns a PromptInstance through an LLM. Options: `{ onToken: fn, onReasoning: fn }` for streaming callbacks.",
            "evalPrompt" => "function evalPrompt(prompt, fixture, typeName?) -> object\nOffline fixture in/out on a PromptInstance. No LLM. Returns { ok, data } or { ok: false, error }. data matches await (variant for sum types). Fixture is a dict or LLM-shaped JSON string. Optional typeName overrides instance.returnType.",
            "withExamples" => "function withExamples(prompt, examples, options?) -> PromptInstance\nReturns a copy of a prompt with runtime few-shot examples. Use `{ merge: true }` to append after static prompt examples.",
            "parseJson" => "function parseJson(value, schemaRef, options?) -> object\nParses and validates JSON against a schema declaration.",
            "validate" => "function validate(schema, value) -> object\nChecks a value against a schema or sum-type name. Returns { ok, data } or { ok: false, error }. Success leaves tagged dicts as dicts.",
            "asVariant" => "function asVariant(typeName, value) -> variant\nCoerces a tagged dict (or an existing variant) into a sum-type variant for match. Throws if typeName is not a sum type or the shape is invalid. validate() does not coerce.",
            "wrap" => "function grounded.wrap(value, citations?) -> object\nWraps a payload with citation provenance: { value, citations, sourced }. Citations are { source, id?, span? }. No flat grounded() alias.",
            "fileRead" => "function cap.fileRead(path) -> cap\nMints an unforgeable FileRead token for that path. Object literals cannot forge one. No flat cap() alias.",
            "fileWrite" => "function cap.fileWrite(path) -> cap\nMints an unforgeable FileWrite token for that path.",
            "dirList" => "function cap.dirList(path) -> cap\nMints an unforgeable DirList token for that path.",
            "httpGet" => "function cap.httpGet(origin) -> cap\nMints an unforgeable HTTP GET token for that origin or URL prefix. cap.fetch / webFetch consume it.",
            "mcpCall" => "function cap.mcpCall(server, tool?) -> cap\nMints an unforgeable MCP call token. cap.invoke / callTool consume it. Omit tool to confine later.",
            "shell" => "function cap.shell(prefix) -> cap\nMints an unforgeable argv-prefix token (string or array). cap.run / runCommand consume it.",
            "fetch" => "function cap.fetch(token, urlOrPath?, maxBytes?, timeoutMs?) -> object\nGET using an httpGet token only. URLs outside the origin throw before HTTP. Host-only on JS.",
            "invoke" => "function cap.invoke(token, server, args?) -> object\nCalls MCPServer/MCPClient.callTool using an mcpCall token. Wrong server/tool throws before invoke. Host-only on JS.",
            "confine" => "function cap.confine(token, relative) -> cap\nReturns a narrower token of the same kind. File paths, HTTP URLs, MCP tool names, and argv extras outside the parent throw.",
            "ask" => "function GraphMemory.ask(query, maxResults?, options?) -> object\nOpt-in GraphMemory ASK: same retrieval as query(), then a grounded wrapper with citations. query(..., { grounded: true }) is the same wrap.",
            "loadDocuments" => "function loadDocuments(pattern, dirPath?) -> array\nGlob-loads files as `{ content, metadata: { source } }` documents.",
            "splitDocuments" => "function splitDocuments(documents, chunkSize?, overlap?) -> array\nSplits documents into overlapping chunks.",
            "formatRetrievedDocs" => "function formatRetrievedDocs(documents) -> string\nFormats retrieved documents for prompt context.",
            "composePipe" => "function composePipe(step1, step2, ...) -> function\nLCEL RunnableSequence: composes callables left-to-right into a reusable pipeline function. Pipe-friendly.",
            "parallelRun" => "function parallelRun(input, branches) -> object\nLCEL RunnableParallel: runs named branches concurrently on the same input; returns a map of branch results.",
            "mergeRetrievedDocs" => "function mergeRetrievedDocs(docArrays...) -> array\nMerges multiple Document[] arrays (e.g. from parallel retrieval), deduping by source+chunk or content.",
            "indexInto" => "function indexInto(vectorDb, documents) -> int\nEmbeds and indexes documents into a VectorDB.",
            "uiOnInit" => "function ui.onInit(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired once when a component first appears in a mounted session.",
            "uiOnPreRender" => "function ui.onPreRender(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired before each render diff cycle for the component.",
            "uiOnLoad" => "function ui.onLoad(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a newly mounted component becomes active after render.",
            "uiOnDispose" => "function ui.onDispose(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component is removed or session state is disposed.",
            "uiOnMount" => "function ui.onMount(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component is mounted.",
            "uiOnUpdate" => "function ui.onUpdate(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component persists across renders.",
            "uiOnUnmount" => "function ui.onUnmount(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component is removed from the tree.",
            "uiOnError" => "function ui.onError(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when the UI runtime emits an error event for the component.",
            "uiGenerate" => "function ui.generate(description: string, agent?: Agent, cache?: HTMLCache) -> object\nUses an agent to generate a structured ui.* tree as JSON, validates it, and returns a node object suitable for ui.mount/ui.mountEnvelope.",
            "onInit" => "function ui.onInit(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired once when a component first appears in a mounted session.",
            "onPreRender" => "function ui.onPreRender(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired before each render diff cycle for the component.",
            "onLoad" => "function ui.onLoad(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a newly mounted component becomes active after render.",
            "onDispose" => "function ui.onDispose(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component is removed or session state is disposed.",
            "onMount" => "function ui.onMount(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component is mounted.",
            "onUpdate" => "function ui.onUpdate(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component persists across renders.",
            "onUnmount" => "function ui.onUnmount(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when a component is removed from the tree.",
            "onError" => "function ui.onError(componentId: string, sessionId?: string)\nRegisters a lifecycle hook fired when the UI runtime emits an error event for the component.",
            _ => AgentToolCompletionCatalog.IsCreateToolFactory(name)
                ? $"function {name}(workingDirectory?: string) -> Tool\nCreates a Tool that can be passed to Agent.addTool."
                : null
        };
    }
}
