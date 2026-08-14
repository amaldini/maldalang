// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>
/// Declared <c>@budget</c> limits for a function or prompt. Omitted keys are unbounded.
/// </summary>
public sealed class ResourceBudget
{
    public int? MaxTokens { get; }
    public int? MaxTools { get; }
    public double? MaxCost { get; }

    public bool HasAnyBound =>
        MaxTokens is > 0 || MaxTools is > 0 || MaxCost is > 0;

    public ResourceBudget(int? maxTokens = null, int? maxTools = null, double? maxCost = null)
    {
        MaxTokens = maxTokens is > 0 ? maxTokens : null;
        MaxTools = maxTools is > 0 ? maxTools : null;
        MaxCost = maxCost is > 0 ? maxCost : null;
    }
}
