// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;

namespace MaldaLang.DesktopIDE.Windows;

public partial class IdeSplashView : UserControl
{
    public IdeSplashView()
    {
        InitializeComponent();
        var version = typeof(IdeSplashView).Assembly.GetName().Version;
        if (version != null)
        {
            var build = version.Build < 0 ? 0 : version.Build;
            VersionText.Text = $"Version {version.Major}.{version.Minor}.{build}";
        }
    }

    public string Status
    {
        get => StatusText.Text;
        set
        {
            StatusText.Text = value ?? "";
            StatusText.Visibility = string.IsNullOrWhiteSpace(StatusText.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
