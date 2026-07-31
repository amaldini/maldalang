// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.PackageManager.Models;

namespace MaldaLang.Tests;

public class PackageVersionTests
{
    [Fact]
    public void Parse_ValidVersion_ReturnsCorrectVersion()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Null(version.Prerelease);
        Assert.Null(version.Build);
    }
    
    [Fact]
    public void Parse_WithPrerelease_ReturnsCorrectVersion()
    {
        var version = PackageVersion.Parse("1.2.3-alpha");
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Equal("alpha", version.Prerelease);
        Assert.Null(version.Build);
    }
    
    [Fact]
    public void Parse_WithBuild_ReturnsCorrectVersion()
    {
        var version = PackageVersion.Parse("1.2.3+build.123");
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Null(version.Prerelease);
        Assert.Equal("build.123", version.Build);
    }
    
    [Fact]
    public void Parse_WithPrereleaseAndBuild_ReturnsCorrectVersion()
    {
        var version = PackageVersion.Parse("1.2.3-beta.1+build.456");
        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
        Assert.Equal("beta.1", version.Prerelease);
        Assert.Equal("build.456", version.Build);
    }
    
    [Fact]
    public void Parse_InvalidVersion_ThrowsException()
    {
        Assert.Throws<ArgumentException>(() => PackageVersion.Parse("invalid"));
        Assert.Throws<ArgumentException>(() => PackageVersion.Parse("1.2"));
        Assert.Throws<ArgumentException>(() => PackageVersion.Parse("1"));
        Assert.Throws<ArgumentException>(() => PackageVersion.Parse(""));
    }
    
    [Fact]
    public void TryParse_ValidVersion_ReturnsTrue()
    {
        var result = PackageVersion.TryParse("1.2.3", out var version);
        Assert.True(result);
        Assert.NotNull(version);
        Assert.Equal(1, version!.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
    }
    
    [Fact]
    public void TryParse_InvalidVersion_ReturnsFalse()
    {
        var result = PackageVersion.TryParse("invalid", out var version);
        Assert.False(result);
        Assert.Null(version);
    }
    
    [Fact]
    public void CompareTo_SameVersion_ReturnsZero()
    {
        var v1 = PackageVersion.Parse("1.2.3");
        var v2 = PackageVersion.Parse("1.2.3");
        Assert.Equal(0, v1.CompareTo(v2));
        Assert.True(v1.Equals(v2));
    }
    
    [Fact]
    public void CompareTo_DifferentMajor_ReturnsCorrect()
    {
        var v1 = PackageVersion.Parse("1.0.0");
        var v2 = PackageVersion.Parse("2.0.0");
        Assert.True(v1 < v2);
        Assert.True(v2 > v1);
        Assert.True(v1 <= v2);
        Assert.True(v2 >= v1);
    }
    
    [Fact]
    public void CompareTo_DifferentMinor_ReturnsCorrect()
    {
        var v1 = PackageVersion.Parse("1.1.0");
        var v2 = PackageVersion.Parse("1.2.0");
        Assert.True(v1 < v2);
        Assert.True(v2 > v1);
    }
    
    [Fact]
    public void CompareTo_DifferentPatch_ReturnsCorrect()
    {
        var v1 = PackageVersion.Parse("1.2.2");
        var v2 = PackageVersion.Parse("1.2.3");
        Assert.True(v1 < v2);
        Assert.True(v2 > v1);
    }
    
    [Fact]
    public void CompareTo_PrereleaseIsLessThanRelease()
    {
        var prerelease = PackageVersion.Parse("1.2.3-alpha");
        var release = PackageVersion.Parse("1.2.3");
        Assert.True(prerelease < release);
        Assert.True(release > prerelease);
    }
    
    [Fact]
    public void Satisfies_ExactMatch_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies("1.2.3"));
        Assert.True(version.Satisfies("=1.2.3"));
    }
    
    [Fact]
    public void Satisfies_ExactMatch_ReturnsFalse()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.False(version.Satisfies("1.2.4"));
        Assert.False(version.Satisfies("=1.2.4"));
    }
    
    [Fact]
    public void Satisfies_CaretRange_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies("^1.0.0"));
        Assert.True(version.Satisfies("^1.2.0"));
        Assert.True(version.Satisfies("^1.2.3"));
    }
    
    [Fact]
    public void Satisfies_CaretRange_ReturnsFalse()
    {
        var version = PackageVersion.Parse("2.0.0");
        Assert.False(version.Satisfies("^1.0.0"));
    }
    
    [Fact]
    public void Satisfies_TildeRange_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies("~1.2.0"));
        Assert.True(version.Satisfies("~1.2.3"));
    }
    
    [Fact]
    public void Satisfies_TildeRange_ReturnsFalse()
    {
        var version = PackageVersion.Parse("1.3.0");
        Assert.False(version.Satisfies("~1.2.0"));
    }
    
    [Fact]
    public void Satisfies_GreaterThanOrEqual_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies(">=1.0.0"));
        Assert.True(version.Satisfies(">=1.2.0"));
        Assert.True(version.Satisfies(">=1.2.3"));
    }
    
    [Fact]
    public void Satisfies_GreaterThanOrEqual_ReturnsFalse()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.False(version.Satisfies(">=1.3.0"));
    }
    
    [Fact]
    public void Satisfies_LessThanOrEqual_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies("<=1.3.0"));
        Assert.True(version.Satisfies("<=1.2.3"));
    }
    
    [Fact]
    public void Satisfies_LessThanOrEqual_ReturnsFalse()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.False(version.Satisfies("<=1.2.2"));
    }
    
    [Fact]
    public void Satisfies_GreaterThan_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies(">1.0.0"));
        Assert.True(version.Satisfies(">1.2.2"));
    }
    
    [Fact]
    public void Satisfies_GreaterThan_ReturnsFalse()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.False(version.Satisfies(">1.2.3"));
        Assert.False(version.Satisfies(">1.3.0"));
    }
    
    [Fact]
    public void Satisfies_LessThan_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies("<1.3.0"));
        Assert.True(version.Satisfies("<2.0.0"));
    }
    
    [Fact]
    public void Satisfies_LessThan_ReturnsFalse()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.False(version.Satisfies("<1.2.3"));
        Assert.False(version.Satisfies("<1.2.0"));
    }
    
    [Fact]
    public void Satisfies_EmptyRange_ReturnsTrue()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.True(version.Satisfies(""));
        Assert.True(version.Satisfies("   "));
    }
    
    [Fact]
    public void ToString_ReturnsCorrectFormat()
    {
        var version = PackageVersion.Parse("1.2.3");
        Assert.Equal("1.2.3", version.ToString());
    }
    
    [Fact]
    public void ToString_WithPrerelease_ReturnsCorrectFormat()
    {
        var version = PackageVersion.Parse("1.2.3-alpha");
        Assert.Equal("1.2.3-alpha", version.ToString());
    }
    
    [Fact]
    public void ToString_WithBuild_ReturnsCorrectFormat()
    {
        var version = PackageVersion.Parse("1.2.3+build.123");
        Assert.Equal("1.2.3+build.123", version.ToString());
    }
    
    [Fact]
    public void ToString_WithPrereleaseAndBuild_ReturnsCorrectFormat()
    {
        var version = PackageVersion.Parse("1.2.3-beta.1+build.456");
        Assert.Equal("1.2.3-beta.1+build.456", version.ToString());
    }
}
