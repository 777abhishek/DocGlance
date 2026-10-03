using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Services.Documents;

/// <summary>
/// Skeleton implementation of IPdfViewerService.
/// Phase 1: foundation only — methods throw NotImplementedException.
/// Logic will be extracted from ViewerPage.Pdf.cs in Phases 2–5.
/// </summary>
public sealed class PdfViewerService : IPdfViewerService, IDisposable
{
    private PdfDocumentState? _document;
    private int _pageCount;
    private readonly HashSet<int> _renderedPages = new();
    private readonly HashSet<int> _renderedThumbs = new();
    private CancellationTokenSource? _prefetchCts;
    // _prefetchGeneration removed — was unused (CS0169). Will be added back in Phase 4 if needed for stale operation detection.

    // ── Document Lifecycle ────────────────────────────────────────────────

    public async Task<PdfLoadResult> LoadDocumentAsync(
        string filePath,
        Func<Task<string?>>? passwordPrompt = null,
        CancellationToken ct = default)
    {
        CloseDocument();

        try
        {
            // First attempt: open without password
            var openResult = await PdfRenderer.OpenDocumentAsync(filePath, ct);

            if (ct.IsCancellationRequested)
                return PdfLoadResult.Error("Operation cancelled");

            // Handle password-protected PDFs
            if (!openResult.Ok && openResult.NeedsPassword && passwordPrompt != null)
            {
                string? password = await passwordPrompt();
                if (password != null)
                {
                    // Retry with password (one attempt only)
                    openResult = await PdfRenderer.OpenDocumentAsync(filePath, ct, password);
                }
            }

            if (ct.IsCancellationRequested)
            {
                openResult.Document?.Dispose();
                return PdfLoadResult.Error("Operation cancelled");
            }

            if (!openResult.Ok || openResult.Document == null)
            {
                return PdfLoadResult.Error(openResult.Error ?? "Cannot open PDF");
            }

            // Success: store document state
            _document = openResult.Document;
            _pageCount = openResult.Document.PageCount;

            return PdfLoadResult.Ok(_document, _pageCount);
        }
        catch (OperationCanceledException)
        {
            return PdfLoadResult.Error("Operation cancelled");
        }
        catch (Exception ex)
        {
            return PdfLoadResult.Error($"Failed to load PDF: {ex.Message}");
        }
    }

    public void CloseDocument()
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = null;
        _document?.Dispose();
        _document = null;
        _pageCount = 0;
        _renderedPages.Clear();
        _renderedThumbs.Clear();
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    public Task<WriteableBitmap?> RenderPageProgressiveAsync(
        int pageIndex,
        Action<WriteableBitmap>? onFullQuality = null,
        CancellationToken ct = default)
        => throw new NotImplementedException("Phase 3");

    public Task<WriteableBitmap?> RenderThumbnailAsync(
        int pageIndex,
        CancellationToken ct = default)
        => throw new NotImplementedException("Phase 5");

    // ── Prefetch & Memory Management ──────────────────────────────────────

    public void PrefetchPagesAround(int centerPage)
        => throw new NotImplementedException("Phase 4");

    public void PrefetchThumbnailsAround(int centerPage)
        => throw new NotImplementedException("Phase 4");

    public void CancelPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = null;
    }

    public void InvalidateRenderedPages()
        => _renderedPages.Clear();

    // ── State Queries ─────────────────────────────────────────────────────

    public PdfDocumentState? CurrentDocument => _document;

    public int PageCount => _pageCount;

    public (double Width, double Height) GetPageSize(int pageIndex)
        => _document?.GetPageSize(pageIndex) ?? (0, 0);

    public double NaturalZoom
        => _document != null ? 96.0 / PdfRenderer.RenderDpi : 1.0;

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose() => CloseDocument();
}
