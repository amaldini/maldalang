// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.BuiltIns;

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;
using System.Threading;
using MaldaLang.Interpreter;
using MaldaLang.Parser.AST.Declarations;
using MaldaLang.Parser.AST.Expressions;
using System.IO;
using MaldaLang.BuiltIns.LLMClientBridge.RateLimiting;

public partial class HttpServerInstance
{
    private string GenerateAIPage(string description)
    {
        if (_interpreter == null)
        {
            return "<html><body><h1>Error</h1><p>AI page generation requires an interpreter instance.</p></body></html>";
        }
        
        try
        {
            // Try to find Agent in globals; otherwise create with default local LLM (auto-download from Hugging Face)
            AgentInstance? agent = null;
            var defaultLlama = DefaultLocalLlm.GetDefaultLocalClient();
            
            // Create agent if not found
            agent = new AgentInstance();
            agent.Initialize(
                "UIGenerator",
                "UI designer",
                "You create beautiful, functional HTML forms with modern CSS. Always include proper form submission using AJAX/fetch API. Use modern CSS styling. Include the AJAX helper script automatically.",
                null,
                defaultLlama,
                null,
                null
            );
            
            // Generate HTML using agent
            var response = agent.Think(RuntimeValue.String($"Create an HTML form: {description}"));
            
            // Extract HTML from response
            string html;
            if (response.Type == ValueType.Object)
            {
                var responseObj = response.AsObject();
                if (responseObj is JsonObject jsonObj)
                {
                    var contentValue = jsonObj.Get("content", null);
                    if (contentValue != null && contentValue.Type == ValueType.String)
                    {
                        html = contentValue.AsString();
                    }
                    else
                    {
                        html = response.ToString();
                    }
                }
                else
                {
                    html = response.ToString();
                }
            }
            else if (response.Type == ValueType.String)
            {
                html = response.AsString();
            }
            else
            {
                html = response.ToString();
            }
            
            // Extract HTML from markdown code blocks if present
            html = ExtractHTML(html);
            
            // Inject AJAX helper script
            html = InjectAjaxHelper(html);
            
            return html;
        }
        catch (Exception ex)
        {
            return $"<html><body><h1>Error Generating Page</h1><p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p></body></html>";
        }
    }
    
    private string ExtractHTML(string markdown)
    {
        // Extract HTML from markdown code blocks
        var htmlPattern = @"```html\s*(.*?)\s*```";
        var match = System.Text.RegularExpressions.Regex.Match(
            markdown, 
            htmlPattern, 
            System.Text.RegularExpressions.RegexOptions.Singleline | 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        
        if (match.Success)
            return match.Groups[1].Value.Trim();
        
        // If no code block, check if it's already HTML
        if (markdown.Contains("<html") || markdown.Contains("<!DOCTYPE"))
            return markdown;
        
        // Return as-is if no HTML found
        return markdown;
    }
    
    private string InjectAjaxHelper(string html)
    {
        // Check if AJAX helper is already injected
        if (html.Contains("id=\"spl-ajax-helper\""))
            return html;
        
        // AJAX helper script. Submit is delegated on document so fragment / SSE
        // innerHTML replacements (new <form> nodes) stay intercepted. Per-form
        // listeners die with the node; a native POST then navigates to the
        // fragment body (unstyled page, no SSE).
        var ajaxScript = @"
<script id=""spl-ajax-helper"">
(function() {
    'use strict';
    // MALDA AJAX Helper - automatically intercepts form submissions
    function initAjaxHelper() {
        if (document.documentElement.dataset.splAjaxBound === '1') {
            return;
        }
        document.documentElement.dataset.splAjaxBound = '1';
        document.addEventListener('submit', function(e) {
            var form = e.target;
            if (!form || form.tagName !== 'FORM') {
                return;
            }
            e.preventDefault();

            // Include the clicked submit control (e.g. name=vote value=up/down).
            // FormData(form) alone omits submitter buttons.
            var submitter = e.submitter || null;
            var formData = submitter ? new FormData(form, submitter) : new FormData(form);
            if (submitter && submitter.name) {
                var hasSubmitter = false;
                formData.forEach(function(_v, k) {
                    if (k === submitter.name) { hasSubmitter = true; }
                });
                if (!hasSubmitter) {
                    formData.append(submitter.name, submitter.value || '');
                }
            }

            var action = form.action || '/submit';
            var submitButton = submitter || form.querySelector('button[type=""submit""], input[type=""submit""]');
            var originalText = submitButton ? submitButton.textContent || submitButton.value : '';
            
            if (submitButton) {
                submitButton.disabled = true;
                if (submitButton.tagName === 'BUTTON') {
                    submitButton.textContent = 'Submitting...';
                } else {
                    submitButton.value = 'Submitting...';
                }
            }
            
            fetch(action, {
                method: 'POST',
                body: formData
            })
            .then(function(response) {
                // Check for redirect (3xx status codes)
                if (response.status >= 300 && response.status < 400) {
                    var location = response.headers.get('Location') || response.url;
                    if (location) {
                        window.location.href = location;
                        return;
                    }
                }
                return response.text().then(function(html) {
                    return {
                        html: html,
                        isFragment: (response.headers.get('X-Malda-Fragment') || '').toLowerCase() === 'true',
                        targetId: response.headers.get('X-Malda-Fragment-Target') || ''
                    };
                });
            })
            .then(function(result) {
                if (result === undefined) return; // Redirect already handled
                if (result.isFragment && result.targetId) {
                    var target = document.getElementById(result.targetId);
                    if (target) {
                        target.innerHTML = result.html;
                        return;
                    }
                }
                // Fallback to full-page replacement.
                document.body.innerHTML = result.html;
            })
            .catch(function(error) {
                alert('Error submitting form: ' + error.message);
            })
            .finally(function() {
                if (submitButton) {
                    submitButton.disabled = false;
                    if (submitButton.tagName === 'BUTTON') {
                        submitButton.textContent = originalText;
                    } else {
                        submitButton.value = originalText;
                    }
                }
            });
        });
    }
    window.__maldaInitAjaxHelper = initAjaxHelper;
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAjaxHelper);
    } else {
        initAjaxHelper();
    }
})();
</script>";
        
        // Inject before closing </body> tag, or at the end if no body tag
        if (html.Contains("</body>"))
        {
            return html.Replace("</body>", ajaxScript + "\n</body>");
        }
        else if (html.Contains("</html>"))
        {
            return html.Replace("</html>", ajaxScript + "\n</html>");
        }
        else
        {
            // No closing tags, append at the end
            return html + ajaxScript;
        }
    }

    /// <summary>
    /// Static method to register transpiled AIPAGE routes with descriptions.
    /// </summary>
    public static void RegisterTranspiledAIPage(
        string path,
        string functionName,
        List<string> paramNames,
        string description,
        string? groupPrefix = null,
        string? versionPrefix = null,
        List<string>? routeMiddlewareFunctions = null,
        string? validationSchema = null)
    {
        var metadata = new RouteMetadata(
            groupPrefix,
            versionPrefix,
            routeMiddlewareFunctions ?? new List<string>(),
            string.IsNullOrWhiteSpace(validationSchema) ? RuntimeValue.Null() : RuntimeValue.String(validationSchema));
        var effectivePath = WebRuntimeHelpers.ComposeRoutePath(path, metadata.GroupPrefix, metadata.VersionPrefix);

        // Store the description for future instances
        lock (_pendingAiPageDescriptionsLock)
        {
            _pendingAiPageDescriptions[effectivePath] = description;
        }
        
        // Store the route for future instances
        lock (_pendingRoutesLock)
        {
            _pendingRoutes.Add(new PendingRoute("GET", effectivePath, functionName, paramNames, null, metadata));
        }
        
        // Apply to all existing instances
        lock (_instancesLock)
        {
            foreach (var instance in _instances)
            {
                instance._routeRegistry.RegisterTranspiledRoute("GET", effectivePath, functionName, paramNames, null, metadata);
                lock (instance._aiCacheLock)
                {
                    instance._aiPageDescriptions[effectivePath] = description;
                }
            }
        }
    }
}
