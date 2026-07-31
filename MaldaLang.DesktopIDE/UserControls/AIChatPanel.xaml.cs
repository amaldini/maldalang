// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.IDE;
using MaldaLang.IDE.Models;
using Markdig;
using MaldaLang.BuiltIns;
using MaldaLang.DesktopIDE.Windows;

namespace MaldaLang.DesktopIDE.UserControls;

public partial class AIChatPanel : UserControl
{
    private readonly ObservableCollection<ChatMessageModel> _messages = new();
    private readonly AIChatService _aiChatService;
    private readonly CodeDiffService _codeDiffService;
    private readonly AIChatSettingsService _settingsService;
    private readonly ModelStorageService _modelStorageService;
    private bool _isLoading = false;
    private ChatMessageModel? _currentSuggestion = null;
    private ChatMode _currentMode = ChatMode.Ask;
    private AIChatSettings _currentSettings;
    
    // Properties
    public string CurrentCode { get; set; } = "";
    public (int Line, int Column) CursorPosition { get; set; }
    public System.Collections.Generic.List<Diagnostic> Errors { get; set; } = new();
    public string? SelectedCode { get; set; }
    
    // Event
    public event Action<string>? OnCodeChange;

    /// <summary>When set, Edit mode tool calls are logged here and appear in the Tool Calls panel.</summary>
    public void SetToolCallLogService(Services.ToolCallLogService? service)
    {
        _aiChatService.SetToolCallLogService(service);
    }

    public AIChatPanel()
    {
        InitializeComponent();
        
        var languageContextService = new MaldaLang.IDE.MALDALanguageContextService();
        _aiChatService = new AIChatService(languageContextService);
        _codeDiffService = new CodeDiffService();
        _settingsService = new AIChatSettingsService();
        _modelStorageService = new ModelStorageService();
        
        // Load saved settings
        _currentSettings = _settingsService.LoadSettings();
        _aiChatService.UpdateSettings(_currentSettings);
        
        // Subscribe to model loading events
        ModelLoadingService.OnLoadingStarted += OnModelLoadingStarted;
        ModelLoadingService.OnProgressChanged += OnModelLoadingProgress;
        ModelLoadingService.OnLoadingCompleted += OnModelLoadingCompleted;
        
        // Clean up event handlers when control is unloaded
        Unloaded += AIChatPanel_Unloaded;
        
        UpdateWelcomeVisibility();
        UpdateModelDisplay();
    }
    
    private void AIChatPanel_Unloaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe from model loading events
        ModelLoadingService.OnLoadingStarted -= OnModelLoadingStarted;
        ModelLoadingService.OnProgressChanged -= OnModelLoadingProgress;
        ModelLoadingService.OnLoadingCompleted -= OnModelLoadingCompleted;
    }
    
    private void OnModelLoadingStarted(ModelLoadingService.ModelLoadingProgress progress)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ModelLoadingOverlay.Visibility = Visibility.Visible;
            ModelLoadingText.Text = progress.Message;
            ModelLoadingProgressBar.Value = progress.Percentage;
            ModelLoadingProgressBar.IsIndeterminate = progress.Percentage < 10;
        });
    }
    
    private void OnModelLoadingProgress(ModelLoadingService.ModelLoadingProgress progress)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (progress.IsError)
            {
                // Show error message and hide progress bar
                ModelLoadingText.Text = progress.Message;
                ModelLoadingProgressBar.Visibility = Visibility.Collapsed;
                // Keep overlay visible briefly to show error, then hide it
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(3)
                };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    if (!ModelLoadingService.IsAnyModelLoading())
                    {
                        ModelLoadingOverlay.Visibility = Visibility.Collapsed;
                        ModelLoadingProgressBar.Visibility = Visibility.Visible;
                    }
                };
                timer.Start();
            }
            else if (progress.IsLoading && progress.Percentage < 100)
            {
                ModelLoadingOverlay.Visibility = Visibility.Visible;
                ModelLoadingProgressBar.Visibility = Visibility.Visible;
                ModelLoadingText.Text = progress.Message;
                ModelLoadingProgressBar.Value = progress.Percentage;
                ModelLoadingProgressBar.IsIndeterminate = false;
            }
        });
    }
    
    private void OnModelLoadingCompleted(string modelPath)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Check if there are any other models still loading
            if (!ModelLoadingService.IsAnyModelLoading())
            {
                ModelLoadingOverlay.Visibility = Visibility.Collapsed;
                ModelLoadingProgressBar.Value = 100;
                ModelLoadingProgressBar.Visibility = Visibility.Visible;
            }
        });
    }
    
    private void UpdateWelcomeVisibility()
    {
        WelcomeTextBlock.Visibility = _messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    
    private void ChatInputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(ChatInputTextBox.Text) && !_isLoading;
    }
    
    private void ChatInputTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && 
            (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != System.Windows.Input.ModifierKeys.Shift &&
            !_isLoading && 
            !string.IsNullOrWhiteSpace(ChatInputTextBox.Text))
        {
            SendMessage();
            e.Handled = true;
        }
    }
    
    private void SendButton_Click(object sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _aiChatService.ClearHistory();
        _messages.Clear();
        MessagesStackPanel.Children.Clear();
        _currentSuggestion = null;
        UpdateWelcomeVisibility();
    }

    private void AskModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        _currentMode = ChatMode.Ask;
    }

    private void AskMaldaModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        _currentMode = ChatMode.AskMalda;
    }

    private void ModelSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ownerWindow = Window.GetWindow(this);
            var window = new Windows.ModelSelectionWindow(_modelStorageService, _currentSettings);
            
            if (ownerWindow != null)
            {
                window.Owner = ownerWindow;
                // Copy theme resources from owner window
                foreach (var key in ownerWindow.Resources.Keys)
                {
                    if (ownerWindow.Resources[key] is System.Windows.Media.Brush)
                    {
                        window.Resources[key] = ownerWindow.Resources[key];
                    }
                }
            }
            
            if (window.ShowDialog() == true && window.SelectedSettings != null)
            {
                _currentSettings = window.SelectedSettings;
                _aiChatService.UpdateSettings(_currentSettings);
                _settingsService.SaveSettings(_currentSettings);
                UpdateModelDisplay();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening model selection: {ex.Message}\n\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UpdateModelDisplay()
    {
        if (_currentSettings.UseLocalModel && !string.IsNullOrEmpty(_currentSettings.LocalModelPath))
        {
            var installedModels = _modelStorageService.GetInstalledModels();
            var model = installedModels.FirstOrDefault(m => m.Path == _currentSettings.LocalModelPath);
            
            if (model != null)
            {
                CurrentModelTextBlock.Text = $"Using: {model.FileName}";
                CurrentModelTextBlock.Visibility = Visibility.Visible;
                ModelSelectionButton.Content = "Change Model";
            }
            else
            {
                CurrentModelTextBlock.Text = $"Using: {System.IO.Path.GetFileName(_currentSettings.LocalModelPath)}";
                CurrentModelTextBlock.Visibility = Visibility.Visible;
                ModelSelectionButton.Content = "Change Model";
            }
        }
        else
        {
            CurrentModelTextBlock.Text = "Using: OpenRouter (Online)";
            CurrentModelTextBlock.Visibility = Visibility.Visible;
            ModelSelectionButton.Content = "Select Model";
        }
    }
    
    private async void SendMessage()
    {
        var userMessage = ChatInputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userMessage) || _isLoading)
            return;
        
        ChatInputTextBox.Text = "";
        SendButton.IsEnabled = false;
        
        // Add user message to UI
        var userMsg = new ChatMessageModel
        {
            IsUser = true,
            Content = userMessage,
            Timestamp = DateTime.Now
        };
        _messages.Add(userMsg);
        AddMessageToUI(userMsg);
        UpdateWelcomeVisibility();
        
        _isLoading = true;
        LoadingIndicator.Visibility = Visibility.Visible;
        ChatInputTextBox.IsEnabled = false;
        
        ScrollToBottom();
        
        try
        {
            // Send to AI service
            var response = await _aiChatService.SendMessageAsync(
                userMessage,
                CurrentCode,
                CursorPosition.Line,
                CursorPosition.Column,
                Errors,
                SelectedCode,
                _currentMode
            );
            
            if (response.IsError)
            {
                var errorMsg = new ChatMessageModel
                {
                    IsUser = false,
                    Content = response.ErrorMessage ?? "An error occurred",
                    IsError = true,
                    Timestamp = DateTime.Now
                };
                _messages.Add(errorMsg);
                Dispatcher.BeginInvoke(new System.Action(() => 
                {
                    AddMessageToUI(errorMsg);
                }));
            }
            else
            {
                var aiMessage = new ChatMessageModel
                {
                    IsUser = false,
                    Content = response.Content,
                    CodeBlock = response.CodeBlock,
                    HasCodeBlock = response.HasCodeBlock,
                    Timestamp = DateTime.Now
                };
                
                // Generate diff if code block exists
                if (response.HasCodeBlock && !string.IsNullOrEmpty(response.CodeBlock))
                {
                    aiMessage.DiffResult = _codeDiffService.GenerateDiff(CurrentCode, response.CodeBlock);
                    _currentSuggestion = aiMessage;
                }
                
                _messages.Add(aiMessage);
                Dispatcher.Invoke(() => AddMessageToUI(aiMessage));
            }
        }
        catch (Exception ex)
        {
            var errorMsg = new ChatMessageModel
            {
                IsUser = false,
                Content = $"Error: {ex.Message}",
                IsError = true,
                Timestamp = DateTime.Now
            };
            _messages.Add(errorMsg);
            Dispatcher.Invoke(() => AddMessageToUI(errorMsg));
        }
        finally
        {
            _isLoading = false;
            Dispatcher.Invoke(() =>
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                ChatInputTextBox.IsEnabled = true;
                SendButton.IsEnabled = !string.IsNullOrWhiteSpace(ChatInputTextBox.Text);
                ScrollToBottom();
            });
        }
    }
    
    // Helper method to safely get resources with fallbacks
    private Brush GetResourceBrush(string key, Color fallbackColor)
    {
        try
        {
            var resource = TryFindResource(key);
            if (resource is Brush brush)
                return brush;
        }
        catch
        {
            // Fall through to fallback
        }
        return new SolidColorBrush(fallbackColor);
    }
    
    private void AddMessageToUI(ChatMessageModel message)
    {
        if (message == null)
            return;
            
        try
        {
            var messageContainer = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8)
            };
            
            var stackPanel = new StackPanel();
        
        // Message Header
        var headerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var textSecondaryBrush = GetResourceBrush("TextSecondaryBrush", Color.FromRgb(0x75, 0x75, 0x75));
        headerPanel.Children.Add(new TextBlock
        {
            Text = message.IsUser ? "You" : "AI",
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Foreground = textSecondaryBrush
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = message.Timestamp.ToString("HH:mm"),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 11,
            Foreground = textSecondaryBrush
        });

        // Expand button for non-user messages
        if (!message.IsUser)
        {
            var expandButton = new Button
            {
                Content = "Expand",
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            expandButton.Click += (s, e) =>
            {
                try
                {
                    var ownerWindow = Window.GetWindow(this);
                    var window = new FullScreenMessageWindow(
                        message,
                        code =>
                        {
                            OnCodeChange?.Invoke(code);
                            _currentSuggestion = null;
                        });
                    
                    if (ownerWindow != null)
                    {
                        window.Owner = ownerWindow;
                        // Copy theme resources from owner window
                        foreach (var key in ownerWindow.Resources.Keys)
                        {
                            if (ownerWindow.Resources[key] is System.Windows.Media.Brush)
                            {
                                window.Resources[key] = ownerWindow.Resources[key];
                            }
                        }
                    }

                    window.Show();
                    window.WindowState = WindowState.Maximized;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening full-screen message: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            headerPanel.Children.Add(expandButton);
        }

        stackPanel.Children.Add(headerPanel);
        
        // Message Content
        var borderBrush = GetResourceBrush("BorderBrush", Color.FromRgb(0xD0, 0xD0, 0xD0));
        var contentBorder = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = borderBrush
        };
        
        if (message.IsUser)
        {
            contentBorder.Background = GetResourceBrush("PrimaryButtonBackgroundBrush", Color.FromRgb(0x21, 0x96, 0xF3));
        }
        else
        {
            contentBorder.Background = GetResourceBrush("ListBackgroundBrush", Color.FromRgb(0xFF, 0xFF, 0xFF));
        }
        
        try
        {
            var webBrowser = new WebBrowser
            {
                Height = double.NaN,
                MinHeight = 20
            };
            
            string htmlContent;
            if (message.IsError)
            {
                var escaped = System.Security.SecurityElement.Escape(message.Content ?? "");
                htmlContent = $"<html><head><style>body {{ font-family: Consolas, monospace; background: transparent; color: #d32f2f; padding: 0; margin: 0; }}</style></head><body>{escaped}</body></html>";
            }
            else if (IsMarkdown(message.Content))
            {
                htmlContent = RenderMarkdown(message.Content ?? "");
            }
            else
            {
                var escaped = System.Security.SecurityElement.Escape(message.Content ?? "");
                
                // Ensure good contrast between text and background for user vs AI messages
                if (message.IsUser)
                {
                    // Match the primary button/chat bubble color and use white text
                    htmlContent = "<html><head><style>body { font-family: Consolas, monospace; " +
                                  "background: #2196F3; color: #FFFFFF; padding: 0; margin: 0; " +
                                  "white-space: pre-wrap; }</style></head><body>" + escaped + "</body></html>";
                }
                else
                {
                    htmlContent = "<html><head><style>body { font-family: Consolas, monospace; " +
                                  "background: transparent; color: #212121; padding: 0; margin: 0; " +
                                  "white-space: pre-wrap; }</style></head><body>" + escaped + "</body></html>";
                }
            }
            
            // Ensure HTML is not null or empty
            if (string.IsNullOrWhiteSpace(htmlContent))
            {
                htmlContent = "<html><head><style>body { font-family: Consolas, monospace; background: transparent; color: #212121; padding: 0; margin: 0; }</style></head><body></body></html>";
            }
            
            webBrowser.NavigateToString(htmlContent);
            contentBorder.Child = webBrowser;
        }
        catch (Exception ex)
        {
            // Fallback to TextBlock if WebBrowser fails
            var errorText = new TextBlock
            {
                Text = message.Content ?? "Error displaying message",
                TextWrapping = TextWrapping.Wrap,
                Foreground = message.IsError ? new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)) : GetResourceBrush("TextForegroundBrush", Color.FromRgb(0x21, 0x21, 0x21)),
                Margin = new Thickness(0)
            };
            contentBorder.Child = errorText;
        }
        stackPanel.Children.Add(contentBorder);
        
        // Code Diff View (if applicable)
        if (message.HasCodeBlock && !message.IsUser && message.DiffResult != null)
        {
            var diffView = new CodeDiffView
            {
                SuggestedCode = message.CodeBlock,
                Margin = new Thickness(0, 8, 0, 0)
            };
            diffView.SetDiffResult(message.DiffResult);
            diffView.OnApply += () =>
            {
                if (_currentSuggestion?.CodeBlock != null)
                {
                    OnCodeChange?.Invoke(_currentSuggestion.CodeBlock);
                    _currentSuggestion = null;
                }
            };
            diffView.OnDiscard += () =>
            {
                _currentSuggestion = null;
            };
            diffView.OnCopy += () =>
            {
                if (_currentSuggestion?.CodeBlock != null)
                {
                    Clipboard.SetText(_currentSuggestion.CodeBlock);
                }
            };
            stackPanel.Children.Add(diffView);
        }
        
            messageContainer.Child = stackPanel;
            MessagesStackPanel.Children.Add(messageContainer);
        }
        catch (Exception ex)
        {
            // Log error and add a simple error message to prevent crash
            System.Diagnostics.Debug.WriteLine($"Error adding message to UI: {ex.Message}");
            var errorContainer = new Border
            {
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            var errorText = new TextBlock
            {
                Text = $"Error displaying message: {ex.Message}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)),
                Margin = new Thickness(4)
            };
            errorContainer.Child = errorText;
            MessagesStackPanel.Children.Add(errorContainer);
        }
    }
    
    private void ScrollToBottom()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            MessagesScrollViewer.ScrollToEnd();
        }));
    }
    
    private bool IsMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        var trimmed = text.TrimStart();
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^#{1,6}\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        if (trimmed.Contains("```"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^---+$", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^[\s]*[-*+]\s+\w", System.Text.RegularExpressions.RegexOptions.Multiline))
            return true;
        if (trimmed.Contains("|") && trimmed.Contains("---"))
            return true;
        return false;
    }
    
    private string RenderMarkdown(string markdown)
    {
        var normalized = markdown.Replace("\r\n", "\n").Replace("\r", "\n");
        var processedMarkdown = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"(?<!\n)\n(?!\n)",
            "  \n"
        );
        
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
        
        var html = Markdown.ToHtml(processedMarkdown, pipeline);
        
        var parts = System.Text.RegularExpressions.Regex.Split(html, @"(<pre[^>]*>.*?</pre>|<code[^>]*>.*?</code>)", System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var result = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^<(pre|code)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                result.Append(part);
            }
            else
            {
                result.Append(part.Replace("\n", "<br>"));
            }
        }
        
        var fullHtml = $@"
            <html>
            <head>
                <style>
                    body {{ 
                        font-family: Consolas, monospace; 
                        background: transparent; 
                        color: #212121; 
                        padding: 0; 
                        margin: 0; 
                        line-height: 1.6;
                    }}
                    pre {{ 
                        background: #f5f5f5; 
                        padding: 12px; 
                        border-radius: 4px;
                        overflow-x: auto;
                    }}
                    code {{ 
                        background: #f5f5f5; 
                        padding: 2px 6px; 
                        border-radius: 3px;
                    }}
                    pre code {{
                        background: transparent;
                        padding: 0;
                    }}
                    p {{
                        margin: 0.5em 0;
                    }}
                </style>
            </head>
            <body>{result}</body>
            </html>";
        
        return fullHtml;
    }
}