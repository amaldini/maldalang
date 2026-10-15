// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Services;
using System.Windows.Threading;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void OnOutputNeedsUpdate()
    {
        // Update output panel (e.g., during sleep)
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_executionService != null)
                {
                    SetOutputText(_executionService.GetCurrentOutput());
                    SwitchToTab("output"); // Ensure output tab is visible
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnOutputNeedsUpdate: {ex.Message}");
        }
    }
    
    private void OnInputRequested(string prompt)
    {
        // Show input dialog on UI thread
        try
        {
            Dispatcher.Invoke(() =>
            {
                // Update output before showing input dialog so user can see progress
                if (_executionService != null)
                {
                    SetOutputText(_executionService.GetCurrentOutput());
                    SwitchToTab("output"); // Ensure output tab is visible
                }
                
                DesktopInputProvider? provider = null;
                try
                {
                    if (_executionService == null)
                    {
                        // Execution service is null - this shouldn't happen, but if it does,
                        // we can't provide input. The TaskCompletionSource will hang.
                        // This is a critical error condition.
                        System.Diagnostics.Debug.WriteLine("ERROR: _executionService is null in OnInputRequested");
                        return;
                    }
                    
                    provider = _executionService.GetInputProvider();
                    if (provider == null)
                    {
                        // Input provider is null - this shouldn't happen, but if it does,
                        // we can't provide input. The TaskCompletionSource will hang.
                        // This is a critical error condition.
                        System.Diagnostics.Debug.WriteLine("ERROR: inputProvider is null in OnInputRequested");
                        return;
                    }
                    
                    // Get current theme for dialog styling
                    var theme = _themeService.CurrentTheme;
                    
                    var inputDialog = new Window
                    {
                        Title = "Input Required",
                        Width = 400,
                        Height = 180,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Owner = this,
                        ResizeMode = ResizeMode.NoResize,
                        Background = new SolidColorBrush(theme.WindowBackground)
                    };
                    
                    var stackPanel = new StackPanel
                    {
                        Margin = new Thickness(20),
                        Background = new SolidColorBrush(theme.WindowBackground)
                    };
                    
                    var promptText = new TextBlock
                    {
                        Text = prompt ?? "Enter value:",
                        Margin = new Thickness(0, 0, 0, 10),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(theme.TextForeground)
                    };
                    
                    var inputTextBox = new TextBox
                    {
                        Name = "InputTextBox",
                        Margin = new Thickness(0, 0, 0, 10),
                        Background = new SolidColorBrush(theme.InputBackground),
                        Foreground = new SolidColorBrush(theme.InputForeground),
                        BorderBrush = new SolidColorBrush(theme.InputBorder)
                    };
                    
                    var buttonPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    
                    // Create round button template using FrameworkElementFactory
                    ControlTemplate roundButtonTemplate;
                    try
                    {
                        roundButtonTemplate = new ControlTemplate(typeof(Button));
                        var borderFactory = new FrameworkElementFactory(typeof(Border));
                        borderFactory.Name = "border";
                        borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                        borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
                        borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
                        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
                        
                        var contentPresenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
                        contentPresenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                        contentPresenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                        borderFactory.AppendChild(contentPresenterFactory);
                        
                        roundButtonTemplate.VisualTree = borderFactory;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error creating round button template: {ex.Message}");
                        // Fallback to null - buttons will use default template
                        roundButtonTemplate = null;
                    }
                    
                    var okButton = new Button
                    {
                        Content = "OK",
                        Width = 75,
                        Height = 30,
                        Margin = new Thickness(5, 0, 0, 0),
                        IsDefault = true,
                        Background = new SolidColorBrush(theme.PrimaryButtonBackground),
                        Foreground = new SolidColorBrush(theme.PrimaryButtonForeground),
                        BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder),
                        BorderThickness = new Thickness(1)
                    };
                    if (roundButtonTemplate != null)
                    {
                        okButton.Template = roundButtonTemplate;
                    }
                    
                    okButton.Click += (s, e) =>
                    {
                        string inputValue = "";
                        try
                        {
                            // Safely get input text
                            if (inputTextBox != null)
                            {
                                inputValue = inputTextBox.Text ?? "";
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error getting input text: {ex.Message}");
                            inputValue = "";
                        }
                        
                        try
                        {
                            // Use the captured provider variable to queue input
                            if (provider != null)
                            {
                                provider.QueueInput(inputValue);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("ERROR: provider is null in OK button click");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error but continue
                            System.Diagnostics.Debug.WriteLine($"Error queuing input: {ex.Message}");
                        }
                        
                        try
                        {
                            inputDialog.DialogResult = true;
                            inputDialog.Close();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error closing dialog: {ex.Message}");
                        }
                    };
                    
                    var cancelButton = new Button
                    {
                        Content = "Cancel",
                        Width = 75,
                        Height = 30,
                        Margin = new Thickness(5, 0, 0, 0),
                        IsCancel = true,
                        Background = new SolidColorBrush(theme.ButtonBackground),
                        Foreground = new SolidColorBrush(theme.ButtonForeground),
                        BorderBrush = new SolidColorBrush(theme.ButtonBorder),
                        BorderThickness = new Thickness(1)
                    };
                    if (roundButtonTemplate != null)
                    {
                        cancelButton.Template = roundButtonTemplate;
                    }
                    
                    // Add hover effects for buttons
                    okButton.MouseEnter += (s, e) => 
                    {
                        okButton.Background = new SolidColorBrush(theme.PrimaryButtonHover);
                        okButton.BorderBrush = new SolidColorBrush(theme.PrimaryButtonHoverBorder);
                    };
                    okButton.MouseLeave += (s, e) => 
                    {
                        okButton.Background = new SolidColorBrush(theme.PrimaryButtonBackground);
                        okButton.BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder);
                    };
                    
                    cancelButton.MouseEnter += (s, e) => 
                    {
                        cancelButton.Background = new SolidColorBrush(theme.ButtonHover);
                        cancelButton.BorderBrush = new SolidColorBrush(theme.ButtonHoverBorder);
                    };
                    cancelButton.MouseLeave += (s, e) => 
                    {
                        cancelButton.Background = new SolidColorBrush(theme.ButtonBackground);
                        cancelButton.BorderBrush = new SolidColorBrush(theme.ButtonBorder);
                    };
                    
                    cancelButton.Click += (s, e) =>
                    {
                        try
                        {
                            // Use the captured provider variable
                            if (provider != null)
                            {
                                provider.QueueInput(""); // Provide empty string on cancel
                            }
                            inputDialog.DialogResult = false;
                            inputDialog.Close();
                        }
                        catch (Exception ex)
                        {
                            // Log error but still close dialog and provide empty input
                            System.Diagnostics.Debug.WriteLine($"Error in Cancel button click: {ex.Message}");
                            try
                            {
                                if (provider != null)
                                {
                                    provider.QueueInput("");
                                }
                            }
                            catch { }
                            inputDialog.DialogResult = false;
                            inputDialog.Close();
                        }
                    };
                    
                    buttonPanel.Children.Add(okButton);
                    buttonPanel.Children.Add(cancelButton);
                    
                    stackPanel.Children.Add(promptText);
                    stackPanel.Children.Add(inputTextBox);
                    stackPanel.Children.Add(buttonPanel);
                    
                    inputDialog.Content = stackPanel;
                    inputDialog.Loaded += (s, e) => 
                    {
                        inputTextBox.Focus();
                        // Apply title bar colors to the dialog window
                        UpdateTitleBarColorsForWindow(inputDialog, theme);
                    };
                    
                    // Handle dialog closing without clicking a button (e.g., X button)
                    inputDialog.Closed += (s, e) =>
                    {
                        try
                        {
                            // If dialog was closed without setting DialogResult, provide empty input
                            if (inputDialog.DialogResult == null)
                            {
                                // Use the captured provider variable
                                if (provider != null)
                                {
                                    provider.QueueInput(""); // Provide empty string if closed without OK/Cancel
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error in dialog Closed event: {ex.Message}");
                            try
                            {
                                if (provider != null)
                                {
                                    provider.QueueInput("");
                                }
                            }
                            catch { }
                        }
                    };
                    
                    var result = inputDialog.ShowDialog();
                    
                    // Ensure input is provided even if dialog was closed unexpectedly
                    if (result != true && result != false)
                    {
                        try
                        {
                            // Use the captured provider variable
                            if (provider != null)
                            {
                                provider.QueueInput(""); // Provide empty string as fallback
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error providing fallback input: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If dialog creation fails, provide empty input to prevent hang
                    System.Diagnostics.Debug.WriteLine($"Error creating input dialog: {ex.Message}");
                    try
                    {
                        // Use the provider variable if it was successfully captured, otherwise get a new one
                        if (provider == null && _executionService != null)
                        {
                            provider = _executionService.GetInputProvider();
                        }
                        if (provider != null)
                        {
                            provider.QueueInput("");
                        }
                    }
                    catch
                    {
                        // Ignore errors when trying to provide fallback input
                    }
                }
            });
        }
        catch (Exception ex)
        {
            // If Dispatcher.Invoke fails, try to provide empty input
            System.Diagnostics.Debug.WriteLine($"Error in OnInputRequested: {ex.Message}");
            try
            {
                var provider = _executionService?.GetInputProvider();
                if (provider != null)
                {
                    provider.QueueInput("");
                }
            }
            catch
            {
                // Ignore errors
            }
        }
    }

    private void OnConfirmRequested(string message, TaskCompletionSource<bool> completion)
    {
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_executionService != null)
                {
                    SetOutputText(_executionService.GetCurrentOutput());
                    SwitchToTab("output");
                }

                var theme = _themeService.CurrentTheme;
                var dialog = new Window
                {
                    Title = "Allow Command?",
                    Width = 480,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize,
                    Background = new SolidColorBrush(theme.WindowBackground)
                };

                var stackPanel = new StackPanel
                {
                    Margin = new Thickness(20),
                    Background = new SolidColorBrush(theme.WindowBackground)
                };

                stackPanel.Children.Add(new TextBlock
                {
                    Text = message ?? "Allow this command?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 16),
                    Foreground = new SolidColorBrush(theme.TextForeground)
                });

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var denyButton = new Button
                {
                    Content = "Deny",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsCancel = true,
                    Background = new SolidColorBrush(theme.ButtonBackground),
                    Foreground = new SolidColorBrush(theme.ButtonForeground),
                    BorderBrush = new SolidColorBrush(theme.ButtonBorder),
                    BorderThickness = new Thickness(1)
                };

                var allowButton = new Button
                {
                    Content = "Allow",
                    Width = 80,
                    Height = 30,
                    Margin = new Thickness(5, 0, 0, 0),
                    IsDefault = true,
                    Background = new SolidColorBrush(theme.PrimaryButtonBackground),
                    Foreground = new SolidColorBrush(theme.PrimaryButtonForeground),
                    BorderBrush = new SolidColorBrush(theme.PrimaryButtonBorder),
                    BorderThickness = new Thickness(1)
                };

                denyButton.Click += (_, _) =>
                {
                    try { completion.TrySetResult(false); } catch { }
                    dialog.DialogResult = false;
                    dialog.Close();
                };

                allowButton.Click += (_, _) =>
                {
                    try { completion.TrySetResult(true); } catch { }
                    dialog.DialogResult = true;
                    dialog.Close();
                };

                buttonPanel.Children.Add(denyButton);
                buttonPanel.Children.Add(allowButton);
                stackPanel.Children.Add(buttonPanel);
                dialog.Content = stackPanel;
                dialog.Closed += (_, _) => completion.TrySetResult(false);
                dialog.ShowDialog();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnConfirmRequested: {ex.Message}");
            completion.TrySetResult(false);
        }
    }
}
