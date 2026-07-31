// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.IDE.Services;

using MaldaLang.IDE.Models;

public class CodeDiffService
{
    public DiffResult GenerateDiff(string originalCode, string suggestedCode)
    {
        var result = new DiffResult();
        
        var originalLines = originalCode.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).ToList();
        var suggestedLines = suggestedCode.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None).ToList();
        
        // Simple line-by-line diff using longest common subsequence approach
        var diff = ComputeDiff(originalLines, suggestedLines);
        
        int origLineNum = 1;
        int newLineNum = 1;
        
        foreach (var op in diff)
        {
            switch (op.Type)
            {
                case DiffOperationType.Keep:
                    result.Lines.Add(new DiffLine
                    {
                        Type = DiffLineType.Unchanged,
                        OriginalLineNumber = origLineNum,
                        NewLineNumber = newLineNum,
                        OriginalContent = op.OriginalLine,
                        NewContent = op.NewLine
                    });
                    origLineNum++;
                    newLineNum++;
                    break;
                    
                case DiffOperationType.Add:
                    result.Lines.Add(new DiffLine
                    {
                        Type = DiffLineType.Added,
                        NewLineNumber = newLineNum,
                        NewContent = op.NewLine
                    });
                    newLineNum++;
                    break;
                    
                case DiffOperationType.Remove:
                    result.Lines.Add(new DiffLine
                    {
                        Type = DiffLineType.Removed,
                        OriginalLineNumber = origLineNum,
                        OriginalContent = op.OriginalLine
                    });
                    origLineNum++;
                    break;
                    
                case DiffOperationType.Modify:
                    result.Lines.Add(new DiffLine
                    {
                        Type = DiffLineType.Modified,
                        OriginalLineNumber = origLineNum,
                        NewLineNumber = newLineNum,
                        OriginalContent = op.OriginalLine,
                        NewContent = op.NewLine
                    });
                    origLineNum++;
                    newLineNum++;
                    break;
            }
        }
        
        return result;
    }
    
    public string ApplyDiff(string originalCode, DiffResult diff)
    {
        var lines = new List<string>();
        
        foreach (var diffLine in diff.Lines)
        {
            if (diffLine.Type == DiffLineType.Unchanged || diffLine.Type == DiffLineType.Modified)
            {
                if (diffLine.NewContent != null)
                    lines.Add(diffLine.NewContent);
            }
            else if (diffLine.Type == DiffLineType.Added)
            {
                if (diffLine.NewContent != null)
                    lines.Add(diffLine.NewContent);
            }
            // Removed lines are skipped
        }
        
        return string.Join("\n", lines);
    }
    
    private List<DiffOperation> ComputeDiff(List<string> original, List<string> suggested)
    {
        // Use dynamic programming to find longest common subsequence
        int m = original.Count;
        int n = suggested.Count;
        
        var dp = new int[m + 1, n + 1];
        
        // Build LCS table
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (original[i - 1] == suggested[j - 1])
                {
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                }
                else
                {
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }
        }
        
        // Reconstruct diff
        var operations = new List<DiffOperation>();
        int i1 = m, j1 = n;
        
        while (i1 > 0 || j1 > 0)
        {
            if (i1 > 0 && j1 > 0 && original[i1 - 1] == suggested[j1 - 1])
            {
                operations.Insert(0, new DiffOperation
                {
                    Type = DiffOperationType.Keep,
                    OriginalLine = original[i1 - 1],
                    NewLine = suggested[j1 - 1]
                });
                i1--;
                j1--;
            }
            else if (j1 > 0 && (i1 == 0 || dp[i1, j1 - 1] >= dp[i1 - 1, j1]))
            {
                operations.Insert(0, new DiffOperation
                {
                    Type = DiffOperationType.Add,
                    NewLine = suggested[j1 - 1]
                });
                j1--;
            }
            else if (i1 > 0 && (j1 == 0 || dp[i1 - 1, j1] >= dp[i1, j1 - 1]))
            {
                operations.Insert(0, new DiffOperation
                {
                    Type = DiffOperationType.Remove,
                    OriginalLine = original[i1 - 1]
                });
                i1--;
            }
            else
            {
                // Modified line
                operations.Insert(0, new DiffOperation
                {
                    Type = DiffOperationType.Modify,
                    OriginalLine = original[i1 - 1],
                    NewLine = suggested[j1 - 1]
                });
                i1--;
                j1--;
            }
        }
        
        return operations;
    }
    
    private class DiffOperation
    {
        public DiffOperationType Type { get; set; }
        public string? OriginalLine { get; set; }
        public string? NewLine { get; set; }
    }
    
    private enum DiffOperationType
    {
        Keep,
        Add,
        Remove,
        Modify
    }
}