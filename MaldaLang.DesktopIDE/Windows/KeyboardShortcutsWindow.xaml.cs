// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows;

namespace MaldaLang.DesktopIDE.Windows;

public partial class KeyboardShortcutsWindow : Window
{
    public const string ShortcutText =
        """
        File
          Ctrl+N          New file
          Ctrl+O          Open file
          Ctrl+S          Save
          Ctrl+Shift+S    Save as
          Alt+F4          Exit

        Edit
          Ctrl+Z          Undo
          Ctrl+Y          Redo
          Ctrl+X          Cut
          Ctrl+C          Copy
          Ctrl+V          Paste
          Ctrl+A          Select all
          Ctrl+F          Find
          Ctrl+H          Replace
          Ctrl+Alt+F      Format document
          Ctrl+.          Quick fix
          Ctrl+Alt+R      Rename symbol

        View
          Ctrl+Shift+P    Command palette
          Ctrl+Shift+L    Toggle syntax panel
          Shift+F7        Maximize / restore AI panel
          Shift+F6        Maximize / restore web preview
          Esc             Restore a maximized panel

        Run
          Ctrl+F5         Run without debugging
          F5              Start debugging / continue
          Shift+F5        Stop
          F6              Preview web
          Ctrl+Shift+B    Compile

        Debug
          F10             Step over
          F11             Step into
          Shift+F11       Step out
          F9              Toggle breakpoint
        """;

    public KeyboardShortcutsWindow()
    {
        InitializeComponent();
        ShortcutsText.Text = ShortcutText;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
