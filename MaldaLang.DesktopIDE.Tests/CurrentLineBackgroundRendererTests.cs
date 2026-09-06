// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using Xunit;

namespace MaldaLang.DesktopIDE.Tests;

public class CurrentLineBackgroundRendererTests
{
    [Fact]
    public void ToViewportY_SubtractsScrollOffset()
    {
        // After ScrollToLine the document Y is far below the viewport.
        // Drawing at the raw document Y paints the highlight off-screen.
        Assert.Equal(40, CurrentLineBackgroundRenderer.ToViewportY(840, 800));
        Assert.Equal(0, CurrentLineBackgroundRenderer.ToViewportY(0, 0));
    }
}
