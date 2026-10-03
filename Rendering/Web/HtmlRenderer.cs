using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Web;

/// <summary>
/// HTML file renderer — loads local .html/.htm files in a hardened WebView2.
/// SEC-01: JavaScript is disabled; only file:// navigation of the original
/// file is allowed. All external and pop-up navigations are cancelled.
/// </summary>
public sealed class HtmlRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".html", ".htm"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var wv = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        // SEC-01: Must initialise CoreWebView2 before adjusting settings.
        await wv.EnsureCoreWebView2Async();

        if (ct.IsCancellationRequested) return null;

        // ── Harden settings ────────────────────────────────────────────────
        // Disable JavaScript — prevents arbitrary code execution from HTML files.
        wv.CoreWebView2.Settings.IsScriptEnabled                = false;
        wv.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        // Disable context menu — hides "View Source", "Inspect" etc.
        wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled  = false;
        // Disable status bar — prevents link URL preview that could mislead users.
        wv.CoreWebView2.Settings.IsStatusBarEnabled             = false;
        // Disable web messages — prevents postMessage from reaching the host.
        wv.CoreWebView2.Settings.IsWebMessageEnabled            = false;

        // ── Block external navigation ──────────────────────────────────────
        // Only allow navigation to the original file:// URI or about: pages.
        // This prevents a malicious HTML file from redirecting to a remote URL.
        string allowedFileUri = new Uri(filePath).AbsoluteUri;
        wv.CoreWebView2.NavigationStarting += (_, args) =>
        {
            string uri = args.Uri;
            bool isAllowed =
                uri.Equals(allowedFileUri, StringComparison.OrdinalIgnoreCase) ||
                uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);

            if (!isAllowed)
                args.Cancel = true;
        };

        // ── Block pop-up windows ───────────────────────────────────────────
        wv.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;

        wv.Source = new Uri(filePath);
        return wv;
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
}
