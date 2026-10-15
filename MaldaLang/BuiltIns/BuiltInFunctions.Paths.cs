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
    private static RuntimeValue BuiltInGetFileName(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("getFileName() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getFileName() expects a string argument");
        var path = args[0].AsString();
        var fileName = Path.GetFileName(path);
        return RuntimeValue.String(fileName);
    }
    
    private static RuntimeValue BuiltInGetDirectoryName(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("getDirectoryName() expects 1 argument");
        if (args[0].Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("getDirectoryName() expects a string argument");
        var path = args[0].AsString();
        var dirName = Path.GetDirectoryName(path);
        return RuntimeValue.String(dirName ?? "");
    }
    
    // ========== Path Manipulation Functions ==========
    
    private static RuntimeValue BuiltInPathJoin(List<RuntimeValue> args)
    {
        if (args.Count < 1) throw new Exception("pathJoin() expects at least 1 argument");
        var parts = new List<string>();
        foreach (var arg in args)
        {
            if (arg.Type != ValueType.String)
                throw new Exception("pathJoin() all arguments must be strings");
            parts.Add(arg.AsString());
        }

        if (parts.Count > 0 && EmbeddedFolderStore.IsEmbedPath(parts[0]))
        {
            var rest = parts.Skip(1).ToArray();
            return RuntimeValue.String(EmbeddedFolderStore.Join(parts[0], rest));
        }

        return RuntimeValue.String(Path.Combine(parts.ToArray()));
    }

    private static RuntimeValue BuiltInHasEmbeddedFolder(List<RuntimeValue> args)
    {
        BuiltInArity.Require("hasEmbeddedFolder", args, 1, 1, "alias");
        if (args[0].Type != ValueType.String)
            throw new Exception("hasEmbeddedFolder() expects a string alias");
        return RuntimeValue.Boolean(EmbeddedFolderStore.HasAlias(args[0].AsString()));
    }

    private static RuntimeValue BuiltInEmbeddedFolderRoot(List<RuntimeValue> args)
    {
        BuiltInArity.Require("embeddedFolderRoot", args, 1, 1, "alias");
        if (args[0].Type != ValueType.String)
            throw new Exception("embeddedFolderRoot() expects a string alias");
        var alias = args[0].AsString();
        if (!EmbeddedFolderStore.HasAlias(alias))
            return RuntimeValue.Null();
        return RuntimeValue.String(EmbeddedFolderStore.MakeRoot(alias));
    }

    private static void RejectEmbedWrite(string path, string operation)
    {
        if (EmbeddedFolderStore.IsEmbedPath(path))
            throw new Exception($"{operation}() cannot write to embedded path '{path}'");
    }
    
    private static RuntimeValue BuiltInPathNormalize(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("pathNormalize() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("pathNormalize() expects a string argument");
        
        var path = args[0].AsString();
        return RuntimeValue.String(Path.GetFullPath(path));
    }
    
    private static RuntimeValue BuiltInPathExists(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("pathExists() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("pathExists() expects a string argument");
        
        var path = args[0].AsString();
        if (EmbeddedFolderStore.IsEmbedPath(path))
        {
            return RuntimeValue.Boolean(EmbeddedFolderStore.HasFile(path) || EmbeddedFolderStore.HasDirectory(path));
        }
        return RuntimeValue.Boolean(File.Exists(path) || Directory.Exists(path));
    }
    
    private static RuntimeValue BuiltInPathGetExtension(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("pathGetExtension() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("pathGetExtension() expects a string argument");
        
        var path = args[0].AsString();
        return RuntimeValue.String(Path.GetExtension(path));
    }

    /// <summary>
    /// True when <paramref name="path"/> resolves under <paramref name="root"/>
    /// (disk or <c>embed:</c>). Uses full-path comparison with a separator boundary
    /// so sibling prefix names (e.g. <c>demo</c> vs <c>demo-evil</c>) do not match.
    /// </summary>
    internal static bool IsPathUnderRoot(string root, string path)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
            return false;

        if (EmbeddedFolderStore.IsEmbedPath(root) || EmbeddedFolderStore.IsEmbedPath(path))
        {
            if (!EmbeddedFolderStore.IsEmbedPath(root) ||
                !EmbeddedFolderStore.TryParsePath(root, out var rootAlias, out var rootRel))
            {
                return false;
            }

            if (!EmbeddedFolderStore.TryParsePath(path, out var pathAlias, out var pathRel))
                return false;

            if (!string.Equals(rootAlias, pathAlias, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrEmpty(rootRel))
                return true;

            if (string.IsNullOrEmpty(pathRel))
                return false;

            return pathRel.Equals(rootRel, StringComparison.OrdinalIgnoreCase) ||
                   pathRel.StartsWith(rootRel + "/", StringComparison.OrdinalIgnoreCase);
        }

        try
        {
            var rootFull = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathFull = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (pathFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
                return true;

            var prefix = rootFull + Path.DirectorySeparatorChar;
            return pathFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static RuntimeValue BuiltInIsPathUnder(List<RuntimeValue> args)
    {
        BuiltInArity.Require("isPathUnder", args, 2, 2, "root, path");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("isPathUnder() expects (string, string)");

        return RuntimeValue.Boolean(IsPathUnderRoot(args[0].AsString(), args[1].AsString()));
    }
}
