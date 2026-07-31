// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0
//
// Window to continue agent execution from a trace step. Shows restored conversation
// and lets the user call think(prompt) to continue.

namespace MaldaLang.DesktopIDE.Windows;

using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MaldaLang.BuiltIns;
using MaldaLang.DesktopIDE.Models;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.Interpreter;
using MaldaLang.TraceViewer;
using MaldaLang.Runtime.Tracing;
using ValueType = MaldaLang.Interpreter.ValueType;

public partial class AgentReplayWindow : Window
{
    private readonly ThemeService _themeService;
    private readonly AgentInstance _agent;
    private readonly StringBuilder _messagesText = new();
    private readonly AgentSessionContext? _branchSessionContext;

    /// <summary>Run-from-here: no tracing.</summary>
    public AgentReplayWindow(ThemeService themeService, string workingDir, AgentReplayState state)
        : this(themeService, workingDir, state, null, null) { }

    /// <summary>Branch-from-here: enable tracing with the given session id and trace base directory.</summary>
    public AgentReplayWindow(ThemeService themeService, string workingDir, AgentReplayState state, string? branchSessionId, string? traceBaseDir)
    {
        InitializeComponent();
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));

        var name = state.AgentName ?? "Replay";
        var instructions = state.SystemPrompt ?? "";
        if (!string.IsNullOrEmpty(branchSessionId))
            _branchSessionContext = AgentSession.Start(name, null, branchSessionId);
        else
            _branchSessionContext = null;

        _agent = new DevAgentInstance(name, "Replay agent", instructions, (LLMClientInstance?)null, workingDir);

        var convVal = _agent.GetConversation();
        if (convVal.Type == ValueType.Object && convVal.AsObject() is ConversationInstance conv)
        {
            TraceReplayEngine.RestoreConversationState(conv, state);
        }

        if (!string.IsNullOrEmpty(branchSessionId) && !string.IsNullOrEmpty(traceBaseDir))
            _agent.EnableTracing(null, traceBaseDir);

        ApplyTheme(_themeService.CurrentTheme);
        _themeService.ThemeChanged += (_, theme) => Dispatcher.Invoke(() => ApplyTheme(theme));

        RefreshMessages(state);
    }

    private void ApplyTheme(Theme theme)
    {
        Resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
        Resources["MainBackgroundBrush"] = new SolidColorBrush(theme.MainBackground);
        Resources["ToolbarBackgroundBrush"] = new SolidColorBrush(theme.ToolbarBackground);
        Resources["ToolbarBorderBrush"] = new SolidColorBrush(theme.ToolbarBorder);
        Resources["TextForegroundBrush"] = new SolidColorBrush(theme.TextForeground);
        Resources["TextSecondaryBrush"] = new SolidColorBrush(theme.TextSecondary);
        Resources["ButtonBackgroundBrush"] = new SolidColorBrush(theme.ButtonBackground);
        Resources["ButtonForegroundBrush"] = new SolidColorBrush(theme.ButtonForeground);
        Resources["ButtonBorderBrush"] = new SolidColorBrush(theme.ButtonBorder);
        Resources["InputBackgroundBrush"] = new SolidColorBrush(theme.InputBackground);
        Resources["InputForegroundBrush"] = new SolidColorBrush(theme.InputForeground);
        Resources["InputBorderBrush"] = new SolidColorBrush(theme.InputBorder);
        Resources["ListBackgroundBrush"] = new SolidColorBrush(theme.ListBackground);
        Resources["ListForegroundBrush"] = new SolidColorBrush(theme.ListForeground);
        Resources["ListBorderBrush"] = new SolidColorBrush(theme.ListBorder);
    }

    private void RefreshMessages(AgentReplayState state)
    {
        _messagesText.Clear();
        foreach (var msg in state.Messages)
        {
            if (msg.Type != ValueType.Object) continue;
            var obj = msg.AsObject();
            var role = GetStr(obj, "role") ?? "user";
            var content = GetStr(obj, "content") ?? "";
            _messagesText.Append($"[{role}] ");
            _messagesText.AppendLine(content.Length > 500 ? content.Substring(0, 500) + "…" : content);
            _messagesText.AppendLine();
        }
        MessagesTextBlock.Text = _messagesText.ToString();
        MessagesScrollViewer.ScrollToEnd();
    }

    private static string? GetStr(ObjectInstance o, string key)
    {
        try
        {
            var v = o.Get(key, null);
            return v?.Type == ValueType.String ? v.AsString() : null;
        }
        catch { return null; }
    }

    private async void ThinkButton_Click(object sender, RoutedEventArgs e)
    {
        var prompt = PromptTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(prompt))
        {
            MessageBox.Show(this, "Enter a prompt.", "Run from Here", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ThinkButton.IsEnabled = false;
        PromptTextBox.IsEnabled = false;
        var branchCtx = _branchSessionContext;
        try
        {
            var result = await Task.Run(() =>
            {
                if (branchCtx != null)
                    AgentSession.SetCurrent(branchCtx);
                try
                {
                    return _agent.Think(RuntimeValue.String(prompt));
                }
                finally
                {
                    if (branchCtx != null)
                        AgentSession.SetCurrent(null);
                }
            });
            Dispatcher.Invoke(() =>
            {
                var content = ExtractContent(result);
                _messagesText.Append("[user] ");
                _messagesText.AppendLine(prompt);
                _messagesText.AppendLine();
                _messagesText.Append("[assistant] ");
                _messagesText.AppendLine(content);
                _messagesText.AppendLine();
                MessagesTextBlock.Text = _messagesText.ToString();
                MessagesScrollViewer.ScrollToEnd();
                PromptTextBox.Clear();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
                MessageBox.Show(this, ex.Message, "Think failed", MessageBoxButton.OK, MessageBoxImage.Warning));
        }
        finally
        {
            ThinkButton.IsEnabled = true;
            PromptTextBox.IsEnabled = true;
        }
    }

    private static string ExtractContent(RuntimeValue result)
    {
        if (result.Type != ValueType.Object) return result.ToString();
        try
        {
            var o = result.AsObject();
            var v = o.Get("content", null);
            return v?.Type == ValueType.String ? v.AsString() : result.ToString();
        }
        catch
        {
            return result.ToString();
        }
    }
}
