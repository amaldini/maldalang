// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;
using System.Threading;
using MaldaLang.BuiltIns;
using MaldaLang.BuiltIns.LLMClientBridge.BackendAdapters;
using System.Reflection;
using System.IO;
using System;
using System.Globalization;
using System.Text.Json;
using System.Linq;
using MaldaLang.PackageManager;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.Workflows;
using MaldaLang.Interpreter.Debug;

public partial class Interpreter
{
    private async Task DefineActorAsync(ActorDeclaration decl)
    {
        var actorDef = new ActorDefinition(decl.Name) { IsWorker = decl.IsWorker };
        
        // Process members (fields, constructors, methods/handlers)
        foreach (var member in decl.Members)
        {
            if (member.Type == MemberType.Field)
            {
                actorDef.Fields[member.Name] = member;
                if (member.IsStatic && member.Value is Expression initExpr)
                {
                    actorDef.StaticFields[member.Name] = await EvaluateAsync(initExpr);
                }
            }
            else if (member.Type == MemberType.Method)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                var handler = new FunctionValue(funcDecl, _globals, false, decl.Name);
                handler.Decorators = funcDecl.Decorators;
                handler.ParameterDecorators = funcDecl.ParameterDecorators;
                if (member.IsStatic)
                {
                    actorDef.StaticMethods[member.Name] = handler;
                }
                else
                {
                    // Message handler
                    actorDef.MessageHandlers[member.Name] = handler;
                }
            }
            else if (member.Type == MemberType.Constructor)
            {
                var funcDecl = (FunctionDeclaration)member.Value!;
                actorDef.Constructor = new FunctionValue(funcDecl, _globals, false, decl.Name);
            }
        }

        // Process actor message declarations (actor sugar)
        foreach (var message in decl.Messages)
        {
            if (actorDef.Messages.ContainsKey(message.Name))
            {
                throw new RuntimeException($"Duplicate message declaration '{message.Name}' in actor '{decl.Name}'.");
            }
            actorDef.Messages[message.Name] = message;
        }
        
        _actors[decl.Name] = actorDef;
    }
    
    private async Task<RuntimeValue> SpawnActorAsync(SpawnExpression expr)
    {
        var actorName = expr.ActorName;
        if (!_actors.ContainsKey(actorName))
        {
            throw new RuntimeException($"Actor '{actorName}' not defined.");
        }
        
        var actorDef = _actors[actorName];
        
        // Evaluate constructor arguments
        var constructorArgs = new List<RuntimeValue>();
        foreach (var argExpr in expr.Arguments)
        {
            constructorArgs.Add(await EvaluateAsync(argExpr));
        }

        if (actorDef.IsWorker)
            constructorArgs = WorkerBoundary.Cross(constructorArgs, actorName);
        
        // Spawn actor via ActorRuntime
        var actorRef = ActorRuntime.Instance.SpawnActor(actorDef, this, constructorArgs);
        
        return RuntimeValue.ActorReference(actorRef);
    }
    
    private async Task<RuntimeValue?> ExecuteSendAsync(SendStatement stmt)
    {
        await SendMessageAsync(stmt);
        return null;
    }
    
    private async Task SendMessageAsync(SendStatement stmt)
    {
        var targetValue = await EvaluateAsync(stmt.Target);
        if (targetValue.Type != ValueType.ActorReference)
        {
            throw new RuntimeException("Can only send messages to actor references.");
        }
        
        var targetRef = targetValue.AsActorReference();
        
        // Get self reference if we're in an actor context
        ActorReference? sender = GetSelfReference();

        // External actor stop is modeled as a built-in method on ActorReference in
        // transpiled mode; match that behavior in the interpreter instead of routing
        // it through actor message dispatch.
        if (stmt.HandlerName == "stop" && stmt.Arguments.Count == 0 && stmt.Callback == null)
        {
            targetRef.Stop();
            return;
        }

        // Determine if this send should use actor message-sugar semantics.
        // If the target actor has a declared message with this name, we treat
        // the send as constructing a tagged message value that can be handled
        // via `receive()` + pattern matching inside the actor.
        bool isMessageBasedSend = false;
        RuntimeValue payloadForMessage = RuntimeValue.Null();
        List<RuntimeValue>? argumentsForMessage = null;
        string? handlerNameForMessage = stmt.HandlerName;

        var argsForVariant = new List<RuntimeValue>();

        // Call-style send: send target.handlerName(args...) [then (result) { ... }]
        var argValues = new List<RuntimeValue>();
        foreach (var argExpr in stmt.Arguments)
        {
            argValues.Add(await EvaluateAsync(argExpr));
        }

        if (targetRef.Instance.Home != _currentActor?.Home)
            argValues = WorkerBoundary.Cross(argValues, (targetRef.Instance.Home ?? _currentActor!.Home!).Actor.Name);

        if (stmt.HandlerName != null)
        {
            var actorDef = targetRef.Instance.Actor;
            if (actorDef.Messages.TryGetValue(stmt.HandlerName, out _))
            {
                // Actor declares this message name: build a variant payload
                // with the same tag and the evaluated arguments as payload.
                isMessageBasedSend = true;
                payloadForMessage = RuntimeValue.Variant(stmt.HandlerName, argValues);
                argumentsForMessage = null; // payload carries all data for receive()
                handlerNameForMessage = null; // avoid routing through handler-based dispatch
            }
        }

        if (!isMessageBasedSend)
        {
            // Preserve existing semantics: payload is unused (null), arguments are separate.
            payloadForMessage = RuntimeValue.Null();
            argumentsForMessage = argValues;
            handlerNameForMessage = stmt.HandlerName;
        }

        Guid? correlationId = null;

        // If there is a callback, register it and use the message's Id as the correlation key
        if (stmt.Callback != null)
        {
            // We need to create the message explicitly to know its Id
            var msg = new Message(
                payloadForMessage,
                sender,
                handlerNameForMessage,
                null,
                argumentsForMessage);

            var callbackEnv = new Environment(_environment);
            
            // Evaluate timeout milliseconds if provided
            int? timeoutMs = null;
            if (stmt.TimeoutMilliseconds != null)
            {
                var timeoutValue = await EvaluateAsync(stmt.TimeoutMilliseconds);
                if (timeoutValue.Type == ValueType.Integer)
                {
                    timeoutMs = timeoutValue.AsInteger();
                }
                else if (timeoutValue.Type == ValueType.Float)
                {
                    timeoutMs = (int)timeoutValue.AsFloat();
                }
                else
                {
                    throw new RuntimeException("Timeout must be an integer (milliseconds).");
                }
            }
            
            CancellationTokenSource? timeoutCts = null;
            if (timeoutMs.HasValue && timeoutMs.Value > 0)
            {
                timeoutCts = new CancellationTokenSource();
                
                // Start timeout task
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(timeoutMs.Value, timeoutCts.Token);
                        
                        // Timeout occurred - try to remove callback and invoke error handler
                        if (_pendingCallbacks.TryGetValue(msg.Id, out var callbackInfo) && _pendingCallbacks.Remove(msg.Id))
                        {
                            timeoutCts.Dispose();
                            
                            if (callbackInfo.TimeoutErrorHandler != null && callbackInfo.TargetRef != null)
                            {
                                var senderInstance = sender?.Instance;
                                if (senderInstance != null)
                                {
                                    var errorMessage = RuntimeValue.String(
                                        $"Request to {callbackInfo.TargetRef}.{callbackInfo.HandlerName} timed out after {timeoutMs.Value}ms");
                                    
                                    // Run the timeout error handler in the sender's own loop, after
                                    // the message it is handling: running it from this task would
                                    // share the sender's interpreter with that message.
                                    senderInstance.Mailbox.Send(new Message(RuntimeValue.Null())
                                    {
                                        Action = async () =>
                                        {
                                            var errorHandlerEnv = new Environment(callbackInfo.Environment);
                                            errorHandlerEnv.Define(
                                                callbackInfo.TimeoutErrorHandler.ParameterName,
                                                errorMessage);
                                            await ExecuteBlockAsync(callbackInfo.TimeoutErrorHandler.Body, errorHandlerEnv);
                                        }
                                    });
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout was cancelled (reply arrived), ignore
                        timeoutCts?.Dispose();
                    }
                });
            }
            
            _pendingCallbacks[msg.Id] = new CallbackInfo(
                stmt.Callback.ParameterName, 
                stmt.Callback.Body, 
                callbackEnv,
                timeoutMs,
                stmt.TimeoutErrorHandler,
                targetRef,
                stmt.HandlerName,
                timeoutCts);

            // Send message directly to target actor's mailbox using its instance
            targetRef.Instance.Mailbox.Send(msg);
        }
        else
        {
            // Fire-and-forget call-style send without callback
            targetRef.Send(payloadForMessage, sender, handlerNameForMessage, correlationId: null, arguments: argumentsForMessage);
        }
    }
    
    private RuntimeValue EvaluateSelf()
    {
        var selfRef = GetSelfReference();
        if (selfRef == null)
        {
            throw new RuntimeException("'self' can only be used within an actor message handler.");
        }
        return RuntimeValue.ActorReference(selfRef);
    }
    
    private async Task<RuntimeValue> ReceiveMessageAsync()
    {
        if (_currentActor == null)
        {
            throw new RuntimeException("receive() can only be called from within an actor message handler.");
        }

        if (_currentMessage != null &&
            !_currentMessage.ReceiveConsumed &&
            _currentMessage.Arguments != null &&
            _currentMessage.Arguments.Count > 0)
        {
            _currentMessage.ReceiveConsumed = true;
            return _currentMessage.Arguments[0];
        }

        var message = await _currentActor.Mailbox.ReceiveAsync(CancellationToken.None);
        // When using actor-sugar with receive() loops, the message consumed here
        // becomes the current message for built-ins like reply().
        SetCurrentMessage(message);

        // Prefer the first argument (if any) as the received value, falling back
        // to the payload. This mirrors callback handling semantics and makes
        // `send target(value)` compatible with `receive()`.
        if (message.Arguments != null && message.Arguments.Count > 0)
        {
            return message.Arguments[0];
        }

        return message.Payload;
    }
}
