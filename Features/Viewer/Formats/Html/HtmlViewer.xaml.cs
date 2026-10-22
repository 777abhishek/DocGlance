using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Infrastructure.Logging;

namespace DocGlance.Features.Viewer.Formats.Html;

/// <summary>
/// HTML file viewer - shows "not supported" message.
/// Native HTML rendering would require WebView2 which uses high RAM.
/// </summary>
public sealed partial class HtmlViewer : UserControl, IDocumentViewer
{
    private string? _currentFilePath;

    public HtmlViewer()
    {
        InitializeComponent();
    }

    public FrameworkElement Control => this;

    public Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        _currentFilePath = document.FilePath;
        
        // Show not supported message
        ShowMessage();
        return Task.CompletedTask;
    }

    public Task CleanupAsync()
    {
        _currentFilePath = null;
        return Task.CompletedTask;
    }

    private void ShowMessage()
    {
        MessageOverlay.Visibility = Visibility.Visible;
    }

    private async void OpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return;

        try
        {
            var uri = new Uri("file:///" + _currentFilePath.Replace('\\', '/'));
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch (Exception ex)
        {
            Logger.Error("[HtmlViewer] Failed to open in browser", ex);
        }
    }

    private void ViewSource_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
            return;

        try
        {
            // Open in default text editor
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{_currentFilePath}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Logger.Error("[HtmlViewer] Failed to open in text editor", ex);
        }
    }
}

public sealed class HtmlViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions = new[] { ".html", ".htm" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new HtmlViewer();
}
