// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class Message
{
    public Guid Id { get; }
    public RuntimeValue Payload { get; }
    public ActorReference? Sender { get; }
    public string? HandlerName { get; set; } // Optional: specific handler to route to
    public Guid? CorrelationId { get; }
    public List<RuntimeValue>? Arguments { get; }
    public bool ReceiveConsumed { get; set; }

    /// <summary>
    /// Work the receiving actor runs in its own loop instead of a handler (a send's timeout
    /// handler), so it never runs concurrently with the actor's other messages.
    /// </summary>
    public Func<Task>? Action { get; init; }
    
    public Message(
        RuntimeValue payload,
        ActorReference? sender = null,
        string? handlerName = null,
        Guid? correlationId = null,
        List<RuntimeValue>? arguments = null)
    {
        Id = Guid.NewGuid();
        Payload = payload;
        Sender = sender;
        HandlerName = handlerName;
        CorrelationId = correlationId;
        Arguments = arguments;
    }
    
    public override string ToString()
    {
        return $"Message(id: {Id}, handler: {HandlerName ?? "null"}, payload: {Payload}, sender: {Sender?.ToString() ?? "null"}, correlationId: {CorrelationId?.ToString() ?? "null"})";
    }
}
