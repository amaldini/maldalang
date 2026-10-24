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
    public List<CompletionItem> GetCompletions(string source, int line, int column, string? sourceFileName = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completions = new List<CompletionItem>();
        
        var stringImportContext = GetImportStringPathContext(source, line, column);
        if (stringImportContext != null)
        {
            completions.AddRange(GetRelativeMaldaFileCompletions(sourceFileName, stringImportContext));
            return completions.OrderBy(c => c.Label).ToList();
        }

        var packageImportContext = GetModulePackageImportContext(source, line, column);
        if (packageImportContext != null)
        {
            completions.AddRange(GetPackageCompletions(packageImportContext));
            return completions.OrderBy(c => c.Label).ToList();
        }

        if (TypeHintCompletions.GetTypeHintPartialPrefix(source, line, column) is { } typeHintPrefix)
        {
            TypeHintNameIndex? typeHintIndex = null;
            try
            {
                var hintStatements = ParseStatements(source, sourceFileName, cancellationToken);
                typeHintIndex = TypeHintNameIndex.Build(hintStatements);
                if (!string.IsNullOrWhiteSpace(sourceFileName))
                {
                    try
                    {
                        typeHintIndex.MergeImported(
                            ModuleSymbolResolver.LoadImportedSymbols(hintStatements, sourceFileName));
                    }
                    catch
                    {
                        // Best-effort import merge for completions
                    }
                }
            }
            catch
            {
                // Completions still offer Tier 0 + host classes when the buffer does not parse.
            }

            return TypeHintNameIndex.GetCompletions(typeHintIndex, typeHintPrefix);
        }
        
        // Check if we're in a decorator context (after @)
        var decoratorContext = GetDecoratorContext(source, line, column);
        if (decoratorContext != null)
        {
            // We're completing a decorator name
            var decoratorCompletions = GetDecoratorCompletions(decoratorContext);
            completions.AddRange(decoratorCompletions);
            return completions.OrderBy(c => c.Label).ToList();
        }
        
        // Fallback: Check if the line starts with @ at the cursor position
        // This handles edge cases where GetDecoratorContext might miss the context
        try
        {
            var lines = source.Split('\n');
            if (line >= 0 && line < lines.Length)
            {
                var currentLine = lines[line];
                if (column > 0 && column <= currentLine.Length)
                {
                    // Look for @ before the cursor
                    for (int i = Math.Min(column - 1, currentLine.Length - 1); i >= 0; i--)
                    {
                        if (currentLine[i] == '@')
                        {
                            // Found @, check if we're still in decorator context
                            int nameStart = i + 1;
                            if (column >= nameStart)
                            {
                                // Extract partial name
                                string partialName = column > nameStart 
                                    ? currentLine.Substring(nameStart, Math.Min(column - nameStart, currentLine.Length - nameStart))
                                    : "";
                                var decoratorCompletions = GetDecoratorCompletions(partialName);
                                completions.AddRange(decoratorCompletions);
                                return completions.OrderBy(c => c.Label).ToList();
                            }
                            break;
                        }
                        if (!char.IsWhiteSpace(currentLine[i]))
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors in fallback
        }
        
        // Check if we're in a member access context (e.g., "obj." or "obj.me")
        string? objectName = GetObjectNameBeforeDot(source, line, column);
        
        if (objectName != null)
        {
            // We're completing members of an object
            var members = GetMembersForObject(objectName, source, line, column);
            completions.AddRange(members);
            return completions.OrderBy(c => c.Label).ToList();
        }
        
        // Add keywords (MALDA supports both "for (var x in collection)" and "foreach (var x in collection)")
        var keywords = new[] { "if", "else", "while", "for", "foreach", "function", "return", "var",
            "print", "input", "true", "false", "and", "or", "not", "break", "continue",
            "class", "new", "this", "super", "extends", "public", "private", "static", "null",
            "import", "export", "include", "using", "await",
            "prompt", "schema", "api",
            "workflow", "step", "approval", "wait", "retry", "backoff", "delay", "maxDelay", "compensate", "onReject",
            "suite", "context", "policy", "within", "expect" };
        
        foreach (var keyword in keywords)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var insertText = keyword;
            var detail = (string?)null;
            if (keyword == "for")
            {
                insertText = "for (var item in collection) {\n\t\n}";
                detail = "for (var x in collection) — iteration";
            }
            else if (keyword == "foreach")
            {
                insertText = "foreach (var item in collection) {\n\t\n}";
                detail = "foreach (var x in collection) — iteration";
            }
            else if (keyword == "prompt")
            {
                insertText = "prompt name(arg1) {\n\tuser: \"\"\n}";
                detail = "Reusable LLM prompt template";
            }
            else if (keyword == "schema")
            {
                insertText = "schema Name {\n\tfield: string;\n}";
                detail = "JSON schema for parseJson / typed prompts";
            }
            else if (keyword == "api")
            {
                insertText = "api Name {\n\tfunction method(a, b);\n}";
                detail = "Closed API for prompt ... -> program(Api) / runProgram";
            }
            else if (keyword == "suite")
            {
                insertText = "suite \"name\" {\n\tcase \"title\" {\n\t\texpect(true);\n\t}\n}";
                detail = "Eval suite — run with malda eval";
            }
            else if (keyword == "context")
            {
                insertText = "context Session {\n\tbudget: 8000 tokens;\n\tretain: last 20;\n\tevict: oldest;\n}";
                detail = "Declared conversation context";
            }
            else if (keyword == "policy")
            {
                insertText = "policy {\n\tshell: deny;\n}";
                detail = "Program-boundary capability policy";
            }
            else if (keyword == "within")
            {
                insertText = "within (30s) {\n\t\n}";
                detail = "Cancel subtree after a duration";
            }
            else if (keyword == "expect")
            {
                insertText = "expect(condition);";
                detail = "Eval-suite assertion";
            }
            completions.Add(new CompletionItem
            {
                Label = keyword,
                Kind = "keyword",
                InsertText = insertText,
                Detail = detail
            });
        }

        if (IsInsidePromptBody(source, line, column))
        {
            foreach (var field in PromptBodyFields.Names)
            {
                completions.Add(new CompletionItem
                {
                    Label = field,
                    Kind = "property",
                    InsertText = field == "gather" ? "gather: [\"read_file\"]" : field,
                    Detail = field == "gather"
                        ? "Mode C: tool round, then typed extract on await"
                        : "prompt field"
                });
            }
        }
        
        // Add ALL built-in functions
        var builtIns = new[] { 
            "int", "float", "string", "abs", "sum", "average", "max", "min", "pow", "sqrt",
            "length", "upper", "lower", "trim", "substring", "indexOf", "replace", "split",
            "normalizeText", "tokenize", "tokenOverlap", "similarity", "extractNumbers",
            "startsWith", "endsWith", "padStart", "padEnd", "repeat",
            "append", "pop", "shift",
            "input", "getEnv", "getCommandLineArgs", "hasEnv", "parseJSON", "toJSON", "parseJson", "validate", "asVariant", "evalPrompt",
            "readFile", "writeFile", "hasFile", "hasDirectory", "listDirectory",
            "replaceInFile", "editFile", "grep", "insertAtLine",
            "getSymbols", "getParseErrors",
            "executePlan", "runProgram", "decomposeTask",
            "runPrompt", "loadDocuments", "splitDocuments", "formatRetrievedDocs", "composePipe", "parallelRun", "mergeRetrievedDocs", "withExamples", "indexInto",
            "extractHTML", "markdownToHtml", "generateUI",
            "uiRow", "uiColumn", "uiStack", "uiSpacer", "uiPanel",
            "uiText", "uiHeading", "uiImage", "uiIcon",
            "uiButton", "uiTextField", "uiCheckbox", "uiSelect", "uiSlider", "uiDatePicker",
            "uiList", "uiTable", "uiAlert", "uiProgress", "uiModal",
            "uiForm", "uiField", "uiTextArea", "uiRadioGroup", "uiSwitch",
            "uiTabs", "uiAccordion", "uiBreadcrumbs", "uiDrawer",
            "uiDataGrid", "uiTreeView", "uiPaginator", "uiEmptyState", "uiBadge",
            "uiToast", "uiSkeleton", "uiSpinner", "uiErrorBoundary",
            "uiSlot", "uiWithSlot", "uiWhen", "uiChoose", "uiEach",
            "uiCrudModel", "uiCrudControls", "uiCrudSchema",
            "uiMount", "uiMountEnvelope", "uiRender", "uiDispatchEvent", "uiPullEvent",
            "uiState", "uiGetState", "uiSetState", "uiPinState", "uiUnpinState", "uiInvalidate",
            "uiOnInit", "uiOnPreRender", "uiOnLoad", "uiOnDispose",
            "uiOnMount", "uiOnUpdate", "uiOnUnmount", "uiOnError",
            "uiConfigure", "uiSnapshot", "uiResync", "uiGenerate"
        };
        
        foreach (var builtIn in builtIns.Concat(AgentToolCompletionCatalog.CreateToolFactories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            completions.Add(new CompletionItem
            {
                Label = builtIn,
                Kind = "function",
                Detail = AgentToolCompletionCatalog.IsCreateToolFactory(builtIn)
                    ? "Tool factory for agents"
                    : "Built-in function",
                InsertText = builtIn + "()"
            });
        }
        
        // Add built-in classes
        var builtInClasses = new[]
        {
            new { Name = "LLMClient", Detail = "LLMClient(apiUrl, apiKey, model)", InsertText = "new LLMClient()" },
            new { Name = "OpenRouterClient", Detail = "OpenRouterClient(model?)", InsertText = "new OpenRouterClient()" },
            new { Name = "LlamaCppClient", Detail = "LlamaCppClient(modelPath?)", InsertText = "new LlamaCppClient()" },
            new { Name = "Conversation", Detail = "Conversation(client, systemPrompt)", InsertText = "new Conversation()" },
            new { Name = "Tool", Detail = "Tool(name, description, schema)", InsertText = "new Tool()" },
            new { Name = "Agent", Detail = "Agent(name, role, instructions, client?)", InsertText = "new Agent()" },
            new { Name = "CodingAgent", Detail = "CodingAgent(name, role, instructions, client?, workingDirectory?)", InsertText = "new CodingAgent()" },
            new { Name = "MALDACodingAgent", Detail = "MALDACodingAgent(name, role, instructions, client?, workingDirectory?)", InsertText = "new MALDACodingAgent()" },
            new { Name = "GitAgent", Detail = "GitAgent(name, role, instructions, client?, workingDirectory?)", InsertText = "new GitAgent()" },
            new { Name = "DevAgent", Detail = "DevAgent(name, role, instructions, client?, workingDirectory?, includeSymbols?)", InsertText = "new DevAgent()" },
            new { Name = "HumanAgent", Detail = "HumanAgent(name, role, instructions, client?, workingDirectory?)", InsertText = "new HumanAgent()" },
            new { Name = "RestServer", Detail = "RestServer(port, host?)", InsertText = "new RestServer()" },
            new { Name = "RestClient", Detail = "RestClient(baseUrl?, timeout?)", InsertText = "new RestClient()" },
            new { Name = "HTMLCache", Detail = "HTMLCache(cacheDirectory?, maxSize?, expirationHours?)", InsertText = "new HTMLCache()" },
            new { Name = "SqlServerClient", Detail = "SqlServerClient(connectionString?)", InsertText = "new SqlServerClient()" },
            new { Name = "PostgresClient", Detail = "PostgresClient(connectionString?)", InsertText = "new PostgresClient()" },
            new { Name = "SqliteClient", Detail = "SqliteClient(connectionString?)", InsertText = "new SqliteClient()" },
            new { Name = "SerialConnection", Detail = "SerialConnection()", InsertText = "new SerialConnection()" },
            new { Name = "ArduinoConnection", Detail = "ArduinoConnection(url) or ArduinoConnection(port, baudRate)", InsertText = "new ArduinoConnection()" },
            new { Name = "GraphMemory", Detail = "GraphMemory() — semantic memory; ask() returns grounded citations", InsertText = "new GraphMemory()" }
        };
        
        foreach (var cls in builtInClasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completions.Add(new CompletionItem
            {
                Label = cls.Name,
                Kind = "class",
                Detail = cls.Detail,
                InsertText = cls.InsertText
            });
        }

        foreach (var cls in NeuralCompletionCatalog.HostClasses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completions.Add(new CompletionItem
            {
                Label = cls.Name,
                Kind = "class",
                Detail = cls.Detail,
                InsertText = cls.InsertText
            });
        }

        foreach (var module in StdLibModuleCompletions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completions.Add(new CompletionItem
            {
                Label = module.Name,
                Kind = "module",
                Detail = module.Detail,
                InsertText = module.Name
            });
        }
        
        // Try to parse and extract symbols (retry a recovered buffer if the current line is incomplete)
        try
        {
            TryExtractDocumentSymbols(source, sourceFileName, completions, line, column, cancellationToken);
        }
        catch
        {
            try
            {
                TryExtractDocumentSymbols(
                    RecoverIncompleteSource(source, line),
                    sourceFileName,
                    completions,
                    line,
                    column,
                    cancellationToken);
            }
            catch
            {
                // Ignore parse errors for completion
            }
        }
        
        return completions.OrderBy(c => c.Label).ToList();
    }

    private string? GetObjectNameBeforeDot(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;
            
            var currentLine = lines[line];
            if (column < 0 || column > currentLine.Length)
                return null;
            
            // Look backwards from the cursor to find a dot
            int dotIndex = -1;
            for (int i = column - 1; i >= 0; i--)
            {
                if (currentLine[i] == '.')
                {
                    dotIndex = i;
                    break;
                }
                if (!char.IsLetterOrDigit(currentLine[i]) && currentLine[i] != '_')
                {
                    // Hit a non-identifier character before finding a dot
                    return null;
                }
            }
            
            if (dotIndex == -1)
                return null;
            
            // Extract the object name before the dot
            int start = dotIndex - 1;
            while (start >= 0 && (char.IsLetterOrDigit(currentLine[start]) || currentLine[start] == '_'))
            {
                start--;
            }
            start++;
            
            if (start >= dotIndex)
                return null;
            
            return currentLine.Substring(start, dotIndex - start);
        }
        catch
        {
            return null;
        }
    }
    
    private List<CompletionItem> GetMembersForObject(string objectName, string source, int line, int column)
    {
        var members = new List<CompletionItem>();

        if (TryAddStdLibNamespaceMembers(objectName, members))
        {
            return members;
        }

        List<Statement>? statements = null;
        try
        {
            statements = ParseStatements(source, sourceFileName: null, CancellationToken.None);
        }
        catch
        {
            // Ignore parse errors
        }

        string? resolvedType = null;
        if (statements != null)
        {
            resolvedType = FindVariableType(statements, objectName, line);
        }

        if (resolvedType == null)
        {
            var recovered = StripUnfinishedMemberAccessLine(source, line);
            if (recovered != null)
            {
                try
                {
                    statements = ParseStatements(recovered, sourceFileName: null, CancellationToken.None);
                    resolvedType = FindVariableType(statements, objectName, line);
                }
                catch
                {
                    // Keep the unresolved name so stdlib and host classes can still match.
                }
            }
        }
        
        // Use resolved type if found, otherwise use objectName directly
        string typeToCheck = resolvedType ?? objectName;
        
        // Check for built-in class instances
        if (typeToCheck == "LLMClient" || typeToCheck == "OpenRouterClient")
        {
            // Properties
            members.Add(new CompletionItem { Label = "apiUrl", Kind = "property", Detail = "string", InsertText = "apiUrl" });
            members.Add(new CompletionItem { Label = "apiKey", Kind = "property", Detail = "string", InsertText = "apiKey" });
            members.Add(new CompletionItem { Label = "model", Kind = "property", Detail = "string", InsertText = "model" });
            members.Add(new CompletionItem { Label = "temperature", Kind = "property", Detail = "float", InsertText = "temperature" });
            members.Add(new CompletionItem { Label = "maxTokens", Kind = "property", Detail = "int", InsertText = "maxTokens" });
            members.Add(new CompletionItem { Label = "examples", Kind = "property", Detail = "array", InsertText = "examples" });
            if (typeToCheck == "OpenRouterClient")
            {
                members.Add(new CompletionItem { Label = "httpReferer", Kind = "property", Detail = "string (HTTP-Referer)", InsertText = "httpReferer" });
                members.Add(new CompletionItem { Label = "appTitle", Kind = "property", Detail = "string (X-OpenRouter-Title)", InsertText = "appTitle" });
                members.Add(new CompletionItem { Label = "appCategories", Kind = "property", Detail = "string (X-OpenRouter-Categories)", InsertText = "appCategories" });
            }
            // Methods
            members.Add(new CompletionItem { Label = "chat", Kind = "method", Detail = "chat(messages, tools?)", InsertText = "chat()" });
            members.Add(new CompletionItem { Label = "complete", Kind = "method", Detail = "complete(prompt)", InsertText = "complete()" });
            members.Add(new CompletionItem { Label = "setTemperature", Kind = "method", Detail = "setTemperature(temp)", InsertText = "setTemperature()" });
            members.Add(new CompletionItem { Label = "setMaxTokens", Kind = "method", Detail = "setMaxTokens(tokens)", InsertText = "setMaxTokens()" });
        }
        else if (typeToCheck == "PromptInstance")
        {
            members.Add(new CompletionItem { Label = "system", Kind = "property", Detail = "string?", InsertText = "system" });
            members.Add(new CompletionItem { Label = "user", Kind = "property", Detail = "string", InsertText = "user" });
            members.Add(new CompletionItem { Label = "model", Kind = "property", Detail = "string?", InsertText = "model" });
            members.Add(new CompletionItem { Label = "temperature", Kind = "property", Detail = "float?", InsertText = "temperature" });
            members.Add(new CompletionItem { Label = "tools", Kind = "property", Detail = "array?", InsertText = "tools" });
            members.Add(new CompletionItem { Label = "gather", Kind = "property", Detail = "array of tool names (Mode C)", InsertText = "gather" });
            members.Add(new CompletionItem { Label = "maxTokens", Kind = "property", Detail = "int?", InsertText = "maxTokens" });
            members.Add(new CompletionItem { Label = "examples", Kind = "property", Detail = "array?", InsertText = "examples" });
            members.Add(new CompletionItem { Label = "returnType", Kind = "property", Detail = "string? (declared -> Type)", InsertText = "returnType" });
            members.Add(new CompletionItem { Label = "eval", Kind = "method", Detail = "eval(fixture, typeName?) — offline fixture in/out", InsertText = "eval()" });
            members.Add(new CompletionItem { Label = "toPromptString", Kind = "method", Detail = "toPromptString() — user prompt string", InsertText = "toPromptString()" });
            members.Add(new CompletionItem { Label = "getUser", Kind = "method", Detail = "getUser()", InsertText = "getUser()" });
            members.Add(new CompletionItem { Label = "getSystem", Kind = "method", Detail = "getSystem()", InsertText = "getSystem()" });
        }
        else if (typeToCheck == "Conversation")
        {
            members.Add(new CompletionItem { Label = "addUserMessage", Kind = "method", Detail = "addUserMessage(content)", InsertText = "addUserMessage()" });
            members.Add(new CompletionItem { Label = "addAssistantMessage", Kind = "method", Detail = "addAssistantMessage(content)", InsertText = "addAssistantMessage()" });
            members.Add(new CompletionItem { Label = "addTool", Kind = "method", Detail = "addTool(tool)", InsertText = "addTool()" });
            members.Add(new CompletionItem { Label = "send", Kind = "method", Detail = "send()", InsertText = "send()" });
            members.Add(new CompletionItem { Label = "getMessages", Kind = "method", Detail = "getMessages()", InsertText = "getMessages()" });
            members.Add(new CompletionItem { Label = "getFailedWriteTools", Kind = "method", Detail = "getFailedWriteTools()", InsertText = "getFailedWriteTools()" });
            members.Add(new CompletionItem { Label = "clear", Kind = "method", Detail = "clear()", InsertText = "clear()" });
            members.Add(new CompletionItem { Label = "getHistory", Kind = "method", Detail = "getHistory()", InsertText = "getHistory()" });
        }
        else if (typeToCheck == "grounded")
        {
            members.Add(new CompletionItem
            {
                Label = "wrap",
                Kind = "method",
                Detail = "wrap(value, citations?) — payload plus { source, id?, span? }",
                InsertText = "wrap()"
            });
        }
        else if (typeToCheck == "cap")
        {
            members.Add(new CompletionItem { Label = "fileRead", Kind = "method", Detail = "fileRead(path) — mint an unforgeable FileRead token", InsertText = "fileRead()" });
            members.Add(new CompletionItem { Label = "fileWrite", Kind = "method", Detail = "fileWrite(path) — mint an unforgeable FileWrite token", InsertText = "fileWrite()" });
            members.Add(new CompletionItem { Label = "dirList", Kind = "method", Detail = "dirList(path) — mint an unforgeable DirList token", InsertText = "dirList()" });
            members.Add(new CompletionItem { Label = "httpGet", Kind = "method", Detail = "httpGet(origin) — mint an unforgeable HTTP GET token", InsertText = "httpGet()" });
            members.Add(new CompletionItem { Label = "mcpCall", Kind = "method", Detail = "mcpCall(server, tool?) — mint an unforgeable MCP call token", InsertText = "mcpCall()" });
            members.Add(new CompletionItem { Label = "shell", Kind = "method", Detail = "shell(prefix) — mint an unforgeable argv-prefix token", InsertText = "shell()" });
            members.Add(new CompletionItem { Label = "is", Kind = "method", Detail = "is(value, kind?) — true only for a real capability token", InsertText = "is()" });
            members.Add(new CompletionItem { Label = "confine", Kind = "method", Detail = "confine(token, relative) — attenuate a token under its path / origin / tool / argv", InsertText = "confine()" });
            members.Add(new CompletionItem { Label = "read", Kind = "method", Detail = "read(token) — read using FileRead only (rejects strings and dicts)", InsertText = "read()" });
            members.Add(new CompletionItem { Label = "write", Kind = "method", Detail = "write(token, content) — write using FileWrite only", InsertText = "write()" });
            members.Add(new CompletionItem { Label = "list", Kind = "method", Detail = "list(token) — list using DirList only", InsertText = "list()" });
            members.Add(new CompletionItem { Label = "fetch", Kind = "method", Detail = "fetch(token, urlOrPath?) — GET using httpGet only (rejects strings and dicts)", InsertText = "fetch()" });
            members.Add(new CompletionItem { Label = "invoke", Kind = "method", Detail = "invoke(token, server, args?) — callTool using mcpCall only", InsertText = "invoke()" });
            members.Add(new CompletionItem { Label = "run", Kind = "method", Detail = "run(token, extraArgs?) — runCommand using shell only", InsertText = "run()" });
        }
        else if (typeToCheck == "GraphMemory")
        {
            members.Add(new CompletionItem { Label = "initialize", Kind = "method", Detail = "initialize(dimension?, precision?, embedFn?)", InsertText = "initialize()" });
            members.Add(new CompletionItem { Label = "remember", Kind = "method", Detail = "remember(fact, context?, metadata?)", InsertText = "remember()" });
            members.Add(new CompletionItem { Label = "query", Kind = "method", Detail = "query(query, maxResults?, options?) — array of hits; options.grounded wraps citations", InsertText = "query()" });
            members.Add(new CompletionItem { Label = "ask", Kind = "method", Detail = "ask(query, maxResults?, options?) — grounded wrapper with citations", InsertText = "ask()" });
            members.Add(new CompletionItem { Label = "getRecent", Kind = "method", Detail = "getRecent(count?)", InsertText = "getRecent()" });
            members.Add(new CompletionItem { Label = "getLastQueryDiagnostics", Kind = "method", Detail = "getLastQueryDiagnostics()", InsertText = "getLastQueryDiagnostics()" });
        }
        else if (typeToCheck == "Agent" || typeToCheck == "CodingAgent" || typeToCheck == "MALDACodingAgent" || typeToCheck == "GitAgent" || typeToCheck == "DevAgent" || typeToCheck == "HumanAgent")
        {
            members.AddRange(AgentToolCompletionCatalog.AgentMembers);
        }
        else if (typeToCheck == "Tool")
        {
            members.AddRange(AgentToolCompletionCatalog.ToolMembers);
        }
        else if (typeToCheck == "this")
        {
            if (statements != null)
            {
                var currentClass = FindClassAtPosition(statements, line);
                AddClassMembers(members, currentClass);
            }
        }
        else if (typeToCheck == "Array")
        {
            AddArrayMembers(members);
        }
        else if (TryAddStdLibNamespaceMembers(typeToCheck, members))
        {
            // math / str / io / pdf / doc / result / option / nn members
        }
        else if (NeuralCompletionCatalog.TryAddMembers(typeToCheck, members))
        {
            // Dense / Sequential / Conv / Embedding / Rnn / LayerNorm / Attention / OnnxModel
        }
        else if (TryAddSumTypeNamespaceMembers(typeToCheck, source, members) ||
                 TryAddSumTypeNamespaceMembers(objectName, source, members))
        {
            // r.Ok / Result.Err
        }
        else if (statements != null)
        {
            var classDecl = FindClassDeclaration(statements, typeToCheck);
            AddClassMembers(members, classDecl);
        }
        
        return members;
    }

    private static readonly (string Name, string Detail)[] StdLibModuleCompletions =
    {
        (StdLibNamespaces.IoModule, "io.print, files, env, git"),
        (StdLibNamespaces.MathModule, "math.sqrt, clamp, random, …"),
        (StdLibNamespaces.StrModule, "str.upper, trim, split, …"),
        (StdLibNamespaces.PdfModule, "pdf.extractText(path)"),
        (StdLibNamespaces.DocModule, "doc.extractText(path)"),
        (StdLibNamespaces.ResultModule, "result.ok / result.err"),
        (StdLibNamespaces.OptionModule, "option.some / option.none"),
        (StdLibNamespaces.GroundedModule, "grounded.wrap(value, citations?) — payload plus citations"),
        (StdLibNamespaces.CapModule, "cap.fileRead(path) — unforgeable file / HTTP / MCP / shell capability tokens"),
        (StdLibNamespaces.AgentsModule, "agents.define / agents.team — role specs plus a relation graph"),
        (StdLibNamespaces.TraceModule, "trace.span / journal / lastUsage — run observability"),
        (StdLibNamespaces.NnModule, "nn.dense / sequential / activations — neural-net helpers"),
        (StdLibNamespaces.BufModule, "buf.float64(n) / bytes(n) / readAt / writeAt — fixed-size numeric buffers, binary data")
    };

    private static bool TryAddStdLibNamespaceMembers(string moduleName, List<CompletionItem> members)
    {
        IReadOnlySet<string>? methods = moduleName switch
        {
            StdLibNamespaces.IoModule => StdLibNamespaces.IoMethodNames,
            StdLibNamespaces.MathModule or StdLibNamespaces.DeprecatedMathModuleAlias => StdLibNamespaces.MathMethodNames,
            StdLibNamespaces.StrModule => StdLibNamespaces.StrMethodNames,
            StdLibNamespaces.PdfModule => StdLibNamespaces.PdfMethodNames,
            StdLibNamespaces.DocModule => StdLibNamespaces.DocMethodNames,
            StdLibNamespaces.ResultModule => StdLibNamespaces.ResultMethodNames,
            StdLibNamespaces.OptionModule => StdLibNamespaces.OptionMethodNames,
            StdLibNamespaces.GroundedModule => StdLibNamespaces.GroundedMethodNames,
            StdLibNamespaces.CapModule => StdLibNamespaces.CapMethodNames,
            StdLibNamespaces.AgentsModule => StdLibNamespaces.AgentsMethodNames,
            StdLibNamespaces.NnModule => StdLibNamespaces.NnMethodNames,
            StdLibNamespaces.BufModule => StdLibNamespaces.BufMethodNames,
            _ => null
        };
        if (methods == null)
            return false;

        var canonical = moduleName == StdLibNamespaces.DeprecatedMathModuleAlias
            ? StdLibNamespaces.MathModule
            : moduleName;
        foreach (var method in methods.OrderBy(name => name, StringComparer.Ordinal))
        {
            var detail = canonical == StdLibNamespaces.NnModule
                ? NeuralCompletionCatalog.NnMethodDetail(method)
                : $"{canonical}.{method}()";
            members.Add(new CompletionItem
            {
                Label = method,
                Kind = "method",
                Detail = detail,
                InsertText = method + "()"
            });
        }

        return true;
    }

    private static bool TryAddSumTypeNamespaceMembers(string typeName, string source, List<CompletionItem> members)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(source))
            return false;

        try
        {
            var lexer = new Lexer(source);
            var parser = new MaldaLang.Parser.Parser(lexer.Tokenize());
            var statements = parser.Parse();
            var index = SumTypeIndex.Build(statements);
            var constructors = index.GetConstructors(typeName);
            if (constructors.Count == 0)
                return false;

            foreach (var ctor in constructors)
            {
                members.Add(new CompletionItem
                {
                    Label = ctor,
                    Kind = "method",
                    Detail = $"{typeName}.{ctor}()",
                    InsertText = ctor + "()"
                });
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddArrayMembers(List<CompletionItem> members)
    {
        members.Add(new CompletionItem { Label = "length", Kind = "property", Detail = "int", InsertText = "length" });
        members.Add(new CompletionItem { Label = "append", Kind = "method", Detail = "append(item)", InsertText = "append()" });
        members.Add(new CompletionItem { Label = "pop", Kind = "method", Detail = "pop()", InsertText = "pop()" });
        members.Add(new CompletionItem { Label = "popOrNull", Kind = "method", Detail = "popOrNull()", InsertText = "popOrNull()" });
        members.Add(new CompletionItem { Label = "shift", Kind = "method", Detail = "shift()", InsertText = "shift()" });
        members.Add(new CompletionItem { Label = "shiftOrNull", Kind = "method", Detail = "shiftOrNull()", InsertText = "shiftOrNull()" });
        members.Add(new CompletionItem { Label = "concat", Kind = "method", Detail = "concat(otherArray)", InsertText = "concat()" });
        members.Add(new CompletionItem { Label = "except", Kind = "method", Detail = "except(otherArray)", InsertText = "except()" });
        members.Add(new CompletionItem { Label = "get", Kind = "method", Detail = "get(index, fallback?)", InsertText = "get()" });
        members.Add(new CompletionItem { Label = "at", Kind = "method", Detail = "at(index)", InsertText = "at()" });
        members.Add(new CompletionItem { Label = "map", Kind = "method", Detail = "map(fn)", InsertText = "map()" });
        members.Add(new CompletionItem { Label = "filter", Kind = "method", Detail = "filter(fn)", InsertText = "filter()" });
        members.Add(new CompletionItem { Label = "reduce", Kind = "method", Detail = "reduce(fn, initialValue?)", InsertText = "reduce()" });
        members.Add(new CompletionItem { Label = "forEach", Kind = "method", Detail = "forEach(fn)", InsertText = "forEach()" });
        members.Add(new CompletionItem { Label = "find", Kind = "method", Detail = "find(fn)", InsertText = "find()" });
        members.Add(new CompletionItem { Label = "findIndex", Kind = "method", Detail = "findIndex(fn)", InsertText = "findIndex()" });
        members.Add(new CompletionItem { Label = "some", Kind = "method", Detail = "some(fn)", InsertText = "some()" });
        members.Add(new CompletionItem { Label = "every", Kind = "method", Detail = "every(fn)", InsertText = "every()" });
        members.Add(new CompletionItem { Label = "sort", Kind = "method", Detail = "sort(compareFn?)", InsertText = "sort()" });
        members.Add(new CompletionItem { Label = "reverse", Kind = "method", Detail = "reverse()", InsertText = "reverse()" });
        members.Add(new CompletionItem { Label = "slice", Kind = "method", Detail = "slice(start, end?)", InsertText = "slice()" });
        members.Add(new CompletionItem { Label = "indexOf", Kind = "method", Detail = "indexOf(value)", InsertText = "indexOf()" });
        members.Add(new CompletionItem { Label = "includes", Kind = "method", Detail = "includes(value)", InsertText = "includes()" });
        members.Add(new CompletionItem { Label = "join", Kind = "method", Detail = "join(separator?)", InsertText = "join()" });
        members.Add(new CompletionItem { Label = "sum", Kind = "method", Detail = "sum()", InsertText = "sum()" });
        members.Add(new CompletionItem { Label = "average", Kind = "method", Detail = "average()", InsertText = "average()" });
        members.Add(new CompletionItem { Label = "min", Kind = "method", Detail = "min()", InsertText = "min()" });
        members.Add(new CompletionItem { Label = "max", Kind = "method", Detail = "max()", InsertText = "max()" });
    }

    private static void AddClassMembers(List<CompletionItem> members, ClassDeclaration? classDecl)
    {
        if (classDecl == null)
        {
            return;
        }

        foreach (var member in classDecl.Members)
        {
            if (member.Type == MemberType.Method && member.Value is FunctionDeclaration funcDecl)
            {
                members.Add(new CompletionItem
                {
                    Label = member.Name,
                    Kind = "method",
                    Detail = $"function {member.Name}({string.Join(", ", funcDecl.Parameters)})",
                    InsertText = member.Name + "()"
                });
            }
            else if (member.Type == MemberType.Field)
            {
                members.Add(new CompletionItem
                {
                    Label = member.Name,
                    Kind = "property",
                    Detail = "field",
                    InsertText = member.Name
                });
            }
        }
    }
}
