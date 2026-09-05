// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;

/// <summary>
/// Specialized agent for coding tasks that automatically includes all file operation tools and command execution tools.
/// </summary>
public class CodingAgentInstance : AgentInstance
{
    public override string Kind => "CodingAgent";

    /// <summary>
    /// Creates a new CodingAgent instance with all file operation tools and command execution tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="client">Optional LLM client instance (defaults to local LLM with auto-download from Hugging Face if not provided)</param>
    /// <param name="workingDirectory">Optional working directory for file operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public CodingAgentInstance(string name, string role, string instructions, LLMClientInstance? client = null, string? workingDirectory = null, IInputProvider? inputProvider = null)
    {
        // Use default local LLM (auto-download from Hugging Face) if client is not provided
        if (client != null)
            Initialize(name, role, instructions, client, null, null, inputProvider);
        else
            Initialize(name, role, instructions, null, DefaultLocalLlm.GetDefaultLocalClient(), null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Automatically add all file operation tools
        AddTool((ToolInstance)BuiltInTools.CreateReadFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWriteFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateReplaceInFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateInsertAtLineTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateEditFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGrepTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGlobTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateListDirectoryTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateRunCommandTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWebSearchTool().AsObject());
    }
    
    /// <summary>
    /// Creates a new CodingAgent instance with all file operation tools and command execution tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="workingDirectory">Optional working directory for file operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public CodingAgentInstance(string name, string role, string instructions, LlamaCppClientInstance? llamaClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent with LlamaCppClient
        Initialize(name, role, instructions, null, llamaClient, null, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Automatically add all file operation tools
        AddTool((ToolInstance)BuiltInTools.CreateReadFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWriteFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateReplaceInFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateInsertAtLineTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateEditFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGrepTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGlobTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateListDirectoryTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateRunCommandTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWebSearchTool().AsObject());
    }
    
    /// <summary>
    /// Creates a new CodingAgent instance with all file operation tools and command execution tools pre-configured.
    /// </summary>
    /// <param name="name">Agent name</param>
    /// <param name="role">Agent role</param>
    /// <param name="instructions">Agent instructions</param>
    /// <param name="llmClient">Optional LLMClient instance</param>
    /// <param name="llamaClient">Optional LlamaCppClient instance</param>
    /// <param name="bridgeClient">Optional LLMClientBridge instance</param>
    /// <param name="workingDirectory">Optional working directory for file operations (defaults to ".")</param>
    /// <param name="inputProvider">Optional input provider for user interaction</param>
    public CodingAgentInstance(string name, string role, string instructions, LLMClientInstance? llmClient, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string? workingDirectory, IInputProvider? inputProvider = null)
    {
        // Initialize the base agent
        Initialize(name, role, instructions, llmClient, llamaClient, bridgeClient, inputProvider);
        
        // Set default working directory if not provided
        var workingDir = workingDirectory ?? ".";
        
        // Automatically add all file operation tools
        AddTool((ToolInstance)BuiltInTools.CreateReadFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWriteFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateReplaceInFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateInsertAtLineTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateEditFileTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGrepTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateGlobTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateListDirectoryTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateRunCommandTool(workingDir).AsObject());
        AddTool((ToolInstance)BuiltInTools.CreateWebSearchTool().AsObject());
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Use the same property and method access as Agent
        // But update the error message to reference CodingAgent
        try
        {
            return base.Get(name, accessingClass);
        }
        catch (Exception ex) when (ex.Message.Contains("Agent"))
        {
            throw new Exception(ex.Message.Replace("Agent", "CodingAgent"));
        }
    }
}