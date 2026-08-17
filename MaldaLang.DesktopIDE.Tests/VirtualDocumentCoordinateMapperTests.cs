// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using MaldaLang.DesktopIDE.Services;
using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class VirtualDocumentCoordinateMapperTests
{
    [Fact]
    public void EditorLineOne_MapsToPhysicalLineAfterSectionStart()
    {
        const int virtualStart = 10;
        var physical = VirtualDocumentCoordinateMapper.ToPhysicalLine(1, virtualStart);
        Assert.Equal(11, physical);
        Assert.Equal(1, VirtualDocumentCoordinateMapper.ToEditorLine(physical, virtualStart));
    }

    [Fact]
    public void ContainsPhysicalLine_UsesZeroBasedSectionRange()
    {
        Assert.True(VirtualDocumentCoordinateMapper.ContainsPhysicalLine(11, 10, 20));
        Assert.False(VirtualDocumentCoordinateMapper.ContainsPhysicalLine(10, 10, 20));
        Assert.True(VirtualDocumentCoordinateMapper.ContainsPhysicalLine(21, 10, 20));
        Assert.False(VirtualDocumentCoordinateMapper.ContainsPhysicalLine(22, 10, 20));
    }

    [Fact]
    public void DiagnosticLines_RemapToSectionLocal()
    {
        Assert.True(VirtualDocumentCoordinateMapper.ContainsDiagnosticLine(10, 10, 20));
        Assert.Equal(0, VirtualDocumentCoordinateMapper.ToSectionLocalDiagnosticLine(10, 10));
        Assert.Equal(5, VirtualDocumentCoordinateMapper.ToSectionLocalDiagnosticLine(15, 10));
    }
}
