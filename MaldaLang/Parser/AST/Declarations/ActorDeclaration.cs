// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Parser.AST.Declarations;

using MaldaLang.Parser.AST.Statements;

public class ActorDeclaration : Statement
{
    public string Name { get; }
    public List<ClassMember> Members { get; } // Fields, constructors, and methods/handlers
    public List<MessageDeclaration> Messages { get; } // Actor message declarations (actor sugar)
    
    public ActorDeclaration(string name, List<ClassMember> members, List<MessageDeclaration> messages, int line = 0, int column = 0)
        : base(line, column)
    {
        Name = name;
        Members = members;
        Messages = messages ?? new List<MessageDeclaration>();
    }
}
