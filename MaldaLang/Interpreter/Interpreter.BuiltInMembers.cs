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
    private RuntimeValue HandleBuiltInMemberAccess(ObjectInstance instance, string member)
    {
        // Handle property access
        if (instance is BuiltIns.OpenRouterClientInstance openRouterProps)
        {
            if (member == "httpReferer")
                return RuntimeValue.String(openRouterProps.HttpReferer ?? "");
            if (member == "appTitle")
                return RuntimeValue.String(openRouterProps.AppTitle ?? "");
            if (member == "appCategories")
                return RuntimeValue.String(openRouterProps.AppCategories ?? "");
            // Shared LLMClient properties fall through below.
        }

        if (instance is BuiltIns.LLMClientInstance llmClient)
        {
            if (member == "apiUrl")
                return RuntimeValue.String(llmClient.ApiUrl);
            if (member == "apiKey")
                return RuntimeValue.String(llmClient.ApiKey);
            if (member == "model")
                return RuntimeValue.String(llmClient.Model);
            if (member == "temperature")
                return RuntimeValue.Float(llmClient.Temperature);
            if (member == "maxTokens")
                return RuntimeValue.Integer(llmClient.MaxTokens);
        }
        else if (instance is BuiltIns.LlamaCppClientInstance llamaClient)
        {
            if (member == "modelPath")
                return RuntimeValue.String(llamaClient.ModelPath);
            if (member == "temperature")
                return RuntimeValue.Float(llamaClient.Temperature);
            if (member == "maxTokens")
                return RuntimeValue.Integer(llamaClient.MaxTokens);
        }
        else if (instance is BuiltIns.LLMServerInstance llmServer)
        {
            if (member == "port")
                return llmServer.Get("port", null);
            if (member == "host")
                return llmServer.Get("host", null);
            if (member == "isRunning")
                return llmServer.Get("isRunning", null);
            if (member == "bridge")
                return llmServer.Get("bridge", null);
        }
        else if (instance is BuiltIns.ConversationInstance conv)
        {
            // Conversation doesn't expose properties directly
        }
        else if (instance is BuiltIns.ToolInstance tool)
        {
            if (member == "name")
                return RuntimeValue.String(tool.Name);
            if (member == "description")
                return RuntimeValue.String(tool.Description);
        }
        else if (instance is BuiltIns.AgentInstance agent)
        {
            if (member == "name")
                return RuntimeValue.String(agent.Name);
            if (member == "role")
                return RuntimeValue.String(agent.Role);
            if (member == "instructions")
                return RuntimeValue.String(agent.Instructions);
            if (member == "kind")
                return RuntimeValue.String(agent.Kind);
        }
        else if (instance is BuiltIns.CodingAgentInstance codingAgent)
        {
            // CodingAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(codingAgent.Name);
            if (member == "role")
                return RuntimeValue.String(codingAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(codingAgent.Instructions);
            if (member == "kind")
                return RuntimeValue.String(codingAgent.Kind);
        }
        else if (instance is BuiltIns.GitAgentInstance gitAgent)
        {
            // GitAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(gitAgent.Name);
            if (member == "role")
                return RuntimeValue.String(gitAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(gitAgent.Instructions);
            if (member == "kind")
                return RuntimeValue.String(gitAgent.Kind);
        }
        else if (instance is BuiltIns.DevAgentInstance devAgent)
        {
            // DevAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(devAgent.Name);
            if (member == "role")
                return RuntimeValue.String(devAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(devAgent.Instructions);
            if (member == "kind")
                return RuntimeValue.String(devAgent.Kind);
        }
        else if (instance is BuiltIns.MALDACodingAgentInstance maldaCodingAgent)
        {
            // MALDACodingAgentInstance inherits from AgentInstance, so it has the same properties
            if (member == "name")
                return RuntimeValue.String(maldaCodingAgent.Name);
            if (member == "role")
                return RuntimeValue.String(maldaCodingAgent.Role);
            if (member == "instructions")
                return RuntimeValue.String(maldaCodingAgent.Instructions);
            if (member == "kind")
                return RuntimeValue.String(maldaCodingAgent.Kind);
        }
        else if (instance is BuiltIns.SqlServerClientInstance sqlServerClient)
        {
            if (member == "isConnected")
                return sqlServerClient.Get("isConnected", null);
        }
        else if (instance is BuiltIns.PostgresClientInstance postgresClient)
        {
            if (member == "isConnected")
                return postgresClient.Get("isConnected", null);
        }
        else if (instance is BuiltIns.SqliteClientInstance sqliteClient)
        {
            if (member == "isConnected")
                return sqliteClient.Get("isConnected", null);
        }
        else if (instance is BuiltIns.DotNetObjectInstance dotNetObj)
        {
            // DotNetObjectInstance properties are handled earlier; if we get here,
            // treat as a method and fall through to wrapper creation.
        }
        else if (instance is BuiltIns.DotNetTypeInstance dotNetType)
        {
            // DotNetTypeInstance only exposes static methods via wrapper.
        }

        // Create a wrapper function that calls the built-in method
        var wrapper = new FunctionValue(null, null, false, null);
        wrapper.BuiltInInstance = instance;
        wrapper.BuiltInMethod = member;
        return RuntimeValue.Function(wrapper);
    }
    
    private RuntimeValue CallBuiltInMethod(ObjectInstance instance, string methodName, List<RuntimeValue> arguments)
    {
        if (instance is BuiltIns.LLMClientInstance llmClient)
        {
            return llmClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.OpenRouterClientInstance openRouterClient)
        {
            // OpenRouterClientInstance inherits from LLMClientInstance, so it can use the same CallMethod
            return openRouterClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LlamaCppClientInstance llamaClient)
        {
            return llamaClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LlamaEmbedderInstance llamaEmbedder)
        {
            return llamaEmbedder.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.OnnxModelInstance onnxModel)
        {
            return onnxModel.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.DenseInstance dense)
        {
            return dense.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.SequentialInstance sequential)
        {
            return sequential.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ParallelInstance parallel)
        {
            return parallel.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.IdentityInstance identity)
        {
            return identity.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ConvInstance convLayer)
        {
            return convLayer.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.Conv2DInstance conv2d)
        {
            return conv2d.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.MaxPool2DInstance maxPool)
        {
            return maxPool.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.AvgPool2DInstance avgPool)
        {
            return avgPool.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.FlattenInstance flatten)
        {
            return flatten.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.BatchNorm2DInstance batchNorm)
        {
            return batchNorm.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.Dropout2DInstance dropout2d)
        {
            return dropout2d.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.GlobalAvgPool2DInstance globalAvgPool)
        {
            return globalAvgPool.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.GlobalMaxPool2DInstance globalMaxPool)
        {
            return globalMaxPool.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ActivationInstance activationLayer)
        {
            return activationLayer.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.EmbeddingInstance embedding)
        {
            return embedding.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.RnnInstance rnn)
        {
            return rnn.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LayerNormInstance layerNorm)
        {
            return layerNorm.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.AttentionInstance attention)
        {
            return attention.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeClient)
        {
            return bridgeClient.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.LLMServerInstance llmServer)
        {
            return llmServer.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ConversationInstance conv)
        {
            return conv.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.ToolInstance tool)
        {
            return tool.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.AgentInstance agent)
        {
            return agent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.CodingAgentInstance codingAgent)
        {
            // CodingAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return codingAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.GitAgentInstance gitAgent)
        {
            // GitAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return gitAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.DevAgentInstance devAgent)
        {
            // DevAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return devAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MALDACodingAgentInstance maldaCodingAgent)
        {
            // MALDACodingAgentInstance inherits from AgentInstance, so it can use the same CallMethod
            return maldaCodingAgent.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MCPClientInstance mcpClient)
        {
            return mcpClient.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.HTMLCacheInstance htmlCache)
        {
            return htmlCache.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RestServerInstance restServer)
        {
            return restServer.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.HttpServerInstance httpServer)
        {
            return httpServer.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RequestContextInstance requestContext)
        {
            return requestContext.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RequestAuthContextInstance requestAuthContext)
        {
            return requestAuthContext.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RequestSessionContextInstance requestSession)
        {
            return requestSession.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.ResponseContextInstance responseContext)
        {
            return responseContext.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MiddlewareNextCallbackInstance nextCallback)
        {
            return nextCallback.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.MCPServerInstance mcpServer)
        {
            return mcpServer.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.SqlServerClientInstance sqlServerClient)
        {
            return sqlServerClient.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.PostgresClientInstance postgresClient)
        {
            return postgresClient.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.SqliteClientInstance sqliteClient)
        {
            return sqliteClient.CallMethod(methodName, arguments);
        }
        else if (instance is ArrayInstance array)
        {
            return array.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.JsonObject jsonObject)
        {
            return jsonObject.CallMethod(methodName, arguments, this);
        }
        else if (instance is DictionaryInstance dict)
        {
            return dict.CallMethod(methodName, arguments, this);
        }
        else if (instance is GraphInstance graph)
        {
            return graph.CallMethod(methodName, arguments, this);
        }
        else if (instance is VectorDBInstance vectorDB)
        {
            return vectorDB.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.RetrieverInstance retriever)
        {
            return retriever.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ComposedPipeInstance composedPipe)
        {
            return composedPipe.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.GraphMemoryInstance graphMemory)
        {
            return graphMemory.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.PromptInstance promptInstance)
        {
            return promptInstance.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ProgressContextWrapper progressCtx)
        {
            return progressCtx.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.MathInstance math)
        {
            return math.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.StrInstance strModule)
        {
            return strModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.IoInstance ioModule)
        {
            return ioModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.PdfInstance pdfModule)
        {
            return pdfModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.DocInstance docModule)
        {
            return docModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ResultInstance resultModule)
        {
            return resultModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.OptionInstance optionModule)
        {
            return optionModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.GroundedInstance groundedModule)
        {
            return groundedModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.CapInstance capModule)
        {
            return capModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.AgentsInstance agentsModule)
        {
            return agentsModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.TraceInstance traceModule)
        {
            return traceModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.NnInstance nnModule)
        {
            return nnModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.BufInstance bufModule)
        {
            return bufModule.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.DeclaredContextInstance declaredContext)
        {
            return declaredContext.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.AgentTeamInstance agentTeam)
        {
            return agentTeam.CallMethod(methodName, arguments, this);
        }
        else if (instance.GetType().FullName == "MaldaLang.Timeseries.TaInstance")
        {
            var result = instance.GetType().GetMethod("CallMethod")?.Invoke(instance, new object[] { methodName, arguments, this });
            return result is RuntimeValue runtimeValue ? runtimeValue : RuntimeValue.Null();
        }
        else if (instance is BuiltIns.UiFrameworkInstance uiFramework)
        {
            return uiFramework.CallMethod(methodName, arguments);
        }
        else if (instance is ActorReferenceWrapper actorRefWrapper)
        {
            if (methodName == "stop")
            {
                if (arguments.Count != 0)
                {
                    throw new RuntimeException("stop() expects 0 arguments");
                }
                actorRefWrapper.ActorReference.Stop();
                return RuntimeValue.Null();
            }
            throw new RuntimeException($"ActorReference has no method '{methodName}'. Available methods: stop()");
        }
        else if (instance is BuiltIns.DotNetObjectInstance dotNetObj)
        {
            return dotNetObj.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.DotNetTypeInstance dotNetType)
        {
            return dotNetType.CallStaticMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.AnsiConsoleInstance ansiConsole)
        {
            // Check if it's an async method
            if (methodName == "status" || methodName == "prompt" || methodName == "progress")
            {
                throw new RuntimeException($"AnsiConsole.{methodName}() is an async method and must be awaited");
            }
            return ansiConsole.CallMethod(methodName, arguments, this);
        }
        else if (instance is BuiltIns.ArduinoConnectionInstance arduinoConnection)
        {
            return arduinoConnection.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.SerialConnectionInstance serialConnection)
        {
            return serialConnection.CallMethod(methodName, arguments);
        }
        else if (instance is BuiltIns.RestClientInstance restClient)
        {
            return restClient.CallMethod(methodName, arguments);
        }
        throw new RuntimeException($"Unknown built-in method: {methodName}");
    }
    
    private async Task<RuntimeValue> CallBuiltInMethodAsync(ObjectInstance instance, string methodName, List<RuntimeValue> arguments)
    {
        if (instance is BuiltIns.AnsiConsoleInstance ansiConsole)
        {
            if (methodName == "status" || methodName == "prompt" || methodName == "progress")
            {
                return await ansiConsole.CallMethodAsync(methodName, arguments, this);
            }
            // Fall back to sync method
            return ansiConsole.CallMethod(methodName, arguments, this);
        }
        if (instance is BuiltIns.StdLibModuleInstance stdLibModule &&
            BuiltIns.StdLibModuleInstance.RequiresAsyncCall(methodName))
        {
            return await stdLibModule.CallMethodAsync(methodName, arguments, this);
        }
        if (instance is BuiltIns.UiFrameworkInstance uiFramework && methodName == "run")
        {
            // ui.run awaits browser events and calls back into the program.
            return await uiFramework.CallMethodAsync(methodName, arguments, this);
        }
        // For other instances, try sync method
        return CallBuiltInMethod(instance, methodName, arguments);
    }
}
