// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class IdePromptWindow : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public IdePromptWindow(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = string.IsNullOrWhiteSpace(title) ? "MALDA" : title;
        MessageText.Text = message ?? "";
        Resources["AccentBorderBrush"] = AccentBrush(image);
        BuildButtons(buttons);
        Closing += (_, _) =>
        {
            if (Result == MessageBoxResult.None)
            {
                Result = buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel
                    ? MessageBoxResult.Cancel
                    : MessageBoxResult.OK;
            }
        };
    }

    public void ApplyAccent(MessageBoxImage image)
    {
        Resources["AccentBorderBrush"] = AccentBrush(image);
    }

    public static MessageBoxResult Show(
        string message,
        string title = "MALDA",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        return Show(ResolveOwner(), message, title, buttons, image);
    }

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var window = new IdePromptWindow(message, title, buttons, image);
        var host = owner ?? ResolveOwner();
        if (host != null && !ReferenceEquals(host, window))
        {
            window.Owner = host;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            DialogTheming.CopyChrome(host, window);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
        }

        window.ApplyAccent(image);
        window.ShowDialog();
        return window.Result;
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, primary: true);
                AddButton("Cancel", MessageBoxResult.Cancel, primary: false);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Yes", MessageBoxResult.Yes, primary: true);
                AddButton("No", MessageBoxResult.No, primary: false);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Yes", MessageBoxResult.Yes, primary: true);
                AddButton("No", MessageBoxResult.No, primary: false);
                AddButton("Cancel", MessageBoxResult.Cancel, primary: false);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, primary: true);
                break;
        }
    }

    private void AddButton(string label, MessageBoxResult result, bool primary)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 84,
            IsDefault = primary,
            IsCancel = result == MessageBoxResult.Cancel,
            Style = TryFindResource(primary ? "ToolbarPrimaryButton" : "ToolbarButton") as Style
        };
        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
        };
        ButtonBar.Children.Add(button);
    }

    private Brush AccentBrush(MessageBoxImage image)
    {
        var key = image switch
        {
            MessageBoxImage.Error or MessageBoxImage.Stop or MessageBoxImage.Hand => "ErrorBrush",
            MessageBoxImage.Warning or MessageBoxImage.Exclamation => "WarningBrush",
            _ => "InfoBrush"
        };
        return TryFindResource(key) as Brush ?? Brushes.DodgerBlue;
    }

    private static Window? ResolveOwner()
    {
        if (Application.Current == null)
        {
            return null;
        }

        foreach (Window candidate in Application.Current.Windows)
        {
            if (candidate.IsActive)
            {
                return candidate;
            }
        }

        return Application.Current.MainWindow;
    }
}
