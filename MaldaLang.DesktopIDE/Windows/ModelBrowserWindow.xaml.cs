// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class ModelBrowserWindow : Window
{
    private readonly HuggingFaceService _huggingFaceService;
    private readonly ModelDownloadService _downloadService;
    private readonly ModelStorageService _storageService;
    private readonly ThemeService _themeService;
    private readonly ObservableCollection<HuggingFaceModel> _availableModels = new();
    private readonly ObservableCollection<InstalledModel> _installedModels = new();
    private readonly ObservableCollection<ActiveDownloadItem> _activeDownloads = new();
    private readonly Dictionary<string, ActiveDownloadItem> _downloadItems = new();
    private HuggingFaceModel? _selectedModel;
    private HuggingFaceFile? _selectedFile;
    private string _currentSearchQuery = string.Empty;
    private string? _lastError;

    public ModelBrowserWindow(ThemeService themeService)
    {
        InitializeComponent();

        _themeService = themeService;
        _huggingFaceService = new HuggingFaceService();
        _downloadService = new ModelDownloadService();
        _storageService = new ModelStorageService();

        ModelsListBox.ItemsSource = _availableModels;
        InstalledModelsListBox.ItemsSource = _installedModels;
        ActiveDownloadsListBox.ItemsSource = _activeDownloads;

        _downloadService.OnDownloadProgress += OnDownloadProgress;
        _huggingFaceService.OnProgress += OnHuggingFaceProgress;

        // Apply theme
        ApplyTheme(_themeService.CurrentTheme);
        
        // Subscribe to theme changes
        _themeService.ThemeChanged += OnThemeChanged;

        LoadInstalledModels();
        LoadAvailableModels();
    }
    
    private void OnThemeChanged(object? sender, Theme theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyTheme(theme);
        });
    }
    
    private void ApplyTheme(Theme theme)
    {
        // Update all dynamic resources
        Resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
        Resources["MainBackgroundBrush"] = new SolidColorBrush(theme.MainBackground);
        Resources["SidebarBackgroundBrush"] = new SolidColorBrush(theme.SidebarBackground);
        Resources["ToolbarBackgroundBrush"] = new SolidColorBrush(theme.ToolbarBackground);
        Resources["ToolbarBorderBrush"] = new SolidColorBrush(theme.ToolbarBorder);
        Resources["EditorBackgroundBrush"] = new SolidColorBrush(theme.EditorBackground);
        Resources["EditorForegroundBrush"] = new SolidColorBrush(theme.EditorForeground);
        Resources["EditorLineNumbersBrush"] = new SolidColorBrush(theme.EditorLineNumbers);
        Resources["EditorSelectionBrush"] = new SolidColorBrush(theme.EditorSelection);
        Resources["TextForegroundBrush"] = new SolidColorBrush(theme.TextForeground);
        Resources["TextSecondaryBrush"] = new SolidColorBrush(theme.TextSecondary);
        Resources["ButtonBackgroundBrush"] = new SolidColorBrush(theme.ButtonBackground);
        Resources["ButtonForegroundBrush"] = new SolidColorBrush(theme.ButtonForeground);
        Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
        Resources["ButtonHoverBrush"] = new SolidColorBrush(theme.ButtonHover);
        Resources["ButtonHoverBorderBrush"] = new SolidColorBrush(theme.ButtonHoverBorder);
        Resources["PrimaryButtonBackgroundBrush"] = new SolidColorBrush(theme.PrimaryButtonBackground);
        Resources["PrimaryButtonForegroundBrush"] = new SolidColorBrush(theme.PrimaryButtonForeground);
        Resources["PrimaryButtonBorderBrush"] = new SolidColorBrush(theme.PrimaryButtonBorder);
        Resources["PrimaryButtonHoverBrush"] = new SolidColorBrush(theme.PrimaryButtonHover);
        Resources["PrimaryButtonHoverBorderBrush"] = new SolidColorBrush(theme.PrimaryButtonHoverBorder);
        Resources["TabBackgroundBrush"] = new SolidColorBrush(theme.TabBackground);
        Resources["TabActiveBackgroundBrush"] = new SolidColorBrush(theme.TabActiveBackground);
        Resources["TabForegroundBrush"] = new SolidColorBrush(theme.TabForeground);
        Resources["TabHoverBrush"] = new SolidColorBrush(theme.TabHover);
        Resources["InputBackgroundBrush"] = new SolidColorBrush(theme.InputBackground);
        Resources["InputForegroundBrush"] = new SolidColorBrush(theme.InputForeground);
        Resources["InputBorderBrush"] = new SolidColorBrush(theme.InputBorder);
        Resources["BorderBrush"] = new SolidColorBrush(theme.BorderColor);
        Resources["GridSplitterBackgroundBrush"] = new SolidColorBrush(theme.GridSplitterBackground);
        Resources["ListBackgroundBrush"] = new SolidColorBrush(theme.ListBackground);
        Resources["ListForegroundBrush"] = new SolidColorBrush(theme.ListForeground);
        Resources["ListBorderBrush"] = new SolidColorBrush(theme.ListBorder);
        Resources["DebugAccentBrush"] = new SolidColorBrush(theme.DebugAccent);
        Resources["ErrorBrush"] = new SolidColorBrush(theme.ErrorColor);
        Resources["WarningBrush"] = new SolidColorBrush(theme.WarningColor);
        Resources["InfoBrush"] = new SolidColorBrush(theme.InfoColor);
        
        // Refresh details panel if it's already populated
        if (_selectedModel != null)
        {
            UpdateDetailsPanel();
        }
    }

    private async void LoadAvailableModels()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingTextBlock.Text = "Loading models from HuggingFace...";
        LoadingStatusTextBlock.Text = "";
        ModelsListBox.IsEnabled = false;
        RefreshButton.IsEnabled = false;

        try
        {
            var models = await _huggingFaceService.SearchGgufModelsAsync(_currentSearchQuery, limit: 30);
            
            _availableModels.Clear();
            foreach (var model in models)
            {
                _availableModels.Add(model);
            }

            if (_availableModels.Count == 0)
            {
                LoadingTextBlock.Text = "No models found. Try a different search or check your internet connection.";
                LoadingStatusTextBlock.Text = "";
                LoadingPanel.Visibility = Visibility.Visible;
            }
            else
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            LoadingTextBlock.Text = $"Error loading models: {ex.Message}";
            LoadingStatusTextBlock.Text = "Please check your internet connection and try again.";
            LoadingPanel.Visibility = Visibility.Visible;
            MessageBox.Show($"Error loading models: {ex.Message}\n\nPlease check your internet connection and try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ModelsListBox.IsEnabled = true;
            RefreshButton.IsEnabled = true;
        }
    }

    private void OnHuggingFaceProgress(string status)
    {
        Dispatcher.Invoke(() =>
        {
            LoadingStatusTextBlock.Text = status;
        });
    }

    private void LoadInstalledModels()
    {
        _installedModels.Clear();
        var installed = _storageService.GetInstalledModels();
        foreach (var model in installed)
        {
            _installedModels.Add(model);
        }
    }

    private async void ModelsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedModel = ModelsListBox.SelectedItem as HuggingFaceModel;
        _selectedFile = null;
        _lastError = null; // Clear previous error
        
        // If model doesn't have file information, fetch it
        if (_selectedModel != null && (_selectedModel.Siblings == null || _selectedModel.Siblings.Count == 0))
        {
            try
            {
                DetailsTextBlock.Text = "Loading model details...";
                DetailsTextBlock.Visibility = Visibility.Visible;
                DetailsPanel.Children.Clear();
                
                var fullModel = await _huggingFaceService.GetModelDetailsAsync(_selectedModel.Id);
                if (fullModel != null)
                {
                    // Update the siblings property directly to preserve the reference
                    if (fullModel.Siblings != null && fullModel.Siblings.Count > 0)
                    {
                        _selectedModel.Siblings = fullModel.Siblings;
                    }
                    else
                    {
                        // Model details were fetched but no siblings found - this might be expected for some models
                        System.Diagnostics.Debug.WriteLine($"Model {_selectedModel.Id} details fetched but no siblings found");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Model {_selectedModel.Id} not found or details unavailable");
                }
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                System.Diagnostics.Debug.WriteLine($"Failed to load model details for {_selectedModel?.Id}: {ex.Message}");
                // Store error message to show in UI
                _lastError = ex.Message;
            }
            finally
            {
                // Always update the panel after fetching (or failing to fetch) details
                UpdateDetailsPanel();
            }
        }
        else
        {
            // Model already has siblings, update panel immediately
            UpdateDetailsPanel();
        }
    }

    private void UpdateDetailsPanel()
    {
        DetailsPanel.Children.Clear();

        if (_selectedModel == null)
        {
            DetailsTextBlock.Text = "Select a model to view details";
            DetailsTextBlock.Visibility = Visibility.Visible;
            DownloadButton.IsEnabled = false;
            return;
        }

        DetailsTextBlock.Visibility = Visibility.Collapsed;

        var theme = _themeService.CurrentTheme;
        
        // Model Name
        var nameText = new TextBlock
        {
            Text = _selectedModel.Id,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(theme.TextForeground)
        };
        DetailsPanel.Children.Add(nameText);

        // Author
        if (!string.IsNullOrEmpty(_selectedModel.Author))
        {
            var authorText = new TextBlock
            {
                Text = $"Author: {_selectedModel.Author}",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4),
                Foreground = new SolidColorBrush(theme.TextSecondary)
            };
            DetailsPanel.Children.Add(authorText);
        }

        // Stats
        var statsText = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = new SolidColorBrush(theme.TextSecondary)
        };
        statsText.Inlines.Add(new Run($"Downloads: {_selectedModel.Downloads:N0}"));
        statsText.Inlines.Add(new Run(" • "));
        statsText.Inlines.Add(new Run($"Likes: {_selectedModel.Likes}"));
        DetailsPanel.Children.Add(statsText);

        // Separator
        var separator = new Separator 
        { 
            Margin = new Thickness(0, 0, 0, 12),
            Background = new SolidColorBrush(theme.BorderColor)
        };
        DetailsPanel.Children.Add(separator);

        // GGUF Files
        var filesLabel = new TextBlock
        {
            Text = "Available Files:",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(theme.TextForeground)
        };
        DetailsPanel.Children.Add(filesLabel);

        var ggufFiles = _selectedModel.GetGgufFiles();
        if (ggufFiles.Count == 0)
        {
            // Check if model name suggests it's a GGUF model
            bool isLikelyGgufModel = _selectedModel.Id.Contains("gguf", StringComparison.OrdinalIgnoreCase) ||
                                     _selectedModel.Tags.Any(t => t.Contains("gguf", StringComparison.OrdinalIgnoreCase));
            
            string message;
            if (isLikelyGgufModel)
            {
                if (_selectedModel.Siblings == null || _selectedModel.Siblings.Count == 0)
                {
                    if (!string.IsNullOrEmpty(_lastError))
                    {
                        message = $"This model appears to be a GGUF model, but the file list could not be loaded.\n\nError: {_lastError}\n\nPlease try refreshing or check your internet connection.";
                    }
                    else
                    {
                        message = "This model appears to be a GGUF model, but the file list could not be loaded. Please try refreshing or check your internet connection.";
                    }
                }
                else
                {
                    message = "This model appears to be a GGUF model, but no files with .gguf extension were found in the file list. The files may use a different naming convention.";
                }
            }
            else
            {
                message = "No GGUF files found in this model.";
            }
            
            // Clear error after showing it
            _lastError = null;
            
            var noFilesText = new TextBlock
            {
                Text = message,
                FontSize = 12,
                Foreground = new SolidColorBrush(theme.TextSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            DetailsPanel.Children.Add(noFilesText);
            DownloadButton.IsEnabled = false;
        }
        else
        {
            var filesList = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            
            foreach (var file in ggufFiles)
            {
                var filePanel = new Border
                {
                    BorderBrush = new SolidColorBrush(theme.BorderColor),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 4),
                    Background = _selectedFile?.RFileName == file.RFileName 
                        ? new SolidColorBrush(theme.EditorSelection)
                        : new SolidColorBrush(Colors.Transparent)
                };

                var fileGrid = new Grid();
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var fileNameText = new TextBlock
                {
                    Text = file.RFileName ?? "Unknown",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(theme.TextForeground)
                };
                Grid.SetColumn(fileNameText, 0);
                fileGrid.Children.Add(fileNameText);

                var fileSizeText = new TextBlock
                {
                    Text = file.GetFormattedSize(),
                    FontSize = 11,
                    Margin = new Thickness(8, 0, 0, 0),
                    Foreground = new SolidColorBrush(theme.TextSecondary)
                };
                Grid.SetColumn(fileSizeText, 1);
                fileGrid.Children.Add(fileSizeText);

                filePanel.Child = fileGrid;
                filePanel.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedFile = file;
                    UpdateDetailsPanel();
                };
                filesList.Children.Add(filePanel);
            }

            DetailsPanel.Children.Add(filesList);
            DownloadButton.IsEnabled = _selectedFile != null 
                && !_storageService.IsModelInstalled(_selectedModel.Id, _selectedFile.RFileName)
                && !_downloadService.IsDownloading(_selectedModel.Id);
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedModel == null || _selectedFile == null)
            return;

        var fileName = _selectedFile.RFileName ?? "model.gguf";
        var modelId = _selectedModel.Id;
        
        // Check if already downloading
        if (_downloadService.IsDownloading(modelId))
        {
            MessageBox.Show("This model is already being downloaded.", "Download In Progress", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var destinationPath = _storageService.GetSuggestedFilePath(modelId, fileName);
        var downloadUrl = _huggingFaceService.GetDownloadUrl(modelId, fileName);

        // Create and add download item to active downloads
        var downloadItem = new ActiveDownloadItem
        {
            ModelId = modelId,
            FileName = fileName,
            Percentage = 0,
            ProgressText = "Starting download..."
        };
        
        _downloadItems[modelId] = downloadItem;
        _activeDownloads.Add(downloadItem);
        UpdateDetailsPanel(); // Update to disable button if needed

        // Start download asynchronously (don't await, let it run in background)
        _ = Task.Run(async () =>
        {
            try
            {
                var success = await _downloadService.DownloadFileAsync(
                    modelId,
                    fileName,
                    downloadUrl,
                    destinationPath
                );

                Dispatcher.Invoke(() =>
                {
                    // Remove from active downloads
                    if (_downloadItems.TryGetValue(modelId, out var item))
                    {
                        _activeDownloads.Remove(item);
                        _downloadItems.Remove(modelId);
                    }

                    if (success)
                    {
                        _storageService.RegisterModel(modelId, destinationPath, fileName);
                        LoadInstalledModels();
                        UpdateDetailsPanel();
                        MessageBox.Show($"Model downloaded successfully!\n\nSaved to: {destinationPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        // Only show message if not cancelled by user
                        if (item != null && !item.ProgressText.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            MessageBox.Show("Download failed.", "Download Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    if (_downloadItems.TryGetValue(modelId, out var item))
                    {
                        _activeDownloads.Remove(item);
                        _downloadItems.Remove(modelId);
                    }
                    MessageBox.Show($"Error downloading model: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateDetailsPanel();
                });
            }
        });
    }

    private void OnDownloadProgress(ModelDownloadProgress progress)
    {
        Dispatcher.Invoke(() =>
        {
            if (_downloadItems.TryGetValue(progress.ModelId, out var downloadItem))
            {
                if (progress.IsComplete)
                {
                    downloadItem.Percentage = 100;
                    downloadItem.ProgressText = "Download complete!";
                }
                else if (!string.IsNullOrEmpty(progress.ErrorMessage))
                {
                    downloadItem.ProgressText = $"Error: {progress.ErrorMessage}";
                }
                else
                {
                    downloadItem.Percentage = progress.Percentage;
                    downloadItem.ProgressText = $"{progress.GetFormattedDownloaded()} / {progress.GetFormattedTotal()} ({progress.GetFormattedSpeed()}) - {progress.Percentage}%";
                }
            }
        });
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _currentSearchQuery = SearchTextBox.Text;
        // Debounce search - could be improved with a timer
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAvailableModels();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _storageService.OpenModelsDirectory();
    }

    private void OpenInstalledModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is InstalledModel model)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{model.Path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void DeleteInstalledModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is InstalledModel model)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to delete this model?\n\n{model.FileName}\n\nThis will permanently delete the file.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                if (_storageService.DeleteModel(model.Id, model.FileName))
                {
                    LoadInstalledModels();
                    MessageBox.Show("Model deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Failed to delete the model file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string modelId)
        {
            _downloadService.CancelDownload(modelId);
            
            // Update the download item to show cancellation
            if (_downloadItems.TryGetValue(modelId, out var downloadItem))
            {
                downloadItem.ProgressText = "Cancelling...";
            }
            
            // Update details panel to re-enable download button if this was the selected model
            if (_selectedModel?.Id == modelId)
            {
                UpdateDetailsPanel();
            }
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadService.OnDownloadProgress -= OnDownloadProgress;
        _huggingFaceService.OnProgress -= OnHuggingFaceProgress;
        _themeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }
}