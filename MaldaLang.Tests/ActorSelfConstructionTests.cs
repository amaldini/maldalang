// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang;
using MaldaLang.Compiler;
using MaldaLang.Runtime.Actors;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// <c>self</c> inside an actor constructor (lambdas included) is the actor being built, as in
/// the interpreter; transpiled code used to throw "'self' can only be used within an actor".
/// </summary>
public class ActorSelfConstructionTests
{
    [Fact]
    public void Transpile_ActorConstructor_EntersConstructionScope()
    {
        var source = """
            actor Box {
                var me;
                function Box() {
                    var f = (u) => self;
                    me = f(0);
                }
                on ping() { print(me == self); }
            }
            var b = spawn Box();
            """;
        var lexer = new Lexer(source, "actor_self.malda");
        var parser = new Parser.Parser(lexer.Tokenize(), "actor_self.malda");
        var statements = parser.Parse();
        Assert.Empty(parser.Errors);

        var generated = new CSharpTranspiler(profilingOptions: null).Transpile(statements);

        Assert.Contains("var __previousSelf = ActorsRuntime.EnterConstruction(this);", generated);
        Assert.Contains("finally { ActorsRuntime.ExitConstruction(__previousSelf); }", generated);
    }

    [Fact]
    public void EnterConstruction_SelfMatchesSpawnedRef_AndIsRestored()
    {
        var instance = new object();
        var previous = ActorsRuntime.EnterConstruction(instance);
        var selfDuringConstruction = ActorsRuntime.GetSelf();
        ActorsRuntime.ExitConstruction(previous);

        Assert.Throws<System.InvalidOperationException>(() => ActorsRuntime.GetSelf());

        var spawned = ActorsRuntime.Spawn(instance);
        try
        {
            Assert.Equal(selfDuringConstruction, spawned);
        }
        finally
        {
            ActorsRuntime.Stop(spawned);
        }
    }
}
