// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;

public class ActorRuntime
{
    private static ActorRuntime? _instance;
    private readonly ConcurrentDictionary<string, ActorInstance> _actors = new();
    private readonly ConcurrentDictionary<string, Task> _actorTasks = new();
    private int _actorCounter = 0;
    
    public static ActorRuntime Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ActorRuntime();
            }
            return _instance;
        }
    }
    
    private ActorRuntime()
    {
    }
    
    public ActorReference SpawnActor(ActorDefinition actorDef, Interpreter parentInterpreter, List<RuntimeValue>? constructorArgs = null)
    {
        var id = $"{actorDef.Name}_{Interlocked.Increment(ref _actorCounter)}";
        var instance = new ActorInstance(actorDef, id);
        _actors[id] = instance;
        
        // Create isolated interpreter for this actor
        var actorInterpreter = new Interpreter(
            debuggerHook: parentInterpreter.GetDebuggerHook(),
            currentFile: parentInterpreter.GetCurrentFile(),
            inputProvider: parentInterpreter.GetInputProvider()
        );
        
        // Copy class definitions from parent (actors can use classes)
        foreach (var kvp in parentInterpreter._classes)
        {
            actorInterpreter._classes[kvp.Key] = kvp.Value;
        }
        
        // Copy actor definitions from parent
        foreach (var kvp in parentInterpreter._actors)
        {
            actorInterpreter._actors[kvp.Key] = kvp.Value;
        }
        
        // Start actor message processing loop (field initialization happens in StartActorLoop)
        instance.IsRunning = true;
        var task = Task.Run(async () => await StartActorLoop(instance, actorInterpreter, actorDef.Constructor, constructorArgs));
        _actorTasks[id] = task;
        
        return new ActorReference(instance, id);
    }
    
    private async Task StartActorLoop(ActorInstance instance, Interpreter interpreter, FunctionValue? constructor, List<RuntimeValue>? constructorArgs)
    {
        try
        {
            // Set current actor context in interpreter
            interpreter.SetCurrentActor(instance);
            
            // Initialize actor state (fields) - evaluate field initializers
            foreach (var field in instance.Actor.Fields.Values)
            {
                if (field.Type == MemberType.Field)
                {
                    if (field.Value is Expression initExpr)
                    {
                        var initValue = await interpreter.EvaluateAsync(initExpr);
                        instance.State.Define(field.Name, initValue);
                    }
                    else
                    {
                        // No initializer, default to null
                        instance.State.Define(field.Name, RuntimeValue.Null());
                    }
                }
            }
            
            // Execute constructor if exists
            if (constructor != null && constructorArgs != null)
            {
                try
                {
                    // Temporarily set current object to null (actors don't use 'this', they use 'self')
                    // Note: We can't directly access _currentObject, so we'll just call the constructor
                    // The interpreter will handle the context properly
                    await interpreter.CallFunctionAsync(constructor, constructorArgs, null);
                }
                catch (Exception ex)
                {
                    // Log constructor error but continue (actor can still receive messages)
                    Console.WriteLine($"Actor {instance.Id} constructor error: {ex.Message}");
                }
            }
            
            // Process messages
            while (instance.IsRunning)
            {
                try
                {
                    var message = await instance.Mailbox.ReceiveAsync(CancellationToken.None);
                    
                    // Route message to appropriate handler
                    await ProcessMessage(instance, interpreter, message);
                }
                catch (Exception ex)
                {
                    if (instance.IsRunning)
                    {
                        Console.WriteLine($"Actor {instance.Id} message processing error: {ex.GetType().Name}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"  Inner exception: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        }
                        Console.WriteLine($"  Stack trace: {ex.StackTrace}");
                        // Continue processing messages even if one fails
                    }
                    else
                    {
                        // Actor was stopped, exit loop
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Actor {instance.Id} loop error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            _actors.TryRemove(instance.Id, out _);
            _actorTasks.TryRemove(instance.Id, out _);
        }
    }
    
    private async Task ProcessMessage(ActorInstance instance, Interpreter interpreter, Message message)
    {
        // Ensure current actor is set before handling message (for 'self' to work)
        // This must be done before any callback or handler execution
        interpreter.SetCurrentActor(instance);
        interpreter.SetCurrentMessage(message);
        
        // Verify _currentActor is set (debug check)
        if (interpreter.GetCurrentActor() == null)
        {
            throw new RuntimeException("Failed to set current actor before calling handler");
        }
        
        // FIRST: Check if this is a reply message for a pending callback
        // This must happen BEFORE trying to find handlers, because reply messages
        // don't have handler names and would fail handler lookup
        if (message.CorrelationId.HasValue)
        {
            var handled = await interpreter.TryHandleCallbackAsync(message);
            if (handled)
            {
                // Callback consumed the message; no further handler dispatch
                interpreter.SetCurrentMessage(null);
                return;
            }
        }

        // Determine which handler to call (only for non-callback messages)
        string handlerName;
        
        if (!string.IsNullOrEmpty(message.HandlerName))
        {
            // Explicit handler name provided
            handlerName = message.HandlerName;
        }
        else
        {
            // Try to infer handler from message payload
            // If payload is a string, use it as handler name
            if (message.Payload.Type == ValueType.String)
            {
                handlerName = message.Payload.AsString();
            }
            else
            {
                // Default handler or use payload as-is
                handlerName = "handle"; // Default handler name
            }
        }
        
        // Find message handler
        var handler = instance.Actor.FindMessageHandler(handlerName);
        if (handler == null)
        {
            // Try default handler
            handler = instance.Actor.FindMessageHandler("handle");
            if (handler == null)
            {
                throw new RuntimeException($"No message handler found for '{handlerName}' in actor {instance.Actor.Name}");
            }
        }

        // Set sender in actor's environment for handler to access
        // We'll set it in the actor's state environment directly
        if (message.Sender != null)
        {
            instance.State.Define("__sender__", RuntimeValue.ActorReference(message.Sender));
        }
        
        // Call handler with message arguments
        // Map arguments positionally to handler parameters
        var args = new List<RuntimeValue>();
        if (message.Arguments != null)
        {
            args.AddRange(message.Arguments);
        }
        await interpreter.CallFunctionAsync(handler, args, null);
        interpreter.SetCurrentMessage(null);
    }
    
    public void StopActor(string actorId)
    {
        if (_actors.TryGetValue(actorId, out var instance))
        {
            instance.Stop();
        }
    }
    
    public ActorInstance? GetActor(string actorId)
    {
        _actors.TryGetValue(actorId, out var instance);
        return instance;
    }
    
    /// <summary>
    /// Resets the singleton instance for testing purposes.
    /// Stops all running actors and clears all state.
    /// </summary>
    public static void ResetForTesting()
    {
        if (_instance != null)
        {
            // Stop all actors
            var actorIds = _instance._actors.Keys.ToList();
            foreach (var id in actorIds)
            {
                _instance.StopActor(id);
            }
            
            // Wait for tasks to complete (with timeout)
            var tasks = _instance._actorTasks.Values.ToList();
            if (tasks.Count > 0)
            {
                Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
            }
            
            // Clear collections
            _instance._actors.Clear();
            _instance._actorTasks.Clear();
            _instance._actorCounter = 0;
        }
    }
    
    /// <summary>
    /// Clears the singleton instance reference for testing purposes.
    /// Should be called after ResetForTesting() to fully reset state.
    /// </summary>
    public static void ClearInstanceForTesting()
    {
        ResetForTesting();
        _instance = null;
    }
}
