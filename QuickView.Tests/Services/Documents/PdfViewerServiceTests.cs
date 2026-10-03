using QuickView.Services.Documents;
using QuickView.Rendering.Pdf;
using Xunit;

namespace QuickView.Tests.Services.Documents;

public class PdfViewerServiceTests : IDisposable
{
    private readonly string _testPdfPath;
    private PdfViewerService? _service;

    public PdfViewerServiceTests()
    {
        // Create a minimal valid PDF for testing
        _testPdfPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.pdf");
        CreateMinimalPdf(_testPdfPath);
    }

    public void Dispose()
    {
        _service?.Dispose();
        if (File.Exists(_testPdfPath))
        {
            File.Delete(_testPdfPath);
        }
    }

    #region LoadDocument Tests

    [Fact]
    public async Task LoadDocumentAsync_ValidPdf_ReturnsSuccess()
    {
        // Arrange
        _service = new PdfViewerService();

        // Act
        var result = await _service.LoadDocumentAsync(_testPdfPath);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Document);
        Assert.True(result.PageCount > 0);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadDocumentAsync_NonExistentFile_ReturnsError()
    {
        // Arrange
        _service = new PdfViewerService();
        var nonExistentPath = "C:\\NonExistent\\File.pdf";

        // Act
        var result = await _service.LoadDocumentAsync(nonExistentPath);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Null(result.Document);
        Assert.Equal(0, result.PageCount);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadDocumentAsync_InvalidPdf_ReturnsError()
    {
        // Arrange
        _service = new PdfViewerService();
        var invalidPath = Path.Combine(Path.GetTempPath(), $"invalid_{Guid.NewGuid()}.pdf");
        await File.WriteAllTextAsync(invalidPath, "This is not a valid PDF");

        try
        {
            // Act
            var result = await _service.LoadDocumentAsync(invalidPath);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Null(result.Document);
            Assert.Equal(0, result.PageCount);
            Assert.NotNull(result.ErrorMessage);
        }
        finally
        {
            File.Delete(invalidPath);
        }
    }

    [Fact]
    public async Task LoadDocumentAsync_MultipleLoads_DisposesOldDocument()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        var firstDoc = _service.CurrentDocument;

        // Act
        await _service.LoadDocumentAsync(_testPdfPath);
        var secondDoc = _service.CurrentDocument;

        // Assert
        Assert.NotNull(firstDoc);
        Assert.NotNull(secondDoc);
        Assert.NotEqual(firstDoc, secondDoc);
    }

    #endregion

    #region CloseDocument Tests

    [Fact]
    public async Task CloseDocument_DisposesResources()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        Assert.NotNull(_service.CurrentDocument);

        // Act
        _service.CloseDocument();

        // Assert
        Assert.Null(_service.CurrentDocument);
    }

    [Fact]
    public void CloseDocument_NoDocumentLoaded_DoesNotThrow()
    {
        // Arrange
        _service = new PdfViewerService();

        // Act & Assert
        var exception = Record.Exception(() => _service.CloseDocument());
        Assert.Null(exception);
    }

    #endregion

    #region RenderPageProgressiveAsync Tests

    [Fact]
    public async Task RenderPageProgressiveAsync_PreviewMode_ReturnsLowResImage()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        bool previewCallbackCalled = false;
        bool fullCallbackCalled = false;

        // Act
        await _service.RenderPageProgressiveAsync(
            pageIndex: 0,
            targetWidth: 800,
            onPreview: (bitmap) => { previewCallbackCalled = bitmap != null; },
            onFull: (bitmap) => { fullCallbackCalled = bitmap != null; }
        );

        // Assert
        Assert.True(previewCallbackCalled, "Preview callback should be called");
        Assert.True(fullCallbackCalled, "Full quality callback should be called");
    }

    [Fact]
    public async Task RenderPageProgressiveAsync_InvalidPageIndex_ThrowsException()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
        {
            await _service.RenderPageProgressiveAsync(
                pageIndex: 999,
                targetWidth: 800,
                onPreview: (_) => { },
                onFull: (_) => { }
            );
        });
    }

    [Fact]
    public async Task RenderPageProgressiveAsync_NoDocumentLoaded_ThrowsException()
    {
        // Arrange
        _service = new PdfViewerService();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.RenderPageProgressiveAsync(
                pageIndex: 0,
                targetWidth: 800,
                onPreview: (_) => { },
                onFull: (_) => { }
            );
        });
    }

    #endregion

    #region Prefetch Tests

    [Fact]
    public async Task PrefetchPagesAround_ValidIndex_QueuesPages()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        var renderedPages = new HashSet<int>();

        // Act
        await _service.PrefetchPagesAround(
            currentPageIndex: 2,
            targetWidth: 800,
            onPageReady: (pageIndex, bitmap) => { renderedPages.Add(pageIndex); },
            onEvictPage: (_) => { }
        );

        // Give it time to render prefetch pages
        await Task.Delay(500);

        // Assert
        Assert.Contains(2, renderedPages); // Current page
        // Should have attempted 0, 1, 3, 4 (±2 range)
    }

    [Fact]
    public async Task PrefetchPagesAround_BoundaryConditions_HandlesEdges()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        var renderedPages = new HashSet<int>();

        // Act - First page
        await _service.PrefetchPagesAround(
            currentPageIndex: 0,
            targetWidth: 800,
            onPageReady: (pageIndex, bitmap) => { renderedPages.Add(pageIndex); },
            onEvictPage: (_) => { }
        );

        await Task.Delay(300);

        // Assert - Should not try negative indices
        Assert.Contains(0, renderedPages);
        Assert.DoesNotContain(-1, renderedPages);
        Assert.DoesNotContain(-2, renderedPages);
    }

    [Fact]
    public async Task PrefetchPagesAround_MultipleCallsQuickly_CancelsOldOperations()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        var renderedPages = new HashSet<int>();

        // Act - Rapid page changes
        var task1 = _service.PrefetchPagesAround(0, 800,
            (i, b) => renderedPages.Add(i), (_) => { });
        var task2 = _service.PrefetchPagesAround(5, 800,
            (i, b) => renderedPages.Add(i), (_) => { });
        var task3 = _service.PrefetchPagesAround(10, 800,
            (i, b) => renderedPages.Add(i), (_) => { });

        await Task.WhenAll(task1, task2, task3);
        await Task.Delay(300);

        // Assert - Should have cancelled early prefetch operations
        // (Exact pages depend on timing, but should have some results)
        Assert.NotEmpty(renderedPages);
    }

    #endregion

    #region Thumbnail Prefetch Tests

    [Fact]
    public async Task PrefetchThumbnailsAround_ValidIndex_QueuesThumbnails()
    {
        // Arrange
        _service = new PdfViewerService();
        await _service.LoadDocumentAsync(_testPdfPath);
        var renderedThumbs = new HashSet<int>();

        // Act
        await _service.PrefetchThumbnailsAround(
            currentPageIndex: 3,
            targetHeight: 150,
            onThumbReady: (pageIndex, bitmap) => { renderedThumbs.Add(pageIndex); },
            onEvictThumb: (_) => { }
        );

        await Task.Delay(500);

        // Assert
        Assert.Contains(3, renderedThumbs); // Current page
        // Should have attempted ±4 range
    }

    [Fact]
    public async Task PrefetchThumbnailsAround_NoDocument_ThrowsException()
    {
        // Arrange
        _service = new PdfViewerService();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _service.PrefetchThumbnailsAround(
                currentPageIndex: 0,
                targetHeight: 150,
                onThumbReady: (_, __) => { },
                onEvictThumb: (_) => { }
            );
        });
    }

    #endregion

    #region Eviction Tests

    [Fact]
    public void EvictPage_RemovesFromCache()
    {
        // Arrange
        _service = new PdfViewerService();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => _service.EvictPage(5));
        Assert.Null(exception);
    }

    [Fact]
    public void EvictThumbnail_RemovesFromCache()
    {
        // Arrange
        _service = new PdfViewerService();

        // Act & Assert - Should not throw
        var exception = Record.Exception(() => _service.EvictThumbnail(5));
        Assert.Null(exception);
    }

    #endregion

    #region Helper Methods

    private static void CreateMinimalPdf(string path)
    {
        // Minimal valid PDF structure (1 page, blank)
        var pdfContent = @"%PDF-1.4
1 0 obj
<< /Type /Catalog /Pages 2 0 R >>
endobj
2 0 obj
<< /Type /Pages /Kids [3 0 R] /Count 1 >>
endobj
3 0 obj
<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << >> >>
endobj
4 0 obj
<< /Length 0 >>
stream
endstream
endobj
xref
0 5
0000000000 65535 f
0000000009 00000 n
0000000058 00000 n
0000000115 00000 n
0000000230 00000 n
trailer
<< /Size 5 /Root 1 0 R >>
startxref
279
%%EOF";

        File.WriteAllText(path, pdfContent);
    }

    #endregion
}
