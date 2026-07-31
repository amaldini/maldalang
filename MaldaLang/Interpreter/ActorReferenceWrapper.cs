// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Declarations;

/// <summary>
/// Wrapper class to allow ActorReference to be used as an ObjectInstance
/// for method dispatch in the interpreter.
/// </summary>
internal class ActorReferenceWrapper : ObjectInstance
{
    private readonly ActorReference _actorRef;
    private static readonly ClassDefinition DummyClass = new("ActorReferenceWrapper", null);
    
    public ActorReferenceWrapper(ActorReference actorRef)
        : base(DummyClass)
    {
        _actorRef = actorRef;
    }
    
    public ActorReference ActorReference => _actorRef;
    
    public override RuntimeValue Get(string name, ClassDefinition? accessingClass = null)
    {
        // Only support the "stop" method
        if (name == "stop")
        {
            var wrapper = new FunctionValue(null, null, false, null)
            {
                BuiltInInstance = this,
                BuiltInMethod = "stop"
            };
            return RuntimeValue.Function(wrapper);
        }
        throw new RuntimeException($"ActorReference has no member '{name}'. Available methods: stop()");
    }
    
    public override string ToString()
    {
        return _actorRef.ToString();
    }
}
