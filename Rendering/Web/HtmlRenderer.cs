using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Core.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using WinRT;

namespace QuickView.Rendering.Web;

public sealed class HtmlRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".html", ".htm"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var wv = new WebView2();
        
        // SEC-01: Initialize CoreWebView2 and disable JavaScript for security
        await wv.EnsureCoreWebView2Async();
        
        // Disable JavaScript execution - prevents XSS attacks from malicious HTML files
        wv.CoreWebView2.Settings.IsScriptEnabled = false;
        wv.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        
        // Block navigation to external URLs - only allow file:// navigation
        wv.CoreWebView2.NavigationStarting += (sender, args) =>
        {
            // Only allow file:// protocol navigation
            if (!args.Uri.StartsWith("file://", System.StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
            }
        };
        
        wv.Source = new System.Uri(filePath);
        return wv;
    }
}
