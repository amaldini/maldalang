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

    /// <summary>
    /// The worker actor this actor runs inside: itself for a <c>@worker</c> actor, the spawner's
    /// home for an actor spawned by code running in a worker, null on the main thread. A value
    /// crosses a worker boundary (and is copied) when it moves between two different homes.
    /// </summary>
    public ActorInstance? Home { get; internal set; }
    private bool _isRunning = false;
    
    /// <param name="globals">
    /// Program globals (top-level functions, variables, std-lib namespaces). Fields live in
    /// <see cref="State"/> itself and stay per-instance; only names the actor does not
    /// declare fall through to globals, as in the C# and JS backends.
    /// </param>
    public ActorInstance(ActorDefinition actor, string id, Environment? globals = null)
    {
        Actor = actor;
        Id = id;
        Mailbox = new ActorMailbox();
        State = new Environment(globals); // Per-instance actor state
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
