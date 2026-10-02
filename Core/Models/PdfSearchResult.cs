using System.Collections.Generic;

namespace QuickView.Core.Models;

/// <summary>
/// One search hit on a page.
/// CharIndex and Length are the PDFium text-layer indices for the matched span —
/// used to identify the active hit across pages without relying on list position.
/// Rects are in top-left-origin page points (1 pt = 1 px at 72 DPI), already
/// converted from PDFium's bottom-left origin at extraction time.
/// </summary>
public sealed record PdfSearchResult(
    int PageIndex,
    int CharIndex,
    int Length,
    IReadOnlyList<PdfSearchRect> Rects);

/// <summary>
/// A highlight rectangle in top-left-origin page points.
/// X, Y is the top-left corner; Width and Height are positive extents.
/// Multiply by (RenderDpi / 96.0) to get physical bitmap pixels.
/// </summary>
public readonly record struct PdfSearchRect(double X, double Y, double Width, double Height);

public sealed record PdfPageText(int PageIndex, string Text, IReadOnlyList<PdfSearchRect> CharacterRects);