using MaldaLang.BuiltIns;
using MaldaLang.Tests.Planning;
using Xunit;

namespace MaldaLang.Tests;

/// <summary>
/// Phase 0 guard: symbols moved to optional packs must not re-enter <see cref="BuiltInRegistry"/>.
/// </summary>
public class OptionalPackRegistryGuardTests
{
    public static IEnumerable<object[]> ForbiddenPackSymbols()
    {
        foreach (var symbol in BuiltInRegistryInventoryLoader.LoadForbiddenPackSymbols())
            yield return new object[] { symbol };
    }

    [Theory]
    [MemberData(nameof(ForbiddenPackSymbols))]
    public void BuiltInRegistry_DoesNotRegisterOptionalPackSymbol(string symbol)
    {
        Assert.Null(BuiltInRegistry.GetDescriptor(symbol));
        Assert.False(BuiltInRegistry.IsInterpreterBuiltIn(symbol));
        Assert.False(BuiltInRegistry.IsTranspilerBuiltIn(symbol));
    }

    [Fact]
    public void ForbiddenPackInventory_ContainsExpectedMinimumSymbols()
    {
        var symbols = BuiltInRegistryInventoryLoader.LoadForbiddenPackSymbols().ToList();
        Assert.Contains("sma", symbols);
        Assert.Contains("createIndicatorEngine", symbols);
        Assert.Contains("ta", symbols);
        Assert.True(symbols.Count >= 10);
    }
}
