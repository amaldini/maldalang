// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Specialized agent for MALDA script development and toolchain operations that automatically includes all file operation tools and MALDA execution/compilation tools.
/// </summary>
public class MALDACodingAgentInstance : AgentInstance
{
    /// <summary>
    /// Creates a new MALDACodingAgent instance with all MALDA development tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="client">Optional LLM client instance (defaults to local LLM with auto-download from Hugging Face if not provided)</param>
    /// <param name="workingDirectory">Optional working directory for operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public MALDACodingAgentInstance(string name, string role, string instructions, LLMClientInstance? client = null, string? workingDirectory = null, IInputProvider? inputProvider = null)
    {
        // Use default local LLM (auto-download from Hugging Face) if client is not provided
        if (client != null)
            Initialize(name, role, instructions, client, null, null, inputProvider);
        else
            Initialize(name, role, instructions, null, DefaultLocalLlm.GetDefaultLocalClient(), null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Register all MALDA development tools
        RegisterAllTools(workingDir);
    }
    
    /// <summary>
    /// Creates a new MALDACodingAgent instance with all MALDA development tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="workingDirectory">Optional working directory for operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public MALDACodingAgentInstance(string name, string role, string instructions, LlamaCppClientInstance? llamaClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent with LlamaCppClient
        Initialize(name, role, instructions, null, llamaClient, null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Register all MALDA development tools
        RegisterAllTools(workingDir);
    }
    
    /// <summary>
    /// Creates a new MALDACodingAgent instance with all MALDA development tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llmClient">Optional LLMClient instance</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="bridgeClient">Optional LLMClientBridge instance</param>
    /// <param name="workingDirectory">Optional working directory for operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public MALDACodingAgentInstance(string name, string role, string instructions, LLMClientInstance? llmClient, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent
        Initialize(name, role, instructions, llmClient, llamaClient, bridgeClient, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Register all MALDA development tools
        RegisterAllTools(workingDir);
    }
    
    /// <summary>
    /// Registers all MALDA development tools for the agent.
    /// </summary>
    private void RegisterAllTools(string workingDirectory)
    {
        // 1. File operation tools (for editing MALDA scripts)
        AddTool((ToolInstance)BuiltInTools.CreateReadFileTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWriteFileTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateReplaceInFileTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateInsertAtLineTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateEditFileTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGrepTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGlobTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateListDirectoryTool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWebSearchTool().AsObject());
        
        // 2. MALDA execution and compilation tools
        AddTool((ToolInstance)BuiltInTools.CreateRunMALDATool(workingDirectory).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateCompileMALDATool(workingDirectory).AsObject());
        
        // 3. MCP agent script generation tool
        AddTool((ToolInstance)BuiltInTools.CreateCreateMcpAgentScriptTool(workingDirectory).AsObject());
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Use the same property and method access as Agent
        // But update the error message to reference MALDACodingAgent
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (Exception ex) when (ex.Message.Contains("Agent"))
        {
            throw new Exception(ex.Message.Replace("Agent", "MALDACodingAgent"));
        }
    }
}
