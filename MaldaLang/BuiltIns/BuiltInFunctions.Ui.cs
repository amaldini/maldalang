// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Security.Cryptography;
using System.Globalization;
using MaldaLang;
using MaldaLang.Parser;
using MaldaLang.Runtime;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;
using MaldaLang.Runtime.UI;
using MaldaLang.Runtime.Tracing;
using MaldaLang.Runtime.Jobs;
using MaldaLang.Runtime.Workflows;
using MaldaLang.IDE;
using MaldaLang.Testing;
using ValueType = MaldaLang.Interpreter.ValueType;
using Spectre.Console;
using Spectre.Console.Rendering;
using Markdig;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using DocumentFormat.OpenXml.Packaging;

public static partial class BuiltInFunctions
{
    private static RuntimeValue BuiltInRedirect(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("redirect() expects location string and optional status integer");

        int? statusCode = null;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("redirect() status must be an integer when provided");
            statusCode = args[1].AsInteger();
        }

        return RuntimeValue.Object(WebRuntimeHelpers.CreateRedirectResponse(args[0].AsString(), statusCode));
    }

    private static RuntimeValue BuiltInRedirectTo(List<RuntimeValue> args)
    {
        if (args.Count != 1 || args[0].Type != ValueType.String)
            throw new Exception("RedirectTo() expects 1 string argument: (location)");

        return BuiltInRedirect(new List<RuntimeValue> { args[0] });
    }

    private static RuntimeValue BuiltInRenderTemplate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("renderTemplate() expects template string and optional object model");

        var template = args[0].AsString();
        if (args.Count == 1)
            return RuntimeValue.String(template);

        var modelObj = CoerceRuntimeObjectToJsonObject(args[1]);
        if (modelObj == null)
            return RuntimeValue.String(template);

        return RuntimeValue.String(RenderTemplateTokenString(template, modelObj));
    }

    private static RuntimeValue BuiltInComponentFragment(List<RuntimeValue> args)
    {
        if (args.Count != 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("componentFragment() expects targetId and html string");

        var targetId = args[0].AsString();
        var html = args[1].AsString();

        var response = new JsonObject();
        response.Set("status", RuntimeValue.Integer(200));

        var headers = new JsonObject();
        headers.Set("X-Malda-Fragment", RuntimeValue.String("true"));
        headers.Set("X-Malda-Fragment-Target", RuntimeValue.String(targetId));
        response.Set("headers", RuntimeValue.Object(headers));
        response.Set("body", RuntimeValue.String(html));

        return RuntimeValue.Object(response);
    }

    private static RuntimeValue BuiltInComponentLiveEmit(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("componentLiveEmit() expects channel, payload, optional eventType");

        var channel = args[0].AsString();
        var payload = args[1];
        var eventType = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "update";

        var msg = new JsonObject();
        msg.Set("type", RuntimeValue.String("componentLiveEvent"));
        msg.Set("event", RuntimeValue.String(eventType));
        msg.Set("channel", RuntimeValue.String(channel));
        msg.Set("payload", payload);

        var json = RuntimeValueToJson(RuntimeValue.Object(msg));
        HttpServerInstance.BroadcastSSEMessage(json, channel);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInComponentStateGet(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("componentStateGet() expects componentId, key, optional defaultValue, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var value = HttpServerInstance.GetComponentState(componentId, key);
        if (value.Type == ValueType.Null && args.Count >= 3)
            return args[2];
        return value;
    }

    private static RuntimeValue BuiltInComponentStateSet(List<RuntimeValue> args)
    {
        if ((args.Count != 3 && args.Count != 4) || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("componentStateSet() expects componentId, key, value, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var value = args[2];
        HttpServerInstance.SetComponentState(componentId, key, value);
        return value;
    }

    private static RuntimeValue BuiltInComponentStateObject(List<RuntimeValue> args)
    {
        if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String)
            throw new Exception("componentStateObject() expects componentId and optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        return HttpServerInstance.GetComponentStateObject(componentId);
    }

    private static RuntimeValue BuiltInComponentStateClear(List<RuntimeValue> args)
    {
        if (args.Count > 2)
            throw new Exception("componentStateClear() expects zero args (all), one componentId, or componentId + scope");

        if (args.Count == 0)
        {
            HttpServerInstance.ClearAllComponentState();
            return RuntimeValue.Null();
        }

        if (args[0].Type != ValueType.String)
            throw new Exception("componentStateClear(componentId) expects a string");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        HttpServerInstance.ClearComponentState(componentId);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInComponentStateConfigure(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.Integer || args[1].Type != ValueType.Integer || (args.Count == 3 && args[2].Type != ValueType.Integer))
            throw new Exception("componentStateConfigure() expects maxComponents, maxKeysPerComponent, optional ttlMs");

        var maxComponents = args[0].AsInteger();
        var maxKeysPerComponent = args[1].AsInteger();
        var ttlMs = args.Count == 3 ? args[2].AsInteger() : 1800000;
        HttpServerInstance.ConfigureComponentStatePolicy(maxComponents, maxKeysPerComponent, ttlMs);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInComponentStatePin(List<RuntimeValue> args)
    {
        if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String)
            throw new Exception("componentStatePin() expects componentId and optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        HttpServerInstance.PinComponentState(componentId);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInComponentStateUnpin(List<RuntimeValue> args)
    {
        if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String)
            throw new Exception("componentStateUnpin() expects componentId and optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        HttpServerInstance.UnpinComponentState(componentId);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiRow(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("row", args);
    private static RuntimeValue BuiltInUiColumn(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("column", args);
    private static RuntimeValue BuiltInUiStack(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("stack", args);
    private static RuntimeValue BuiltInUiSpacer(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("spacer", args);
    private static RuntimeValue BuiltInUiPanel(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("panel", args);
    private static RuntimeValue BuiltInUiText(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("text", args);
    private static RuntimeValue BuiltInUiHeading(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("heading", args);
    private static RuntimeValue BuiltInUiImage(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("image", args);
    private static RuntimeValue BuiltInUiIcon(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("icon", args);
    private static RuntimeValue BuiltInUiButton(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("button", args);
    private static RuntimeValue BuiltInUiTextField(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("textField", args);
    private static RuntimeValue BuiltInUiCheckbox(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("checkbox", args);
    private static RuntimeValue BuiltInUiSelect(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("select", args);
    private static RuntimeValue BuiltInUiSlider(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("slider", args);
    private static RuntimeValue BuiltInUiDatePicker(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("datePicker", args);
    private static RuntimeValue BuiltInUiList(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("list", args);
    private static RuntimeValue BuiltInUiTable(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("table", args);
    private static RuntimeValue BuiltInUiAlert(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("alert", args);
    private static RuntimeValue BuiltInUiProgress(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("progress", args);
    private static RuntimeValue BuiltInUiModal(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("modal", args);
    private static RuntimeValue BuiltInUiForm(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("form", args);
    private static RuntimeValue BuiltInUiField(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("field", args);
    private static RuntimeValue BuiltInUiTextArea(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("textArea", args);
    private static RuntimeValue BuiltInUiRadioGroup(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("radioGroup", args);
    private static RuntimeValue BuiltInUiSwitch(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("switch", args);
    private static RuntimeValue BuiltInUiTabs(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("tabs", args);
    private static RuntimeValue BuiltInUiAccordion(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("accordion", args);
    private static RuntimeValue BuiltInUiBreadcrumbs(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("breadcrumbs", args);
    private static RuntimeValue BuiltInUiDrawer(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("drawer", args);
    private static RuntimeValue BuiltInUiDataGrid(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("dataGrid", args);
    private static RuntimeValue BuiltInUiTreeView(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("treeView", args);
    private static RuntimeValue BuiltInUiPaginator(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("paginator", args);
    private static RuntimeValue BuiltInUiEmptyState(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("emptyState", args);
    private static RuntimeValue BuiltInUiBadge(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("badge", args);
    private static RuntimeValue BuiltInUiToast(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("toast", args);
    private static RuntimeValue BuiltInUiSkeleton(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("skeleton", args);
    private static RuntimeValue BuiltInUiSpinner(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("spinner", args);
    private static RuntimeValue BuiltInUiErrorBoundary(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("errorBoundary", args);
    private static RuntimeValue BuiltInUiSlot(List<RuntimeValue> args) => UiFrameworkInstance.BuildNode("slot", args);

    private static RuntimeValue BuiltInUiTemplate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiTemplate() expects source string, optional model object, optional options object");

        var model = args.Count >= 2 ? CoerceRuntimeObjectToJsonObject(args[1]) : null;
        var useCache = true;
        var compatRaw = false;
        if (args.Count == 3)
        {
            var options = CoerceRuntimeObjectToJsonObject(args[2]);
            if (options == null)
                throw new Exception("uiTemplate() options must be an object when provided");
            useCache = GetBooleanOptionFromObject(options, "cache", true);
            compatRaw = GetBooleanOptionFromObject(options, "compatRaw", false);
        }

        var template = ResolveUiTemplateSource(args[0].AsString(), useCache);
        return RuntimeValue.String(RenderUiTemplateString(template, model, compatRaw));
    }

    private static RuntimeValue BuiltInUiPartial(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("uiPartial() expects source string and optional model object");

        var model = args.Count == 2 ? CoerceRuntimeObjectToJsonObject(args[1]) : null;
        var template = ResolveUiTemplateSource(args[0].AsString(), true);
        return RuntimeValue.String(RenderUiTemplateString(template, model, false));
    }

    private static RuntimeValue BuiltInUiLayout(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiLayout() expects source string, slots object, optional model object");

        var slots = CoerceRuntimeObjectToJsonObject(args[1]);
        if (slots == null)
            throw new Exception("uiLayout() slots must be an object");

        var model = args.Count == 3 ? CoerceRuntimeObjectToJsonObject(args[2]) : null;
        var mergedModel = new JsonObject();
        if (model != null)
        {
            foreach (var kvp in model.GetProperties())
            {
                mergedModel.Set(kvp.Key, kvp.Value);
            }
        }
        var rawSlotKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kvp in slots.GetProperties())
        {
            var slotKey = "slot:" + kvp.Key;
            mergedModel.Set(slotKey, kvp.Value);
            rawSlotKeys.Add(slotKey);
        }

        var template = ResolveUiTemplateSource(args[0].AsString(), true);
        return RuntimeValue.String(RenderUiTemplateString(template, mergedModel, false, rawSlotKeys));
    }

    private static RuntimeValue BuiltInUiWithSlot(List<RuntimeValue> args)
    {
        if (args.Count != 3 || args[0].Type != ValueType.Object || args[1].Type != ValueType.String)
            throw new Exception("uiWithSlot() expects rootNode, slotName, slotContent");

        var root = args[0].AsObject() as JsonObject;
        if (root == null)
            throw new Exception("uiWithSlot() rootNode must be a UI node object");

        var slotName = args[1].AsString();
        var slotContent = args[2];
        var replaced = ReplaceSlot(root, slotName, slotContent);
        return RuntimeValue.Object(replaced);
    }

    private static RuntimeValue BuiltInUiWhen(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.Boolean)
            throw new Exception("uiWhen() expects condition, thenNode, optional elseNode");

        if (args[0].AsBoolean())
            return args[1];

        return args.Count == 3 ? args[2] : RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiChoose(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[1].Type != ValueType.Object)
            throw new Exception("uiChoose() expects key, casesObject, optional defaultNode");

        var key = args[0].ToString();
        var casesObject = args[1].AsObject() as JsonObject;
        if (casesObject == null)
            return args.Count == 3 ? args[2] : RuntimeValue.Null();

        var selected = casesObject.Get(key, null);
        if (selected.Type != ValueType.Null)
            return selected;

        return args.Count == 3 ? args[2] : RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiEach(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.Array)
            throw new Exception("uiEach() expects items array and optional propName (default: value)");

        var propName = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "value";
        var children = new List<RuntimeValue>();
        foreach (var item in args[0].AsArray())
        {
            var props = new JsonObject();
            props.Set(propName, item);
            var node = new JsonObject();
            node.Set("type", RuntimeValue.String("text"));
            node.Set("props", RuntimeValue.Object(props));
            node.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));
            children.Add(RuntimeValue.Object(node));
        }

        return RuntimeValue.Array(children);
    }

    private static JsonObject ReplaceSlot(JsonObject root, string slotName, RuntimeValue slotContent)
    {
        var type = root.Get("type", null);
        if (type.Type == ValueType.String && type.AsString() == "slot")
        {
            var propsValue = root.Get("props", null);
            if (propsValue.Type == ValueType.Object && propsValue.AsObject() is JsonObject slotProps)
            {
                var nameValue = slotProps.Get("name", null);
                if (nameValue.Type == ValueType.String && nameValue.AsString() == slotName)
                {
                    if (slotContent.Type == ValueType.Object && slotContent.AsObject() is JsonObject replacementNode)
                    {
                        return replacementNode;
                    }

                    var textNode = new JsonObject();
                    textNode.Set("type", RuntimeValue.String("text"));
                    var props = new JsonObject();
                    props.Set("value", RuntimeValue.String(slotContent.ToString()));
                    textNode.Set("props", RuntimeValue.Object(props));
                    textNode.Set("children", RuntimeValue.Array(new List<RuntimeValue>()));
                    return textNode;
                }
            }
        }

        var cloned = new JsonObject();
        foreach (var kvp in root.GetProperties())
        {
            if (kvp.Key != "children")
            {
                cloned.Set(kvp.Key, kvp.Value);
                continue;
            }

            if (kvp.Value.Type != ValueType.Array)
            {
                cloned.Set(kvp.Key, kvp.Value);
                continue;
            }

            var nextChildren = new List<RuntimeValue>();
            foreach (var child in kvp.Value.AsArray())
            {
                if (child.Type == ValueType.Object && child.AsObject() is JsonObject childObj)
                {
                    nextChildren.Add(RuntimeValue.Object(ReplaceSlot(childObj, slotName, slotContent)));
                }
                else
                {
                    nextChildren.Add(child);
                }
            }

            cloned.Set("children", RuntimeValue.Array(nextChildren));
        }

        return cloned;
    }

    private static RuntimeValue BuiltInUiMount(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiMount() expects rootNode and optional sessionId");

        var root = UiNode.FromRuntimeValue(args[0]);
        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        return session.Mount(root);
    }

    private static RuntimeValue BuiltInUiMountEnvelope(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3)
            throw new Exception("uiMountEnvelope() expects rootNode, optional sessionId, optional options");

        var root = UiNode.FromRuntimeValue(args[0]);
        var sessionId = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";

        JsonObject? options = null;
        if (args.Count == 3)
        {
            options = CoerceRuntimeObjectToJsonObject(args[2]);
            if (options == null)
                throw new Exception("uiMountEnvelope() options must be an object when provided");
        }

        var maxPatchCount = options == null ? 1024 : GetIntegerOptionFromObject(options, "maxPatchCount", 1024);
        var maxEventQueueDepth = options == null ? 1024 : GetIntegerOptionFromObject(options, "maxEventQueueDepth", 1024);
        var sessionTtlMs = options == null ? 1800000 : GetIntegerOptionFromObject(options, "sessionTtlMs", 1800000);

        UiSessionRegistry.ConfigureTtl(TimeSpan.FromMilliseconds(Math.Max(60000, sessionTtlMs)));
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        session.MaxPatchCountPerEnvelope = Math.Max(1, maxPatchCount);
        session.ConfigureQueueDepth(maxEventQueueDepth);

        var mount = session.Mount(root);
        var envelope = new JsonObject();
        envelope.Set("status", RuntimeValue.Integer(200));
        envelope.Set("sessionId", RuntimeValue.String(sessionId));
        envelope.Set("mount", mount);
        envelope.Set("snapshot", session.SnapshotAsRuntimeValue());
        envelope.Set("resync", session.BuildResyncEnvelope());
        return RuntimeValue.Object(envelope);
    }

    private static RuntimeValue BuiltInUiRender(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiRender() expects rootNode and optional sessionId");

        var nextTree = UiNode.FromRuntimeValue(args[0]);
        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        return session.Render(nextTree);
    }

    private static RuntimeValue BuiltInUiDispatchEvent(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3 || args[0].Type != ValueType.Object)
            throw new Exception("uiDispatchEvent() expects event object, optional sessionId, optional sequence");

        // Signature: ui.dispatchEvent(event, sessionId?, sequence?). sessionId is always
        // the second argument when it is a string — including the three-argument form.
        var sessionId = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        // Interpreted dict literals are JsonObject, transpiled ones DictionaryInstance.
        var evtObject = CoerceRuntimeObjectToJsonObject(args[0]);
        if (evtObject == null)
            throw new Exception("uiDispatchEvent() event must be a json object");

        var sequence = args.Count == 3 && args[2].Type == ValueType.Integer ? args[2].AsInteger() : 0;
        if (sequence > 0 && !session.TryAcceptInboundSequence(sequence, out var reason))
        {
            return session.BuildNack(sequence, "InvalidSequence", reason ?? "invalid sequence");
        }

        EnqueueUiEvent(session, evtObject);
        return sequence > 0 ? session.BuildAck(sequence) : RuntimeValue.Null();
    }

    /// <summary>
    /// Queues one event (<c>type</c>, <c>targetPath</c>, <c>payload</c>) for <c>ui.pullEvent</c>; an
    /// <c>error</c> event also fires the component's <c>onError</c> hook. Shared by
    /// <c>ui.dispatchEvent</c> and browser events arriving through <see cref="UiHostLink"/>.
    /// </summary>
    internal static void EnqueueUiEvent(UiSession session, JsonObject evtObject)
    {
        var eventType = evtObject.Get("type", null).Type == ValueType.String ? evtObject.Get("type", null).AsString() : "event";
        var targetPath = evtObject.Get("targetPath", null).Type == ValueType.String ? evtObject.Get("targetPath", null).AsString() : "/";
        var payload = evtObject.Get("payload", null);

        session.EnqueueEvent(new UiEvent(eventType, targetPath, payload.Type == ValueType.Null ? RuntimeValue.Null() : payload));
        if (string.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
        {
            var errorComponentId = ResolveUiErrorComponentId(evtObject, payload, targetPath);
            if (!string.IsNullOrWhiteSpace(errorComponentId))
            {
                session.EmitLifecycleHook("onError", errorComponentId, payload);
            }
        }
    }

    private static RuntimeValue BuiltInUiPullEvent(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("uiPullEvent() expects optional sessionId");

        var sessionId = args.Count == 1 && args[0].Type == ValueType.String ? args[0].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        if (session.TryDequeueEvent(out var uiEvent) && uiEvent != null)
        {
            return uiEvent.ToRuntimeValue();
        }

        return RuntimeValue.Null();
    }

    /// <summary>
    /// <c>ui.run(state, view, update, sessionId?)</c>: mounts <c>view(state)</c>, then for each queued
    /// event sets <c>state = update(state, event)</c> and renders <c>view(state)</c>. <c>update</c>
    /// returning <c>null</c> ends the loop. Without a linked host (offline programs, tests) the loop
    /// also ends once the queue is empty, because no event can arrive any more; with one it waits
    /// for browser events. Returns the last state.
    /// </summary>
    private static async Task<RuntimeValue> BuiltInUiRunAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        BuiltInArity.Require("uiRun", args, 3, 4, "state, view, update, sessionId?");
        if (args[1].Type != ValueType.Function || args[2].Type != ValueType.Function)
            throw new Exception("ui.run() expects view and update to be functions: ui.run(state, view, update, sessionId?)");

        var view = args[1].AsFunction();
        var update = args[2].AsFunction();
        var sessionId = args.Count == 4 && args[3].Type == ValueType.String ? args[3].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        var state = args[0];

        session.Mount(UiNode.FromRuntimeValue(await CallUiCallbackAsync(view, new List<RuntimeValue> { state }, interpreter)));
        while (true)
        {
            if (!session.TryDequeueEvent(out var uiEvent) || uiEvent == null)
            {
                if (!UiHostLink.IsAttached)
                    return state;
                await session.WaitForEventAsync(TimeSpan.FromSeconds(1));
                continue;
            }

            var next = await CallUiCallbackAsync(update, new List<RuntimeValue> { state, uiEvent.ToRuntimeValue() }, interpreter);
            if (next.Type == ValueType.Null)
                return state;

            state = next;
            session.Render(UiNode.FromRuntimeValue(await CallUiCallbackAsync(view, new List<RuntimeValue> { state }, interpreter)));
        }
    }

    /// <summary>Calls a <c>ui.run</c> callback: through the interpreter, or as a transpiled delegate when there is none.</summary>
    private static Task<RuntimeValue> CallUiCallbackAsync(FunctionValue callback, List<RuntimeValue> args, Interpreter? interpreter) =>
        interpreter != null
            ? interpreter.CallFunctionAsync(callback, args)
            : MaldaLang.Interpreter.Interpreter.InvokeTranspiledFunctionAsync(callback, args);

    private static RuntimeValue BuiltInUiState(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("uiState() expects componentId, key, defaultValue, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var existing = HttpServerInstance.GetComponentState(componentId, key);
        if (existing.Type != ValueType.Null)
        {
            return existing;
        }

        HttpServerInstance.SetComponentState(componentId, key, args[2]);
        return args[2];
    }

    private static RuntimeValue BuiltInUiGetState(List<RuntimeValue> args)
    {
        // Peek: default is return-only and is never persisted (unlike ui.state).
        if (args.Count < 2 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("uiGetState() expects componentId, key, optional defaultValue, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        var key = args[1].AsString();
        var value = HttpServerInstance.GetComponentState(componentId, key);
        if (value.Type == ValueType.Null && args.Count >= 3)
            return args[2];
        return value;
    }

    private static RuntimeValue BuiltInUiSetState(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args.Count > 4 || args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("uiSetState() expects componentId, key, value, optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 4 ? args[3] : RuntimeValue.Null());
        HttpServerInstance.SetComponentState(componentId, args[1].AsString(), args[2]);
        return args[2];
    }

    private static RuntimeValue BuiltInUiPinState(List<RuntimeValue> args)
    {
        if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String)
            throw new Exception("uiPinState() expects componentId and optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        HttpServerInstance.PinComponentState(componentId);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiUnpinState(List<RuntimeValue> args)
    {
        if ((args.Count != 1 && args.Count != 2) || args[0].Type != ValueType.String)
            throw new Exception("uiUnpinState() expects componentId and optional scope");

        var componentId = ComposeScopedComponentId(args[0].AsString(), args.Count == 2 ? args[1] : RuntimeValue.Null());
        HttpServerInstance.UnpinComponentState(componentId);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiInvalidate(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception("uiInvalidate() expects channel and optional payload");

        var channel = args[0].AsString();
        var payload = args.Count == 2 ? args[1] : RuntimeValue.Null();
        var message = new JsonObject();
        message.Set("type", RuntimeValue.String("invalidate"));
        message.Set("channel", RuntimeValue.String(channel));
        message.Set("payload", payload);
        HttpServerInstance.BroadcastSSEMessage(RuntimeValueToJson(RuntimeValue.Object(message)), channel);
        return RuntimeValue.Null();
    }

    private static RuntimeValue BuiltInUiSessionId(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiSessionId() expects source and optional defaultSessionId");

        var defaultSessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var sessionId = ResolveSessionId(args[0]);
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = defaultSessionId;
        return RuntimeValue.String(sessionId);
    }

    private static RuntimeValue BuiltInUiRedirectWithSession(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.String)
            throw new Exception("uiRedirectWithSession() expects path, source, optional defaultSessionId");

        var path = args[0].AsString();
        var defaultSessionId = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "default";
        var sessionId = ResolveSessionId(args[1]);
        if (string.IsNullOrWhiteSpace(sessionId))
            sessionId = defaultSessionId;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var separator = path.Contains("?", StringComparison.Ordinal) ? "&" : "?";
            path = path + separator + "session=" + sessionId;
        }

        return BuiltInRedirectTo(new List<RuntimeValue> { RuntimeValue.String(path) });
    }

    private static RuntimeValue BuiltInUiOnInit(List<RuntimeValue> args) => RegisterUiHook(args, "onInit");
    private static RuntimeValue BuiltInUiOnPreRender(List<RuntimeValue> args) => RegisterUiHook(args, "onPreRender");
    private static RuntimeValue BuiltInUiOnLoad(List<RuntimeValue> args) => RegisterUiHook(args, "onLoad");
    private static RuntimeValue BuiltInUiOnDispose(List<RuntimeValue> args) => RegisterUiHook(args, "onDispose");
    private static RuntimeValue BuiltInUiOnMount(List<RuntimeValue> args) => RegisterUiHook(args, "onMount");
    private static RuntimeValue BuiltInUiOnUpdate(List<RuntimeValue> args) => RegisterUiHook(args, "onUpdate");
    private static RuntimeValue BuiltInUiOnUnmount(List<RuntimeValue> args) => RegisterUiHook(args, "onUnmount");
    private static RuntimeValue BuiltInUiOnError(List<RuntimeValue> args) => RegisterUiHook(args, "onError");

    private static RuntimeValue RegisterUiHook(List<RuntimeValue> args, string hookName)
    {
        if (args.Count < 1 || args.Count > 2 || args[0].Type != ValueType.String)
            throw new Exception($"{hookName}() expects componentId and optional sessionId");

        var componentId = args[0].AsString();
        var sessionId = args.Count == 2 && args[1].Type == ValueType.String ? args[1].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        session.RegisterLifecycleHook(hookName, componentId);
        return RuntimeValue.Null();
    }

    private static string? ResolveUiErrorComponentId(JsonObject eventObject, RuntimeValue payload, string targetPath)
    {
        if (TryGetStringProperty(eventObject, "componentId", out var eventComponentId))
        {
            return eventComponentId;
        }

        if (payload.Type == ValueType.Object &&
            payload.AsObject() is JsonObject payloadObject &&
            TryGetStringProperty(payloadObject, "componentId", out var payloadComponentId))
        {
            return payloadComponentId;
        }

        if (string.IsNullOrWhiteSpace(targetPath) || targetPath == "/")
        {
            return null;
        }

        var trimmedPath = targetPath.Trim('/');
        if (string.IsNullOrWhiteSpace(trimmedPath))
        {
            return null;
        }

        var separatorIndex = trimmedPath.LastIndexOf('/');
        return separatorIndex >= 0 ? trimmedPath[(separatorIndex + 1)..] : trimmedPath;
    }

    private static bool TryGetStringProperty(JsonObject obj, string propertyName, out string value)
    {
        value = string.Empty;
        var property = obj.Get(propertyName, null);
        if (property.Type != ValueType.String)
        {
            return false;
        }

        value = property.AsString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static RuntimeValue BuiltInUiConfigure(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args[0].Type != ValueType.String || args[1].Type != ValueType.Integer)
            throw new Exception("uiConfigure() expects settingName and integer value");

        var setting = args[0].AsString();
        var value = args[1].AsInteger();

        if (setting == "sessionTtlMs")
        {
            UiSessionRegistry.ConfigureTtl(TimeSpan.FromMilliseconds(Math.Max(60000, value)));
            return RuntimeValue.Null();
        }

        var sessionId = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "default";
        var session = UiSessionRegistry.GetOrCreate(sessionId);
        switch (setting)
        {
            case "maxPatchCount":
                session.MaxPatchCountPerEnvelope = Math.Max(1, value);
                return RuntimeValue.Null();
            case "maxPayloadBytes":
                session.MaxPayloadSizeBytes = Math.Max(1024, value);
                return RuntimeValue.Null();
            case "maxEventQueueDepth":
                session.ConfigureQueueDepth(value);
                return RuntimeValue.Null();
            default:
                throw new Exception($"Unknown uiConfigure setting: {setting}");
        }
    }

    private static RuntimeValue BuiltInUiSnapshot(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("uiSnapshot() expects optional sessionId");

        var sessionId = args.Count == 1 && args[0].Type == ValueType.String ? args[0].AsString() : "default";
        return UiSessionRegistry.GetOrCreate(sessionId).SnapshotAsRuntimeValue();
    }

    private static RuntimeValue BuiltInUiResync(List<RuntimeValue> args)
    {
        if (args.Count > 1)
            throw new Exception("uiResync() expects optional sessionId");

        var sessionId = args.Count == 1 && args[0].Type == ValueType.String ? args[0].AsString() : "default";
        return UiSessionRegistry.GetOrCreate(sessionId).BuildResyncEnvelope();
    }

    private static string ResolveUiTemplateSource(string sourceOrTemplate, bool useCache)
    {
        if (string.IsNullOrEmpty(sourceOrTemplate))
        {
            return string.Empty;
        }

        try
        {
            var fullPath = Path.IsPathRooted(sourceOrTemplate)
                ? Path.GetFullPath(sourceOrTemplate)
                : Path.GetFullPath(Path.Combine(System.Environment.CurrentDirectory, sourceOrTemplate));
            if (!File.Exists(fullPath))
            {
                return sourceOrTemplate;
            }

            if (!useCache)
            {
                return File.ReadAllText(fullPath);
            }

            lock (UiTemplateCacheLock)
            {
                if (UiTemplateCache.TryGetValue(fullPath, out var cached))
                {
                    return cached;
                }

                var content = File.ReadAllText(fullPath);
                UiTemplateCache[fullPath] = content;
                return content;
            }
        }
        catch
        {
            return sourceOrTemplate;
        }
    }

    private static JsonObject? CoerceRuntimeObjectToJsonObject(RuntimeValue value)
    {
        if (value.Type != ValueType.Object)
        {
            return null;
        }

        var obj = value.AsObject();
        if (obj is JsonObject jsonObject)
        {
            return jsonObject;
        }

        if (obj is DictionaryInstance dict)
        {
            var mapped = new JsonObject();
            foreach (var kvp in dict.GetEntries())
            {
                mapped.Set(kvp.Key, kvp.Value);
            }

            return mapped;
        }

        return null;
    }

    private static bool GetBooleanOptionFromObject(JsonObject options, string key, bool defaultValue)
    {
        var optionValue = options.Get(key, null);
        if (optionValue == null || optionValue.Type == ValueType.Null)
        {
            return defaultValue;
        }

        return optionValue.Type switch
        {
            ValueType.Boolean => optionValue.AsBoolean(),
            ValueType.Integer => optionValue.AsInteger() != 0,
            ValueType.String when bool.TryParse(optionValue.AsString(), out var parsed) => parsed,
            ValueType.String when int.TryParse(optionValue.AsString(), out var parsedInt) => parsedInt != 0,
            _ => defaultValue
        };
    }

    private static string GetStringOptionFromObject(JsonObject options, string key, string defaultValue)
    {
        var optionValue = options.Get(key, null);
        if (optionValue == null || optionValue.Type == ValueType.Null)
        {
            return defaultValue;
        }

        var text = optionValue.Type == ValueType.String ? optionValue.AsString() : optionValue.ToString();
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    private static int GetIntegerOptionFromObject(JsonObject options, string key, int defaultValue)
    {
        var optionValue = options.Get(key, null);
        if (optionValue == null || optionValue.Type == ValueType.Null)
        {
            return defaultValue;
        }

        return optionValue.Type switch
        {
            ValueType.Integer => optionValue.AsInteger(),
            ValueType.Boolean => optionValue.AsBoolean() ? 1 : 0,
            ValueType.String when int.TryParse(optionValue.AsString(), out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static string RuntimeValueToTemplateString(RuntimeValue value)
    {
        return value.Type == ValueType.String ? value.AsString() : RuntimeValueToJson(value);
    }

    private static string RenderTemplateTokenString(string template, JsonObject? modelObj)
    {
        if (modelObj == null)
        {
            return template;
        }

        var rendered = template;
        foreach (var kvp in modelObj.GetProperties())
        {
            var token = "{{" + kvp.Key + "}}";
            rendered = rendered.Replace(token, RuntimeValueToTemplateString(kvp.Value), StringComparison.Ordinal);
        }

        return rendered;
    }

    private static string RenderUiTemplateString(string template, JsonObject? modelObj, bool compatRaw, HashSet<string>? rawKeys = null)
    {
        return UiTemplateEngine.Render(template, modelObj, new UiTemplateRenderOptions
        {
            EscapeByDefault = true,
            CompatRaw = compatRaw,
            RawKeys = rawKeys,
            StringifyValue = RuntimeValueToTemplateString
        });
    }

    private static string ComposeScopedComponentId(string componentId, RuntimeValue scopeValue)
    {
        if (scopeValue.Type != ValueType.String)
        {
            return componentId;
        }

        var scope = scopeValue.AsString();
        if (string.IsNullOrWhiteSpace(scope))
        {
            return componentId;
        }

        return scope + "::" + componentId;
    }
}
