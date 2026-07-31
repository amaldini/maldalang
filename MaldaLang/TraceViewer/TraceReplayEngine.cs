// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Core engine for restoring trace state and managing replay execution.
// Used by ReplayToHere, RunFromHere, and BranchFromHere in the IDE trace viewer.

namespace MaldaLang.TraceViewer;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Tracing;
using ValueType = MaldaLang.Interpreter.ValueType;

/// <summary>
/// Restores file system and agent state from a trace for time-travel debugging.
/// </summary>
public static class TraceReplayEngine
{
    /// <summary>
    /// Registers an optional custom tool factory used during replay to reconstruct non built-in tools.
    /// This allows hosts that define their own ToolInstance implementations to participate in replay.
    /// Pass null to remove the custom factory.
    /// </summary>
    /// <param name="factory">Factory delegate receiving (toolName, workingDirectory) and returning a ToolInstance or null.</param>
    public static void RegisterCustomToolFactory(Func<string, string?, ToolInstance?>? factory)
    {
        ToolReplayHelper.RegisterCustomToolFactory(factory);
    }

    /// <summary>
    /// Restores file system state from the ReplayContext snapshot at the given step.
    /// Writes each file in Context.Files to disk.
    /// </summary>
    /// <param name="context">Replay context; will be advanced to stepIndex.</param>
    /// <param name="stepIndex">Zero-based event index to restore state up to.</param>
    /// <param name="workingDirectory">If set, files are written under this directory (sandbox mode). If null, original paths are used.</param>
    /// <returns>Paths of files that were written.</returns>
    public static IReadOnlyList<string> RestoreStateToStep(
        ReplayContext context,
        int stepIndex,
        string? workingDirectory = null)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (stepIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(stepIndex));

        context.StepTo(stepIndex);
        var files = context.Files;
        var restored = new List<string>();

        foreach (var kv in files)
        {
            var path = kv.Key;
            var content = kv.Value ?? "";

            string targetPath = workingDirectory != null
                ? Path.Combine(workingDirectory, Path.GetFileName(path))
                : path;

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(targetPath, content);
            restored.Add(targetPath);
        }

        return restored;
    }

    /// <summary>
    /// Builds agent/conversation state up to the given step from the trace session.
    /// Extracts messages from AgentMessage events, system prompt from the first LlmRequest, and agent name from events.
    /// </summary>
    /// <param name="session">Loaded trace viewer session.</param>
    /// <param name="stepIndex">Zero-based event index (inclusive) to include.</param>
    /// <returns>Agent replay state with messages, system prompt, tools (v1 empty), and agent name.</returns>
    public static AgentReplayState PrepareAgentFromStep(TraceViewerSession session, int stepIndex)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));
        if (stepIndex < 0 || stepIndex >= session.Events.Count)
            throw new ArgumentOutOfRangeException(nameof(stepIndex));

        var messages = new List<RuntimeValue>();
        var systemPrompt = "";
        var tools = new Dictionary<string, ToolInstance>();
        string? agentName = null;

        for (int i = 0; i <= stepIndex; i++)
        {
            var evt = session.Events[i].RawEvent;

            if (evt.AgentName != null && agentName == null)
                agentName = evt.AgentName;

            if (evt.Type == TraceEventType.AgentMessage && TryGetPayload(evt, out var payloadMsg))
            {
                var role = payloadMsg.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : "user";
                var content = payloadMsg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : "";
                if (role == null) role = "user";
                if (content == null) content = "";
                var msgObj = new JsonObject();
                msgObj.Set("role", RuntimeValue.String(role));
                msgObj.Set("content", RuntimeValue.String(content));
                messages.Add(RuntimeValue.Object(msgObj));
            }
            else if (evt.Type == TraceEventType.LlmRequest && string.IsNullOrEmpty(systemPrompt) && TryGetPayload(evt, out var payloadReq))
            {
                if (payloadReq.TryGetProperty("systemPrompt", out var sp) && sp.ValueKind == JsonValueKind.String)
                    systemPrompt = sp.GetString() ?? "";
            }
            else if (evt.Type == TraceEventType.ToolCallStart && TryGetPayload(evt, out var payloadTool))
            {
                // Best-effort reconstruction of tools that were used up to this step.
                var toolName = payloadTool.TryGetProperty("toolName", out var tn) && tn.ValueKind == JsonValueKind.String
                    ? tn.GetString()
                    : null;
                var workingDir = payloadTool.TryGetProperty("workingDirectory", out var wd) && wd.ValueKind == JsonValueKind.String
                    ? wd.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(toolName) && !tools.ContainsKey(toolName!))
                {
                    var tool = ToolReplayHelper.TryCreateTool(toolName!, workingDir);
                    if (tool != null)
                    {
                        // Key by schema/tool name to avoid duplicates.
                        tools[tool.Name] = tool;
                    }
                }
            }
        }

        return new AgentReplayState(messages, systemPrompt, tools, agentName);
    }

    /// <summary>
    /// Restores conversation messages, system prompt, and tools from a replay state.
    /// Clears the conversation, sets the system prompt, re-adds user/assistant messages, and adds tools.
    /// </summary>
    public static void RestoreConversationState(ConversationInstance conversation, AgentReplayState state)
    {
        if (conversation == null)
            throw new ArgumentNullException(nameof(conversation));
        if (state == null)
            throw new ArgumentNullException(nameof(state));

        conversation.SetSystemPrompt(state.SystemPrompt);
        conversation.Clear();

        foreach (var msgVal in state.Messages)
        {
            if (msgVal.Type != ValueType.Object)
                continue;
            var msgObj = msgVal.AsObject();
            var role = GetStringProp(msgObj, "role") ?? "user";
            var content = GetStringProp(msgObj, "content") ?? "";
            if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
                conversation.AddUserMessage(content);
            else if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
                conversation.AddAssistantMessage(content);
            // Skip "system" – already applied via SetSystemPrompt and Clear()
        }

        foreach (var tool in state.Tools.Values)
            conversation.AddTool(tool);
    }

    private static bool TryGetPayload(TraceEvent evt, out JsonElement payload)
    {
        payload = default;
        if (evt.Payload is not string json || string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            payload = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? GetStringProp(ObjectInstance obj, string name)
    {
        if (obj == null) return null;
        try
        {
            var v = obj.Get(name, null);
            return v?.Type == ValueType.String ? v.AsString() : null;
        }
        catch
        {
            return null;
        }
    }
}
