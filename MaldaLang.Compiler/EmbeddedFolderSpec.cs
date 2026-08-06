// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Compiler;

/// <summary>
/// A host folder staged into a compiled executable as <c>embed:&lt;Alias&gt;/...</c> resources.
/// </summary>
public sealed class EmbeddedFolderSpec
{
    public EmbeddedFolderSpec(string path, string alias)
    {
        Path = path;
        Alias = alias;
    }

    public string Path { get; }
    public string Alias { get; }
}
