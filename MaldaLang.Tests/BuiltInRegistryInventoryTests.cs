using MaldaLang.BuiltIns;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Phase 0: keep <see cref="BuiltInRegistry"/> and docs/planning/core-builtin-inventory.txt in sync.
/// </summary>
public class BuiltInRegistryInventoryTests
{
    [Fact]
    public void CoreBuiltinInventory_MatchesBuiltInRegistrySource()
    {
        var registry = BuiltInRegistryInventoryLoader.LoadSymbolsFromRegistrySource();
        var inventory = BuiltInRegistryInventoryLoader.LoadSymbolsFromCoreInventory();

        var onlyInRegistry = registry.Except(inventory).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var onlyInInventory = inventory.Except(registry).OrderBy(s => s, StringComparer.Ordinal).ToList();

        Assert.True(
            onlyInRegistry.Count == 0 && onlyInInventory.Count == 0,
            $"Registry/inventory drift. Only in BuiltInRegistry.cs: [{string.Join(", ", onlyInRegistry)}]. " +
            $"Only in core-builtin-inventory.txt: [{string.Join(", ", onlyInInventory)}]. " +
            "Regenerate with scripts/generate-core-builtin-inventory.ps1");
    }

    [Fact]
    public void CoreBuiltinInventory_HasExpectedScale()
    {
        var inventory = BuiltInRegistryInventoryLoader.LoadSymbolsFromCoreInventory();
        Assert.InRange(inventory.Count, 280, 350);
    }

    [Fact]
    public void BuiltInRegistry_IsQueryableForInventorySample()
    {
        foreach (var symbol in new[] { "print", "typeOf", "abs", "loadNativeModule" })
        {
            Assert.NotNull(BuiltInRegistry.GetDescriptor(symbol));
            Assert.True(BuiltInRegistry.IsInterpreterBuiltIn(symbol), symbol);
        }
    }
}
