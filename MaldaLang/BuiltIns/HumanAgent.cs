// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Specialized agent for human interaction that automatically includes the ask_user tool.
/// </summary>
public class HumanAgentInstance : AgentInstance
{
    /// <summary>
    /// Creates a new HumanAgent instance with the ask_user tool pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="client">Optional LLM client instance (defaults to local LLM with auto-download from Hugging Face if not provided)</param>
    /// <param name="workingDirectory">Optional working directory (included for consistency, not used by ask_user)</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public HumanAgentInstance(string name, string role, string instructions, LLMClientInstance? client = null, string? workingDirectory = null, IInputProvider? inputProvider = null)
    {
        // Use default local LLM (auto-download from Hugging Face) if client is not provided
        if (client != null)
            Initialize(name, role, instructions, client, null, null, inputProvider);
        else
            Initialize(name, role, instructions, null, DefaultLocalLlm.GetDefaultLocalClient(), null, inputProvider);
        
        // Automatically add the ask_user tool
        AddTool((ToolInstance)BuiltInTools.CreateAskUserTool().AsObject());
    }
    
    /// <summary>
    /// Creates a new HumanAgent instance with the ask_user tool pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="workingDirectory">Optional working directory (included for consistency, not used by ask_user)</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public HumanAgentInstance(string name, string role, string instructions, LlamaCppClientInstance? llamaClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent with LlamaCppClient
        Initialize(name, role, instructions, null, llamaClient, null, inputProvider);
        
        // Automatically add the ask_user tool
        AddTool((ToolInstance)BuiltInTools.CreateAskUserTool().AsObject());
    }
    
    /// <summary>
    /// Creates a new HumanAgent instance with the ask_user tool pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llmClient">Optional LLMClient instance</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="bridgeClient">Optional LLMClientBridge instance</param>
    /// <param name="workingDirectory">Optional working directory (included for consistency, not used by ask_user)</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public HumanAgentInstance(string name, string role, string instructions, LLMClientInstance? llmClient, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent
        Initialize(name, role, instructions, llmClient, llamaClient, bridgeClient, inputProvider);
        
        // Automatically add the ask_user tool
        AddTool((ToolInstance)BuiltInTools.CreateAskUserTool().AsObject());
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Use the same property and method access as Agent
        // But update the error message to reference HumanAgent
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (Exception ex) when (ex.Message.Contains("Agent"))
        {
            throw new Exception(ex.Message.Replace("Agent", "HumanAgent"));
        }
    }
}
