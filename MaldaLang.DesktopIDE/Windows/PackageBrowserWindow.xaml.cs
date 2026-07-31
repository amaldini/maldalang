// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Windows;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaldaLang.PackageManager;
using MaldaLang.PackageManager.Models;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Models;

public partial class PackageBrowserWindow : Window
{
    private readonly MaldaLang.PackageManager.PackageManager _packageManager;
    private readonly PackageStorage _storage;
    private readonly PackageRegistry _registry;
    private readonly ThemeService? _themeService;
    
    public PackageBrowserWindow(ThemeService? themeService = null)
    {
        InitializeComponent();
        _themeService = themeService;
        _packageManager = new MaldaLang.PackageManager.PackageManager();
        _storage = new PackageStorage();
        _registry = new PackageRegistry(_storage);
        
        // Apply theme if available
        if (_themeService != null)
        {
            ApplyTheme(_themeService.CurrentTheme);
            _themeService.ThemeChanged += OnThemeChanged;
        }
        
        LoadAvailablePackages();
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
    }
    
    private void OnThemeChanged(object? sender, Theme theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyTheme(theme);
        });
    }
    
    private void LoadInstalledPackages()
    {
        var packages = new List<PackageInfo>();
        var installedPackages = _storage.GetInstalledPackages();
        
        foreach (var packageName in installedPackages)
        {
            var versions = _storage.GetInstalledVersions(packageName);
            foreach (var version in versions)
            {
                var metadata = _storage.LoadPackageMetadata(packageName, version);
                packages.Add(new PackageInfo
                {
                    Name = packageName,
                    Version = version,
                    Description = metadata?.Description ?? "No description"
                });
            }
        }
        
        PackagesDataGrid.ItemsSource = packages;
    }
    
    private async void LoadAvailablePackages()
    {
        try
        {
            var packages = await _packageManager.ListAllPackagesAsync();
            PackagesDataGrid.ItemsSource = packages;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading available packages: {ex.Message}\n\nShowing installed packages instead.", 
                "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            LoadInstalledPackages();
        }
    }
    
    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        var query = SearchTextBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            LoadAvailablePackages();
            return;
        }
        
        try
        {
            var results = await _packageManager.SearchAsync(query);
            PackagesDataGrid.ItemsSource = results;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error searching packages: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Auto-search could be implemented here
    }
    
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAvailablePackages();
    }
    
    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesDataGrid.SelectedItem is PackageInfo selectedPackage)
        {
            try
            {
                var success = await _packageManager.InstallAsync(selectedPackage.Name, selectedPackage.Version);
                if (success)
                {
                    MessageBox.Show($"Package {selectedPackage.Name} installed successfully", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    // Refresh the available packages list
                    LoadAvailablePackages();
                }
                else
                {
                    MessageBox.Show($"Failed to install package {selectedPackage.Name}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error installing package: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            MessageBox.Show("Please select a package to install", 
                "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesDataGrid.SelectedItem is PackageInfo selectedPackage)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to uninstall {selectedPackage.Name}@{selectedPackage.Version}?",
                "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var success = _packageManager.Uninstall(selectedPackage.Name, selectedPackage.Version);
                    if (success)
                    {
                        MessageBox.Show($"Package {selectedPackage.Name} uninstalled successfully", 
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        // Refresh the available packages list
                        LoadAvailablePackages();
                    }
                    else
                    {
                        MessageBox.Show($"Failed to uninstall package {selectedPackage.Name}", 
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error uninstalling package: {ex.Message}", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        else
        {
            MessageBox.Show("Please select a package to uninstall", 
                "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    
    private void PackagesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update UI based on selection if needed
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        if (_themeService != null)
        {
            _themeService.ThemeChanged -= OnThemeChanged;
        }
        base.OnClosed(e);
    }
}
