using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Models;
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

    // ── PDF Metadata & Permissions ────────────────────────────────────────

    public PdfPermissions GetPermissions()
    {
        if (_document == null)
            return PdfPermissions.Unrestricted;

        lock (Pdfium.Gate)
        {
            IntPtr doc = _document.Handle;
            if (doc == IntPtr.Zero)
                return PdfPermissions.Unrestricted;

            int revision = Pdfium.FPDF_GetSecurityHandlerRevision(doc);
            
            // Unencrypted PDFs return revision -1
            if (revision == -1)
                return PdfPermissions.Unrestricted;

            uint perms = Pdfium.FPDF_GetDocPermissions(doc);
            
            // 0xFFFFFFFF means all permissions granted
            if (perms == 0xFFFFFFFF)
                return PdfPermissions.Unrestricted;

            return new PdfPermissions
            {
                CanPrint = (perms & Pdfium.FPDF_PERM_PRINT) != 0,
                CanModify = (perms & Pdfium.FPDF_PERM_MODIFY) != 0,
                CanCopy = (perms & Pdfium.FPDF_PERM_EXTRACT) != 0,
                CanAnnotate = (perms & Pdfium.FPDF_PERM_ANNOTATE) != 0,
                CanFillForms = (perms & Pdfium.FPDF_PERM_FILL_FORM) != 0,
                IsEncrypted = true,
            };
        }
    }

    public PdfMetadata GetMetadata()
    {
        if (_document == null)
            return new PdfMetadata();

        return PdfMetadataService.GetMetadata(_document);
    }

    // ── IDisposable ───────────────────────────────────────────────────────

    public void Dispose() => CloseDocument();
}
