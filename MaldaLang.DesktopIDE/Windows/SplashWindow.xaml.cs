// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace MaldaLang.DesktopIDE.Windows;

public partial class SplashWindow : Window
{
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _dismissed;

    public SplashWindow()
    {
        InitializeComponent();
        SplashView.Status = "Loading…";
        MouseLeftButtonDown += (_, _) => Dismiss();
        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Enter or Key.Space)
            {
                Dismiss();
            }
        };
    }

    public bool Dismissed => _dismissed;

    public Task ClosedTask => _closed.Task;

    public void Dismiss()
    {
        if (_dismissed)
        {
            return;
        }

        _dismissed = true;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160))
        {
            FillBehavior = FillBehavior.HoldEnd
        };
        fade.Completed += (_, _) =>
        {
            if (IsLoaded)
            {
                Close();
            }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        _dismissed = true;
        _closed.TrySetResult();
        base.OnClosed(e);
    }
}
