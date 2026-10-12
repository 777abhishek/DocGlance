using QuickView.Rendering.Presentation;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace QuickView.Features.Viewer.TextSelection.Providers;

/// <summary>
/// Text selection provider for PPTX documents using DeckCraft FFI.
/// 
/// LIMITATION: DeckCraft FFI provides text extraction and search with bounding boxes,
/// but NOT character-level hit testing (no dc_get_char_at_position equivalent).
/// 
/// Therefore, this provider only supports COPY functionality from existing search results,
/// NOT interactive drag-to-select. To enable drag-to-select, DeckCraft FFI would need:
/// - dc_get_char_index_at_pos(doc, slide, x, y) → charIndex
/// - dc_get_char_box(doc, slide, charIndex) → (x, y, w, h)
/// </summary>
public sealed class PptxTextSelectionProvider : ITextSelectionProvider
{
    private readonly PptxDocument _document;
    private int _currentSlideIndex;
    private double _zoom = 1.0;
    private Point _scrollOffset;
    private string? _cachedSlideText;

    public PptxTextSelectionProvider(PptxDocument document, int currentSlideIndex, double zoom = 1.0)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _currentSlideIndex = currentSlideIndex;
        _zoom = zoom;
    }

    public void UpdateTransform(double zoom, double rotation, Point scrollOffset)
    {
        _zoom = zoom;
        _scrollOffset = scrollOffset;
    }

    public void SetCurrentSlide(int slideIndex)
    {
        if (_currentSlideIndex != slideIndex)
        {
            _currentSlideIndex = slideIndex;
            _cachedSlideText = null;
        }
    }

    // NOT SUPPORTED: DeckCraft FFI lacks character-level hit testing
    public int? GetCharIndexAtPosition(Point viewportPoint)
    {
        // Would need: dc_get_char_index_at_pos(slide, x, y) in Rust FFI
        return null;
    }

    // NOT SUPPORTED: Without hit testing, can't create selection rectangles
    public IReadOnlyList<Rect> GetSelectionRectangles(int startIndex, int endIndex)
    {
        // Would need character bounding box APIs in DeckCraft FFI
        return Array.Empty<Rect>();
    }

    // SUPPORTED: Can extract text from the slide
    public string ExtractText(int startIndex, int endIndex)
    {
        if (_cachedSlideText == null)
        {
            _cachedSlideText = _document.ExtractSlideText(_currentSlideIndex);
        }

        if (string.IsNullOrEmpty(_cachedSlideText))
            return string.Empty;

        int start = Math.Max(0, Math.Min(startIndex, _cachedSlideText.Length));
        int end = Math.Max(0, Math.Min(endIndex + 1, _cachedSlideText.Length));
        int length = Math.Max(0, end - start);

        return length > 0 ? _cachedSlideText.Substring(start, length) : string.Empty;
    }

    public int GetCharacterCount()
    {
        if (_cachedSlideText == null)
        {
            // Extract text to get count
            ExtractText(0, 0);
        }
        return _cachedSlideText?.Length ?? 0;
    }

    public void Cleanup()
    {
        _cachedSlideText = null;
    }
}
