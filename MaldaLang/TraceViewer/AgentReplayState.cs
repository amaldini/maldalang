// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// State model for restoring an agent/conversation from a trace step.
// Used by TraceReplayEngine.PrepareAgentFromStep and RestoreConversationState.

namespace MaldaLang.TraceViewer;

using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Interpreter;

/// <summary>
/// Snapshot of agent/conversation state at a trace step, for replay or branch.
/// </summary>
public sealed class AgentReplayState
{
    public List<RuntimeValue> Messages { get; }
    public string SystemPrompt { get; }
    public Dictionary<string, ToolInstance> Tools { get; }
    public string? AgentName { get; }

    public AgentReplayState(
        List<RuntimeValue> messages,
        string systemPrompt,
        Dictionary<string, ToolInstance> tools,
        string? agentName)
    {
        Messages = messages ?? new List<RuntimeValue>();
        SystemPrompt = systemPrompt ?? "";
        Tools = tools ?? new Dictionary<string, ToolInstance>();
        AgentName = agentName;
    }
}
