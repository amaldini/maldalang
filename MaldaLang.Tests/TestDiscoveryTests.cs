namespace MaldaLang.Tests;

using System.IO;
using MaldaLang.Testing;

public class TestDiscoveryTests : TestBase
{
    [Fact]
    public void Discover_ReturnsDeterministicSortedTestFiles()
    {
        var root = CreateTempDirectory("malda_discovery_");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "tests"));
            File.WriteAllText(Path.Combine(root, "tests", "b.spec.malda"), "print(\"b\");");
            File.WriteAllText(Path.Combine(root, "tests", "a.test.malda"), "print(\"a\");");
            File.WriteAllText(Path.Combine(root, "tests", "ignore.malda"), "print(\"x\");");

            var discovery = new TestDiscovery();
            var files = discovery.Discover(root);

            Assert.Equal(3, files.Count);
            Assert.EndsWith("a.test.malda", files[0], System.StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("b.spec.malda", files[1], System.StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("ignore.malda", files[2], System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void Discover_Filter_RestrictsResults()
    {
        var root = CreateTempDirectory("malda_discovery_filter_");
        try
        {
            File.WriteAllText(Path.Combine(root, "api.test.malda"), "print(\"api\");");
            File.WriteAllText(Path.Combine(root, "unit.spec.malda"), "print(\"unit\");");

            var discovery = new TestDiscovery();
            var files = discovery.Discover(root, "api");

            Assert.Single(files);
            Assert.EndsWith("api.test.malda", files[0], System.StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }
}
