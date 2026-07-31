using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.UserControls;

namespace MaldaLang.DesktopIDE.Windows;

public partial class FullScreenMessageWindow : Window
{
    private readonly ChatMessageModel _message;
    private readonly Action<string>? _onApplyCode;

    public FullScreenMessageWindow(ChatMessageModel message, Action<string>? onApplyCode = null)
    {
        InitializeComponent();
        _message = message;
        _onApplyCode = onApplyCode;

        TitleTextBlock.Text = message.IsUser ? "User Message" : "AI Message";
        SubtitleTextBlock.Text = message.Timestamp.ToString("yyyy-MM-dd HH:mm");

        BuildContent();

        KeyDown += FullScreenMessageWindow_KeyDown;
    }

    private void BuildContent()
    {
        // Header inside content: who + when
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var roleText = new TextBlock
        {
            Text = _message.IsUser ? "You" : "AI",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Foreground = GetResourceBrush("TextSecondaryBrush", Color.FromRgb(0x75, 0x75, 0x75))
        };
        headerPanel.Children.Add(roleText);

        var timeText = new TextBlock
        {
            Text = _message.Timestamp.ToString("HH:mm"),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            Foreground = GetResourceBrush("TextSecondaryBrush", Color.FromRgb(0x75, 0x75, 0x75))
        };
        headerPanel.Children.Add(timeText);

        ContentStackPanel.Children.Add(headerPanel);

        // Message body
        var bodyBorder = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = GetResourceBrush("BorderBrush", Color.FromRgb(0xD0, 0xD0, 0xD0)),
            Margin = new Thickness(0, 0, 0, 12)
        };

        if (_message.IsUser)
        {
            bodyBorder.Background = GetResourceBrush("PrimaryButtonBackgroundBrush", Color.FromRgb(0x21, 0x96, 0xF3));
        }
        else if (_message.IsError)
        {
            bodyBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
        }
        else
        {
            bodyBorder.Background = GetResourceBrush("ListBackgroundBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
        }

        try
        {
            var browser = new WebBrowser
            {
                Height = double.NaN,
                MinHeight = 40
            };

            string html;
            if (_message.IsError)
            {
                var escaped = System.Security.SecurityElement.Escape(_message.Content ?? string.Empty);
                html = $"<html><head><style>body {{ font-family: Consolas, monospace; background: transparent; color: #d32f2f; padding: 0; margin: 0; }}</style></head><body>{escaped}</body></html>";
            }
            else
            {
                // For now, reuse plain-text style; markdown-specific rendering can be added later if needed.
                var escaped = System.Security.SecurityElement.Escape(_message.Content ?? string.Empty);
                if (_message.IsUser)
                {
                    html = "<html><head><style>body { font-family: Consolas, monospace; " +
                           "background: #2196F3; color: #FFFFFF; padding: 0; margin: 0; " +
                           "white-space: pre-wrap; }</style></head><body>" + escaped + "</body></html>";
                }
                else
                {
                    html = "<html><head><style>body { font-family: Consolas, monospace; " +
                           "background: transparent; color: #212121; padding: 0; margin: 0; " +
                           "white-space: pre-wrap; }</style></head><body>" + escaped + "</body></html>";
                }
            }

            if (string.IsNullOrWhiteSpace(html))
            {
                html = "<html><head><style>body { font-family: Consolas, monospace; background: transparent; color: #212121; padding: 0; margin: 0; }</style></head><body></body></html>";
            }

            browser.NavigateToString(html);
            bodyBorder.Child = browser;
        }
        catch
        {
            bodyBorder.Child = new TextBlock
            {
                Text = _message.Content ?? "Error displaying message.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x21, 0x21))
            };
        }

        ContentStackPanel.Children.Add(bodyBorder);

        // Optional diff view
        if (_message.HasCodeBlock && !_message.IsUser && _message.DiffResult != null)
        {
            var diffView = new CodeDiffView
            {
                SuggestedCode = _message.CodeBlock,
                Margin = new Thickness(0, 0, 0, 0)
            };
            diffView.SetDiffResult(_message.DiffResult);

            diffView.OnApply += () =>
            {
                if (_message.CodeBlock != null)
                {
                    _onApplyCode?.Invoke(_message.CodeBlock);
                }
            };

            diffView.OnDiscard += () =>
            {
                // nothing special to do; caller may clear suggestion separately if desired
            };

            diffView.OnCopy += () =>
            {
                if (!string.IsNullOrEmpty(_message.CodeBlock))
                {
                    Clipboard.SetText(_message.CodeBlock);
                }
            };

            ContentStackPanel.Children.Add(diffView);
        }
    }

    private Brush GetResourceBrush(string key, Color fallbackColor)
    {
        try
        {
            var resource = TryFindResource(key);
            if (resource is Brush b)
            {
                return b;
            }
        }
        catch
        {
            // ignore and fall back
        }

        return new SolidColorBrush(fallbackColor);
    }

    private void FullScreenMessageWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

