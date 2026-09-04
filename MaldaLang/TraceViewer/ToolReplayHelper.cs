// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Helper for reconstructing ToolInstance objects from trace events
// when preparing replay/branch agent state.

namespace MaldaLang.TraceViewer;

using System;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Factory for recreating known built-in tools from trace metadata.
/// </summary>
internal static class ToolReplayHelper
{
    private static Func<string, string?, ToolInstance?>? _customFactory;

    /// <summary>
    /// Registers an optional custom factory that can recreate tools by name and working directory.
    /// This is intended for hosts that define their own tools (e.g., MCP tools) and wish to
    /// participate in replay. Pass null to remove the custom factory.
    /// </summary>
    public static void RegisterCustomToolFactory(Func<string, string?, ToolInstance?>? factory)
    {
        _customFactory = factory;
    }

    /// <summary>
    /// Attempts to create a ToolInstance for the given tool name and working directory.
    /// Returns null for unknown tools.
    /// </summary>
    public static ToolInstance? TryCreateTool(string toolName, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;

        // Allow host-provided factory to handle custom tools first.
        if (_customFactory != null)
        {
            try
            {
                var custom = _customFactory(toolName, workingDirectory);
                if (custom != null)
                    return custom;
            }
            catch
            {
                // Hosts are responsible for handling their own factory errors; replay should not fail.
            }
        }

        // Normalize working directory for built-in factories.
        workingDirectory ??= string.Empty;

        RuntimeValue toolValue;

        // Map known built-in tool names to their factory methods.
        // IMPORTANT: toolName here is the schema name (e.g. "read_file"),
        // which must match the Initialize() name in BuiltInTools.
        switch (toolName)
        {
            // File operations
            case "read_file":
                toolValue = BuiltInTools.CreateReadFileTool(workingDirectory);
                break;
            case "write_file":
                toolValue = BuiltInTools.CreateWriteFileTool(workingDirectory);
                break;
            case "replace_in_file":
                toolValue = BuiltInTools.CreateReplaceInFileTool(workingDirectory);
                break;
            case "insertAtLine":
                toolValue = BuiltInTools.CreateInsertAtLineTool(workingDirectory);
                break;
            case "edit_file":
                toolValue = BuiltInTools.CreateEditFileTool(workingDirectory);
                break;
            case "list_directory":
                toolValue = BuiltInTools.CreateListDirectoryTool(workingDirectory);
                break;
            case "delete_file":
                toolValue = BuiltInTools.CreateDeleteFileTool(workingDirectory);
                break;
            case "copy_file":
                toolValue = BuiltInTools.CreateCopyFileTool(workingDirectory);
                break;
            case "ensure_dir":
                toolValue = BuiltInTools.CreateEnsureDirTool(workingDirectory);
                break;
            case "grep":
                toolValue = BuiltInTools.CreateGrepTool(workingDirectory);
                break;
            case "glob":
                toolValue = BuiltInTools.CreateGlobTool(workingDirectory);
                break;

            // Git operations
            case "git_status":
                toolValue = BuiltInTools.CreateGitStatusTool(workingDirectory);
                break;
            case "git_add":
                toolValue = BuiltInTools.CreateGitAddTool(workingDirectory);
                break;
            case "git_commit":
                toolValue = BuiltInTools.CreateGitCommitTool(workingDirectory);
                break;
            case "git_log":
                toolValue = BuiltInTools.CreateGitLogTool(workingDirectory);
                break;
            case "git_diff":
                toolValue = BuiltInTools.CreateGitDiffTool(workingDirectory);
                break;
            case "git_branch":
                toolValue = BuiltInTools.CreateGitBranchTool(workingDirectory);
                break;
            case "git_checkout":
                toolValue = BuiltInTools.CreateGitCheckoutTool(workingDirectory);
                break;
            case "git_push":
                toolValue = BuiltInTools.CreateGitPushTool(workingDirectory);
                break;
            case "git_pull":
                toolValue = BuiltInTools.CreateGitPullTool(workingDirectory);
                break;

            // Command / MALDA execution
            case "run_command":
                toolValue = BuiltInTools.CreateRunCommandTool(workingDirectory);
                break;
            case "run_malda":
                toolValue = BuiltInTools.CreateRunMALDATool(workingDirectory);
                break;
            case "compile_malda":
                toolValue = BuiltInTools.CreateCompileMALDATool(workingDirectory);
                break;

            // Analysis / meta-tools
            case "get_symbols":
                toolValue = BuiltInTools.CreateGetSymbolsTool(workingDirectory);
                break;
            case "get_parse_errors":
                toolValue = BuiltInTools.CreateGetParseErrorsTool(workingDirectory);
                break;
            case "create_mcp_agent_script":
                toolValue = BuiltInTools.CreateCreateMcpAgentScriptTool(workingDirectory);
                break;
            case "submit_plan":
                toolValue = BuiltInTools.CreateSubmitPlanTool();
                break;
            case "web_search":
                toolValue = BuiltInTools.CreateWebSearchTool();
                break;

            // User interaction and others that are not safely restorable from trace
            // (ask_user, MCP tools, etc.) are intentionally not reconstructed here.
            default:
                return null;
        }

        try
        {
            var obj = toolValue.AsObject();
            if (obj is ToolInstance tool)
                return tool;
        }
        catch
        {
            // If anything goes wrong, fail gracefully and treat as non-restorable.
        }

        return null;
    }
}

