// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class MCPServerConfigWindow : Window
{
    private readonly MCPServerConfigService _configService;
    private readonly MCPServerConnectionService _connectionService;
    private readonly ThemeService _themeService;
    private MCPServerConfig? _currentServer;
    private bool _isDirty = false;

    public MCPServerConfigWindow(MCPServerConfigService configService, MCPServerConnectionService connectionService, ThemeService themeService)
    {
        InitializeComponent();
        _configService = configService;
        _connectionService = connectionService;
        _themeService = themeService;

        // Subscribe to theme changes
        _themeService.ThemeChanged += OnThemeChanged;
        
        // Apply theme immediately after InitializeComponent (like ModelBrowserWindow does)
        try
        {
            if (_themeService?.CurrentTheme != null)
            {
                ApplyTheme(_themeService.CurrentTheme);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying theme in constructor: {ex.Message}\n{ex.StackTrace}");
            // Continue - window will use default theme
        }
        
        // Load servers when window is loaded to ensure all controls are initialized
        Loaded += Window_Loaded;
    }
    
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Load servers after window is fully initialized
            LoadServers();
        }
        catch (Exception ex)
        {
            // Log error
            System.Diagnostics.Debug.WriteLine($"Error in Window_Loaded: {ex.Message}\n{ex.StackTrace}");
            // Don't show message box here as it might cause issues during window initialization
        }
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
        if (theme == null || Resources == null)
            return;
            
        try
        {
            // Update all dynamic resources
            Resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
            Resources["TextForegroundBrush"] = new SolidColorBrush(theme.TextForeground);
            Resources["TextSecondaryBrush"] = new SolidColorBrush(theme.TextSecondary);
            Resources["BorderBrush"] = new SolidColorBrush(theme.BorderColor);
            Resources["ListBackgroundBrush"] = new SolidColorBrush(theme.ListBackground);
            Resources["ListForegroundBrush"] = new SolidColorBrush(theme.ListForeground);
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
            Resources["InputBackgroundBrush"] = new SolidColorBrush(theme.InputBackground);
            Resources["InputForegroundBrush"] = new SolidColorBrush(theme.InputForeground);
            Resources["InputBorderBrush"] = new SolidColorBrush(theme.InputBorder);
            Resources["ErrorBrush"] = new SolidColorBrush(theme.ErrorColor);
            // Use a green color that's visible in both light and dark themes
            Resources["SuccessBrush"] = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // #4CAF50 - Material Design green
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying theme: {ex.Message}\n{ex.StackTrace}");
            // Don't throw - allow window to continue loading with default theme
        }
    }

    private void LoadServers()
    {
        try
        {
            if (ServersListBox == null)
                return;
                
            var config = _configService?.LoadConfig();
            if (config == null)
                return;
                
            var serverViewModels = config.Servers.Select(s => new ServerViewModel(s, _connectionService)).ToList();
            ServersListBox.ItemsSource = serverViewModels;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading servers: {ex.Message}\n{ex.StackTrace}");
            // Don't throw - allow window to continue loading
        }
    }

    private void ServersListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ServersListBox.SelectedItem as ServerViewModel;
        if (selected != null)
        {
            LoadServerDetails(selected.Server);
        }
        else
        {
            ClearDetails();
        }
    }

    private void LoadServerDetails(MCPServerConfig server)
    {
        _currentServer = server;
        _isDirty = false;

        DetailsHeader.Text = $"Configure: {server.Name}";
        NameTextBox.Text = server.Name;
        CommandTextBox.Text = server.Command;
        ArgsTextBox.Text = string.Join("\n", server.Args);
        EnvTextBox.Text = string.Join("\n", server.Env.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        AutoConnectCheckBox.IsChecked = server.AutoConnect;

        UpdateStatus();
        UpdateToolsList();
        UpdateButtons();

        DetailsPanel.IsEnabled = true;
        DeleteButton.IsEnabled = true;
    }

    private void ClearDetails()
    {
        _currentServer = null;
        _isDirty = false;

        DetailsHeader.Text = "Select a server to configure";
        NameTextBox.Text = "";
        CommandTextBox.Text = "";
        ArgsTextBox.Text = "";
        EnvTextBox.Text = "";
        AutoConnectCheckBox.IsChecked = false;
        StatusTextBlock.Text = "Not connected";
        ToolsListBox.ItemsSource = null;

        DetailsPanel.IsEnabled = false;
        DeleteButton.IsEnabled = false;
        UpdateButtons();
    }

    private void UpdateStatus()
    {
        if (StatusTextBlock == null)
            return;
            
        if (_currentServer == null)
        {
            StatusTextBlock.Text = "Not connected";
            var brush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
            if (brush != null)
                StatusTextBlock.Foreground = brush;
            return;
        }

        var isConnected = _connectionService.IsServerConnected(_currentServer.Name);
        _currentServer.IsConnected = isConnected;

        if (isConnected)
        {
            StatusTextBlock.Text = "Connected";
            var brush = TryFindResource("SuccessBrush") as System.Windows.Media.Brush;
            if (brush != null)
                StatusTextBlock.Foreground = brush;
        }
        else
        {
            StatusTextBlock.Text = "Disconnected";
            var brush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
            if (brush != null)
                StatusTextBlock.Foreground = brush;
        }
    }

    private void UpdateToolsList()
    {
        if (_currentServer == null)
        {
            ToolsListBox.ItemsSource = null;
            return;
        }

        if (_connectionService.IsServerConnected(_currentServer.Name))
        {
            // Get tools from ToolRegistry that start with mcp:serverName:
            var toolPrefix = $"mcp:{_currentServer.Name}:";
            var allTools = MaldaLang.Interpreter.ToolRegistry.Instance.GetToolNames();
            var serverTools = allTools.Where(t => t.StartsWith(toolPrefix))
                                     .Select(t => t.Substring(toolPrefix.Length))
                                     .ToList();
            ToolsListBox.ItemsSource = serverTools;
        }
        else
        {
            ToolsListBox.ItemsSource = null;
        }
    }

    private void UpdateButtons()
    {
        var hasServer = _currentServer != null;
        var isConnected = hasServer && _connectionService.IsServerConnected(_currentServer!.Name);

        TestButton.IsEnabled = hasServer && !string.IsNullOrWhiteSpace(CommandTextBox.Text);
        ConnectButton.IsEnabled = hasServer && !isConnected;
        DisconnectButton.IsEnabled = hasServer && isConnected;
        SaveButton.IsEnabled = hasServer && _isDirty;
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isDirty = true;
        UpdateButtons();
    }

    private void CommandTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isDirty = true;
        UpdateButtons();
    }

    private void ArgsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isDirty = true;
    }

    private void EnvTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _isDirty = true;
    }

    private void AutoConnectCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        _isDirty = true;
    }

    private void AutoConnectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        _isDirty = true;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var newServer = new MCPServerConfig
        {
            Name = "New Server",
            Command = "",
            Args = new List<string>(),
            Env = new Dictionary<string, string>(),
            AutoConnect = false
        };

        var config = _configService.LoadConfig();
        config.Servers.Add(newServer);
        _configService.SaveConfig(config);

        LoadServers();
        ServersListBox.SelectedItem = ServersListBox.Items.Cast<ServerViewModel>()
            .FirstOrDefault(vm => vm.Server == newServer);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete server '{_currentServer.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            // Disconnect if connected
            if (_connectionService.IsServerConnected(_currentServer.Name))
            {
                _connectionService.DisconnectServer(_currentServer.Name);
            }

            var config = _configService.LoadConfig();
            config.Servers.RemoveAll(s => s.Name == _currentServer.Name);
            _configService.SaveConfig(config);

            LoadServers();
            ClearDetails();
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
            return;

        TestButton.IsEnabled = false;
        StatusTextBlock.Text = "Testing connection...";
        var brush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
        if (brush != null)
            StatusTextBlock.Foreground = brush;

        try
        {
            // Create a temporary config with current values
            var tempConfig = new MCPServerConfig
            {
                Name = _currentServer.Name,
                Command = CommandTextBox.Text,
                Args = ArgsTextBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
                Env = ParseEnvVars(EnvTextBox.Text)
            };

            // Try to connect
            var client = new MaldaLang.BuiltIns.MCP.MCPClient(tempConfig.Name);
            var connected = await client.ConnectAsync(
                tempConfig.Command,
                tempConfig.Args,
                tempConfig.Env.Count > 0 ? tempConfig.Env : null);

            if (connected)
            {
                await client.DiscoverToolsAsync();
                client.Dispose();

                StatusTextBlock.Text = $"Connection successful! Found {client.Tools.Count} tool(s).";
                var successBrush = TryFindResource("SuccessBrush") as System.Windows.Media.Brush;
                if (successBrush != null)
                    StatusTextBlock.Foreground = successBrush;
            }
            else
            {
                StatusTextBlock.Text = "Connection failed.";
                var errorBrush = TryFindResource("ErrorBrush") as System.Windows.Media.Brush;
                if (errorBrush != null)
                    StatusTextBlock.Foreground = errorBrush;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Connection error: {ex.Message}";
            var errorBrush = TryFindResource("ErrorBrush") as System.Windows.Media.Brush;
            if (errorBrush != null)
                StatusTextBlock.Foreground = errorBrush;
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
            return;

        ConnectButton.IsEnabled = false;
        StatusTextBlock.Text = "Connecting...";
        var brush = TryFindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
        if (brush != null)
            StatusTextBlock.Foreground = brush;

        try
        {
            // Save current values first
            SaveCurrentServer();

            await _connectionService.ConnectServerAsync(_currentServer.Name);
            UpdateStatus();
            UpdateToolsList();
            UpdateButtons();

            MessageBox.Show(
                $"Successfully connected to '{_currentServer.Name}'.",
                "Connection Successful",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to connect: {ex.Message}",
                "Connection Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            UpdateStatus();
        }
        finally
        {
            UpdateButtons();
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
            return;

        _connectionService.DisconnectServer(_currentServer.Name);
        UpdateStatus();
        UpdateToolsList();
        UpdateButtons();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentServer == null)
            return;

        SaveCurrentServer();
        _isDirty = false;
        UpdateButtons();
        LoadServers();

        MessageBox.Show(
            "Server configuration saved.",
            "Saved",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SaveCurrentServer()
    {
        if (_currentServer == null)
            return;

        var config = _configService.LoadConfig();
        var server = config.Servers.FirstOrDefault(s => s.Name == _currentServer.Name);
        if (server != null)
        {
            // Update existing server
            var wasConnected = server.IsConnected;
            server.Name = NameTextBox.Text;
            server.Command = CommandTextBox.Text;
            server.Args = ArgsTextBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
            server.Env = ParseEnvVars(EnvTextBox.Text);
            server.AutoConnect = AutoConnectCheckBox.IsChecked ?? false;
            server.IsConnected = wasConnected; // Preserve connection state
        }

        _configService.SaveConfig(config);
        _currentServer = server;
    }

    private Dictionary<string, string> ParseEnvVars(string envText)
    {
        var env = new Dictionary<string, string>();
        var lines = envText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2)
            {
                env[parts[0].Trim()] = parts[1].Trim();
            }
        }
        return env;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    // ViewModel for server list display
    private class ServerViewModel
    {
        public MCPServerConfig Server { get; }
        private readonly MCPServerConnectionService _connectionService;

        public string Name => Server.Name;
        public string Command => Server.Command;
        public string StatusText
        {
            get
            {
                if (_connectionService.IsServerConnected(Server.Name))
                    return "● Connected";
                return "○ Disconnected";
            }
        }

        public ServerViewModel(MCPServerConfig server, MCPServerConnectionService connectionService)
        {
            Server = server;
            _connectionService = connectionService;
        }
    }
}