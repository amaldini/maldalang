// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public class ActorDeclaration : Statement
{
    public string Name { get; }
    public List<ClassMember> Members { get; } // Fields, constructors, and methods/handlers
    public List<MessageDeclaration> Messages { get; } // Actor message declarations (actor sugar)
    public List<Decorator> Decorators { get; }

    /// <summary>The decorator that runs an actor off the main thread.</summary>
    public const string WorkerDecorator = "worker";

    /// <summary>
    /// <c>@worker() actor</c>: a Web Worker on JavaScript, a thread on the host backends. Messages,
    /// spawn arguments and replies cross by copy (structured clone).
    /// </summary>
    public bool IsWorker => Decorators.Any(d => d.Name == WorkerDecorator);

    public ActorDeclaration(string name, List<ClassMember> members, List<MessageDeclaration> messages, int line = 0, int column = 0, List<Decorator>? decorators = null)
        : base(line, column)
    {
        Name = name;
        Members = members;
        Messages = messages ?? new List<MessageDeclaration>();
        Decorators = decorators ?? new List<Decorator>();
    }
}
