using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace QuickView.Features.Viewer.TextSelection;

/// <summary>
/// Provides text selection capabilities for different document types (PDF, DOCX, PPTX, XLSX).
/// Implementations use native APIs (PDFium, WordCraft, DeckCraft, GridCraft) to extract
/// text content and compute selection rectangles from mouse positions.
/// </summary>
public interface ITextSelectionProvider
{
    /// <summary>
    /// Get the character index at the given screen/viewport position.
    /// Returns null if no text exists at that position.
    /// </summary>
    /// <param name="viewportPoint">Point in viewport coordinates (relative to the ScrollViewer content)</param>
    int? GetCharIndexAtPosition(Point viewportPoint);

    /// <summary>
    /// Get all selection rectangles between startIndex and endIndex (inclusive).
    /// Returns rectangles in viewport coordinates.
    /// Multi-line selections return multiple rectangles.
    /// </summary>
    IReadOnlyList<Rect> GetSelectionRectangles(int startIndex, int endIndex);

    /// <summary>
    /// Extract the text between startIndex and endIndex (inclusive).
    /// </summary>
    string ExtractText(int startIndex, int endIndex);

    /// <summary>
    /// Get the total character count in the current page/sheet/slide.
    /// Used for bounds checking.
    /// </summary>
    int GetCharacterCount();

    /// <summary>
    /// Called when the page/zoom/rotation changes to update coordinate transforms.
    /// </summary>
    void UpdateTransform(double zoom, double rotation, Point scrollOffset);

    /// <summary>
    /// Called to release native resources when switching pages or closing document.
    /// </summary>
    void Cleanup();
}

/// <summary>
/// Immutable selection state captured from user drag interaction.
/// </summary>
public sealed record TextSelection(
    int StartIndex,
    int EndIndex,
    string Text,
    IReadOnlyList<Rect> HighlightRects
);
