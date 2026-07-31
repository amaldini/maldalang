// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.PackageManager.Models;

using System;
using System.Text.RegularExpressions;

public class PackageVersion : IComparable<PackageVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? Prerelease { get; }
    public string? Build { get; }
    
    private static readonly Regex VersionRegex = new Regex(
        @"^(\d+)\.(\d+)\.(\d+)(?:-([\w\.-]+))?(?:\+([\w\.-]+))?$",
        RegexOptions.Compiled
    );
    
    public PackageVersion(int major, int minor, int patch, string? prerelease = null, string? build = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
        Build = build;
    }
    
    public static PackageVersion Parse(string versionString)
    {
        var match = VersionRegex.Match(versionString);
        if (!match.Success)
        {
            throw new ArgumentException($"Invalid version string: {versionString}");
        }
        
        var major = int.Parse(match.Groups[1].Value);
        var minor = int.Parse(match.Groups[2].Value);
        var patch = int.Parse(match.Groups[3].Value);
        var prerelease = match.Groups[4].Success ? match.Groups[4].Value : null;
        var build = match.Groups[5].Success ? match.Groups[5].Value : null;
        
        return new PackageVersion(major, minor, patch, prerelease, build);
    }
    
    public static bool TryParse(string versionString, out PackageVersion? version)
    {
        try
        {
            version = Parse(versionString);
            return true;
        }
        catch
        {
            version = null;
            return false;
        }
    }
    
    public bool Satisfies(string versionRange)
    {
        // Simple semver range checking
        // Supports: ^1.0.0, ~1.0.0, >=1.0.0, <=1.0.0, =1.0.0, 1.0.0
        if (string.IsNullOrEmpty(versionRange))
            return true;
            
        versionRange = versionRange.Trim();
        
        // After trimming, check if it's empty (whitespace-only strings become empty after trim)
        if (string.IsNullOrEmpty(versionRange))
            return true;
        
        // Exact match
        if (versionRange.StartsWith("="))
        {
            var exactVersion = Parse(versionRange.Substring(1));
            return Equals(exactVersion);
        }
        
        // Caret range (^1.0.0 = >=1.0.0 <2.0.0)
        if (versionRange.StartsWith("^"))
        {
            var baseVersion = Parse(versionRange.Substring(1));
            return this >= baseVersion && this < new PackageVersion(baseVersion.Major + 1, 0, 0);
        }
        
        // Tilde range (~1.0.0 = >=1.0.0 <1.1.0)
        if (versionRange.StartsWith("~"))
        {
            var baseVersion = Parse(versionRange.Substring(1));
            return this >= baseVersion && this < new PackageVersion(baseVersion.Major, baseVersion.Minor + 1, 0);
        }
        
        // Greater than or equal
        if (versionRange.StartsWith(">="))
        {
            var minVersion = Parse(versionRange.Substring(2));
            return this >= minVersion;
        }
        
        // Less than or equal
        if (versionRange.StartsWith("<="))
        {
            var maxVersion = Parse(versionRange.Substring(2));
            return this <= maxVersion;
        }
        
        // Greater than
        if (versionRange.StartsWith(">"))
        {
            var minVersion = Parse(versionRange.Substring(1));
            return this > minVersion;
        }
        
        // Less than
        if (versionRange.StartsWith("<"))
        {
            var maxVersion = Parse(versionRange.Substring(1));
            return this < maxVersion;
        }
        
        // Default: exact match
        try
        {
            var exactVersion = Parse(versionRange);
            return Equals(exactVersion);
        }
        catch
        {
            return false;
        }
    }
    
    public int CompareTo(PackageVersion? other)
    {
        if (other == null) return 1;
        
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        
        // Prerelease versions are less than release versions
        if (Prerelease == null && other.Prerelease != null) return 1;
        if (Prerelease != null && other.Prerelease == null) return -1;
        if (Prerelease != null && other.Prerelease != null)
        {
            return string.Compare(Prerelease, other.Prerelease, StringComparison.Ordinal);
        }
        
        return 0;
    }
    
    public static bool operator <(PackageVersion left, PackageVersion right)
    {
        return left.CompareTo(right) < 0;
    }
    
    public static bool operator >(PackageVersion left, PackageVersion right)
    {
        return left.CompareTo(right) > 0;
    }
    
    public static bool operator <=(PackageVersion left, PackageVersion right)
    {
        return left.CompareTo(right) <= 0;
    }
    
    public static bool operator >=(PackageVersion left, PackageVersion right)
    {
        return left.CompareTo(right) >= 0;
    }
    
    public override bool Equals(object? obj)
    {
        if (obj is PackageVersion other)
        {
            return Major == other.Major && 
                   Minor == other.Minor && 
                   Patch == other.Patch &&
                   Prerelease == other.Prerelease;
        }
        return false;
    }
    
    public override int GetHashCode()
    {
        return HashCode.Combine(Major, Minor, Patch, Prerelease);
    }
    
    public override string ToString()
    {
        var result = $"{Major}.{Minor}.{Patch}";
        if (!string.IsNullOrEmpty(Prerelease))
        {
            result += $"-{Prerelease}";
        }
        if (!string.IsNullOrEmpty(Build))
        {
            result += $"+{Build}";
        }
        return result;
    }
}
