using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Pdf;
using DocGlance.Services.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.UI;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private async Task LoadPdfNavigationDataAsync(
        PdfDocumentState state,
        System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            SetOutlineItems(await PdfAnnotationService.GetOutlineAsync(state, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error("[Sidebar] Could not load PDF outline", ex);
            SetOutlineItems(Array.Empty<OutlineItem>());
        }

        try
        {
            var annotations = await PdfAnnotationService.GetAnnotationsAsync(state, cancellationToken);
            if (!ReferenceEquals(_pdfState, state))
                return;
            SetPdfAnnotations(annotations);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Error("[PDFAnnotation] Could not load saved annotations", ex);
            SetPdfAnnotations(Array.Empty<PdfTextAnnotation>());
        }
    }

    private void DrawPdfAnnotationOverlays()
    {
        PdfAnnotationCanvas.Children.Clear();
        if (_pdfState == null || _pdfAnnotations.Count == 0)
            return;

        const double PtToLp = 96.0 / 72.0;
        foreach (var annotation in _pdfAnnotations)
        {
            if ((uint)annotation.PageIndex >= (uint)_pdfState.PageCount)
                continue;
            double pageTop = GetPageTopLp(annotation.PageIndex);
            foreach (var rect in annotation.Rectangles)
            {
                double x = rect.X * PtToLp;
                double y = pageTop + rect.Y * PtToLp;
                double width = rect.Width * PtToLp;
                double height = rect.Height * PtToLp;
                if (width <= 0 || height <= 0)
                    continue;

                if (annotation.Kind == PdfTextAnnotationKind.Note)
                {
                    var noteIcon = new FontIcon
                    {
                        Glyph = "\uE70B",
                        FontSize = 13,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 180, 120, 0)),
                    };
                    Canvas.SetLeft(noteIcon, x);
                    Canvas.SetTop(noteIcon, y);
                    PdfAnnotationCanvas.Children.Add(noteIcon);
                    continue;
                }

                double lineHeight = Math.Clamp(height * 0.09, 1.25, 2.5);
                var mark = new Rectangle
                {
                    Width = width,
                    Height = annotation.Kind == PdfTextAnnotationKind.Highlight ? height : lineHeight,
                    IsHitTestVisible = false,
                    Fill = annotation.Kind switch
                    {
                        PdfTextAnnotationKind.Highlight =>
                            new SolidColorBrush(Color.FromArgb(85, 255, 220, 40)),
                        PdfTextAnnotationKind.Underline =>
                            new SolidColorBrush(Color.FromArgb(255, 0, 85, 190)),
                        _ => new SolidColorBrush(Color.FromArgb(230, 210, 45, 45)),
                    },
                };
                Canvas.SetLeft(mark, x);
                Canvas.SetTop(mark, annotation.Kind switch
                {
                    PdfTextAnnotationKind.Underline => y + height - lineHeight,
                    PdfTextAnnotationKind.Strikeout => y + (height - lineHeight) / 2,
                    _ => y,
                });
                PdfAnnotationCanvas.Children.Add(mark);
            }
        }
    }

    private IReadOnlyList<PdfSearchRect> GetPdfSelectionRects()
    {
        if (!HasPdfTextSelection ||
            !_pdfTextPageMaps.TryGetValue(_pdfSelectionPage, out var map))
            return Array.Empty<PdfSearchRect>();

        int start = Math.Min(_pdfSelectionAnchor, _pdfSelectionFocus);
        int end = Math.Max(_pdfSelectionAnchor, _pdfSelectionFocus) + 1;
        return map.GetSelectionRects(start, end);
    }

    private void AddPdfAnnotationMenuItem(
        MenuFlyout menu,
        string label,
        PdfTextAnnotationKind kind)
    {
        var icon = kind switch
        {
            PdfTextAnnotationKind.Highlight => Symbol.Highlight,
            PdfTextAnnotationKind.Underline => Symbol.Font,
            PdfTextAnnotationKind.Strikeout => Symbol.Remove,
            _ => Symbol.Edit,
        };
        var item = new MenuFlyoutItem
        {
            Text = label,
            Icon = new SymbolIcon(icon),
        };
        item.Click += async (_, _) => await AddPdfMarkupAsync(kind);
        menu.Items.Add(item);
    }

    private async Task AddPdfMarkupAsync(PdfTextAnnotationKind kind)
    {
        PdfDocumentState? state = _pdfState;
        int page = _pdfSelectionPage;
        int start = Math.Min(_pdfSelectionAnchor, _pdfSelectionFocus);
        int end = Math.Max(_pdfSelectionAnchor, _pdfSelectionFocus) + 1;
        IReadOnlyList<PdfSearchRect> rectangles = GetPdfSelectionRects();
        if (state == null || rectangles.Count == 0 || !HasPdfTextSelection)
            return;

        try
        {
            string selectedText = await PdfTextService.GetTextRangeAsync(
                state, page, start, end, _cts?.Token ?? default);
            if (selectedText.Length == 0)
                throw new InvalidOperationException("No text was found in the current selection.");
            if (!ReferenceEquals(_pdfState, state))
                return;

            byte[] updatedPdf = await PdfAnnotationService.AddAsync(
                state, page, kind, rectangles, selectedText, _cts?.Token ?? default);
            await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            await ShowPdfAnnotationErrorAsync("Could not save annotation", ex);
        }
    }

    private async Task AddPdfNoteAsync()
    {
        PdfDocumentState? state = _pdfState;
        int page = _pdfSelectionPage;
        IReadOnlyList<PdfSearchRect> rectangles = GetPdfSelectionRects();
        if (state == null || rectangles.Count == 0 || !HasPdfTextSelection)
            return;

        var noteInput = new TextBox
        {
            Header = "Note",
            PlaceholderText = "Enter a note for the selected text",
            AcceptsReturn = true,
            MinHeight = 100,
            TextWrapping = TextWrapping.Wrap,
        };
        var dialog = new ContentDialog
        {
            Title = "Add note",
            Content = noteInput,
            PrimaryButtonText = "Add note",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
            string.IsNullOrWhiteSpace(noteInput.Text) ||
            !ReferenceEquals(_pdfState, state))
            return;

        try
        {
            byte[] updatedPdf = await PdfAnnotationService.AddAsync(
                state, page, PdfTextAnnotationKind.Note, rectangles,
                noteInput.Text.Trim(), _cts?.Token ?? default);
            await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            await ShowPdfAnnotationErrorAsync("Could not save note", ex);
        }
    }

    private async Task DeletePdfAnnotationsAsync()
    {
        PdfDocumentState? state = _pdfState;
        int page = _pdfSelectionPage;
        IReadOnlyList<PdfSearchRect> rectangles = GetPdfSelectionRects();
        if (state == null || rectangles.Count == 0 || !HasPdfTextSelection)
            return;

        try
        {
            byte[] updatedPdf = await PdfAnnotationService.DeleteAsync(
                state, page, rectangles, _cts?.Token ?? default);
            await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            await ShowPdfAnnotationErrorAsync("Could not delete annotation", ex);
        }
    }

    private async Task SaveAndReloadAnnotatedPdfAsync(PdfDocumentState sourceState, byte[] updatedPdf)
    {
        if (!ReferenceEquals(_pdfState, sourceState))
            return;
        string? filePath = _doc?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new InvalidOperationException("The source PDF file is no longer available.");

        string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, updatedPdf, _cts?.Token ?? default);
            File.Replace(tempPath, filePath, null);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }

        _renderCts?.Cancel();
        ClearPdfTextSelectionState();
        ClearPdfPageCache();
        _pdfService.InvalidateRenderedPages();
        PdfLoadResult loadResult = await _pdfService.LoadDocumentFromBytesAsync(
            updatedPdf, _cts?.Token ?? default);
        if (!loadResult.Success || loadResult.Document == null)
        {
            _pdfState = null;
            throw new InvalidOperationException(
                $"The annotation was saved, but the updated PDF could not be reloaded: {loadResult.ErrorMessage}");
        }

        _pdfState = loadResult.Document;
        _total = loadResult.PageCount;
        await LoadPdfNavigationDataAsync(_pdfState, _cts?.Token ?? default);
        _page = Math.Clamp(_page, 1, Math.Max(1, _total));
        if (_viewportState != null)
            _viewportState.CurrentPageIndex = _page - 1;

        Toolbar.UpdatePageInfo(_page, _total);
        UpdateCurrentPage(_page);
        UpdateCanvasSize();
        EnsurePdfTextPageMap(_page - 1);
        RenderPagesAsync().SafeFireAndForget("PdfAnnotationRender");
    }

    private async Task ShowPdfAnnotationErrorAsync(string title, Exception exception)
    {
        Logger.Error($"[PDFAnnotation] {title}", exception);
        var dialog = new ContentDialog
        {
            Title = title,
            Content = exception.Message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        };
        await dialog.ShowAsync();
    }
}
