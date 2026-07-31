// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;

public class ActorDefinition
{
    public string Name { get; }
    public Dictionary<string, ClassMember> Fields { get; }
    public Dictionary<string, FunctionValue> MessageHandlers { get; } // Message handlers (on messageType(...) { ... })
    public Dictionary<string, MessageDeclaration> Messages { get; } // Actor message declarations (actor sugar)
    public FunctionValue? Constructor { get; set; }
    public Dictionary<string, RuntimeValue> StaticFields { get; }
    public Dictionary<string, FunctionValue> StaticMethods { get; }
    
    public ActorDefinition(string name)
    {
        Name = name;
        Fields = new Dictionary<string, ClassMember>();
        MessageHandlers = new Dictionary<string, FunctionValue>();
        Messages = new Dictionary<string, MessageDeclaration>();
        StaticFields = new Dictionary<string, RuntimeValue>();
        StaticMethods = new Dictionary<string, FunctionValue>();
    }
    
    public FunctionValue? FindMessageHandler(string handlerName)
    {
        if (MessageHandlers.ContainsKey(handlerName))
            return MessageHandlers[handlerName];
        
        return null;
    }
    
    public ClassMember? FindField(string name)
    {
        if (Fields.ContainsKey(name))
            return Fields[name];
        
        return null;
    }
}
