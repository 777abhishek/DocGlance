using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Rendering.Pdf;
using DocGlance.Services.Documents;
using DocGlance.Features.Viewer.Components;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private uint _pdfMarkupColorArgb = 0xFFFFDC28;
    private float _pdfMarkupOpacity = 0.35f;
    private PdfTextAnnotationKind _pdfMarkupKind = PdfTextAnnotationKind.Highlight;
    private bool _pdfMarkupToolActive;
    private PdfAnnotationTool? _activePdfAnnotationTool;
    private readonly List<(double X, double Y)> _pdfInkPoints = new();
    private readonly List<string> _pdfUndoSnapshots = new();
    private readonly List<string> _pdfRedoSnapshots = new();
    private readonly SemaphoreSlim _pdfAnnotationSaveGate = new(1, 1);
    private string? _pdfHistoryDirectory;
    private string? _pdfWorkingPath;
    private bool _pdfChangesPending;
    private Pointer? _pdfInkPointer;
    private int _pdfInkPage = -1;
    private Polyline? _pdfInkPreview;
    private const float PdfPenWidthPt = 2.5f;
    private const int PdfHistoryEntryLimit = 8;
    private const long PdfHistoryBytesLimit = 256L * 1024 * 1024;

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

                if (annotation.Kind == PdfTextAnnotationKind.Ink)
                {
                    var stroke = new Polyline
                    {
                        Stroke = new SolidColorBrush(Color.FromArgb(
                            (byte)Math.Round(annotation.Opacity * 255),
                            (byte)(annotation.ColorArgb >> 16),
                            (byte)(annotation.ColorArgb >> 8),
                            (byte)annotation.ColorArgb)),
                        StrokeThickness = Math.Max(0.5, annotation.Width * PtToLp),
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false,
                    };
                    foreach (var point in annotation.Points)
                        stroke.Points.Add(new Windows.Foundation.Point(
                            point.X * PtToLp, pageTop + point.Y * PtToLp));
                    PdfAnnotationCanvas.Children.Add(stroke);
                    break;
                }

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

                if (annotation.Kind == PdfTextAnnotationKind.FreeText)
                {
                    var text = new TextBlock
                    {
                        Text = annotation.Contents,
                        Width = width,
                        Height = height,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.FromArgb(
                            (byte)Math.Round(annotation.Opacity * 255),
                            (byte)(annotation.ColorArgb >> 16),
                            (byte)(annotation.ColorArgb >> 8),
                            (byte)annotation.ColorArgb)),
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(text, x);
                    Canvas.SetTop(text, y);
                    PdfAnnotationCanvas.Children.Add(text);
                    continue;
                }

                double lineHeight = Math.Clamp(height * 0.09, 1.25, 2.5);
                Color annotationColor = Color.FromArgb(
                    (byte)Math.Round(annotation.Opacity * 255),
                    (byte)(annotation.ColorArgb >> 16),
                    (byte)(annotation.ColorArgb >> 8),
                    (byte)annotation.ColorArgb);
                var mark = new Rectangle
                {
                    Width = width,
                    Height = annotation.Kind == PdfTextAnnotationKind.Highlight ? height : lineHeight,
                    IsHitTestVisible = false,
                    Fill = new SolidColorBrush(annotationColor),
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
                state, page, kind, rectangles, selectedText,
                _pdfMarkupColorArgb, _pdfMarkupOpacity, _cts?.Token ?? default);
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
                noteInput.Text.Trim(), _pdfMarkupColorArgb, _pdfMarkupOpacity,
                _cts?.Token ?? default);
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

        private async Task AddPdfNoteAtAsync(int page, double x, double y)
        {
            PdfDocumentState? state = _pdfState;
            if (state == null)
                return;
            var noteInput = new TextBox
            {
                Header = "Note",
                PlaceholderText = "Enter a note",
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
                    state, page, PdfTextAnnotationKind.Note,
                    new[] { new PdfSearchRect(x, y, 1, 1) },
                    noteInput.Text.Trim(), _pdfMarkupColorArgb, _pdfMarkupOpacity,
                    _cts?.Token ?? default);
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

        private async Task AddPdfTextAtAsync(int page, double x, double y)
        {
            PdfDocumentState? state = _pdfState;
            if (state == null)
                return;
            var textInput = new TextBox
            {
                Header = "Text",
                PlaceholderText = "Enter text for the page",
                AcceptsReturn = true,
                MinHeight = 100,
                TextWrapping = TextWrapping.Wrap,
            };
            var dialog = new ContentDialog
            {
                Title = "Add text box",
                Content = textInput,
                PrimaryButtonText = "Add text",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary ||
                string.IsNullOrWhiteSpace(textInput.Text) ||
                !ReferenceEquals(_pdfState, state))
                return;
            try
            {
                byte[] updatedPdf = await PdfAnnotationService.AddAsync(
                    state, page, PdfTextAnnotationKind.FreeText,
                    new[] { new PdfSearchRect(x, y, 180, 48) },
                    textInput.Text.Trim(), _pdfMarkupColorArgb, 1,
                    _cts?.Token ?? default);
                await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
            {
            }
            catch (Exception ex)
            {
                await ShowPdfAnnotationErrorAsync("Could not save text box", ex);
            }
        }

        private async Task AddPdfImageAtAsync(int page, double x, double y)
        {
            PdfDocumentState? state = _pdfState;
            if (state == null)
                return;
            try
            {
                var picker = new FileOpenPicker();
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");
                picker.FileTypeFilter.Add(".bmp");
                var window = DocGlance.UI.Shell.TitleBarWindow.Current
                    ?? throw new InvalidOperationException("The document window is unavailable.");
                InitializeWithWindow.Initialize(picker, window.Hwnd);
                StorageFile? file = await picker.PickSingleFileAsync();
                if (file == null || !ReferenceEquals(_pdfState, state))
                    return;

                using var sourceStream = await file.OpenAsync(FileAccessMode.Read);
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(sourceStream);
                double scale = Math.Min(1.0, 4096.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                uint pixelWidth = Math.Max(1, (uint)Math.Round(decoder.PixelWidth * scale));
                uint pixelHeight = Math.Max(1, (uint)Math.Round(decoder.PixelHeight * scale));
                var transform = new BitmapTransform
                {
                    ScaledWidth = pixelWidth,
                    ScaledHeight = pixelHeight,
                };
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Straight,
                    transform,
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                using var jpegStream = new InMemoryRandomAccessStream();
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                    BitmapEncoder.JpegEncoderId, jpegStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Straight,
                    pixelWidth,
                    pixelHeight,
                    96,
                    96,
                    pixelData.DetachPixelData());
                await encoder.FlushAsync();
                if (jpegStream.Size > 20 * 1024 * 1024)
                    throw new InvalidOperationException("The selected image is too large to embed in a PDF.");
                jpegStream.Seek(0);
                using var reader = new DataReader(jpegStream.GetInputStreamAt(0));
                await reader.LoadAsync(checked((uint)jpegStream.Size));
                var jpegBytes = new byte[checked((int)jpegStream.Size)];
                reader.ReadBytes(jpegBytes);

                var (pageWidth, pageHeight) = state.GetPageSize(page);
                double imageWidth = Math.Min(180, pageWidth);
                double imageHeight = Math.Min(
                    imageWidth * pixelHeight / pixelWidth,
                    pageHeight);
                imageWidth = Math.Min(imageWidth, imageHeight * pixelWidth / pixelHeight);
                x = Math.Clamp(x, 0, Math.Max(0, pageWidth - imageWidth));
                y = Math.Clamp(y, 0, Math.Max(0, pageHeight - imageHeight));
                byte[] updatedPdf = await PdfAnnotationService.AddImageAsync(
                    state,
                    page,
                    new PdfSearchRect(x, y, imageWidth, imageHeight),
                    jpegBytes,
                    pixelWidth,
                    pixelHeight,
                    _cts?.Token ?? default);
                await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
            {
            }
            catch (Exception ex)
            {
                await ShowPdfAnnotationErrorAsync("Could not insert image", ex);
            }
        }
        private void StartPdfInkStroke(Pointer pointer, int page, Windows.Foundation.Point point)
        {
            _pdfInkPointer = pointer;
            _pdfInkPage = page;
            _pdfInkPoints.Clear();
            _pdfInkPoints.Add(ToPdfTextPoint(page, point));
            PdfViewportCanvas.CapturePointer(pointer);
            if (_activePdfAnnotationTool is PdfAnnotationTool.Pen or PdfAnnotationTool.Signature)
            {
                _pdfInkPreview = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(
                        (byte)Math.Round(_pdfMarkupOpacity * 255),
                        (byte)(_pdfMarkupColorArgb >> 16),
                        (byte)(_pdfMarkupColorArgb >> 8),
                        (byte)_pdfMarkupColorArgb)),
                    StrokeThickness = PdfPenWidthPt * PtToLp,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };
                _pdfInkPreview.Points.Add(point);
                PdfAnnotationCanvas.Children.Add(_pdfInkPreview);
            }
        }

        private void UpdatePdfInkStroke(Windows.Foundation.Point point)
        {
            if (_pdfInkPointer == null || _pdfInkPage < 0)
                return;

            if (_activePdfAnnotationTool is PdfAnnotationTool.Pen or PdfAnnotationTool.Signature)
            {
                var previous = _pdfInkPreview?.Points.LastOrDefault() ?? point;
                if (Math.Abs(point.X - previous.X) + Math.Abs(point.Y - previous.Y) < 1.5)
                    return;
                _pdfInkPreview?.Points.Add(point);
            }
            _pdfInkPoints.Add(ToPdfTextPoint(_pdfInkPage, point));
        }

        private void FinishPdfInkStroke(bool cancelled)
        {
            Pointer? pointer = _pdfInkPointer;
            int page = _pdfInkPage;
            bool shouldPersist = !cancelled && _pdfInkPoints.Count > 0;
            bool erase = _activePdfAnnotationTool == PdfAnnotationTool.Eraser;
            var points = _pdfInkPoints.ToArray();
            _pdfInkPointer = null;
            _pdfInkPage = -1;
            _pdfInkPoints.Clear();
            if (_pdfInkPreview != null)
                PdfAnnotationCanvas.Children.Remove(_pdfInkPreview);
            _pdfInkPreview = null;
            if (pointer != null)
                PdfViewportCanvas.ReleasePointerCapture(pointer);
            if (!shouldPersist)
                return;

            if (erase)
                _ = ErasePdfAnnotationsAsync(page, points);
            else
                _ = SavePdfInkAsync(page, points);
        }

        private async Task SavePdfInkAsync(int page, IReadOnlyList<(double X, double Y)> points)
        {
            PdfDocumentState? state = _pdfState;
            if (state == null)
                return;
            if (points.Count == 1)
                points = new[] { points[0], points[0] };

            try
            {
                byte[] updatedPdf = await PdfAnnotationService.AddInkAsync(
                    state, page, points, _pdfMarkupColorArgb, _pdfMarkupOpacity,
                    PdfPenWidthPt, _cts?.Token ?? default);
                await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
            {
            }
            catch (Exception ex)
            {
                await ShowPdfAnnotationErrorAsync("Could not save pen stroke", ex);
            }
        }

        private async Task ErasePdfAnnotationsAsync(
            int page,
            IReadOnlyList<(double X, double Y)> points)
        {
            PdfDocumentState? state = _pdfState;
            if (state == null || points.Count == 0)
                return;
            int stride = Math.Max(1, (int)Math.Ceiling(points.Count / 2000.0));
            var hitRegions = new List<PdfSearchRect>();
            for (int index = 0; index < points.Count; index += stride)
            {
                var point = points[index];
                hitRegions.Add(new PdfSearchRect(point.X - 6, point.Y - 6, 12, 12));
            }

            try
            {
                int count = await PdfAnnotationService.CountRemovableAsync(
                    state, page, hitRegions, _cts?.Token ?? default);
                if (count == 0 || !ReferenceEquals(_pdfState, state))
                    return;
                byte[] updatedPdf = await PdfAnnotationService.DeleteAsync(
                    state, page, hitRegions, _cts?.Token ?? default);
                await SaveAndReloadAnnotatedPdfAsync(state, updatedPdf);
            }
            catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
            {
            }
            catch (Exception ex)
            {
                await ShowPdfAnnotationErrorAsync("Could not erase annotations", ex);
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

    private async Task SaveAndReloadAnnotatedPdfAsync(
        PdfDocumentState sourceState,
        byte[] updatedPdf,
        bool recordHistory = true)
    {
        await _pdfAnnotationSaveGate.WaitAsync(_cts?.Token ?? default);
        string? newWorkingPath = null;
        try
        {
            if (!ReferenceEquals(_pdfState, sourceState))
                return;
            string? filePath = _doc?.FilePath;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new InvalidOperationException("The source PDF file is no longer available.");

            if (recordHistory)
            {
                string snapshotSource = _pdfChangesPending && _pdfWorkingPath != null
                    ? _pdfWorkingPath
                    : filePath;
                bool recorded = await RecordPdfHistorySnapshotAsync(snapshotSource, _pdfUndoSnapshots);
                if (!recorded)
                {
                    ClearPdfHistorySnapshots(_pdfUndoSnapshots);
                    ClearPdfHistorySnapshots(_pdfRedoSnapshots);
                    ShowInfoBar(
                        "Undo history is unavailable for PDFs larger than 128 MB.",
                        Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning);
                }
                else
                {
                    ClearPdfHistorySnapshots(_pdfRedoSnapshots);
                }
                UpdatePdfHistoryAvailability();
            }

            string workingDirectory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "DocGlance", "PendingAnnotations");
            Directory.CreateDirectory(workingDirectory);
            newWorkingPath = System.IO.Path.Combine(
                workingDirectory, Guid.NewGuid().ToString("N") + ".pdf");
            await File.WriteAllBytesAsync(newWorkingPath, updatedPdf, _cts?.Token ?? default);

            _renderCts?.Cancel();
            ClearPdfTextSelectionState();
            PdfLoadResult loadResult = await _pdfService.ReplaceDocumentFromPathAsync(
                newWorkingPath, _cts?.Token ?? default);
            if (!loadResult.Success || loadResult.Document == null)
                throw new InvalidOperationException(
                    $"The annotation was prepared, but the updated PDF could not be opened: {loadResult.ErrorMessage}");

            string? previousWorkingPath = _pdfWorkingPath;
            _pdfWorkingPath = newWorkingPath;
            newWorkingPath = null;
            _pdfChangesPending = true;
            _pdfState = loadResult.Document;
            _total = loadResult.PageCount;
            await LoadPdfNavigationDataAsync(_pdfState, _cts?.Token ?? default);
            DrawPdfAnnotationOverlays();
            _page = Math.Clamp(_page, 1, Math.Max(1, _total));
            if (_viewportState != null)
                _viewportState.CurrentPageIndex = _page - 1;

            Toolbar.UpdatePageInfo(_page, _total);
            UpdateCurrentPage(_page);
            UpdatePdfHistoryAvailability();

            if (previousWorkingPath != null)
                DeletePdfWorkingFile(previousWorkingPath);
        }
        catch
        {
            if (newWorkingPath != null)
                DeletePdfWorkingFile(newWorkingPath);
            throw;
        }
        finally
        {
            _pdfAnnotationSaveGate.Release();
        }
    }

    public async Task<bool> FlushPendingPdfAnnotationsAsync()
    {
        await _pdfAnnotationSaveGate.WaitAsync();
        try
        {
            if (!_pdfChangesPending)
                return true;

            string? sourcePath = _doc?.FilePath;
            string? workingPath = _pdfWorkingPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath) ||
                string.IsNullOrWhiteSpace(workingPath) || !File.Exists(workingPath))
                throw new InvalidOperationException("The original or working PDF is no longer available.");

            string tempPath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var input = new FileStream(
                    workingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                await using (var output = new FileStream(
                    tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    await input.CopyToAsync(output);
                    await output.FlushAsync();
                }

                File.Replace(tempPath, sourcePath, null);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            _pdfChangesPending = false;
            _pdfWorkingPath = null;
            DeletePdfWorkingFile(workingPath);
            Logger.Log($"[PDFAnnotation] Deferred changes saved to {sourcePath}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("[PDFAnnotation] Could not save deferred PDF changes", ex);
            ShowInfoBar($"PDF annotation changes are still unsaved: {ex.Message}",
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            _pdfAnnotationSaveGate.Release();
        }
    }

    private static void DeletePdfWorkingFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Error($"[PDFAnnotation] Could not remove temporary PDF {path}", ex);
        }
    }

    private async Task<bool> RecordPdfHistorySnapshotAsync(string filePath, List<string> stack)
    {
        long fileSize = new FileInfo(filePath).Length;
        if (fileSize > 128L * 1024 * 1024)
            return false;

        _pdfHistoryDirectory ??= System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "DocGlance", "AnnotationHistory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_pdfHistoryDirectory);
        string snapshotPath = System.IO.Path.Combine(_pdfHistoryDirectory, Guid.NewGuid().ToString("N") + ".pdf");
        await Task.Run(() => File.Copy(filePath, snapshotPath), _cts?.Token ?? default);
        stack.Add(snapshotPath);
        while (stack.Count > PdfHistoryEntryLimit ||
            stack.Sum(path => File.Exists(path) ? new FileInfo(path).Length : 0) > PdfHistoryBytesLimit)
        {
            DeletePdfHistoryFile(stack[0]);
            stack.RemoveAt(0);
        }
        return true;
    }

    private void ClearPdfHistorySnapshots(List<string> snapshots)
    {
        foreach (string path in snapshots)
            DeletePdfHistoryFile(path);
        snapshots.Clear();
        UpdatePdfHistoryAvailability();
    }

    private static void DeletePdfHistoryFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private void ClearPdfAnnotationHistory()
    {
        ClearPdfHistorySnapshots(_pdfUndoSnapshots);
        ClearPdfHistorySnapshots(_pdfRedoSnapshots);
        if (_pdfHistoryDirectory != null && Directory.Exists(_pdfHistoryDirectory))
            Directory.Delete(_pdfHistoryDirectory, recursive: true);
        _pdfHistoryDirectory = null;
    }

    private void UpdatePdfHistoryAvailability() =>
        Toolbar.SetAnnotationHistoryAvailability(
            _pdfUndoSnapshots.Count > 0,
            _pdfRedoSnapshots.Count > 0);

    private async Task RestorePdfAnnotationHistoryAsync(bool undo)
    {
        var sourceStack = undo ? _pdfUndoSnapshots : _pdfRedoSnapshots;
        var destinationStack = undo ? _pdfRedoSnapshots : _pdfUndoSnapshots;
        if (sourceStack.Count == 0 || _pdfState == null)
            return;

        PdfDocumentState state = _pdfState;
        string? filePath = _doc?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        string targetSnapshot = sourceStack[^1];
        try
        {
            string currentVersionPath = _pdfChangesPending && _pdfWorkingPath != null
                ? _pdfWorkingPath
                : filePath;
            if (!await RecordPdfHistorySnapshotAsync(currentVersionPath, destinationStack))
                throw new InvalidOperationException("The current PDF is too large for annotation history.");
            byte[] targetPdf = await File.ReadAllBytesAsync(targetSnapshot, _cts?.Token ?? default);
            await SaveAndReloadAnnotatedPdfAsync(state, targetPdf, recordHistory: false);
            DeletePdfHistoryFile(targetSnapshot);
            sourceStack.RemoveAt(sourceStack.Count - 1);
            UpdatePdfHistoryAvailability();
        }
        catch (OperationCanceledException) when (_cts?.IsCancellationRequested == true)
        {
        }
        catch (Exception ex)
        {
            await ShowPdfAnnotationErrorAsync(
                undo ? "Could not undo annotation" : "Could not redo annotation", ex);
        }
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
