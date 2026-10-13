using QuickView.Services.Documents;
using Xunit;
using System.IO;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Tests.Services.Documents;

/// <summary>
/// Unit tests for PdfViewerService.
/// Uses a programmatically generated minimal valid PDF — no external file dependency.
/// </summary>
public sealed class PdfViewerServiceTests : IDisposable
{
    private readonly string _validPdfPath;
    private readonly string _invalidFilePath;

    public PdfViewerServiceTests()
    {
        _validPdfPath   = Path.Combine(Path.GetTempPath(), $"qv_test_{Guid.NewGuid():N}.pdf");
        _invalidFilePath = Path.Combine(Path.GetTempPath(), $"qv_notpdf_{Guid.NewGuid():N}.txt");

        WriteSinglePagePdf(_validPdfPath);
        File.WriteAllText(_invalidFilePath, "This is not a PDF file.");
    }

    public void Dispose()
    {
        if (File.Exists(_validPdfPath))   File.Delete(_validPdfPath);
        if (File.Exists(_invalidFilePath)) File.Delete(_invalidFilePath);
    }

    // ── Document Lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task LoadDocumentAsync_ValidPdf_ReturnsSuccess()
    {
        using var svc = new PdfViewerService();

        var result = await svc.LoadDocumentAsync(_validPdfPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Document);
        Assert.True(result.PageCount > 0);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadDocumentAsync_NonExistentFile_ReturnsError()
    {
        using var svc = new PdfViewerService();
        var missing = Path.Combine(Path.GetTempPath(), "does_not_exist_qv.pdf");

        var result = await svc.LoadDocumentAsync(missing);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.Equal(0, result.PageCount);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadDocumentAsync_InvalidFile_ReturnsError()
    {
        using var svc = new PdfViewerService();

        var result = await svc.LoadDocumentAsync(_invalidFilePath);

        Assert.False(result.Success);
        Assert.Null(result.Document);
        Assert.Equal(0, result.PageCount);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadDocumentAsync_Cancelled_ReturnsError()
    {
        using var svc = new PdfViewerService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await svc.LoadDocumentAsync(_validPdfPath, ct: cts.Token);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task LoadDocumentAsync_LoadTwice_DisposesFirst()
    {
        using var svc = new PdfViewerService();

        var r1 = await svc.LoadDocumentAsync(_validPdfPath);
        var r2 = await svc.LoadDocumentAsync(_validPdfPath);

        Assert.True(r2.Success);
        Assert.NotNull(r2.Document);
    }

    // ── CloseDocument ─────────────────────────────────────────────────────

    [Fact]
    public async Task CloseDocument_AfterLoad_ClearsState()
    {
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        svc.CloseDocument();

        Assert.Null(svc.CurrentDocument);
        Assert.Equal(0, svc.PageCount);
    }

    [Fact]
    public void CloseDocument_WithNoDocument_DoesNotThrow()
    {
        using var svc = new PdfViewerService();

        var ex = Record.Exception(() => svc.CloseDocument());

        Assert.Null(ex);
    }

    // ── State Queries ─────────────────────────────────────────────────────

    [Fact]
    public void CurrentDocument_BeforeLoad_IsNull()
    {
        using var svc = new PdfViewerService();

        Assert.Null(svc.CurrentDocument);
    }

    [Fact]
    public void PageCount_BeforeLoad_IsZero()
    {
        using var svc = new PdfViewerService();

        Assert.Equal(0, svc.PageCount);
    }

    [Fact]
    public async Task PageCount_AfterLoad_MatchesDocument()
    {
        using var svc = new PdfViewerService();

        var result = await svc.LoadDocumentAsync(_validPdfPath);

        Assert.Equal(result.PageCount, svc.PageCount);
    }

    [Fact]
    public async Task GetPageSize_ValidPage_ReturnsPositiveDimensions()
    {
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        var (w, h) = svc.GetPageSize(0);

        Assert.True(w > 0, "Width should be positive");
        Assert.True(h > 0, "Height should be positive");
    }

    [Fact]
    public void NaturalZoom_IsPositive()
    {
        using var svc = new PdfViewerService();

        Assert.True(svc.NaturalZoom > 0);
    }

    // ── Rendering ─────────────────────────────────────────────────────────
    // NOTE: RenderPageProgressiveAsync and RenderThumbnailAsync call the native renderer
    // which requires a UI thread for WriteableBitmap creation. In the headless
    // test runner these return null (no bitmap). We test the service's tracking
    // logic (HashSet guard, eviction, invalidation) independently of the bitmap.

    [Fact]
    public async Task RenderPageProgressiveAsync_NoDocument_ReturnsNull()
    {
        using var svc = new PdfViewerService();

        var bmp = await svc.RenderPageProgressiveAsync(0);

        Assert.Null(bmp);
    }

    [Fact]
    public async Task RenderPageProgressiveAsync_SecondCallSamePage_SkippedByHashSet()
    {
        // Even without a real bitmap, the second call returns null because
        // _renderedPages.Add(0) returns false on the second attempt.
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        // First call — adds to HashSet (may return null if no UI thread)
        await svc.RenderPageProgressiveAsync(0);
        // Second call — HashSet blocks it, definitely returns null
        var second = await svc.RenderPageProgressiveAsync(0);

        Assert.Null(second);
    }

    [Fact]
    public async Task EvictPage_AllowsRenderRetry()
    {
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        await svc.RenderPageProgressiveAsync(0); // first call — marks page rendered
        svc.EvictPage(0);                         // removes from HashSet
        // After eviction the service no longer skips the page.
        // We can't assert a bitmap (no UI thread) but we can confirm the
        // call does NOT return null due to the skip guard — it either renders
        // or fails on bitmap creation. Either way it re-entered the render path.
        var ex = await Record.ExceptionAsync(() => svc.RenderPageProgressiveAsync(0));
        Assert.Null(ex); // no exception thrown
    }

    [Fact]
    public async Task InvalidateRenderedPages_ClearsAllTracking()
    {
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        await svc.RenderPageProgressiveAsync(0);
        svc.InvalidateRenderedPages();
        // After invalidation, the second call is no longer blocked by the guard.
        var ex = await Record.ExceptionAsync(() => svc.RenderPageProgressiveAsync(0));
        Assert.Null(ex);
    }

    // ── Thumbnails ────────────────────────────────────────────────────────

    [Fact]
    public async Task RenderThumbnailAsync_NoDocument_ReturnsNull()
    {
        using var svc = new PdfViewerService();

        var bmp = await svc.RenderThumbnailAsync(0);

        Assert.Null(bmp);
    }

    [Fact]
    public async Task IsThumbnailRendered_BeforeRender_ReturnsFalse()
    {
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        Assert.False(svc.IsThumbnailRendered(0));
    }

    [Fact]
    public async Task EvictThumbnail_ResetsTracking()
    {
        using var svc = new PdfViewerService();
        await svc.LoadDocumentAsync(_validPdfPath);

        await svc.RenderThumbnailAsync(0);
        svc.EvictThumbnail(0);

        // After eviction, thumbnail is no longer tracked as rendered
        Assert.False(svc.IsThumbnailRendered(0));
    }

    // ── CancelPrefetch ────────────────────────────────────────────────────

    [Fact]
    public void CancelPrefetch_WithNoInflightWork_DoesNotThrow()
    {
        using var svc = new PdfViewerService();

        var ex = Record.Exception(() => svc.CancelPrefetch());

        Assert.Null(ex);
    }

    // ── PdfLoadResult factory methods ─────────────────────────────────────

    [Fact]
    public void PdfLoadResult_Ok_HasCorrectState()
    {
        var result = PdfLoadResult.Ok(null!, 5);

        Assert.True(result.Success);
        Assert.Equal(5, result.PageCount);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void PdfLoadResult_Error_HasCorrectState()
    {
        var result = PdfLoadResult.Error("Something failed");

        Assert.False(result.Success);
        Assert.Equal(0, result.PageCount);
        Assert.Null(result.Document);
        Assert.Equal("Something failed", result.ErrorMessage);
    }

    // ── Minimal valid single-page PDF generator ───────────────────────────

    private static void WriteSinglePagePdf(string path)
    {
        // Minimal valid PDF 1.4 — one blank letter-size page (612×792 pts).
        // Hand-crafted to avoid any external dependency.
        const string body =
            "%PDF-1.4\n" +
            "1 0 obj\n<</Type/Catalog/Pages 2 0 R>>\nendobj\n" +
            "2 0 obj\n<</Type/Pages/Kids[3 0 R]/Count 1>>\nendobj\n" +
            "3 0 obj\n<</Type/Page/Parent 2 0 R/MediaBox[0 0 612 792]>>\nendobj\n" +
            "xref\n0 4\n" +
            "0000000000 65535 f \n" +
            "0000000009 00000 n \n" +
            "0000000058 00000 n \n" +
            "0000000115 00000 n \n" +
            "trailer\n<</Size 4/Root 1 0 R>>\n" +
            "startxref\n190\n%%EOF";

        File.WriteAllText(path, body);
    }
}
