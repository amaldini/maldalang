using MaldaLang.BuiltIns;
using Xunit;

namespace MaldaLang.Tests;

public class BuiltInRegistryTests
{
    [Fact]
    public void BuiltInRegistry_DescriptorsProvideAuthoritativeCodegenMetadata()
    {
        var stringDescriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("string"));
        Assert.True(BuiltInRegistry.IsInterpreterBuiltIn("string"));
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("string"));
        Assert.True(stringDescriptor.IsAlwaysSynchronousForCodegen);

        var printDescriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("print"));
        Assert.True(BuiltInRegistry.IsTranspilerBuiltIn("print"));
        Assert.False(printDescriptor.IsAlwaysSynchronousForCodegen);

        var inputDescriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("input"));
        Assert.False(inputDescriptor.SupportsSync);
        Assert.True(inputDescriptor.SupportsAsync);
        Assert.False(inputDescriptor.IsAlwaysSynchronousForCodegen);

        var trimDescriptor = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("trim"));
        Assert.Equal(BuiltInTranspilerStrategy.SupportedByTranspiler, trimDescriptor.TranspilerStrategy);
        Assert.True(trimDescriptor.IsAlwaysSynchronousForCodegen);

        Assert.Null(BuiltInRegistry.GetDescriptor("sma"));
        Assert.False(BuiltInRegistry.IsInterpreterBuiltIn("sma"));
        Assert.False(BuiltInRegistry.IsTranspilerBuiltIn("sma"));
        Assert.Null(BuiltInRegistry.GetDescriptor("createIndicatorEngine"));

        Assert.Null(BuiltInRegistry.GetDescriptor("__missing_builtin__"));

        var setDefault = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("setDefaultAgent"));
        Assert.Equal(BuiltInTranspilerStrategy.SupportedByTranspiler, setDefault.TranspilerStrategy);
        Assert.False(setDefault.IsAlwaysSynchronousForCodegen);

        var editFile = Assert.IsType<BuiltInDescriptor>(BuiltInRegistry.GetDescriptor("editFile"));
        Assert.Equal(BuiltInTranspilerStrategy.SupportedByTranspiler, editFile.TranspilerStrategy);
        Assert.True(editFile.IsAlwaysSynchronousForCodegen);
    }
}
