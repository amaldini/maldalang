// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

using System.Windows.Media;
using System.Text;
using MaldaLang.DesktopIDE.Services;
using MaldaLang.DesktopIDE.Models;
using System.Windows.Threading;

namespace MaldaLang.DesktopIDE;

public partial class MainWindow
{
    private void ToolCallsWebBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        // Scroll to bottom when content loads and set zoom
        // The JavaScript in the HTML handles this, but we also try here as a backup
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (ToolCallsWebBrowser.Document != null)
                {
                    // Set zoom to 150% for better readability
                    SetWebBrowserZoom(ToolCallsWebBrowser, 150);
                    
                    var script = "window.scrollTo(0, document.body.scrollHeight);";
                    dynamic doc = ToolCallsWebBrowser.Document;
                    if (doc != null)
                    {
                        dynamic window = doc.parentWindow;
                        if (window != null)
                        {
                            window.execScript(script, "JavaScript");
                        }
                    }
                }
            }
            catch
            {
                // JavaScript in HTML will handle scrolling if this fails
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void OutputWebBrowser_LoadCompleted(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        // Set zoom to 150% for better readability
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                SetWebBrowserZoom(OutputWebBrowser, 150);
            }
            catch
            {
                // Ignore zoom errors
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    
    private void OnToolCallLogged(ToolCallLogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateToolCallsDisplay();
        });
    }
    
    private string ColorToHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
    
    private void SetWebBrowserZoom(System.Windows.Controls.WebBrowser browser, int zoomPercent = 150)
    {
        try
        {
            if (browser.Document != null)
            {
                dynamic doc = browser.Document;
                if (doc != null)
                {
                    dynamic window = doc.parentWindow;
                    if (window != null)
                    {
                        // Use CSS zoom property - works reliably in IE/Trident engine
                        var script = $@"
                            (function() {{
                                document.body.style.zoom = '{zoomPercent}%';
                                if (document.documentElement) {{
                                    document.documentElement.style.zoom = '{zoomPercent}%';
                                }}
                            }})();
                        ";
                        window.execScript(script, "JavaScript");
                    }
                }
            }
        }
        catch
        {
            // If zoom fails, the increased font sizes will still make text more readable
        }
    }
    
    private string GetScrollbarCss(Theme theme)
    {
        // Calculate scrollbar colors based on theme (same logic as in ApplyTheme)
        var isDarkTheme = theme.ListBackground.R < 128;
        Color scrollBarTrack, scrollBarThumb, scrollBarThumbHover;
        
        if (isDarkTheme)
        {
            // Dark theme: use darker, more visible colors
            // For very dark backgrounds, use medium-dark grays for better visibility
            var bgLuminance = (theme.ListBackground.R + theme.ListBackground.G + theme.ListBackground.B) / 3.0;
            if (bgLuminance < 30)
            {
                // Very dark background - use medium-dark grays
                scrollBarTrack = Color.FromRgb(40, 40, 40);
                scrollBarThumb = Color.FromRgb(70, 70, 70);
                scrollBarThumbHover = Color.FromRgb(90, 90, 90);
            }
            else
            {
                // Moderately dark background - slightly lighter
                scrollBarTrack = Color.FromRgb(
                    (byte)Math.Min(255, theme.ListBackground.R + 30),
                    (byte)Math.Min(255, theme.ListBackground.G + 30),
                    (byte)Math.Min(255, theme.ListBackground.B + 30));
                scrollBarThumb = Color.FromRgb(
                    (byte)Math.Min(255, theme.ListBackground.R + 60),
                    (byte)Math.Min(255, theme.ListBackground.G + 60),
                    (byte)Math.Min(255, theme.ListBackground.B + 60));
                scrollBarThumbHover = Color.FromRgb(
                    (byte)Math.Min(255, theme.ListBackground.R + 80),
                    (byte)Math.Min(255, theme.ListBackground.G + 80),
                    (byte)Math.Min(255, theme.ListBackground.B + 80));
            }
        }
        else
        {
            // Light theme: slightly darker colors
            scrollBarTrack = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 20),
                (byte)Math.Max(0, theme.ListBackground.G - 20),
                (byte)Math.Max(0, theme.ListBackground.B - 20));
            scrollBarThumb = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 40),
                (byte)Math.Max(0, theme.ListBackground.G - 40),
                (byte)Math.Max(0, theme.ListBackground.B - 40));
            scrollBarThumbHover = Color.FromRgb(
                (byte)Math.Max(0, theme.ListBackground.R - 60),
                (byte)Math.Max(0, theme.ListBackground.G - 60),
                (byte)Math.Max(0, theme.ListBackground.B - 60));
        }
        
        // Generate CSS for scrollbars - support multiple browsers
        // Note: WPF WebBrowser uses IE/Trident engine which has limited scrollbar styling support
        // IE/Edge (Trident) scrollbar properties - these must be on body/html elements
        var ieScrollbarCss = $@"
        body, html {{
            scrollbar-base-color: {ColorToHex(scrollBarTrack)};
            scrollbar-face-color: {ColorToHex(scrollBarThumb)};
            scrollbar-track-color: {ColorToHex(scrollBarTrack)};
            scrollbar-arrow-color: {ColorToHex(scrollBarThumb)};
            scrollbar-shadow-color: {ColorToHex(scrollBarThumb)};
            scrollbar-highlight-color: {ColorToHex(scrollBarThumbHover)};
            scrollbar-3dlight-color: {ColorToHex(scrollBarTrack)};
            scrollbar-darkshadow-color: {ColorToHex(scrollBarThumb)};
        }}
        ";
        
        // Webkit scrollbars (Chrome, Edge Chromium, Safari) - for newer engines
        var webkitScrollbarCss = $@"
        ::-webkit-scrollbar {{
            width: 12px !important;
            height: 12px !important;
        }}
        ::-webkit-scrollbar-track {{
            background: {ColorToHex(scrollBarTrack)} !important;
        }}
        ::-webkit-scrollbar-thumb {{
            background: {ColorToHex(scrollBarThumb)} !important;
            border-radius: 6px !important;
        }}
        ::-webkit-scrollbar-thumb:hover {{
            background: {ColorToHex(scrollBarThumbHover)} !important;
        }}
        ::-webkit-scrollbar-corner {{
            background: {ColorToHex(scrollBarTrack)} !important;
        }}
        ";
        
        // Firefox scrollbars
        var firefoxScrollbarCss = $@"
        * {{
            scrollbar-width: thin !important;
            scrollbar-color: {ColorToHex(scrollBarThumb)} {ColorToHex(scrollBarTrack)} !important;
        }}
        ";
        
        return ieScrollbarCss + webkitScrollbarCss + firefoxScrollbarCss;
    }
    
    private void UpdateToolCallsDisplay()
    {
        var theme = _themeService.CurrentTheme;
        var entries = _toolCallLogService.GetEntriesForHtml();
        var contextUsage = _toolCallLogService.GetContextUsage();
        
        var html = new StringBuilder();
        html.AppendLine("<html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"UTF-8\"><style>");
        html.AppendLine($"html {{ background: {ColorToHex(theme.ListBackground)}; margin: 0; padding: 0; height: 100%; }}");
        html.AppendLine($"body {{ font-family: Consolas, Monaco, monospace; font-size: 16px; color: {ColorToHex(theme.TextForeground)}; background: {ColorToHex(theme.ListBackground)}; padding: 8px; margin: 0; min-height: 100vh; }}");
        var primaryColor = theme.PrimaryButtonBackground;
        var primaryDark = Color.FromRgb((byte)(primaryColor.R * 0.7), (byte)(primaryColor.G * 0.7), (byte)(primaryColor.B * 0.7));
        html.AppendLine($".context-summary {{ background: {ColorToHex(primaryColor)} !important; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; padding: 12px; border-radius: 6px; margin-top: 12px; margin-bottom: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.2); border: 2px solid {ColorToHex(primaryDark)}; }}");
        html.AppendLine($".context-summary-title {{ font-weight: bold; font-size: 13px; margin-bottom: 8px; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; }}");
        html.AppendLine(".context-summary-stats { display: flex; flex-wrap: wrap; gap: 24px; margin-bottom: 8px; }");
        html.AppendLine(".context-stat { display: flex; flex-direction: column; min-width: 80px; padding-right: 12px; }");
        html.AppendLine($".context-stat-label {{ font-size: 10px; margin-bottom: 2px; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; opacity: 0.9; }}");
        html.AppendLine($".context-stat-value {{ font-size: 14px; font-weight: bold; color: {ColorToHex(theme.PrimaryButtonForeground)} !important; }}");
        html.AppendLine(".context-total-bar { width: 100%; height: 8px; background: rgba(255,255,255,0.3); border-radius: 4px; margin-top: 8px; overflow: hidden; }");
        html.AppendLine(".context-total-bar-fill { height: 100%; transition: width 0.3s ease; background: #FFFFFF; }");
        var entryBg = theme.ListBackground.R < 128 ? Color.FromRgb((byte)(theme.ListBackground.R + 20), (byte)(theme.ListBackground.G + 20), (byte)(theme.ListBackground.B + 20)) : Color.FromRgb((byte)(theme.ListBackground.R - 10), (byte)(theme.ListBackground.G - 10), (byte)(theme.ListBackground.B - 10));
        html.AppendLine($".entry {{ margin-bottom: 12px; padding: 8px; background: {ColorToHex(entryBg)}; border-left: 3px solid {ColorToHex(theme.DebugAccent)}; }}");
        html.AppendLine($".timestamp {{ color: {ColorToHex(theme.TextSecondary)}; font-size: 15px; }}");
        html.AppendLine($".tool-name {{ color: {ColorToHex(theme.DebugAccent)}; font-weight: bold; font-size: 18px; margin: 4px 0; }}");
        html.AppendLine($".label {{ color: {ColorToHex(theme.TextSecondary)}; font-size: 15px; margin-top: 4px; }}");
        html.AppendLine($".arguments {{ color: {ColorToHex(theme.TextForeground)}; font-size: 16px; margin-left: 16px; white-space: pre-wrap; word-wrap: break-word; }}");
        html.AppendLine($".result {{ color: #4caf50; font-size: 16px; margin-left: 16px; white-space: pre-wrap; word-wrap: break-word; }}");
        html.AppendLine($".error {{ color: {ColorToHex(theme.ErrorColor)}; font-size: 16px; margin-left: 16px; white-space: pre-wrap; word-wrap: break-word; }}");
        html.AppendLine(".size-badge { display: inline-block; padding: 2px 6px; border-radius: 3px; font-size: 10px; font-weight: bold; margin-left: 6px; }");
        html.AppendLine(".size-small { background: #c8e6c9; color: #2e7d32; }");
        html.AppendLine(".size-medium { background: #fff9c4; color: #f57f17; }");
        html.AppendLine(".size-large { background: #ffe0b2; color: #e65100; }");
        html.AppendLine(".size-xlarge { background: #ffcdd2; color: #c62828; }");
        html.AppendLine(".size-bar-container { width: 100%; height: 4px; background: #e0e0e0; border-radius: 2px; margin-top: 2px; overflow: hidden; }");
        html.AppendLine(".size-bar { height: 100%; transition: width 0.3s ease; }");
        html.AppendLine(".size-bar-small { background: #4caf50; }");
        html.AppendLine(".size-bar-medium { background: #ffc107; }");
        html.AppendLine(".size-bar-large { background: #ff9800; }");
        html.AppendLine(".size-bar-xlarge { background: #f44336; }");
        html.AppendLine(".copy-button { display: inline-block; margin-left: 8px; padding: 4px 8px; background: #d32f2f; color: white; border: none; border-radius: 3px; cursor: pointer; font-size: 10px; font-weight: bold; }");
        html.AppendLine(".copy-button:hover { background: #b71c1c; }");
        html.AppendLine(".copy-button:active { background: #8b0000; }");
        html.AppendLine(".copy-button.copied { background: #4caf50; }");
        html.AppendLine(GetScrollbarCss(theme));
        html.AppendLine("</style></head><body>");
        
        if (entries.Count == 0)
        {
            html.AppendLine($"<p style='color: {ColorToHex(theme.TextSecondary)}; font-style: italic; padding: 8px;'>No tool calls yet. Tool calls will appear here when agents use tools.</p>");
        }
        else
        {
            // Collect full arguments data for entries with errors
            var fullArgumentsData = new Dictionary<int, string>();
            int entryIndex = 0;
            
            foreach (var entry in entries)
            {
                html.AppendLine("<div class='entry'>");
                html.AppendLine($"<div class='timestamp'>[{entry.Timestamp:HH:mm:ss}]</div>");
                html.AppendLine($"<div class='tool-name'>🔧 {System.Security.SecurityElement.Escape(entry.ToolName)}</div>");
                
                if (!string.IsNullOrEmpty(entry.Arguments))
                {
                    var escapedArgs = System.Security.SecurityElement.Escape(entry.Arguments);
                    var argsSizeInfo = GetSizeInfo(entry.ArgumentsSize);
                    html.AppendLine($"<div class='label'>📥 Arguments: <span class='size-badge {argsSizeInfo.Class}'>{argsSizeInfo.Label}</span></div>");
                    html.AppendLine($"<div class='size-bar-container'><div class='size-bar {argsSizeInfo.BarClass}' style='width: {argsSizeInfo.Percentage:F1}%'></div></div>");
                    html.AppendLine($"<div class='arguments'>{escapedArgs}</div>");
                }
                
                if (!string.IsNullOrEmpty(entry.Result))
                {
                    var escapedResult = System.Security.SecurityElement.Escape(entry.Result);
                    var resultClass = entry.IsError ? "error" : "result";
                    var resultLabel = entry.IsError ? "❌ Error:" : "✅ Result:";
                    var resultSizeInfo = GetSizeInfo(entry.ResultSize);
                    html.AppendLine($"<div class='label'>{resultLabel} <span class='size-badge {resultSizeInfo.Class}'>{resultSizeInfo.Label}</span>");
                    
                    // Add copy button for errors with full arguments available
                    if (entry.IsError && !string.IsNullOrEmpty(entry.FullArguments) && entry.FullArguments != entry.Arguments)
                    {
                        fullArgumentsData[entryIndex] = entry.FullArguments;
                        html.AppendLine($"<button class='copy-button' onclick='copyFullArguments({entryIndex}, this)' title='Copy full tool parameters'>📋 Copy Parameters</button>");
                    }
                    
                    html.AppendLine("</div>");
                    html.AppendLine($"<div class='size-bar-container'><div class='size-bar {resultSizeInfo.BarClass}' style='width: {resultSizeInfo.Percentage:F1}%'></div></div>");
                    html.AppendLine($"<div class='{resultClass}'>{escapedResult}</div>");
                }
                
                html.AppendLine("</div>");
                entryIndex++;
            }
            
            // Add script with full arguments data (always initialize, even if empty)
            html.AppendLine("<script type='text/javascript'>");
            html.AppendLine("var fullArgumentsData = {};");
            foreach (var kvp in fullArgumentsData)
            {
                var jsonEscaped = System.Text.Json.JsonSerializer.Serialize(kvp.Value);
                html.AppendLine($"fullArgumentsData[{kvp.Key}] = {jsonEscaped};");
            }
            html.AppendLine("</script>");
            
            // Add context summary at the bottom (so it's visible when auto-scrolling)
            var totalSizeInfo = GetSizeInfo(contextUsage.TotalSize);
            // Estimate token usage (rough approximation: 1 token ≈ 4 characters)
            var estimatedTokens = contextUsage.TotalSize / 4;
            var tokenPercentage = Math.Min(100, (estimatedTokens / 200000.0) * 100); // Assuming 200K token context window
            
            html.AppendLine("<div class='context-summary' style='background-color: #1565C0; color: #FFFFFF; padding: 12px; border-radius: 6px; margin-top: 12px; margin-bottom: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.2); border: 2px solid #0D47A1;'>");
            html.AppendLine("<div class='context-summary-title' style='font-weight: bold; font-size: 13px; margin-bottom: 8px; color: #FFFFFF;'>📊 Conversation Context Usage</div>");
            html.AppendLine("<div class='context-summary-stats' style='display: flex; flex-wrap: wrap; gap: 24px; margin-bottom: 8px;'>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Tool Calls:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{contextUsage.ToolCallCount}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Arguments:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{FormatSize(contextUsage.TotalArgumentsSize)}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Results:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{FormatSize(contextUsage.TotalResultSize)}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Total Context:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{FormatSize(contextUsage.TotalSize)}</span></div>");
            html.AppendLine($"<div class='context-stat' style='display: flex; flex-direction: column; min-width: 80px; padding-right: 12px;'><span class='context-stat-label' style='font-size: 10px; margin-bottom: 6px; color: #E3F2FD;'>Est. Tokens:</span><span class='context-stat-value' style='font-size: 14px; font-weight: bold; color: #FFFFFF;'>{estimatedTokens:N0}</span></div>");
            html.AppendLine("</div>");
            html.AppendLine($"<div class='context-total-bar' style='width: 100%; height: 8px; background: rgba(255,255,255,0.3); border-radius: 4px; margin-top: 8px; overflow: hidden;'><div class='context-total-bar-fill' style='width: {tokenPercentage:F1}%; height: 100%; background: #FFFFFF;'></div></div>");
            html.AppendLine($"<div style='font-size: 10px; margin-top: 4px; color: #E3F2FD;'>Estimated token usage: {tokenPercentage:F1}% of 200K context window</div>");
            html.AppendLine("</div>");
        }
        
        html.AppendLine("<script>");
        html.AppendLine("window.onload = function() { window.scrollTo(0, document.body.scrollHeight); };");
        html.AppendLine("function copyFullArguments(index, buttonElement) {");
        html.AppendLine("    if (typeof fullArgumentsData !== 'undefined' && fullArgumentsData[index]) {");
        html.AppendLine("        var text = fullArgumentsData[index];");
        html.AppendLine("        var textArea = document.createElement('textarea');");
        html.AppendLine("        textArea.value = text;");
        html.AppendLine("        textArea.style.position = 'fixed';");
        html.AppendLine("        textArea.style.left = '-999999px';");
        html.AppendLine("        textArea.style.top = '-999999px';");
        html.AppendLine("        document.body.appendChild(textArea);");
        html.AppendLine("        textArea.focus();");
        html.AppendLine("        textArea.select();");
        html.AppendLine("        try {");
        html.AppendLine("            var successful = document.execCommand('copy');");
        html.AppendLine("            if (successful && buttonElement) {");
        html.AppendLine("                var originalText = buttonElement.innerHTML;");
        html.AppendLine("                buttonElement.innerHTML = '✓ Copied';");
        html.AppendLine("                buttonElement.classList.add('copied');");
        html.AppendLine("                setTimeout(function() {");
        html.AppendLine("                    buttonElement.innerHTML = originalText;");
        html.AppendLine("                    buttonElement.classList.remove('copied');");
        html.AppendLine("                }, 2000);");
        html.AppendLine("            }");
        html.AppendLine("        } catch (err) {");
        html.AppendLine("            // Silently handle copy errors");
        html.AppendLine("        }");
        html.AppendLine("        document.body.removeChild(textArea);");
        html.AppendLine("    }");
        html.AppendLine("}");
        html.AppendLine("</script>");
        html.AppendLine("</body></html>");
        
        ToolCallsWebBrowser.NavigateToString(html.ToString());
    }
    
    private (string Label, string Class, string BarClass, double Percentage) GetSizeInfo(int size)
    {
        // Size thresholds (in characters)
        const int smallThreshold = 1000;      // < 1KB
        const int mediumThreshold = 5000;     // < 5KB
        const int largeThreshold = 20000;      // < 20KB
        // >= 20KB is xlarge
        
        string label;
        string cssClass;
        string barClass;
        double percentage;
        
        if (size < smallThreshold)
        {
            label = FormatSize(size);
            cssClass = "size-small";
            barClass = "size-bar-small";
            percentage = Math.Min(100, (size / (double)smallThreshold) * 25); // Use 25% of bar for small
        }
        else if (size < mediumThreshold)
        {
            label = FormatSize(size);
            cssClass = "size-medium";
            barClass = "size-bar-medium";
            percentage = 25 + Math.Min(25, ((size - smallThreshold) / (double)(mediumThreshold - smallThreshold)) * 25); // 25-50%
        }
        else if (size < largeThreshold)
        {
            label = FormatSize(size);
            cssClass = "size-large";
            barClass = "size-bar-large";
            percentage = 50 + Math.Min(25, ((size - mediumThreshold) / (double)(largeThreshold - mediumThreshold)) * 25); // 50-75%
        }
        else
        {
            label = FormatSize(size);
            cssClass = "size-xlarge";
            barClass = "size-bar-xlarge";
            // For xlarge, show percentage based on a max of 100KB
            percentage = 75 + Math.Min(25, (size / 100000.0) * 25); // 75-100%
        }
        
        return (label, cssClass, barClass, percentage);
    }
    
    private string FormatSize(int size)
    {
        if (size < 1024)
            return $"{size} B";
        else if (size < 1024 * 1024)
            return $"{size / 1024.0:F1} KB";
        else
            return $"{size / (1024.0 * 1024.0):F2} MB";
    }
}
