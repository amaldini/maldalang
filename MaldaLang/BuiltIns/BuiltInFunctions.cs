// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    /// <summary>Windows treats env var names as case-insensitive; other platforms are case-sensitive.</summary>
    private static readonly StringComparer EnvVarNameComparer =
        System.Environment.OSVersion.Platform == PlatformID.Win32NT
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>First resolved value wins for the process lifetime (hot paths call getEnv millions of times).</summary>
    private static readonly ConcurrentDictionary<string, string?> GetEnvCache = new(EnvVarNameComparer);

    private static readonly object UiTemplateCacheLock = new();
    private static readonly Dictionary<string, string> UiTemplateCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Func<RuntimeValue, string, Task<RuntimeValue?>>> TranspiledWorkflowRunners = new(StringComparer.Ordinal);
    private static readonly AsyncLocal<int> TranspiledWorkflowDepth = new();
    private static readonly AsyncLocal<int> TranspiledWorkflowStepDepth = new();

    public sealed class TranspiledWorkflowPauseException : Exception
    {
        public TranspiledWorkflowPauseException(string message) : base(message) { }
    }

    public static void RegisterTranspiledWorkflowRunner(string workflowName, Func<RuntimeValue, string, Task<RuntimeValue?>> runner)
    {
        lock (TranspiledWorkflowRunners)
        {
            TranspiledWorkflowRunners[workflowName] = runner;
        }
    }

    public static void ClearTranspiledWorkflowRunners()
    {
        lock (TranspiledWorkflowRunners)
        {
            TranspiledWorkflowRunners.Clear();
        }
    }

    public static void EnterTranspiledWorkflowContext() => TranspiledWorkflowDepth.Value = TranspiledWorkflowDepth.Value + 1;
    public static void ExitTranspiledWorkflowContext() => TranspiledWorkflowDepth.Value = Math.Max(0, TranspiledWorkflowDepth.Value - 1);
    public static void EnterTranspiledWorkflowStep() => TranspiledWorkflowStepDepth.Value = TranspiledWorkflowStepDepth.Value + 1;
    public static void ExitTranspiledWorkflowStep() => TranspiledWorkflowStepDepth.Value = Math.Max(0, TranspiledWorkflowStepDepth.Value - 1);

    public static void RegisterBuiltIns(MaldaLang.Interpreter.Environment env)
    {
        // Built-ins are handled specially in the interpreter
        // We just need to mark them as available
        // The actual implementation is in CallBuiltIn
        
        // Configure Spectre.Console for non-terminal output (e.g., when running in IDE)
        // When output is redirected (like in IDE), disable ANSI codes for plain text output
        try
        {
            // Check if output is redirected or not a terminal
            if (System.Console.IsOutputRedirected || !AnsiConsole.Profile.Capabilities.IsTerminal)
            {
                // Force plain text output for IDE/non-terminal environments
                // Setting Ansi = false disables all ANSI codes including colors
                AnsiConsole.Profile.Capabilities.Ansi = false;
                AnsiConsole.Profile.Capabilities.Unicode = false;
            }
        }
        catch
        {
            // If detection fails, assume non-terminal and disable ANSI
            AnsiConsole.Profile.Capabilities.Ansi = false;
            AnsiConsole.Profile.Capabilities.Unicode = false;
        }
        
        // Register AnsiConsole as a global singleton instance
        var ansiConsole = new AnsiConsoleInstance();
        env.Define("AnsiConsole", RuntimeValue.Object(ansiConsole));
        
        // Register ui as a global singleton instance for server-driven UI composition.
        var uiFramework = new UiFrameworkInstance();
        env.Define("ui", RuntimeValue.Object(uiFramework));

        // math.* namespace (canonical); Math is a deprecated alias for one release
        var math = new MathInstance();
        math.Set("PI", RuntimeValue.Float(System.Math.PI));
        math.Set("E", RuntimeValue.Float(System.Math.E));
        math.Set("TAU", RuntimeValue.Float(2 * System.Math.PI));
        math.Set("INF", RuntimeValue.Float(double.PositiveInfinity));
        math.Set("NaN", RuntimeValue.Float(double.NaN));
        env.Define(StdLibNamespaces.MathModule, RuntimeValue.Object(math));
        env.Define(StdLibNamespaces.DeprecatedMathModuleAlias, RuntimeValue.Object(math));

        env.Define(StdLibNamespaces.StrModule, RuntimeValue.Object(new StrInstance()));
        env.Define(StdLibNamespaces.IoModule, RuntimeValue.Object(new IoInstance()));
        env.Define(StdLibNamespaces.PdfModule, RuntimeValue.Object(new PdfInstance()));
        env.Define(StdLibNamespaces.DocModule, RuntimeValue.Object(new DocInstance()));
        env.Define(StdLibNamespaces.ResultModule, RuntimeValue.Object(new ResultInstance()));
        env.Define(StdLibNamespaces.OptionModule, RuntimeValue.Object(new OptionInstance()));
        env.Define(StdLibNamespaces.GroundedModule, RuntimeValue.Object(new GroundedInstance()));
        env.Define(StdLibNamespaces.CapModule, RuntimeValue.Object(new CapInstance()));
        env.Define(StdLibNamespaces.AgentsModule, RuntimeValue.Object(new AgentsInstance()));
        env.Define(StdLibNamespaces.TraceModule, RuntimeValue.Object(new TraceInstance()));
        env.Define(StdLibNamespaces.NnModule, RuntimeValue.Object(new NnInstance()));
        AgentErrorStdLib.BindGlobals(env);
    }

    public static RuntimeValue CallBuiltIn(string name, List<RuntimeValue> args, Interpreter? interpreter)
    {
        CheckWorkflowDeterminism(name, interpreter);
        var profileToken = MaldaProfiler.EnterBuiltIn(name);
        try
        {
            return DispatchBuiltIn(name, args, interpreter);
        }
        finally
        {
            MaldaProfiler.Exit(profileToken);
        }
    }

    /// <summary>
    /// The one name → implementation table. <see cref="CallBuiltIn"/> and
    /// <see cref="CallBuiltInAsync"/> both end here; the async entry only adds the
    /// built-ins whose implementation awaits.
    /// </summary>
    private static RuntimeValue DispatchBuiltIn(string name, List<RuntimeValue> args, Interpreter? interpreter)
    {
        return name switch
        {
            "int" => BuiltInInt(args),
            "toIntOr" => BuiltInToIntOr(args),
            "toIntOrNull" => BuiltInToIntOrNull(args),
            "float" => BuiltInFloat(args),
            "string" => BuiltInString(args),
            "formatNumber" => BuiltInFormatNumber(args),
            "abs" => BuiltInAbs(args),
            "sum" => BuiltInSum(args),
            "average" => BuiltInAverage(args),
            "max" => BuiltInMax(args),
            "min" => BuiltInMin(args),
            "pow" => BuiltInPow(args),
            "sqrt" => BuiltInSqrt(args),
            // Extended math: rounding and sign
            "floor" => BuiltInFloor(args),
            "ceil" => BuiltInCeil(args),
            "round" => BuiltInRound(args),
            "trunc" => BuiltInTrunc(args),
            "sign" => BuiltInSign(args),
            // Extended math: exponential and logarithm
            "exp" => BuiltInExp(args),
            "log" => BuiltInLog(args),
            "log10" => BuiltInLog10(args),
            "log2" => BuiltInLog2(args),
            // Extended math: trigonometry
            "sin" => BuiltInSin(args),
            "cos" => BuiltInCos(args),
            "tan" => BuiltInTan(args),
            "asin" => BuiltInAsin(args),
            "acos" => BuiltInAcos(args),
            "atan" => BuiltInAtan(args),
            "atan2" => BuiltInAtan2(args),
            // Extended math: utility
            "hypot" => BuiltInHypot(args),
            "clamp" => BuiltInClamp(args),
            "degToRad" => BuiltInDegToRad(args),
            "radToDeg" => BuiltInRadToDeg(args),
            "zeros" => BuiltInZeros(args),
            // LLM-oriented math helpers
            "rsqrt" => BuiltInRsqrt(args),
            "randn" => BuiltInRandn(args),
            "argmax" => BuiltInArgmax(args),
            "argmin" => BuiltInArgmin(args),
            "logSumExp" => BuiltInLogSumExp(args),
            "softmax" => BuiltInSoftmax(args),
            "crossEntropyFromLogits" => BuiltInCrossEntropyFromLogits(args),
            "dot" => MathNeural.Dot(args),
            "matmul" => MathNeural.MatMul(args),
            "transpose" => MathNeural.Transpose(args),
            "sigmoid" => MathNeural.Sigmoid(args),
            "tanh" => MathNeural.Tanh(args),
            "randomChoiceWeighted" => BuiltInRandomChoiceWeighted(args),
            "seed" => BuiltInSeed(args),
            "anneal" => MathAnneal.Run(args, interpreter),
            "length" => BuiltInLength(args),
            "upper" => BuiltInUpper(args),
            "lower" => BuiltInLower(args),
            "trim" => BuiltInTrim(args),
            "text" => BuiltInText(args),
            "trimText" => BuiltInTrimText(args),
            "substring" => BuiltInSubstring(args),
            "indexOf" => BuiltInIndexOf(args),
            "replace" => BuiltInReplace(args),
            "split" => BuiltInSplit(args),
            "normalizeText" => BuiltInNormalizeText(args),
            "tokenize" => BuiltInTokenize(args),
            "tokenOverlap" => BuiltInTokenOverlap(args),
            "similarity" => BuiltInSimilarity(args),
            "extractNumbers" => BuiltInExtractNumbers(args),
            "regexMatch" => BuiltInRegexMatch(args),
            "regexReplace" => BuiltInRegexReplace(args),
            "regexFind" => BuiltInRegexFind(args),
            "getFileName" => BuiltInGetFileName(args),
            "getDirectoryName" => BuiltInGetDirectoryName(args),
            "getMaldaHome" => BuiltInGetMaldaHome(args),
            "getProgramDirectory" => BuiltInGetProgramDirectory(args, interpreter),
            "getMaldaConfig" => BuiltInGetMaldaConfig(args),
            "getAssistantMemory" => BuiltInGetAssistantMemory(args, interpreter),
            "enableAgentVerboseLogging" => BuiltInEnableAgentVerboseLogging(args),
            "setAgentVerbosePhase" => BuiltInSetAgentVerbosePhase(args),
            "setAgentStatusBanner" => BuiltInSetAgentStatusBanner(args),
            "reportRalphStatus" => BuiltInReportRalphStatus(args),
            "getSkillNames" => BuiltInGetSkillNames(args),
            "loadSkill" => BuiltInLoadSkill(args, interpreter),
            "loadSkillsFromDir" => BuiltInLoadSkillsFromDir(args, interpreter),
            "print" => BuiltInPrint(args, interpreter),
            "input" => throw new Exception("input() must be called via CallBuiltInAsync"),
            "sleep" => throw new Exception("sleep() must be called via CallBuiltInAsync"),
            "domQuery" => BuiltInDomUnavailable("domQuery"),
            "domCreate" => BuiltInDomUnavailable("domCreate"),
            "domAppend" => BuiltInDomUnavailable("domAppend"),
            "domClear" => BuiltInDomUnavailable("domClear"),
            "domSetText" => BuiltInDomUnavailable("domSetText"),
            "domHtml" => BuiltInDomUnavailable("domHtml"),
            "domOn" => BuiltInDomUnavailable("domOn"),
            "getEnv" => BuiltInGetEnv(args),
            "getEnvOr" => BuiltInGetEnvOr(args),
            "getHostPlatform" => BuiltInGetHostPlatform(args),
            "getCommandLineArgs" => BuiltInGetCommandLineArgs(args),
            "hasEnv" => BuiltInHasEnv(args),
            "parseJSON" => BuiltInParseJSON(args),
            "parseJson" => BuiltInParseJsonTyped(args, interpreter),
            "runPrompt" => throw new Exception("runPrompt() must be called via CallBuiltInAsync"),
            "loadDocuments" => BuiltInLoadDocuments(args),
            "splitDocuments" => BuiltInSplitDocuments(args),
            "formatRetrievedDocs" => BuiltInFormatRetrievedDocs(args),
            "composePipe" => BuiltInComposePipe(args, interpreter),
            "parallelRun" => throw new Exception("parallelRun() must be called via CallBuiltInAsync"),
            "mergeRetrievedDocs" => BuiltInMergeRetrievedDocs(args),
            "withExamples" => BuiltInWithExamples(args),
            "indexInto" => BuiltInIndexInto(args, interpreter),
            "toJSON" => BuiltInToJSON(args),
            "loadNativeModule" => BuiltInLoadNativeModule(args),
            "createNativeCallback" => BuiltInCreateNativeCallback(args, interpreter),
            "readFile" => BuiltInReadFile(args),
            "readTextFileLines" => BuiltInReadTextFileLines(args),
            "writeFile" => BuiltInWriteFile(args),
            "copyFile" => BuiltInCopyFile(args),
            "writeFileBase64" => BuiltInWriteFileBase64(args),
            "readFileBase64" => BuiltInReadFileBase64(args),
            "writePng" => BuiltInWritePng(args),
            "readPng" => BuiltInReadPng(args),
            "hasFile" => BuiltInHasFile(args),
            "deleteFile" => BuiltInDeleteFile(args),
            "hasDirectory" => BuiltInHasDirectory(args),
            "ensureDir" => BuiltInEnsureDir(args),
            "listDirectory" => BuiltInListDirectory(args),
            "hasEmbeddedFolder" => BuiltInHasEmbeddedFolder(args),
            "embeddedFolderRoot" => BuiltInEmbeddedFolderRoot(args),
            "replaceInFile" => BuiltInReplaceInFile(args),
            "editFile" => BuiltInEditFile(args),
            "createReadFileTool" => BuiltInCreateReadFileTool(args),
            "createWriteFileTool" => BuiltInCreateWriteFileTool(args),
            "createReplaceInFileTool" => BuiltInCreateReplaceInFileTool(args),
            "createListDirectoryTool" => BuiltInCreateListDirectoryTool(args),
            "createDeleteFileTool" => BuiltInCreateDeleteFileTool(args),
            "createCopyFileTool" => BuiltInCreateCopyFileTool(args),
            "createEnsureDirTool" => BuiltInCreateEnsureDirTool(args),
            "createAskUserTool" => BuiltInCreateAskUserTool(args),
            "createWebSearchTool" => BuiltInCreateWebSearchTool(args),
            "createWebFetchTool" => BuiltInCreateWebFetchTool(args),
            "webFetch" => BuiltInWebFetch(args),
            "createGrepTool" => BuiltInCreateGrepTool(args),
            "grep" => BuiltInGrep(args),
            "createGlobTool" => BuiltInCreateGlobTool(args),
            "glob" => BuiltInGlob(args),
            "createInsertAtLineTool" => BuiltInCreateInsertAtLineTool(args),
            "insertAtLine" => BuiltInInsertAtLine(args),
            "createEditFileTool" => BuiltInCreateEditFileTool(args),
            "getSymbols" => BuiltInGetSymbols(args),
            "createGetSymbolsTool" => BuiltInCreateGetSymbolsTool(args),
            "getParseErrors" => BuiltInGetParseErrors(args),
            "createGetParseErrorsTool" => BuiltInCreateGetParseErrorsTool(args),
            "checkMalda" => BuiltInCheckMalda(args),
            "createCheckMaldaTool" => BuiltInCreateCheckMaldaTool(args),
            "createValidateJsonTool" => BuiltInCreateValidateJsonTool(args),
            "createTestMaldaTool" => BuiltInCreateTestMaldaTool(args),
            "gitStatus" => BuiltInGitStatus(args),
            "gitAdd" => BuiltInGitAdd(args),
            "gitCommit" => BuiltInGitCommit(args),
            "gitLog" => BuiltInGitLog(args),
            "gitDiff" => BuiltInGitDiff(args),
            "gitBranch" => BuiltInGitBranch(args),
            "gitCheckout" => BuiltInGitCheckout(args),
            "gitPush" => BuiltInGitPush(args),
            "gitPull" => BuiltInGitPull(args),
            "createGitStatusTool" => BuiltInCreateGitStatusTool(args),
            "createGitAddTool" => BuiltInCreateGitAddTool(args),
            "createGitCommitTool" => BuiltInCreateGitCommitTool(args),
            "createGitLogTool" => BuiltInCreateGitLogTool(args),
            "createGitDiffTool" => BuiltInCreateGitDiffTool(args),
            "createGitBranchTool" => BuiltInCreateGitBranchTool(args),
            "createGitCheckoutTool" => BuiltInCreateGitCheckoutTool(args),
            "createGitPushTool" => BuiltInCreateGitPushTool(args),
            "createGitPullTool" => BuiltInCreateGitPullTool(args),
            "runCommand" => BuiltInRunCommand(args),
            "createRunCommandTool" => BuiltInCreateRunCommandTool(args),
            "runMALDA" => BuiltInRunMALDA(args),
            "createRunMALDATool" => BuiltInCreateRunMALDATool(args),
            "compileMALDA" => BuiltInCompileMALDA(args),
            "createCompileMALDATool" => BuiltInCreateCompileMALDATool(args),
            "createMcpAgentScript" => BuiltInCreateMcpAgentScript(args),
            "createCreateMcpAgentScriptTool" => BuiltInCreateCreateMcpAgentScriptTool(args),
            "createSubmitPlanTool" => BuiltInCreateSubmitPlanTool(args),
            "createUpdatePlanTool" => BuiltInCreateUpdatePlanTool(args),
            "createMarkStepTool" => BuiltInCreateMarkStepTool(args),
            "executePlan" => BuiltInExecutePlan(args, interpreter),
            "runProgram" => BuiltInRunProgram(args, interpreter),
            "decomposeTask" => BuiltInDecomposeTask(args),
            "extractHTML" => BuiltInExtractHTML(args),
            "markdownToHtml" => BuiltInMarkdownToHtml(args),
            "extractPdfText" => BuiltInExtractPdfText(args),
            "extractDocxText" => BuiltInExtractDocxText(args),
            "renderTemplate" => BuiltInRenderTemplate(args),
            "componentFragment" => BuiltInComponentFragment(args),
            "componentLiveEmit" => BuiltInComponentLiveEmit(args),
            "componentStateGet" => BuiltInComponentStateGet(args),
            "componentStateSet" => BuiltInComponentStateSet(args),
            "componentStateObject" => BuiltInComponentStateObject(args),
            "componentStateClear" => BuiltInComponentStateClear(args),
            "componentStateConfigure" => BuiltInComponentStateConfigure(args),
            "componentStatePin" => BuiltInComponentStatePin(args),
            "componentStateUnpin" => BuiltInComponentStateUnpin(args),
            "onAgentProgress" => BuiltInOnAgentProgress(args, interpreter),
            "clearAgentProgress" => BuiltInClearAgentProgress(args),
            "cancelThink" => BuiltInCancelThink(args),
            "uiRow" => BuiltInUiRow(args),
            "uiColumn" => BuiltInUiColumn(args),
            "uiStack" => BuiltInUiStack(args),
            "uiSpacer" => BuiltInUiSpacer(args),
            "uiPanel" => BuiltInUiPanel(args),
            "uiText" => BuiltInUiText(args),
            "uiHeading" => BuiltInUiHeading(args),
            "uiImage" => BuiltInUiImage(args),
            "uiIcon" => BuiltInUiIcon(args),
            "uiButton" => BuiltInUiButton(args),
            "uiTextField" => BuiltInUiTextField(args),
            "uiCheckbox" => BuiltInUiCheckbox(args),
            "uiSelect" => BuiltInUiSelect(args),
            "uiSlider" => BuiltInUiSlider(args),
            "uiDatePicker" => BuiltInUiDatePicker(args),
            "uiList" => BuiltInUiList(args),
            "uiTable" => BuiltInUiTable(args),
            "uiAlert" => BuiltInUiAlert(args),
            "uiProgress" => BuiltInUiProgress(args),
            "uiModal" => BuiltInUiModal(args),
            "uiForm" => BuiltInUiForm(args),
            "uiField" => BuiltInUiField(args),
            "uiTextArea" => BuiltInUiTextArea(args),
            "uiRadioGroup" => BuiltInUiRadioGroup(args),
            "uiSwitch" => BuiltInUiSwitch(args),
            "uiTabs" => BuiltInUiTabs(args),
            "uiAccordion" => BuiltInUiAccordion(args),
            "uiBreadcrumbs" => BuiltInUiBreadcrumbs(args),
            "uiDrawer" => BuiltInUiDrawer(args),
            "uiDataGrid" => BuiltInUiDataGrid(args),
            "uiTreeView" => BuiltInUiTreeView(args),
            "uiPaginator" => BuiltInUiPaginator(args),
            "uiEmptyState" => BuiltInUiEmptyState(args),
            "uiBadge" => BuiltInUiBadge(args),
            "uiToast" => BuiltInUiToast(args),
            "uiSkeleton" => BuiltInUiSkeleton(args),
            "uiSpinner" => BuiltInUiSpinner(args),
            "uiErrorBoundary" => BuiltInUiErrorBoundary(args),
            "uiSlot" => BuiltInUiSlot(args),
            "uiWithSlot" => BuiltInUiWithSlot(args),
            "uiWhen" => BuiltInUiWhen(args),
            "uiChoose" => BuiltInUiChoose(args),
            "uiEach" => BuiltInUiEach(args),
            "uiTemplate" => BuiltInUiTemplate(args),
            "uiPartial" => BuiltInUiPartial(args),
            "uiLayout" => BuiltInUiLayout(args),
            "uiRenderList" => BuiltInUiRenderList(args),
            "uiCrudModel" => BuiltInUiCrudModel(args),
            "uiCrudControls" => BuiltInUiCrudControls(args),
            "uiCrudSchema" => BuiltInUiCrudSchema(args),
            "uiMount" => BuiltInUiMount(args),
            "uiMountEnvelope" => BuiltInUiMountEnvelope(args),
            "uiRender" => BuiltInUiRender(args),
            "uiDispatchEvent" => BuiltInUiDispatchEvent(args),
            "uiPullEvent" => BuiltInUiPullEvent(args),
            "uiState" => BuiltInUiState(args),
            "uiGetState" => BuiltInUiGetState(args),
            "uiSetState" => BuiltInUiSetState(args),
            "uiPinState" => BuiltInUiPinState(args),
            "uiUnpinState" => BuiltInUiUnpinState(args),
            "uiInvalidate" => BuiltInUiInvalidate(args),
            "uiOnInit" => BuiltInUiOnInit(args),
            "uiOnPreRender" => BuiltInUiOnPreRender(args),
            "uiOnLoad" => BuiltInUiOnLoad(args),
            "uiOnDispose" => BuiltInUiOnDispose(args),
            "uiOnMount" => BuiltInUiOnMount(args),
            "uiOnUpdate" => BuiltInUiOnUpdate(args),
            "uiOnUnmount" => BuiltInUiOnUnmount(args),
            "uiOnError" => BuiltInUiOnError(args),
            "uiConfigure" => BuiltInUiConfigure(args),
            "uiSnapshot" => BuiltInUiSnapshot(args),
            "uiResync" => BuiltInUiResync(args),
            "uiSessionId" => BuiltInUiSessionId(args),
            "uiRedirectWithSession" => BuiltInUiRedirectWithSession(args),
            "uiGenerate" => BuiltInUiGenerate(args, interpreter),
            "redirect" => BuiltInRedirect(args),
            "RedirectTo" => BuiltInRedirectTo(args),
            "loadAssembly" => BuiltInLoadAssembly(args),
            "getDotNetType" => BuiltInGetDotNetType(args),
            "dotnetNew" => BuiltInDotNetNew(args),
            "httpGet" => BuiltInHttpGet(args),
            "httpPost" => BuiltInHttpPost(args),
            "httpPut" => BuiltInHttpPut(args),
            "httpDelete" => BuiltInHttpDelete(args),
            "httpPatch" => BuiltInHttpPatch(args),
            "httpBearerToken" => BuiltInHttpBearerToken(args),
            "httpCookieToken" => BuiltInHttpCookieToken(args),
            "httpAuthToken" => BuiltInHttpAuthToken(args),
            "webSearch" => BuiltInWebSearch(args),
            "reply" => BuiltInReply(args, interpreter),
            "embedBagOfWords" => BuiltInEmbedBagOfWords(args),
            "embedCharacterNGrams" => BuiltInEmbedCharacterNGrams(args),
            "embedHash" => BuiltInEmbedHash(args),
            "embedTFIDF" => BuiltInEmbedTFIDF(args),
            "embedFromFile" => BuiltInEmbedFromFile(args),
            "embedFromFiles" => BuiltInEmbedFromFiles(args),
            // Date/Time functions
            "now" => BuiltInNow(args),
            "formatDate" => BuiltInFormatDate(args),
            "parseDate" => BuiltInParseDate(args),
            "addDays" => BuiltInAddDays(args),
            "addHours" => BuiltInAddHours(args),
            // Random functions
            "random" => BuiltInRandom(args),
            "randomInt" => BuiltInRandomInt(args),
            "randomFloat" => BuiltInRandomFloat(args),
            // Type checking functions
            "isNumber" => BuiltInIsNumber(args),
            "isString" => BuiltInIsString(args),
            "isArray" => BuiltInIsArray(args),
            "isObject" => BuiltInIsObject(args),
            "typeOf" => BuiltInTypeOf(args),
            "isTag" => BuiltInIsTag(args),
            "validate" => BuiltInValidate(args),
            "asVariant" => TypedPromptValidator.AsVariant(args),
            "evalPrompt" => PromptEval.EvalPrompt(args, interpreter),
            // Array utilities
            "join" => BuiltInJoin(args),
            "toCsv" => BuiltInToCsv(args),
            "reverse" => BuiltInReverse(args),
            "sort" => BuiltInSort(args, interpreter),
            "includes" => BuiltInIncludes(args),
            // Encoding/Decoding
            "base64Encode" => BuiltInBase64Encode(args),
            "base64Decode" => BuiltInBase64Decode(args),
            "urlEncode" => BuiltInUrlEncode(args),
            "urlDecode" => BuiltInUrlDecode(args),
            // Hash functions
            "md5" => BuiltInMd5(args),
            "sha256" => BuiltInSha256(args),
            "hashPassword" => BuiltInHashPassword(args),
            "verifyPassword" => BuiltInVerifyPassword(args),
            "createJwt" => BuiltInCreateJwt(args),
            "verifyJwt" => BuiltInVerifyJwt(args),
            "generateCsrfToken" => BuiltInGenerateCsrfToken(args),
            "verifyCsrfToken" => BuiltInVerifyCsrfToken(args),
            "csrfField" => BuiltInCsrfField(args),
            "formErrors" => BuiltInFormErrors(args),
            "bindForm" => BuiltInBindForm(args),
            "pageLayout" => BuiltInPageLayout(args),
            "createSecureCookie" => BuiltInCreateSecureCookie(args),
            "readSecureCookie" => BuiltInReadSecureCookie(args),
            // Path manipulation
            "pathJoin" => BuiltInPathJoin(args),
            "pathNormalize" => BuiltInPathNormalize(args),
            "pathExists" => BuiltInPathExists(args),
            "pathGetExtension" => BuiltInPathGetExtension(args),
            "isPathUnder" => BuiltInIsPathUnder(args),
            // Range generation
            "range" => BuiltInRange(args),
            // Error handling
            "exit" => BuiltInExit(args),
            "error" => BuiltInError(args),
            "assert" => BuiltInAssert(args),
            "expect" => BuiltInExpect(args),
            "race" => BuiltInRace(args),
            "firstOk" => BuiltInFirstOk(args),
            "stream" => StreamStdLib.Stream(args),
            // Additional string utilities
            "startsWith" => BuiltInStartsWith(args),
            "endsWith" => BuiltInEndsWith(args),
            "padStart" => BuiltInPadStart(args),
            "padEnd" => BuiltInPadEnd(args),
            "repeat" => BuiltInRepeat(args),
            "all" => BuiltInAll(args),
            "getWorkflowStatus" => BuiltInGetWorkflowStatus(args),
            "getWorkflow" => BuiltInGetWorkflow(args),
            "getWorkflowSteps" => BuiltInGetWorkflowSteps(args),
            "getWorkflowEvents" => BuiltInGetWorkflowEvents(args),
            "getWorkflowMetrics" => BuiltInGetWorkflowMetrics(args),
            "listWorkflows" => BuiltInListWorkflows(args),
            "listWorkflowDeadLetters" => BuiltInListWorkflowDeadLetters(args),
            "requeueDeadLetter" => BuiltInRequeueDeadLetter(args),
            "cancelWorkflow" => BuiltInCancelWorkflow(args),
            "resumeWorkflow" => BuiltInResumeWorkflow(args),
            "retryWorkflow" => BuiltInRetryWorkflow(args),
            "approveWorkflowStep" => BuiltInApproveWorkflowStep(args),
            "signalWorkflow" => BuiltInSignalWorkflow(args),
            "enqueueJob" => BuiltInEnqueueJob(args, interpreter),
            "claimJob" => BuiltInClaimJob(args),
            "completeJob" => BuiltInCompleteJob(args, interpreter),
            "failJob" => BuiltInFailJob(args, interpreter),
            "getJob" => BuiltInGetJob(args),
            "listJobs" => BuiltInListJobs(args),
            "runProperty" => BuiltInRunProperty(args, interpreter),
            "setDefaultAgent" => BuiltInSetDefaultAgent(args, interpreter),
            "runWorkflowInstance" => throw new Exception("runWorkflowInstance() must be called via CallBuiltInAsync"),
            _ => throw new Exception($"Unknown built-in function: {name}")
        };
    }

    public static async Task<RuntimeValue> CallBuiltInAsync(string name, List<RuntimeValue> args, Interpreter? interpreter)
    {
        CheckWorkflowDeterminism(name, interpreter);
        var profileToken = MaldaProfiler.EnterBuiltIn(name);
        try
        {
            return name switch
            {
                "anneal" => await MathAnneal.RunAsync(args, interpreter),
                "input" => await BuiltInInputAsync(args, interpreter),
                "sleep" => await BuiltInSleepAsync(args, interpreter),
                "runPrompt" => await AiPipelineHelpers.RunPromptAsync(args, interpreter),
                "parallelRun" => await AiPipelineHelpers.ParallelRunAsync(args, interpreter),
                "uiGenerate" => await BuiltInUiGenerateAsync(args, interpreter),
                "generateUI" => await BuiltInGenerateUIAsync(args, interpreter),
                "startWorkflow" => await BuiltInStartWorkflowAsync(args, interpreter),
                "runWorkflowInstance" => await BuiltInRunWorkflowInstanceAsync(args, interpreter),
                _ => DispatchBuiltIn(name, args, interpreter)
            };
        }
        finally
        {
            MaldaProfiler.Exit(profileToken);
        }
    }
}

public class InputRequiredException : Exception
{
    public string Prompt { get; }
    
    public InputRequiredException(string prompt) : base("Input required")
    {
        Prompt = prompt;
    }
}
