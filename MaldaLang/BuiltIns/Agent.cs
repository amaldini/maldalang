// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.IO;
using System.Linq;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using ValueType = MaldaLang.Interpreter.ValueType;

public class AgentInstance : ObjectInstance
{
    private static readonly Dictionary<string, GraphMemoryInstance> SharedMemoriesByPath = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object SharedMemoryLock = new();
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public string Instructions { get; set; } = "";
    
    /// <summary>
    /// Optional trace session identifier associated with this agent instance.
    /// When set and tracing is enabled, all conversation activity will be
    /// recorded under this session.
    /// </summary>
    public string? SessionId { get; private set; }
    
    /// <summary>
    /// Indicates whether tracing has been enabled for this agent.
    /// </summary>
    public bool TraceEnabled => SessionId != null;
    
    private ConversationInstance? _conversation;
    private List<ToolInstance> _tools = new();
    protected GraphMemoryInstance? _memory;
    protected Interpreter? _interpreter;
    private bool _autoRememberOnThink = true;
    private bool _memoryProgressToolsAdded;
    private string? _memoryScope;
    private string? _memoryScopeParent;
    private List<string>? _memoryScopeHierarchy;
    private bool _memoryQueryRerankEnabled;
    private string? _memoryQueryRerankMode;
    private string? _memoryQueryRerankModelPath;
    private int? _memoryQueryRerankTopK;
    private string? _memoryPath;
    private List<string> _lastInjectedNodeIds = new();
    
    public AgentInstance() : base(null)
    {
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle property access
        if (name == "name")
            return RuntimeValue.String(Name);
        if (name == "role")
            return RuntimeValue.String(Role);
        if (name == "instructions")
            return RuntimeValue.String(Instructions);
        
        // Handle method access - create a FunctionValue wrapper
        if (name == "think" || name == "addTool" || name == "getConversation" || name == "reset" ||
            name == "addToolByName" || name == "addAllTools" || name == "getAvailableTools" || name == "addSubAgent" ||
            name == "enableMemory" || name == "useMemory" || name == "getMemory" || name == "saveMemory" || name == "remember" ||
            name == "setAutoRememberOnThink" || name == "setMemoryScope" || name == "setMemoryScopeParent" || name == "setMemoryScopeHierarchy" || name == "setMemoryRerank" || name == "addMemoryProgressTools" ||
            name == "setContextTrimHandoff" || name == "getEstimatedContextTokens")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        // Handle memory property access
        if (name == "memory")
        {
            return _memory != null ? RuntimeValue.Object(_memory) : RuntimeValue.Null();
        }
        
        throw new Exception($"Undefined property '{name}' on Agent.");
    }
    
    public void Initialize(string name, string role, string instructions, LLMClientInstance? client, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, IInputProvider? inputProvider = null)
    {
        Name = name;
        Role = role;
        Instructions = instructions;
        
        var systemPrompt = $"You are {name}, a {role}.\n\n{instructions}";
        _conversation = new ConversationInstance();
        _conversation.Initialize(client, llamaClient, bridgeClient, systemPrompt, inputProvider);
        
        // Set agent name on conversation for dashboard reporting & tracing
        _conversation.AgentName = name;
        if (SessionId != null)
        {
            _conversation.SessionId = SessionId;
        }
        
        // Report agent creation to dashboard (non-blocking)
        try
        {
            AgentDashboardService.Instance.ReportAgentCreated(name, role);
        }
        catch
        {
            // Silently ignore - dashboard reporting should not affect agent initialization
        }
    }
    
    /// <summary>
    /// Enables tracing for this agent and its underlying conversation.
    /// This can be called by host code or custom MALDA tooling to record
    /// LLM calls, tool calls, and other activity for later replay/analysis.
    /// </summary>
    /// <param name="traceName">Optional human-friendly name for the session (defaults to agent name).</param>
    /// <param name="baseDirectory">
    /// Optional base directory for trace files. If null or empty, a default
    /// <c>traces</c> directory under the current working directory is used.
    /// </param>
    public void EnableTracing(string? traceName = null, string? baseDirectory = null)
    {
        if (TraceEnabled)
        {
            // Tracing already enabled for this agent.
            return;
        }
        
        // Start or reuse the current agent session.
        var name = traceName ?? Name;
        var ctx = AgentSession.Current ?? AgentSession.Start(name, null);
        SessionId = ctx.SessionId;
        
        // Propagate session id to the conversation if it already exists.
        if (_conversation != null)
        {
            _conversation.SessionId = SessionId;
        }
        
        // Configure a file-based trace writer if tracing is not already enabled.
        if (!TraceManager.Current.IsEnabled)
        {
            var dir = string.IsNullOrWhiteSpace(baseDirectory)
                ? TracingConfig.BaseDirectory
                : baseDirectory!;
            
            try
            {
                var writer = new FileTraceWriter(dir, SessionId);
                TraceManager.EnableTracing(writer);
            }
            catch
            {
                // If tracing cannot be initialized, fail silently so agent
                // behavior is not affected.
            }
        }
    }
    
    // Overload for backward compatibility
    public void Initialize(string name, string role, string instructions, LLMClientInstance? client, IInputProvider? inputProvider = null)
    {
        Initialize(name, role, instructions, client, null, null, inputProvider);
    }
    
    public RuntimeValue AddTool(ToolInstance tool)
    {
        _tools.Add(tool);
        if (_conversation != null)
        {
            _conversation.AddTool(tool);
        }
        return RuntimeValue.Null();
    }

    protected void AppendToSystemPrompt(string text)
    {
        _conversation?.AppendToSystemPrompt(text);
    }
    
    public RuntimeValue Think(RuntimeValue promptOrInstance)
    {
        if (_conversation == null)
            return RuntimeValue.Null();
        
        string prompt;
        string? systemPrompt = null;
        
        // Check if it's a PromptInstance
        RuntimeValue? responseFormat = null;
        LlmRequestOverrides? requestOverrides = null;
        if (promptOrInstance.Type == ValueType.Object && promptOrInstance.AsObject() is PromptInstance promptInst)
        {
            prompt = promptInst.User;
            systemPrompt = promptInst.System;
            responseFormat = promptInst.ResponseFormatSchema;
            requestOverrides = BuildLlmRequestOverrides(promptInst);

            // Set system prompt if provided
            if (systemPrompt != null)
            {
                _conversation.SetSystemPrompt(systemPrompt);
            }

            if (promptInst.Examples != null && promptInst.Examples.Count > 0)
            {
                foreach (var example in promptInst.Examples)
                {
                    _conversation.AddUserMessage(example.Input);
                    _conversation.AddAssistantMessage(example.Output);
                }
            }
        }
        else if (promptOrInstance.Type == ValueType.String)
        {
            prompt = promptOrInstance.AsString();
        }
        else
        {
            throw new Exception("think() expects a string or PromptInstance argument.");
        }
        
        // Report think() call to dashboard (non-blocking)
        try
        {
            AgentDashboardService.Instance.ReportAgentThink(Name, prompt);
        }
        catch
        {
            // Silently ignore - dashboard reporting should not affect agent execution
        }
        
        // If memory is enabled, query for relevant context and inject into prompt
        if (_memory != null)
        {
            try
            {
                var queryOptions = new JsonObject();
                queryOptions.Set("recentCount", RuntimeValue.Integer(0));
                queryOptions.Set("hybrid", RuntimeValue.Boolean(false));
                queryOptions.Set("minScore", RuntimeValue.Float(0.5));
                queryOptions.Set("excludeType", RuntimeValue.String("episodic"));
                queryOptions.Set("synapse", RuntimeValue.Boolean(true));
                queryOptions.Set("activation", RuntimeValue.Boolean(true));
                queryOptions.Set("diversity", RuntimeValue.Float(0.3));
                queryOptions.Set("hybridLexical", RuntimeValue.Boolean(true));
                queryOptions.Set("lexicalWeight", RuntimeValue.Float(0.25));
                if (_lastInjectedNodeIds.Count > 0)
                {
                    var excluded = new List<RuntimeValue>();
                    foreach (var id in _lastInjectedNodeIds)
                        excluded.Add(RuntimeValue.String(id));
                    queryOptions.Set("excludeNodeIds", RuntimeValue.Array(excluded));
                }
                var memoryScope = ResolveMemoryScope();
                if (!string.IsNullOrWhiteSpace(memoryScope))
                {
                    queryOptions.Set("scope", RuntimeValue.String(memoryScope));
                    var hierarchy = BuildMemoryScopeHierarchy(memoryScope);
                    if (hierarchy.Count > 0)
                    {
                        var hierarchyValues = hierarchy.Select(RuntimeValue.String).ToList();
                        queryOptions.Set("scopeHierarchy", RuntimeValue.Array(hierarchyValues));
                    }
                }
                ApplyMemoryQueryRerank(queryOptions);
                var semanticContext = _memory.CallMethod("query", new List<RuntimeValue>
                {
                    RuntimeValue.String(prompt),
                    RuntimeValue.Integer(5),
                    RuntimeValue.Object(queryOptions)
                }, _interpreter!);
                var recentArgs = new List<RuntimeValue>
                {
                    RuntimeValue.Integer(3)
                };
                if (!string.IsNullOrWhiteSpace(memoryScope))
                    recentArgs.AddRange(new[]
                    {
                        RuntimeValue.String(""),
                        RuntimeValue.String(""),
                        RuntimeValue.String(memoryScope!)
                    });
                var recentContext = _memory.CallMethod("getRecent", recentArgs, _interpreter!);
                
                var mergedMemories = MergeWorkingMemory(semanticContext, recentContext);
                if (mergedMemories.Count > 0)
                {
                    // Build context string from memories
                    var contextBuilder = new System.Text.StringBuilder();
                    contextBuilder.AppendLine("\n[Relevant memories from past interactions:]");
                    foreach (var mem in mergedMemories)
                    {
                        contextBuilder.AppendLine("- " + GraphMemoryInstance.FormatMemoryLine(mem));
                    }
                    prompt = prompt + contextBuilder.ToString();
                    
                    _lastInjectedNodeIds = new List<string>();
                    foreach (var mem in mergedMemories)
                    {
                        if (mem.Type != ValueType.Object || mem.AsObject() is not JsonObject memObj)
                            continue;
                        var nodeId = memObj.Get("nodeId", null);
                        if (nodeId != null && nodeId.Type == ValueType.String && !string.IsNullOrWhiteSpace(nodeId.AsString()))
                            _lastInjectedNodeIds.Add(nodeId.AsString());
                    }
                }
                else
                {
                    _lastInjectedNodeIds.Clear();
                }
            }
            catch
            {
                // Silently ignore memory query errors - don't break agent execution
            }
        }
        
        _conversation.AddUserMessage(prompt);
        var timeoutMs = promptOrInstance.Type == ValueType.Object &&
                        promptOrInstance.AsObject() is PromptInstance boundedPrompt &&
                        boundedPrompt.WithinTimeoutMs is > 0
            ? boundedPrompt.WithinTimeoutMs.Value
            : ConversationInstance.ResolveThinkTimeoutMs();
        ConversationInstance.ThinkDeadlineUtc = timeoutMs > 0
            ? DateTime.UtcNow.AddMilliseconds(timeoutMs)
            : null;
        RuntimeValue response;
        try
        {
            response = _conversation.Send(responseFormat, requestOverrides);
        }
        finally
        {
            ConversationInstance.ThinkDeadlineUtc = null;
        }
        
        // After getting response, optionally remember the interaction
        if (_autoRememberOnThink && _memory != null && response.Type == ValueType.Object)
        {
            try
            {
                var responseObj = response.AsObject();
                if (responseObj is JsonObject jsonObj)
                {
                    var content = jsonObj.Get("content");
                    if (content.Type == ValueType.String)
                    {
                        var metadata = new JsonObject();
                        metadata.Set("type", RuntimeValue.String("episodic"));
                        metadata.Set("source", RuntimeValue.String("agent"));
                        var memoryScope = ResolveMemoryScope();
                        if (!string.IsNullOrWhiteSpace(memoryScope))
                            metadata.Set("scope", RuntimeValue.String(memoryScope));
                        _memory.CallMethod("remember", new List<RuntimeValue> 
                        { 
                            RuntimeValue.String(prompt),
                            content,
                            RuntimeValue.Object(metadata)
                        }, _interpreter!);
                    }
                }
            }
            catch
            {
                // Silently ignore memory storage errors
            }
        }
        
        return response;
    }
    
    private static List<RuntimeValue> MergeWorkingMemory(RuntimeValue semanticContext, RuntimeValue recentContext)
    {
        var merged = new List<RuntimeValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        
        void Add(RuntimeValue value)
        {
            var key = GraphMemoryInstance.FormatMemoryLine(value);
            if (seen.Add(key))
                merged.Add(value);
        }
        
        if (recentContext.Type == ValueType.Array)
        {
            foreach (var value in recentContext.AsArray())
                Add(value);
        }
        
        if (semanticContext.Type == ValueType.Array)
        {
            foreach (var value in semanticContext.AsArray())
                Add(value);
        }
        
        return merged;
    }
    
    public RuntimeValue GetConversation()
    {
        return _conversation != null ? RuntimeValue.Object(_conversation) : RuntimeValue.Null();
    }
    
    public RuntimeValue Reset()
    {
        if (_conversation != null)
        {
            _conversation.Clear();
            // Re-add tools
            foreach (var tool in _tools)
            {
                _conversation.AddTool(tool);
            }
        }
        
        // Report agent reset to dashboard (non-blocking)
        try
        {
            AgentDashboardService.Instance.ReportAgentReset(Name);
        }
        catch
        {
            // Silently ignore - dashboard reporting should not affect agent execution
        }
        
        return RuntimeValue.Null();
    }
    
    public RuntimeValue AddToolByName(string toolName)
    {
        var tool = ToolRegistry.Instance.GetTool(toolName);
        if (tool == null)
            throw new Exception($"Tool '{toolName}' not found in registry");
        return AddTool(tool);
    }
    
    public RuntimeValue AddAllTools()
    {
        var allTools = ToolRegistry.Instance.GetAllTools();
        foreach (var tool in allTools.Values)
        {
            AddTool(tool);
        }
        return RuntimeValue.Null();
    }
    
    public RuntimeValue GetAvailableTools()
    {
        var toolNames = ToolRegistry.Instance.GetToolNames();
        var namesList = new List<RuntimeValue>();
        foreach (var name in toolNames)
        {
            namesList.Add(RuntimeValue.String(name));
        }
        return RuntimeValue.Array(namesList);
    }
    
    public RuntimeValue AddSubAgent(AgentInstance subAgent, string toolDescription)
    {
        // Create an AgentToolInstance wrapper with agent name and tool description
        var agentTool = new AgentToolInstance(subAgent, subAgent.Name, toolDescription);
        
        // Add to tools list and conversation
        return AddTool(agentTool);
    }
    
    private static LlmRequestOverrides? BuildLlmRequestOverrides(PromptInstance promptInst)
    {
        HashSet<string>? toolNames = null;
        if (promptInst.Tools != null && promptInst.Tools.Count > 0)
            toolNames = new HashSet<string>(promptInst.Tools, StringComparer.OrdinalIgnoreCase);

        if (promptInst.Model == null
            && !promptInst.Temperature.HasValue
            && !promptInst.MaxTokens.HasValue
            && toolNames == null)
        {
            return null;
        }

        return new LlmRequestOverrides
        {
            Model = promptInst.Model,
            Temperature = promptInst.Temperature,
            MaxTokens = promptInst.MaxTokens,
            ToolNames = toolNames
        };
    }

    public virtual RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        EnsureInterpreter();
        switch (methodName)
        {
            case "addTool":
                if (args.Count != 1)
                    throw new Exception("addTool() expects 1 argument (Tool instance or tool name string)");
                // Check if argument is a string (tool name) or Tool instance
                if (args[0].Type == ValueType.String)
                {
                    // Tool name - get from registry
                    return AddToolByName(args[0].AsString());
                }
                else if (args[0].Type == ValueType.Object)
                {
                    // Tool instance
                    var toolObj = args[0].AsObject();
                    if (toolObj is not ToolInstance tool)
                        throw new Exception("addTool() expects a Tool instance or tool name string");
                    return AddTool(tool);
                }
                else
                {
                    throw new Exception("addTool() expects a Tool instance or tool name string");
                }
            
            case "addToolByName":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("addToolByName() expects 1 string argument (tool name)");
                return AddToolByName(args[0].AsString());
            
            case "addAllTools":
                if (args.Count != 0)
                    throw new Exception("addAllTools() expects no arguments");
                return AddAllTools();
            
            case "getAvailableTools":
                if (args.Count != 0)
                    throw new Exception("getAvailableTools() expects no arguments");
                return GetAvailableTools();
            
            case "think":
                if (args.Count != 1)
                    throw new Exception("think() expects 1 argument (string or PromptInstance)");
                return Think(args[0]);
            
            case "getConversation":
                return GetConversation();
            
            case "reset":
                return Reset();
            
            case "addSubAgent":
                if (args.Count != 2)
                    throw new Exception("addSubAgent() expects 2 arguments: (Agent instance, tool description string)");
                if (args[0].Type != ValueType.Object)
                    throw new Exception("addSubAgent() first argument must be an Agent instance");
                if (args[1].Type != ValueType.String)
                    throw new Exception("addSubAgent() second argument must be a string (tool description)");
                
                var agentObj = args[0].AsObject();
                if (agentObj is not AgentInstance subAgent)
                    throw new Exception("addSubAgent() first argument must be an Agent instance");
                
                var toolDesc = args[1].AsString();
                return AddSubAgent(subAgent, toolDesc);
            
            case "enableMemory":
                return EnableMemory(args);
            
            case "useMemory":
                return UseMemory(args);
            
            case "getMemory":
                return GetMemory();
            
            case "saveMemory":
                return SaveMemory(args);
            
            case "remember":
                if (args.Count < 1)
                    throw new Exception("remember() expects at least 1 argument (fact, context?)");
                return Remember(args);
            
            case "setAutoRememberOnThink":
                if (args.Count != 1 || args[0].Type != ValueType.Boolean)
                    throw new Exception("setAutoRememberOnThink() expects 1 boolean argument");
                _autoRememberOnThink = args[0].AsBoolean();
                return RuntimeValue.Null();
            
            case "setMemoryScope":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setMemoryScope() expects 1 string argument (scope)");
                var scopeValue = args[0].AsString();
                _memoryScope = string.IsNullOrWhiteSpace(scopeValue) ? null : scopeValue.Trim();
                return RuntimeValue.Null();

            case "setMemoryScopeParent":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setMemoryScopeParent() expects 1 string argument (parent scope)");
                var parentScopeValue = args[0].AsString();
                _memoryScopeParent = string.IsNullOrWhiteSpace(parentScopeValue) ? null : parentScopeValue.Trim();
                return RuntimeValue.Null();

            case "setMemoryScopeHierarchy":
                if (args.Count != 1 || args[0].Type != ValueType.Array)
                    throw new Exception("setMemoryScopeHierarchy() expects 1 array argument (scope list)");
                _memoryScopeHierarchy = new List<string>();
                foreach (var item in args[0].AsArray())
                {
                    if (item.Type == ValueType.String && !string.IsNullOrWhiteSpace(item.AsString()))
                        _memoryScopeHierarchy.Add(item.AsString().Trim());
                }
                if (_memoryScopeHierarchy.Count == 0)
                    _memoryScopeHierarchy = null;
                return RuntimeValue.Null();

            case "setMemoryRerank":
                if (args.Count < 1 || args[0].Type != ValueType.Boolean)
                    throw new Exception("setMemoryRerank() expects at least 1 boolean argument (enabled)");
                _memoryQueryRerankEnabled = args[0].AsBoolean();
                _memoryQueryRerankMode = null;
                _memoryQueryRerankModelPath = null;
                _memoryQueryRerankTopK = null;
                if (args.Count > 1 && args[1].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[1].AsString()))
                    _memoryQueryRerankMode = args[1].AsString().Trim();
                if (args.Count > 2 && args[2].Type == ValueType.String)
                {
                    var pathArg = args[2].AsString();
                    if (!string.IsNullOrWhiteSpace(pathArg))
                        _memoryQueryRerankModelPath = pathArg.Trim();
                }
                if (args.Count > 3 && args[3].Type == ValueType.Integer)
                    _memoryQueryRerankTopK = args[3].AsInteger();
                return RuntimeValue.Null();
            
            case "addMemoryProgressTools":
                if (args.Count != 0)
                    throw new Exception("addMemoryProgressTools() expects no arguments");
                return AddMemoryProgressTools();

            case "setContextTrimHandoff":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setContextTrimHandoff() expects 1 string argument");
                _conversation?.SetContextTrimHandoffNote(args[0].AsString());
                return RuntimeValue.Null();

            case "getEstimatedContextTokens":
                if (args.Count != 0)
                    throw new Exception("getEstimatedContextTokens() expects no arguments");
                return RuntimeValue.Integer(_conversation?.EstimateContextTokens() ?? 0);
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
    
    public void SetInterpreter(Interpreter interpreter)
    {
        _interpreter = interpreter;
        TranspiledBuiltinRuntime.SetInterpreter(interpreter);
        if (_memory != null)
        {
            _memory.SetInterpreter(interpreter);
        }
    }

    protected void EnsureInterpreter()
    {
        if (_interpreter != null)
            return;

        _interpreter = TranspiledBuiltinRuntime.GetOrCreateInterpreter();
        if (_memory != null)
            _memory.SetInterpreter(_interpreter);
    }
    
    public RuntimeValue EnableMemory(List<RuntimeValue> args)
    {
        EnsureInterpreter();
        
        if (args.Count >= 1 && args[0].Type == ValueType.String)
        {
            var pathArg = args[0].AsString();
            if (!string.IsNullOrWhiteSpace(pathArg)
                && !int.TryParse(pathArg, out _)
                && pathArg is not ("single" or "double"))
            {
                return EnableSharedMemoryAtPath(pathArg);
            }
        }
        
        if (_memory == null)
        {
            _memory = new GraphMemoryInstance();
            _memory.SetInterpreter(_interpreter!);
        }
        
        var initArgs = new List<RuntimeValue>();
        if (args.Count >= 1 && args[0].Type == ValueType.Integer)
            initArgs.Add(args[0]);
        if (args.Count >= 2 && args[1].Type == ValueType.String)
            initArgs.Add(args[1]);
        
        _memory.CallMethod("initialize", initArgs, _interpreter!);
        _memoryPath = null;
        return RuntimeValue.Null();
    }
    
    private RuntimeValue EnableSharedMemoryAtPath(string path)
    {
        var normalizedPath = path.Trim();
        lock (SharedMemoryLock)
        {
            if (!SharedMemoriesByPath.TryGetValue(normalizedPath, out _memory))
            {
                _memory = new GraphMemoryInstance();
                _memory.SetInterpreter(_interpreter!);
                _memory.CallMethod("initialize", new List<RuntimeValue>(), _interpreter!);
                
                var basePath = normalizedPath;
                if (File.Exists($"{basePath}.graph.json")
                    || File.Exists($"{basePath}.metadata.json")
                    || File.Exists($"{basePath}.vectordb.bin"))
                {
                    _memory.CallMethod("load", new List<RuntimeValue> { RuntimeValue.String(normalizedPath) }, _interpreter!);
                }
                
                SharedMemoriesByPath[normalizedPath] = _memory;
            }
            else
            {
                _memory.SetInterpreter(_interpreter!);
            }
        }
        
        _memoryPath = normalizedPath;
        return RuntimeValue.Null();
    }
    
    public RuntimeValue SaveMemory(List<RuntimeValue> args)
    {
        if (_memory == null)
            throw new Exception("saveMemory() requires memory — call useMemory() or enableMemory() first");
        
        EnsureInterpreter();
        
        var path = _memoryPath;
        if (args.Count >= 1 && args[0].Type == ValueType.String && !string.IsNullOrWhiteSpace(args[0].AsString()))
            path = args[0].AsString().Trim();
        
        if (string.IsNullOrWhiteSpace(path))
            throw new Exception("saveMemory() requires a path argument or prior enableMemory(path)");
        
        _memory.CallMethod("save", new List<RuntimeValue> { RuntimeValue.String(path) }, _interpreter!);
        _memoryPath = path;
        return RuntimeValue.Null();
    }
    
    /// <summary>
    /// Attaches an existing GraphMemory instance to this agent (shared memory).
    /// Multiple agents can use the same memory instance to share remembered facts.
    /// </summary>
    public RuntimeValue UseMemory(List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new Exception("useMemory() expects 1 argument (GraphMemory instance)");
        if (args[0].Type != ValueType.Object)
            throw new Exception("useMemory() expects a GraphMemory instance");
        var obj = args[0].AsObject();
        if (obj is not GraphMemoryInstance graphMemory)
            throw new Exception("useMemory() expects a GraphMemory instance");
        _memory = graphMemory;
        if (_interpreter != null)
            _memory.SetInterpreter(_interpreter);
        return RuntimeValue.Null();
    }
    
    public RuntimeValue GetMemory()
    {
        return _memory != null ? RuntimeValue.Object(_memory) : RuntimeValue.Null();
    }
    
    public RuntimeValue Remember(List<RuntimeValue> args)
    {
        if (_memory == null)
        {
            EnableMemory(new List<RuntimeValue>());
        }
        
        if (_interpreter == null)
            throw new Exception("Interpreter not set for Agent");
        
        return _memory!.CallMethod("remember", EnsureRememberScope(args), _interpreter);
    }
    
    private List<RuntimeValue> EnsureRememberScope(List<RuntimeValue> args)
    {
        var scope = ResolveMemoryScope();
        if (string.IsNullOrWhiteSpace(scope))
            return args;
        
        JsonObject? metadata = null;
        if (args.Count >= 3 && args[2].Type == ValueType.Object && args[2].AsObject() is JsonObject metaWithContext)
            metadata = metaWithContext;
        else if (args.Count >= 2 && args[1].Type == ValueType.Object && args[1].AsObject() is JsonObject metaOnly)
            metadata = metaOnly;
        
        if (metadata != null)
        {
            var existing = metadata.Get("scope", null);
            if (existing != null && existing.Type == ValueType.String && !string.IsNullOrWhiteSpace(existing.AsString()))
                return args;
            metadata.Set("scope", RuntimeValue.String(scope));
            return args;
        }
        
        var meta = new JsonObject();
        meta.Set("scope", RuntimeValue.String(scope));
        if (args.Count >= 2 && args[1].Type != ValueType.Object)
            return new List<RuntimeValue> { args[0], args[1], RuntimeValue.Object(meta) };
        return new List<RuntimeValue> { args[0], RuntimeValue.Object(meta) };
    }
    
    private string? ResolveMemoryScope()
    {
        if (!string.IsNullOrWhiteSpace(_memoryScope))
            return _memoryScope;

        var chatId = System.Environment.GetEnvironmentVariable("MALDA_CHAT_ID");
        if (!string.IsNullOrWhiteSpace(chatId))
            return "chat:" + chatId.Trim();

        return null;
    }

    private void ApplyMemoryQueryRerank(JsonObject queryOptions)
    {
        var enabled = _memoryQueryRerankEnabled;
        if (!enabled)
        {
            var env = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_RERANK");
            enabled = string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
        }
        if (!enabled)
            return;

        queryOptions.Set("rerank", RuntimeValue.Boolean(true));

        var mode = _memoryQueryRerankMode;
        if (string.IsNullOrWhiteSpace(mode))
            mode = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_RERANK_MODE");
        if (!string.IsNullOrWhiteSpace(mode))
            queryOptions.Set("rerankMode", RuntimeValue.String(mode.Trim()));

        var modelPath = _memoryQueryRerankModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
            modelPath = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_RERANK_MODEL_PATH");
        if (!string.IsNullOrWhiteSpace(modelPath))
            queryOptions.Set("rerankModelPath", RuntimeValue.String(modelPath.Trim()));

        if (_memoryQueryRerankTopK.HasValue && _memoryQueryRerankTopK.Value > 0)
            queryOptions.Set("rerankTopK", RuntimeValue.Integer(_memoryQueryRerankTopK.Value));
    }

    private List<string> BuildMemoryScopeHierarchy(string? memoryScope)
    {
        if (_memoryScopeHierarchy != null && _memoryScopeHierarchy.Count > 0)
        {
            var hierarchy = new List<string>();
            if (!string.IsNullOrWhiteSpace(memoryScope)
                && !_memoryScopeHierarchy.Exists(s => string.Equals(s, memoryScope, StringComparison.OrdinalIgnoreCase)))
            {
                hierarchy.Add(memoryScope.Trim());
            }
            foreach (var scope in _memoryScopeHierarchy)
            {
                if (!hierarchy.Exists(s => string.Equals(s, scope, StringComparison.OrdinalIgnoreCase)))
                    hierarchy.Add(scope);
            }
            if (!hierarchy.Exists(s => string.Equals(s, "global", StringComparison.OrdinalIgnoreCase)))
                hierarchy.Add("global");
            return hierarchy;
        }

        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(memoryScope))
            result.Add(memoryScope.Trim());
        var parent = _memoryScopeParent;
        if (string.IsNullOrWhiteSpace(parent))
            parent = System.Environment.GetEnvironmentVariable("MALDA_MEMORY_SCOPE_PARENT");
        if (!string.IsNullOrWhiteSpace(parent))
            result.Add(parent.Trim());
        result.Add("global");
        return result;
    }
    
    public RuntimeValue AddMemoryProgressTools()
    {
        if (_memory == null)
            throw new Exception("addMemoryProgressTools() requires memory — call useMemory() or enableMemory() first");
        if (_interpreter == null)
            throw new Exception("Interpreter not set for Agent");
        if (_memoryProgressToolsAdded)
            return RuntimeValue.Null();
        
        var memoryScope = ResolveMemoryScope();
        AddTool(MemoryProgressToolInstance.CreateRememberTool(_memory, _interpreter, memoryScope));
        AddTool(MemoryProgressToolInstance.CreateRecallTool(_memory, _interpreter, memoryScope));
        _memoryProgressToolsAdded = true;
        
        AppendToSystemPrompt(
            "\nProgress memory tools: remember_progress(note, type?, phase?) saves notes to GraphMemory; " +
            "recall_progress(query?, maxResults?, phase?) retrieves recent and relevant notes. " +
            "When a memory scope is active, progress notes are scoped automatically.");
        
        return RuntimeValue.Null();
    }
}
