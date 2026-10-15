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
    // ========== Encoding/Decoding Functions ==========
    
    private static RuntimeValue BuiltInBase64Encode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("base64Encode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("base64Encode() expects a string argument");
        
        var text = args[0].AsString();
        var bytes = Encoding.UTF8.GetBytes(text);
        var encoded = Convert.ToBase64String(bytes);
        return RuntimeValue.String(encoded);
    }
    
    private static RuntimeValue BuiltInBase64Decode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("base64Decode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("base64Decode() expects a string argument");
        
        try
        {
            var encoded = args[0].AsString();
            var bytes = Convert.FromBase64String(encoded);
            var decoded = Encoding.UTF8.GetString(bytes);
            return RuntimeValue.String(decoded);
        }
        catch (FormatException)
        {
            throw new Exception("base64Decode() invalid base64 string");
        }
    }
    
    private static RuntimeValue BuiltInUrlEncode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("urlEncode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("urlEncode() expects a string argument");
        
        var text = args[0].AsString();
        var encoded = Uri.EscapeDataString(text);
        return RuntimeValue.String(encoded);
    }
    
    private static RuntimeValue BuiltInUrlDecode(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("urlDecode() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("urlDecode() expects a string argument");
        
        try
        {
            var encoded = args[0].AsString();
            var decoded = Uri.UnescapeDataString(encoded);
            return RuntimeValue.String(decoded);
        }
        catch (UriFormatException)
        {
            throw new Exception("urlDecode() invalid URL encoded string");
        }
    }
    
    // ========== Hash Functions ==========
    
    private static RuntimeValue BuiltInMd5(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("md5() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("md5() expects a string argument");
        
        var text = args[0].AsString();
        var bytes = Encoding.UTF8.GetBytes(text);
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            var hashBytes = md5.ComputeHash(bytes);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return RuntimeValue.String(hash);
        }
    }
    
    private static RuntimeValue BuiltInSha256(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("sha256() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("sha256() expects a string argument");
        
        var text = args[0].AsString();
        var bytes = Encoding.UTF8.GetBytes(text);
        using (var sha256 = System.Security.Cryptography.SHA256.Create())
        {
            var hashBytes = sha256.ComputeHash(bytes);
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            return RuntimeValue.String(hash);
        }
    }

    private static RuntimeValue BuiltInHashPassword(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2)
            throw new Exception("hashPassword() expects 1 or 2 arguments: (password, iterations?)");
        if (args[0].Type != ValueType.String)
            throw new Exception("hashPassword() first argument must be a string");

        var password = args[0].AsString();
        var iterations = 100_000;
        if (args.Count == 2)
        {
            if (args[1].Type != ValueType.Integer)
                throw new Exception("hashPassword() second argument (iterations) must be an integer");
            iterations = args[1].AsInteger();
        }

        if (iterations < 10_000)
            throw new Exception("hashPassword() iterations must be >= 10000");

        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);

        var encoded = string.Join(
            "$",
            "pbkdf2",
            "sha256",
            iterations.ToString(),
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
        return RuntimeValue.String(encoded);
    }

    private static RuntimeValue BuiltInVerifyPassword(List<RuntimeValue> args)
    {
        if (args.Count != 2)
            throw new Exception("verifyPassword() expects 2 arguments: (password, passwordHash)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("verifyPassword() expects (string, string)");

        var password = args[0].AsString();
        var storedHash = args[1].AsString();
        var parts = storedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2" || parts[1] != "sha256")
            return RuntimeValue.Boolean(false);

        if (!int.TryParse(parts[2], out var iterations) || iterations < 10_000)
            return RuntimeValue.Boolean(false);

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expectedHash = Convert.FromBase64String(parts[4]);
        }
        catch
        {
            return RuntimeValue.Boolean(false);
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        var isValid = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        return RuntimeValue.Boolean(isValid);
    }
}
