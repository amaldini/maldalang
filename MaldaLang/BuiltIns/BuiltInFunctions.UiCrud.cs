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
    private static RuntimeValue BuiltInUiRenderList(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3 || args[0].Type != ValueType.Array || args[1].Type != ValueType.String)
            throw new Exception("uiRenderList() expects items array, source string, optional itemName");

        var itemName = args.Count == 3 && args[2].Type == ValueType.String ? args[2].AsString() : "item";
        if (string.IsNullOrWhiteSpace(itemName))
            itemName = "item";

        var template = ResolveUiTemplateSource(args[1].AsString(), true);
        var items = args[0].AsArray();
        var parts = new List<string>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var model = new JsonObject();
            model.Set("index", RuntimeValue.Integer(i));
            model.Set(itemName, item);
            var itemObject = CoerceRuntimeObjectToJsonObject(item);
            if (itemObject != null)
            {
                foreach (var kvp in itemObject.GetProperties())
                {
                    model.Set(kvp.Key, kvp.Value);
                }
            }

            parts.Add(RenderUiTemplateString(template, model, false));
        }

        return RuntimeValue.String(string.Join(string.Empty, parts));
    }

    private static RuntimeValue BuiltInUiCrudModel(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 4)
            throw new Exception("uiCrudModel() expects schema object, optional sessionId, optional queryParams object, optional lookups object");
        if (args[0].Type != ValueType.Object)
            throw new Exception("uiCrudModel() schema must be an object");

        var schema = CoerceRuntimeObjectToJsonObject(args[0]);
        if (schema == null)
            throw new Exception("uiCrudModel() schema must be an object");

        var sessionIdArg = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var queryParams = args.Count >= 3 ? CoerceRuntimeObjectToJsonObject(args[2]) : null;
        var lookups = args.Count == 4 ? CoerceRuntimeObjectToJsonObject(args[3]) : null;
        var model = BuildUiCrudModelFromSchema(schema, sessionIdArg, queryParams, lookups, null);
        return RuntimeValue.Object(model);
    }

    private static RuntimeValue BuiltInUiCrudControls(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 5)
            throw new Exception("uiCrudControls() expects schema object, optional sessionId, optional queryParams object, optional lookups object, optional options object");
        if (args[0].Type != ValueType.Object)
            throw new Exception("uiCrudControls() schema must be an object");

        var schema = CoerceRuntimeObjectToJsonObject(args[0]);
        if (schema == null)
            throw new Exception("uiCrudControls() schema must be an object");

        var sessionIdArg = args.Count >= 2 && args[1].Type == ValueType.String ? args[1].AsString() : null;
        var queryParams = args.Count >= 3 ? CoerceRuntimeObjectToJsonObject(args[2]) : null;
        var lookups = args.Count >= 4 ? CoerceRuntimeObjectToJsonObject(args[3]) : null;
        var options = args.Count == 5 ? CoerceRuntimeObjectToJsonObject(args[4]) : null;
        if (args.Count == 5 && options == null)
            throw new Exception("uiCrudControls() options must be an object when provided");

        var model = BuildUiCrudModelFromSchema(schema, sessionIdArg, queryParams, lookups, options);

        var useCache = options == null ? true : GetBooleanOptionFromObject(options, "cache", true);
        var compatRaw = options != null && GetBooleanOptionFromObject(options, "compatRaw", false);
        var templatePath = ResolveCrudControlsTemplatePath(schema, options);
        var template = ResolveUiTemplateSource(templatePath, useCache);
        return RuntimeValue.String(RenderUiTemplateString(template, model, compatRaw));
    }

    private static RuntimeValue BuiltInUiCrudSchema(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("uiCrudSchema() expects schema object and optional defaults object");
        if (args[0].Type != ValueType.Object)
            throw new Exception("uiCrudSchema() schema must be an object");

        var schema = CoerceRuntimeObjectToJsonObject(args[0]);
        if (schema == null)
            throw new Exception("uiCrudSchema() schema must be an object");

        JsonObject? defaults = null;
        if (args.Count == 2)
        {
            defaults = CoerceRuntimeObjectToJsonObject(args[1]);
            if (defaults == null)
                throw new Exception("uiCrudSchema() defaults must be an object when provided");
        }

        var normalized = new JsonObject();
        foreach (var kvp in schema.GetProperties())
        {
            normalized.Set(kvp.Key, kvp.Value);
        }

        var templateBasePath = GetStringValueFromObject(
            normalized,
            "templateBasePath",
            defaults == null
                ? "templates"
                : GetStringValueFromObject(defaults, "templateBasePath", "templates"));
        normalized.Set("templateBasePath", RuntimeValue.String(templateBasePath));

        var controlsDefault = defaults == null
            ? Path.Combine(templateBasePath, "entity_controls.html")
            : GetStringValueFromObject(defaults, "controlsTemplatePath", Path.Combine(templateBasePath, "entity_controls.html"));
        var controlsTemplatePath = GetStringValueFromObject(normalized, "controlsTemplatePath", controlsDefault);
        normalized.Set("controlsTemplatePath", RuntimeValue.String(controlsTemplatePath));

        var filterDefs = normalized.Get("filterDefs", null);
        if (filterDefs.Type != ValueType.Array)
        {
            normalized.Set("filterDefs", RuntimeValue.Array(new List<RuntimeValue>()));
        }

        var dialogLookupOptions = normalized.Get("dialogLookupOptions", null);
        if (dialogLookupOptions.Type != ValueType.Array)
        {
            normalized.Set("dialogLookupOptions", RuntimeValue.Array(new List<RuntimeValue>()));
        }

        var singular = GetStringValueFromObject(normalized, "entitySingularLower", "item");
        var addLabelDefault = defaults == null
            ? "Add " + singular
            : GetStringValueFromObject(defaults, "openAddLabel", "Add " + singular);
        var editLabelDefault = defaults == null
            ? "Edit selected " + singular
            : GetStringValueFromObject(defaults, "openEditLabel", "Edit selected " + singular);

        normalized.Set("openAddLabel", RuntimeValue.String(GetStringValueFromObject(normalized, "openAddLabel", addLabelDefault)));
        normalized.Set("openEditLabel", RuntimeValue.String(GetStringValueFromObject(normalized, "openEditLabel", editLabelDefault)));
        return RuntimeValue.Object(normalized);
    }

    private static JsonObject BuildUiCrudModelFromSchema(
        JsonObject schema,
        string? sessionIdArg,
        JsonObject? queryParams,
        JsonObject? lookups,
        JsonObject? options)
    {
        var sessionId = string.IsNullOrWhiteSpace(sessionIdArg)
            ? GetStringValueFromObject(schema, "sessionDefault", "default")
            : sessionIdArg!;
        var effectiveQuery = queryParams ?? new JsonObject();
        var effectiveLookups = lookups ?? new JsonObject();
        var templateBasePath = ResolveCrudTemplateBasePath(schema, options);

        var model = new JsonObject();
        model.Set("sessionId", RuntimeValue.String(sessionId));
        model.Set("entityPluralLower", RuntimeValue.String(GetStringValueFromObject(schema, "entityPluralLower", string.Empty)));
        model.Set("entitySingularLower", RuntimeValue.String(GetStringValueFromObject(schema, "entitySingularLower", string.Empty)));
        model.Set("listAction", RuntimeValue.String(GetStringValueFromObject(schema, "listAction", string.Empty)));
        model.Set("filterGridColumns", RuntimeValue.String(GetStringValueFromObject(schema, "filterGridColumns", string.Empty)));
        model.Set("openAddButtonId", RuntimeValue.String(GetStringValueFromObject(schema, "openAddButtonId", string.Empty)));
        model.Set("openEditButtonId", RuntimeValue.String(GetStringValueFromObject(schema, "openEditButtonId", string.Empty)));
        model.Set("openAddLabel", RuntimeValue.String(GetStringValueFromObject(schema, "openAddLabel", string.Empty)));
        model.Set("openEditLabel", RuntimeValue.String(GetStringValueFromObject(schema, "openEditLabel", string.Empty)));
        model.Set("filters", RuntimeValue.Array(BuildCrudFilters(schema, effectiveQuery)));

        var dialogModel = new JsonObject();
        dialogModel.Set("sessionId", RuntimeValue.String(sessionId));
        var lookupBindings = BuildCrudLookupBindings(schema, effectiveLookups, templateBasePath);
        foreach (var kvp in lookupBindings.GetProperties())
        {
            dialogModel.Set(kvp.Key, kvp.Value);
        }

        model.Set("addDialogHtml", RuntimeValue.String(RenderCrudTemplate(schema, "addDialogTemplate", dialogModel, templateBasePath)));
        model.Set("editDialogHtml", RuntimeValue.String(RenderCrudTemplate(schema, "editDialogTemplate", dialogModel, templateBasePath)));
        model.Set("dialogScript", RuntimeValue.String(RenderCrudTemplate(schema, "dialogScriptTemplate", null, templateBasePath)));
        return model;
    }

    private static List<RuntimeValue> BuildCrudFilters(JsonObject schema, JsonObject queryParams)
    {
        var filters = new List<RuntimeValue>();
        var defsValue = schema.Get("filterDefs", null);
        if (defsValue.Type != ValueType.Array)
        {
            return filters;
        }

        foreach (var defValue in defsValue.AsArray())
        {
            var def = CoerceRuntimeObjectToJsonObject(defValue);
            if (def == null)
            {
                continue;
            }

            var kind = GetStringValueFromObject(def, "kind", string.Empty);
            var name = GetStringValueFromObject(def, "name", string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var defaultValue = GetStringValueFromObject(def, "defaultValue", string.Empty);
            var currentValue = GetQueryParamString(queryParams, name, defaultValue);

            if (string.Equals(kind, "input", StringComparison.Ordinal))
            {
                var inputFilter = new JsonObject();
                inputFilter.Set("isInput", RuntimeValue.Boolean(true));
                inputFilter.Set("isSelect", RuntimeValue.Boolean(false));
                inputFilter.Set("name", RuntimeValue.String(name));
                inputFilter.Set("placeholder", RuntimeValue.String(GetStringValueFromObject(def, "placeholder", string.Empty)));
                inputFilter.Set("value", RuntimeValue.String(currentValue));
                filters.Add(RuntimeValue.Object(inputFilter));
                continue;
            }

            var options = new List<RuntimeValue>();
            var rawOptions = def.Get("options", null);
            if (rawOptions.Type == ValueType.Array)
            {
                foreach (var optionValue in rawOptions.AsArray())
                {
                    var optionObj = CoerceRuntimeObjectToJsonObject(optionValue);
                    if (optionObj == null)
                    {
                        continue;
                    }

                    var optionKey = GetStringValueFromObject(optionObj, "value", string.Empty);
                    var optionModel = new JsonObject();
                    optionModel.Set("value", RuntimeValue.String(optionKey));
                    optionModel.Set("label", RuntimeValue.String(GetStringValueFromObject(optionObj, "label", string.Empty)));
                    optionModel.Set("selectedAttr", RuntimeValue.String(string.Equals(currentValue, optionKey, StringComparison.Ordinal) ? " selected" : string.Empty));
                    options.Add(RuntimeValue.Object(optionModel));
                }
            }

            var selectFilter = new JsonObject();
            selectFilter.Set("isInput", RuntimeValue.Boolean(false));
            selectFilter.Set("isSelect", RuntimeValue.Boolean(true));
            selectFilter.Set("name", RuntimeValue.String(name));
            selectFilter.Set("options", RuntimeValue.Array(options));
            filters.Add(RuntimeValue.Object(selectFilter));
        }

        return filters;
    }

    private static JsonObject BuildCrudLookupBindings(JsonObject schema, JsonObject lookups, string templateBasePath)
    {
        var rendered = new JsonObject();
        var bindingsValue = schema.Get("dialogLookupOptions", null);
        if (bindingsValue.Type != ValueType.Array)
        {
            return rendered;
        }

        foreach (var bindingValue in bindingsValue.AsArray())
        {
            var binding = CoerceRuntimeObjectToJsonObject(bindingValue);
            if (binding == null)
            {
                continue;
            }

            var key = GetStringValueFromObject(binding, "key", string.Empty);
            var source = GetStringValueFromObject(binding, "source", string.Empty);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var rowsValue = lookups.Get(source, null);
            var rows = rowsValue.Type == ValueType.Array ? rowsValue.AsArray() : new List<RuntimeValue>();

            var renderer = GetStringValueFromObject(binding, "renderer", string.Empty);
            var itemName = GetStringValueFromObject(binding, "itemName", "item");
            var templatePath = GetStringValueFromObject(binding, "templatePath", string.Empty);
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                if (string.Equals(renderer, "customerOptions", StringComparison.Ordinal))
                {
                    templatePath = "customer_option.html";
                    if (string.IsNullOrWhiteSpace(itemName) || string.Equals(itemName, "item", StringComparison.Ordinal))
                    {
                        itemName = "customer";
                    }
                }
                else if (string.Equals(renderer, "agentOptions", StringComparison.Ordinal))
                {
                    templatePath = "agent_option.html";
                    if (string.IsNullOrWhiteSpace(itemName) || string.Equals(itemName, "item", StringComparison.Ordinal))
                    {
                        itemName = "agent";
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(templatePath))
            {
                continue;
            }

            var resolvedTemplatePath = ResolveCrudTemplatePath(templatePath, templateBasePath);
            var renderedValue = BuiltInUiRenderList(new List<RuntimeValue>
            {
                RuntimeValue.Array(rows),
                RuntimeValue.String(resolvedTemplatePath),
                RuntimeValue.String(string.IsNullOrWhiteSpace(itemName) ? "item" : itemName)
            });
            rendered.Set(key, renderedValue.Type == ValueType.String ? renderedValue : RuntimeValue.String(string.Empty));
        }

        return rendered;
    }

    private static string RenderCrudTemplate(JsonObject schema, string schemaKey, JsonObject? model, string templateBasePath)
    {
        var templateName = GetStringValueFromObject(schema, schemaKey, string.Empty);
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return string.Empty;
        }

        var templatePath = ResolveCrudTemplatePath(templateName, templateBasePath);
        var template = ResolveUiTemplateSource(templatePath, true);
        return RenderUiTemplateString(template, model, false);
    }

    private static string ResolveCrudControlsTemplatePath(JsonObject schema, JsonObject? options)
    {
        if (options != null)
        {
            var optionsTemplatePath = GetStringValueFromObject(options, "templatePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(optionsTemplatePath))
            {
                return optionsTemplatePath;
            }
        }

        var schemaTemplatePath = GetStringValueFromObject(schema, "controlsTemplatePath", string.Empty);
        if (!string.IsNullOrWhiteSpace(schemaTemplatePath))
        {
            return schemaTemplatePath;
        }

        schemaTemplatePath = GetStringValueFromObject(schema, "controlsTemplate", string.Empty);
        if (!string.IsNullOrWhiteSpace(schemaTemplatePath))
        {
            return schemaTemplatePath;
        }

        return Path.Combine("templates", "entity_controls.html");
    }

    private static string ResolveCrudTemplateBasePath(JsonObject schema, JsonObject? options)
    {
        if (options != null)
        {
            var optionsBasePath = GetStringValueFromObject(options, "templateBasePath", string.Empty);
            if (!string.IsNullOrWhiteSpace(optionsBasePath))
            {
                return optionsBasePath;
            }
        }

        var schemaBasePath = GetStringValueFromObject(schema, "templateBasePath", string.Empty);
        if (!string.IsNullOrWhiteSpace(schemaBasePath))
        {
            return schemaBasePath;
        }

        return "templates";
    }

    private static string ResolveCrudTemplatePath(string templatePathOrName, string templateBasePath)
    {
        if (string.IsNullOrWhiteSpace(templatePathOrName))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(templatePathOrName))
        {
            return templatePathOrName;
        }

        if (string.IsNullOrWhiteSpace(templateBasePath))
        {
            return templatePathOrName;
        }

        return Path.Combine(templateBasePath, templatePathOrName);
    }

    private static string GetQueryParamString(JsonObject queryParams, string key, string defaultValue)
    {
        var value = queryParams.Get(key, null);
        if (value.Type == ValueType.Null)
        {
            return defaultValue;
        }

        var text = value.Type == ValueType.String ? value.AsString() : RuntimeValueToTemplateString(value);
        return string.IsNullOrEmpty(text) ? defaultValue : text;
    }

    private static string GetStringValueFromObject(JsonObject obj, string key, string defaultValue)
    {
        var value = obj.Get(key, null);
        if (value.Type == ValueType.Null)
        {
            return defaultValue;
        }

        if (value.Type == ValueType.String)
        {
            var text = value.AsString();
            return string.IsNullOrEmpty(text) ? defaultValue : text;
        }

        return RuntimeValueToTemplateString(value);
    }
}
