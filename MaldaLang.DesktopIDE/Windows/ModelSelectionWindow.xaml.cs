// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class ModelSelectionWindow : Window
{
    private readonly ModelStorageService _storageService;
    private readonly AIChatSettings _currentSettings;
    private InstalledModel? _selectedModel;
    private bool _useLocalModel = true;

    public AIChatSettings? SelectedSettings { get; private set; }

    public ModelSelectionWindow(ModelStorageService storageService, AIChatSettings currentSettings)
    {
        InitializeComponent();
        _storageService = storageService;
        _currentSettings = currentSettings;

        // Set current settings
        if (currentSettings.UseLocalModel)
        {
            UseLocalModelRadioButton.IsChecked = true;
            _useLocalModel = true;
        }
        else
        {
            UseOpenRouterRadioButton.IsChecked = true;
            _useLocalModel = false;
        }

        LoadModels();
        ModelsListBox.SelectionChanged += ModelsListBox_SelectionChanged;
    }

    private void LoadModels()
    {
        try
        {
            var installedModels = _storageService.GetInstalledModels();
            
            if (installedModels.Count == 0)
            {
                ModelsListBox.Visibility = Visibility.Collapsed;
                NoModelsTextBlock.Visibility = Visibility.Visible;
                SelectButton.IsEnabled = false;
            }
            else
            {
                ModelsListBox.Visibility = Visibility.Visible;
                NoModelsTextBlock.Visibility = Visibility.Collapsed;
                ModelsListBox.ItemsSource = installedModels;

                // If using local model, try to select the current one
                if (_useLocalModel && !string.IsNullOrEmpty(_currentSettings.LocalModelPath))
                {
                    var currentModel = installedModels.FirstOrDefault(m => m.Path == _currentSettings.LocalModelPath);
                    if (currentModel != null)
                    {
                        ModelsListBox.SelectedItem = currentModel;
                        _selectedModel = currentModel;
                        SelectButton.IsEnabled = true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // If loading fails, show error and disable local model option
            System.Diagnostics.Debug.WriteLine($"Error loading models: {ex.Message}");
            ModelsListBox.Visibility = Visibility.Collapsed;
            NoModelsTextBlock.Visibility = Visibility.Visible;
            NoModelsTextBlock.Text = $"Error loading models: {ex.Message}";
            SelectButton.IsEnabled = false;
        }
    }

    private void ModelsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedModel = ModelsListBox.SelectedItem as InstalledModel;
        SelectButton.IsEnabled = _selectedModel != null && _useLocalModel;
    }

    private void UseLocalModelRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        // Guard against being called during initialization before _storageService is set
        if (_storageService == null)
        {
            _useLocalModel = true;
            return;
        }
        
        _useLocalModel = true;
        var installedModels = _storageService.GetInstalledModels();
        if (installedModels.Count > 0)
        {
            SelectButton.IsEnabled = _selectedModel != null;
            ModelsListBox.IsEnabled = true;
        }
        else
        {
            SelectButton.IsEnabled = false;
            ModelsListBox.IsEnabled = false;
        }
    }

    private void UseOpenRouterRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        _useLocalModel = false;
        SelectButton.IsEnabled = true;
        ModelsListBox.IsEnabled = false;
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_useLocalModel && _selectedModel != null)
        {
            SelectedSettings = new AIChatSettings
            {
                UseLocalModel = true,
                LocalModelPath = _selectedModel.Path,
                UseOpenRouterClient = false
            };
        }
        else
        {
            SelectedSettings = new AIChatSettings
            {
                UseLocalModel = false,
                UseOpenRouterClient = true
            };
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}