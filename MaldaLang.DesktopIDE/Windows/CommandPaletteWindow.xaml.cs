// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MaldaLang.DesktopIDE.Services;

namespace MaldaLang.DesktopIDE.Windows;

public partial class CommandPaletteWindow : Window
{
    private readonly IReadOnlyList<PaletteCommand> _commands;

    public PaletteCommand? SelectedCommand { get; private set; }

    public CommandPaletteWindow(IReadOnlyList<PaletteCommand> commands)
    {
        _commands = commands;
        InitializeComponent();
        ApplyFilter();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        FilterTextBox.Focus();
    }

    private void FilterTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var matches = _commands.Where(command => command.Matches(FilterTextBox.Text)).ToList();
        CommandList.ItemsSource = matches;
        if (matches.Count > 0)
        {
            CommandList.SelectedIndex = 0;
        }
    }

    private void FilterTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down && CommandList.Items.Count > 0)
        {
            CommandList.Focus();
            if (CommandList.SelectedIndex < 0)
            {
                CommandList.SelectedIndex = 0;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            AcceptSelection();
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !ReferenceEquals(e.OriginalSource, FilterTextBox))
        {
            AcceptSelection();
            e.Handled = true;
        }
    }

    private void CommandList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        AcceptSelection();
    }

    private void AcceptSelection()
    {
        if (CommandList.SelectedItem is not PaletteCommand command)
        {
            return;
        }

        SelectedCommand = command;
        DialogResult = true;
    }
}
