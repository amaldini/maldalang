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
    private async Task<RuntimeValue> CreateRestServerAsync(List<Expression> args)
    {
        if (args.Count > 2)
            throw new RuntimeException("RestServer() expects 0-2 arguments: (port?, host?)");

        if (args.Count == 0)
        {
            var deferred = new BuiltIns.RestServerInstance(0, null, this);
            return RuntimeValue.Object(deferred);
        }
        
        var port = await EvaluateAsync(args[0]);
        if (port.Type != ValueType.Integer)
            throw new RuntimeException("RestServer() port must be an integer");
        
        var portNum = port.AsInteger();
        if (portNum != 0 && (portNum < 1 || portNum > 65535))
            throw new RuntimeException("RestServer() port must be 0 (deferred/mounted) or between 1 and 65535");
        
        string? host = null;
        if (args.Count == 2)
        {
            var hostValue = await EvaluateAsync(args[1]);
            if (hostValue.Type != ValueType.String)
                throw new RuntimeException("RestServer() host must be a string");
            host = hostValue.AsString();
        }
        
        var server = new BuiltIns.RestServerInstance(portNum, host, this);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateRestClientAsync(List<Expression> args)
    {
        string? baseUrl = null;
        int? timeout = null;
        
        if (args.Count > 0)
        {
            var baseUrlValue = await EvaluateAsync(args[0]);
            if (baseUrlValue.Type != ValueType.String)
                throw new RuntimeException("RestClient() baseUrl must be a string");
            baseUrl = baseUrlValue.AsString();
        }
        
        if (args.Count > 1)
        {
            var timeoutValue = await EvaluateAsync(args[1]);
            if (timeoutValue.Type != ValueType.Integer)
                throw new RuntimeException("RestClient() timeout must be an integer");
            timeout = timeoutValue.AsInteger();
        }
        
        var client = new BuiltIns.RestClientInstance(baseUrl, timeout);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateHttpServerAsync(List<Expression> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new RuntimeException("HttpServer() expects 1-4 arguments: (port, webDirectory?, pathBase?, host?)");
        
        var port = await EvaluateAsync(args[0]);
        if (port.Type != ValueType.Integer)
            throw new RuntimeException("HttpServer() port must be an integer");
        
        var portNum = port.AsInteger();
        if (portNum < 1 || portNum > 65535)
            throw new RuntimeException("HttpServer() port must be between 1 and 65535");
        
        string? webDirectory = null;
        if (args.Count >= 2)
        {
            var webDirValue = await EvaluateAsync(args[1]);
            if (webDirValue.Type != ValueType.Null && webDirValue.Type != ValueType.String)
                throw new RuntimeException("HttpServer() webDirectory must be a string or null");
            webDirectory = webDirValue.Type == ValueType.String ? webDirValue.AsString() : null;
        }
        
        string? pathBase = null;
        if (args.Count >= 3)
        {
            var pathBaseValue = await EvaluateAsync(args[2]);
            if (pathBaseValue.Type != ValueType.Null && pathBaseValue.Type != ValueType.String)
                throw new RuntimeException("HttpServer() pathBase must be a string or null");
            pathBase = pathBaseValue.Type == ValueType.String ? pathBaseValue.AsString() : null;
        }

        string? host = null;
        if (args.Count >= 4)
        {
            var hostValue = await EvaluateAsync(args[3]);
            if (hostValue.Type != ValueType.Null && hostValue.Type != ValueType.String)
                throw new RuntimeException("HttpServer() host must be a string or null");
            host = hostValue.Type == ValueType.String ? hostValue.AsString() : null;
        }
        
        var server = new BuiltIns.HttpServerInstance(portNum, webDirectory, this, pathBase, host);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateMCPServerAsync(List<Expression> args)
    {
        // MCPServer(transportType?, port?)
        // transportType: "stdio" (default) or "http"
        // port: Required for HTTP transport
        
        string? transportType = null;
        int? port = null;
        
        if (args.Count > 0)
        {
            var transportValue = await EvaluateAsync(args[0]);
            if (transportValue.Type != ValueType.String)
                throw new RuntimeException("MCPServer() transportType must be a string");
            transportType = transportValue.AsString();
            
            if (transportType != "stdio" && transportType != "http")
                throw new RuntimeException("MCPServer() transportType must be 'stdio' or 'http'");
        }
        
        if (args.Count > 1)
        {
            var portValue = await EvaluateAsync(args[1]);
            if (portValue.Type != ValueType.Integer)
                throw new RuntimeException("MCPServer() port must be an integer");
            
            var portNum = portValue.AsInteger();
            if (portNum < 1 || portNum > 65535)
                throw new RuntimeException("MCPServer() port must be between 1 and 65535");
            
            port = portNum;
        }
        
        if (transportType == "http" && port == null)
            throw new RuntimeException("MCPServer() port is required when using 'http' transport");
        
        var server = new BuiltIns.MCPServerInstance(transportType, port, this);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateMCPClientAsync(List<Expression> args)
    {
        // MCPClient(serverName)
        if (args.Count != 1)
            throw new RuntimeException("MCPClient() expects 1 argument: (serverName)");
        
        var serverNameValue = await EvaluateAsync(args[0]);
        if (serverNameValue.Type != ValueType.String)
            throw new RuntimeException("MCPClient() serverName must be a string");
        
        var serverName = serverNameValue.AsString();
        if (string.IsNullOrWhiteSpace(serverName))
            throw new RuntimeException("MCPClient() serverName cannot be empty");
        
        var client = new BuiltIns.MCPClientInstance(serverName);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateACPClientAsync(List<Expression> args)
    {
        // ACPClient(baseUrl, apiKey?)
        if (args.Count < 1)
            throw new RuntimeException("ACPClient() expects at least 1 argument: (baseUrl, apiKey?)");
        
        var baseUrlValue = await EvaluateAsync(args[0]);
        if (baseUrlValue.Type != ValueType.String)
            throw new RuntimeException("ACPClient() baseUrl must be a string");
        
        var baseUrl = baseUrlValue.AsString();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new RuntimeException("ACPClient() baseUrl cannot be empty");
        
        string? apiKey = null;
        if (args.Count > 1)
        {
            var apiKeyValue = await EvaluateAsync(args[1]);
            if (apiKeyValue.Type != ValueType.String)
                throw new RuntimeException("ACPClient() apiKey must be a string");
            apiKey = apiKeyValue.AsString();
        }
        
        var client = new BuiltIns.ACP.ACPClientInstance(baseUrl, apiKey);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateACPServerAsync(List<Expression> args)
    {
        // ACPServer(port)
        if (args.Count != 1)
            throw new RuntimeException("ACPServer() expects 1 argument: (port)");
        
        var portValue = await EvaluateAsync(args[0]);
        if (portValue.Type != ValueType.Integer)
            throw new RuntimeException("ACPServer() port must be an integer");
        
        var port = portValue.AsInteger();
        if (port < 1 || port > 65535)
            throw new RuntimeException("ACPServer() port must be between 1 and 65535");
        
        var server = new BuiltIns.ACP.ACPServerInstance(port);
        return RuntimeValue.Object(server);
    }
    
    private async Task<RuntimeValue> CreateACPAgentToolAsync(List<Expression> args)
    {
        // ACPAgentTool(acpClient, agentId, description)
        if (args.Count != 3)
            throw new RuntimeException("ACPAgentTool() expects 3 arguments: (acpClient, agentId, description)");
        
        var clientValue = await EvaluateAsync(args[0]);
        if (clientValue.Type != ValueType.Object)
            throw new RuntimeException("ACPAgentTool() acpClient must be an ACPClient instance");
        
        var clientObj = clientValue.AsObject();
        if (clientObj is not BuiltIns.ACP.ACPClientInstance acpClient)
            throw new RuntimeException("ACPAgentTool() acpClient must be an ACPClient instance");
        
        var agentIdValue = await EvaluateAsync(args[1]);
        if (agentIdValue.Type != ValueType.String)
            throw new RuntimeException("ACPAgentTool() agentId must be a string");
        
        var descriptionValue = await EvaluateAsync(args[2]);
        if (descriptionValue.Type != ValueType.String)
            throw new RuntimeException("ACPAgentTool() description must be a string");
        
        var agentId = agentIdValue.AsString();
        var description = descriptionValue.AsString();
        
        var tool = new BuiltIns.ACP.ACPAgentToolInstance(acpClient, agentId, description);
        return RuntimeValue.Object(tool);
    }
    
    private async Task<RuntimeValue> CreateLLMClientBridgeAsync(List<Expression> args)
    {
        if (args.Count == 0)
            throw new RuntimeException("LLMClientBridge() expects at least 1 argument");
        
        var bridge = new BuiltIns.LLMClientBridge.LLMClientBridgeInstance();
        BuiltIns.LLMClientBridge.IBackendAdapter? adapter = null;
        
        // Detect backend type from arguments
        var firstArg = await EvaluateAsync(args[0]);
        
        if (firstArg.Type == ValueType.String)
        {
            var firstStr = firstArg.AsString();
            
            // Check for "openrouter"
            if (firstStr == "openrouter")
            {
                // OpenRouter: LLMClientBridge("openrouter", model?)
                string? model = null;
                if (args.Count > 1)
                {
                    var modelValue = await EvaluateAsync(args[1]);
                    if (modelValue.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() model argument must be a string");
                    model = modelValue.AsString();
                }
                
                var openRouterClient = new BuiltIns.OpenRouterClientInstance(model);
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.OpenRouterAdapter(openRouterClient);
            }
            // Check for "local"
            else if (firstStr == "local")
            {
                // Direct local: LLMClientBridge("local", modelPath, gpuLayers?)
                if (args.Count < 2)
                    throw new RuntimeException("LLMClientBridge() expects modelPath when using 'local'");
                
                var modelPathValue = await EvaluateAsync(args[1]);
                if (modelPathValue.Type != ValueType.String)
                    throw new RuntimeException("LLMClientBridge() modelPath must be a string");
                
                var llamaClient = new BuiltIns.LlamaCppClientInstance();
                llamaClient.ModelPath = modelPathValue.AsString();
                
                // Optional GPU layers
                if (args.Count > 2)
                {
                    var gpuLayersValue = await EvaluateAsync(args[2]);
                    if (gpuLayersValue.Type != ValueType.Integer)
                        throw new RuntimeException("LLMClientBridge() gpuLayers must be an integer");
                    llamaClient.GpuLayerCount = gpuLayersValue.AsInteger();
                }
                
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
            }
            // Check for HTTP URL (local server)
            else if (firstStr.StartsWith("http://") || firstStr.StartsWith("https://"))
            {
                // Local server: LLMClientBridge(serverUrl, apiKey?)
                string? apiKey = null;
                if (args.Count > 1)
                {
                    var apiKeyValue = await EvaluateAsync(args[1]);
                    if (apiKeyValue.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() apiKey must be a string");
                    apiKey = apiKeyValue.AsString();
                }
                
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.LocalServerAdapter(firstStr, apiKey);
            }
            // Remote API: LLMClientBridge(apiUrl, apiKey, model)
            else if (args.Count >= 3)
            {
                var apiUrl = firstStr;
                var apiKeyValue = await EvaluateAsync(args[1]);
                var modelValue = await EvaluateAsync(args[2]);
                
                if (apiKeyValue.Type != ValueType.String || modelValue.Type != ValueType.String)
                    throw new RuntimeException("LLMClientBridge() expects (string, string, string) for remote API");
                
                var llmClient = new BuiltIns.LLMClientInstance();
                llmClient.ApiUrl = apiUrl;
                llmClient.ApiKey = apiKeyValue.AsString();
                llmClient.Model = modelValue.AsString();
                
                adapter = new BuiltIns.LLMClientBridge.BackendAdapters.RemoteApiAdapter(llmClient);
            }
            else
            {
                throw new RuntimeException("LLMClientBridge() invalid arguments. Expected: (serverUrl), (apiUrl, apiKey, model), (\"openrouter\", model?), or (\"local\", modelPath, gpuLayers?)");
            }
        }
        else if (firstArg.Type == ValueType.Array)
        {
            // Multiple backends: LLMClientBridge([{backend1}, {backend2}, ...])
            var backendsArray = firstArg.AsArray();
            
            if (backendsArray.Count == 0)
                throw new RuntimeException("LLMClientBridge() backend array cannot be empty");
            
            // Process each backend configuration
            foreach (var backendConfig in backendsArray)
            {
                if (backendConfig.Type != ValueType.Object)
                {
                    throw new RuntimeException("LLMClientBridge() backend configurations must be objects");
                }
                
                var backendObj = backendConfig.AsObject();
                
                // Helper to get property from object
                RuntimeValue? GetProp(string name)
                {
                    try
                    {
                        return backendObj.Get(name, null);
                    }
                    catch
                    {
                        return null;
                    }
                }
                
                var typeProp = GetProp("type");
                var urlProp = GetProp("url");
                
                if (typeProp == null || typeProp.Type != ValueType.String)
                {
                    throw new RuntimeException("LLMClientBridge() backend must have 'type' property");
                }
                
                    var backendType = typeProp.AsString();
                    BuiltIns.LLMClientBridge.IBackendAdapter? backendAdapter = null;
                
                if (backendType == "server")
                {
                    if (urlProp == null || urlProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() server backend must have 'url' property");
                    
                    var apiKeyProp = GetProp("apiKey");
                    var apiKey = apiKeyProp != null && apiKeyProp.Type == ValueType.String 
                        ? apiKeyProp.AsString() 
                        : null;
                    
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.LocalServerAdapter(
                        urlProp.AsString(), apiKey);
                }
                else if (backendType == "api")
                {
                    if (urlProp == null || urlProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() api backend must have 'url' property");
                    
                    var apiKeyProp = GetProp("apiKey");
                    var modelProp = GetProp("model");
                    
                    if (apiKeyProp == null || apiKeyProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() api backend must have 'apiKey' property");
                    if (modelProp == null || modelProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() api backend must have 'model' property");
                    
                    var llmClient = new BuiltIns.LLMClientInstance();
                    llmClient.ApiUrl = urlProp.AsString();
                    llmClient.ApiKey = apiKeyProp.AsString();
                    llmClient.Model = modelProp.AsString();
                    
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.RemoteApiAdapter(llmClient);
                }
                else if (backendType == "openrouter")
                {
                    var modelProp = GetProp("model");
                    var model = modelProp != null && modelProp.Type == ValueType.String 
                        ? modelProp.AsString() 
                        : null;
                    
                    var openRouterClient = new BuiltIns.OpenRouterClientInstance(model);
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.OpenRouterAdapter(openRouterClient);
                }
                else if (backendType == "local")
                {
                    var modelPathProp = GetProp("modelPath");
                    if (modelPathProp == null || modelPathProp.Type != ValueType.String)
                        throw new RuntimeException("LLMClientBridge() local backend must have 'modelPath' property");
                    
                    var llamaClient = new BuiltIns.LlamaCppClientInstance();
                    llamaClient.ModelPath = modelPathProp.AsString();
                    
                    var gpuLayersProp = GetProp("gpuLayers");
                    if (gpuLayersProp != null && gpuLayersProp.Type == ValueType.Integer)
                    {
                        llamaClient.GpuLayerCount = gpuLayersProp.AsInteger();
                    }
                    
                    backendAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                }
                else
                {
                    throw new RuntimeException($"LLMClientBridge() unknown backend type: {backendType}");
                }
                
                if (backendAdapter is not null)
                {
                    bridge.AddBackend(backendAdapter);
                    
                    // First backend becomes primary
                    if (bridge._backends.Count == 1)
                    {
                        bridge.SetPrimaryAdapter(backendAdapter);
                    }
                    
                    // Add to failover handler
                    var priorityProp = GetProp("priority");
                    var priority = priorityProp != null && priorityProp.Type == ValueType.Integer
                        ? priorityProp.AsInteger()
                        : bridge._backends.Count - 1;
                    
                    // Initialize failover handler if not already done
                    if (bridge._failoverHandler == null)
                    {
                        bridge._failoverHandler = new BuiltIns.LLMClientBridge.Failover.FailoverHandler();
                    }
                    bridge._failoverHandler.AddBackend(bridge._backends.Count - 1, priority);
                }
            }
            
            // Initialize load balancer for multiple backends
            bridge._loadBalancer = new BuiltIns.LLMClientBridge.LoadBalancing.LoadBalancer();
            
            return RuntimeValue.Object(bridge);
        }
        else
        {
            throw new RuntimeException("LLMClientBridge() first argument must be a string or array");
        }
        
        if (adapter is not null)
        {
            bridge.SetPrimaryAdapter(adapter);
        }
        
        return RuntimeValue.Object(bridge);
    }
    
    private async Task<RuntimeValue> CreateSqlServerClientAsync(List<Expression> args)
    {
        // SqlServerClient() or SqlServerClient(connectionString?)
        if (args.Count > 1)
            throw new RuntimeException("SqlServerClient() expects 0 or 1 argument: (connectionString?)");
        
        string? connectionString = null;
        if (args.Count == 1)
        {
            var connStrValue = await EvaluateAsync(args[0]);
            if (connStrValue.Type != ValueType.String)
                throw new RuntimeException("SqlServerClient() connectionString argument must be a string");
            connectionString = connStrValue.AsString();
        }
        
        var client = new BuiltIns.SqlServerClientInstance(connectionString);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreatePostgresClientAsync(List<Expression> args)
    {
        // PostgresClient() or PostgresClient(connectionString?)
        if (args.Count > 1)
            throw new RuntimeException("PostgresClient() expects 0 or 1 argument: (connectionString?)");
        
        string? connectionString = null;
        if (args.Count == 1)
        {
            var connStrValue = await EvaluateAsync(args[0]);
            if (connStrValue.Type != ValueType.String)
                throw new RuntimeException("PostgresClient() connectionString argument must be a string");
            connectionString = connStrValue.AsString();
        }
        
        var client = new BuiltIns.PostgresClientInstance(connectionString);
        return RuntimeValue.Object(client);
    }

    private async Task<RuntimeValue> CreateSqliteClientAsync(List<Expression> args)
    {
        // SqliteClient() or SqliteClient(connectionString?)
        if (args.Count > 1)
            throw new RuntimeException("SqliteClient() expects 0 or 1 argument: (connectionString?)");

        string? connectionString = null;
        if (args.Count == 1)
        {
            var connStrValue = await EvaluateAsync(args[0]);
            if (connStrValue.Type != ValueType.String)
                throw new RuntimeException("SqliteClient() connectionString argument must be a string");
            connectionString = connStrValue.AsString();
        }

        var client = new BuiltIns.SqliteClientInstance(connectionString);
        return RuntimeValue.Object(client);
    }
    
    private async Task<RuntimeValue> CreateSerialConnectionAsync(List<Expression> args)
    {
        // SerialConnection() - no arguments, connection is established via connect() method
        if (args.Count != 0)
            throw new RuntimeException("SerialConnection() expects 0 arguments. Use connect(portName, baudRate) to establish connection.");
        
        var connection = new BuiltIns.SerialConnectionInstance();
        return RuntimeValue.Object(connection);
    }
    
    private async Task<RuntimeValue> CreateArduinoConnectionAsync(List<Expression> args)
    {
        // ArduinoConnection(url) for HTTP mode
        // ArduinoConnection(port, baudRate) for serial mode
        if (args.Count < 1 || args.Count > 2)
            throw new RuntimeException("ArduinoConnection() expects 1 or 2 arguments: (url) for HTTP mode or (port, baudRate) for serial mode");
        
        if (args.Count == 1)
        {
            // HTTP mode: ArduinoConnection(url)
            var urlValue = await EvaluateAsync(args[0]);
            if (urlValue.Type != ValueType.String)
                throw new RuntimeException("ArduinoConnection() url argument must be a string");
            
            var connection = new BuiltIns.ArduinoConnectionInstance(urlValue.AsString());
            return RuntimeValue.Object(connection);
        }
        else
        {
            // Serial mode: ArduinoConnection(port, baudRate)
            var portValue = await EvaluateAsync(args[0]);
            var baudRateValue = await EvaluateAsync(args[1]);
            
            if (portValue.Type != ValueType.String)
                throw new RuntimeException("ArduinoConnection() port argument must be a string");
            if (baudRateValue.Type != ValueType.Integer)
                throw new RuntimeException("ArduinoConnection() baudRate argument must be an integer");
            
            var connection = new BuiltIns.ArduinoConnectionInstance(portValue.AsString(), baudRateValue.AsInteger());
            return RuntimeValue.Object(connection);
        }
    }
    
    private async Task<RuntimeValue> CreateLLMServerAsync(List<Expression> args)
    {
        if (args.Count < 2)
            throw new RuntimeException("LLMServer() expects at least 2 arguments");
        
        var server = new BuiltIns.LLMServerInstance();
        BuiltIns.LLMClientBridge.LLMClientBridgeInstance? bridge = null;
        BuiltIns.LLMClientBridge.IBackendAdapter? adapter = null;
        int port;
        string? host = null;
        
        // Evaluate port (should be second argument in all patterns)
        var portValue = await EvaluateAsync(args[1]);
        if (portValue.Type != ValueType.Integer)
            throw new RuntimeException("LLMServer() port must be an integer");
        port = portValue.AsInteger();
        
        // Pattern 1: (string modelPath, int port, object? config?)
        if (args[0] != null)
        {
            var firstArg = await EvaluateAsync(args[0]);
            
            if (firstArg.Type == ValueType.String)
            {
                // Model path string - create local bridge
                var modelPath = firstArg.AsString();
                
                // Optional config
                if (args.Count > 2)
                {
                    var configValue = await EvaluateAsync(args[2]);
                    if (configValue.Type == ValueType.Object)
                    {
                        var configObj = configValue.AsObject();
                        var llamaClient = new BuiltIns.LlamaCppClientInstance();
                        llamaClient.ModelPath = modelPath;
                        
                        // Apply config if provided
                        var temperature = GetPropertyFromObject(configObj, "temperature");
                        if (temperature != null && temperature.Type == ValueType.Float)
                        {
                            llamaClient.Temperature = temperature.AsFloat();
                        }
                        
                        var maxTokens = GetPropertyFromObject(configObj, "maxTokens");
                        if (maxTokens != null && maxTokens.Type == ValueType.Integer)
                        {
                            llamaClient.MaxTokens = maxTokens.AsInteger();
                        }
                        
                        var gpuLayers = GetPropertyFromObject(configObj, "gpuLayers");
                        if (gpuLayers != null && gpuLayers.Type == ValueType.Integer)
                        {
                            llamaClient.GpuLayerCount = gpuLayers.AsInteger();
                        }
                        
                        adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                    }
                    else
                    {
                        // No config, use defaults
                        var llamaClient = new BuiltIns.LlamaCppClientInstance();
                        llamaClient.ModelPath = modelPath;
                        adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                    }
                }
                else
                {
                    // No config, use defaults
                    var llamaClient = new BuiltIns.LlamaCppClientInstance();
                    llamaClient.ModelPath = modelPath;
                    adapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                }
                
                if (adapter != null)
                {
                    bridge = new BuiltIns.LLMClientBridge.LLMClientBridgeInstance();
                    bridge.SetPrimaryAdapter(adapter);
                    server.SetBridgeCreatedInternally(true);
                }
            }
            // Pattern 2: (LLMClientBridge bridge, int port, string? host?)
            else if (firstArg.Type == ValueType.Object && 
                     firstArg.AsObject() is BuiltIns.LLMClientBridge.LLMClientBridgeInstance bridgeInstance)
            {
                bridge = bridgeInstance;
                
                // Optional host
                if (args.Count > 2)
                {
                    var hostValue = await EvaluateAsync(args[2]);
                    if (hostValue.Type != ValueType.String)
                        throw new RuntimeException("LLMServer() host must be a string");
                    host = hostValue.AsString();
                }
            }
            // Pattern 3: (object config, int port, string? host?)
            else if (firstArg.Type == ValueType.Object)
            {
                // Parse config object and create bridge
                var configObj = firstArg.AsObject();
                
                // Optional host
                if (args.Count > 2)
                {
                    var hostValue = await EvaluateAsync(args[2]);
                    if (hostValue.Type == ValueType.String)
                    {
                        host = hostValue.AsString();
                    }
                }
                
                // Create bridge from config
                bridge = new BuiltIns.LLMClientBridge.LLMClientBridgeInstance();
                
                // Check if it's a single backend config
                var typeProp = GetPropertyFromObject(configObj, "type");
                
                if (typeProp != null && typeProp.Type == ValueType.String)
                {
                    // Single backend config
                    var backendType = typeProp.AsString();
                    BuiltIns.LLMClientBridge.IBackendAdapter? configAdapter = null;
                    
                    if (backendType == "local")
                    {
                        var modelPathProp = GetPropertyFromObject(configObj, "modelPath");
                        if (modelPathProp == null || modelPathProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() local backend must have 'modelPath' property");
                        
                        var llamaClient = new BuiltIns.LlamaCppClientInstance();
                        llamaClient.ModelPath = modelPathProp.AsString();
                        
                        var gpuLayersProp = GetPropertyFromObject(configObj, "gpuLayers");
                        if (gpuLayersProp != null && gpuLayersProp.Type == ValueType.Integer)
                        {
                            llamaClient.GpuLayerCount = gpuLayersProp.AsInteger();
                        }
                        
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.DirectLocalAdapter(llamaClient);
                    }
                    else if (backendType == "server")
                    {
                        var urlProp = GetPropertyFromObject(configObj, "url");
                        if (urlProp == null || urlProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() server backend must have 'url' property");
                        
                        var apiKeyProp = GetPropertyFromObject(configObj, "apiKey");
                        var apiKey = apiKeyProp != null && apiKeyProp.Type == ValueType.String 
                            ? apiKeyProp.AsString() 
                            : null;
                        
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.LocalServerAdapter(
                            urlProp.AsString(), apiKey);
                    }
                    else if (backendType == "api")
                    {
                        var urlProp = GetPropertyFromObject(configObj, "url");
                        var apiKeyProp = GetPropertyFromObject(configObj, "apiKey");
                        var modelProp = GetPropertyFromObject(configObj, "model");
                        
                        if (urlProp == null || urlProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() api backend must have 'url' property");
                        if (apiKeyProp == null || apiKeyProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() api backend must have 'apiKey' property");
                        if (modelProp == null || modelProp.Type != ValueType.String)
                            throw new RuntimeException("LLMServer() api backend must have 'model' property");
                        
                        var llmClient = new BuiltIns.LLMClientInstance();
                        llmClient.ApiUrl = urlProp.AsString();
                        llmClient.ApiKey = apiKeyProp.AsString();
                        llmClient.Model = modelProp.AsString();
                        
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.RemoteApiAdapter(llmClient);
                    }
                    else if (backendType == "openrouter")
                    {
                        var modelProp = GetPropertyFromObject(configObj, "model");
                        var model = modelProp != null && modelProp.Type == ValueType.String 
                            ? modelProp.AsString() 
                            : null;
                        
                        var openRouterClient = new BuiltIns.OpenRouterClientInstance(model);
                        configAdapter = new BuiltIns.LLMClientBridge.BackendAdapters.OpenRouterAdapter(openRouterClient);
                    }
                    else
                    {
                        throw new RuntimeException($"LLMServer() unknown backend type: {backendType}");
                    }
                    
                    if (configAdapter != null)
                    {
                        bridge.SetPrimaryAdapter(configAdapter);
                    }
                    
                    server.SetBridgeCreatedInternally(true);
                }
                else
                {
                    throw new RuntimeException("LLMServer() config object must have 'type' property");
                }
            }
            else
            {
                throw new RuntimeException("LLMServer() first argument must be a string (modelPath), LLMClientBridge instance, or config object");
            }
        }
        
        if (bridge == null)
        {
            throw new RuntimeException("LLMServer() failed to create or initialize bridge");
        }
        
        // Initialize server
        server.Initialize(bridge, port, host, this);
        
        return RuntimeValue.Object(server);
    }
    
    private RuntimeValue? GetPropertyFromObject(ObjectInstance obj, string name)
    {
        try
        {
            return obj.Get(name, null);
        }
        catch
        {
            return null;
        }
    }
    
    private async Task<RuntimeValue> CreateHTMLCacheAsync(List<Expression> args)
    {
        if (args.Count > 3)
            throw new RuntimeException("HTMLCache() expects 0-3 arguments: (cacheDirectory?, maxSize?, expirationHours?)");
        
        string? cacheDir = null;
        int? maxSize = null;
        int? expirationHours = null;
        
        if (args.Count >= 1)
        {
            var dir = await EvaluateAsync(args[0]);
            if (dir.Type != ValueType.String)
                throw new RuntimeException("HTMLCache() cacheDirectory must be a string");
            cacheDir = dir.AsString();
        }
        
        if (args.Count >= 2)
        {
            var size = await EvaluateAsync(args[1]);
            if (size.Type != ValueType.Integer)
                throw new RuntimeException("HTMLCache() maxSize must be an integer");
            maxSize = size.AsInteger();
        }
        
        if (args.Count >= 3)
        {
            var hours = await EvaluateAsync(args[2]);
            if (hours.Type != ValueType.Integer)
                throw new RuntimeException("HTMLCache() expirationHours must be an integer");
            expirationHours = hours.AsInteger();
        }
        
        var cache = new BuiltIns.HTMLCacheInstance(cacheDir, maxSize, expirationHours);
        return RuntimeValue.Object(cache);
    }
}
