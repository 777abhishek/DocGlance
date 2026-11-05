using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.Storage;
using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using DocGlance.Infrastructure.Diagnostics;
using DocGlance.Infrastructure.Logging;

namespace DocGlance.Features.Viewer.Formats.Rtf;

/// <summary>
/// RTF file viewer using WinUI 3 RichEditBox.
/// Attempts to load RTF, shows "not supported" if loading fails.
/// </summary>
public sealed partial class RtfViewer : UserControl, IDocumentViewer
{
    public RtfViewer()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => ApplyThemeTextColor();
    }

    public FrameworkElement Control => this;

    public async Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        string filePath = document.FilePath
            ?? throw new ArgumentException("The document has no file path.", nameof(document));

        ShowLoading(true);

        try
        {
            // Try to load RTF file
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            
            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                // Load RTF into RichEditBox
                RichEditor.Document.LoadFromStream(Microsoft.UI.Text.TextSetOptions.FormatRtf, stream);
            }

            ApplyThemeTextColor();
            ShowLoading(false);
            DocumentOpenTrace.For(document)?.MarkFirstContentRendered();
            Logger.Log($"[RtfViewer] Successfully loaded RTF: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Logger.Error($"[RtfViewer] Failed to load RTF: {Path.GetFileName(filePath)}", ex);
            ShowError("RTF Not Supported", 
                "This RTF file cannot be displayed. The format may be too complex or corrupted.");
        }
    }

    public Task CleanupAsync()
    {
        RichEditor.Document.SetText(Microsoft.UI.Text.TextSetOptions.None, string.Empty);
        return Task.CompletedTask;
    }

    private void ShowLoading(bool show)
    {
        LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        RichEditor.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ErrorOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string title, string message)
    {
        ErrorTitle.Text = title;
        ErrorMessage.Text = message;
        ErrorOverlay.Visibility = Visibility.Visible;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        RichEditor.Visibility = Visibility.Collapsed;
    }

    private void ApplyThemeTextColor()
    {
        if (RichEditor.Foreground is not SolidColorBrush textBrush)
            return;

        RichEditor.Document.GetText(
            Microsoft.UI.Text.TextGetOptions.None,
            out string documentText);
        if (documentText.Length == 0)
            return;

        RichEditor.IsReadOnly = false;
        try
        {
            RichEditor.Document.GetRange(0, documentText.Length)
                .CharacterFormat.ForegroundColor = textBrush.Color;
        }
        finally
        {
            RichEditor.IsReadOnly = true;
        }
    }
}

public sealed class RtfViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions = new[] { ".rtf" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new RtfViewer();
}
