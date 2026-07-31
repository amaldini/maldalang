// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Declarations;

public partial class Interpreter
{
    private async Task<RuntimeValue> EvaluateMemberAccessViaResolverAsync(MemberAccessExpression expr)
    {
        var obj = await EvaluateAsync(expr.Object);

        if (expr.IsNullConditional && obj.Type == ValueType.Null)
            return RuntimeValue.Null();

        if (obj.Type == ValueType.Array)
        {
            var arrayInstance = obj.AsArrayInstance();
            return arrayInstance.Get(expr.Member, _currentClass);
        }

        if (obj.Type == ValueType.ActorReference)
        {
            var actorRef = obj.AsActorReference();
            if (expr.Member == "stop")
            {
                var wrapper = new FunctionValue(null, null, false, null);
                wrapper.BuiltInInstance = new ActorReferenceWrapper(actorRef);
                wrapper.BuiltInMethod = "stop";
                return RuntimeValue.Function(wrapper);
            }
            throw new RuntimeException($"ActorReference has no member '{expr.Member}'. Available methods: stop()");
        }

        if (obj.Type == ValueType.Object)
        {
            var instance = obj.AsObject();

            if (instance is BuiltIns.JsonObject jsonObj)
            {
                try
                {
                    return jsonObj.Get(expr.Member, null);
                }
                catch
                {
                    return RuntimeValue.Null();
                }
            }

            if (instance is BuiltIns.LLMClientInstance llmClient)
            {
                return HandleBuiltInMemberAccess(llmClient, expr.Member);
            }
            else if (instance is BuiltIns.ConversationInstance conv)
            {
                return HandleBuiltInMemberAccess(conv, expr.Member);
            }
            else if (instance is BuiltIns.ToolInstance tool)
            {
                return HandleBuiltInMemberAccess(tool, expr.Member);
            }
            else if (instance is BuiltIns.AgentInstance agent)
            {
                return HandleBuiltInMemberAccess(agent, expr.Member);
            }
            else if (instance is BuiltIns.DotNetObjectInstance dotNetObj)
            {
                try
                {
                    return dotNetObj.GetProperty(expr.Member);
                }
                catch
                {
                    return HandleBuiltInMemberAccess(dotNetObj, expr.Member);
                }
            }
            else if (instance is BuiltIns.DotNetTypeInstance dotNetType)
            {
                return HandleBuiltInMemberAccess(dotNetType, expr.Member);
            }

            var value = instance.Get(expr.Member, _currentClass);

            if (value.Type == ValueType.Function)
            {
                var method = value.AsFunction();
                if (!method.IsConstructor && method.Declaration != null && instance.Class != null && instance.Class.MethodAccess.ContainsKey(expr.Member))
                {
                    var access = instance.Class.MethodAccess[expr.Member];
                    if (access == AccessModifier.Private && _currentClass != instance.Class)
                        throw new RuntimeException($"Cannot access private method '{expr.Member}' from outside {instance.Class.Name}.");
                }
            }

            return value;
        }
        else if (obj.Type == ValueType.Class)
        {
            var klass = obj.AsClass();
            if (klass.StaticFields.ContainsKey(expr.Member))
                return klass.StaticFields[expr.Member];
            if (klass.StaticMethods.ContainsKey(expr.Member))
            {
                if (klass.StaticMethodAccess.ContainsKey(expr.Member))
                {
                    var access = klass.StaticMethodAccess[expr.Member];
                    if (access == AccessModifier.Private && _currentClass != klass)
                        throw new RuntimeException($"Cannot access private static method '{expr.Member}' from outside {klass.Name}.");
                }
                return RuntimeValue.Function(klass.StaticMethods[expr.Member]);
            }
            throw new RuntimeException($"Class {klass.Name} has no static member '{expr.Member}'.");
        }
        else if (obj.Type == ValueType.String)
        {
            if (!IsStringExtensionMethod(expr.Member))
                throw new RuntimeException($"String has no member '{expr.Member}'. Available: length, upper, lower, trim, substring, indexOf, replace, split, startsWith, endsWith, padStart, padEnd, repeat.", expr.Line, _currentFile);
            var wrapper = new FunctionValue(null, null, false, null);
            wrapper.BoundReceiver = obj;
            wrapper.BoundBuiltInName = expr.Member;
            return RuntimeValue.Function(wrapper);
        }
        else
        {
            throw new RuntimeException("Only objects and classes have members.", expr.Line, _currentFile);
        }
    }
}
