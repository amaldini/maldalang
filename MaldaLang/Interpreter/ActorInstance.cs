// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;

public class ActorInstance
{
    public ActorDefinition Actor { get; }
    public ActorMailbox Mailbox { get; }
    public Environment State { get; } // Isolated actor state
    public string Id { get; }
    private bool _isRunning = false;
    
    public ActorInstance(ActorDefinition actor, string id)
    {
        Actor = actor;
        Id = id;
        Mailbox = new ActorMailbox();
        State = new Environment(); // Isolated environment for actor state
    }
    
    public bool IsRunning
    {
        get => _isRunning;
        set => _isRunning = value;
    }
    
    public void Stop()
    {
        _isRunning = false;
        Mailbox.Close();
    }
    
    public override string ToString()
    {
        return $"<{Actor.Name} instance#{Id}>";
    }
}
