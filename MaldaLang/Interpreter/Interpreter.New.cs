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
    private async Task<RuntimeValue> EvaluateNewAsync(NewExpression expr)
    {
        // Check for built-in classes first
        if (expr.ClassName == "LLMClient")
        {
            return await CreateLLMClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "OpenRouterClient")
        {
            return await CreateOpenRouterClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LlamaCppClient")
        {
            return await CreateLlamaCppClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LlamaEmbedder")
        {
            return await CreateLlamaEmbedderAsync(expr.Arguments);
        }
        else if (expr.ClassName == "OnnxModel")
        {
            return await CreateOnnxModelAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Dense")
        {
            return await CreateDenseAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Sequential")
        {
            return await CreateSequentialAsync(expr.Arguments);
        }
        else if (expr.ClassName is "Conv" or "Embedding" or "Rnn" or "LayerNorm" or "Attention"
                 or "Conv2D" or "MaxPool2D" or "AvgPool2D" or "Flatten" or "BatchNorm2D" or "Dropout2D"
                 or "GlobalAvgPool2D" or "GlobalMaxPool2D" or "Activation" or "Parallel" or "Identity")
        {
            return await CreateNeuralLayerAsync(expr.ClassName, expr.Arguments);
        }
        else if (expr.ClassName == "Conversation")
        {
            return await CreateConversationAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Tool")
        {
            return await CreateToolAsync(expr.Arguments);
        }
        else if (expr.ClassName == "Agent")
        {
            return await CreateAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "CodingAgent")
        {
            return await CreateCodingAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "GitAgent")
        {
            return await CreateGitAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "DevAgent")
        {
            return await CreateDevAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "MALDACodingAgent")
        {
            return await CreateMALDACodingAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "HumanAgent")
        {
            return await CreateHumanAgentAsync(expr.Arguments);
        }
        else if (expr.ClassName == "HTMLCache")
        {
            return await CreateHTMLCacheAsync(expr.Arguments);
        }
        else if (expr.ClassName == "RestServer")
        {
            return await CreateRestServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "RestClient")
        {
            return await CreateRestClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "HttpServer")
        {
            return await CreateHttpServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "MCPServer")
        {
            return await CreateMCPServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "MCPClient")
        {
            return await CreateMCPClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ACPClient")
        {
            return await CreateACPClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ACPServer")
        {
            return await CreateACPServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ACPAgentTool")
        {
            return await CreateACPAgentToolAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LLMClientBridge")
        {
            return await CreateLLMClientBridgeAsync(expr.Arguments);
        }
        else if (expr.ClassName == "LLMServer")
        {
            return await CreateLLMServerAsync(expr.Arguments);
        }
        else if (expr.ClassName == "SqlServerClient")
        {
            return await CreateSqlServerClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "PostgresClient")
        {
            return await CreatePostgresClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "SqliteClient")
        {
            return await CreateSqliteClientAsync(expr.Arguments);
        }
        else if (expr.ClassName == "SerialConnection")
        {
            return await CreateSerialConnectionAsync(expr.Arguments);
        }
        else if (expr.ClassName == "ArduinoConnection")
        {
            return await CreateArduinoConnectionAsync(expr.Arguments);
        }
        else if (expr.ClassName == "VectorDB")
        {
            var vectorDbArguments = new List<RuntimeValue>();
            foreach (var arg in expr.Arguments)
            {
                vectorDbArguments.Add(await EvaluateAsync(arg));
            }
            return await CallConstructorAsync(VectorDBClassDefinition.Instance, vectorDbArguments);
        }
        else if (expr.ClassName == "GraphMemory")
        {
            var graphMemoryArguments = new List<RuntimeValue>();
            foreach (var arg in expr.Arguments)
            {
                graphMemoryArguments.Add(await EvaluateAsync(arg));
            }
            return await CallConstructorAsync(GraphMemoryClassDefinition.Instance, graphMemoryArguments);
        }
        
        if (!_classes.ContainsKey(expr.ClassName))
            throw new RuntimeException($"Class '{expr.ClassName}' not found.");
        
        var klass = _classes[expr.ClassName];
        var arguments = new List<RuntimeValue>();
        foreach (var arg in expr.Arguments)
        {
            arguments.Add(await EvaluateAsync(arg));
        }
        
        return await CallConstructorAsync(klass, arguments);
    }
    
    private async Task<RuntimeValue> CreateLLMClientAsync(List<Expression> args)
    {
        if (args.Count < 3)
            throw new RuntimeException("LLMClient() expects 3 arguments: (apiUrl, apiKey, model)");
        
        var apiUrl = await EvaluateAsync(args[0]);
        var apiKey = await EvaluateAsync(args[1]);
        var model = await EvaluateAsync(args[2]);
        
        if (apiUrl.Type != ValueType.String || apiKey.Type != ValueType.String || model.Type != ValueType.String)
            throw new RuntimeException("LLMClient() expects (string, string, string)");
        
        var client = new BuiltIns.LLMClientInstance();
        client.ApiUrl = apiUrl.AsString();
        client.ApiKey = apiKey.AsString();
        client.Model = model.AsString();
        
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateOpenRouterClientAsync(List<Expression> args)
    {
        // OpenRouterClient takes 0 or 1 argument (optional model name)
        if (args.Count > 1)
            throw new RuntimeException("OpenRouterClient() expects 0 or 1 argument: (model?)");
        
        string? model = null;
        if (args.Count == 1)
        {
            var modelValue = await EvaluateAsync(args[0]);
            if (modelValue.Type != ValueType.String)
                throw new RuntimeException("OpenRouterClient() model argument must be a string");
            model = modelValue.AsString();
        }
        
        var client = new BuiltIns.OpenRouterClientInstance(model);
        
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateLlamaCppClientAsync(List<Expression> args)
    {
        var client = new BuiltIns.LlamaCppClientInstance();
        
        if (args.Count == 0)
            return RuntimeValue.Object(client);
        
        if (args.Count != 1)
            throw new RuntimeException("LlamaCppClient() expects 0 or 1 argument: (modelPath?)");
        
        var modelPath = await EvaluateAsync(args[0]);
        if (modelPath.Type != ValueType.String)
            throw new RuntimeException("LlamaCppClient() expects an optional string modelPath");
        
        client.ModelPath = modelPath.AsString();
        
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateLlamaEmbedderAsync(List<Expression> args)
    {
        // LlamaEmbedder takes 1 argument: modelPath (string)
        if (args.Count < 1)
            throw new RuntimeException("LlamaEmbedder() expects 1 argument: (modelPath)");
        
        var modelPath = await EvaluateAsync(args[0]);
        if (modelPath.Type != ValueType.String)
            throw new RuntimeException("LlamaEmbedder() expects (string)");
        
        var embedder = new BuiltIns.LlamaEmbedderInstance();
        embedder.ModelPath = modelPath.AsString();
        
        return RuntimeValue.Object(embedder);
    }

    private async Task<RuntimeValue> CreateOnnxModelAsync(List<Expression> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("OnnxModel() expects 1 argument: (path)");

        var pathValue = await EvaluateAsync(args[0]);
        if (pathValue.Type != ValueType.String)
            throw new RuntimeException("OnnxModel() expects 1 argument: (path)");

        return RuntimeValue.Object(new BuiltIns.OnnxModelInstance(pathValue.AsString()));
    }

    private async Task<RuntimeValue> CreateDenseAsync(List<Expression> args)
    {
        var values = new List<RuntimeValue>(args.Count);
        foreach (var arg in args)
            values.Add(await EvaluateAsync(arg));
        return RuntimeValue.Object(new BuiltIns.DenseInstance(values));
    }

    private async Task<RuntimeValue> CreateSequentialAsync(List<Expression> args)
    {
        var values = new List<RuntimeValue>(args.Count);
        foreach (var arg in args)
            values.Add(await EvaluateAsync(arg));
        return RuntimeValue.Object(new BuiltIns.SequentialInstance(values));
    }

    private async Task<RuntimeValue> CreateNeuralLayerAsync(string className, List<Expression> args)
    {
        var values = new List<RuntimeValue>(args.Count);
        foreach (var arg in args)
            values.Add(await EvaluateAsync(arg));
        ObjectInstance instance = className switch
        {
            "Conv" => new BuiltIns.ConvInstance(values),
            "Conv2D" => new BuiltIns.Conv2DInstance(values),
            "MaxPool2D" => new BuiltIns.MaxPool2DInstance(values),
            "AvgPool2D" => new BuiltIns.AvgPool2DInstance(values),
            "Flatten" => new BuiltIns.FlattenInstance(values),
            "BatchNorm2D" => new BuiltIns.BatchNorm2DInstance(values),
            "Dropout2D" => new BuiltIns.Dropout2DInstance(values),
            "GlobalAvgPool2D" => new BuiltIns.GlobalAvgPool2DInstance(values),
            "GlobalMaxPool2D" => new BuiltIns.GlobalMaxPool2DInstance(values),
            "Activation" => new BuiltIns.ActivationInstance(values),
            "Parallel" => new BuiltIns.ParallelInstance(values),
            "Identity" => new BuiltIns.IdentityInstance(values),
            "Embedding" => new BuiltIns.EmbeddingInstance(values),
            "Rnn" => new BuiltIns.RnnInstance(values),
            "LayerNorm" => new BuiltIns.LayerNormInstance(values),
            _ => new BuiltIns.AttentionInstance(values)
        };
        return RuntimeValue.Object(instance);
    }
    
    private async Task<RuntimeValue> CreateConversationAsync(List<Expression> args)
    {
        if (args.Count < 2)
            throw new RuntimeException("Conversation() expects 2 arguments: (client, systemPrompt)");
        
        var client = await EvaluateAsync(args[0]);
        var systemPrompt = await EvaluateAsync(args[1]);
        
        if (client.Type != ValueType.Object || systemPrompt.Type != ValueType.String)
            throw new RuntimeException("Conversation() expects (LLMClient, string)");
        
        var clientObj = client.AsObject();
        BuiltIns.LLMClientInstance? llmClient = null;
        BuiltIns.LlamaCppClientInstance? llamaClient = null;
        BuiltIns.LLMClientBridge.LLMClientBridgeInstance? bridgeClient = null;
        
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
            throw new RuntimeException("Conversation() first argument must be an LLMClient, LlamaCppClient, or LLMClientBridge");
        }
        
        var conv = new BuiltIns.ConversationInstance();
        conv.Initialize(llmClient, llamaClient, bridgeClient, systemPrompt.AsString(), _inputProvider);
        
        return RuntimeValue.Object(conv);
    }
    
    private async Task<RuntimeValue> CreateToolAsync(List<Expression> args)
    {
        if (args.Count < 4)
            throw new RuntimeException("Tool() expects 4 arguments: (name, description, parameters, handler)");
        
        var name = await EvaluateAsync(args[0]);
        var description = await EvaluateAsync(args[1]);
        var parameters = await EvaluateAsync(args[2]);
        var handler = await EvaluateAsync(args[3]);
        
        if (name.Type != ValueType.String || description.Type != ValueType.String || 
            parameters.Type != ValueType.Object)
            throw new RuntimeException("Tool() expects (string, string, object, function?)");
        
        var tool = new BuiltIns.ToolInstance();
        // Keep schema on Initialize; wire the callable handler separately so Conversation
        // / tool.execute() can invoke it (same path as @Tool registration).
        tool.Initialize(name.AsString(), description.AsString(), parameters, null);
        if (handler.Type == ValueType.Function)
            tool.SetFunctionHandler(handler.AsFunction(), this);

        return RuntimeValue.Object(tool);
    }
}
