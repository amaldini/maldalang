// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Text;

/// <summary>Inverted-index BM25 scorer for GraphMemory hybrid retrieval.</summary>
internal sealed class MemoryBm25Index
{
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly Dictionary<string, List<string>> _postings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _documentFrequency = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _termFrequencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _documentLengths = new(StringComparer.OrdinalIgnoreCase);
    private int _documentCount;
    private double _averageDocumentLength;

    public void Clear()
    {
        _postings.Clear();
        _documentFrequency.Clear();
        _termFrequencies.Clear();
        _documentLengths.Clear();
        _documentCount = 0;
        _averageDocumentLength = 0;
    }

    public void IndexDocument(string nodeId, string text)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return;
        RemoveDocument(nodeId);
        var terms = Tokenize(text);
        if (terms.Count == 0)
            return;

        var termFreq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var term in terms)
            termFreq[term] = termFreq.GetValueOrDefault(term) + 1;

        _termFrequencies[nodeId] = termFreq;
        _documentLengths[nodeId] = terms.Count;
        _documentCount++;
        RecalculateAverageLength();

        foreach (var kvp in termFreq)
        {
            var term = kvp.Key;
            if (!_postings.TryGetValue(term, out var docs))
            {
                docs = new List<string>();
                _postings[term] = docs;
            }
            if (!docs.Contains(nodeId, StringComparer.Ordinal))
                docs.Add(nodeId);
            _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
        }
    }

    public void RemoveDocument(string nodeId)
    {
        if (!_termFrequencies.Remove(nodeId, out var termFreq))
            return;

        _documentLengths.Remove(nodeId);
        _documentCount = Math.Max(0, _documentCount - 1);
        RecalculateAverageLength();

        foreach (var term in termFreq.Keys)
        {
            if (_postings.TryGetValue(term, out var docs))
            {
                docs.RemoveAll(id => string.Equals(id, nodeId, StringComparison.Ordinal));
                if (docs.Count == 0)
                    _postings.Remove(term);
            }
            if (_documentFrequency.TryGetValue(term, out var df))
            {
                df--;
                if (df <= 0)
                    _documentFrequency.Remove(term);
                else
                    _documentFrequency[term] = df;
            }
        }
    }

    public double Score(string query, string nodeId)
    {
        if (_documentCount == 0 || !_termFrequencies.ContainsKey(nodeId))
            return 0.0;

        var queryTerms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (queryTerms.Count == 0)
            return 0.0;

        var termFreq = _termFrequencies[nodeId];
        var docLen = _documentLengths[nodeId];
        var avgLen = _averageDocumentLength <= 0 ? docLen : _averageDocumentLength;
        double score = 0.0;

        foreach (var term in queryTerms)
        {
            if (!termFreq.TryGetValue(term, out var tf))
                continue;
            var df = _documentFrequency.GetValueOrDefault(term);
            var idf = Math.Log(((_documentCount - df) + 0.5) / (df + 0.5) + 1.0);
            var denom = tf + K1 * (1.0 - B + B * (docLen / avgLen));
            score += idf * ((tf * (K1 + 1.0)) / denom);
        }

        return score;
    }

    public Dictionary<string, double> ScoreQuery(string query, ISet<string>? allowedNodeIds = null)
    {
        var results = new Dictionary<string, double>(StringComparer.Ordinal);
        if (_documentCount == 0)
            return results;

        var queryTerms = Tokenize(query).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (queryTerms.Count == 0)
            return results;

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in queryTerms)
        {
            if (!_postings.TryGetValue(term, out var docs))
                continue;
            foreach (var nodeId in docs)
            {
                if (allowedNodeIds == null || allowedNodeIds.Contains(nodeId))
                    candidateIds.Add(nodeId);
            }
        }

        foreach (var nodeId in candidateIds)
        {
            var score = Score(query, nodeId);
            if (score > 0)
                results[nodeId] = score;
        }

        return results;
    }

    public static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return tokens;

        var current = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '/' || ch == ':' || ch == '.')
                current.Append(ch);
            else if (current.Length > 0)
            {
                if (current.Length >= 2)
                    tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length >= 2)
            tokens.Add(current.ToString());

        return tokens;
    }

    private void RecalculateAverageLength()
    {
        if (_documentCount == 0)
        {
            _averageDocumentLength = 0;
            return;
        }
        _averageDocumentLength = _documentLengths.Values.Sum() / (double)_documentCount;
    }
}
