// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.LanguageServer;

using MaldaLang.IDE;

/// <summary>
/// LSP-side type diagnostic severity. Mirrors VS Code <c>malda.types.strict</c> (default true).
/// </summary>
public sealed class MaldaLspTypeSettings
{
    /// <summary>
    /// When true (default), type mismatches and unknown hints are Errors.
    /// When false, uses <see cref="StrictTypesOptions.Lenient"/>.
    /// </summary>
    public bool TypeErrors { get; set; } = true;

    public StrictTypesOptions ToOptions() =>
        TypeErrors ? StrictTypesOptions.Default : StrictTypesOptions.Lenient;

    public void ApplyFromInitializationOptions(object? initializationOptions)
    {
        if (initializationOptions == null)
            return;

        try
        {
            if (initializationOptions is System.Text.Json.JsonElement element)
            {
                ApplyJsonElement(element);
                return;
            }

            // Newtonsoft / dynamic bags from OmniSharp
            var type = initializationOptions.GetType();
            var prop = type.GetProperty("typeStrict")
                       ?? type.GetProperty("TypeStrict")
                       ?? type.GetProperty("malda.types.strict");
            if (prop?.GetValue(initializationOptions) is bool b)
                TypeErrors = b;
        }
        catch
        {
            // Keep default
        }
    }

    public void ApplyConfigurationValue(bool? typeStrict)
    {
        if (typeStrict.HasValue)
            TypeErrors = typeStrict.Value;
    }

    private void ApplyJsonElement(System.Text.Json.JsonElement element)
    {
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object)
            return;

        if (element.TryGetProperty("typeStrict", out var prop) &&
            (prop.ValueKind == System.Text.Json.JsonValueKind.True ||
             prop.ValueKind == System.Text.Json.JsonValueKind.False))
        {
            TypeErrors = prop.GetBoolean();
        }
    }
}
