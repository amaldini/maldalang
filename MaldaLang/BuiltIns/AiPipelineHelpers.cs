// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using MaldaLang.Interpreter;
using MaldaLang.Runtime;
using MaldaLangValueType = MaldaLang.Interpreter.ValueType;

public static class AiPipelineHelpers
{
    /// <summary>
    /// Optional stream handler set by <see cref="RunPromptAsync"/> for onToken/onReasoning callbacks.
    /// Chained by Conversation during LLM streaming.
    /// </summary>
    internal static Action<LlmStreamDelta>? PromptRunStreamHandler { get; set; }

    public static string? TryExtractResponseContent(RuntimeValue response)
    {
        if (response.Type == MaldaLangValueType.Object && response.AsObject() is JsonObject jsonObj)
        {
            var contentValue = jsonObj.Get("content");
            if (contentValue.Type == MaldaLangValueType.String)
                return contentValue.AsString();
        }

        if (response.Type == MaldaLangValueType.String)
            return response.AsString();

        return null;
    }

    public static async Task<RuntimeValue> CoerceAwaitResultAsync(RuntimeValue value, Interpreter? interpreter)
    {
        if (value.Type == MaldaLangValueType.Task)
            return await value.AsTask().ConfigureAwait(false);

        if (value.Type == MaldaLangValueType.Object && value.AsObject() is PromptInstance)
            return await RunPromptAsync(new List<RuntimeValue> { value }, interpreter);

        return value;
    }

    public static async Task<RuntimeValue> RunPromptAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 1)
            throw new RuntimeException("runPrompt() expects at least 1 argument (promptInstance or text).");

        var promptValue = args[0];
        PromptInstance promptInstance;

        if (promptValue.Type == MaldaLangValueType.Object && promptValue.AsObject() is PromptInstance inst)
        {
            promptInstance = inst;
        }
        else if (promptValue.Type == MaldaLangValueType.String)
        {
            promptInstance = new PromptInstance(null, promptValue.AsString());
        }
        else
        {
            throw new RuntimeException("runPrompt() expects a PromptInstance or string.");
        }

        var runOptions = ParseRunPromptOptions(args, interpreter);
        var agent = ResolveAgent(args, interpreter, runOptions.Client);

        EnsureStreamCallbackAvailable(runOptions.OnToken, interpreter, "onToken");
        EnsureStreamCallbackAvailable(runOptions.OnReasoning, interpreter, "onReasoning");

        if (runOptions.OnToken != null || runOptions.OnReasoning != null)
        {
            PromptRunStreamHandler = BuildPromptRunStreamHandler(
                interpreter,
                runOptions.OnToken,
                runOptions.OnReasoning);
        }

        try
        {
            var response = agent.Think(RuntimeValue.Object(promptInstance));
            var content = TryExtractResponseContent(response);
            if (content != null)
                return RuntimeValue.String(content);

            return response;
        }
        finally
        {
            PromptRunStreamHandler = null;
        }
    }

    private static void EnsureStreamCallbackAvailable(FunctionValue? callback, Interpreter? interpreter, string optionName)
    {
        if (callback != null && interpreter == null && callback.TranspiledDelegate == null)
            throw new RuntimeException($"runPrompt() {optionName} callback requires an active interpreter context.");
    }

    private static Action<LlmStreamDelta> BuildPromptRunStreamHandler(
        Interpreter? interpreter,
        FunctionValue? onToken,
        FunctionValue? onReasoning)
    {
        return delta =>
        {
            if (string.IsNullOrEmpty(delta.Text))
                return;

            FunctionValue? callback = delta.Kind switch
            {
                "content" => onToken,
                "reasoning" => onReasoning,
                _ => null
            };

            if (callback == null)
                return;

            InvokeStreamCallback(interpreter, callback, delta.Text, delta.Kind);
        };
    }

    private static void InvokeStreamCallback(
        Interpreter? interpreter,
        FunctionValue callback,
        string text,
        string kind)
    {
        var optionName = kind == "reasoning" ? "onReasoning" : "onToken";
        var transpiledDelegate = callback.TranspiledDelegate;

        try
        {
            if (transpiledDelegate != null)
            {
                transpiledDelegate(text).ConfigureAwait(false).GetAwaiter().GetResult();
                return;
            }

            interpreter!.CallFunctionAsync(callback, new List<RuntimeValue> { RuntimeValue.String(text) })
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        }
        catch (RuntimeException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new RuntimeException($"runPrompt() {optionName} callback failed: {ex.Message}");
        }
    }

    public static RuntimeValue ParseJsonTyped(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 2)
            throw new RuntimeException("parseJson() expects 2 arguments (value, schemaRef) and optional options object. For a plain JSON reader use parseJSON(text).");

        var valueArg = args[0];
        var schemaRef = ResolveSchemaRef(args[1]);
        var maxAttempts = ResolveRetryCount(args, defaultAttempts: 1);

        string? content = ExtractParseInput(valueArg);
        if (content == null)
            throw new RuntimeException("parseJson() value must be a string or an LLM response object with content.");

        string? lastError = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (!TypedPromptValidator.TryExtractJsonCandidate(content, out var jsonCandidate, out var extractError))
            {
                lastError = extractError;
                continue;
            }

            if (!TypedPromptValidator.TryParseJson(jsonCandidate, out var parsed, out var parseError))
            {
                lastError = parseError;
                continue;
            }

            if (!TypedPromptValidator.TryValidateReturnType(parsed, schemaRef, interpreter, out var validated, out var validationError))
            {
                lastError = validationError;
                continue;
            }

            return validated;
        }

        throw new RuntimeException(
            $"parseJson() validation failed after {maxAttempts} attempt(s) for schema '{schemaRef}'. " +
            $"Last error: {lastError ?? "Unknown error."}");
    }

    private sealed class RunPromptOptions
    {
        public LLMClientInstance? Client { get; init; }
        public FunctionValue? OnToken { get; init; }
        public FunctionValue? OnReasoning { get; init; }
    }

    private static RunPromptOptions ParseRunPromptOptions(List<RuntimeValue> args, Interpreter? interpreter)
    {
        LLMClientInstance? client = null;
        FunctionValue? onToken = null;
        FunctionValue? onReasoning = null;

        for (int i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Type == MaldaLangValueType.Object && arg.AsObject() is LLMClientInstance explicitClient)
            {
                client = explicitClient;
                continue;
            }

            if (TryGetRunPromptOptionsObject(arg, out var options))
            {
                if (options.TryGetValue("onToken", out var onTokenValue) &&
                    onTokenValue.Type == MaldaLangValueType.Function)
                {
                    onToken = onTokenValue.AsFunction();
                }

                if (options.TryGetValue("onReasoning", out var onReasoningValue) &&
                    onReasoningValue.Type == MaldaLangValueType.Function)
                {
                    onReasoning = onReasoningValue.AsFunction();
                }
            }
        }

        return new RunPromptOptions { Client = client, OnToken = onToken, OnReasoning = onReasoning };
    }

    private static bool TryGetRunPromptOptionsObject(RuntimeValue value, out Dictionary<string, RuntimeValue> options)
    {
        options = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        if (value.Type != MaldaLangValueType.Object)
            return false;

        var obj = value.AsObject();
        if (obj is DictionaryInstance dict)
        {
            foreach (var key in dict.GetKeys())
            {
                if (dict.TryGetEntry(key, out var entryValue))
                    options[key] = entryValue;
            }
            return options.Count > 0;
        }

        if (obj is JsonObject jsonObj)
        {
            foreach (var kvp in jsonObj.GetProperties())
                options[kvp.Key] = kvp.Value;
            return options.Count > 0;
        }

        return false;
    }

    private static AgentInstance ResolveAgent(List<RuntimeValue> args, Interpreter? interpreter, LLMClientInstance? explicitClient = null)
    {
        if (interpreter?._defaultAgent != null)
            return interpreter._defaultAgent;

        if (interpreter == null)
        {
            var shared = TranspiledBuiltinRuntime.GetOrCreateInterpreter();
            if (shared._defaultAgent != null)
                return shared._defaultAgent;
        }

        LLMClientInstance? client = explicitClient;
        if (client == null)
        {
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i].Type == MaldaLangValueType.Object && args[i].AsObject() is LLMClientInstance foundClient)
                {
                    client = foundClient;
                    break;
                }
            }
        }

        var agent = new AgentInstance();
        if (client != null)
            agent.Initialize("PromptAgent", "AI Assistant", "You are a helpful AI assistant.", client, null);
        else
            agent.Initialize("PromptAgent", "AI Assistant", "You are a helpful AI assistant.", null, DefaultLocalLlm.GetDefaultLocalClient(), null, null);

        return agent;
    }

    private static string ResolveSchemaRef(RuntimeValue schemaArg)
    {
        if (schemaArg.Type == MaldaLangValueType.String)
            return schemaArg.AsString();

        return schemaArg.ToString();
    }

    private static int ResolveRetryCount(List<RuntimeValue> args, int defaultAttempts)
    {
        if (args.Count < 3 || args[2].Type != MaldaLangValueType.Object)
            return defaultAttempts;

        var options = args[2].AsObject();
        if (options is DictionaryInstance dict && dict.TryGetEntry("retries", out var retriesValue))
        {
            if (retriesValue.Type == MaldaLangValueType.Integer)
                return Math.Max(1, retriesValue.AsInteger());
            if (retriesValue.Type == MaldaLangValueType.Float)
                return Math.Max(1, (int)retriesValue.AsFloat());
        }

        return defaultAttempts;
    }

    private static string? ExtractParseInput(RuntimeValue valueArg)
    {
        var content = TryExtractResponseContent(valueArg);
        if (content != null)
            return content;

        if (valueArg.Type == MaldaLangValueType.String)
            return valueArg.AsString();

        return null;
    }

    public static RuntimeValue LoadDocuments(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != MaldaLangValueType.String)
            throw new RuntimeException("loadDocuments() expects (pattern, dirPath?).");

        var pattern = args[0].AsString();
        var dirPath = args.Count > 1 && args[1].Type == MaldaLangValueType.String ? args[1].AsString() : ".";
        var searchRoot = Path.GetFullPath(dirPath);

        var globResult = BuiltInFunctions.CallBuiltIn(
            "glob",
            new List<RuntimeValue> { RuntimeValue.String(pattern), RuntimeValue.String(dirPath) },
            null);

        if (globResult.Type != MaldaLangValueType.Object || globResult.AsObject() is not JsonObject globObj)
            return RuntimeValue.Array(new List<RuntimeValue>());

        var itemsValue = globObj.Get("items");
        if (itemsValue.Type != MaldaLangValueType.Array)
            return RuntimeValue.Array(new List<RuntimeValue>());

        var documents = new List<RuntimeValue>();
        foreach (var item in itemsValue.AsArray())
        {
            if (item.Type != MaldaLangValueType.Object || item.AsObject() is not JsonObject itemObj)
                continue;

            var pathValue = itemObj.Get("path");
            var typeValue = itemObj.Get("type");
            if (pathValue.Type != MaldaLangValueType.String || typeValue.Type != MaldaLangValueType.String)
                continue;

            if (typeValue.AsString() != "file")
                continue;

            var path = pathValue.AsString();
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(
                    searchRoot,
                    path.Replace('/', Path.DirectorySeparatorChar)));
            }
            else
            {
                path = Path.GetFullPath(path);
            }

            var text = BuiltInFunctions.CallBuiltIn(
                "readFile",
                new List<RuntimeValue> { RuntimeValue.String(path) },
                null);

            if (text.Type != MaldaLangValueType.String)
                continue;

            var source = Path.GetRelativePath(searchRoot, path).Replace('\\', '/');
            var metadata = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal)
            {
                ["source"] = RuntimeValue.String(source)
            };
            documents.Add(RuntimeValue.Object(new DocumentInstance(text.AsString(), metadata)));
        }

        return RuntimeValue.Array(documents);
    }

    public static RuntimeValue SplitDocuments(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args[0].Type != MaldaLangValueType.Array)
            throw new RuntimeException("splitDocuments() expects (documents, chunkSize?, overlap?).");

        var chunkSize = 512;
        var overlap = 50;

        if (args.Count > 1)
        {
            if (args[1].Type == MaldaLangValueType.Integer)
                chunkSize = args[1].AsInteger();
            else if (args[1].Type == MaldaLangValueType.Float)
                chunkSize = (int)args[1].AsFloat();
        }

        if (args.Count > 2)
        {
            if (args[2].Type == MaldaLangValueType.Integer)
                overlap = args[2].AsInteger();
            else if (args[2].Type == MaldaLangValueType.Float)
                overlap = (int)args[2].AsFloat();
        }

        if (chunkSize <= 0)
            throw new RuntimeException("splitDocuments() chunkSize must be greater than 0.");
        if (overlap < 0 || overlap >= chunkSize)
            throw new RuntimeException("splitDocuments() overlap must be >= 0 and less than chunkSize.");

        var output = new List<RuntimeValue>();
        var chunkIndex = 0;

        foreach (var docValue in args[0].AsArray())
        {
            if (!TryGetDocumentContent(docValue, out var content, out var metadata))
                continue;

            if (string.IsNullOrEmpty(content))
                continue;

            var start = 0;
            while (start < content.Length)
            {
                var length = Math.Min(chunkSize, content.Length - start);
                var piece = content.Substring(start, length);
                var chunkMetadata = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
                foreach (var entry in metadata)
                    chunkMetadata[entry.Key] = entry.Value;

                chunkMetadata["chunk"] = RuntimeValue.Integer(chunkIndex);
                chunkIndex = chunkIndex + 1;
                output.Add(RuntimeValue.Object(new DocumentInstance(piece, chunkMetadata)));
                if (start + length >= content.Length)
                    break;
                start = start + chunkSize - overlap;
            }
        }

        return RuntimeValue.Array(output);
    }

    public static RuntimeValue FormatRetrievedDocs(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new RuntimeException("formatRetrievedDocs() expects a document array.");

        var values = args[0].Type == MaldaLangValueType.Array
            ? args[0].AsArray()
            : new List<RuntimeValue> { args[0] };

        var parts = new List<string>();
        foreach (var value in values)
        {
            if (value.Type == MaldaLangValueType.Object && value.AsObject() is DocumentInstance doc)
            {
                var source = doc.GetMetadataString("source") ?? "unknown";
                parts.Add("[source: " + source + "]\n" + doc.Content);
                continue;
            }

            if (TryGetDocumentContent(value, out var content, out var metadata))
            {
                var source = metadata.TryGetValue("source", out var sourceValue) && sourceValue.Type == MaldaLangValueType.String
                    ? sourceValue.AsString()
                    : "unknown";
                parts.Add("[source: " + source + "]\n" + content);
                continue;
            }

            if (value.Type == MaldaLangValueType.Object && value.AsObject() is JsonObject hit)
            {
                var data = hit.Get("data");
                if (data.Type == MaldaLangValueType.String)
                {
                    parts.Add(data.AsString());
                    continue;
                }
            }

            if (value.Type == MaldaLangValueType.String)
                parts.Add(value.AsString());
        }

        return RuntimeValue.String(string.Join("\n\n", parts));
    }

    public static RuntimeValue WithExamples(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new RuntimeException("withExamples() expects (prompt, examples, options?).");

        if (args[0].Type != MaldaLangValueType.Object || args[0].AsObject() is not PromptInstance prompt)
            throw new RuntimeException("withExamples() first argument must be a PromptInstance.");

        if (args[1].Type != MaldaLangValueType.Array)
            throw new RuntimeException("withExamples() second argument must be an array of { input, output } objects.");

        var merge = ResolveWithExamplesMerge(args);
        var runtimeExamples = args[1].AsArray();
        IReadOnlyList<PromptExample>? examples;

        if (runtimeExamples.Count == 0)
        {
            examples = null;
        }
        else
        {
            var parsed = PromptExampleHelpers.ParseExamplesOrNull(args[1]);
            if (parsed == null || parsed.Count == 0)
                throw new RuntimeException("withExamples() examples must contain valid { input, output } entries.");
            examples = parsed;
        }

        if (merge && examples != null && prompt.Examples != null && prompt.Examples.Count > 0)
        {
            var merged = new List<PromptExample>(prompt.Examples);
            merged.AddRange(examples);
            examples = merged;
        }

        return RuntimeValue.Object(CopyPromptInstance(prompt, examples));
    }

    public static RuntimeValue IndexInto(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 2)
            throw new RuntimeException("indexInto() expects (vectorDb, documents).");

        if (args[0].Type != MaldaLangValueType.Object || args[0].AsObject() is not VectorDBInstance vdb)
            throw new RuntimeException("indexInto() first argument must be a VectorDB instance.");

        if (args[1].Type != MaldaLangValueType.Array)
            throw new RuntimeException("indexInto() second argument must be a document array.");

        foreach (var docValue in args[1].AsArray())
        {
            if (!TryGetDocumentContent(docValue, out var content, out var metadata))
                continue;

            var stored = metadata.Count > 0
                ? RuntimeValue.Object(new DocumentInstance(content, metadata))
                : RuntimeValue.String(content);

            vdb.CallMethod("add", new List<RuntimeValue> { stored }, interpreter);
        }

        return RuntimeValue.Integer(args[1].AsArray().Count);
    }

    private static PromptInstance CopyPromptInstance(PromptInstance prompt, IReadOnlyList<PromptExample>? examples)
    {
        return new PromptInstance(
            prompt.System,
            prompt.User,
            prompt.Model,
            prompt.Temperature,
            prompt.Tools,
            prompt.MaxTokens,
            prompt.ResponseFormatSchema,
            examples,
            prompt.WithinTimeoutMs,
            prompt.Gather,
            prompt.Budget,
            prompt.ReturnType,
            prompt.Attachments);
    }

    private static bool ResolveWithExamplesMerge(List<RuntimeValue> args)
    {
        if (args.Count < 3 || args[2].Type != MaldaLangValueType.Object)
            return false;

        var options = args[2].AsObject();
        if (options is DictionaryInstance dict && dict.TryGetEntry("merge", out var mergeValue))
        {
            if (mergeValue.Type == MaldaLangValueType.Boolean)
                return mergeValue.AsBoolean();
            if (mergeValue.Type == MaldaLangValueType.Integer)
                return mergeValue.AsInteger() != 0;
        }

        if (options is JsonObject jsonObj)
        {
            var jsonMergeValue = jsonObj.Get("merge");
            if (jsonMergeValue.Type == MaldaLangValueType.Boolean)
                return jsonMergeValue.AsBoolean();
            if (jsonMergeValue.Type == MaldaLangValueType.Integer)
                return jsonMergeValue.AsInteger() != 0;
        }

        return false;
    }

    private static bool TryGetDocumentContent(
        RuntimeValue docValue,
        out string content,
        out Dictionary<string, RuntimeValue> metadata)
    {
        content = "";
        metadata = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);

        if (docValue.Type == MaldaLangValueType.Object && docValue.AsObject() is DocumentInstance doc)
        {
            content = doc.Content;
            foreach (var entry in doc.MetadataEntries)
                metadata[entry.Key] = entry.Value;
            return true;
        }

        if (docValue.Type != MaldaLangValueType.Object)
            return false;

        var obj = docValue.AsObject();
        if (obj is DictionaryInstance dict)
        {
            if (!dict.TryGetEntry("content", out var contentValue) || contentValue.Type != MaldaLangValueType.String)
                return false;
            content = contentValue.AsString();
            if (dict.TryGetEntry("metadata", out var metadataValue) && metadataValue.Type == MaldaLangValueType.Object &&
                metadataValue.AsObject() is DictionaryInstance metadataDict)
            {
                foreach (var key in metadataDict.GetKeys())
                {
                    if (metadataDict.TryGetEntry(key, out var entryValue))
                        metadata[key] = entryValue;
                }
            }
            return true;
        }

        if (obj is JsonObject jsonObj)
        {
            var contentValue = jsonObj.Get("content");
            if (contentValue.Type != MaldaLangValueType.String)
                return false;
            content = contentValue.AsString();
            var metadataValue = jsonObj.Get("metadata");
            CopyMetadata(metadataValue, metadata);
            return true;
        }

        return false;
    }

    private static void CopyMetadata(RuntimeValue metadataValue, Dictionary<string, RuntimeValue> metadata)
    {
        if (metadataValue.Type != MaldaLangValueType.Object)
            return;

        if (metadataValue.AsObject() is DictionaryInstance metadataDict)
        {
            foreach (var key in metadataDict.GetKeys())
            {
                if (metadataDict.TryGetEntry(key, out var entryValue))
                    metadata[key] = entryValue;
            }
            return;
        }

        if (metadataValue.AsObject() is JsonObject metadataJson)
        {
            foreach (var kvp in metadataJson.GetProperties())
                metadata[kvp.Key] = kvp.Value;
        }
    }

    public static RuntimeValue ComposePipe(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 2)
            throw new RuntimeException("composePipe() expects at least 2 callable steps.");

        ValidatePipelineCallables(args, "composePipe()");
        var instance = new ComposedPipeInstance(args, interpreter);
        var wrapper = new FunctionValue(null, null, false, null)
        {
            BuiltInInstance = instance,
            BuiltInMethod = "call"
        };
        return RuntimeValue.Function(wrapper);
    }

    public static async Task<RuntimeValue> ParallelRunAsync(List<RuntimeValue> args, Interpreter? interpreter)
    {
        if (args.Count < 2)
            throw new RuntimeException("parallelRun() expects (input, branches).");

        var input = args[0];
        var branches = ExtractNamedBranches(args[1]);

        if (branches.Count == 0)
            throw new RuntimeException("parallelRun() branches object must contain at least one callable entry.");

        var keys = branches.Keys.ToList();
        var tasks = new List<Task<RuntimeValue>>(keys.Count);
        foreach (var key in keys)
        {
            tasks.Add(InvokePipelineCallableAsync(branches[key], new List<RuntimeValue> { input }, interpreter));
        }

        RuntimeValue[] results;
        try
        {
            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not RuntimeException)
        {
            throw new RuntimeException(ex.Message);
        }

        var output = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
            output[keys[i]] = await CoerceAwaitResultAsync(results[i], interpreter);

        return RuntimeValue.Object(new DictionaryInstance(output));
    }

    public static RuntimeValue MergeRetrievedDocs(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new RuntimeException("mergeRetrievedDocs() expects one or more document arrays.");

        var docArrays = NormalizeDocumentArrayArgs(args);
        if (docArrays.Count == 0)
            return RuntimeValue.Array(new List<RuntimeValue>());

        var merged = new List<RuntimeValue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var docArray in docArrays)
        {
            foreach (var doc in docArray)
            {
                var key = GetDocumentDedupeKey(doc);
                if (!seen.Add(key))
                    continue;
                merged.Add(doc);
            }
        }

        return RuntimeValue.Array(merged);
    }

    public static async Task<RuntimeValue> InvokePipelineCallableAsync(
        RuntimeValue callee,
        List<RuntimeValue> args,
        Interpreter? interpreter)
    {
        if (callee.Type == MaldaLangValueType.Prompt)
            return await callee.AsPrompt().Call(args, interpreter);

        if (callee.Type != MaldaLangValueType.Function)
            throw new RuntimeException("Pipeline step must be a callable function or prompt.");

        var fn = callee.AsFunction();

        if (fn.BuiltInInstance != null && fn.BuiltInMethod != null)
            return InvokeBuiltInInstanceMethod(fn.BuiltInInstance, fn.BuiltInMethod, args, interpreter);

        if (fn.BoundReceiver != null && fn.BoundBuiltInName != null)
        {
            var boundArgs = new List<RuntimeValue> { fn.BoundReceiver! };
            boundArgs.AddRange(args);
            return await BuiltInFunctions.CallBuiltInAsync(fn.BoundBuiltInName, boundArgs, interpreter);
        }

        if (fn.Declaration != null && BuiltInRegistry.IsInterpreterBuiltIn(fn.Declaration.Name))
            return await BuiltInFunctions.CallBuiltInAsync(fn.Declaration.Name, args, interpreter);

        if (fn.TranspiledDelegate != null)
            return await InvokeTranspiledDelegateAsync(fn.TranspiledDelegate, args);

        if (interpreter != null)
            return await interpreter.InvokeComposedPipelineFunctionAsync(fn, args);

        throw new RuntimeException("Pipeline step requires an active interpreter or transpiled delegate.");
    }

    private static void ValidatePipelineCallables(List<RuntimeValue> callables, string context)
    {
        foreach (var callable in callables)
        {
            if (callable.Type == MaldaLangValueType.Prompt)
                continue;
            if (callable.Type == MaldaLangValueType.Function)
                continue;
            throw new RuntimeException($"{context} steps must be callables (functions, lambdas, prompts, or built-ins).");
        }
    }

    private static Dictionary<string, RuntimeValue> ExtractNamedBranches(RuntimeValue branchesArg)
    {
        var branches = new Dictionary<string, RuntimeValue>(StringComparer.Ordinal);

        if (branchesArg.Type != MaldaLangValueType.Object)
            throw new RuntimeException("parallelRun() second argument must be an object/map of named callables.");

        if (branchesArg.AsObject() is DictionaryInstance dict)
        {
            foreach (var key in dict.GetKeys())
            {
                if (!dict.TryGetEntry(key, out var value))
                    continue;
                if (value.Type != MaldaLangValueType.Function && value.Type != MaldaLangValueType.Prompt)
                    throw new RuntimeException($"parallelRun() branch '{key}' must be callable.");
                branches[key] = value;
            }
            return branches;
        }

        if (branchesArg.AsObject() is JsonObject jsonObj)
        {
            foreach (var kvp in jsonObj.GetProperties())
            {
                if (kvp.Value.Type != MaldaLangValueType.Function && kvp.Value.Type != MaldaLangValueType.Prompt)
                    throw new RuntimeException($"parallelRun() branch '{kvp.Key}' must be callable.");
                branches[kvp.Key] = kvp.Value;
            }
            return branches;
        }

        throw new RuntimeException("parallelRun() second argument must be an object/map of named callables.");
    }

    private static List<List<RuntimeValue>> NormalizeDocumentArrayArgs(List<RuntimeValue> args)
    {
        var docArrays = new List<List<RuntimeValue>>();

        if (args.Count == 1 && args[0].Type == MaldaLangValueType.Array)
        {
            var arr = args[0].AsArray();
            if (arr.Count > 0 && arr[0].Type == MaldaLangValueType.Array)
            {
                foreach (var item in arr)
                {
                    if (item.Type == MaldaLangValueType.Array)
                        docArrays.Add(item.AsArray());
                }
                return docArrays;
            }

            docArrays.Add(arr);
            return docArrays;
        }

        foreach (var arg in args)
        {
            if (arg.Type == MaldaLangValueType.Array)
                docArrays.Add(arg.AsArray());
        }

        return docArrays;
    }

    private static string GetDocumentDedupeKey(RuntimeValue docValue)
    {
        if (TryGetDocumentContent(docValue, out var content, out var metadata))
        {
            var source = metadata.TryGetValue("source", out var sourceValue) && sourceValue.Type == MaldaLangValueType.String
                ? sourceValue.AsString()
                : "";
            var chunk = metadata.TryGetValue("chunk", out var chunkValue)
                ? chunkValue.ToString()
                : "";
            if (!string.IsNullOrEmpty(source) || !string.IsNullOrEmpty(chunk))
                return source + "\0" + chunk;
            return content;
        }

        return docValue.ToString();
    }

    private static RuntimeValue InvokeBuiltInInstanceMethod(
        ObjectInstance instance,
        string methodName,
        List<RuntimeValue> arguments,
        Interpreter? interpreter)
    {
        if (instance is ComposedPipeInstance composedPipe)
            return composedPipe.CallMethod(methodName, arguments, interpreter);
        if (instance is RetrieverInstance retriever)
            return retriever.CallMethod(methodName, arguments, interpreter!);
        if (instance is VectorDBInstance vectorDb)
            return vectorDb.CallMethod(methodName, arguments, interpreter);
        if (instance is PromptInstance promptInstance)
            return promptInstance.CallMethod(methodName, arguments, interpreter);

        if (interpreter != null)
            return interpreter.InvokeBuiltInInstanceMethod(instance, methodName, arguments);

        throw new RuntimeException($"Cannot invoke built-in method '{methodName}' without interpreter context.");
    }

    private static async Task<RuntimeValue> InvokeTranspiledDelegateAsync(
        Func<object, Task<object>> delegateFn,
        List<RuntimeValue> args)
    {
        if (args.Count != 1)
            throw new RuntimeException("Transpiled pipeline step expects a single piped argument.");

        var arg = args[0];
        object? input = arg.Type switch
        {
            MaldaLangValueType.Integer => arg.AsInteger(),
            MaldaLangValueType.Float => arg.AsFloat(),
            MaldaLangValueType.String => arg.AsString(),
            MaldaLangValueType.Boolean => arg.AsBoolean(),
            MaldaLangValueType.Array => arg.AsArray(),
            MaldaLangValueType.Object => arg.AsObject(),
            MaldaLangValueType.Function => arg.AsFunction(),
            MaldaLangValueType.Null => null,
            _ => arg
        };

        var result = await delegateFn(input).ConfigureAwait(false);
        return CoerceTranspiledDelegateResult(result);
    }

    private static RuntimeValue CoerceTranspiledDelegateResult(object? result)
    {
        return result switch
        {
            RuntimeValue rv => rv,
            int i => RuntimeValue.Integer(i),
            long l => RuntimeValue.Integer((int)l),
            double d => RuntimeValue.Float(d),
            float f => RuntimeValue.Float(f),
            string s => RuntimeValue.String(s),
            bool b => RuntimeValue.Boolean(b),
            null => RuntimeValue.Null(),
            FunctionValue fn => RuntimeValue.Function(fn),
            _ => RuntimeValue.Object(new DotNetObjectInstance(result!))
        };
    }
}

