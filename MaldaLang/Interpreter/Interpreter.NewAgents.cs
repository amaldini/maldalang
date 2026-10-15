// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using System.Reflection;
using System.IO;
using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Interpreter.Debug;

public partial class Interpreter
{
    private async Task<RuntimeValue> CreateAgentAsync(List<Expression> args)
    {
        // Agent(name, role, instructions, client?) — client optional; defaults to local LLM
        // (same convention as CodingAgent / GitAgent / HumanAgent).
        if (args.Count < 3 || args.Count > 4)
            throw new RuntimeException("Agent() expects 3 or 4 arguments: (name, role, instructions, client?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("Agent() expects (string, string, string, LLMClient?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        BuiltIns.LlamaCppClientInstance? llamaClient = null;
        BuiltIns.LLMClientBridge.LLMClientBridgeInstance? bridgeClient = null;

        if (args.Count == 4)
        {
            var client = await EvaluateAsync(args[3]);
            if (client.Type == ValueType.Null)
            {
                // Omitted client → default local below.
            }
            else if (client.Type != ValueType.Object)
            {
                throw new RuntimeException("Agent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
            }
            else
            {
                var clientObj = client.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance llm)
                {
                    llmClient = llm;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llama)
                {
                    llamaClient = llama;
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridge)
                {
                    bridgeClient = bridge;
                }
                else
                {
                    throw new RuntimeException("Agent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
            }
        }

        if (llmClient == null && llamaClient == null && bridgeClient == null)
            llamaClient = BuiltIns.DefaultLocalLlm.GetDefaultLocalClient();
        
        var agent = new BuiltIns.AgentInstance();
        agent.Initialize(name.AsString(), role.AsString(), instructions.AsString(), llmClient, llamaClient, bridgeClient, _inputProvider);
        agent.SetInterpreter(this);
        
        return RuntimeValue.Object(agent);
    }
    
    private async Task<RuntimeValue> CreateCodingAgentAsync(List<Expression> args)
    {
        // CodingAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("CodingAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("CodingAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("CodingAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create CodingAgent with LlamaCppClient
                    var llamaCodingAgent = new BuiltIns.CodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    llamaCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaCodingAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create CodingAgent with LLMClientBridge
                    var bridgeCodingAgent = new BuiltIns.CodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeCodingAgent);
                }
                else
                {
                    throw new RuntimeException("CodingAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("CodingAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("CodingAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var codingAgent = new BuiltIns.CodingAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        codingAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(codingAgent);
    }
    
    private async Task<RuntimeValue> CreateGitAgentAsync(List<Expression> args)
    {
        // GitAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("GitAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("GitAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("GitAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create GitAgent with LlamaCppClient
                    var llamaGitAgent = new BuiltIns.GitAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    llamaGitAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaGitAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create GitAgent with LLMClientBridge
                    var bridgeGitAgent = new BuiltIns.GitAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeGitAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeGitAgent);
                }
                else
                {
                    throw new RuntimeException("GitAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("GitAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("GitAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var gitAgent = new BuiltIns.GitAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        gitAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(gitAgent);
    }
    
    private async Task<RuntimeValue> CreateHumanAgentAsync(List<Expression> args)
    {
        // HumanAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("HumanAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("HumanAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("HumanAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create HumanAgent with LlamaCppClient
                    var llamaHumanAgent = new BuiltIns.HumanAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    return RuntimeValue.Object(llamaHumanAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create HumanAgent with LLMClientBridge
                    var bridgeHumanAgent = new BuiltIns.HumanAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeHumanAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeHumanAgent);
                }
                else
                {
                    throw new RuntimeException("HumanAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("HumanAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("HumanAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var humanAgent = new BuiltIns.HumanAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        humanAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(humanAgent);
    }
    
    private async Task<RuntimeValue> CreateDevAgentAsync(List<Expression> args)
    {
        // DevAgent(name, role, instructions, client?, workingDirectory?, includeSymbols?, prdAuthorOnly?)
        if (args.Count < 3)
            throw new RuntimeException("DevAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?, includeSymbols?, prdAuthorOnly?)");
        
        if (args.Count > 7)
            throw new RuntimeException("DevAgent() expects at most 7 arguments: (name, role, instructions, client?, workingDirectory?, includeSymbols?, prdAuthorOnly?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("DevAgent() expects (string, string, string, LLMClient?, string?, bool?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        bool includeSymbols = false;
        bool prdAuthorOnly = false;
        
        if (args.Count == 7)
        {
            var prdAuthorArg = await EvaluateAsync(args[6]);
            if (prdAuthorArg.Type != ValueType.Boolean)
                throw new RuntimeException("DevAgent() prdAuthorOnly argument must be a boolean");
            prdAuthorOnly = prdAuthorArg.AsBoolean();
        }
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
                
                // Check for 5th argument (includeSymbols)
                if (args.Count >= 5)
                {
                    var arg4 = await EvaluateAsync(args[4]);
                    if (arg4.Type != ValueType.Boolean)
                        throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                    includeSymbols = arg4.AsBoolean();
                }
            }
            // If 4th argument is a boolean, it's includeSymbols (client and workingDirectory were omitted)
            else if (arg3.Type == ValueType.Boolean)
            {
                includeSymbols = arg3.AsBoolean();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Handle workingDirectory and includeSymbols for LlamaCppClient
                    if (args.Count >= 5)
                    {
                        var arg4 = await EvaluateAsync(args[4]);
                        if (arg4.Type == ValueType.String)
                        {
                            workingDirectory = arg4.AsString();
                            
                            // Check for 6th argument (includeSymbols)
                            if (args.Count >= 6)
                            {
                                var arg5 = await EvaluateAsync(args[5]);
                                if (arg5.Type != ValueType.Boolean)
                                    throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                                includeSymbols = arg5.AsBoolean();
                            }
                        }
                        else if (arg4.Type == ValueType.Boolean)
                        {
                            includeSymbols = arg4.AsBoolean();
                        }
                        else
                        {
                            throw new RuntimeException("DevAgent() fifth argument must be a string (workingDirectory) or bool (includeSymbols)");
                        }
                    }
                    
                    // Create DevAgent with LlamaCppClient
                    var llamaDevAgent = new BuiltIns.DevAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        includeSymbols,
                        _inputProvider,
                        readOnly: false,
                        prdAuthorOnly: prdAuthorOnly
                    );
                    llamaDevAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaDevAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Handle workingDirectory and includeSymbols for LLMClientBridge
                    if (args.Count >= 5)
                    {
                        var arg4 = await EvaluateAsync(args[4]);
                        if (arg4.Type == ValueType.String)
                        {
                            workingDirectory = arg4.AsString();
                            
                            // Check for 6th argument (includeSymbols)
                            if (args.Count >= 6)
                            {
                                var arg5 = await EvaluateAsync(args[5]);
                                if (arg5.Type != ValueType.Boolean)
                                    throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                                includeSymbols = arg5.AsBoolean();
                            }
                        }
                        else if (arg4.Type == ValueType.Boolean)
                        {
                            includeSymbols = arg4.AsBoolean();
                        }
                        else
                        {
                            throw new RuntimeException("DevAgent() fifth argument must be a string (workingDirectory) or bool (includeSymbols)");
                        }
                    }
                    
                    // Create DevAgent with LLMClientBridge
                    var bridgeDevAgent = new BuiltIns.DevAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        includeSymbols,
                        _inputProvider,
                        readOnly: false,
                        prdAuthorOnly: prdAuthorOnly
                    );
                    bridgeDevAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeDevAgent);
                }
                else
                {
                    throw new RuntimeException("DevAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count >= 5)
                {
                    var arg4 = await EvaluateAsync(args[4]);
                    if (arg4.Type == ValueType.String)
                    {
                        workingDirectory = arg4.AsString();
                        
                        // Check for 6th argument (includeSymbols)
                        if (args.Count >= 6)
                        {
                            var arg5 = await EvaluateAsync(args[5]);
                            if (arg5.Type != ValueType.Boolean)
                                throw new RuntimeException("DevAgent() includeSymbols argument must be a boolean");
                            includeSymbols = arg5.AsBoolean();
                        }
                    }
                    else if (arg4.Type == ValueType.Boolean)
                    {
                        includeSymbols = arg4.AsBoolean();
                    }
                    else
                    {
                        throw new RuntimeException("DevAgent() fifth argument must be a string (workingDirectory) or bool (includeSymbols)");
                    }
                }
            }
            else
            {
                throw new RuntimeException("DevAgent() fourth argument must be an LLMClient, string (workingDirectory), or bool (includeSymbols)");
            }
        }
        
        var devAgent = new BuiltIns.DevAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            includeSymbols,
            _inputProvider,
            readOnly: false,
            prdAuthorOnly: prdAuthorOnly
        );
        devAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(devAgent);
    }
    
    private async Task<RuntimeValue> CreateMALDACodingAgentAsync(List<Expression> args)
    {
        // MALDACodingAgent(name, role, instructions, client?, workingDirectory?)
        if (args.Count < 3)
            throw new RuntimeException("MALDACodingAgent() expects at least 3 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        if (args.Count > 5)
            throw new RuntimeException("MALDACodingAgent() expects at most 5 arguments: (name, role, instructions, client?, workingDirectory?)");
        
        var name = await EvaluateAsync(args[0]);
        var role = await EvaluateAsync(args[1]);
        var instructions = await EvaluateAsync(args[2]);
        
        if (name.Type != ValueType.String || role.Type != ValueType.String || 
            instructions.Type != ValueType.String)
            throw new RuntimeException("MALDACodingAgent() expects (string, string, string, LLMClient?, string?)");
        
        BuiltIns.LLMClientInstance? llmClient = null;
        string? workingDirectory = null;
        
        // Determine which arguments are provided
        if (args.Count >= 4)
        {
            var arg3 = await EvaluateAsync(args[3]);
            
            // If 4th argument is a string, it's the working directory (client was omitted)
            if (arg3.Type == ValueType.String)
            {
                workingDirectory = arg3.AsString();
            }
            // If 4th argument is an object, it's the client
            else if (arg3.Type == ValueType.Object)
            {
                var clientObj = arg3.AsObject();
                if (clientObj is BuiltIns.LLMClientInstance clientInstance)
                {
                    llmClient = clientInstance;
                }
                else if (clientObj is BuiltIns.LlamaCppClientInstance llamaClientInstance)
                {
                    // Create MALDACodingAgent with LlamaCppClient
                    var llamaMaldaCodingAgent = new BuiltIns.MALDACodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        llamaClientInstance,
                        workingDirectory,
                        _inputProvider
                    );
                    llamaMaldaCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(llamaMaldaCodingAgent);
                }
                else if (clientObj is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
                {
                    // Create MALDACodingAgent with LLMClientBridge
                    var bridgeMaldaCodingAgent = new BuiltIns.MALDACodingAgentInstance(
                        name.AsString(),
                        role.AsString(),
                        instructions.AsString(),
                        null,
                        null,
                        bridgeClient,
                        workingDirectory ?? ".",
                        _inputProvider
                    );
                    bridgeMaldaCodingAgent.SetInterpreter(this);
                    return RuntimeValue.Object(bridgeMaldaCodingAgent);
                }
                else
                {
                    throw new RuntimeException("MALDACodingAgent() fourth argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
                }
                
                // Check if there's a 5th argument for working directory
                if (args.Count == 5)
                {
                    var workingDirValue = await EvaluateAsync(args[4]);
                    if (workingDirValue.Type != ValueType.String)
                        throw new RuntimeException("MALDACodingAgent() workingDirectory argument must be a string");
                    workingDirectory = workingDirValue.AsString();
                }
            }
            else
            {
                throw new RuntimeException("MALDACodingAgent() fourth argument must be an LLMClient or string (workingDirectory)");
            }
        }
        
        var maldaCodingAgent = new BuiltIns.MALDACodingAgentInstance(
            name.AsString(), 
            role.AsString(), 
            instructions.AsString(), 
            llmClient, 
            workingDirectory, 
            _inputProvider
        );
        maldaCodingAgent.SetInterpreter(this);
        
        return RuntimeValue.Object(maldaCodingAgent);
    }
}
