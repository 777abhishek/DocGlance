using System;
using System.Collections.Generic;

namespace DocGlance.Core.Models;

/// <summary>Cached glyph geometry used for PDF text hit testing and selection.</summary>
public sealed class PdfPageTextMap
{
    private readonly PdfSearchRect[] _glyphRects;

    public int PageIndex { get; }
    public int GlyphCount => _glyphRects.Length;

    public PdfPageTextMap(int pageIndex, IReadOnlyList<PdfSearchRect> glyphRects)
    {
        PageIndex = pageIndex;
        _glyphRects = new PdfSearchRect[glyphRects.Count];
        for (int i = 0; i < glyphRects.Count; i++)
            _glyphRects[i] = glyphRects[i];
    }

    public int HitTest(double x, double y, double tolerance = 6)
    {
        int nearest = -1;
        double nearestDistance = double.MaxValue;

        for (int i = 0; i < _glyphRects.Length; i++)
        {
            var rect = _glyphRects[i];
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            double dx = x < rect.X ? rect.X - x :
                x > rect.X + rect.Width ? x - (rect.X + rect.Width) : 0;
            double dy = y < rect.Y ? rect.Y - y :
                y > rect.Y + rect.Height ? y - (rect.Y + rect.Height) : 0;

            if (dx == 0 && dy == 0)
                return i;

            double distance = Math.Max(dx, dy);
            if (distance <= tolerance && distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>Returns merged per-line rectangles for a half-open glyph range.</summary>
    public IReadOnlyList<PdfSearchRect> GetSelectionRects(int start, int endExclusive)
    {
        start = Math.Clamp(start, 0, _glyphRects.Length);
        endExclusive = Math.Clamp(endExclusive, start, _glyphRects.Length);
        var result = new List<PdfSearchRect>();
        PdfSearchRect line = default;
        bool hasLine = false;

        for (int i = start; i < endExclusive; i++)
        {
            var rect = _glyphRects[i];
            if (rect.Width <= 0 || rect.Height <= 0)
                continue;

            if (hasLine && VerticallyOverlaps(line, rect))
            {
                double left = Math.Min(line.X, rect.X);
                double top = Math.Min(line.Y, rect.Y);
                double right = Math.Max(line.X + line.Width, rect.X + rect.Width);
                double bottom = Math.Max(line.Y + line.Height, rect.Y + rect.Height);
                line = new PdfSearchRect(left, top, right - left, bottom - top);
            }
            else
            {
                if (hasLine)
                    result.Add(line);
                line = rect;
                hasLine = true;
            }
        }

        if (hasLine)
            result.Add(line);
        return result;
    }

    private static bool VerticallyOverlaps(PdfSearchRect a, PdfSearchRect b)
    {
        double overlap = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
        double minHeight = Math.Min(a.Height, b.Height);
        return minHeight > 0 && overlap >= minHeight * 0.5;
    }
}
