using QuickView.Rendering.Document;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace QuickView.Features.Viewer.TextSelection.Providers;

/// <summary>
/// Text selection provider for DOCX documents using WordCraft FFI.
/// 
/// LIMITATION: WordCraft FFI provides text extraction and search with bounding boxes,
/// but NOT character-level hit testing (no wc_get_char_at_position equivalent).
/// 
/// Therefore, this provider only supports COPY functionality from existing search results,
/// NOT interactive drag-to-select. To enable drag-to-select, WordCraft FFI would need:
/// - wc_get_char_index_at_pos(doc, page, x, y) → charIndex
/// - wc_get_char_box(doc, page, charIndex) → (x, y, w, h)
/// </summary>
public sealed class DocxTextSelectionProvider : ITextSelectionProvider
{
    private readonly DocxDocument _document;
    private int _currentPageIndex;
    private double _zoom = 1.0;
    private Point _scrollOffset;
    private string? _cachedPageText;

    public DocxTextSelectionProvider(DocxDocument document, int currentPageIndex, double zoom = 1.0)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _currentPageIndex = currentPageIndex;
        _zoom = zoom;
    }

    public void UpdateTransform(double zoom, double rotation, Point scrollOffset)
    {
        _zoom = zoom;
        _scrollOffset = scrollOffset;
    }

    public void SetCurrentPage(int pageIndex)
    {
        if (_currentPageIndex != pageIndex)
        {
            _currentPageIndex = pageIndex;
            _cachedPageText = null;
        }
    }

    // NOT SUPPORTED: WordCraft FFI lacks character-level hit testing
    public int? GetCharIndexAtPosition(Point viewportPoint)
    {
        // Would need: wc_get_char_index_at_pos(page, x, y) in Rust FFI
        return null;
    }

    // NOT SUPPORTED: Without hit testing, can't create selection rectangles
    public IReadOnlyList<Rect> GetSelectionRectangles(int startIndex, int endIndex)
    {
        // Would need character bounding box APIs in WordCraft FFI
        return Array.Empty<Rect>();
    }

    // SUPPORTED: Can extract text from the page
    public string ExtractText(int startIndex, int endIndex)
    {
        if (_cachedPageText == null)
        {
            _cachedPageText = _document.ExtractPageText(_currentPageIndex);
        }

        if (string.IsNullOrEmpty(_cachedPageText))
            return string.Empty;

        int start = Math.Max(0, Math.Min(startIndex, _cachedPageText.Length));
        int end = Math.Max(0, Math.Min(endIndex + 1, _cachedPageText.Length));
        int length = Math.Max(0, end - start);

        return length > 0 ? _cachedPageText.Substring(start, length) : string.Empty;
    }

    public int GetCharacterCount()
    {
        if (_cachedPageText == null)
        {
            _cachedPageText = _document.ExtractPageText(_currentPageIndex);
        }
        return _cachedPageText?.Length ?? 0;
    }

    public void Cleanup()
    {
        _cachedPageText = null;
    }
}
