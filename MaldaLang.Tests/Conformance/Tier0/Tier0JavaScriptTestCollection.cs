// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Tests.Conformance.Tier0;

/// <summary>
/// Tier 0 JavaScript conformance spawns Node.js child processes; run serially to avoid
/// parallel testhost hangs and idle blame timeouts under full-suite load.
/// </summary>
[CollectionDefinition("Tier0JavaScriptSerial", DisableParallelization = true)]
public sealed class Tier0JavaScriptTestCollection
{
}
