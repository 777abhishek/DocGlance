using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Rendering.Pdf;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Services.Documents;

/// <summary>
/// Orchestrates PDF document lifecycle, progressive rendering, prefetching, and memory management.
/// Bridges ViewerPage UI layer and low-level PdfRenderer.
/// </summary>
public interface IPdfViewerService
{
    // ── Document Lifecycle ────────────────────────────────────────────────

    /// <summary>
    /// Opens a PDF document, handling passwords and validation.
    /// Returns document state + page count, or error message.
    /// </summary>
    Task<PdfLoadResult> LoadDocumentAsync(
        string filePath,
        Func<Task<string?>>? passwordPrompt = null,
        CancellationToken ct = default);

    /// <summary>Closes the document and releases all resources.</summary>
    void CloseDocument();

    // ── Rendering ─────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a page with progressive strategy (fast preview → full quality).
    /// Returns preview bitmap synchronously if available, full quality via callback.
    /// </summary>
    Task<WriteableBitmap?> RenderPageProgressiveAsync(
        int pageIndex,
        Action<WriteableBitmap>? onFullQuality = null,
        CancellationToken ct = default);

    /// <summary>Renders a thumbnail at low DPI.</summary>
    Task<WriteableBitmap?> RenderThumbnailAsync(
        int pageIndex,
        CancellationToken ct = default);

    // ── Prefetch & Memory Management ──────────────────────────────────────

    /// <summary>
    /// Prefetches pages around the current page for smooth scrolling.
    /// Evicts pages outside the window to manage memory.
    /// </summary>
    void PrefetchPagesAround(int centerPage);

    /// <summary>
    /// Prefetches thumbnails around the current page for smooth sidebar scrolling.
    /// </summary>
    void PrefetchThumbnailsAround(int centerPage);

    /// <summary>Cancels all in-flight prefetch operations.</summary>
    void CancelPrefetch();

    /// <summary>
    /// Triggers re-render of visible pages when zoom changes significantly.
    /// Used when zoom crosses DPI threshold (e.g., 2x → needs higher res render).
    /// </summary>
    void InvalidateRenderedPages();

    // ── State Queries ─────────────────────────────────────────────────────

    /// <summary>Gets the current document state (null if no document loaded).</summary>
    PdfDocumentState? CurrentDocument { get; }

    /// <summary>Gets page count (0 if no document loaded).</summary>
    int PageCount { get; }

    /// <summary>Gets page size in points (PDF units, 1 pt = 1/72 inch).</summary>
    (double Width, double Height) GetPageSize(int pageIndex);

    /// <summary>Natural zoom factor (96 DPI / RenderDpi).</summary>
    double NaturalZoom { get; }
}
