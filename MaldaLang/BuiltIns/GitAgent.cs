// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Specialized agent for git operations that automatically includes all git tools.
/// </summary>
public class GitAgentInstance : AgentInstance
{
    public override string Kind => "GitAgent";

    /// <summary>
    /// Creates a new GitAgent instance with all git operation tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="client">Optional LLM client instance (defaults to local LLM with auto-download from Hugging Face if not provided)</param>
    /// <param name="workingDirectory">Optional working directory for git operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public GitAgentInstance(string name, string role, string instructions, LLMClientInstance? client = null, string? workingDirectory = null, IInputProvider? inputProvider = null)
    {
        // Use default local LLM (auto-download from Hugging Face) if client is not provided
        if (client != null)
            Initialize(name, role, instructions, client, null, null, inputProvider);
        else
            Initialize(name, role, instructions, null, DefaultLocalLlm.GetDefaultLocalClient(), null, inputProvider);
    }
    
    /// <summary>
    /// Creates a new GitAgent instance with all git operation tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="workingDirectory">Optional working directory for git operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public GitAgentInstance(string name, string role, string instructions, LlamaCppClientInstance? llamaClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent with LlamaCppClient
        Initialize(name, role, instructions, null, llamaClient, null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Automatically add all git operation tools
        AddTool((ToolInstance)BuiltInTools.CreateGitStatusTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitAddTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitCommitTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitLogTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitDiffTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitBranchTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitCheckoutTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitPushTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitPullTool(workingDir).AsObject());
    }
    
    /// <summary>
    /// Creates a new GitAgent instance with all git operation tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llmClient">Optional LLMClient instance</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="bridgeClient">Optional LLMClientBridge instance</param>
    /// <param name="workingDirectory">Optional working directory for git operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public GitAgentInstance(string name, string role, string instructions, LLMClientInstance? llmClient, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent
        Initialize(name, role, instructions, llmClient, llamaClient, bridgeClient, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Automatically add all git operation tools
        AddTool((ToolInstance)BuiltInTools.CreateGitStatusTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitAddTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitCommitTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitLogTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitDiffTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitBranchTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitCheckoutTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitPushTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGitPullTool(workingDir).AsObject());
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Use the same property and method access as Agent
        // But update the error message to reference GitAgent
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (Exception ex) when (ex.Message.Contains("Agent"))
        {
            throw new Exception(ex.Message.Replace("Agent", "GitAgent"));
        }
    }
}