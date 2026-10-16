using DocGlance.Rendering.Pdf;
using System.Collections.Generic;

namespace DocGlance.Rendering.Pdf.Models;

/// <summary>
/// Tracks the current viewport state for PDF rendering.
/// This replaces the old page-based caching approach with viewport-only rendering.
/// </summary>
public sealed class PdfViewportState
{
    /// <summary>Current scroll position in PDF points (horizontal)</summary>
    public double ScrollXPt { get; set; }

    /// <summary>Current scroll position in PDF points (vertical)</summary>
    public double ScrollYPt { get; set; }

    /// <summary>Viewport width in screen pixels</summary>
    public int ViewportWidthPx { get; set; }

    /// <summary>Viewport height in screen pixels</summary>
    public int ViewportHeightPx { get; set; }

    /// <summary>Current zoom factor (1.0 = 100%, 2.0 = 200%)</summary>
    public float ZoomFactor { get; set; } = 1.0f;

    /// <summary>Current page index (for multi-page viewport spanning)</summary>
    public int CurrentPageIndex { get; set; }

    /// <summary>Render DPI calculated from zoom factor</summary>
    public float RenderDpi => 96f * ZoomFactor;

    /// <summary>
    /// Calculates which pages are visible in the current viewport.
    /// Returns list of (pageIndex, pageTopPt, pageHeightPt) tuples.
    /// Canvas lp = pt × (96/72) × zoom, so lp → pt divides by both factors.
    /// </summary>
    public List<(int PageIndex, double PageTopPt, double PageHeightPt)> GetVisiblePages(
        PdfDocumentState document)
    {
        var visiblePages = new List<(int, double, double)>();
        double currentY = 0;
        const double pageSpacingPt = 12.0 * 72.0 / 96.0; // 12 layout px gap, in PDF points

        // Viewport height in PDF points
        double viewportHeightPt = ViewportHeightPx * 72.0 / RenderDpi;

        for (int i = 0; i < document.PageCount; i++)
        {
            var (_, pageHeightPt) = document.GetPageSize(i);
            double pageBottom = currentY + pageHeightPt;

            // Check if this page intersects the viewport
            bool intersects = !(pageBottom < ScrollYPt || currentY > ScrollYPt + viewportHeightPt);

            if (intersects)
            {
                visiblePages.Add((i, currentY, pageHeightPt));
            }

            currentY = pageBottom + pageSpacingPt;

            // Early exit if we've scrolled past the viewport
            if (currentY > ScrollYPt + viewportHeightPt)
                break;
        }

        return visiblePages;
    }
}
