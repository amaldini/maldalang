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
