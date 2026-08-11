// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

public enum WorkflowBuiltInBehavior
{
    Deterministic,
    NonDeterministic,
    SideEffecting
}

public enum BuiltInTranspilerStrategy
{
    NotSupported,
    SupportedByTranspiler
}

public sealed record BuiltInDescriptor(
    string Name,
    bool SupportsSync,
    bool SupportsAsync,
    WorkflowBuiltInBehavior WorkflowBehavior,
    string InterpreterDispatchTarget,
    BuiltInTranspilerStrategy TranspilerStrategy,
    bool IsAlwaysSynchronousForCodegen);

public static class BuiltInRegistry
{
    public static bool IsInterpreterBuiltIn(string name)
    {
        return GetDescriptor(name) != null;
    }

    public static bool IsTranspilerBuiltIn(string name)
    {
        return GetDescriptor(name)?.TranspilerStrategy == BuiltInTranspilerStrategy.SupportedByTranspiler;
    }

    public static BuiltInDescriptor? GetDescriptor(string name)
    {
        return name switch
        {
            // These built-ins are transpiler-supported and their call sites do not
            // introduce an async boundary by themselves, so await analysis only
            // needs to inspect their arguments/receiver expressions.
            "int" or
            "toIntOr" or
            "toIntOrNull" or
            "float" or
            "string" or
            "formatNumber" or
            "abs" or
            "sum" or
            "average" or
            "max" or
            "min" or
            "pow" or
            "sqrt" or
            "floor" or
            "ceil" or
            "round" or
            "trunc" or
            "sign" or
            "exp" or
            "log" or
            "log10" or
            "log2" or
            "sin" or
            "cos" or
            "tan" or
            "asin" or
            "acos" or
            "atan" or
            "atan2" or
            "hypot" or
            "clamp" or
            "degToRad" or
            "radToDeg" or
            "rsqrt" or
            "randn" or
            "argmax" or
            "argmin" or
            "logSumExp" or
            "softmax" or
            "crossEntropyFromLogits" or
            "randomChoiceWeighted" or
            "seed" or
            "length" or
            "upper" or
            "lower" or
            "trim" or
            "text" or
            "trimText" or
            "substring" or
            "indexOf" or
            "replace" or
            "split" or
            "normalizeText" or
            "tokenize" or
            "tokenOverlap" or
            "similarity" or
            "extractNumbers" or
            "regexMatch" or
            "regexReplace" or
            "regexFind" or
            "getMaldaHome" or
            "getProgramDirectory" or
            "getMaldaConfig" or
            "getAssistantMemory" or
            "enableAgentVerboseLogging" or
            "setAgentVerbosePhase" or
            "setAgentStatusBanner" or
            "reportRalphStatus" or
            "getSkillNames" or
            "loadSkill" or
            "loadSkillsFromDir" or
            "getEnv" or
            "getEnvOr" or
            "getHostPlatform" or
            "getCommandLineArgs" or
            "hasEnv" or
            "parseJSON" or
            "parseJson" or
            "loadDocuments" or
            "splitDocuments" or
            "formatRetrievedDocs" or
            "composePipe" or
            "mergeRetrievedDocs" or
            "withExamples" or
            "indexInto" or
            "toJSON" or
            "validate" or
            "readFile" or
            "readTextFileLines" or
            "loadNativeModule" or
            "createNativeCallback" or
            "writeFile" or
            "writeFileBase64" or
            "readFileBase64" or
            "hasFile" or
            "deleteFile" or
            "hasDirectory" or
            "ensureDir" or
            "listDirectory" or
            "hasEmbeddedFolder" or
            "embeddedFolderRoot" or
            "glob" or
            "grep" or
            "replaceInFile" or
            "gitStatus" or
            "gitAdd" or
            "gitCommit" or
            "gitDiff" or
            "getFileName" or
            "getDirectoryName" or
            "embedBagOfWords" or
            "embedCharacterNGrams" or
            "embedHash" or
            "embedTFIDF" or
            "embedFromFile" or
            "embedFromFiles" or
            // Array methods (member codegen uses RuntimeHelpers; registry marks transpile support).
            "append" or
            "pop" or
            "shift" or
            "editFile" or
            "insertAtLine" or
            "gitLog" or
            "gitBranch" or
            "gitCheckout" or
            "gitPush" or
            "gitPull" or
            "createReadFileTool" or
            "createWriteFileTool" or
            "createReplaceInFileTool" or
            "createListDirectoryTool" or
            "createAskUserTool" or
            "createGrepTool" or
            "createGlobTool" or
            "createInsertAtLineTool" or
            "createEditFileTool" or
            "createGitStatusTool" or
            "createGitAddTool" or
            "createGitCommitTool" or
            "createGitLogTool" or
            "createGitDiffTool" or
            "createGitBranchTool" or
            "createGitCheckoutTool" or
            "createGitPushTool" or
            "createGitPullTool" or
            "loadAssembly" or
            "getDotNetType" or
            "dotnetNew" or
            "uiGenerate" or
            "markdownToHtml" or
            "extractPdfText" or
            "extractDocxText" => Descriptor(
                name,
                BuiltInTranspilerStrategy.SupportedByTranspiler,
                isAlwaysSynchronousForCodegen: true),

            // These built-ins are available to the transpiler, but codegen must
            // stay conservative because the emitted call site may await directly
            // or is not explicitly modeled as sync-safe.
            "print" or
            "input" or
            "sleep" or
            "runPrompt" or
            "parallelRun" or
            "runProperty" or
            "reply" or
            "runCommand" or
            "createRunCommandTool" or
            "createWebSearchTool" or
            "setDefaultAgent" or
            "generateUI" or
            "runMALDA" or
            "createRunMALDATool" or
            "compileMALDA" or
            "createCompileMALDATool" or
            "getSymbols" or
            "createGetSymbolsTool" or
            "getParseErrors" or
            "createGetParseErrorsTool" or
            "createMcpAgentScript" or
            "createCreateMcpAgentScriptTool" or
            "createSubmitPlanTool" or
            "executePlan" or
            "runProgram" or
            "decomposeTask" or
            "extractHTML" or
            "renderTemplate" or
            "componentFragment" or
            "componentLiveEmit" or
            "componentStateGet" or
            "componentStateSet" or
            "componentStateObject" or
            "componentStateClear" or
            "componentStateConfigure" or
            "componentStatePin" or
            "componentStateUnpin" or
            "onAgentProgress" or
            "clearAgentProgress" or
            "uiRow" or
            "uiColumn" or
            "uiStack" or
            "uiSpacer" or
            "uiPanel" or
            "uiText" or
            "uiHeading" or
            "uiImage" or
            "uiIcon" or
            "uiButton" or
            "uiTextField" or
            "uiCheckbox" or
            "uiSelect" or
            "uiSlider" or
            "uiDatePicker" or
            "uiList" or
            "uiTable" or
            "uiAlert" or
            "uiProgress" or
            "uiModal" or
            "uiForm" or
            "uiField" or
            "uiTextArea" or
            "uiRadioGroup" or
            "uiSwitch" or
            "uiTabs" or
            "uiAccordion" or
            "uiBreadcrumbs" or
            "uiDrawer" or
            "uiDataGrid" or
            "uiTreeView" or
            "uiPaginator" or
            "uiEmptyState" or
            "uiBadge" or
            "uiToast" or
            "uiSkeleton" or
            "uiSpinner" or
            "uiErrorBoundary" or
            "uiSlot" or
            "uiWithSlot" or
            "uiWhen" or
            "uiChoose" or
            "uiEach" or
            "uiTemplate" or
            "uiPartial" or
            "uiLayout" or
            "uiRenderList" or
            "uiCrudModel" or
            "uiCrudControls" or
            "uiCrudSchema" or
            "uiMount" or
            "uiMountEnvelope" or
            "uiRender" or
            "uiDispatchEvent" or
            "uiPullEvent" or
            "uiState" or
            "uiGetState" or
            "uiSetState" or
            "uiPinState" or
            "uiUnpinState" or
            "uiInvalidate" or
            "uiOnInit" or
            "uiOnPreRender" or
            "uiOnLoad" or
            "uiOnDispose" or
            "uiOnMount" or
            "uiOnUpdate" or
            "uiOnUnmount" or
            "uiOnError" or
            "uiConfigure" or
            "uiSnapshot" or
            "uiResync" or
            "uiSessionId" or
            "uiRedirectWithSession" or
            "redirect" or
            "RedirectTo" or
            "httpGet" or
            "httpPost" or
            "httpPut" or
            "httpDelete" or
            "httpPatch" or
            "httpBearerToken" or
            "httpCookieToken" or
            "httpAuthToken" or
            "webSearch" or
            "now" or
            "formatDate" or
            "parseDate" or
            "addDays" or
            "addHours" or
            "random" or
            "randomInt" or
            "randomFloat" or
            "isNumber" or
            "isString" or
            "isArray" or
            "isObject" or
            "typeOf" or
            "isTag" or
            "join" or
            "toCsv" or
            "reverse" or
            "sort" or
            "includes" or
            "base64Encode" or
            "base64Decode" or
            "urlEncode" or
            "urlDecode" or
            "md5" or
            "sha256" or
            "hashPassword" or
            "verifyPassword" or
            "createJwt" or
            "verifyJwt" or
            "generateCsrfToken" or
            "verifyCsrfToken" or
            "csrfField" or
            "formErrors" or
            "bindForm" or
            "pageLayout" or
            "createSecureCookie" or
            "readSecureCookie" or
            "pathJoin" or
            "pathNormalize" or
            "pathExists" or
            "pathGetExtension" or
            "isPathUnder" or
            "range" or
            "exit" or
            "error" or
            "assert" or
            "startsWith" or
            "endsWith" or
            "padStart" or
            "padEnd" or
            "repeat" or
            "all" or
            "startWorkflow" or
            "getWorkflowStatus" or
            "getWorkflow" or
            "getWorkflowSteps" or
            "getWorkflowEvents" or
            "getWorkflowMetrics" or
            "listWorkflows" or
            "listWorkflowDeadLetters" or
            "requeueDeadLetter" or
            "cancelWorkflow" or
            "resumeWorkflow" or
            "retryWorkflow" or
            "approveWorkflowStep" or
            "signalWorkflow" or
            "runWorkflowInstance" or
            "enqueueJob" or
            "claimJob" or
            "completeJob" or
            "failJob" or
            "getJob" or
            "listJobs" => Descriptor(
                name,
                BuiltInTranspilerStrategy.SupportedByTranspiler),

            _ => null
        };
    }

    public static WorkflowBuiltInBehavior GetWorkflowBehavior(string name)
    {
        return name switch
        {
            "now" or "random" or "randomInt" or "randomFloat" => WorkflowBuiltInBehavior.NonDeterministic,
            "runCommand" or "writeFile" or "replaceInFile" or "editFile" or "deleteFile" or
            "runMALDA" or "compileMALDA" or "httpGet" or "httpPost" or "httpPut" or
            "httpDelete" or "httpPatch" => WorkflowBuiltInBehavior.SideEffecting,
            _ => WorkflowBuiltInBehavior.Deterministic
        };
    }

    private static BuiltInDescriptor Descriptor(
        string name,
        BuiltInTranspilerStrategy transpilerStrategy,
        bool isAlwaysSynchronousForCodegen = false)
    {
        return new BuiltInDescriptor(
            name,
            SupportsSync: !IsAsyncOnly(name),
            SupportsAsync: true,
            WorkflowBehavior: GetWorkflowBehavior(name),
            InterpreterDispatchTarget: name,
            TranspilerStrategy: transpilerStrategy,
            IsAlwaysSynchronousForCodegen: isAlwaysSynchronousForCodegen);
    }

    private static bool IsAsyncOnly(string name)
    {
        return name is "input" or "sleep" or "startWorkflow" or "runWorkflowInstance";
    }
}
