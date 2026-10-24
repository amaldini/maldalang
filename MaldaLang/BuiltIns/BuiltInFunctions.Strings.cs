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
    private static RuntimeValue BuiltInLength(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("length() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.Integer(arg.AsString().Length);
        if (arg.Type == MaldaLang.Interpreter.ValueType.Array)
            return RuntimeValue.Integer(arg.AsArray().Count);
        if (arg.Type == MaldaLang.Interpreter.ValueType.Object && arg.AsObject() is TypedBufferInstance buffer)
            return RuntimeValue.Integer(buffer.Length);
        throw new Exception("length() expects a string or array");
    }
    
    private static RuntimeValue BuiltInUpper(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("upper() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.String(arg.AsString().ToUpper());
        throw new Exception("upper() expects a string");
    }
    
    private static RuntimeValue BuiltInLower(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("lower() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.String(arg.AsString().ToLower());
        throw new Exception("lower() expects a string");
    }
    
    private static RuntimeValue BuiltInTrim(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("trim() expects 1 argument");
        var arg = args[0];
        if (arg.Type == MaldaLang.Interpreter.ValueType.String)
            return RuntimeValue.String(arg.AsString().Trim());
        throw new Exception("trim() expects a string");
    }

    /// <summary>
    /// Null-safe stringification for boundary values. Unlike <c>string(null)</c> (which yields
    /// the literal "null"), <c>str.text(null)</c> returns an empty string.
    /// </summary>
    private static RuntimeValue BuiltInText(List<RuntimeValue> args)
    {
        BuiltInArity.Require("text", args, 1, 1, "value");
        if (args[0].Type == MaldaLang.Interpreter.ValueType.Null)
            return RuntimeValue.String("");
        return RuntimeValue.String(args[0].ToString());
    }

    /// <summary>Null-safe trim: <c>str.trim(str.text(value))</c> in one call.</summary>
    private static RuntimeValue BuiltInTrimText(List<RuntimeValue> args)
    {
        BuiltInArity.Require("trimText", args, 1, 1, "value");
        if (args[0].Type == MaldaLang.Interpreter.ValueType.Null)
            return RuntimeValue.String("");
        return RuntimeValue.String(args[0].ToString().Trim());
    }
    
    private static RuntimeValue BuiltInSubstring(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("substring() expects 3 arguments");
        var str = args[0];
        if (str.Type != MaldaLang.Interpreter.ValueType.String
            || !NumericCoercion.TryAsInteger(args[1], out var startIdx)
            || !NumericCoercion.TryAsInteger(args[2], out var len))
            throw new Exception("substring() expects (string, int, int)");
        var s = str.AsString();
        if (startIdx < 0 || startIdx >= s.Length || len < 0)
            throw new Exception("Invalid substring indices");
        var endIdx = Math.Min(startIdx + len, s.Length);
        return RuntimeValue.String(s.Substring(startIdx, endIdx - startIdx));
    }
    
    private static RuntimeValue BuiltInIndexOf(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("indexOf() expects 2 arguments");
        var str = args[0];
        var searchStr = args[1];
        if (str.Type != MaldaLang.Interpreter.ValueType.String || searchStr.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("indexOf() expects (string, string)");
        var s = str.AsString();
        var search = searchStr.AsString();
        var index = s.IndexOf(search, StringComparison.Ordinal);
        return RuntimeValue.Integer(index);
    }
    
    private static RuntimeValue BuiltInReplace(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("replace() expects 3 arguments");
        var text = args[0];
        var oldText = args[1];
        var newText = args[2];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || oldText.Type != MaldaLang.Interpreter.ValueType.String || newText.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("replace() expects (string, string, string)");
        var textStr = text.AsString();
        var oldTextStr = oldText.AsString();
        var newTextStr = newText.AsString();
        var result = textStr.Replace(oldTextStr, newTextStr, StringComparison.Ordinal);
        return RuntimeValue.String(result);
    }
    
    private static RuntimeValue BuiltInSplit(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 3) throw new Exception("split() expects 1 to 3 arguments");
        var text = args[0];
        if (text.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("split() expects (string, string?, bool?)");
        var textStr = text.AsString();
        
        var separator = args.Count > 1 ? args[1] : null;
        var keepSeparator = args.Count > 2 && args[2].Type == ValueType.Boolean && args[2].AsBoolean();
        var result = new List<RuntimeValue>();
        
        if (separator == null || separator.Type == MaldaLang.Interpreter.ValueType.Null)
        {
            // Split by whitespace (default)
            var parts = textStr.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                result.Add(RuntimeValue.String(part));
            }
        }
        else if (separator.Type == MaldaLang.Interpreter.ValueType.String)
        {
            var separatorStr = separator.AsString();
            
            // Check if separator looks like a regex pattern (contains regex special chars)
            // Simple heuristic: if it contains common regex chars, treat as regex
            bool isRegex = separatorStr.Contains("(") || separatorStr.Contains("[") || 
                          separatorStr.Contains("*") || separatorStr.Contains("+") || 
                          separatorStr.Contains("?") || separatorStr.Contains("\\");
            
            if (isRegex)
            {
                // Split by regex pattern
                try
                {
                    var regex = new Regex(separatorStr);
                    var matches = regex.Matches(textStr);
                    var lastIndex = 0;
                    
                    foreach (Match match in matches)
                    {
                        // Add text before match
                        if (match.Index > lastIndex)
                        {
                            result.Add(RuntimeValue.String(textStr.Substring(lastIndex, match.Index - lastIndex)));
                        }
                        
                        // Add separator if keepSeparator is true
                        if (keepSeparator)
                        {
                            result.Add(RuntimeValue.String(match.Value));
                        }
                        
                        lastIndex = match.Index + match.Length;
                    }
                    
                    // Add remaining text
                    if (lastIndex < textStr.Length)
                    {
                        result.Add(RuntimeValue.String(textStr.Substring(lastIndex)));
                    }
                }
                catch (ArgumentException ex)
                {
                    throw new Exception($"Invalid regex pattern in split(): {ex.Message}");
                }
            }
            else
            {
                // Split by string separator
                var parts = textStr.Split(new[] { separatorStr }, StringSplitOptions.None);
                foreach (var part in parts)
                {
                    result.Add(RuntimeValue.String(part));
                }
            }
        }
        else
        {
            throw new Exception("split() separator must be a string or null");
        }
        
        return RuntimeValue.Array(result);
    }

    private static RuntimeValue BuiltInNormalizeText(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("normalizeText() expects 1 or 2 arguments");
        if (args[0].Type != ValueType.String)
            throw new Exception("normalizeText() expects (string, options?)");

        var options = args.Count > 1 ? args[1] : RuntimeValue.Null();
        return RuntimeValue.String(NormalizeTextValue(args[0].AsString(), options));
    }

    private static RuntimeValue BuiltInTokenize(List<RuntimeValue> args)
    {
        if (args.Count < 1 || args.Count > 2) throw new Exception("tokenize() expects 1 or 2 arguments");
        if (args[0].Type != ValueType.String)
            throw new Exception("tokenize() expects (string, options?)");

        var options = args.Count > 1 ? args[1] : RuntimeValue.Null();
        var normalized = NormalizeTextValue(args[0].AsString(), options);
        var tokens = TokenizeNormalizedText(normalized);
        return RuntimeValue.Array(tokens.Select(RuntimeValue.String).ToList());
    }

    private static RuntimeValue BuiltInTokenOverlap(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3) throw new Exception("tokenOverlap() expects 2 or 3 arguments");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("tokenOverlap() expects (string, string, options?)");

        var options = args.Count > 2 ? args[2] : RuntimeValue.Null();
        var leftTokens = TokenizeNormalizedText(NormalizeTextValue(args[0].AsString(), options));
        var rightTokens = TokenizeNormalizedText(NormalizeTextValue(args[1].AsString(), options));
        var leftSet = new HashSet<string>(leftTokens, StringComparer.Ordinal);
        var rightSet = new HashSet<string>(rightTokens, StringComparer.Ordinal);
        var shared = leftSet.Intersect(rightSet, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var unionCount = leftSet.Union(rightSet, StringComparer.Ordinal).Count();
        var payload = new JsonObject();
        payload.Set("sharedCount", RuntimeValue.Integer(shared.Count));
        payload.Set("leftCount", RuntimeValue.Integer(leftSet.Count));
        payload.Set("rightCount", RuntimeValue.Integer(rightSet.Count));
        payload.Set("jaccard", RuntimeValue.Float(unionCount == 0 ? 1.0 : (double)shared.Count / unionCount));
        payload.Set("sharedTokens", RuntimeValue.Array(shared.Select(RuntimeValue.String).ToList()));
        return RuntimeValue.Object(payload);
    }

    private static RuntimeValue BuiltInSimilarity(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 4) throw new Exception("similarity() expects 2 to 4 arguments");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("similarity() expects (string, string, method?, options?)");

        var left = args[0].AsString();
        var right = args[1].AsString();
        var method = args.Count > 2 && args[2].Type == ValueType.String
            ? args[2].AsString().Trim().ToLowerInvariant()
            : "jaccard";
        var options = args.Count > 3 ? args[3] : RuntimeValue.Null();

        var score = method switch
        {
            "exact" => string.Equals(
                NormalizeTextValue(left, options),
                NormalizeTextValue(right, options),
                StringComparison.Ordinal) ? 1.0 : 0.0,
            "contains" => ComputeContainsSimilarity(left, right, options),
            "char-ngram" => ComputeCharacterNGramSimilarity(left, right, options),
            _ => ComputeJaccardSimilarity(left, right, options)
        };

        return RuntimeValue.Float(score);
    }

    private static RuntimeValue BuiltInExtractNumbers(List<RuntimeValue> args)
    {
        if (args.Count != 1) throw new Exception("extractNumbers() expects 1 argument");
        if (args[0].Type != ValueType.String)
            throw new Exception("extractNumbers() expects a string");

        var matches = Regex.Matches(args[0].AsString(), @"-?\d+(?:[.,]\d+)?");
        var values = new List<RuntimeValue>();
        foreach (Match match in matches)
        {
            var text = match.Value.Replace(',', '.');
            if (text.Contains('.'))
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
                {
                    values.Add(RuntimeValue.Float(floatValue));
                }
            }
            else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
            {
                values.Add(RuntimeValue.Integer(intValue));
            }
        }

        return RuntimeValue.Array(values);
    }
    
    private static RuntimeValue BuiltInRegexMatch(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("regexMatch() expects 2 arguments");
        var text = args[0];
        var pattern = args[1];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || pattern.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("regexMatch() expects (string, string)");
        var textStr = text.AsString();
        var patternStr = pattern.AsString();
        try
        {
            var regex = new Regex(patternStr);
            var isMatch = regex.IsMatch(textStr);
            return RuntimeValue.Boolean(isMatch);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regex pattern: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInRegexReplace(List<RuntimeValue> args)
    {
        if (args.Count != 3) throw new Exception("regexReplace() expects 3 arguments");
        var text = args[0];
        var pattern = args[1];
        var replacement = args[2];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || pattern.Type != MaldaLang.Interpreter.ValueType.String || replacement.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("regexReplace() expects (string, string, string)");
        var textStr = text.AsString();
        var patternStr = pattern.AsString();
        var replacementStr = replacement.AsString();
        try
        {
            var regex = new Regex(patternStr);
            var result = regex.Replace(textStr, replacementStr);
            return RuntimeValue.String(result);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regex pattern: {ex.Message}");
        }
    }
    
    private static RuntimeValue BuiltInRegexFind(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("regexFind() expects 2 arguments");
        var text = args[0];
        var pattern = args[1];
        if (text.Type != MaldaLang.Interpreter.ValueType.String || pattern.Type != MaldaLang.Interpreter.ValueType.String)
            throw new Exception("regexFind() expects (string, string)");
        var textStr = text.AsString();
        var patternStr = pattern.AsString();
        try
        {
            var regex = new Regex(patternStr);
            var matches = regex.Matches(textStr);
            var matchList = new List<RuntimeValue>();
            foreach (Match match in matches)
            {
                var matchObj = new BuiltIns.JsonObject();
                matchObj.Set("text", RuntimeValue.String(match.Value));
                var groups = new List<RuntimeValue>();
                for (int i = 0; i < match.Groups.Count; i++)
                {
                    groups.Add(RuntimeValue.String(match.Groups[i].Value));
                }
                matchObj.Set("groups", RuntimeValue.Array(groups));
                matchList.Add(RuntimeValue.Object(matchObj));
            }
            return RuntimeValue.Array(matchList);
        }
        catch (ArgumentException ex)
        {
            throw new Exception($"Invalid regex pattern: {ex.Message}");
        }
    }

    // ========== Additional String Utilities ==========
    
    private static RuntimeValue BuiltInStartsWith(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("startsWith() expects 2 arguments: (str, prefix)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("startsWith() expects string arguments");
        
        var str = args[0].AsString();
        var prefix = args[1].AsString();
        return RuntimeValue.Boolean(str.StartsWith(prefix, StringComparison.Ordinal));
    }
    
    private static RuntimeValue BuiltInEndsWith(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("endsWith() expects 2 arguments: (str, suffix)");
        if (args[0].Type != ValueType.String || args[1].Type != ValueType.String)
            throw new Exception("endsWith() expects string arguments");
        
        var str = args[0].AsString();
        var suffix = args[1].AsString();
        return RuntimeValue.Boolean(str.EndsWith(suffix, StringComparison.Ordinal));
    }
    
    private static RuntimeValue BuiltInPadStart(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3) throw new Exception("padStart() expects 2-3 arguments: (str, length, padChar?)");
        if (args[0].Type != ValueType.String || !NumericCoercion.TryAsInteger(args[1], out var length))
            throw new Exception("padStart() expects (string, integer, string?)");

        var str = args[0].AsString();
        var padChar = args.Count > 2 && args[2].Type == ValueType.String && args[2].AsString().Length > 0
            ? args[2].AsString()[0]
            : ' ';
        
        if (str.Length >= length)
            return RuntimeValue.String(str);
        
        return RuntimeValue.String(str.PadLeft(length, padChar));
    }
    
    private static RuntimeValue BuiltInPadEnd(List<RuntimeValue> args)
    {
        if (args.Count < 2 || args.Count > 3) throw new Exception("padEnd() expects 2-3 arguments: (str, length, padChar?)");
        if (args[0].Type != ValueType.String || !NumericCoercion.TryAsInteger(args[1], out var length))
            throw new Exception("padEnd() expects (string, integer, string?)");

        var str = args[0].AsString();
        var padChar = args.Count > 2 && args[2].Type == ValueType.String && args[2].AsString().Length > 0
            ? args[2].AsString()[0]
            : ' ';
        
        if (str.Length >= length)
            return RuntimeValue.String(str);
        
        return RuntimeValue.String(str.PadRight(length, padChar));
    }
    
    private static RuntimeValue BuiltInRepeat(List<RuntimeValue> args)
    {
        if (args.Count != 2) throw new Exception("repeat() expects 2 arguments: (str, count)");
        if (args[0].Type != ValueType.String || !NumericCoercion.TryAsInteger(args[1], out var count))
            throw new Exception("repeat() expects (string, integer)");

        var str = args[0].AsString();
        
        if (count < 0)
            throw new Exception("repeat() count must be non-negative");
        if (count == 0)
            return RuntimeValue.String("");
        
        return RuntimeValue.String(string.Concat(Enumerable.Repeat(str, count)));
    }

    private static string NormalizeTextValue(string text, RuntimeValue options)
    {
        var lowercase = GetBooleanOption(options, "lowercase", true);
        var stripDiacritics = GetBooleanOption(options, "stripDiacritics", true);
        var collapseWhitespace = GetBooleanOption(options, "collapseWhitespace", true);
        var removePunctuation = GetBooleanOption(options, "removePunctuation", true);

        var normalized = text ?? string.Empty;
        if (lowercase)
        {
            normalized = normalized.ToLowerInvariant();
        }

        if (stripDiacritics)
        {
            normalized = RemoveDiacritics(normalized);
        }

        if (removePunctuation)
        {
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                builder.Append(char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch) ? ch : ' ');
            }
            normalized = builder.ToString();
        }

        if (collapseWhitespace)
        {
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        return normalized;
    }

    private static List<string> TokenizeNormalizedText(string text)
    {
        return text
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static bool GetBooleanOption(RuntimeValue options, string key, bool fallback)
    {
        // Dict literals are JsonObject in the interpreter and DictionaryInstance in C# transpile.
        if (CoerceRuntimeObjectToJsonObject(options) is not { } jsonObject)
        {
            return fallback;
        }

        var value = jsonObject.Get(key, null);
        return value.Type == ValueType.Boolean ? value.AsBoolean() : fallback;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static double ComputeJaccardSimilarity(string left, string right, RuntimeValue options)
    {
        var leftSet = new HashSet<string>(TokenizeNormalizedText(NormalizeTextValue(left, options)), StringComparer.Ordinal);
        var rightSet = new HashSet<string>(TokenizeNormalizedText(NormalizeTextValue(right, options)), StringComparer.Ordinal);
        if (leftSet.Count == 0 && rightSet.Count == 0)
        {
            return 1.0;
        }

        var intersection = leftSet.Intersect(rightSet, StringComparer.Ordinal).Count();
        var union = leftSet.Union(rightSet, StringComparer.Ordinal).Count();
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static double ComputeContainsSimilarity(string left, string right, RuntimeValue options)
    {
        var normalizedLeft = NormalizeTextValue(left, options);
        var normalizedRight = NormalizeTextValue(right, options);
        if (normalizedLeft.Length == 0 && normalizedRight.Length == 0)
        {
            return 1.0;
        }

        if (normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal))
        {
            return 1.0;
        }

        return ComputeJaccardSimilarity(left, right, options);
    }

    private static double ComputeCharacterNGramSimilarity(string left, string right, RuntimeValue options)
    {
        var normalizedLeft = NormalizeTextValue(left, options);
        var normalizedRight = NormalizeTextValue(right, options);
        var leftNgrams = BuildCharacterNgrams(normalizedLeft, 3);
        var rightNgrams = BuildCharacterNgrams(normalizedRight, 3);
        if (leftNgrams.Count == 0 && rightNgrams.Count == 0)
        {
            return 1.0;
        }

        var shared = leftNgrams.Intersect(rightNgrams, StringComparer.Ordinal).Count();
        var total = leftNgrams.Count + rightNgrams.Count;
        return total == 0 ? 0.0 : (2.0 * shared) / total;
    }

    private static HashSet<string> BuildCharacterNgrams(string text, int size)
    {
        var compact = text.Replace(" ", "", StringComparison.Ordinal);
        var ngrams = new HashSet<string>(StringComparer.Ordinal);
        if (compact.Length == 0)
        {
            return ngrams;
        }

        if (compact.Length <= size)
        {
            ngrams.Add(compact);
            return ngrams;
        }

        for (var i = 0; i <= compact.Length - size; i++)
        {
            ngrams.Add(compact.Substring(i, size));
        }

        return ngrams;
    }
}
