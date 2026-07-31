// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns.ACP;

using System;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Run status enum for ACP protocol.
/// </summary>
public enum RunStatus
{
    Created,
    InProgress,
    Awaiting,
    Cancelling,
    Cancelled,
    Completed,
    Failed
}

/// <summary>
/// Represents citation metadata for message parts.
/// </summary>
public class CitationMetadata
{
    public string Kind { get; set; } = "citation";
    public int? StartIndex { get; set; }
    public int? EndIndex { get; set; }
    public string? Url { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Represents trajectory metadata for agent reasoning steps.
/// </summary>
public class TrajectoryMetadata
{
    public string Kind { get; set; } = "trajectory";
    public string? Message { get; set; }
    public string? ToolName { get; set; }
    public object? ToolInput { get; set; }
    public object? ToolOutput { get; set; }
}

/// <summary>
/// Represents a message part in ACP protocol with content and MIME type.
/// </summary>
public class ACPMessagePart
{
    public string? Name { get; set; }
    public string ContentType { get; set; } = "text/plain";
    public string? Content { get; set; }
    public string ContentEncoding { get; set; } = "plain"; // plain or base64
    public string? ContentUrl { get; set; }
    public object? Metadata { get; set; } // CitationMetadata or TrajectoryMetadata
    
    public ACPMessagePart(string content, string mimeType = "text/plain")
    {
        Content = content;
        ContentType = mimeType;
    }
    
    public ACPMessagePart()
    {
    }
}

/// <summary>
/// Represents an ACP message with multiple parts.
/// </summary>
public class ACPMessage
{
    public string Role { get; set; } = "user"; // user, agent, or agent/{name}
    public List<ACPMessagePart> Parts { get; set; } = new();
    public DateTime? CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    public ACPMessage()
    {
    }
    
    public ACPMessage(string textContent)
    {
        Parts.Add(new ACPMessagePart(textContent, "text/plain"));
    }
    
    public ACPMessage(List<ACPMessagePart> parts)
    {
        Parts = parts;
    }
    
    /// <summary>
    /// Gets all text content from message parts, joined together.
    /// </summary>
    public string GetTextContent()
    {
        return string.Join("", Parts.Where(p => p.ContentType.StartsWith("text/") && p.Content != null).Select(p => p.Content ?? ""));
    }
}

/// <summary>
/// Represents agent status metrics.
/// </summary>
public class ACPAgentStatus
{
    public double? AvgRunTokens { get; set; }
    public double? AvgRunTimeSeconds { get; set; }
    public double? SuccessRate { get; set; } // 0-100
}

/// <summary>
/// Represents agent metadata for discovery and cataloging.
/// </summary>
public class ACPAgentMetadata
{
    public Dictionary<string, object>? Annotations { get; set; }
    public string? Documentation { get; set; }
    public string? License { get; set; }
    public string? ProgrammingLanguage { get; set; }
    public List<string>? NaturalLanguages { get; set; }
    public string? Framework { get; set; }
    public List<ACPCapability>? Capabilities { get; set; }
    public List<string>? Domains { get; set; }
    public List<string>? Tags { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ACPPerson? Author { get; set; }
    public List<ACPPerson>? Contributors { get; set; }
    public List<ACPLink>? Links { get; set; }
    public List<ACPAgentDependency>? Dependencies { get; set; }
    public List<string>? RecommendedModels { get; set; }
}

/// <summary>
/// Represents an agent capability.
/// </summary>
public class ACPCapability
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>
/// Represents a person (author/contributor).
/// </summary>
public class ACPPerson
{
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Url { get; set; }
}

/// <summary>
/// Represents a link (source code, homepage, etc.).
/// </summary>
public class ACPLink
{
    public string Type { get; set; } = ""; // source-code, container-image, homepage, documentation
    public string Url { get; set; } = "";
}

/// <summary>
/// Represents an agent dependency.
/// </summary>
public class ACPAgentDependency
{
    public string Type { get; set; } = ""; // agent, tool, model
    public string Name { get; set; } = "";
}

/// <summary>
/// Represents an ACP agent manifest with metadata.
/// </summary>
public class ACPAgentManifest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> InputContentTypes { get; set; } = new() { "*/*" };
    public List<string> OutputContentTypes { get; set; } = new() { "*/*" };
    public ACPAgentMetadata? Metadata { get; set; }
    public ACPAgentStatus? Status { get; set; }
    public string Version { get; set; } = "1.0.0";
    
    public ACPAgentManifest()
    {
    }
    
    public ACPAgentManifest(string name, string description, string version = "1.0.0")
    {
        Name = name;
        Description = description;
        Version = version;
    }
}

/// <summary>
/// Represents an await request when agent needs input.
/// </summary>
public class ACPAwaitRequest
{
    public string? Prompt { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Represents an await resume payload.
/// </summary>
public class ACPAwaitResume
{
    public string? Input { get; set; }
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Represents a structured error.
/// </summary>
public class ACPError
{
    public string Code { get; set; } = "server_error"; // server_error, invalid_input, not_found
    public string Message { get; set; } = "";
    public object? Data { get; set; }
    
    public ACPError()
    {
    }
    
    public ACPError(string code, string message, object? data = null)
    {
        Code = code;
        Message = message;
        Data = data;
    }
}

/// <summary>
/// Represents a run response from an ACP agent.
/// </summary>
public class ACPRunResponse
{
    public string AgentName { get; set; } = "";
    public string? SessionId { get; set; }
    public string RunId { get; set; } = "";
    public RunStatus Status { get; set; } = RunStatus.Created;
    public ACPAwaitRequest? AwaitRequest { get; set; }
    public List<ACPMessage> Output { get; set; } = new();
    public ACPError? Error { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? FinishedAt { get; set; }
    
    // Legacy support - for backward compatibility
    public ACPMessage? Message
    {
        get => Output.FirstOrDefault();
        set
        {
            Output.Clear();
            if (value != null)
                Output.Add(value);
        }
    }
    
    // Legacy support - for backward compatibility
    public string? ErrorString
    {
        get => Error?.Message;
        set => Error = value != null ? new ACPError("server_error", value) : null;
    }
    
    public ACPRunResponse()
    {
    }
}

/// <summary>
/// Represents an agent in the discovery list.
/// </summary>
public class ACPAgentInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    
    public ACPAgentInfo()
    {
    }
}

/// <summary>
/// Represents an ACP session.
/// </summary>
public class ACPSession
{
    public string Id { get; set; } = "";
    public List<string> History { get; set; } = new(); // URIs to run history
    public string? State { get; set; } // URI to state
    
    public ACPSession()
    {
    }
    
    public ACPSession(string id)
    {
        Id = id;
    }
}

/// <summary>
/// Base class for ACP events.
/// </summary>
public abstract class ACPEvent
{
    public string Type { get; set; } = "";
}

/// <summary>
/// Message created event.
/// </summary>
public class MessageCreatedEvent : ACPEvent
{
    public ACPMessage Message { get; set; } = new();
    
    public MessageCreatedEvent()
    {
        Type = "message.created";
    }
}

/// <summary>
/// Message part event.
/// </summary>
public class MessagePartEvent : ACPEvent
{
    public ACPMessagePart Part { get; set; } = new();
    
    public MessagePartEvent()
    {
        Type = "message.part";
    }
}

/// <summary>
/// Message completed event.
/// </summary>
public class MessageCompletedEvent : ACPEvent
{
    public ACPMessage Message { get; set; } = new();
    
    public MessageCompletedEvent()
    {
        Type = "message.completed";
    }
}

/// <summary>
/// Generic event.
/// </summary>
public class GenericEvent : ACPEvent
{
    public object? Generic { get; set; }
    
    public GenericEvent()
    {
        Type = "generic";
    }
}

/// <summary>
/// Run created event.
/// </summary>
public class RunCreatedEvent : ACPEvent
{
    public ACPRunResponse Run { get; set; } = new();
    
    public RunCreatedEvent()
    {
        Type = "run.created";
    }
}

/// <summary>
/// Run in progress event.
/// </summary>
public class RunInProgressEvent : ACPEvent
{
    public ACPRunResponse Run { get; set; } = new();
    
    public RunInProgressEvent()
    {
        Type = "run.in-progress";
    }
}

/// <summary>
/// Run awaiting event.
/// </summary>
public class RunAwaitingEvent : ACPEvent
{
    public ACPRunResponse Run { get; set; } = new();
    
    public RunAwaitingEvent()
    {
        Type = "run.awaiting";
    }
}

/// <summary>
/// Run completed event.
/// </summary>
public class RunCompletedEvent : ACPEvent
{
    public ACPRunResponse Run { get; set; } = new();
    
    public RunCompletedEvent()
    {
        Type = "run.completed";
    }
}

/// <summary>
/// Run cancelled event.
/// </summary>
public class RunCancelledEvent : ACPEvent
{
    public ACPRunResponse Run { get; set; } = new();
    
    public RunCancelledEvent()
    {
        Type = "run.cancelled";
    }
}

/// <summary>
/// Run failed event.
/// </summary>
public class RunFailedEvent : ACPEvent
{
    public ACPRunResponse Run { get; set; } = new();
    
    public RunFailedEvent()
    {
        Type = "run.failed";
    }
}

/// <summary>
/// Error event.
/// </summary>
public class ErrorEvent : ACPEvent
{
    public ACPError Error { get; set; } = new();
    
    public ErrorEvent()
    {
        Type = "error";
    }
}
