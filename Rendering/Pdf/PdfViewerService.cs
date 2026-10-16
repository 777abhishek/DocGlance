using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Rendering.Pdf;

/// <summary>
/// Per-tab PDF document lifecycle and render bookkeeping (which pages/thumbnails have been
/// rendered). Prefetch scheduling itself lives in ViewerPage.
/// </summary>
public sealed class PdfViewerService : IPdfViewerService, IDisposable
{
    private PdfDocumentState? _document;
    private int _pageCount;
    private readonly HashSet<int> _renderedPages = new();
    private readonly HashSet<int> _renderedThumbs = new();
    private CancellationTokenSource? _prefetchCts;


    // ── Document Lifecycle ────────────────────────────────────────────────

    public async Task<PdfLoadResult> LoadDocumentAsync(
        string filePath,
        Func<string?, Task<string?>>? passwordPrompt = null,
        CancellationToken ct = default)
    {
        using var op = Logger.BeginOperation("PdfOpen", ("FilePath", filePath));
        CloseDocument();

        try
        {
            // First attempt: open without password
            var openResult = await PdfRenderer.OpenDocumentAsync(filePath, ct);

            if (ct.IsCancellationRequested)
            {
                op.MarkCancelled();
                return PdfLoadResult.Error("Operation cancelled");
            }

            // Retry until the password is accepted or the user cancels.
            while (!openResult.Ok && openResult.NeedsPassword && passwordPrompt != null)
            {
                string? password = await passwordPrompt(openResult.Error);
                if (password == null)
                    return PdfLoadResult.Error("Opening the PDF was canceled.");

                openResult = await PdfRenderer.OpenDocumentAsync(filePath, ct, password);
            }

            if (ct.IsCancellationRequested)
            {
                op.MarkCancelled();
                openResult.Document?.Dispose();
                return PdfLoadResult.Error("Operation cancelled");
            }

            if (!openResult.Ok || openResult.Document == null)
            {
                op.Fail();
                return PdfLoadResult.Error(openResult.Error ?? "Cannot open PDF");
            }

            // Success: store document state
            _document = openResult.Document;
            _pageCount = openResult.Document.PageCount;

            return PdfLoadResult.Ok(_document, _pageCount);
        }
        catch (OperationCanceledException)
        {
            op.MarkCancelled();
            return PdfLoadResult.Error("Operation cancelled");
        }
        catch (Exception ex)
        {
            op.Fail();
            Logger.Write(LogLevel.Error, "Event=PdfOpenFailed", ex);
            return PdfLoadResult.Error($"Failed to load PDF: {ex.Message}");
        }
    }

    public async Task<PdfLoadResult> LoadDocumentFromBytesAsync(
        byte[] bytes,
        CancellationToken ct = default)
    {
        CloseDocument();

        try
        {
            var openResult = await PdfRenderer.OpenDocumentFromBytesAsync(bytes, ct);

            if (ct.IsCancellationRequested)
                return PdfLoadResult.Error("Operation cancelled");

            if (!openResult.Ok || openResult.Document == null)
            {
                return PdfLoadResult.Error(openResult.Error ?? "Cannot open PDF from memory");
            }

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
            return PdfLoadResult.Error($"Failed to load PDF from memory: {ex.Message}");
        }
    }

    public async Task<PdfLoadResult> ReplaceDocumentFromPathAsync(
        string filePath,
        CancellationToken ct = default)
    {
        try
        {
            var openResult = await PdfRenderer.OpenDocumentAsync(filePath, ct);
            if (ct.IsCancellationRequested)
            {
                openResult.Document?.Dispose();
                return PdfLoadResult.Error("Operation cancelled");
            }

            if (!openResult.Ok || openResult.Document == null)
                return PdfLoadResult.Error(openResult.Error ?? "Cannot replace PDF document");

            var previousDocument = _document;
            _document = openResult.Document;
            _pageCount = openResult.Document.PageCount;
            CancelPrefetch();
            previousDocument?.Dispose();

            return PdfLoadResult.Ok(_document, _pageCount);
        }
        catch (OperationCanceledException)
        {
            return PdfLoadResult.Error("Operation cancelled");
        }
        catch (Exception ex)
        {
            Logger.Write(LogLevel.Error, "Event=PdfReplaceFailed", ex);
            return PdfLoadResult.Error($"Failed to replace PDF: {ex.Message}");
        }
    }

    public void CloseDocument()
    {
        // Cancel only — a render task may still hold a token from this CTS.
        _prefetchCts?.Cancel();
        _prefetchCts = null;

        if (_document != null)
        {
            Logger.Write(LogLevel.Debug,
                $"Event=PdfDocumentClosed | PageCount={_pageCount}");
            _document.Dispose();
            _document = null;
        }
        _pageCount = 0;
        _renderedPages.Clear();
        _renderedThumbs.Clear();
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a page at dynamic DPI (screen_dpi × zoomFactor). No preview phase — avoids
    /// the blurry-preview-stuck problem. Skips if already rendered.
    /// </summary>
    public async Task<WriteableBitmap?> RenderPageProgressiveAsync(
        int pageIndex,
        float zoomFactor = 1.0f,
        Action<WriteableBitmap>? onFullQuality = null,
        CancellationToken ct = default)
    {
        if (_document == null)
            return null;

        // Skip if already rendered
        if (!_renderedPages.Add(pageIndex))
            return null;

        try
        {
            // Calculate DPI based on zoom factor (96 at 1.0x, 144 at 1.5x, 192 at 2.0x)
            float renderDpi = PdfRenderer.GetRenderDpi(zoomFactor);
            
            var bmp = await PdfRenderer.RenderPageAsync(_document, pageIndex, renderDpi, ct);

            if (bmp == null || ct.IsCancellationRequested)
            {
                _renderedPages.Remove(pageIndex);
                return null;
            }

            // Fire the onFullQuality callback (same bitmap, used by search highlight path)
            onFullQuality?.Invoke(bmp);

            return bmp;
        }
        catch (Exception ex)
        {
            _renderedPages.Remove(pageIndex);
            System.Diagnostics.Debug.WriteLine($"[PdfViewerService] Render failed for page {pageIndex}: {ex.Message}");
            return null;
        }
    }

    public Task<WriteableBitmap?> RenderThumbnailAsync(
        int pageIndex,
        CancellationToken ct = default)
    {
        if (_document == null)
            return Task.FromResult<WriteableBitmap?>(null);

        // Skip if already rendered
        if (!_renderedThumbs.Add(pageIndex))
            return Task.FromResult<WriteableBitmap?>(null);

        return RenderThumbnailCoreAsync(pageIndex, ct);
    }

    private async Task<WriteableBitmap?> RenderThumbnailCoreAsync(int pageIndex, CancellationToken ct)
    {
        try
        {
            var bmp = await PdfRenderer.RenderPageAsync(_document!, pageIndex, PdfRenderer.ThumbnailDpi, ct);
            if (bmp == null || ct.IsCancellationRequested)
            {
                _renderedThumbs.Remove(pageIndex);
                return null;
            }
            return bmp;
        }
        catch (Exception ex)
        {
            _renderedThumbs.Remove(pageIndex);
            System.Diagnostics.Debug.WriteLine($"[PdfViewerService] Thumbnail render failed for page {pageIndex}: {ex.Message}");
            return null;
        }
    }

    // ── Prefetch & Memory Management ──────────────────────────────────────


    public void CancelPrefetch()
    {
        _prefetchCts?.Cancel();
        _prefetchCts = null;
    }

    public void InvalidateRenderedPages() => _renderedPages.Clear();

    public void EvictPage(int pageIndex) => _renderedPages.Remove(pageIndex);

    public void EvictThumbnail(int pageIndex) => _renderedThumbs.Remove(pageIndex);

    public bool IsThumbnailRendered(int pageIndex) => _renderedThumbs.Contains(pageIndex);

    // ── State Queries ─────────────────────────────────────────────────────

    public PdfDocumentState? CurrentDocument => _document;

    public int PageCount => _pageCount;

    public (double Width, double Height) GetPageSize(int pageIndex)
        => _document?.GetPageSize(pageIndex) ?? (0, 0);

    public double NaturalZoom => 1.0; // Always 1.0 with dynamic DPI rendering

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose() => CloseDocument();
}
