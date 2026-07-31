// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

public class ActorReference
{
    private readonly ActorInstance _instance;
    private readonly string _id;
    
    internal ActorReference(ActorInstance instance, string id)
    {
        _instance = instance;
        _id = id;
    }
    
    internal ActorInstance Instance => _instance;
    
    public string Id => _id;
    
    public void Send(RuntimeValue message, ActorReference? sender = null, string? handlerName = null, Guid? correlationId = null, List<RuntimeValue>? arguments = null)
    {
        var msg = new Message(message, sender, handlerName, correlationId, arguments);
        _instance.Mailbox.Send(msg);
    }
    
    public void Stop()
    {
        ActorRuntime.Instance.StopActor(_id);
    }
    
    public override string ToString()
    {
        return $"<ActorReference: {_instance.Actor.Name}#{_id}>";
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is ActorReference other)
        {
            return _id == other._id && _instance == other._instance;
        }
        return false;
    }
    
    public override int GetHashCode()
    {
        return _id.GetHashCode();
    }
}
