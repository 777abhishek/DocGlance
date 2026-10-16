using Microsoft.UI.Xaml.Media.Imaging;
using DocGlance.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Rendering.Pdf;

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
        Func<string?, Task<string?>>? passwordPrompt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Opens a PDF document from in-memory bytes.
    /// </summary>
    Task<PdfLoadResult> LoadDocumentFromBytesAsync(
        byte[] bytes,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the active PDF state without invalidating rendered-page bookkeeping.
    /// Use when the PDF changes only through annotation updates and cached page images
    /// remain valid.
    /// </summary>
    Task<PdfLoadResult> ReplaceDocumentFromPathAsync(
        string filePath,
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
        float zoomFactor = 1.0f,
        Action<WriteableBitmap>? onFullQuality = null,
        CancellationToken ct = default);

    /// <summary>Renders a thumbnail at low DPI.</summary>
    Task<WriteableBitmap?> RenderThumbnailAsync(
        int pageIndex,
        CancellationToken ct = default);

    // ── Prefetch & Memory Management ──────────────────────────────────────


    /// <summary>Cancels all in-flight prefetch operations.</summary>
    void CancelPrefetch();

    /// <summary>Clears all rendered page tracking so pages will be re-rendered.</summary>
    void InvalidateRenderedPages();

    /// <summary>Marks a page as evicted so it can be re-rendered when needed.</summary>
    void EvictPage(int pageIndex);

    /// <summary>Marks a thumbnail as evicted so it can be re-rendered when needed.</summary>
    void EvictThumbnail(int pageIndex);

    /// <summary>Returns true if the thumbnail for the given page has been rendered.</summary>
    bool IsThumbnailRendered(int pageIndex);

    // ── State Queries ─────────────────────────────────────────────────────

    /// <summary>Gets the current document state (null if no document loaded).</summary>
    PdfDocumentState? CurrentDocument { get; }

    /// <summary>Gets page count (0 if no document loaded).</summary>
    int PageCount { get; }

    /// <summary>Gets page size in points (PDF units, 1 pt = 1/72 inch).</summary>
    (double Width, double Height) GetPageSize(int pageIndex);

    /// <summary>Natural zoom factor (always 1.0 with dynamic DPI rendering).</summary>
    double NaturalZoom { get; }

}
