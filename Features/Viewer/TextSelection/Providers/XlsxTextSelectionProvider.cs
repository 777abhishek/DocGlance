using QuickView.Rendering.Document;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace QuickView.Features.Viewer.TextSelection.Providers;

/// <summary>
/// Text selection provider for XLSX documents using GridCraft FFI.
/// 
/// LIMITATION: GridCraft FFI provides text extraction and cell-based search,
/// but NOT character-level hit testing (no gc_get_char_at_position equivalent).
/// 
/// Spreadsheet text selection is fundamentally different from document text:
/// - Text is organized in discrete cells, not continuous paragraphs
/// - Selection should be CELL-BASED (copy cell values), not character-based
/// - The SpreadsheetViewer already implements cell selection
/// 
/// Therefore, character-level text selection is NOT APPROPRIATE for spreadsheets.
/// Use the existing SpreadsheetViewer cell selection system instead.
/// </summary>
public sealed class XlsxTextSelectionProvider : ITextSelectionProvider
{
    private readonly SpreadsheetDocument _document;
    private int _currentSheetIndex;

    public XlsxTextSelectionProvider(SpreadsheetDocument document, int currentSheetIndex)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _currentSheetIndex = currentSheetIndex;
    }

    public void UpdateTransform(double zoom, double rotation, Point scrollOffset)
    {
        // Not used for spreadsheets
    }

    public void SetCurrentSheet(int sheetIndex)
    {
        _currentSheetIndex = sheetIndex;
    }

    // NOT SUPPORTED: Spreadsheet selection is cell-based, not character-based
    public int? GetCharIndexAtPosition(Point viewportPoint) => null;

    public IReadOnlyList<Rect> GetSelectionRectangles(int startIndex, int endIndex) => Array.Empty<Rect>();

    // Can extract sheet text, but not recommended for UI selection
    public string ExtractText(int startIndex, int endIndex)
    {
        var fullText = _document.ExtractSheetText(_currentSheetIndex);
        if (string.IsNullOrEmpty(fullText))
            return string.Empty;

        int start = Math.Max(0, Math.Min(startIndex, fullText.Length));
        int end = Math.Max(0, Math.Min(endIndex + 1, fullText.Length));
        int length = Math.Max(0, end - start);

        return length > 0 ? fullText.Substring(start, length) : string.Empty;
    }

    public int GetCharacterCount()
    {
        return _document.ExtractSheetText(_currentSheetIndex).Length;
    }

    public void Cleanup()
    {
        // No state to cleanup
    }
}
