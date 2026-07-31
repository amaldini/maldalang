// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;
using MaldaLang.Cli;
using System.IO;

namespace MaldaLang.Tests;

[Collection("Sequential")]
public class GatewayNotifierTests : TestBase
{
    [Fact]
    public void RecordCrash_and_TryReadCrashMarker_roundtrip()
    {
        var tempDir = CreateTempDirectory("gateway_notifier_");
        try
        {
            GatewayNotifier.RecordCrash(tempDir, "test failure");
            Assert.True(GatewayNotifier.TryReadCrashMarker(tempDir, out var marker));
            Assert.Equal("test failure", marker.Reason);
            Assert.False(string.IsNullOrWhiteSpace(marker.AtUtc));
            Assert.True(File.Exists(GatewayNotifier.GetAlertsLogPath(tempDir)));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public void ClearCrashMarker_removes_marker_file()
    {
        var tempDir = CreateTempDirectory("gateway_notifier_clear_");
        try
        {
            GatewayNotifier.RecordCrash(tempDir, "gone");
            GatewayNotifier.ClearCrashMarker(tempDir);
            Assert.False(GatewayNotifier.TryReadCrashMarker(tempDir, out _));
        }
        finally
        {
            SafeDeleteDirectory(tempDir);
        }
    }
}
