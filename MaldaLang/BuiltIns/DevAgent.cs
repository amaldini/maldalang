// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Specialized agent for full development workflows that automatically includes all file operation tools, git tools, command execution tools, and optional code analysis tools.
/// </summary>
public class DevAgentInstance : AgentInstance
{
    private string _workingDirectory = ".";
    private bool _codeMemoryToolsAdded;
    /// <summary>
    /// Creates a new DevAgent instance with all development tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="client">Optional LLM client instance (defaults to local LLM with auto-download from Hugging Face if not provided)</param>
    /// <param name="workingDirectory">Optional working directory for operations (defaults to ".")</param>
    /// <param name="includeSymbols">Optional flag to include getSymbols tool for code analysis (defaults to false)</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    /// <param name="readOnly">If true, only register read-only tools (read_file, grep, glob, list_directory, getSymbols, getParseErrors); no file writes, git, or run_command</param>
    /// <param name="prdAuthorOnly">If true, register PRD-interview tools only (read/write/explore + ask_user; no git, run_command, or web_search)</param>
    public DevAgentInstance(string name, string role, string instructions, LLMClientInstance? client = null, string? workingDirectory = null, bool includeSymbols = false, IInputProvider? inputProvider = null, bool readOnly = false, bool prdAuthorOnly = false)
    {
        // Use default local LLM (auto-download from Hugging Face) if client is not provided
        if (client != null)
            Initialize(name, role, instructions, client, null, null, inputProvider);
        else
            Initialize(name, role, instructions, null, DefaultLocalLlm.GetDefaultLocalClient(), null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        _workingDirectory = workingDir;
        
        // Register all development tools
        var toolNames = RegisterAllTools(workingDir, includeSymbols, readOnly, prdAuthorOnly);
        AppendToolUsageGuidance(toolNames, readOnly, workingDir, prdAuthorOnly);
    }
    
    /// <summary>
    /// Creates a new DevAgent instance with all development tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="workingDirectory">Optional working directory for operations (defaults to ".")</param>
    /// <param name="includeSymbols">Optional flag to include getSymbols tool for code analysis (defaults to false)</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    /// <param name="readOnly">If true, only register read-only tools (read_file, grep, glob, list_directory, getSymbols, getParseErrors); no file writes, git, or run_command</param>
    /// <param name="prdAuthorOnly">If true, register PRD-interview tools only (read/write/explore + ask_user; no git, run_command, or web_search)</param>
    public DevAgentInstance(string name, string role, string instructions, LlamaCppClientInstance? llamaClient, string? workingDirectory, bool includeSymbols = false, IInputProvider? inputProvider = null, bool readOnly = false, bool prdAuthorOnly = false)
    {
        // Initialize the base agent with LlamaCppClient
        Initialize(name, role, instructions, null, llamaClient, null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        _workingDirectory = workingDir;
        
        // Register all development tools
        var toolNames = RegisterAllTools(workingDir, includeSymbols, readOnly, prdAuthorOnly);
        AppendToolUsageGuidance(toolNames, readOnly, workingDir, prdAuthorOnly);
    }
    
    /// <summary>
    /// Creates a new DevAgent instance with all development tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llmClient">Optional LLMClient instance</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="bridgeClient">Optional LLMClientBridge instance</param>
    /// <param name="workingDirectory">Optional working directory for operations (defaults to ".")</param>
    /// <param name="includeSymbols">Optional flag to include getSymbols tool for code analysis (defaults to false)</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    /// <param name="readOnly">If true, only register read-only tools (read_file, grep, glob, list_directory, getSymbols, getParseErrors); no file writes, git, or run_command</param>
    /// <param name="prdAuthorOnly">If true, register PRD-interview tools only (read/write/explore + ask_user; no git, run_command, or web_search)</param>
    public DevAgentInstance(string name, string role, string instructions, LLMClientInstance? llmClient, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string? workingDirectory, bool includeSymbols = false, IInputProvider? inputProvider = null, bool readOnly = false, bool prdAuthorOnly = false)
    {
        // Initialize the base agent
        Initialize(name, role, instructions, llmClient, llamaClient, bridgeClient, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        _workingDirectory = workingDir;
        
        // Register all development tools
        var toolNames = RegisterAllTools(workingDir, includeSymbols, readOnly, prdAuthorOnly);
        AppendToolUsageGuidance(toolNames, readOnly, workingDir, prdAuthorOnly);
    }

    public override RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "enableCodeMemory":
                return EnableCodeMemory(args);
            case "indexCodebase":
                return IndexCodebase(args);
            default:
                return base.CallMethod(methodName, args);
        }
    }

    private RuntimeValue EnableCodeMemory(List<RuntimeValue> args)
    {
        EnsureInterpreter();

        var fullWorkDir = Path.GetFullPath(_workingDirectory);
        var hadMemory = _memory != null;

        if (!hadMemory)
        {
            var memoryPath = Path.Combine(fullWorkDir, ".dev-agent-memory");
            if (args.Count > 0 && args[0].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[0].AsString()))
                memoryPath = args[0].AsString().Trim();

            EnableMemory(new List<RuntimeValue> { RuntimeValue.String(memoryPath) });

            var scopeName = Path.GetFileName(fullWorkDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(scopeName))
                scopeName = "project";
            var scope = "code:" + scopeName;
            if (args.Count > 1 && args[1].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[1].AsString()))
                scope = args[1].AsString().Trim();

            CallMethod("setMemoryScope", new List<RuntimeValue> { RuntimeValue.String(scope) });
        }

        if (!_codeMemoryToolsAdded && _memory != null && _interpreter != null)
        {
            AddTool(DevAgentCodeMemoryToolInstance.CreateIndexCodeFileTool(_memory, _interpreter, fullWorkDir));
            AddTool(DevAgentCodeMemoryToolInstance.CreateFindCodeRelationshipsTool(_memory, _interpreter, fullWorkDir));
            _codeMemoryToolsAdded = true;
        }

        if (hadMemory)
        {
            AppendToSystemPrompt(
                "\n\n## Code memory\n" +
                "Code memory tools are attached to the active GraphMemory instance. " +
                "Use index_code_file after editing source files and find_code_relationships to inspect dependencies. " +
                "Call indexCodebase() to bulk-index common source extensions.");
        }
        else
        {
            AppendToSystemPrompt(
                "\n\n## Code memory\n" +
                "GraphMemory is enabled for this workdir. " +
                "Use index_code_file after editing source files and find_code_relationships to inspect dependencies. " +
                "Call indexCodebase() to bulk-index common source extensions.");
        }

        return RuntimeValue.Null();
    }

    private RuntimeValue IndexCodebase(List<RuntimeValue> args)
    {
        if (_memory == null)
            throw new Exception("indexCodebase() requires code memory — call enableCodeMemory() first");

        EnsureInterpreter();

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".malda", ".js", ".ts", ".py", ".html", ".css", ".json", ".md"
        };
        if (args.Count > 0 && args[0].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[0].AsString()))
        {
            extensions.Clear();
            foreach (var ext in args[0].AsString().Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var normalized = ext.Trim();
                if (!normalized.StartsWith('.'))
                    normalized = "." + normalized;
                extensions.Add(normalized);
            }
        }

        var fullWorkDir = Path.GetFullPath(_workingDirectory);
        if (!Directory.Exists(fullWorkDir))
            return RuntimeValue.Integer(0);

        var indexed = 0;
        foreach (var file in Directory.EnumerateFiles(fullWorkDir, "*.*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!extensions.Contains(Path.GetExtension(file)))
                continue;

            try
            {
                _memory.CallMethod("analyzeFile", new List<RuntimeValue> { RuntimeValue.String(file) }, _interpreter!);
                indexed++;
            }
            catch
            {
            }
        }

        return RuntimeValue.Integer(indexed);
    }
    
    /// <summary>
    /// Registers all development tools for the agent. When readOnly is true, only read-only tools are registered.
    /// </summary>
    private List<string> RegisterAllTools(string workingDirectory, bool includeSymbols, bool readOnly = false, bool prdAuthorOnly = false)
    {
        var names = new List<string>();
        void Register(ToolInstance tool)
        {
            AddTool(tool);
            names.Add(tool.Name);
        }

        if (prdAuthorOnly)
        {
            Register((ToolInstance)BuiltInTools.CreateReadFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateWriteFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGrepTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGlobTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateListDirectoryTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateAskUserTool().AsObject());
            Register((ToolInstance)BuiltInTools.CreateCheckMaldaTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateValidateJsonTool().AsObject());
            if (includeSymbols)
            {
                Register((ToolInstance)BuiltInTools.CreateGetSymbolsTool(workingDirectory).AsObject());
                Register((ToolInstance)BuiltInTools.CreateGetParseErrorsTool(workingDirectory).AsObject());
            }
            return names;
        }

        // 1. File operation tools (read-only: read_file, grep, list_directory only)
        Register((ToolInstance)BuiltInTools.CreateReadFileTool(workingDirectory).AsObject());
        if (!readOnly)
        {
            Register((ToolInstance)BuiltInTools.CreateWriteFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateReplaceInFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateInsertAtLineTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateEditFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateDeleteFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateCopyFileTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateEnsureDirTool(workingDirectory).AsObject());
        }
        Register((ToolInstance)BuiltInTools.CreateGrepTool(workingDirectory).AsObject());
        Register((ToolInstance)BuiltInTools.CreateGlobTool(workingDirectory).AsObject());
        Register((ToolInstance)BuiltInTools.CreateListDirectoryTool(workingDirectory).AsObject());
        Register((ToolInstance)BuiltInTools.CreateWebSearchTool().AsObject());
        Register((ToolInstance)BuiltInTools.CreateWebFetchTool().AsObject());
        
        if (!readOnly)
        {
            // 2. Git operation tools (from GitAgent)
            Register((ToolInstance)BuiltInTools.CreateGitStatusTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitAddTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitCommitTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitLogTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitDiffTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitBranchTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitCheckoutTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitPushTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGitPullTool(workingDirectory).AsObject());
            
            // 3. Command execution
            Register((ToolInstance)BuiltInTools.CreateRunCommandTool(workingDirectory).AsObject());
            
            // 4. User interaction
            Register((ToolInstance)BuiltInTools.CreateAskUserTool().AsObject());
            
            // 5. Structured task planning
            Register((ToolInstance)BuiltInTools.CreateSubmitPlanTool().AsObject());
            Register((ToolInstance)BuiltInTools.CreateUpdatePlanTool().AsObject());
            Register((ToolInstance)BuiltInTools.CreateMarkStepTool().AsObject());
        }
        
        // 6. Code analysis (read-only). check_malda and validate_json are always registered.
        Register((ToolInstance)BuiltInTools.CreateCheckMaldaTool(workingDirectory).AsObject());
        Register((ToolInstance)BuiltInTools.CreateValidateJsonTool().AsObject());
        if (!readOnly)
            Register((ToolInstance)BuiltInTools.CreateTestMaldaTool(workingDirectory).AsObject());
        if (includeSymbols)
        {
            Register((ToolInstance)BuiltInTools.CreateGetSymbolsTool(workingDirectory).AsObject());
            Register((ToolInstance)BuiltInTools.CreateGetParseErrorsTool(workingDirectory).AsObject());
        }

        return names;
    }

    private void AppendToolUsageGuidance(List<string> toolNames, bool readOnly, string workingDirectory, bool prdAuthorOnly = false)
    {
        var names = toolNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("\n\n## Tool calling rules");
        sb.AppendLine("Use ONLY the registered tool names below. Do NOT invoke shell programs as tool names.");
        sb.AppendLine("Available tools: " + string.Join(", ", names) + ".");
        sb.AppendLine("Working directory: " + Path.GetFullPath(workingDirectory) + ".");
        sb.AppendLine("For file tools (read_file, grep, edit_file, etc.), pass paths relative to that directory (e.g. \"PRD.md\", \"snake.html\"). Do not use paths outside this directory.");
        sb.AppendLine("The agent working directory is already set — do not run pwd; use list_directory and grep for file exploration.");
        if (prdAuthorOnly)
        {
            sb.AppendLine("PRD interview mode: use read_file/list_directory/glob/grep to inspect the project, ask_user for clarifications, then write the PRD with write_file only.");
            sb.AppendLine("Do not modify product source files in this session — only the PRD markdown file.");
            sb.AppendLine("Batch read_file, glob, grep, and list_directory calls in one response when exploring — read-only tools run in parallel.");
            sb.Append(AgentPlatformContext.DescribeForAgentPrompt(workingDirectory));
            AppendToSystemPrompt(sb.ToString());
            return;
        }
        sb.AppendLine("For running programs, call run_command with command + args (e.g. dotnet build). Shell wrappers require user confirmation when a UI is available.");
        sb.AppendLine("For reading/writing files, use read_file, edit_file, replace_in_file, write_file, list_directory.");
        sb.AppendLine("Batch multiple read_file, glob, grep, and list_directory calls in one response when exploring — read-only tools run in parallel.");
        sb.AppendLine("Use glob for file discovery by pattern (e.g. **/*.cs); use grep for content search.");
        sb.AppendLine("For git, use git_status, git_add (files='.' or basename paths), git_commit — never run_command with cmd/powershell/git.");
        sb.AppendLine("Do NOT pass repoPath on git tools — they already use the agent working directory above (same as read_file/write_file).");
        sb.AppendLine("After creating a new file, check git_status (untracked). git_diff does not show untracked files; empty git_diff is normal for brand-new files.");
        sb.Append(AgentPlatformContext.DescribeForAgentPrompt(workingDirectory));
        AppendToSystemPrompt(sb.ToString());
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        if (name == "enableCodeMemory" || name == "indexCodebase")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }

        // Use the same property and method access as Agent
        // But update the error message to reference DevAgent
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (Exception ex) when (ex.Message.Contains("Agent"))
        {
            throw new Exception(ex.Message.Replace("Agent", "DevAgent"));
        }
    }
}
