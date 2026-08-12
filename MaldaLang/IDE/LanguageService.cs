// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.PackageManager;
using System.Threading;

namespace MaldaLang.IDE.Services;

public class DecoratorInfo
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Documentation { get; set; } = string.Empty;
    public int MinArgs { get; set; }
    public int MaxArgs { get; set; }
    public List<string> ArgDescriptions { get; set; } = new();
}

public class LanguageService : ILanguageService
{
    private static readonly Dictionary<string, DecoratorInfo> SupportedDecorators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PAGE"] = new DecoratorInfo
        {
            Name = "PAGE",
            Description = "Server-rendered HTML page decorator",
            Format = "@PAGE(\"/path\")",
            Documentation = "Marks a function as an HTTP GET page route for HttpServer. If omitted, path defaults to '/'.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Page route path, e.g., \"/\" or \"/users/{id}\"" }
        },
        ["AIPAGE"] = new DecoratorInfo
        {
            Name = "AIPAGE",
            Description = "AI-generated HTML page decorator",
            Format = "@AIPAGE(\"/path\", \"description\")",
            Documentation = "Marks a function as an AI-generated page route. The first argument is the route path and the second is a natural-language description of the desired page.",
            MinArgs = 2,
            MaxArgs = 2,
            ArgDescriptions = new List<string>
            {
                "path (string): Page route path, e.g., \"/contact\"",
                "description (string): Prompt describing the page to generate"
            }
        },
        ["COMPONENT"] = new DecoratorInfo
        {
            Name = "COMPONENT",
            Description = "Component route decorator",
            Format = "@COMPONENT(\"/path\")",
            Documentation = "Marks a function as a component route. If path is omitted, a default component route is derived from the function name.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Component route path" }
        },
        ["LIVE"] = new DecoratorInfo
        {
            Name = "LIVE",
            Description = "Live component endpoint decorator",
            Format = "@LIVE(\"/path\")",
            Documentation = "Marks a function as a live (SSE) endpoint for component updates. If path is omitted, a default live path is derived from the function name.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Live endpoint path" }
        },
        ["ACTION"] = new DecoratorInfo
        {
            Name = "ACTION",
            Description = "Component action endpoint decorator",
            Format = "@ACTION(\"/path\")",
            Documentation = "Marks a function as an action endpoint. It is registered as an HTTP POST route for form and component actions.",
            MinArgs = 0,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string, optional): Action endpoint path" }
        },
        ["client"] = new DecoratorInfo
        {
            Name = "client",
            Description = "Compile-time client target decorator",
            Format = "@client()",
            Documentation = "Marks a function as JavaScript-only during transpilation. The function is excluded from C# output.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["javascript"] = new DecoratorInfo
        {
            Name = "javascript",
            Description = "Alias of @client() compile-time target decorator",
            Format = "@javascript()",
            Documentation = "Marks a function as JavaScript-only during transpilation. Alias of @client().",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["server"] = new DecoratorInfo
        {
            Name = "server",
            Description = "Compile-time server target decorator",
            Format = "@server()",
            Documentation = "Marks a function as C# server-only during transpilation. The function is excluded from JavaScript output.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["csharp"] = new DecoratorInfo
        {
            Name = "csharp",
            Description = "Alias of @server() compile-time target decorator",
            Format = "@csharp()",
            Documentation = "Marks a function as C# server-only during transpilation. Alias of @server().",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["shared"] = new DecoratorInfo
        {
            Name = "shared",
            Description = "Compile-time cross-target decorator",
            Format = "@shared()",
            Documentation = "Marks a function as shared between C# and JavaScript transpilation targets. Shared functions should avoid target-specific built-ins.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        },
        ["GET"] = new DecoratorInfo
        {
            Name = "GET",
            Description = "HTTP GET endpoint decorator",
            Format = "@GET(\"/path\")",
            Documentation = "Marks a function as a GET endpoint. The first argument must be a string path (e.g., \"/api/users\"). Supports path parameters with {paramName} syntax.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users\" or \"/api/users/{id}\"" }
        },
        ["POST"] = new DecoratorInfo
        {
            Name = "POST",
            Description = "HTTP POST endpoint decorator",
            Format = "@POST(\"/path\")",
            Documentation = "Marks a function as a POST endpoint. The first argument must be a string path. Use a parameter named 'body' to receive the request body.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users\"" }
        },
        ["PUT"] = new DecoratorInfo
        {
            Name = "PUT",
            Description = "HTTP PUT endpoint decorator",
            Format = "@PUT(\"/path\")",
            Documentation = "Marks a function as a PUT endpoint. The first argument must be a string path. Use a parameter named 'body' to receive the request body.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users/{id}\"" }
        },
        ["DELETE"] = new DecoratorInfo
        {
            Name = "DELETE",
            Description = "HTTP DELETE endpoint decorator",
            Format = "@DELETE(\"/path\")",
            Documentation = "Marks a function as a DELETE endpoint. The first argument must be a string path.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users/{id}\"" }
        },
        ["PATCH"] = new DecoratorInfo
        {
            Name = "PATCH",
            Description = "HTTP PATCH endpoint decorator",
            Format = "@PATCH(\"/path\")",
            Documentation = "Marks a function as a PATCH endpoint. The first argument must be a string path. Use a parameter named 'body' to receive the request body.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path, e.g., \"/api/users/{id}\"" }
        },
        ["OPTIONS"] = new DecoratorInfo
        {
            Name = "OPTIONS",
            Description = "HTTP OPTIONS endpoint decorator",
            Format = "@OPTIONS(\"/path\")",
            Documentation = "Marks a function as an OPTIONS endpoint. The first argument must be a string path.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "path (string): Route path" }
        },
        ["Tool"] = new DecoratorInfo
        {
            Name = "Tool",
            Description = "LLM tool decorator",
            Format = "@Tool(\"name\", \"description\", schema?)",
            Documentation = "Registers a function as an LLM tool. Requires at least 2 arguments: tool name and description. Optional third argument is a JSON schema string or object.",
            MinArgs = 2,
            MaxArgs = 3,
            ArgDescriptions = new List<string>
            {
                "name (string): Tool identifier",
                "description (string): Human-readable description",
                "schema (string/object, optional): JSON schema for parameters. If omitted, auto-generated from function parameters."
            }
        },
        ["MCPTool"] = new DecoratorInfo
        {
            Name = "MCPTool",
            Description = "MCP tool decorator",
            Format = "@MCPTool(\"name\", \"description\", schema?)",
            Documentation = "Registers a function as an MCP (Model Context Protocol) tool. Requires at least 2 arguments: tool name and description. Optional third argument is a JSON schema string or object.",
            MinArgs = 2,
            MaxArgs = 3,
            ArgDescriptions = new List<string>
            {
                "name (string): Tool identifier",
                "description (string): Human-readable description",
                "schema (string/object, optional): JSON schema for parameters. If omitted, auto-generated from function parameters."
            }
        },
        ["PathParam"] = new DecoratorInfo
        {
            Name = "PathParam",
            Description = "Path parameter decorator",
            Format = "@PathParam(\"paramName\")",
            Documentation = "Marks a function parameter as a path parameter. Used in REST endpoints with decorator-based parameter binding. The argument is the path parameter name from the route.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "paramName (string): Name of the path parameter in the route (e.g., \"id\" for route \"/users/{id}\")" }
        },
        ["QueryParam"] = new DecoratorInfo
        {
            Name = "QueryParam",
            Description = "Query parameter decorator",
            Format = "@QueryParam(\"paramName\")",
            Documentation = "Marks a function parameter as a query parameter. Used in REST endpoints with decorator-based parameter binding.",
            MinArgs = 1,
            MaxArgs = 1,
            ArgDescriptions = new List<string> { "paramName (string): Query parameter name (e.g., \"limit\" for ?limit=10)" }
        },
        ["Body"] = new DecoratorInfo
        {
            Name = "Body",
            Description = "Request body decorator",
            Format = "@Body()",
            Documentation = "Marks a function parameter as the request body. Used in REST endpoints with decorator-based parameter binding. Takes no arguments.",
            MinArgs = 0,
            MaxArgs = 0,
            ArgDescriptions = new List<string>()
        }
    };
    
    /// <summary>
    /// Gets all supported decorators with their information.
    /// This is used to provide decorator documentation to the AI system.
    /// </summary>
    public static Dictionary<string, DecoratorInfo> GetSupportedDecorators()
    {
        return SupportedDecorators;
    }
    
    public List<Diagnostic> GetDiagnostics(
        string source,
        string? sourceFileName = null,
        CancellationToken cancellationToken = default,
        StrictTypesOptions? strictTypesOptions = null)
    {
        var diagnostics = new List<Diagnostic>();
        var typeOptions = strictTypesOptions ?? StrictTypesOptions.Default;
        
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lexer = new Lexer(source, sourceFileName);
            var tokens = lexer.Tokenize();
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDeprecatedFunctionKeywordAliases(tokens, diagnostics);

            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            var statements = parser.Parse(); // This will collect errors in parser.Errors
            cancellationToken.ThrowIfCancellationRequested();
            StdLibNamespaceDiagnostics.Validate(statements, diagnostics);
            WorkflowDeterminismDiagnostics.Validate(statements, diagnostics);
            UiLoopDiagnostics.Validate(statements, diagnostics);
            StrictTypesAnalysis.Analyze(statements, typeOptions, diagnostics, sourceFileName);
            
            // Report all parser errors
            foreach (var error in parser.Errors)
            {
                // Extract just the error message (after "Parse error at line X, column Y: ")
                var message = error.Message;
                var colonIndex = message.LastIndexOf(": ");
                if (colonIndex >= 0 && colonIndex < message.Length - 2)
                {
                    message = message.Substring(colonIndex + 2);
                }
                
                var diagnostic = new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = message,
                    Line = error.Line - 1, // 0-based
                    Column = error.Column - 1,
                    Length = 1,
                    Source = !string.IsNullOrEmpty(error.DiagnosticCode) ? error.DiagnosticCode : "parser"
                };
                
                // Try to get autofix suggestion (pass the ParseException for better detection)
                diagnostic.AutoFix = GetAutoFix(source, diagnostic, error, cancellationToken);
                ApplyLearningSupport(diagnostic);
                
                diagnostics.Add(diagnostic);
            }
            
            // Validate decorators
            ValidateDecorators(statements, diagnostics, cancellationToken);
        }
        catch (MaldaLang.Parser.ParseException ex)
        {
            // Fallback for any errors that weren't collected
            var message = ex.Message;
            var colonIndex = message.LastIndexOf(": ");
            if (colonIndex >= 0 && colonIndex < message.Length - 2)
            {
                message = message.Substring(colonIndex + 2);
            }
            
            var diagnostic = new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = message,
                Line = ex.Line > 0 ? ex.Line - 1 : 0, // 0-based
                Column = ex.Column > 0 ? ex.Column - 1 : 0,
                Length = 1,
                Source = !string.IsNullOrEmpty(ex.DiagnosticCode) ? ex.DiagnosticCode : "parser"
            };
            
            // Try to get autofix suggestion (pass the ParseException for better detection)
            diagnostic.AutoFix = GetAutoFix(source, diagnostic, ex, cancellationToken);
            ApplyLearningSupport(diagnostic);
            
            diagnostics.Add(diagnostic);
        }
        catch (OperationCanceledException)
        {
            return diagnostics;
        }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = ex.Message,
                Line = 0,
                Column = 0,
                Length = 1,
                Source = "lexer",
                LearningHint = "The source could not be tokenized cleanly yet. Start with a small runnable example and reintroduce changes one step at a time.",
                SuggestedFix = "Compare your code with a simple beginner example such as Hello World or Variables and Arithmetic.",
                RelatedExamplePath = "Basics/hello_world.malda",
                RelatedExampleTitle = "Hello World",
                RelatedDocumentationPath = "/?lesson=hello-world#lesson-hello-world",
                RelatedDocumentationTitle = "Hello World"
            });
        }
        
        return diagnostics;
    }
    
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
                var hintLexer = new Lexer(source, sourceFileName);
                var hintTokens = hintLexer.Tokenize();
                var hintParser = new MaldaLang.Parser.Parser(hintTokens, sourceFileName);
                var hintStatements = hintParser.Parse();
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
        var keywords = new[] { "if", "else", "while", "for", "foreach", "function", "fn", "def", "return", "var",
            "print", "input", "true", "false", "and", "or", "not", "break", "continue",
            "class", "new", "this", "super", "extends", "public", "private", "static", "null",
            "import", "export", "include", "using", "await",
            "prompt", "schema",
            "workflow", "step", "approval", "wait", "retry", "backoff", "delay", "maxDelay", "compensate", "onReject" };
        
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
            completions.Add(new CompletionItem
            {
                Label = keyword,
                Kind = "keyword",
                InsertText = insertText,
                Detail = detail
            });
        }
        
        // Add ALL built-in functions
        var builtIns = new[] { 
            "int", "float", "string", "abs", "sum", "average", "max", "min", "pow", "sqrt",
            "length", "upper", "lower", "trim", "substring", "indexOf", "replace", "split",
            "normalizeText", "tokenize", "tokenOverlap", "similarity", "extractNumbers",
            "startsWith", "endsWith", "padStart", "padEnd", "repeat",
            "append", "pop", "shift",
            "input", "getEnv", "getCommandLineArgs", "hasEnv", "parseJSON", "toJSON", "parseJson",
            "readFile", "writeFile", "hasFile", "hasDirectory", "listDirectory",
            "replaceInFile", "editFile", "grep", "insertAtLine",
            "createReadFileTool", "createWriteFileTool", "createReplaceInFileTool",
            "createListDirectoryTool", "createAskUserTool", "createGrepTool", "createGlobTool", "createInsertAtLineTool",
            "getSymbols", "createGetSymbolsTool",
            "getParseErrors", "createGetParseErrorsTool",
            "createSubmitPlanTool", "executePlan", "runProgram", "decomposeTask",
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
        
        foreach (var builtIn in builtIns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            completions.Add(new CompletionItem
            {
                Label = builtIn,
                Kind = "function",
                Detail = "Built-in function",
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
            new { Name = "ArduinoConnection", Detail = "ArduinoConnection(url) or ArduinoConnection(port, baudRate)", InsertText = "new ArduinoConnection()" }
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
        
        // Try to parse and extract symbols
        try
        {
            var lexer = new Lexer(source, sourceFileName);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens, sourceFileName);
            var statements = parser.Parse();
            cancellationToken.ThrowIfCancellationRequested();
            
            ExtractSymbols(statements, completions, line, column);
            var imported = ModuleSymbolResolver.LoadImportedSymbols(statements, sourceFileName);
            ExtractImportedSymbols(imported, completions);
        }
        catch
        {
            // Ignore parse errors for completion
        }
        
        return completions.OrderBy(c => c.Label).ToList();
    }

    public SignatureHelpInfo? GetSignatureHelp(string source, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }

            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
            {
                return null;
            }

            var openParen = FindCallOpenParen(lines, line, column);
            if (openParen == null)
            {
                return null;
            }

            var (callLine, callCol, name) = openParen.Value;
            var activeParam = CountCommasBeforePosition(lines, callLine, callCol, line, column);

            List<string>? parameters = null;
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens);
            var statements = parser.Parse();

            parameters = FindFunctionParameters(statements, name) ?? GetBuiltInParameters(name);
            if (parameters == null || parameters.Count == 0)
            {
                return null;
            }

            return new SignatureHelpInfo
            {
                SignatureLabel = $"{name}({string.Join(", ", parameters)})",
                Parameters = parameters,
                ActiveParameter = Math.Min(activeParam, parameters.Count - 1)
            };
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

    private static (int line, int col, string name)? FindCallOpenParen(string[] lines, int line0, int char0)
    {
        var line = line0;
        var col = char0;
        if (line < 0 || line >= lines.Length) return null;
        if (col > lines[line].Length) col = lines[line].Length;

        var depth = 0;
        for (var iter = 0; iter < 2000; iter++)
        {
            if (col <= 0)
            {
                line--;
                if (line < 0) return null;
                col = lines[line].Length;
                continue;
            }

            col--;
            var c = lines[line][col];
            if (c == ')') depth++;
            else if (c == '(')
            {
                if (depth == 0)
                {
                    var parenCol = col;
                    while (col > 0 && (char.IsLetterOrDigit(lines[line][col - 1]) || lines[line][col - 1] == '_'))
                    {
                        col--;
                    }

                    var name = lines[line].Substring(col, parenCol - col).Trim();
                    if (name.Length > 0)
                    {
                        return (line, parenCol, name);
                    }

                    return null;
                }

                depth--;
            }
        }

        return null;
    }

    private static int CountCommasBeforePosition(string[] lines, int openLine, int openCol, int line0, int char0)
    {
        var count = 0;
        var depth = 0;
        for (var line = openLine; line <= line0; line++)
        {
            var currentLine = lines[line];
            var start = line == openLine ? openCol + 1 : 0;
            var end = line == line0 ? char0 : currentLine.Length;
            for (var i = start; i < end && i < currentLine.Length; i++)
            {
                var c = currentLine[i];
                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (depth == 0 && c == ',') count++;
            }
        }

        return count;
    }

    private static List<string>? FindFunctionParameters(List<Statement> statements, string name)
    {
        foreach (var stmt in statements)
        {
            if (stmt is FunctionDeclaration fd && fd.Name == name)
            {
                return fd.Parameters;
            }

            if (stmt is PromptDeclaration pd && pd.Name == name)
            {
                return pd.Parameters;
            }

            if (stmt is ClassDeclaration cd)
            {
                foreach (var member in cd.Members)
                {
                    if (member.Type == MemberType.Method && member.Name == name && member.Value is FunctionDeclaration methodDecl)
                    {
                        return methodDecl.Parameters;
                    }
                }
            }
        }

        return null;
    }

    private static List<string>? GetBuiltInParameters(string name)
    {
        return name switch
        {
            "print" => new List<string> { "value" },
            "getSymbols" => new List<string> { "sourceOrFilePath" },
            "getParseErrors" => new List<string> { "sourceOrFilePath" },
            "createGetParseErrorsTool" => new List<string> { "workingDirectory?" },
            "formatNumber" => new List<string> { "value", "format" },
            "string" => new List<string> { "value" },
            "normalizeText" => new List<string> { "text", "options?" },
            "tokenize" => new List<string> { "text", "options?" },
            "tokenOverlap" => new List<string> { "left", "right", "options?" },
            "similarity" => new List<string> { "left", "right", "method?", "options?" },
            "extractNumbers" => new List<string> { "text" },
            "sleep" => new List<string> { "milliseconds" },
            "runPrompt" => new List<string> { "prompt", "client?", "options?" },
            "withExamples" => new List<string> { "prompt", "examples", "options?" },
            "parseJson" => new List<string> { "value", "schemaRef", "options?" },
            "loadDocuments" => new List<string> { "pattern", "dirPath?" },
            "splitDocuments" => new List<string> { "documents", "chunkSize?", "overlap?" },
            "formatRetrievedDocs" => new List<string> { "documents" },
            "composePipe" => new List<string> { "step1", "step2", "..." },
            "parallelRun" => new List<string> { "input", "branches" },
            "mergeRetrievedDocs" => new List<string> { "docArrays..." },
            "indexInto" => new List<string> { "vectorDb", "documents" },
            "uiOnInit" => new List<string> { "componentId", "sessionId?" },
            "uiOnPreRender" => new List<string> { "componentId", "sessionId?" },
            "uiOnLoad" => new List<string> { "componentId", "sessionId?" },
            "uiOnDispose" => new List<string> { "componentId", "sessionId?" },
            "uiOnMount" => new List<string> { "componentId", "sessionId?" },
            "uiOnUpdate" => new List<string> { "componentId", "sessionId?" },
            "uiOnUnmount" => new List<string> { "componentId", "sessionId?" },
            "uiOnError" => new List<string> { "componentId", "sessionId?" },
            "onInit" => new List<string> { "componentId", "sessionId?" },
            "onPreRender" => new List<string> { "componentId", "sessionId?" },
            "onLoad" => new List<string> { "componentId", "sessionId?" },
            "onDispose" => new List<string> { "componentId", "sessionId?" },
            "onMount" => new List<string> { "componentId", "sessionId?" },
            "onUpdate" => new List<string> { "componentId", "sessionId?" },
            "onUnmount" => new List<string> { "componentId", "sessionId?" },
            "onError" => new List<string> { "componentId", "sessionId?" },
            _ => null
        };
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
        
        // First, try to find the variable type if objectName is a variable
        string? resolvedType = null;
        try
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens);
            var statements = parser.Parse();
            
            var varType = FindVariableType(statements, objectName, line);
            if (varType != null)
            {
                resolvedType = varType;
            }
        }
        catch
        {
            // Ignore parse errors
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
        else if (typeToCheck == "Agent" || typeToCheck == "CodingAgent" || typeToCheck == "GitAgent" || typeToCheck == "DevAgent" || typeToCheck == "HumanAgent")
        {
            members.Add(new CompletionItem { Label = "name", Kind = "property", Detail = "string", InsertText = "name" });
            members.Add(new CompletionItem { Label = "role", Kind = "property", Detail = "string", InsertText = "role" });
            members.Add(new CompletionItem { Label = "instructions", Kind = "property", Detail = "string", InsertText = "instructions" });
            members.Add(new CompletionItem { Label = "think", Kind = "method", Detail = "think(prompt)", InsertText = "think()" });
            members.Add(new CompletionItem { Label = "addTool", Kind = "method", Detail = "addTool(tool)", InsertText = "addTool()" });
            members.Add(new CompletionItem { Label = "getConversation", Kind = "method", Detail = "getConversation()", InsertText = "getConversation()" });
            members.Add(new CompletionItem { Label = "reset", Kind = "method", Detail = "reset()", InsertText = "reset()" });
        }
        else if (typeToCheck == "Tool")
        {
            members.Add(new CompletionItem { Label = "name", Kind = "property", Detail = "string", InsertText = "name" });
            members.Add(new CompletionItem { Label = "description", Kind = "property", Detail = "string", InsertText = "description" });
            members.Add(new CompletionItem { Label = "getSchema", Kind = "method", Detail = "getSchema()", InsertText = "getSchema()" });
            members.Add(new CompletionItem { Label = "execute", Kind = "method", Detail = "execute(arguments)", InsertText = "execute()" });
            members.Add(new CompletionItem { Label = "describe", Kind = "method", Detail = "describe()", InsertText = "describe()" });
        }
        else if (typeToCheck == "this")
        {
            // Try to find the current class and its members
            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                var parser = new MaldaLang.Parser.Parser(tokens);
                var statements = parser.Parse();
                
                // Find the class we're in
                var currentClass = FindClassAtPosition(statements, line);
                if (currentClass != null)
                {
                    foreach (var member in currentClass.Members)
                    {
                        if (member.Type == MaldaLang.Parser.AST.Declarations.MemberType.Method)
                        {
                            var funcDecl = member.Value as MaldaLang.Parser.AST.Declarations.FunctionDeclaration;
                            if (funcDecl != null)
                            {
                                members.Add(new CompletionItem
                                {
                                    Label = member.Name,
                                    Kind = "method",
                                    Detail = $"function {member.Name}({string.Join(", ", funcDecl.Parameters)})",
                                    InsertText = member.Name + "()"
                                });
                            }
                        }
                        else if (member.Type == MaldaLang.Parser.AST.Declarations.MemberType.Field)
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
            catch
            {
                // Ignore parse errors
            }
        }
        else if (typeToCheck == "Array")
        {
            AddArrayMembers(members);
        }
        else
        {
            // Try to find user-defined class members
            try
            {
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                var parser = new MaldaLang.Parser.Parser(tokens);
                var statements = parser.Parse();
                
                // Find class definition
                var classDecl = FindClassDeclaration(statements, typeToCheck);
                if (classDecl != null)
                {
                    foreach (var member in classDecl.Members)
                    {
                        if (member.Type == MaldaLang.Parser.AST.Declarations.MemberType.Method)
                        {
                            var funcDecl = member.Value as MaldaLang.Parser.AST.Declarations.FunctionDeclaration;
                            if (funcDecl != null)
                            {
                                members.Add(new CompletionItem
                                {
                                    Label = member.Name,
                                    Kind = "method",
                                    Detail = $"function {member.Name}({string.Join(", ", funcDecl.Parameters)})",
                                    InsertText = member.Name + "()"
                                });
                            }
                        }
                        else if (member.Type == MaldaLang.Parser.AST.Declarations.MemberType.Field)
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
            catch
            {
                // Ignore parse errors
            }
        }
        
        return members;
    }
    
    private MaldaLang.Parser.AST.Declarations.ClassDeclaration? FindClassAtPosition(
        List<MaldaLang.Parser.AST.Statements.Statement> statements, int line)
    {
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
            {
                // Check if we're inside this class (simple heuristic: check if line is after class declaration)
                if (classDecl.Line <= line + 1)
                {
                    return classDecl;
                }
            }
        }
        return null;
    }
    
    private string? FindVariableType(List<MaldaLang.Parser.AST.Statements.Statement> statements, 
        string varName, int line)
    {
        // Look for variable declaration before the current line
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Statements.VarDeclStatement varDecl && 
                varDecl.Name == varName && stmt.Line <= line + 1)
            {
                return InferExpressionType(statements, varDecl.Initializer, line);
            }
        }
        return null;
    }

    private string? InferExpressionType(List<MaldaLang.Parser.AST.Statements.Statement> statements, Expression? expression, int line)
    {
        if (expression == null)
            return null;

        switch (expression)
        {
            case NewExpression newExpr:
                return newExpr.ClassName;
            case ArrayLiteralExpression:
                return "Array";
            case IdentifierExpression identifier:
                return FindVariableType(statements, identifier.Name, line);
            case FunctionCallExpression functionCall:
                return InferFunctionCallType(statements, functionCall, line);
            default:
                return null;
        }
    }

    private string? InferFunctionCallType(List<MaldaLang.Parser.AST.Statements.Statement> statements, FunctionCallExpression functionCall, int line)
    {
        if (functionCall.Callee is IdentifierExpression identifier)
        {
            if (IsArrayReturningBuiltInFunction(identifier.Name))
                return "Array";

            var functionDecl = statements.OfType<FunctionDeclaration>().FirstOrDefault(f => f.Name == identifier.Name);
            if (functionDecl != null)
            {
                if (string.Equals(functionDecl.ReturnType, "Array", StringComparison.OrdinalIgnoreCase))
                    return "Array";

                return InferFunctionReturnType(statements, functionDecl, line);
            }
        }

        if (functionCall.Callee is MemberAccessExpression memberAccess)
        {
            var receiverType = InferExpressionType(statements, memberAccess.Object, line);
            if (receiverType == "Array" && IsArrayReturningArrayMethod(memberAccess.Member))
                return "Array";
        }

        return null;
    }

    private string? InferFunctionReturnType(List<MaldaLang.Parser.AST.Statements.Statement> statements, FunctionDeclaration functionDecl, int line)
    {
        foreach (var stmt in functionDecl.Body.Statements)
        {
            if (stmt is ReturnStatement returnStmt)
            {
                var returnType = InferExpressionType(statements, returnStmt.Value, line);
                if (returnType != null)
                    return returnType;
            }
        }

        return null;
    }

    private static bool IsArrayReturningBuiltInFunction(string name)
    {
        return name is "split" or "regexFind" or "listDirectory" or "range" or "reverse" or "sort" or "softmax" or "getSkillNames";
    }

    private static bool IsArrayReturningArrayMethod(string name)
    {
        return name is "concat" or "map" or "filter" or "sort" or "reverse" or "slice";
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
    
    private MaldaLang.Parser.AST.Declarations.ClassDeclaration? FindClassDeclaration(
        List<MaldaLang.Parser.AST.Statements.Statement> statements, string className)
    {
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl && 
                classDecl.Name == className)
            {
                return classDecl;
            }
        }
        return null;
    }
    
    private void ExtractSymbols(List<MaldaLang.Parser.AST.Statements.Statement> statements, 
        List<CompletionItem> completions, int line, int column)
    {
        foreach (var stmt in statements)
        {
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = classDecl.Name,
                    Kind = "class",
                    Detail = "Class",
                    InsertText = classDecl.Name
                });
            }
            else if (stmt is MaldaLang.Parser.AST.Declarations.FunctionDeclaration funcDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = funcDecl.Name,
                    Kind = "function",
                    Detail = $"function {funcDecl.Name}({string.Join(", ", funcDecl.Parameters)})",
                    InsertText = funcDecl.Name + "()"
                });
            }
            else if (stmt is MaldaLang.Parser.AST.Declarations.PromptDeclaration promptDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = promptDecl.Name,
                    Kind = "function",
                    Detail = $"prompt {promptDecl.Name}({string.Join(", ", promptDecl.Parameters)})",
                    InsertText = promptDecl.Name + "()"
                });
            }
            else if (stmt is WorkflowDeclaration workflowDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = workflowDecl.Name,
                    Kind = "function",
                    Detail = $"workflow {workflowDecl.Name}({string.Join(", ", workflowDecl.Parameters)})",
                    InsertText = workflowDecl.Name + "()"
                });
            }
            else if (stmt is MaldaLang.Parser.AST.Statements.VarDeclStatement varDecl)
            {
                completions.Add(new CompletionItem
                {
                    Label = varDecl.Name,
                    Kind = "variable",
                    Detail = "Variable",
                    InsertText = varDecl.Name
                });
            }
        }
    }
    
    public string? GetHoverInformation(string source, int line, int column, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new MaldaLang.Parser.Parser(tokens);
            var statements = parser.Parse();
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
            
            // Look up symbol information
            return GetSymbolInfo(statements, token.Lexeme);
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
    
    private Token? FindTokenAtPosition(List<Token> tokens, int line, int column)
    {
        return tokens.FirstOrDefault(t => t.Line == line && t.Column <= column && 
            t.Column + t.Lexeme.Length >= column);
    }
    
    private static string? GetKeywordHoverInfo(Token token, string source, int line, int column, List<Statement>? statements = null)
    {
        return token.Type switch
        {
            TokenType.Await => "**await** — Await async results, including pipe steps with `runPrompt`.\n\n`var text = await (prompt() |> runPrompt(client));`",
            TokenType.Foreach => "**foreach** — Iterate over each element in an array.\n\n`foreach (var item in collection) { ... }`\n\nSame as `for (var item in collection)`.",
            TokenType.For => "**for** — Loop: traditional (init; condition; increment) or for-in over array.\n\n`for (var i = 0; i < n; i = i + 1) { ... }`\n`for (var x in array) { ... }`",
            TokenType.While => "**while** — Loop while condition is true.\n\n`while (condition) { ... }`",
            TokenType.If => "**if** — Conditional execution.\n\n`if (condition) { ... } else { ... }`",
            TokenType.Workflow => "**workflow** — Durable workflow declaration.\n\n`workflow Name(input) { ... }`",
            TokenType.Prompt => "**prompt** — Reusable LLM prompt template.\n\n`prompt Name(params) -> Type? { system: \"...\", user: \"...\" }`",
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
            _ => null
        };
    }
    
    private string? GetSymbolInfo(List<MaldaLang.Parser.AST.Statements.Statement> statements, string name)
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
            if (stmt is MaldaLang.Parser.AST.Declarations.ClassDeclaration classDecl && 
                classDecl.Name == name)
            {
                return $"class {classDecl.Name}";
            }
            if (stmt is WorkflowDeclaration workflowDecl &&
                workflowDecl.Name == name)
            {
                return $"workflow {workflowDecl.Name}({string.Join(", ", workflowDecl.Parameters)})";
            }
        }
        return null;
    }
    
    private string? GetBuiltInFunctionInfo(string name)
    {
        return name switch
        {
            "getSymbols" => "function getSymbols(sourceOrFilePath: string) -> object\nParses MALDA code and extracts structured symbol information (classes, functions, actors, prompts) with line numbers and signatures. Accepts file path or source string. Returns an object with 'classes', 'functions', 'actors', 'prompts', and 'parseErrors' arrays.",
            "createGetSymbolsTool" => "function createGetSymbolsTool(workingDirectory?: string) -> Tool\nCreates a tool for parsing MALDA code and extracting symbol information.",
            "getParseErrors" => "function getParseErrors(sourceOrFilePath: string) -> object\nParses MALDA code and returns only parse errors (line, column, message). Accepts file path or source string. Use to validate syntax without running or compiling.",
            "createGetParseErrorsTool" => "function createGetParseErrorsTool(workingDirectory?: string) -> Tool\nCreates a tool that parses MALDA code and returns only parse errors (line, column, message).",
            "createSubmitPlanTool" => "function createSubmitPlanTool() -> Tool\nCreates a tool that agents can call to submit a structured plan (steps with id, description, optional dependsOn). Parameters: plan or steps, optional taskSummary. Returns { accepted, planId?, stepCount?, error? }.",
            "executePlan" => "function executePlan(plan: object, agent: Agent) -> object\nValidates the plan, topo-sorts steps by dependsOn, then runs agent.think(step.description) for each step. Returns { planId, completed, failed, results }.",
            "runProgram" => "function runProgram(program: object) -> any\nRuns a validated program from await prompt(...) -> program(Api) (or equivalent JSON). Calls top-level functions named like api methods; no LLM.",
            "decomposeTask" => "function decomposeTask(instruction: string, client?: LLMClient) -> object\nUses an LLM to break a high-level task into a structured plan. Returns { steps, planId?, taskSummary? } or { error }.",
            "runPrompt" => "function runPrompt(prompt, client?, options?) -> string\nRuns a PromptInstance through an LLM. Options: `{ onToken: fn, onReasoning: fn }` for streaming callbacks.",
            "withExamples" => "function withExamples(prompt, examples, options?) -> PromptInstance\nReturns a copy of a prompt with runtime few-shot examples. Use `{ merge: true }` to append after static prompt examples.",
            "parseJson" => "function parseJson(value, schemaRef, options?) -> object\nParses and validates JSON against a schema declaration.",
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
            _ => null
        };
    }
    
    private string? GetDecoratorContext(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;
            
            var currentLine = lines[line];
            if (column < 0)
                return null;
            
            // Column is 0-based and points to the position after the last typed character
            // With full source text, the line should include all typed characters
            
            // Look backwards from cursor position to find @
            // Start from column - 1, but clamp to valid line bounds
            int searchStart = column > 0 ? Math.Min(column - 1, currentLine.Length - 1) : (currentLine.Length > 0 ? currentLine.Length - 1 : 0);
            if (searchStart < 0) return null;
            
            for (int i = searchStart; i >= 0; i--)
            {
                if (currentLine[i] == '@')
                {
                    // Found @, now check if cursor is within or right after the decorator name
                    int nameStart = i + 1;
                    
                    // If cursor is at or past nameStart, we're in decorator context
                    // This handles:
                    // - cursor right after @ (column == nameStart) -> return ""
                    // - cursor within the name (column > nameStart) -> return partial name
                    if (column >= nameStart)
                    {
                        // Extract partial name if any (from @ to cursor position)
                        if (nameStart < column && nameStart < currentLine.Length)
                        {
                            int extractLength = Math.Min(column - nameStart, currentLine.Length - nameStart);
                            if (extractLength > 0)
                            {
                                return currentLine.Substring(nameStart, extractLength);
                            }
                        }
                        return ""; // Just @, no name yet or cursor is right after @
                    }
                    break;
                }
                if (!char.IsWhiteSpace(currentLine[i]) && currentLine[i] != '@')
                {
                    // Hit a non-whitespace, non-@ character before finding @
                    break;
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return null;
    }
    
    private static string? GetImportStringPathContext(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;

            var rawLine = lines[line];
            var trimmed = rawLine.TrimStart();
            if (!trimmed.StartsWith("import", StringComparison.Ordinal))
                return null;

            var quoteIndex = trimmed.IndexOf('"');
            if (quoteIndex < 0)
                return null;

            var lineStartOffset = rawLine.Length - trimmed.Length;
            var quotePosInRaw = lineStartOffset + quoteIndex;
            if (column <= quotePosInRaw)
                return null;

            var afterQuote = trimmed.Substring(quoteIndex + 1);
            var cursorInTrimmed = column - lineStartOffset;
            var cursorAfterQuote = cursorInTrimmed - (quoteIndex + 1);
            if (cursorAfterQuote < 0 || cursorAfterQuote > afterQuote.Length)
                return null;

            var partial = afterQuote.Substring(0, Math.Min(cursorAfterQuote, afterQuote.Length));
            if (partial.Contains('"'))
                return null;

            return partial;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetModulePackageImportContext(string source, int line, int column)
    {
        try
        {
            var lines = source.Split('\n');
            if (line < 0 || line >= lines.Length)
                return null;

            var currentLine = lines[line].TrimStart();
            string? keyword = null;
            if (currentLine.StartsWith("import", StringComparison.Ordinal))
                keyword = "import";
            else if (currentLine.StartsWith("using", StringComparison.Ordinal))
                keyword = "using";
            else
                return null;

            if (currentLine.Contains('"'))
                return null;

            if (column < keyword.Length)
                return null;

            var afterKeyword = currentLine.Substring(keyword.Length).TrimStart();
            if (afterKeyword.StartsWith('='))
                return null;

            var cursorPosInLine = column - (lines[line].Length - currentLine.Length);
            var cursorPosInKeyword = cursorPosInLine - keyword.Length;
            if (cursorPosInKeyword < 0 || cursorPosInKeyword > afterKeyword.Length)
                return null;

            var partialName = afterKeyword.Substring(0, Math.Min(cursorPosInKeyword, afterKeyword.Length));
            partialName = partialName.TrimEnd(' ', '\t', ';');
            if (partialName.Contains('='))
            {
                var eq = partialName.IndexOf('=');
                partialName = partialName[(eq + 1)..].TrimStart();
            }

            return partialName;
        }
        catch
        {
            return null;
        }
    }

    private static List<CompletionItem> GetRelativeMaldaFileCompletions(string? sourceFileName, string partialPath)
    {
        var completions = new List<CompletionItem>();
        try
        {
            var baseDir = !string.IsNullOrWhiteSpace(sourceFileName)
                ? Path.GetDirectoryName(Path.GetFullPath(sourceFileName))
                : Environment.CurrentDirectory;
            if (string.IsNullOrWhiteSpace(baseDir))
                baseDir = Environment.CurrentDirectory;

            var searchDir = baseDir;
            if (!string.IsNullOrWhiteSpace(partialPath))
            {
                var dirPart = Path.GetDirectoryName(partialPath);
                if (!string.IsNullOrWhiteSpace(dirPart))
                    searchDir = Path.GetFullPath(Path.Combine(baseDir, dirPart));
            }

            if (!Directory.Exists(searchDir))
                return completions;

            foreach (var file in Directory.EnumerateFiles(searchDir, "*.malda"))
            {
                var name = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(partialPath) &&
                    !name.StartsWith(Path.GetFileName(partialPath), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                completions.Add(new CompletionItem
                {
                    Label = name,
                    Kind = "file",
                    Detail = "Malda module",
                    InsertText = name
                });
            }
        }
        catch
        {
            // ignore
        }

        return completions;
    }

    private static void ExtractImportedSymbols(ModuleSymbolResolver.ImportedSymbolSet imported, List<CompletionItem> completions)
    {
        foreach (var funcDecl in imported.Functions)
        {
            completions.Add(new CompletionItem
            {
                Label = funcDecl.Name,
                Kind = "function",
                Detail = $"imported function {funcDecl.Name}({string.Join(", ", funcDecl.Parameters)})",
                InsertText = funcDecl.Name + "()"
            });
        }

        foreach (var classDecl in imported.Classes)
        {
            completions.Add(new CompletionItem
            {
                Label = classDecl.Name,
                Kind = "class",
                Detail = "imported class",
                InsertText = classDecl.Name
            });
        }

        foreach (var varDecl in imported.Variables)
        {
            completions.Add(new CompletionItem
            {
                Label = varDecl.Name,
                Kind = "variable",
                Detail = "imported variable",
                InsertText = varDecl.Name
            });
        }
    }
    
    private List<CompletionItem> GetPackageCompletions(string partialName)
    {
        var completions = new List<CompletionItem>();
        
        try
        {
            var storage = new PackageStorage();
            var packages = storage.GetInstalledPackages();
            
            // Filter packages that match partial name
            var matchingPackages = packages.Where(p => 
                string.IsNullOrEmpty(partialName) || 
                p.StartsWith(partialName, StringComparison.OrdinalIgnoreCase));
            
            foreach (var packageName in matchingPackages)
            {
                var versions = storage.GetInstalledVersions(packageName);
                if (versions.Length > 0)
                {
                    var metadata = storage.LoadPackageMetadata(packageName, versions[0]);
                    var description = metadata?.Description ?? "Package";
                    
                    completions.Add(new CompletionItem
                    {
                        Label = packageName,
                        Kind = "package",
                        Detail = description,
                        InsertText = packageName
                    });
                }
            }
            
            // Also add common .NET namespaces
            var dotNetNamespaces = new[]
            {
                "System", "System.Collections.Generic", "System.Linq", 
                "System.IO", "System.Text", "System.Threading"
            };
            
            foreach (var ns in dotNetNamespaces)
            {
                if (string.IsNullOrEmpty(partialName) || 
                    ns.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                {
                    completions.Add(new CompletionItem
                    {
                        Label = ns,
                        Kind = "namespace",
                        Detail = ".NET namespace",
                        InsertText = ns
                    });
                }
            }
        }
        catch
        {
            // If package storage fails, return empty list
        }
        
        return completions;
    }
    
    private List<CompletionItem> GetDecoratorCompletions(string? partialName)
    {
        var completions = new List<CompletionItem>();
        
        foreach (var decorator in SupportedDecorators.Values)
        {
            // Filter by partial name if provided
            if (partialName != null && !decorator.Name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
                continue;
            
            var insertText = decorator.Format;
            // If we have a partial name, replace it in the insert text
            if (!string.IsNullOrEmpty(partialName) && decorator.Name.StartsWith(partialName, StringComparison.OrdinalIgnoreCase))
            {
                insertText = decorator.Format.Replace(decorator.Name, partialName + decorator.Name.Substring(partialName.Length));
            }
            
            completions.Add(new CompletionItem
            {
                Label = decorator.Name,
                Kind = "decorator",
                Detail = decorator.Description,
                Documentation = decorator.Documentation,
                InsertText = insertText
            });
        }
        
        return completions;
    }
    
    private string? GetDecoratorHoverInfo(string source, int line, int column, Token token)
    {
        // Check if the token is a decorator name
        if (SupportedDecorators.TryGetValue(token.Lexeme, out var decoratorInfo))
        {
            // Verify we're actually in a decorator context (after @)
            var decoratorContext = GetDecoratorContext(source, line, column);
            if (decoratorContext != null)
            {
                var info = $"{decoratorInfo.Description}\n\nFormat: {decoratorInfo.Format}";
                if (!string.IsNullOrEmpty(decoratorInfo.Documentation))
                {
                    info += $"\n\n{decoratorInfo.Documentation}";
                }
                if (decoratorInfo.ArgDescriptions.Count > 0)
                {
                    info += "\n\nArguments:";
                    for (int i = 0; i < decoratorInfo.ArgDescriptions.Count; i++)
                    {
                        info += $"\n  {i + 1}. {decoratorInfo.ArgDescriptions[i]}";
                    }
                }
                if (decoratorInfo.MinArgs != decoratorInfo.MaxArgs)
                {
                    info += $"\n\nRequired arguments: {decoratorInfo.MinArgs}";
                    if (decoratorInfo.MaxArgs > decoratorInfo.MinArgs)
                    {
                        info += $", optional up to {decoratorInfo.MaxArgs}";
                    }
                }
                else if (decoratorInfo.MinArgs > 0)
                {
                    info += $"\n\nRequired arguments: {decoratorInfo.MinArgs}";
                }
                return info;
            }
        }
        return null;
    }
    
    private static void ValidateDeprecatedFunctionKeywordAliases(List<Token> tokens, List<Diagnostic> diagnostics)
    {
        foreach (var token in tokens)
        {
            if (token.Type != TokenType.Function)
                continue;

            if (token.Lexeme is not ("fn" or "def"))
                continue;

            diagnostics.Add(new Diagnostic
            {
                Severity = DiagnosticSeverity.Warning,
                Message = $"Prefer 'function' instead of '{token.Lexeme}' (deprecated alias).",
                Line = token.Line - 1,
                Column = token.Column - 1,
                Length = token.Lexeme.Length,
                Source = "malda-style"
            });
        }
    }

    private void ValidateDecorators(List<MaldaLang.Parser.AST.Statements.Statement> statements, List<Diagnostic> diagnostics, CancellationToken cancellationToken = default)
    {
        foreach (var stmt in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stmt is FunctionDeclaration funcDecl && funcDecl.Decorators != null)
            {
                foreach (var decorator in funcDecl.Decorators)
                {
                    // Check if decorator is supported
                    if (!SupportedDecorators.TryGetValue(decorator.Name, out var decoratorInfo))
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Warning,
                            Message = $"Unknown decorator '@{decorator.Name}'. Supported decorators: {string.Join(", ", SupportedDecorators.Keys.Select(k => "@" + k))}",
                            Line = decorator.Line - 1,
                            Column = decorator.Column - 1,
                            Length = decorator.Name.Length,
                            Source = "decorator"
                        });
                        continue;
                    }
                    
                    // Validate argument count
                    var argCount = decorator.Arguments?.Count ?? 0;
                    if (argCount < decoratorInfo.MinArgs)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Message = $"@{decorator.Name} requires at least {decoratorInfo.MinArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                            Line = decorator.Line - 1,
                            Column = decorator.Column - 1,
                            Length = decorator.Name.Length,
                            Source = "decorator"
                        });
                    }
                    else if (decoratorInfo.MaxArgs > 0 && argCount > decoratorInfo.MaxArgs)
                    {
                        diagnostics.Add(new Diagnostic
                        {
                            Severity = DiagnosticSeverity.Error,
                            Message = $"@{decorator.Name} accepts at most {decoratorInfo.MaxArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                            Line = decorator.Line - 1,
                            Column = decorator.Column - 1,
                            Length = decorator.Name.Length,
                            Source = "decorator"
                        });
                    }
                }
            }
            
            // Also check class methods for decorators
            if (stmt is ClassDeclaration classDecl)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member.Type == MaldaLang.Parser.AST.Declarations.MemberType.Method && member.Value is FunctionDeclaration methodDecl && methodDecl.Decorators != null)
                    {
                        foreach (var decorator in methodDecl.Decorators)
                        {
                            if (!SupportedDecorators.TryGetValue(decorator.Name, out var decoratorInfo))
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Warning,
                                    Message = $"Unknown decorator '@{decorator.Name}'. Supported decorators: {string.Join(", ", SupportedDecorators.Keys.Select(k => "@" + k))}",
                                    Line = decorator.Line - 1,
                                    Column = decorator.Column - 1,
                                    Length = decorator.Name.Length,
                                    Source = "decorator"
                                });
                                continue;
                            }
                            
                            var argCount = decorator.Arguments?.Count ?? 0;
                            if (argCount < decoratorInfo.MinArgs)
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Error,
                                    Message = $"@{decorator.Name} requires at least {decoratorInfo.MinArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                                    Line = decorator.Line - 1,
                                    Column = decorator.Column - 1,
                                    Length = decorator.Name.Length,
                                    Source = "decorator"
                                });
                            }
                            else if (decoratorInfo.MaxArgs > 0 && argCount > decoratorInfo.MaxArgs)
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Severity = DiagnosticSeverity.Error,
                                    Message = $"@{decorator.Name} accepts at most {decoratorInfo.MaxArgs} argument(s), but {argCount} provided. Format: {decoratorInfo.Format}",
                                    Line = decorator.Line - 1,
                                    Column = decorator.Column - 1,
                                    Length = decorator.Name.Length,
                                    Source = "decorator"
                                });
                            }
                        }
                    }
                }
            }
        }
    }
    
    public AutoFixInfo? GetAutoFix(string source, Diagnostic diagnostic, MaldaLang.Parser.ParseException? parseException = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Only autofix parser errors with missing characters
        if (diagnostic.Source != "parser")
        {
            return null;
        }
        
        // First, try to use ExpectedType from ParseException if available
        if (parseException != null && parseException.ExpectedType.HasValue)
        {
            var expectedType = parseException.ExpectedType.Value;
            var expectedTypeStr = expectedType.ToString();
            var charToken = TokenTypeToCharacter(expectedTypeStr);
            
            if (charToken != null && IsSimpleCharacterToken(charToken))
            {
                // Determine insertion position
                var insertColumn = diagnostic.Column + diagnostic.Length;
                
                // If we're replacing (ActualType is different), replace at current position
                if (parseException.ActualType.HasValue && parseException.ActualType.Value != expectedType)
                {
                    insertColumn = diagnostic.Column;
                }
                
                return new AutoFixInfo
                {
                    Description = $"Insert missing '{charToken}'",
                    Line = diagnostic.Line,
                    Column = insertColumn,
                    TextToInsert = charToken,
                    LengthToReplace = parseException.ActualType.HasValue && parseException.ActualType.Value != expectedType ? diagnostic.Length : 0,
                    IsSimpleCharacterFix = true
                };
            }
        }
        
        var message = diagnostic.Message;
        
        // Check for "Expect 'X' after/before ..." pattern (e.g., "Expect '}' after object properties.")
        if (message.Contains("Expect '"))
        {
            var expectIndex = message.IndexOf("Expect '");
            if (expectIndex >= 0)
            {
                var startQuote = expectIndex + 8; // Length of "Expect '"
                var endQuote = message.IndexOf("'", startQuote);
                
                if (endQuote > startQuote)
                {
                    var expectedToken = message.Substring(startQuote, endQuote - startQuote);
                    var charToken = TokenTypeToCharacter(expectedToken);
                    
                    // Check if it's a simple character (brace, bracket, parenthesis, semicolon)
                    if (charToken != null && IsSimpleCharacterToken(charToken))
                    {
                        // For "Expect 'X' after/before ..." patterns, the parser column points to where
                        // it encountered the error. For "after" messages, the parser has already advanced
                        // past where we should insert, so we need to insert at the previous position.
                        // For "before" messages, we insert at the current position.
                        var insertColumn = diagnostic.Column;
                        
                        // Check if message contains " after " - if so, the parser is one position ahead
                        if (message.Contains(" after ") || message.Contains("after "))
                        {
                            // Parser has advanced past where we should insert, so go back one position
                            insertColumn = Math.Max(0, diagnostic.Column - 1);
                        }
                        
                        return new AutoFixInfo
                        {
                            Description = $"Insert missing '{charToken}'",
                            Line = diagnostic.Line,
                            Column = insertColumn,
                            TextToInsert = charToken,
                            LengthToReplace = 0,
                            IsSimpleCharacterFix = true
                        };
                    }
                }
            }
        }
        
        // Check for "Missing 'X'" or "Expected 'X'" patterns
        if (message.StartsWith("Missing '") || message.StartsWith("Expected '"))
        {
            // Extract the expected token from message
            var startQuote = message.IndexOf("'") + 1;
            var endQuote = message.IndexOf("'", startQuote);
            
            if (startQuote > 0 && endQuote > startQuote)
            {
                var expectedToken = message.Substring(startQuote, endQuote - startQuote);
                var charToken = TokenTypeToCharacter(expectedToken);
                
                // Check if it's a simple character (brace, bracket, parenthesis, semicolon)
                if (charToken != null && IsSimpleCharacterToken(charToken))
                {
                    return new AutoFixInfo
                    {
                        Description = $"Insert missing '{charToken}'",
                        Line = diagnostic.Line,
                        Column = diagnostic.Column + diagnostic.Length,
                        TextToInsert = charToken,
                        LengthToReplace = 0,
                        IsSimpleCharacterFix = true
                    };
                }
            }
        }
        
        // Check for "Expected 'X' but found 'Y'" pattern
        if (message.Contains("Expected '") && message.Contains(" but found '"))
        {
            var expectedStart = message.IndexOf("Expected '") + 10;
            var expectedEnd = message.IndexOf("'", expectedStart);
            
            if (expectedStart > 0 && expectedEnd > expectedStart)
            {
                var expectedToken = message.Substring(expectedStart, expectedEnd - expectedStart);
                var charToken = TokenTypeToCharacter(expectedToken);
                
                // Only autofix if expected token is simple character
                if (charToken != null && IsSimpleCharacterToken(charToken))
                {
                    return new AutoFixInfo
                    {
                        Description = $"Insert '{charToken}' instead of unexpected token",
                        Line = diagnostic.Line,
                        Column = diagnostic.Column,
                        TextToInsert = charToken,
                        LengthToReplace = diagnostic.Length,
                        IsSimpleCharacterFix = true
                    };
                }
            }
        }
        
        // Try to detect missing closing braces by analyzing the code structure
        var missingBraceFix = DetectMissingClosingBrace(source, diagnostic);
        if (missingBraceFix != null)
        {
            return missingBraceFix;
        }
        
        return null;
    }
    
    private string? TokenTypeToCharacter(string tokenType)
    {
        // Map TokenType enum names to their character representations
        return tokenType switch
        {
            "RightBrace" => "}",
            "LeftBrace" => "{",
            "RightParen" => ")",
            "LeftParen" => "(",
            "RightBracket" => "]",
            "LeftBracket" => "[",
            "Semicolon" => ";",
            "Comma" => ",",
            "Dot" => ".",
            "Colon" => ":",
            // Also handle if it's already a character
            "}" => "}",
            "{" => "{",
            ")" => ")",
            "(" => "(",
            "]" => "]",
            "[" => "[",
            ";" => ";",
            "," => ",",
            "." => ".",
            ":" => ":",
            _ => null
        };
    }
    
    private bool IsSimpleCharacterToken(string token)
    {
        // Return true for tokens that are single characters or simple multi-character
        // tokens that we can safely auto-insert
        return token == ";" || token == "{" || token == "}" || 
               token == "(" || token == ")" || 
               token == "[" || token == "]" ||
               token == "," || token == "." || token == ":";
    }
    
    private AutoFixInfo? DetectMissingClosingBrace(string source, Diagnostic diagnostic)
    {
        // Only check for missing closing braces if the error is near the end of the file
        // or if the message suggests a brace-related issue
        var message = diagnostic.Message.ToLower();
        if (!message.Contains("brace") && !message.Contains("}") && !message.Contains("block"))
        {
            return null;
        }
        
        var lines = source.Split('\n');
        if (diagnostic.Line >= lines.Length) return null;
        
        // Count opening and closing braces from the error line to the end
        int openBraces = 0;
        int closeBraces = 0;
        
        for (int i = diagnostic.Line; i < lines.Length; i++)
        {
            var line = lines[i];
            for (int j = (i == diagnostic.Line ? diagnostic.Column : 0); j < line.Length; j++)
            {
                if (line[j] == '{') openBraces++;
                if (line[j] == '}') closeBraces++;
            }
        }
        
        // If we have unmatched opening braces, suggest adding a closing brace at the end
        if (openBraces > closeBraces)
        {
            var lastLine = lines.Length - 1;
            var lastLineText = lines[lastLine];
            var insertColumn = lastLineText.Length;
            
            // Skip trailing whitespace
            while (insertColumn > 0 && char.IsWhiteSpace(lastLineText[insertColumn - 1]))
            {
                insertColumn--;
            }
            
            return new AutoFixInfo
            {
                Description = "Insert missing closing brace '}'",
                Line = lastLine,
                Column = insertColumn,
                TextToInsert = "}",
                LengthToReplace = 0,
                IsSimpleCharacterFix = true
            };
        }
        
        return null;
    }

    private static void ApplyLearningSupport(Diagnostic diagnostic)
    {
        var message = diagnostic.Message.ToLowerInvariant();

        if (diagnostic.Source == "decorator" || message.Contains("decorator") || message.Contains("@get") || message.Contains("@post"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Decorators are part of MALDA's API and UI features. They work best after you are comfortable with plain functions.",
                suggestedFix: "Check the decorator format and compare it with a runnable REST or UI example.",
                relatedExamplePath: "Web/rest_api_server.malda",
                relatedExampleTitle: "REST API Server",
                relatedDocumentationPath: "/?lesson=build-ai-apps#lesson-build-ai-apps",
                relatedDocumentationTitle: "Build AI Apps");
            return;
        }

        if (message.Contains("prompt") || message.Contains("agent"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Prompts and agents build on the same function-style thinking used in the beginner lessons.",
                suggestedFix: "Open the First Prompt or First Agent starter and compare the structure with your code.",
                relatedExamplePath: "Prompts/basic_prompt.malda",
                relatedExampleTitle: "Basic Prompt",
                relatedDocumentationPath: "/?lesson=first-prompt#lesson-first-prompt",
                relatedDocumentationTitle: "First Prompt");
            return;
        }

        if (message.Contains("if") || message.Contains("else"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Conditionals need a complete condition and a block for each branch you want to run.",
                suggestedFix: "Recheck the condition, parentheses, and braces around the if/else blocks.",
                relatedExamplePath: "Basics/conditionals.malda",
                relatedExampleTitle: "Conditionals",
                relatedDocumentationPath: "/?lesson=conditionals#lesson-conditionals",
                relatedDocumentationTitle: "Conditionals");
            return;
        }

        if (message.Contains("while") || message.Contains("for") || message.Contains("loop"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Loops repeat code, so they usually need a valid condition and a block surrounded by braces.",
                suggestedFix: "Compare your loop header and braces with the While Loop or For Loop examples.",
                relatedExamplePath: "Basics/while_loop.malda",
                relatedExampleTitle: "While Loop",
                relatedDocumentationPath: "/?lesson=loops#lesson-loops",
                relatedDocumentationTitle: "Loops");
            return;
        }

        if (message.Contains("function") || message.Contains("return") || message.Contains("parameter"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Functions let you name reusable steps. In MALDA they need parentheses after the name and braces around the body.",
                suggestedFix: "Compare your declaration with the Functions example and verify each parameter is separated correctly.",
                relatedExamplePath: "Basics/functions.malda",
                relatedExampleTitle: "Functions",
                relatedDocumentationPath: "/?lesson=functions#lesson-functions",
                relatedDocumentationTitle: "Functions");
            return;
        }

        if (message.Contains("input"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Input is read as text first, then often converted to numbers before calculations.",
                suggestedFix: "Check the Input Example to confirm where conversion happens and how values are printed.",
                relatedExamplePath: "Basics/input_example.malda",
                relatedExampleTitle: "Input Example",
                relatedDocumentationPath: "/?lesson=input-output#lesson-input-output",
                relatedDocumentationTitle: "Input and Output");
            return;
        }

        if (message.Contains("';'") || message.Contains("semicolon") || message.Contains("expected ';'"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "MALDA statements usually end with a semicolon so the parser knows where one statement stops and the next begins.",
                suggestedFix: "Add the missing semicolon and rerun, or compare your line with Hello World or Variables and Arithmetic.",
                relatedExamplePath: "Basics/variables_arithmetic.malda",
                relatedExampleTitle: "Variables and Arithmetic",
                relatedDocumentationPath: "/?lesson=variables#lesson-variables",
                relatedDocumentationTitle: "Variables and Arithmetic");
            return;
        }

        if (message.Contains("}") || message.Contains("{") || message.Contains("brace") || message.Contains("block"))
        {
            SetLearningSupport(
                diagnostic,
                hint: "Blocks group code together. Every opening brace should eventually have a matching closing brace.",
                suggestedFix: "Look for the nearest missing or extra brace and compare the shape of your code with a basic function or loop example.",
                relatedExamplePath: "Basics/functions.malda",
                relatedExampleTitle: "Functions",
                relatedDocumentationPath: "/?lesson=functions#lesson-functions",
                relatedDocumentationTitle: "Functions");
            return;
        }

        SetLearningSupport(
            diagnostic,
            hint: "When a parser error feels abstract, the fastest way forward is to compare your code with a very small working example.",
            suggestedFix: diagnostic.AutoFix != null
                ? diagnostic.AutoFix.Description
                : "Start from Hello World or Variables and Arithmetic, then reapply your changes one step at a time.",
            relatedExamplePath: "Basics/hello_world.malda",
            relatedExampleTitle: "Hello World",
            relatedDocumentationPath: "/?lesson=hello-world#lesson-hello-world",
            relatedDocumentationTitle: "Hello World");
    }

    private static void SetLearningSupport(
        Diagnostic diagnostic,
        string hint,
        string suggestedFix,
        string relatedExamplePath,
        string relatedExampleTitle,
        string relatedDocumentationPath,
        string relatedDocumentationTitle)
    {
        diagnostic.LearningHint = hint;
        diagnostic.SuggestedFix = diagnostic.AutoFix != null
            ? $"{suggestedFix} Suggested autofix: {diagnostic.AutoFix.Description}."
            : suggestedFix;
        diagnostic.RelatedExamplePath = relatedExamplePath;
        diagnostic.RelatedExampleTitle = relatedExampleTitle;
        diagnostic.RelatedDocumentationPath = relatedDocumentationPath;
        diagnostic.RelatedDocumentationTitle = relatedDocumentationTitle;
    }
}