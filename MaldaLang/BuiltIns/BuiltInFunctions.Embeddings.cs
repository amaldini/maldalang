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
    // ============================================
    // Text Embedding Functions
    // ============================================
    
    // Helper: Read text from string or file path
    private static string ReadTextOrFile(RuntimeValue textOrPath)
    {
        if (textOrPath.Type == ValueType.Array)
        {
            // If array, concatenate all strings
            var array = textOrPath.AsArray();
            var sb = new StringBuilder();
            foreach (var item in array)
            {
                if (item.Type == ValueType.String)
                {
                    var text = ReadTextOrFile(item);
                    sb.Append(text);
                    sb.Append(" ");
                }
            }
            return sb.ToString().Trim();
        }
        
        if (textOrPath.Type != ValueType.String)
            throw new Exception("embed functions expect string or array of strings");
        
        var str = textOrPath.AsString();
        
        // Check if it looks like a file path
        if (str.Contains(Path.DirectorySeparatorChar) || str.Contains(Path.AltDirectorySeparatorChar) ||
            str.Contains("/") || str.Contains("\\") ||
            str.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            str.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            // Try to read as file
            if (File.Exists(str))
            {
                try
                {
                    return File.ReadAllText(str);
                }
                catch
                {
                    // If file read fails, treat as text
                    return str;
                }
            }
        }
        
        return str;
    }
    
    // Helper: Tokenize text into words
    private static List<string> TokenizeText(string text)
    {
        var words = new List<string>();
        var currentWord = new StringBuilder();
        
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
            {
                currentWord.Append(char.ToLowerInvariant(c));
            }
            else
            {
                if (currentWord.Length > 0)
                {
                    words.Add(currentWord.ToString());
                    currentWord.Clear();
                }
            }
        }
        
        if (currentWord.Length > 0)
        {
            words.Add(currentWord.ToString());
        }
        
        return words;
    }
    
    // Helper: Generate character n-grams
    private static List<string> GenerateNGrams(string text, int n)
    {
        var ngrams = new List<string>();
        if (string.IsNullOrEmpty(text) || n <= 0)
            return ngrams;
        
        text = text.ToLowerInvariant();
        for (int i = 0; i <= text.Length - n; i++)
        {
            ngrams.Add(text.Substring(i, n));
        }
        
        return ngrams;
    }
    
    // Helper: FNV-1a hash function for feature hashing
    private static uint FNV1aHash(string input)
    {
        const uint FNV_OFFSET_BASIS = 2166136261u;
        const uint FNV_PRIME = 16777619u;
        
        uint hash = FNV_OFFSET_BASIS;
        foreach (byte b in Encoding.UTF8.GetBytes(input))
        {
            hash ^= b;
            hash *= FNV_PRIME;
        }
        
        return hash;
    }
    
    // Helper: Hash feature to dimension index
    private static int HashFeature(string feature, int dimension)
    {
        var hash = FNV1aHash(feature);
        return (int)(hash % (uint)dimension);
    }
    
    // Helper: L2 normalize vector
    private static List<double> NormalizeVector(List<double> vector)
    {
        double norm = 0.0;
        foreach (var val in vector)
        {
            norm += val * val;
        }
        
        norm = Math.Sqrt(norm);
        
        if (norm == 0.0)
            return vector; // Zero vector stays zero
        
        var normalized = new List<double>();
        foreach (var val in vector)
        {
            normalized.Add(val / norm);
        }
        
        return normalized;
    }
    
    // embedBagOfWords: Bag-of-words with word frequencies
    private static RuntimeValue BuiltInEmbedBagOfWords(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedBagOfWords() expects at least 1 argument (text, dimension?, vocabulary?)");
        
        var text = ReadTextOrFile(args[0]);
        int dimension = 1000;
        List<string>? vocabulary = null;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            dimension = args[1].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedBagOfWords() dimension must be greater than 0");
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Array)
        {
            vocabulary = new List<string>();
            foreach (var item in args[2].AsArray())
            {
                if (item.Type == ValueType.String)
                    vocabulary.Add(item.AsString().ToLowerInvariant());
            }
        }
        
        var words = TokenizeText(text);
        var vector = new List<double>(new double[dimension]);
        
        if (vocabulary != null)
        {
            // Use exact vocabulary matching
            var vocabDict = new Dictionary<string, int>();
            for (int i = 0; i < vocabulary.Count && i < dimension; i++)
            {
                vocabDict[vocabulary[i]] = i;
            }
            
            foreach (var word in words)
            {
                if (vocabDict.TryGetValue(word, out int index))
                {
                    vector[index] += 1.0;
                }
            }
        }
        else
        {
            // Use feature hashing
            foreach (var word in words)
            {
                int index = HashFeature(word, dimension);
                vector[index] += 1.0;
            }
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result);
    }
    
    // embedCharacterNGrams: Character n-gram embeddings
    private static RuntimeValue BuiltInEmbedCharacterNGrams(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedCharacterNGrams() expects at least 1 argument (text, n?, dimension?)");
        
        var text = ReadTextOrFile(args[0]);
        int n = 3;
        int dimension = 1000;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            n = args[1].AsInteger();
            if (n <= 0)
                throw new Exception("embedCharacterNGrams() n must be greater than 0");
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Integer)
        {
            dimension = args[2].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedCharacterNGrams() dimension must be greater than 0");
        }
        
        var ngrams = GenerateNGrams(text, n);
        var vector = new List<double>(new double[dimension]);
        
        foreach (var ngram in ngrams)
        {
            int index = HashFeature(ngram, dimension);
            vector[index] += 1.0;
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result);
    }
    
    // embedHash: Hash-based feature hashing
    private static RuntimeValue BuiltInEmbedHash(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedHash() expects at least 1 argument (text, dimension?)");
        
        var text = ReadTextOrFile(args[0]);
        int dimension = 128;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            dimension = args[1].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedHash() dimension must be greater than 0");
        }
        
        var words = TokenizeText(text);
        var vector = new List<double>(new double[dimension]);
        
        foreach (var word in words)
        {
            int index = HashFeature(word, dimension);
            vector[index] += 1.0;
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result);
    }
    
    // embedTFIDF: TF-IDF embeddings
    private static RuntimeValue BuiltInEmbedTFIDF(List<RuntimeValue> args)
    {
        if (args.Count < 1)
            throw new Exception("embedTFIDF() expects at least 1 argument (text, corpus?, dimension?)");
        
        var text = ReadTextOrFile(args[0]);
        int dimension = 1000;
        List<string>? corpus = null;
        
        if (args.Count >= 2 && args[1].Type == ValueType.Array)
        {
            corpus = new List<string>();
            foreach (var item in args[1].AsArray())
            {
                corpus.Add(ReadTextOrFile(item));
            }
        }
        else if (args.Count >= 2 && args[1].Type == ValueType.Integer)
        {
            // Second argument is dimension, not corpus
            dimension = args[1].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedTFIDF() dimension must be greater than 0");
        }
        
        if (args.Count >= 3 && args[2].Type == ValueType.Integer)
        {
            dimension = args[2].AsInteger();
            if (dimension <= 0)
                throw new Exception("embedTFIDF() dimension must be greater than 0");
        }
        
        var words = TokenizeText(text);
        var vector = new List<double>(new double[dimension]);
        
        // Calculate term frequencies (TF)
        var wordCounts = new Dictionary<string, int>();
        foreach (var word in words)
        {
            wordCounts[word] = wordCounts.GetValueOrDefault(word, 0) + 1;
        }
        
        int totalWords = words.Count;
        if (totalWords == 0)
        {
            // Return zero vector
            var result = new List<RuntimeValue>();
            for (int i = 0; i < dimension; i++)
            {
                result.Add(RuntimeValue.Float(0.0));
            }
            return RuntimeValue.Array(result);
        }
        
        // Calculate IDF if corpus provided
        var idf = new Dictionary<string, double>();
        if (corpus != null && corpus.Count > 0)
        {
            var docWordSets = new List<HashSet<string>>();
            foreach (var doc in corpus)
            {
                var docWords = new HashSet<string>(TokenizeText(doc));
                docWordSets.Add(docWords);
            }
            
            int totalDocs = corpus.Count;
            foreach (var word in wordCounts.Keys)
            {
                int docsWithWord = 0;
                foreach (var docWords in docWordSets)
                {
                    if (docWords.Contains(word))
                        docsWithWord++;
                }
                
                if (docsWithWord > 0)
                {
                    idf[word] = Math.Log((double)totalDocs / docsWithWord) + 1.0;
                }
                else
                {
                    idf[word] = 1.0;
                }
            }
        }
        
        // Calculate TF-IDF
        foreach (var kvp in wordCounts)
        {
            var word = kvp.Key;
            var tf = (double)kvp.Value / totalWords;
            var idfValue = idf.GetValueOrDefault(word, 1.0);
            var tfidf = tf * idfValue;
            
            int index = HashFeature(word, dimension);
            vector[index] += tfidf;
        }
        
        // Normalize
        vector = NormalizeVector(vector);
        
        var result2 = new List<RuntimeValue>();
        foreach (var val in vector)
        {
            result2.Add(RuntimeValue.Float(val));
        }
        
        return RuntimeValue.Array(result2);
    }
    
    // embedFromFile: Convenience function to read file and embed
    private static RuntimeValue BuiltInEmbedFromFile(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("embedFromFile() expects at least 2 arguments (filePath, method, ...)");
        
        if (args[0].Type != ValueType.String)
            throw new Exception("embedFromFile() first argument must be a string (file path)");
        
        if (args[1].Type != ValueType.String)
            throw new Exception("embedFromFile() second argument must be a string (method name)");
        
        var filePath = args[0].AsString();
        var method = args[1].AsString().ToLowerInvariant();
        
        if (!File.Exists(filePath))
            throw new Exception($"embedFromFile() file not found: {filePath}");
        
        var fileContent = File.ReadAllText(filePath);
        var newArgs = new List<RuntimeValue> { RuntimeValue.String(fileContent) };
        
        // Add remaining arguments
        for (int i = 2; i < args.Count; i++)
        {
            newArgs.Add(args[i]);
        }
        
        return method switch
        {
            "bagofwords" or "bag_of_words" => BuiltInEmbedBagOfWords(newArgs),
            "characterngrams" or "character_ngrams" => BuiltInEmbedCharacterNGrams(newArgs),
            "hash" => BuiltInEmbedHash(newArgs),
            "tfidf" => BuiltInEmbedTFIDF(newArgs),
            _ => throw new Exception($"embedFromFile() unknown method: {method}. Supported: bagOfWords, characterNGrams, hash, tfidf")
        };
    }
    
    // embedFromFiles: Embed multiple files
    private static RuntimeValue BuiltInEmbedFromFiles(List<RuntimeValue> args)
    {
        if (args.Count < 2)
            throw new Exception("embedFromFiles() expects at least 2 arguments (filePaths, method, ...)");
        
        if (args[0].Type != ValueType.Array)
            throw new Exception("embedFromFiles() first argument must be an array of file paths");
        
        if (args[1].Type != ValueType.String)
            throw new Exception("embedFromFiles() second argument must be a string (method name)");
        
        var filePaths = args[0].AsArray();
        var method = args[1].AsString().ToLowerInvariant();
        
        var results = new List<RuntimeValue>();
        
        foreach (var filePathValue in filePaths)
        {
            if (filePathValue.Type != ValueType.String)
                continue;
            
            var filePath = filePathValue.AsString();
            if (!File.Exists(filePath))
                continue;
            
            var newArgs = new List<RuntimeValue> { filePathValue, args[1] };
            for (int i = 2; i < args.Count; i++)
            {
                newArgs.Add(args[i]);
            }
            
            try
            {
                var embedding = BuiltInEmbedFromFile(newArgs);
                results.Add(embedding);
            }
            catch
            {
                // Skip files that can't be read
            }
        }
        
        return RuntimeValue.Array(results);
    }
}
