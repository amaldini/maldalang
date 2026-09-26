// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Services;

public sealed class PaletteCommand
{
    public required string Title { get; init; }
    public string Shortcut { get; init; } = "";
    public required Action Invoke { get; init; }

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return Title.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
