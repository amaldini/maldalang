// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.BuiltIns;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;

namespace MaldaLang.Compiler;

/// <summary>
/// One rule of the JavaScript capability pre-check: a stable code and the row of
/// <c>docs/spec/backend-capability-matrix.md</c> it enforces.
/// </summary>
/// <param name="Code">Stable diagnostic code (<c>JS0xxx</c>).</param>
/// <param name="Feature">Short feature name used in messages.</param>
/// <param name="MatrixRows">
/// Leading text of each capability-matrix row the rule enforces: a product-feature row, or a
/// property-test capability tag in backticks (e.g. <c>`file-io`</c>).
/// </param>
public sealed record JsCapabilityRule(string Code, string Feature, IReadOnlyList<string> MatrixRows);

public sealed record JsCapabilityDiagnostic(
    string Code,
    string Feature,
    string Message,
    int Line,
    int Column,
    string? SourceFile)
{
    public override string ToString()
    {
        var location = Line > 0 ? $"Line {Line}:{Math.Max(1, Column)}" : "Line ?";
        var file = string.IsNullOrEmpty(SourceFile) ? string.Empty : System.IO.Path.GetFileName(SourceFile) + " ";
        return $"{file}{location} - {Code}: {Message}";
    }
}

/// <summary>Raised before JavaScript emit when the program uses host-only features.</summary>
public sealed class JsCapabilityException : Exception
{
    public IReadOnlyList<JsCapabilityDiagnostic> Diagnostics { get; }

    public JsCapabilityException(IReadOnlyList<JsCapabilityDiagnostic> diagnostics)
        : base(FormatMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string FormatMessage(IReadOnlyList<JsCapabilityDiagnostic> diagnostics)
    {
        var header = $"The JavaScript backend does not support {diagnostics.Count} feature use(s) in this program "
                     + "(see docs/spec/backend-capability-matrix.md):";
        return header + Environment.NewLine + string.Join(Environment.NewLine, diagnostics.Select(d => "  " + d));
    }
}

/// <summary>
/// Single AST pass run before JavaScript emit. It reports every use of a feature that
/// the capability matrix marks <c>no</c> for JavaScript, with line and column, instead of
/// stopping at the first <see cref="NotSupportedException"/> in <see cref="JsTranspiler"/>
/// (which stays as a safety net) or emitting a call that fails at runtime in the browser.
/// Names the program declares itself (functions, classes, variables, parameters) are not
/// reported, so a user <c>function readFile()</c> is fine.
/// </summary>
public static class JsCapabilityChecker
{
    public static readonly JsCapabilityRule FileIo = new("JS0001", "file I/O", ["File I/O", "`file-io`"]);
    public static readonly JsCapabilityRule Workflows = new("JS0002", "durable workflows", ["Durable workflows", "`workflows`"]);
    public static readonly JsCapabilityRule Prompts = new("JS0003", "prompts", ["Agents / prompts / MCP / ACP", "Typed prompt `response_format`"]);
    public static readonly JsCapabilityRule Agents = new("JS0004", "agents / LLM clients / MCP / ACP", ["Agents / prompts / MCP / ACP"]);
    public static readonly JsCapabilityRule VectorDb = new("JS0005", "VectorDB", ["VectorDB"]);
    public static readonly JsCapabilityRule Onnx = new("JS0006", "ONNX models", ["ONNX inspect"]);
    public static readonly JsCapabilityRule HttpServer = new("JS0007", "HttpServer / RestServer", ["HttpServer / RestServer / sessions"]);
    public static readonly JsCapabilityRule UiHost = new("JS0008", "UIHost / ui.*", ["UIHost / `ui.*`"]);
    public static readonly JsCapabilityRule Jobs = new("JS0009", "jobs", ["Jobs"]);
    public static readonly JsCapabilityRule DotNetInterop = new("JS0010", ".NET interop", [".NET interop", "`dotnet-interop`"]);
    public static readonly JsCapabilityRule HostInterop = new("JS0011", "host processes and drivers", ["`host-interop`"]);
    public static readonly JsCapabilityRule ClosedApi = new("JS0012", "closed api / runProgram", ["Closed `api`"]);
    public static readonly JsCapabilityRule AgenticRuns = new("JS0013", "agentic run constructs", ["`within` scopes", "Run journal"]);
    public static readonly JsCapabilityRule HostStdLib = new("JS0014", "host-only stdlib helpers", ["Host-only stdlib helpers"]);

    /// <summary>Every rule, in code order.</summary>
    public static IReadOnlyList<JsCapabilityRule> Rules { get; } =
    [
        FileIo, Workflows, Prompts, Agents, VectorDb, Onnx, HttpServer, UiHost, Jobs,
        DotNetInterop, HostInterop, ClosedApi, AgenticRuns, HostStdLib
    ];

    private static readonly Dictionary<string, JsCapabilityRule> FlatBuiltIns = BuildFlatBuiltIns();

    private static readonly Dictionary<string, JsCapabilityRule> HostClasses = new(StringComparer.Ordinal)
    {
        ["VectorDB"] = VectorDb,
        ["OnnxModel"] = Onnx,
        ["HttpServer"] = HttpServer,
        ["RestServer"] = HttpServer,
        ["HTMLCache"] = HttpServer,
        ["LLMClient"] = Agents,
        ["OpenRouterClient"] = Agents,
        ["LlamaCppClient"] = Agents,
        ["LlamaEmbedder"] = Agents,
        ["Conversation"] = Agents,
        ["Tool"] = Agents,
        ["Agent"] = Agents,
        ["CodingAgent"] = Agents,
        ["GitAgent"] = Agents,
        ["DevAgent"] = Agents,
        ["MALDACodingAgent"] = Agents,
        ["HumanAgent"] = Agents,
        ["GraphMemory"] = Agents,
        ["MCPServer"] = Agents,
        ["MCPClient"] = Agents,
        ["ACPClient"] = Agents,
        ["ACPServer"] = Agents,
        ["ACPAgentTool"] = Agents,
        ["LLMClientBridge"] = Agents,
        ["LLMServer"] = Agents,
        ["SqlServerClient"] = HostInterop,
        ["PostgresClient"] = HostInterop,
        ["SqliteClient"] = HostInterop,
        ["SerialConnection"] = HostInterop,
        ["ArduinoConnection"] = HostInterop
    };

    /// <summary><c>io.*</c> members the JavaScript runtime provides; the other io members are file I/O.</summary>
    private static readonly HashSet<string> JsIoMembers = new(StringComparer.Ordinal)
    {
        "print", "input", "getEnv", "getEnvOr", "hasEnv"
    };

    /// <summary>Property-test capability tags the JavaScript backend runs (see BackendCapabilityMatrix).</summary>
    private static readonly HashSet<string> JsPropertyCapabilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "core", "web-dom", "game-canvas"
    };

    /// <summary>Checks the statements that will be emitted as JavaScript and returns every violation, sorted by position.</summary>
    public static IReadOnlyList<JsCapabilityDiagnostic> Check(IReadOnlyList<Statement> statements)
    {
        var declared = CollectDeclaredNames(statements);
        var diagnostics = new List<JsCapabilityDiagnostic>();

        foreach (var statement in statements)
        {
            if (statement is PropertyDeclaration property && !PropertyRunsOnJavaScript(property))
            {
                continue;
            }

            if (IsReportedAsWhole(statement))
            {
                // Reported once; the body of a host-only declaration is not inspected.
                CheckNode(statement, declared, diagnostics);
                continue;
            }

            foreach (var node in AstNodeWalker.Descendants(statement, descendInto: n => !IsReportedAsWhole(n)))
            {
                CheckNode(node, declared, diagnostics);
            }
        }

        return diagnostics
            .GroupBy(d => (d.Code, d.Line, d.Column, d.SourceFile, d.Message))
            .Select(g => g.First())
            .OrderBy(d => d.SourceFile ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .ThenBy(d => d.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Declarations that are reported once; their bodies are not inspected.</summary>
    private static bool IsReportedAsWhole(Node node) =>
        node is PromptDeclaration or WorkflowDeclaration or ApiDeclaration or SuiteDeclaration
            or ContextDeclaration or PolicyDeclaration;

    private static void CheckNode(Node node, HashSet<string> declared, List<JsCapabilityDiagnostic> diagnostics)
    {
        switch (node)
        {
            case PromptDeclaration prompt:
                Add(diagnostics, Prompts, prompt, $"prompt '{prompt.Name}' is host-only (interpreter and C# transpile)");
                break;
            case WorkflowDeclaration workflow:
                Add(diagnostics, Workflows, workflow, $"workflow '{workflow.Name}' is host-only (interpreter and C# transpile)");
                break;
            case WorkflowStepStatement or WorkflowApprovalStatement or WorkflowAwaitSignalStatement:
                Add(diagnostics, Workflows, node, "workflow steps, approvals and signals are host-only");
                break;
            case ApiDeclaration api:
                Add(diagnostics, ClosedApi, api, $"closed api '{api.Name}' is host-only (interpreter and C# transpile)");
                break;
            case SuiteDeclaration suite:
                Add(diagnostics, AgenticRuns, suite, $"suite \"{suite.Title}\" runs under malda eval on a host backend");
                break;
            case ContextDeclaration context:
                Add(diagnostics, AgenticRuns, context, $"context '{context.Name}' is host-only");
                break;
            case PolicyDeclaration policy:
                Add(diagnostics, AgenticRuns, policy, "policy blocks are host-only");
                break;
            case WithinStatement within:
                Add(diagnostics, AgenticRuns, within, "within (…) { } scopes are host-only; @within(ms) on a function is supported");
                break;
            case GraphLiteralExpression graph:
                Add(diagnostics, Agents, graph, "graph literals (agent team topologies) are host-only");
                break;
            case NewExpression newExpression
                when HostClasses.TryGetValue(newExpression.ClassName, out var classRule) && !declared.Contains(newExpression.ClassName):
                Add(diagnostics, classRule, newExpression, $"new {newExpression.ClassName}(…) is host-only");
                break;
            // Calls are located at the callee (the call node itself sits on its closing parenthesis).
            case FunctionCallExpression { Callee: IdentifierExpression callee } call:
                CheckFlatCall(callee.Name, LocatedAt(callee, call), declared, diagnostics);
                break;
            case FunctionCallExpression { Callee: MemberAccessExpression { Object: IdentifierExpression module } member } call:
                CheckModuleCall(module.Name, member.Member, LocatedAt(module, call), declared, diagnostics);
                break;
            case PipeExpression { Right: IdentifierExpression pipedName } pipe:
                CheckFlatCall(pipedName.Name, pipe, declared, diagnostics);
                break;
        }
    }

    private static void CheckFlatCall(string name, Node site, HashSet<string> declared, List<JsCapabilityDiagnostic> diagnostics)
    {
        if (declared.Contains(name) || !FlatBuiltIns.TryGetValue(name, out var rule))
        {
            return;
        }

        Add(diagnostics, rule, site, $"{name}(…) is host-only");
    }

    private static void CheckModuleCall(string module, string member, Node site, HashSet<string> declared, List<JsCapabilityDiagnostic> diagnostics)
    {
        if (declared.Contains(module))
        {
            return;
        }

        JsCapabilityRule? rule = module switch
        {
            StdLibNamespaces.IoModule when StdLibNamespaces.IoMethodNames.Contains(member) && !JsIoMembers.Contains(member) => FileIo,
            StdLibNamespaces.PdfModule or StdLibNamespaces.DocModule => FileIo,
            StdLibNamespaces.AgentsModule => Agents,
            "ui" => UiHost,
            "AnsiConsole" => HostStdLib,
            _ => null
        };

        if (rule != null)
        {
            Add(diagnostics, rule, site, $"{module}.{member}(…) is host-only");
        }
    }

    private static Node LocatedAt(Node preferred, Node fallback) => preferred.Line > 0 ? preferred : fallback;

    private static void Add(List<JsCapabilityDiagnostic> diagnostics, JsCapabilityRule rule, Node node, string detail)
    {
        diagnostics.Add(new JsCapabilityDiagnostic(
            rule.Code,
            rule.Feature,
            $"{detail}; JavaScript does not support {rule.Feature}.",
            node.Line,
            node.Column,
            node.SourceFile));
    }

    private static bool PropertyRunsOnJavaScript(PropertyDeclaration property)
    {
        var modes = property.GetTargetModes();
        if (!modes.Any(mode => string.Equals(mode, "js", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return property.GetRequiredCapabilities().All(JsPropertyCapabilities.Contains);
    }

    private static HashSet<string> CollectDeclaredNames(IReadOnlyList<Statement> statements)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in statements.SelectMany(statement => AstNodeWalker.Descendants(statement)))
        {
            switch (node)
            {
                case FunctionDeclaration function:
                    names.Add(function.Name);
                    names.UnionWith(function.Parameters);
                    break;
                case ClassDeclaration classDeclaration:
                    names.Add(classDeclaration.Name);
                    names.UnionWith(classDeclaration.Members.Select(member => member.Name));
                    break;
                case ActorDeclaration actor:
                    names.Add(actor.Name);
                    break;
                case VarDeclStatement variable:
                    names.Add(variable.Name);
                    break;
                case ForInStatement forIn:
                    names.Add(forIn.VariableName);
                    break;
                case UsingResourceStatement usingResource:
                    names.Add(usingResource.VariableName);
                    break;
                case LambdaExpression lambda:
                    names.UnionWith(lambda.Parameters);
                    break;
                case IdentifierPattern pattern:
                    names.Add(pattern.Name);
                    break;
                case CatchClause { ExceptionVariable: { } exceptionVariable }:
                    names.Add(exceptionVariable);
                    break;
            }
        }

        return names;
    }

    private static Dictionary<string, JsCapabilityRule> BuildFlatBuiltIns()
    {
        var map = new Dictionary<string, JsCapabilityRule>(StringComparer.Ordinal);

        void AddAll(JsCapabilityRule rule, params string[] names)
        {
            foreach (var name in names)
            {
                map[name] = rule;
            }
        }

        AddAll(FileIo,
            "readFile", "readTextFileLines", "writeFile", "copyFile", "appendFile", "writeFileBase64", "readFileBase64",
            "writePng", "readPng", "hasFile", "fileExists", "deleteFile", "hasDirectory", "directoryExists",
            "createDirectory", "ensureDir", "listDirectory", "hasEmbeddedFolder", "embeddedFolderRoot",
            "glob", "grep", "replaceInFile", "editFile", "insertAtLine",
            "gitStatus", "gitAdd", "gitCommit", "gitDiff", "gitLog", "gitBranch", "gitCheckout", "gitPush", "gitPull",
            "getFileName", "getDirectoryName", "pathExists", "pathJoin", "pathNormalize", "pathGetExtension", "isPathUnder",
            "embedFromFile", "embedFromFiles", "loadDocuments", "extractPdfText", "extractDocxText");

        AddAll(Workflows,
            "startWorkflow", "getWorkflowStatus", "getWorkflow", "getWorkflowSteps", "getWorkflowEvents",
            "getWorkflowMetrics", "listWorkflows", "listWorkflowDeadLetters", "requeueDeadLetter", "cancelWorkflow",
            "resumeWorkflow", "retryWorkflow", "approveWorkflowStep", "signalWorkflow", "runWorkflowInstance");

        AddAll(Prompts, "runPrompt", "evalPrompt", "parallelRun");

        AddAll(Agents,
            "executePlan", "decomposeTask", "setDefaultAgent", "onAgentProgress", "clearAgentProgress", "cancelThink",
            "enableAgentVerboseLogging", "setAgentVerbosePhase", "setAgentStatusBanner", "createMcpAgentScript",
            "getAssistantMemory", "reportRalphStatus", "getSkillNames", "loadSkill", "loadSkillsFromDir",
            "splitDocuments", "formatRetrievedDocs", "composePipe", "mergeRetrievedDocs", "withExamples", "indexInto",
            "embedBagOfWords", "embedCharacterNGrams", "embedHash", "embedTFIDF", "webFetch", "webSearch");

        AddAll(HttpServer,
            "renderTemplate", "componentFragment", "componentLiveEmit", "componentStateGet", "componentStateSet",
            "componentStateObject", "componentStateClear", "componentStateConfigure", "componentStatePin",
            "componentStateUnpin", "redirect", "RedirectTo", "httpBearerToken", "httpCookieToken", "httpAuthToken",
            "generateCsrfToken", "verifyCsrfToken", "csrfField", "formErrors", "bindForm", "pageLayout",
            "createSecureCookie", "readSecureCookie");

        // Pure-looking helpers that need a host library (Markdig, HTML parsing, PBKDF2 / HMAC)
        // or block on tasks; the rest of math / str / collections / JSON runs on JavaScript.
        AddAll(HostStdLib,
            "markdownToHtml", "extractHTML", "hashPassword", "verifyPassword", "createJwt", "verifyJwt",
            "race", "firstOk", "stream");

        AddAll(Jobs, "enqueueJob", "claimJob", "completeJob", "failJob", "getJob", "listJobs");

        AddAll(DotNetInterop, "loadAssembly", "getDotNetType", "dotnetNew", "loadNativeModule", "createNativeCallback");

        AddAll(HostInterop,
            "runCommand", "runMALDA", "compileMALDA", "checkMalda", "getSymbols", "getParseErrors", "exit",
            "getMaldaHome", "getProgramDirectory", "getMaldaConfig", "getHostPlatform", "getCommandLineArgs");

        AddAll(ClosedApi, "runProgram");

        AddAll(UiHost, "uiGenerate", "generateUI");
        AddAll(UiHost, UiBuiltIns);
        AddAll(Agents, AgentToolBuiltIns);

        return map;
    }

    // Expression-bodied so the FlatBuiltIns initializer above can read them (field initializers run in order).
    /// <summary>The <c>uiXxx</c> builtins of the server-driven UI host.</summary>
    private static string[] UiBuiltIns =>
    [
        "uiRow", "uiColumn", "uiStack", "uiSpacer", "uiPanel", "uiText", "uiHeading", "uiImage", "uiIcon", "uiButton",
        "uiTextField", "uiCheckbox", "uiSelect", "uiSlider", "uiDatePicker", "uiList", "uiTable", "uiAlert", "uiProgress",
        "uiModal", "uiForm", "uiField", "uiTextArea", "uiRadioGroup", "uiSwitch", "uiTabs", "uiAccordion", "uiBreadcrumbs",
        "uiDrawer", "uiDataGrid", "uiTreeView", "uiPaginator", "uiEmptyState", "uiBadge", "uiToast", "uiSkeleton",
        "uiSpinner", "uiErrorBoundary", "uiSlot", "uiWithSlot", "uiWhen", "uiChoose", "uiEach", "uiTemplate", "uiPartial",
        "uiLayout", "uiRenderList", "uiCrudModel", "uiCrudControls", "uiCrudSchema", "uiMount", "uiMountEnvelope",
        "uiRender", "uiDispatchEvent", "uiPullEvent", "uiState", "uiGetState", "uiSetState", "uiPinState", "uiUnpinState",
        "uiInvalidate", "uiOnInit", "uiOnPreRender", "uiOnLoad", "uiOnDispose", "uiOnMount", "uiOnUpdate", "uiOnUnmount",
        "uiOnError", "uiConfigure", "uiSnapshot", "uiResync", "uiSessionId", "uiRedirectWithSession"
    ];

    /// <summary>The <c>createXxxTool</c> builtins that build agent tools.</summary>
    private static string[] AgentToolBuiltIns =>
    [
        "createReadFileTool", "createWriteFileTool", "createReplaceInFileTool", "createListDirectoryTool",
        "createDeleteFileTool", "createCopyFileTool", "createEnsureDirTool", "createCheckMaldaTool",
        "createValidateJsonTool", "createAskUserTool", "createGrepTool", "createGlobTool", "createInsertAtLineTool",
        "createEditFileTool", "createGitStatusTool", "createGitAddTool", "createGitCommitTool", "createGitLogTool",
        "createGitDiffTool", "createGitBranchTool", "createGitCheckoutTool", "createGitPushTool", "createGitPullTool",
        "createRunCommandTool", "createWebSearchTool", "createWebFetchTool", "createRunMALDATool", "createTestMaldaTool",
        "createCompileMALDATool", "createGetSymbolsTool", "createGetParseErrorsTool", "createCreateMcpAgentScriptTool",
        "createSubmitPlanTool", "createUpdatePlanTool", "createMarkStepTool"
    ];
}
