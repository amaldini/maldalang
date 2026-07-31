// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using System.Collections.Frozen;

/// <summary>
/// Phase 6.1: builtins and namespaces treated as non-pure (IO / platform side effects).
/// </summary>
public static class PureEffectsBuiltIns
{
    private static readonly FrozenSet<string> IoNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "print", "input", "sleep", "reply",
        "readFile", "readTextFileLines", "readFileBase64", "writeFile", "writeFileBase64",
        "deleteFile", "hasFile", "hasDirectory", "ensureDir", "listDirectory",
        "glob", "grep", "replaceInFile", "editFile", "runCommand",
        "httpGet", "httpPost", "httpPut", "httpDelete", "httpPatch",
        "spawn", "send", "receive",
        "getEnv", "hasEnv", "getCommandLineArgs", "loadNativeModule", "createNativeCallback",
        "runMALDA", "compileMALDA", "executePlan", "decomposeTask",
        "startWorkflow", "runWorkflowInstance", "cancelWorkflow", "approveWorkflowStep",
        "gitStatus", "gitAdd", "gitCommit", "gitDiff", "gitLog", "gitBranch", "gitCheckout", "gitPush", "gitPull",
        "embedFromFile", "embedFromFiles",
        "enableAgentVerboseLogging", "setAgentVerbosePhase", "setAgentStatusBanner", "reportRalphStatus",
        "loadSkill", "loadSkillsFromDir", "getMaldaHome", "getMaldaConfig", "getAssistantMemory", "getHostPlatform",
        "uiGenerate", "generateUI", "uiMount", "uiInvalidate", "uiSetState",
        "dotnetNew", "loadAssembly",
    }.ToFrozenSet();

    private static readonly FrozenSet<string> IoNamespaces = new HashSet<string>(StringComparer.Ordinal)
    {
        "io", "file",
    }.ToFrozenSet();

    public static bool IsIoEffect(string name) => IoNames.Contains(name);

    public static bool IsIoNamespace(string name) => IoNamespaces.Contains(name);

    public static bool IsIoMemberAccess(string? rootName, string memberName) =>
        rootName != null && IsIoNamespace(rootName);

    public static bool IsEffectAllowed(IReadOnlySet<string> allowed, string calleeName, string? namespaceRoot = null)
    {
        if (allowed.Contains(calleeName))
            return true;

        if (namespaceRoot != null && allowed.Contains(namespaceRoot))
            return true;

        return false;
    }
}
