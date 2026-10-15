// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLang.Runtime.Tracing;
using Spectre.Console;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class ConversationInstance : ObjectInstance
{
    internal static DateTime? ThinkDeadlineUtc { get; set; }

    internal static int ResolveThinkTimeoutMs()
    {
        foreach (var name in new[] { "MALDA_AGENT_THINK_TIMEOUT_MS", "MALDA_RALPH_ITER_TIMEOUT_MS" })
        {
            var raw = System.Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out var ms) && ms > 0)
                return ms;
        }
        return 0;
    }

    internal static int ResolveMaxLlmRounds()
    {
        foreach (var name in new[] { "MALDA_AGENT_MAX_LLM_ROUNDS", "MALDA_RALPH_INTERVIEW_MAX_LLM_ROUNDS", "MALDA_RALPH_MAX_LLM_ROUNDS" })
        {
            var raw = System.Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out var rounds) && rounds > 0)
                return rounds;
        }
        return 0;
    }

    internal static void EnsureWithinThinkDeadline()
    {
        if (IsThinkCancelRequested())
            throw new InvalidOperationException("Agent think() cancelled");
        if (ThinkDeadlineUtc.HasValue && DateTime.UtcNow > ThinkDeadlineUtc.Value)
            throw new InvalidOperationException("Agent think() timed out");
    }

    private List<RuntimeValue> _messages = new();
    private LLMClientInstance? _client;
    private LlamaCppClientInstance? _llamaClient;
    private LLMClientBridge.LLMClientBridgeInstance? _bridgeClient;
    private string _systemPrompt;
    private Dictionary<string, ToolInstance> _tools = new();
    private IInputProvider? _inputProvider;
    private static Action<string, string, string, bool, string?>? _toolCallLogger;
    private static readonly object AgentProgressLock = new();
    private static Action<RuntimeValue>? _agentProgressHandler;
    /// <summary>Process-wide fallback (single-threaded hosts / tests).</summary>
    private static string? _agentProgressLiveChannel;
    /// <summary>Per-request/async flow channel so concurrent ASK sessions do not clash.</summary>
    private static readonly AsyncLocal<string?> AgentProgressLiveChannelLocal = new();
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> ThinkCancelByChannel =
        new(StringComparer.Ordinal);
    private static readonly AsyncLocal<LiveDraftState?> LiveDraftLocal = new();
    internal const int DefaultLiveDraftMinIntervalMs = 80;
    internal const int LiveDraftMaxChars = 12000;
    internal static int LiveDraftMinIntervalMs = DefaultLiveDraftMinIntervalMs;
    private static bool _verboseLoggingResolved;
    private static bool _verboseLoggingEnabled;
    private static string? _verbosePhaseLabel;
    private static bool? _parallelToolCallsEnabled;

    [ThreadStatic]
    private static string? _readFileToolLineSummary;

    private static bool? _agentRichCliResolved;
    private static bool _agentRichCli;
    private static string? _statusBanner;
    private static readonly object ActivityStatusLock = new();
    private static int _activityLineWidth;
    private static readonly AsyncLocal<int> ActivitySendDepth = new();

    private static readonly HashSet<string> CompactVerboseToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "grep",
        "glob",
        "list_directory",
        "get_symbols",
        "get_parse_errors",
        "check_malda",
        "validate_json",
        "web_search",
        "web_fetch",
        "git_status",
        "git_log",
        "git_diff",
        "git_branch",
        "recall_progress",
    };

    private static readonly HashSet<string> ParallelSafeToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "grep",
        "glob",
        "list_directory",
        "get_symbols",
        "get_parse_errors",
        "check_malda",
        "validate_json",
        "web_search",
        "web_fetch",
        "git_status",
        "git_log",
        "git_diff",
        "git_branch",
        "recall_progress",
    };

    private static readonly HashSet<string> WriteToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "write_file",
        "replace_in_file",
        "edit_file",
        "insertAtLine",
        "insert_at_line",
        "delete_file",
        "copy_file",
        "ensure_dir",
        "update_plan",
        "mark_step",
        "test_malda",
    };

    private static readonly Dictionary<string, string> ToolNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["findstr"] = "grep",
        ["rg"] = "grep",
        ["run_terminal_cmd"] = "run_command",
        ["runCommand"] = "run_command",
        ["execute_command"] = "run_command",
        ["insert_at_line"] = "insertAtLine",
    };

    /// <summary>
    /// Factory-created tools that Conversation / <c>tool.execute</c> dispatch by name
    /// (interpret and C# transpile share this list).
    /// </summary>
    private static readonly HashSet<string> NamedBuiltInToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "read_file",
        "write_file",
        "replace_in_file",
        "edit_file",
        "list_directory",
        "delete_file",
        "copy_file",
        "ensure_dir",
        "insertAtLine",
        "insert_at_line",
        "ask_user",
        "glob",
        "grep",
        "git_status",
        "git_add",
        "git_commit",
        "git_log",
        "git_diff",
        "git_branch",
        "git_checkout",
        "git_push",
        "git_pull",
        "web_search",
        "web_fetch",
        "run_command",
        "run_malda",
        "compile_malda",
        "get_symbols",
        "get_parse_errors",
        "check_malda",
        "validate_json",
        "test_malda",
        "submit_plan",
        "update_plan",
        "mark_step",
        "create_mcp_agent_script",
    };

    internal static bool IsNamedBuiltInTool(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (NamedBuiltInToolNames.Contains(name))
            return true;
        return ToolNameAliases.TryGetValue(name, out var aliased)
            && NamedBuiltInToolNames.Contains(aliased);
    }

    internal static string CanonicalToolName(string name)
    {
        if (ToolNameAliases.TryGetValue(name, out var aliased))
            return aliased;
        return name;
    }

    /// <summary>
    /// Runs a factory-created built-in tool with the same dispatch as <c>think()</c>.
    /// Used by <c>tool.execute</c> so interpret and C# transpile share one implementation.
    /// </summary>
    internal static RuntimeValue ExecuteNamedBuiltIn(
        ToolInstance tool,
        RuntimeValue arguments,
        IInputProvider? inputProvider = null)
    {
        var conv = new ConversationInstance();
        conv._inputProvider = inputProvider;
        return conv.ExecuteToolOperation(tool, arguments);
    }

    /// <summary>
    /// Re-runs a recorded tool call from its <c>argumentsJson</c> payload.
    /// Used by <c>malda trace replay-tools</c>.
    /// </summary>
    internal static RuntimeValue ExecuteNamedBuiltInJson(ToolInstance tool, string? argumentsJson)
    {
        var conv = new ConversationInstance();
        var args = RuntimeValue.Object(new JsonObject());
        if (!string.IsNullOrWhiteSpace(argumentsJson))
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            args = conv.JsonToRuntimeValue(doc.RootElement);
        }
        return conv.ExecuteToolOperation(tool, args);
    }

    internal static string FormatToolResultJson(RuntimeValue value)
    {
        var conv = new ConversationInstance();
        return conv.SerializeRuntimeValueToJson(value);
    }

    private static readonly HashSet<string> ShellWrapperToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh", "cmd", "bash", "sh", "zsh", "fish", "csh", "tcsh", "ksh"
    };
    private int _llmRound;
    private bool _llmStreamHeaderPrinted;
    private bool _llmStreamThinkingPrinted;
    private int _turnPromptTokens;
    private int _turnCompletionTokens;
    private int _turnTotalTokens;
    private double _turnCost;
    private bool _turnHasUsage;
    private int _recordedTurnTokens;
    private double _recordedTurnCost;
    private readonly List<FailedWriteToolRecord> _turnFailedWriteTools = new();
    
    /// <summary>
    /// Optional trace session identifier associated with this conversation.
    /// When set and tracing is enabled, LLM and tool calls will be recorded
    /// under this session using <see cref="TraceManager"/>.
    /// </summary>
    public string? SessionId { get; set; }
    
    /// <summary>
    /// The name of the agent that owns this conversation, used for dashboard reporting.
    /// Set automatically by AgentInstance when the conversation is initialized.
    /// </summary>
    public string? AgentName { get; set; }
    
    public ConversationInstance() : base(null)
    {
        _systemPrompt = "";
    }
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Handle method access - create a FunctionValue wrapper
        if (name == "addUserMessage" || name == "addAssistantMessage" || name == "addTool" || 
            name == "send" || name == "getMessages" || name == "clear" || name == "getHistory" ||
            name == "getFailedWriteTools" || name == "getEstimatedContextTokens" ||
            name == "setContextTrimHandoff")
        {
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BuiltInInstance = this;
            wrapper.BuiltInMethod = name;
            return RuntimeValue.Function(wrapper);
        }
        
        throw new Exception($"Undefined property '{name}' on Conversation.");
    }
    
    public void Initialize(LLMClientInstance? client, LlamaCppClientInstance? llamaClient, LLMClientBridge.LLMClientBridgeInstance? bridgeClient, string systemPrompt, IInputProvider? inputProvider = null)
    {
        _client = client;
        _llamaClient = llamaClient;
        _bridgeClient = bridgeClient;
        _systemPrompt = systemPrompt;
        _inputProvider = inputProvider;
        _messages.Clear();
        
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            var systemMsg = new JsonObject();
            systemMsg.Set("role", RuntimeValue.String("system"));
            systemMsg.Set("content", RuntimeValue.String(systemPrompt));
            _messages.Add(RuntimeValue.Object(systemMsg));
        }
    }
    
    // Overload for backward compatibility
    public void Initialize(LLMClientInstance? client, string systemPrompt, IInputProvider? inputProvider = null)
    {
        Initialize(client, null, null, systemPrompt, inputProvider);
    }
    
    // Overload for backward compatibility with llamaClient
    public void Initialize(LLMClientInstance? client, LlamaCppClientInstance? llamaClient, string systemPrompt, IInputProvider? inputProvider = null)
    {
        Initialize(client, llamaClient, null, systemPrompt, inputProvider);
    }
    
    public void SetInputProvider(IInputProvider? inputProvider)
    {
        _inputProvider = inputProvider;
    }

    /// <summary>
    /// Sets the system prompt without clearing messages. Used when restoring from a trace replay.
    /// </summary>
    public void SetSystemPrompt(string systemPrompt)
    {
        _systemPrompt = systemPrompt ?? "";
    }

    /// <summary>
    /// Appends text to the system prompt without clearing messages.
    /// </summary>
    public void AppendToSystemPrompt(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        _systemPrompt = string.IsNullOrEmpty(_systemPrompt) ? text : _systemPrompt + text;
    }

    public RuntimeValue AddUserMessage(string content, IReadOnlyList<PromptAttachment>? attachments = null)
    {
        _llmRound = 0;
        ResetTurnUsage();
        _turnFailedWriteTools.Clear();
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("user"));
        if (attachments != null && attachments.Count > 0)
            msg.Set("content", PromptAttachmentCodec.BuildContentParts(content, attachments));
        else
            msg.Set("content", RuntimeValue.String(content));
        _messages.Add(RuntimeValue.Object(msg));
        
        // Record agent message in trace (if enabled). Never log data URLs / bytes.
        TraceManager.Record(
            TraceEventType.AgentMessage,
            attachments != null && attachments.Count > 0
                ? (object)new
                {
                    role = "user",
                    content,
                    attachments = PromptAttachmentCodec.TraceSummary(attachments),
                    toolName = (string?)null,
                    toolCallId = (string?)null
                }
                : new
                {
                    role = "user",
                    content,
                    toolName = (string?)null,
                    toolCallId = (string?)null
                },
            AgentName,
            SessionId);
        
        return RuntimeValue.Null();
    }
    
    public RuntimeValue AddAssistantMessage(string content)
    {
        var msg = new JsonObject();
        msg.Set("role", RuntimeValue.String("assistant"));
        msg.Set("content", RuntimeValue.String(content));
        _messages.Add(RuntimeValue.Object(msg));
        
        // Record agent message in trace (if enabled)
        TraceManager.Record(
            TraceEventType.AgentMessage,
            new
            {
                role = "assistant",
                content,
                toolName = (string?)null,
                toolCallId = (string?)null
            },
            AgentName,
            SessionId);
        
        return RuntimeValue.Null();
    }
    
    public RuntimeValue AddTool(ToolInstance tool)
    {
        _tools[tool.Name] = tool;
        return RuntimeValue.Null();
    }
    
    public RuntimeValue GetMessages()
    {
        return RuntimeValue.Array(new List<RuntimeValue>(_messages));
    }

    public RuntimeValue GetFailedWriteTools()
    {
        var items = new List<RuntimeValue>(_turnFailedWriteTools.Count);
        foreach (var failure in _turnFailedWriteTools)
        {
            var obj = new JsonObject();
            obj.Set("toolName", RuntimeValue.String(failure.ToolName));
            if (!string.IsNullOrEmpty(failure.Target))
                obj.Set("target", RuntimeValue.String(failure.Target));
            obj.Set("error", RuntimeValue.String(failure.Error));
            items.Add(RuntimeValue.Object(obj));
        }
        return RuntimeValue.Array(items);
    }
    
    public RuntimeValue Clear()
    {
        _messages.Clear();
        _turnFailedWriteTools.Clear();
        if (!string.IsNullOrEmpty(_systemPrompt))
        {
            var systemMsg = new JsonObject();
            systemMsg.Set("role", RuntimeValue.String("system"));
            systemMsg.Set("content", RuntimeValue.String(_systemPrompt));
            _messages.Add(RuntimeValue.Object(systemMsg));
        }
        return RuntimeValue.Null();
    }
    
    public RuntimeValue GetHistory()
    {
        var history = new StringBuilder();
        foreach (var msg in _messages)
        {
            if (msg.Type == ValueType.Object)
            {
                var msgObj = msg.AsObject();
                var role = GetStringProperty(msgObj, "role") ?? "unknown";
                var content = GetStringProperty(msgObj, "content") ?? "";
                history.AppendLine($"{role}: {content}");
            }
        }
        return RuntimeValue.String(history.ToString());
    }
    
    public RuntimeValue CallMethod(string methodName, List<RuntimeValue> args)
    {
        switch (methodName)
        {
            case "addUserMessage":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("addUserMessage() expects 1 string argument");
                return AddUserMessage(args[0].AsString());
            
            case "addAssistantMessage":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("addAssistantMessage() expects 1 string argument");
                return AddAssistantMessage(args[0].AsString());
            
            case "addTool":
                if (args.Count != 1 || args[0].Type != ValueType.Object)
                    throw new Exception("addTool() expects 1 Tool argument");
                var toolObj = args[0].AsObject();
                if (toolObj is not ToolInstance tool)
                    throw new Exception("addTool() expects a Tool instance");
                return AddTool(tool);
            
            case "send":
                return Send();
            
            case "getMessages":
                return GetMessages();

            case "getFailedWriteTools":
                return GetFailedWriteTools();
            
            case "clear":
                return Clear();
            
            case "getHistory":
                return GetHistory();

            case "getEstimatedContextTokens":
                return RuntimeValue.Integer(EstimateContextTokens());

            case "setContextTrimHandoff":
                if (args.Count != 1 || args[0].Type != ValueType.String)
                    throw new Exception("setContextTrimHandoff() expects 1 string argument");
                SetContextTrimHandoffNote(args[0].AsString());
                return RuntimeValue.Null();
            
            default:
                throw new Exception($"Unknown method: {methodName}");
        }
    }
}
