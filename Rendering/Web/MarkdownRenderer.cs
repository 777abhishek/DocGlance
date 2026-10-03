using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Markdig;
using QuickView.Core.Interfaces;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Web;

/// <summary>
/// Markdown file renderer — converts MD → HTML and displays in WebView2.
/// Supports: MD, MARKDOWN, MDX.
/// SEC-02: Uses Markdig library instead of custom parser to prevent XSS vulnerabilities.
/// </summary>
public sealed class MarkdownRenderer : IFileRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()  // Tables, task lists, etc.
        .DisableHtml()            // Block inline HTML to prevent XSS
        .Build();

    public string[] SupportedExtensions => [".md", ".markdown", ".mdx"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        string mdText = await Task.Run(
            () => QuickView.Rendering.Text.TextFileReader.ReadCapped(filePath, 1_000_000).Text, ct);

        // Determine theme from app-level setting (simplified for now)
        bool isDarkTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark;
        string html = BuildHtml(mdText, isDarkTheme);

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        await webView.EnsureCoreWebView2Async();

        if (ct.IsCancellationRequested) return null;

        // SEC-02: Harden settings
        webView.CoreWebView2.Settings.IsScriptEnabled                = false;
        webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled  = false;
        webView.CoreWebView2.Settings.IsStatusBarEnabled             = false;
        webView.CoreWebView2.Settings.IsWebMessageEnabled            = false;

        // SEC-02: NavigateToString produces data: / about: URIs only.
        // Block everything else (file://, http://, https://) to prevent
        // links in the Markdown from navigating the WebView away.
        webView.CoreWebView2.NavigationStarting += (_, args) =>
        {
            string uri = args.Uri;
            bool isAllowed =
                uri.StartsWith("data:",  StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);

            if (!isAllowed)
                args.Cancel = true;
        };

        // Block pop-up windows triggered by any remaining link clicks.
        webView.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;

        if (!ct.IsCancellationRequested)
            webView.NavigateToString(html);

        return webView;
    }

    /// <summary>
    /// PERF-05: Dispose WebView2 to prevent memory leak.
    /// Without this, WebView2 processes remain after tab close.
    /// </summary>
    public Task CleanupAsync(UIElement? element)
    {
        if (element is WebView2 webView)
        {
            try
            {
                // Close WebView2 to release native resources and terminate process
                webView.Close();
            }
            catch
            {
                // Swallow exceptions during cleanup (already shutting down)
            }
        }
        return Task.CompletedTask;
    }

    private static string BuildHtml(string md, bool dark)
    {
        // SEC-02: Use Markdig library to convert Markdown to HTML (prevents XSS)
        string markdownHtml = Markdown.ToHtml(md, Pipeline);
        
        // Sanitize any remaining URLs to prevent javascript: and data: URIs
        markdownHtml = SanitizeUrls(markdownHtml);
        
        string bg   = dark ? "#1e1e1e" : "#ffffff";
        string fg   = dark ? "#d4d4d4" : "#1e1e1e";
        string codeBg = dark ? "#2d2d2d" : "#f6f8fa";
        string codeBorder = dark ? "#404040" : "#d0d7de";
        string linkColor  = dark ? "#58a6ff" : "#0969da";
        string hrColor    = dark ? "#404040" : "#d0d7de";
        string quoteBorder = dark ? "#58a6ff" : "#0969da";
        string quoteBg = dark ? "rgba(88,166,255,0.1)" : "rgba(9,105,218,0.05)";
        string tableBorder = dark ? "#444" : "#d0d7de";
        string tableHeaderBg = dark ? "#2d2d2d" : "#f6f8fa";

        // SEC-02 & SEC-08: Add Content Security Policy to prevent XSS
        return $$"""
            <!DOCTYPE html><html><head><meta charset='utf-8'>
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; img-src data: https:; font-src data:;">
            <style>
              body {
                background:{{bg}}; color:{{fg}};
                font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,system-ui,sans-serif;
                font-size:15px; line-height:1.65; padding:32px 40px; margin:0;
                max-width:880px; margin:0 auto;
              }
              h1,h2,h3,h4,h5,h6 { line-height:1.3; margin-top:24px; margin-bottom:12px; font-weight:600 }
              h1 { font-size:2em; padding-bottom:8px; border-bottom:1px solid {{hrColor}} }
              h2 { font-size:1.5em }
              h3 { font-size:1.25em }
              h4 { font-size:1.1em }
              code {
                background:{{codeBg}}; border:1px solid {{codeBorder}}; border-radius:3px; padding:2px 6px;
                font-family:'Cascadia Code','Cascadia Mono','Consolas',monospace; font-size:0.9em
              }
              pre { margin:16px 0; overflow-x:auto; background:{{codeBg}}; border:1px solid {{codeBorder}}; border-radius:6px; padding:16px }
              pre code { border:none; padding:0; background:transparent }
              a { color:{{linkColor}}; text-decoration:none }
              a:hover { text-decoration:underline }
              ul,ol { padding-left:24px; margin:12px 0 }
              li { margin:6px 0 }
              img { max-width:100%; height:auto; border-radius:6px; margin:16px 0 }
              blockquote {
                border-left:3px solid {{quoteBorder}}; background:{{quoteBg}};
                margin:12px 0; padding:8px 16px; border-radius:4px
              }
              table { border-collapse:collapse; width:100%; margin:16px 0 }
              th { background:{{tableHeaderBg}}; font-weight:600; text-align:left }
              th,td { border:1px solid {{tableBorder}}; padding:8px 12px }
              tr:nth-child(even) { background:{{(dark ? "#252525" : "#f9f9f9")}} }
              hr { border:none; border-top:1px solid {{hrColor}}; margin:24px 0 }
              ::selection { background:{{(dark ? "#264f78" : "#b3d4fc")}} }
              input[type="checkbox"] { margin-right:8px }
            </style></head><body>{{markdownHtml}}</body></html>
            """;
    }

    /// <summary>
    /// SEC-02: Sanitize URLs in HTML to prevent javascript:, data:, and vbscript: URIs
    /// </summary>
    private static string SanitizeUrls(string html)
    {
        // Remove dangerous URL schemes from href and src attributes
        html = Regex.Replace(html, 
            @"(href|src)\s*=\s*[""']?\s*(javascript|data|vbscript):",
            "$1=\"#blocked-unsafe-url\"",
            RegexOptions.IgnoreCase);
        
        return html;
    }
}
