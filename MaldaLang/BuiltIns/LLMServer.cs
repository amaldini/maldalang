// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Generic;
using System.Linq;
using MaldaLang.BuiltIns.LLMClientBridge;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// LLM Server that automatically provides REST API endpoints for LLM backends.
/// Wraps RestServer and LLMClientBridge to simplify creating LLM API servers.
/// </summary>
public class LLMServerInstance : ObjectInstance
{
    private static readonly List<LLMServerInstance> _instances = new();
    private static readonly object _instancesLock = new object();
    
    private RestServerInstance? _restServer;
    private LLMClientBridgeInstance? _bridge;
    private int _port;
    private string _host;
    private bool _isRunning = false;
    private Interpreter? _interpreter;
    private bool _bridgeCreatedInternally = false;
    
    public LLMServerInstance() : base(null)
    {
        _port = 8080;
        _host = "localhost";
        
        // Register this instance
        lock (_instancesLock)
        {
            _instances.Add(this);
        }
    }
    
    /// <summary>
    /// Constructor for transpiled code that directly initializes the server.
    /// </summary>
    public LLMServerInstance(LLMClientBridgeInstance? bridge, int port, string? host = null, Interpreter? interpreter = null) : base(null)
    {
        _port = 8080;
        _host = "localhost";
        
        // Register this instance
        lock (_instancesLock)
        {
            _instances.Add(this);
        }
        
        // Initialize with provided parameters
        Initialize(bridge, port, host, interpreter);
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "port")
            return RuntimeValue.Integer(_port);
        if (name == "host")
            return RuntimeValue.String(_host);
        if (name == "isRunning")
            return RuntimeValue.Boolean(_isRunning);
        if (name == "bridge")
            return _bridge != null ? RuntimeValue.Object(_bridge) : RuntimeValue.Null();
        
        // Handle method access
        if (name == "start" || name == "stop" || name == "setTemperature" || 
            name == "setMaxTokens" || name == "enableCORS" || name == "setCORSOrigin")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on LLMServer.");
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args, Interpreter? interpreter = null)
    {
        switch (methodName)
        {
            case "start":
                if (args.Count != 0)
                    throw new Exception("start() expects 0 arguments");
                Start(interpreter);
                return RuntimeValue.Null();
            
            case "stop":
                if (args.Count != 0)
                    throw new Exception("stop() expects 0 arguments");
                Stop();
                return RuntimeValue.Null();
            
            case "setTemperature":
                if (args.Count != 1 || args[0].Type != ValueType.Float)
                    throw new Exception("setTemperature() expects 1 float argument");
                if (_bridge != null)
                {
                    _bridge.CallMethod("setTemperature", args, interpreter);
                }
                return RuntimeValue.Null();
            
            case "setMaxTokens":
                if (args.Count != 1 || args[0].Type != ValueType.Integer)
                    throw new Exception("setMaxTokens() expects 1 integer argument");
                if (_bridge != null)
                {
                    _bridge.CallMethod("setMaxTokens", args, interpreter);
                }
                return RuntimeValue.Null();
            
            case "enableCORS":
                if (args.Count != 1 || args[0].Type != ValueType.Boolean)
                    throw new Exception("enableCORS() expects 1 boolean argument");
                if (_restServer != null)
                {
                    _restServer.CallMethod("enableCORS", args);
                }
                return RuntimeValue.Null();
            
            case "setCORSOrigin":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setCORSOrigin() expects 1 string argument");
                if (_restServer != null)
                {
                    _restServer.CallMethod("setCORSOrigin", args);
                }
                return RuntimeValue.Null();
            
            // Endpoint methods (called by RestServer)
            case "_endpointHealth":
                return HealthCheck();
            
            case "_endpointChat":
                return ChatCompletions(args.Count > 0 ? args[0] : null);
            
            case "_endpointComplete":
                return SimpleComplete(args.Count > 0 ? args[0] : null);
            
            case "_endpointModelInfo":
                return GetModelInfo();
            
            case "_endpointUpdateSettings":
                return UpdateSettings(args.Count > 0 ? args[0] : null);
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    public void Initialize(LLMClientBridgeInstance? bridge, int port, string? host, Interpreter? interpreter = null)
    {
        _bridge = bridge;
        _port = port;
        _host = host ?? "localhost";
        _interpreter = interpreter;
        
        // Create RestServer instance (now supports null interpreter for transpiled code)
        _restServer = new RestServerInstance(_port, _host, interpreter);
    }
    
    public void SetBridgeCreatedInternally(bool created)
    {
        _bridgeCreatedInternally = created;
    }
    
    private void Start(Interpreter? interpreter)
    {
        if (_isRunning)
            throw new Exception("LLMServer is already running");
        
        if (_bridge == null)
            throw new Exception("LLM bridge not initialized");
        
        if (_restServer == null)
            throw new Exception("RestServer not initialized");
        
        if (interpreter == null)
            interpreter = _interpreter;
        
        if (interpreter == null)
        {
            // Transpiled mode: register endpoints directly with RestServer
            RegisterTranspiledEndpoints();
        }
        else
        {
            // Interpreted mode: register endpoints in interpreter
            RegisterEndpoints(interpreter);
        }
        
        // Start the RestServer (it will discover the registered endpoints)
        _restServer.CallMethod("start", new List<RuntimeValue>());
        _isRunning = true;
    }
    
    private void Stop()
    {
        if (!_isRunning)
            return;
        
        _isRunning = false;
        if (_restServer != null)
        {
            _restServer.CallMethod("stop", new List<RuntimeValue>());
        }
    }
    
    private void RegisterEndpoints(Interpreter interpreter)
    {
        // Create endpoint functions with decorators programmatically
        // We'll create FunctionDeclaration AST nodes with decorators
        
        // GET /health
        RegisterEndpoint(interpreter, "GET", "/health", "llmServerHealthCheck", 
            new List<string>());
        
        // POST /v1/chat/completions
        RegisterEndpoint(interpreter, "POST", "/v1/chat/completions", "llmServerChatCompletions",
            new List<string> { "body" });
        
        // POST /api/complete
        RegisterEndpoint(interpreter, "POST", "/api/complete", "llmServerComplete",
            new List<string> { "body" });
        
        // GET /api/model
        RegisterEndpoint(interpreter, "GET", "/api/model", "llmServerModelInfo",
            new List<string>());
        
        // POST /api/model/settings
        RegisterEndpoint(interpreter, "POST", "/api/model/settings", "llmServerUpdateSettings",
            new List<string> { "body" });
    }
    
    private void RegisterEndpoint(Interpreter interpreter, string method, string path, string functionName,
        List<string> parameters)
    {
        // Create decorator with path as string literal
        var pathLiteral = new LiteralExpression(path, 0, 0);
        var decorator = new Decorator(method, new List<Expression> { pathLiteral }, 0, 0);
        
        // Create function declaration
        var funcDecl = new FunctionDeclaration(
            functionName,
            parameters,
            new BlockStatement(new List<Statement>(), 0, 0),
            new List<Decorator> { decorator },
            null,
            null,
            null,
            false,
            0,
            0
        );
        
        // Create function value with BuiltInInstance pointing to this server
        // The method name will be determined by the function name
        var wrapper = new FunctionValue(null, interpreter._globals, false, null);
        wrapper.BuiltInInstance = this;
        wrapper.BuiltInMethod = GetEndpointMethodName(functionName);
        wrapper.Decorators = new List<Decorator> { decorator }; // Add decorators so RestServer can discover it
        
        // Register in interpreter environment
        interpreter._globals.Define(functionName, RuntimeValue.Function(wrapper));
    }
    
    private string GetEndpointMethodName(string functionName)
    {
        // Map function names to internal method names
        return functionName switch
        {
            "llmServerHealthCheck" => "_endpointHealth",
            "llmServerChatCompletions" => "_endpointChat",
            "llmServerComplete" => "_endpointComplete",
            "llmServerModelInfo" => "_endpointModelInfo",
            "llmServerUpdateSettings" => "_endpointUpdateSettings",
            _ => throw new Exception($"Unknown endpoint function: {functionName}")
        };
    }
    
    // Endpoint implementations
    private RuntimeValue HealthCheck()
    {
        if (_bridge == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.String("error"));
            errorObj.Set("message", RuntimeValue.String("LLM bridge not initialized"));
            return RuntimeValue.Object(errorObj);
        }
        
        var resultObj = new JsonObject();
        resultObj.Set("status", RuntimeValue.String("healthy"));
        resultObj.Set("backendType", RuntimeValue.String(_bridge.Get("backendType", null).AsString()));
        resultObj.Set("isConnected", RuntimeValue.Boolean(_bridge.Get("isConnected", null).AsBoolean()));
        return RuntimeValue.Object(resultObj);
    }
    
    private RuntimeValue ChatCompletions(RuntimeValue? body)
    {
        if (_bridge == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(500));
            errorObj.Set("error", RuntimeValue.Object(CreateErrorObject("LLM bridge not initialized")));
            return RuntimeValue.Object(errorObj);
        }
        
        if (body == null || body.Type != ValueType.Object)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(400));
            errorObj.Set("error", RuntimeValue.Object(CreateErrorObject("Missing request body")));
            return RuntimeValue.Object(errorObj);
        }
        
        var bodyObj = body.AsObject();
        var messages = GetProperty(bodyObj, "messages");
        var temperature = GetProperty(bodyObj, "temperature");
        var maxTokens = GetProperty(bodyObj, "max_tokens");
        
        if (messages == null || messages.Type != ValueType.Array)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(400));
            errorObj.Set("error", RuntimeValue.Object(CreateErrorObject("Missing 'messages' field")));
            return RuntimeValue.Object(errorObj);
        }
        
        // Set temperature if provided
        if (temperature != null)
        {
            _bridge.CallMethod("setTemperature", new List<RuntimeValue> { temperature }, _interpreter);
        }
        
        // Set max tokens if provided
        if (maxTokens != null)
        {
            _bridge.CallMethod("setMaxTokens", new List<RuntimeValue> { maxTokens }, _interpreter);
        }
        
        // Call bridge chat
        var response = _bridge.CallMethod("chat", new List<RuntimeValue> { messages }, _interpreter);
        
        if (response.Type != ValueType.Object)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(500));
            errorObj.Set("error", RuntimeValue.Object(CreateErrorObject("Error generating response")));
            return RuntimeValue.Object(errorObj);
        }
        
        var responseObj = response.AsObject();
        var content = GetProperty(responseObj, "content");
        var contentStr = content != null && content.Type == ValueType.String ? content.AsString() : "";
        
        // Return OpenAI-compatible format
        var resultObj = new JsonObject();
        resultObj.Set("id", RuntimeValue.String("chatcmpl-llmserver-" + Guid.NewGuid().ToString().Substring(0, 8)));
        resultObj.Set("object", RuntimeValue.String("chat.completion"));
        resultObj.Set("created", RuntimeValue.Integer((int)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        resultObj.Set("model", RuntimeValue.String(_bridge.Get("backendType", null).AsString()));
        
        var choiceObj = new JsonObject();
        choiceObj.Set("index", RuntimeValue.Integer(0));
        var messageObj = new JsonObject();
        messageObj.Set("role", RuntimeValue.String("assistant"));
        messageObj.Set("content", RuntimeValue.String(contentStr));
        choiceObj.Set("message", RuntimeValue.Object(messageObj));
        choiceObj.Set("finish_reason", RuntimeValue.String("stop"));
        
        var choicesArray = new List<RuntimeValue> { RuntimeValue.Object(choiceObj) };
        resultObj.Set("choices", RuntimeValue.Array(choicesArray));
        
        var usageObj = new JsonObject();
        usageObj.Set("prompt_tokens", RuntimeValue.Integer(0));
        usageObj.Set("completion_tokens", RuntimeValue.Integer(0));
        usageObj.Set("total_tokens", RuntimeValue.Integer(0));
        resultObj.Set("usage", RuntimeValue.Object(usageObj));
        
        return RuntimeValue.Object(resultObj);
    }
    
    private RuntimeValue SimpleComplete(RuntimeValue? body)
    {
        if (_bridge == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(500));
            errorObj.Set("error", RuntimeValue.String("LLM bridge not initialized"));
            return RuntimeValue.Object(errorObj);
        }
        
        if (body == null || body.Type != ValueType.Object)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(400));
            errorObj.Set("error", RuntimeValue.String("Missing request body"));
            return RuntimeValue.Object(errorObj);
        }
        
        var bodyObj = body.AsObject();
        var prompt = GetProperty(bodyObj, "prompt");
        
        if (prompt == null || prompt.Type != ValueType.String)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(400));
            errorObj.Set("error", RuntimeValue.String("Missing 'prompt' field"));
            return RuntimeValue.Object(errorObj);
        }
        
        var temperature = GetProperty(bodyObj, "temperature");
        if (temperature != null)
        {
            _bridge.CallMethod("setTemperature", new List<RuntimeValue> { temperature }, _interpreter);
        }
        
        var response = _bridge.CallMethod("complete", new List<RuntimeValue> { prompt }, _interpreter);
        
        if (response.Type == ValueType.Object)
        {
            var responseObj = response.AsObject();
            var content = GetProperty(responseObj, "content");
            var contentStr = content != null && content.Type == ValueType.String ? content.AsString() : "";
            
            var resultObj = new JsonObject();
            resultObj.Set("status", RuntimeValue.Integer(200));
            resultObj.Set("response", RuntimeValue.String(contentStr));
            return RuntimeValue.Object(resultObj);
        }
        
        var errorResult = new JsonObject();
        errorResult.Set("status", RuntimeValue.Integer(500));
        errorResult.Set("error", RuntimeValue.String("Error generating response"));
        return RuntimeValue.Object(errorResult);
    }
    
    private RuntimeValue GetModelInfo()
    {
        if (_bridge == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(500));
            errorObj.Set("error", RuntimeValue.String("LLM bridge not initialized"));
            return RuntimeValue.Object(errorObj);
        }
        
        var resultObj = new JsonObject();
        resultObj.Set("backendType", RuntimeValue.String(_bridge.Get("backendType", null).AsString()));
        resultObj.Set("temperature", _bridge.Get("temperature", null));
        resultObj.Set("maxTokens", _bridge.Get("maxTokens", null));
        return RuntimeValue.Object(resultObj);
    }
    
    private RuntimeValue UpdateSettings(RuntimeValue? body)
    {
        if (_bridge == null)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(500));
            errorObj.Set("error", RuntimeValue.String("LLM bridge not initialized"));
            return RuntimeValue.Object(errorObj);
        }
        
        if (body == null || body.Type != ValueType.Object)
        {
            var errorObj = new JsonObject();
            errorObj.Set("status", RuntimeValue.Integer(400));
            errorObj.Set("error", RuntimeValue.String("Missing request body"));
            return RuntimeValue.Object(errorObj);
        }
        
        var bodyObj = body.AsObject();
        var temperature = GetProperty(bodyObj, "temperature");
        var maxTokens = GetProperty(bodyObj, "max_tokens");
        
        if (temperature != null)
        {
            _bridge.CallMethod("setTemperature", new List<RuntimeValue> { temperature }, _interpreter);
        }
        
        if (maxTokens != null)
        {
            _bridge.CallMethod("setMaxTokens", new List<RuntimeValue> { maxTokens }, _interpreter);
        }
        
        var resultObj = new JsonObject();
        resultObj.Set("status", RuntimeValue.Integer(200));
        resultObj.Set("message", RuntimeValue.String("Settings updated"));
        resultObj.Set("temperature", _bridge.Get("temperature", null));
        resultObj.Set("maxTokens", _bridge.Get("maxTokens", null));
        return RuntimeValue.Object(resultObj);
    }
    
    private JsonObject CreateErrorObject(string message)
    {
        var errorObj = new JsonObject();
        errorObj.Set("message", RuntimeValue.String(message));
        return errorObj;
    }
    
    private RuntimeValue? GetProperty(ObjectInstance obj, string name)
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
    
    /// <summary>
    /// Register transpiled endpoints directly with RestServer.
    /// Called when LLMServer is started in transpiled mode (no interpreter).
    /// </summary>
    private void RegisterTranspiledEndpoints()
    {
        if (_restServer == null)
            return;
        
        // Register endpoints directly with RestServer's route registry
        // GET /health
        RestServerInstance.RegisterTranspiledRoute("GET", "/health", "llmServerHealthCheck", 
            new List<string>(), null);
        
        // POST /v1/chat/completions
        RestServerInstance.RegisterTranspiledRoute("POST", "/v1/chat/completions", "llmServerChatCompletions",
            new List<string> { "body" }, null);
        
        // POST /api/complete
        RestServerInstance.RegisterTranspiledRoute("POST", "/api/complete", "llmServerComplete",
            new List<string> { "body" }, null);
        
        // GET /api/model
        RestServerInstance.RegisterTranspiledRoute("GET", "/api/model", "llmServerModelInfo",
            new List<string>(), null);
        
        // POST /api/model/settings
        RestServerInstance.RegisterTranspiledRoute("POST", "/api/model/settings", "llmServerUpdateSettings",
            new List<string> { "body" }, null);
    }
    
    /// <summary>
    /// Get LLMServerInstance by function name (for transpiled route calls).
    /// Returns the first running instance, or null if none found.
    /// </summary>
    public static LLMServerInstance? GetInstanceForRoute(string functionName)
    {
        lock (_instancesLock)
        {
            // Return the first running instance
            return _instances.FirstOrDefault(i => i._isRunning);
        }
    }
}