// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE;

using MaldaLang.IDE.Models;

/// <summary>
/// Completion surface for Agent / Tool members and <c>create*Tool</c> factories,
/// kept in sync with <c>AgentInstance</c> / <c>ToolInstance</c> / <c>BuiltInFunctions</c>.
/// </summary>
internal static class AgentToolCompletionCatalog
{
    public static readonly CompletionItem[] AgentMembers =
    {
        Member("name", "property", "string", "name"),
        Member("role", "property", "string", "role"),
        Member("instructions", "property", "string", "instructions"),
        Member("memory", "property", "GraphMemory?", "memory"),
        Member("think", "method", "think(prompt) — string or PromptInstance", "think()"),
        Member("addTool", "method", "addTool(toolOrName) — Tool instance or tool name", "addTool()"),
        Member("addToolByName", "method", "addToolByName(name)", "addToolByName()"),
        Member("addAllTools", "method", "addAllTools() — register every built-in tool", "addAllTools()"),
        Member("getAvailableTools", "method", "getAvailableTools() — registered tool names", "getAvailableTools()"),
        Member("getConversation", "method", "getConversation()", "getConversation()"),
        Member("reset", "method", "reset() — clear conversation", "reset()"),
        Member("addSubAgent", "method", "addSubAgent(agent, toolDescription)", "addSubAgent()"),
        Member("enableMemory", "method", "enableMemory(dimensionOrPath?, precision?)", "enableMemory()"),
        Member("useMemory", "method", "useMemory(query, maxResults?)", "useMemory()"),
        Member("getMemory", "method", "getMemory() — GraphMemory or null", "getMemory()"),
        Member("saveMemory", "method", "saveMemory(path?)", "saveMemory()"),
        Member("remember", "method", "remember(fact, context?)", "remember()"),
        Member("setAutoRememberOnThink", "method", "setAutoRememberOnThink(enabled)", "setAutoRememberOnThink()"),
        Member("setMemoryScope", "method", "setMemoryScope(scope)", "setMemoryScope()"),
        Member("setMemoryScopeParent", "method", "setMemoryScopeParent(parentScope)", "setMemoryScopeParent()"),
        Member("setMemoryScopeHierarchy", "method", "setMemoryScopeHierarchy(scopes)", "setMemoryScopeHierarchy()"),
        Member("setMemoryRerank", "method", "setMemoryRerank(enabled, mode?, modelPath?, topK?)", "setMemoryRerank()"),
        Member("addMemoryProgressTools", "method", "addMemoryProgressTools()", "addMemoryProgressTools()"),
        Member("setContextTrimHandoff", "method", "setContextTrimHandoff(note)", "setContextTrimHandoff()"),
        Member("getEstimatedContextTokens", "method", "getEstimatedContextTokens()", "getEstimatedContextTokens()")
    };

    public static readonly CompletionItem[] ToolMembers =
    {
        Member("name", "property", "string", "name"),
        Member("description", "property", "string", "description"),
        Member("getSchema", "method", "getSchema()", "getSchema()"),
        Member("execute", "method", "execute(arguments)", "execute()"),
        Member("describe", "method", "describe()", "describe()")
    };

    public static readonly string[] CreateToolFactories =
    {
        "createReadFileTool", "createWriteFileTool", "createReplaceInFileTool",
        "createListDirectoryTool", "createAskUserTool", "createWebSearchTool",
        "createGrepTool", "createGlobTool", "createInsertAtLineTool", "createEditFileTool",
        "createGetSymbolsTool", "createGetParseErrorsTool",
        "createGitStatusTool", "createGitAddTool", "createGitCommitTool", "createGitLogTool",
        "createGitDiffTool", "createGitBranchTool", "createGitCheckoutTool",
        "createGitPushTool", "createGitPullTool",
        "createRunCommandTool", "createRunMALDATool", "createCompileMALDATool",
        "createCreateMcpAgentScriptTool", "createSubmitPlanTool"
    };

    public static bool IsCreateToolFactory(string name) =>
        Array.IndexOf(CreateToolFactories, name) >= 0;

    private static CompletionItem Member(string label, string kind, string detail, string insertText) =>
        new()
        {
            Label = label,
            Kind = kind,
            Detail = detail,
            InsertText = insertText
        };
}
