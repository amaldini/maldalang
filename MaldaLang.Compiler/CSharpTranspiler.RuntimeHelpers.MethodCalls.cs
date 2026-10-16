// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Text;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MaldaLang.BuiltIns;
using MaldaLang.Parser;
using MaldaLang.Parser.AST;
using MaldaLang.Parser.AST.Expressions;
using MaldaLang.Parser.AST.Statements;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.IDE;
using MaldaLang.Interpreter;
using MaldaLang.Runtime.Profiling;

using MaldaLang.Compiler.OptionalPack;

namespace MaldaLang.Compiler;

public partial class CSharpTranspiler
{
    private void GenerateRuntimeMethodCallHelpers()
    {
        // Object/dict literals hold their members as entries. Like the interpreter
        // (JsonObject/DictionaryInstance.Get), an entry named like the call wins over the
        // built-in dictionary methods: `{ set: v => v * 2 }.set(4)` calls the stored lambda.
        EmitLines(
            "public static bool TryGetObjectEntry(object? obj, string name, out object? value)",
            "{",
            "    if (obj is MaldaLang.Interpreter.RuntimeValue entryRv && entryRv.Type == MaldaLang.Interpreter.ValueType.Object)",
            "        obj = entryRv.AsObject();",
            "    switch (obj)",
            "    {",
            "        case System.Collections.Generic.Dictionary<string, object?> entryDict:",
            "            return entryDict.TryGetValue(name, out value);",
            "        case MaldaLang.BuiltIns.JsonObject entryJson when entryJson.GetProperties().TryGetValue(name, out var jsonEntry):",
            "            value = jsonEntry;",
            "            return true;",
            "        case MaldaLang.Interpreter.DictionaryInstance entryDi when entryDi.TryGetEntry(name, out var dictEntry):",
            "            value = dictEntry;",
            "            return true;",
            "    }",
            "    value = null;",
            "    return false;",
            "}",
            "public static bool HasObjectEntry(object? obj, string name) => TryGetObjectEntry(obj, name, out _);",
            "public static async System.Threading.Tasks.Task<object> CallObjectEntry(object? entry, List<object> args)",
            "{",
            "    var callable = entry is System.Delegate || entry is MaldaLang.Interpreter.FunctionValue ||",
            "        (entry is MaldaLang.Interpreter.RuntimeValue entryFn && entryFn.Type == MaldaLang.Interpreter.ValueType.Function);",
            "    if (!callable) throw new MaldaLang.Interpreter.RuntimeException(\"Can only call functions, prompts, and classes.\");",
            "    if (args.Count <= 1 && entry is System.Func<object, System.Threading.Tasks.Task<object>> unary)",
            "        return await unary(args.Count == 1 ? args[0] : null!);",
            "    return await CallFunctionN(entry, args.Cast<object?>().ToArray());",
            "}");
        WriteIndent();
        _output.AppendLine("public static async System.Threading.Tasks.Task<object> CallObjectMethod(object? obj, string methodName, List<object> args, System.Type? accessingClass = null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (obj == null) throw new InvalidOperationException(\"Cannot access methods of null.\");");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.RuntimeValue __rvCall)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var __unwrappedCall = UnwrapRuntimeValue(__rvCall);");
        WriteIndent();
        _output.AppendLine("if (__unwrappedCall is MaldaLang.Interpreter.ObjectInstance)");
        WriteIndent();
        _output.AppendLine("obj = __unwrappedCall;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (TryGetObjectEntry(obj, methodName, out var __entryValue))");
        WriteIndent();
        _output.AppendLine("    return await CallObjectEntry(__entryValue, args);");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.Dictionary<string, object?> nativeDict)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        // The remaining dictionary methods of JsonObject/DictionaryInstance.CallMethod.
        EmitLines(
            "case \"get\":",
            "    if (args.Count != 1) throw new MaldaLang.Interpreter.RuntimeException(\"get() expects 1 argument\");",
            "    if (args[0] is not string getKey) throw new MaldaLang.Interpreter.RuntimeException(\"Dictionary keys must be strings.\");",
            "    return nativeDict.TryGetValue(getKey, out var gotValue) ? gotValue! : null!;",
            "case \"set\":",
            "    if (args.Count != 2) throw new MaldaLang.Interpreter.RuntimeException(\"set() expects 2 arguments\");",
            "    if (args[0] is not string setKey) throw new MaldaLang.Interpreter.RuntimeException(\"Dictionary keys must be strings.\");",
            "    nativeDict[setKey] = args[1];",
            "    return nativeDict;",
            "case \"remove\":",
            "    if (args.Count != 1) throw new MaldaLang.Interpreter.RuntimeException(\"remove() expects 1 argument\");",
            "    if (args[0] is not string removeKey) throw new MaldaLang.Interpreter.RuntimeException(\"Dictionary keys must be strings.\");",
            "    return nativeDict.Remove(removeKey);",
            "case \"entries\":",
            "    return nativeDict.Select(e => (object)new List<object> { e.Key, e.Value! }).ToList();");
        WriteIndent();
        _output.AppendLine("case \"containsKey\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return false;");
        WriteIndent();
        _output.AppendLine("return nativeDict.ContainsKey(CoerceToString(args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"keys\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return nativeDict.Keys.Select(k => (object)k).ToList();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"values\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return nativeDict.Values.Select(v => v ?? new object()).ToList();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.List<MaldaLang.Interpreter.RuntimeValue> runtimeValueList)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("obj = runtimeValueList.Select(v => UnwrapRuntimeValue(v) ?? new object()).ToList();");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is List<object> list)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"includes\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return false;");
        WriteIndent();
        _output.AppendLine("return list.Any(item => AreObjectsEqual(item, args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"except\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return new List<object>();");
        WriteIndent();
        _output.AppendLine("return ArrayExcept(list, GetArray(args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"indexOf\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return -1;");
        WriteIndent();
        _output.AppendLine("for (var i = 0; i < list.Count; i++)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (AreObjectsEqual(list[i], args[0])) return i;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return -1;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"join\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var separator = args.Count > 0 ? CoerceToString(args[0]) : \",\";");
        WriteIndent();
        _output.AppendLine("return string.Join(separator, list.Select(CoerceToString));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"reverse\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("list.Reverse();");
        WriteIndent();
        _output.AppendLine("return list;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"popOrNull\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (list.Count == 0) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayPop(list);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"shiftOrNull\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (list.Count == 0) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayShift(list);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"get\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayGet(list, (int)CoerceToInt(args[0]), args.Count > 1 ? args[1] : null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"at\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return null;");
        WriteIndent();
        _output.AppendLine("return ArrayGet(list, (int)CoerceToInt(args[0]));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("case \"slice\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count < 1) return new List<object>();");
        WriteIndent();
        _output.AppendLine("var start = (int)CoerceToInt(args[0]);");
        WriteIndent();
        _output.AppendLine("var end = args.Count > 1 ? (int)CoerceToInt(args[1]) : list.Count;");
        WriteIndent();
        _output.AppendLine("start = NormalizeArrayIndex(list, start);");
        WriteIndent();
        _output.AppendLine("end = NormalizeArrayIndex(list, end);");
        WriteIndent();
        _output.AppendLine("start = System.Math.Max(0, System.Math.Min(start, list.Count));");
        WriteIndent();
        _output.AppendLine("end = System.Math.Max(0, System.Math.Min(end, list.Count));");
        WriteIndent();
        _output.AppendLine("end = System.Math.Max(start, end);");
        WriteIndent();
        _output.AppendLine("return list.GetRange(start, end - start);");
        _indentLevel--;
        // Higher-order methods mirror ArrayInstance in the interpreter. Without these cases
        // `arr.map(f)` fell through to reflection on List<object> and returned null.
        EmitLines(
            "case \"map\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"map() expects 1 argument\");",
            "    var mapped = new List<object>(list.Count);",
            "    foreach (var item in list.ToList()) mapped.Add(await CallFunction(args[0], item));",
            "    return mapped;",
            "}",
            "case \"filter\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"filter() expects 1 argument\");",
            "    var kept = new List<object>();",
            "    foreach (var item in list.ToList()) if (CoerceToBool(await CallFunction(args[0], item))) kept.Add(item);",
            "    return kept;",
            "}",
            "case \"reduce\":",
            "{",
            "    if (args.Count < 1 || args.Count > 2) throw new InvalidOperationException(\"reduce() expects 1 or 2 arguments\");",
            "    if (list.Count == 0 && args.Count == 1) throw new InvalidOperationException(\"reduce() on empty array requires initial value\");",
            "    var acc = args.Count == 2 ? args[1] : list[0];",
            "    foreach (var item in list.Skip(args.Count == 2 ? 0 : 1).ToList()) acc = await CallFunctionN(args[0], new object?[] { acc, item });",
            "    return acc;",
            "}",
            "case \"forEach\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"forEach() expects 1 argument\");",
            "    foreach (var item in list.ToList()) await CallFunction(args[0], item);",
            "    return null;",
            "}",
            "case \"find\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"find() expects 1 argument\");",
            "    foreach (var item in list.ToList()) if (CoerceToBool(await CallFunction(args[0], item))) return item;",
            "    return null;",
            "}",
            "case \"findIndex\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"findIndex() expects 1 argument\");",
            "    var snapshot = list.ToList();",
            "    for (var fi = 0; fi < snapshot.Count; fi++) if (CoerceToBool(await CallFunction(args[0], snapshot[fi]))) return fi;",
            "    return -1;",
            "}",
            "case \"some\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"some() expects 1 argument\");",
            "    foreach (var item in list.ToList()) if (CoerceToBool(await CallFunction(args[0], item))) return true;",
            "    return false;",
            "}",
            "case \"every\":",
            "{",
            "    if (args.Count != 1) throw new InvalidOperationException(\"every() expects 1 argument\");",
            "    foreach (var item in list.ToList()) if (!CoerceToBool(await CallFunction(args[0], item))) return false;",
            "    return true;",
            "}");
        WriteIndent();
        _output.AppendLine("case \"sort\":");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (args.Count > 0 && args[0] is System.Func<object, object, System.Threading.Tasks.Task<object>> compare)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("list.Sort((a, b) => (int)CoerceToInt(compare(a, b).GetAwaiter().GetResult()));");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("list.Sort(CompareObjects);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return list;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.Interpreter.ObjectInstance instance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Convert arguments to RuntimeValue");
        WriteIndent();
        _output.AppendLine("var runtimeArgs = args.Select(a => a switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("null => MaldaLang.Interpreter.RuntimeValue.Null(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue runtimeValue => runtimeValue,");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.FunctionValue functionValue => MaldaLang.Interpreter.RuntimeValue.Function(functionValue),");
        WriteIndent();
        _output.AppendLine("int i => MaldaLang.Interpreter.RuntimeValue.Integer(i),");
        WriteIndent();
        _output.AppendLine("long l => MaldaLang.Interpreter.RuntimeValue.Integer((int)l),");
        WriteIndent();
        _output.AppendLine("double d => MaldaLang.Interpreter.RuntimeValue.Float(d),");
        WriteIndent();
        _output.AppendLine("float f => MaldaLang.Interpreter.RuntimeValue.Float(f),");
        WriteIndent();
        _output.AppendLine("string s => MaldaLang.Interpreter.RuntimeValue.String(s),");
        WriteIndent();
        _output.AppendLine("bool b => MaldaLang.Interpreter.RuntimeValue.Boolean(b),");
        WriteIndent();
        _output.AppendLine("System.Func<object, System.Threading.Tasks.Task<object>> funcDel => MaldaLang.Interpreter.RuntimeValue.Function(new MaldaLang.Interpreter.FunctionValue { TranspiledDelegate = funcDel }),");
        WriteIndent();
        _output.AppendLine("System.Delegate del => WrapTranspiledDelegate(del),");
        WriteIndent();
        _output.AppendLine("System.Collections.Generic.Dictionary<string, object?> dictObj => MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(dictObj.ToDictionary(e => e.Key, e => RuntimeHelpers.ToRuntimeValue(e.Value)))),");
        WriteIndent();
        _output.AppendLine("System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<string, object?>> kvpsObj => MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.Interpreter.DictionaryInstance(kvpsObj.ToDictionary(e => e.Key, e => RuntimeHelpers.ToRuntimeValue(e.Value)))),");
        WriteIndent();
        _output.AppendLine("System.Collections.IEnumerable seq => MaldaLang.Interpreter.RuntimeValue.Array(seq.Cast<object?>().Select(v => RuntimeHelpers.ToRuntimeValue(v)).ToList()),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ObjectInstance oi => MaldaLang.Interpreter.RuntimeValue.Object(oi),");
        WriteIndent();
        _output.AppendLine("object other => MaldaLang.Interpreter.RuntimeValue.Object(new MaldaLang.BuiltIns.DotNetObjectInstance(other))");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}).ToList();");
        WriteIndent();
        _output.AppendLine("// Call the method");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue? result = null;");
        WriteIndent();
        _output.AppendLine("if (instance is MaldaLang.BuiltIns.RestServerInstance restServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = restServer.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.HttpServerInstance httpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = httpServer.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.RestClientInstance restClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = restClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SerialConnectionInstance serialConnection)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = serialConnection.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ArduinoConnectionInstance arduinoConnection)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = arduinoConnection.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.OnnxModelInstance onnxModel)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = onnxModel.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.DenseInstance denseLayer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = denseLayer.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SequentialInstance sequentialNet)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = sequentialNet.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.ConvInstance", "convLayer");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.Conv2DInstance", "conv2dLayer");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.MaxPool2DInstance", "maxPool2d");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.AvgPool2DInstance", "avgPool2d");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.FlattenInstance", "flattenLayer");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.BatchNorm2DInstance", "batchNorm2d");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.Dropout2DInstance", "dropout2d");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.GlobalAvgPool2DInstance", "globalAvgPool2d");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.GlobalMaxPool2DInstance", "globalMaxPool2d");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.ActivationInstance", "activationLayer");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.EmbeddingInstance", "embeddingLayer");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.RnnInstance", "rnnLayer");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.LayerNormInstance", "layerNorm");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.AttentionInstance", "attentionHead");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.ParallelInstance", "parallelNet");
        WriteNeuralLayerDispatch("MaldaLang.BuiltIns.IdentityInstance", "identityLayer");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.GraphMemoryInstance graphMemory)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var transpiledInterpreter = MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter();");
        WriteIndent();
        _output.AppendLine("graphMemory.SetInterpreter(transpiledInterpreter);");
        WriteIndent();
        _output.AppendLine("result = graphMemory.CallMethod(methodName, runtimeArgs, transpiledInterpreter);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.VectorDBInstance vectorDb)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = vectorDb.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.RetrieverInstance retriever)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = retriever.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.GroundedInstance grounded)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = grounded.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.CapInstance cap)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = cap.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.AgentsInstance agents)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = agents.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.AgentTeamInstance agentTeam)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = agentTeam.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.DeclaredContextInstance declaredContext)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = declaredContext.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.AgentInstance agent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = agent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.CodingAgentInstance codingAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = codingAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.GitAgentInstance gitAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = gitAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.DevAgentInstance devAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = devAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MALDACodingAgentInstance maldaCodingAgent)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = maldaCodingAgent.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ConversationInstance conv)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = conv.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.LLMClientInstance llmClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = llmClient.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.LlamaCppClientInstance llamaCppClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = llamaCppClient.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.LLMClientBridge.LLMClientBridgeInstance llmBridge)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = llmBridge.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ToolInstance tool)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = tool.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.DotNetObjectInstance dotNetObj)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = dotNetObj.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MCPClientInstance mcpClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = mcpClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MCPServerInstance mcpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = mcpServer.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SqlServerClientInstance sqlServerClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = sqlServerClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.PostgresClientInstance postgresClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = postgresClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.SqliteClientInstance sqliteClient)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = sqliteClient.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.AnsiConsoleInstance ansiConsole)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("// Check if it's an async method");
        WriteIndent();
        _output.AppendLine("if (methodName == \"status\" || methodName == \"prompt\" || methodName == \"progress\")");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = await ansiConsole.CallMethodAsync(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = ansiConsole.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.UiFrameworkInstance ui)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = ui.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.GraphInstance graph)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = graph.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.ArrayInstance array)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = array.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.JsonObject jsonObject)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = jsonObject.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.Interpreter.DictionaryInstance dict)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = dict.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ProgressContextWrapper progressCtx)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = progressCtx.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ResponseContextInstance responseContext)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = responseContext.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.RequestContextInstance requestContext)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = requestContext.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.RequestAuthContextInstance requestAuthContext)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = requestAuthContext.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.RequestSessionContextInstance requestSessionContext)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = requestSessionContext.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.MiddlewareNextCallbackInstance nextCallback)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = nextCallback.CallMethod(methodName, runtimeArgs);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.ComposedPipeInstance composedPipe)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = composedPipe.CallMethod(methodName, runtimeArgs, null);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (instance is MaldaLang.BuiltIns.PromptInstance promptInstance)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("result = promptInstance.CallMethod(methodName, runtimeArgs, MaldaLang.Runtime.TranspiledBuiltinRuntime.GetOrCreateInterpreter());");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// For other ObjectInstance types, return null");
        WriteIndent();
        _output.AppendLine("if (result == null) return null;");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.RuntimeValue resultValue = result!;");
        WriteIndent();
        _output.AppendLine("return resultValue.Type switch");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Integer => resultValue.AsInteger(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Float => resultValue.AsFloat(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.String => resultValue.AsString(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Boolean => resultValue.AsBoolean(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Array => resultValue.AsArray(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Object => resultValue.AsObject(),");
        WriteIndent();
        _output.AppendLine("MaldaLang.Interpreter.ValueType.Variant => resultValue,");
        WriteIndent();
        _output.AppendLine("_ => null");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("};");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// A dictionary without that entry or built-in method: the interpreter's callee is null.");
        WriteIndent();
        _output.AppendLine("if (obj is System.Collections.Generic.Dictionary<string, object?>) throw new MaldaLang.Interpreter.RuntimeException(\"Can only call functions, prompts, and classes.\");");
        WriteIndent();
        _output.AppendLine("// Reflection fallback for transpiled class instances");
        WriteIndent();
        _output.AppendLine("var targetType = obj.GetType();");
        WriteIndent();
        _output.AppendLine("var methodCandidate = FindInheritedMethods(targetType, methodName)");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine(".FirstOrDefault(m => m.GetParameters().Length == args.Count);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("if (methodCandidate != null && methodCandidate.IsPrivate && accessingClass != targetType)");
        WriteIndent();
        _output.AppendLine("    throw new MaldaLang.Interpreter.RuntimeException($\"Cannot access private method '{methodName}' from outside {targetType.Name}.\");");
        WriteIndent();
        _output.AppendLine("if (methodCandidate != null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var parameters = methodCandidate.GetParameters();");
        WriteIndent();
        _output.AppendLine("var invokeArgs = new object?[args.Count];");
        WriteIndent();
        _output.AppendLine("for (int i = 0; i < args.Count; i++)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("var parameterType = parameters[i].ParameterType;");
        WriteIndent();
        _output.AppendLine("var argValue = args[i];");
        WriteIndent();
        _output.AppendLine("if (parameterType == typeof(object) || argValue == null)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = argValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(int))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = (int)CoerceToInt(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(double))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = (double)CoerceToFloat(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(float))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = (float)(double)CoerceToFloat(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType == typeof(string))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = CoerceToString(argValue);");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (parameterType.IsInstanceOfType(argValue))");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = argValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("invokeArgs[i] = argValue;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("var invokeResult = methodCandidate.Invoke(obj, invokeArgs);");
        WriteIndent();
        _output.AppendLine("if (invokeResult is System.Threading.Tasks.Task<object> objectTask)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("return await objectTask;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("if (invokeResult is System.Threading.Tasks.Task anyTask)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("await anyTask;");
        WriteIndent();
        _output.AppendLine("var resultProperty = anyTask.GetType().GetProperty(\"Result\");");
        WriteIndent();
        _output.AppendLine("return resultProperty != null ? resultProperty.GetValue(anyTask) : null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return invokeResult;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
        
        WriteIndent();
        _output.AppendLine("public static object CallVoidMethod(object? obj, string methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        // An object-literal entry, or a method of a transpiled class (`e.start()`), is a
        // user call like any other; only the built-in servers below are fire-and-forget.
        EmitLines(
            "if (TryGetObjectEntry(obj, methodName, out var voidEntry))",
            "    return CallObjectEntry(voidEntry, new List<object>()).GetAwaiter().GetResult();",
            "if (obj != null && obj.GetType().Assembly == typeof(RuntimeHelpers).Assembly)",
            "    return CallObjectMethod(obj, methodName, new List<object>()).GetAwaiter().GetResult();");
        WriteIndent();
        _output.AppendLine("if (obj is MaldaLang.BuiltIns.HttpServerInstance httpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"start\": httpServer.Start(); break;");
        WriteIndent();
        _output.AppendLine("case \"stop\": httpServer.Stop(); break;");
        WriteIndent();
        _output.AppendLine("case \"clearCache\": httpServer.ClearCache(); break;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (obj is MaldaLang.BuiltIns.MCPServerInstance mcpServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"start\": mcpServer.CallMethod(\"start\", new List<MaldaLang.Interpreter.RuntimeValue>()); break;");
        WriteIndent();
        _output.AppendLine("case \"stop\": mcpServer.CallMethod(\"stop\", new List<MaldaLang.Interpreter.RuntimeValue>()); break;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("else if (obj is MaldaLang.BuiltIns.RestServerInstance restServer)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("switch (methodName)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("case \"start\": restServer.CallMethod(\"start\", new List<MaldaLang.Interpreter.RuntimeValue>()); break;");
        WriteIndent();
        _output.AppendLine("case \"stop\": restServer.CallMethod(\"stop\", new List<MaldaLang.Interpreter.RuntimeValue>()); break;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();

        // Helper to route .stop() calls either to ActorsRuntime.Stop for actors
        // or to regular object methods (e.g., HttpServerInstance, MCPServerInstance).
        WriteIndent();
        _output.AppendLine("public static object? CallActorOrVoidStop(object? target)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("if (target == null) return null;");
        WriteIndent();
        _output.AppendLine("// Check if target is an ActorRef (struct, may be boxed)");
        WriteIndent();
        _output.AppendLine("if (target is ActorRef actorRef)");
        WriteIndent();
        _output.AppendLine("{");
        _indentLevel++;
        WriteIndent();
        _output.AppendLine("ActorsRuntime.Stop(actorRef);");
        WriteIndent();
        _output.AppendLine("return null;");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        WriteIndent();
        _output.AppendLine("// For non-actor objects, try calling their stop method");
        WriteIndent();
        _output.AppendLine("return CallVoidMethod(target, \"stop\");");
        _indentLevel--;
        WriteIndent();
        _output.AppendLine("}");
        _output.AppendLine();
    }
}
