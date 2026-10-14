// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Text.RegularExpressions;
using MaldaLang.BuiltIns;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// A built-in is declared in <see cref="BuiltInRegistry"/> and implemented by one arm of
/// <c>BuiltInFunctions.DispatchBuiltIn</c> (plus an arm in <c>CallBuiltInAsync</c> only when
/// the implementation awaits). The C# transpiler routes every registry name it has no
/// special emit for through that same dispatch, so a missing arm is a runtime
/// "Unknown built-in function" on the interpreter and in transpiled programs alike.
/// </summary>
public class BuiltInSurfaceGuardTests
{
    /// <summary>Registry names implemented as array members, not by the dispatch table.</summary>
    private static readonly HashSet<string> MemberOnlyBuiltIns = new(StringComparer.Ordinal)
    {
        "append", "pop", "shift"
    };

    /// <summary>
    /// Dispatch arms reached only through a namespace (<c>nn.*</c>) or the browser runtime
    /// (<c>dom*</c>), so they are deliberately not flat registry names.
    /// </summary>
    private static readonly HashSet<string> DispatchOnlyBuiltIns = new(StringComparer.Ordinal)
    {
        "crossEntropyFromLogits",
        "domAppend", "domClear", "domCreate", "domHtml", "domOn", "domQuery", "domSetText"
    };

    private static readonly Regex ArmPattern = new(
        @"^\s+""(?<name>[a-zA-Z][a-zA-Z0-9_]*)""\s*(?:or\s*$|=>)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void EveryRegistryBuiltIn_HasADispatchArm()
    {
        var dispatched = LoadDispatchArms().Keys.ToHashSet(StringComparer.Ordinal);
        var missing = BuiltInRegistryInventoryLoader.LoadSymbolsFromRegistrySource()
            .Where(name => !dispatched.Contains(name) && !MemberOnlyBuiltIns.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Registered in BuiltInRegistry but not implemented in BuiltInFunctions.DispatchBuiltIn: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void EveryDispatchArm_IsRegisteredOrExplicitlyNamespaceOnly()
    {
        var registry = BuiltInRegistryInventoryLoader.LoadSymbolsFromRegistrySource();
        var unregistered = LoadDispatchArms().Keys
            .Where(name => !registry.Contains(name) && !DispatchOnlyBuiltIns.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unregistered.Count == 0,
            "Dispatched but missing from BuiltInRegistry.GetDescriptor (the interpreter and transpiler "
            + "will not treat them as built-ins): " + string.Join(", ", unregistered)
            + ". Register them, or list them in DispatchOnlyBuiltIns with the reason.");
    }

    [Fact]
    public void AsyncOnlyBuiltIns_AreNotMarkedSynchronousForCodegen()
    {
        // The transpiler emits CallBuiltIn (sync) for IsAlwaysSynchronousForCodegen names.
        // A name whose sync path throws or does not exist must therefore stay async.
        var arms = LoadDispatchArms();
        var wrong = arms
            .Where(arm => arm.Value is DispatchKind.AsyncOnly)
            .Select(arm => arm.Key)
            .Where(name => BuiltInRegistry.GetDescriptor(name)?.IsAlwaysSynchronousForCodegen == true)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            wrong.Count == 0,
            "Async-only built-ins marked IsAlwaysSynchronousForCodegen: " + string.Join(", ", wrong));
    }

    private enum DispatchKind
    {
        Sync,
        AsyncOnly
    }

    /// <summary>Arm names of the sync table and of the async-only overrides.</summary>
    private static Dictionary<string, DispatchKind> LoadDispatchArms()
    {
        var path = PlanningPaths.ResolveRepoFile("MaldaLang", "BuiltIns", "BuiltInFunctions.cs");
        var text = File.ReadAllText(path);

        var syncBody = SwitchBody(text, "RuntimeValue DispatchBuiltIn(string name", "_ => throw new Exception($\"Unknown built-in function");
        var asyncBody = SwitchBody(text, "Task<RuntimeValue> CallBuiltInAsync(string name", "_ => DispatchBuiltIn(");

        var arms = new Dictionary<string, DispatchKind>(StringComparer.Ordinal);
        foreach (var line in syncBody.Split('\n'))
        {
            var match = ArmPattern.Match(line);
            if (!match.Success)
                continue;
            var asyncOnly = line.Contains("must be called via CallBuiltInAsync", StringComparison.Ordinal);
            arms[match.Groups["name"].Value] = asyncOnly ? DispatchKind.AsyncOnly : DispatchKind.Sync;
        }

        foreach (Match match in ArmPattern.Matches(asyncBody))
        {
            arms.TryAdd(match.Groups["name"].Value, DispatchKind.AsyncOnly);
        }

        Assert.InRange(arms.Count, 280, 500);
        return arms;
    }

    private static string SwitchBody(string text, string signature, string defaultArm)
    {
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not locate '{signature}' in BuiltInFunctions.cs.");
        var end = text.IndexOf(defaultArm, start, StringComparison.Ordinal);
        Assert.True(end >= 0, $"Could not locate the default arm of '{signature}'.");
        return text[start..end];
    }
}
