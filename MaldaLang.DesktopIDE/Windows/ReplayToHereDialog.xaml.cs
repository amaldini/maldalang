// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.DesktopIDE.Windows;

using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

public partial class ReplayToHereDialog : Window
{
    public string WorkingDirectory { get; private set; } = "";

    public ReplayToHereDialog(IEnumerable<string> filePaths, string? defaultWorkingDir = null)
    {
        InitializeComponent();
        WorkingDirTextBox.Text = defaultWorkingDir ?? Path.Combine(Path.GetTempPath(), "TraceReplay_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        FilesListBox.ItemsSource = new List<string>(filePaths ?? System.Array.Empty<string>());
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select a file in the target folder (the folder will be used)",
            InitialDirectory = WorkingDirTextBox.Text.Trim(),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.FileName))
        {
            var dir = Path.GetDirectoryName(dlg.FileName);
            if (!string.IsNullOrEmpty(dir))
                WorkingDirTextBox.Text = dir;
        }
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        var dir = WorkingDirTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(dir))
        {
            MessageBox.Show(this, "Please enter a working directory.", "Replay to Here", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        WorkingDirectory = dir;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
