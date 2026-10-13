using System;

namespace QuickView.Rendering;

/// <summary>
/// Shared raster limits: render at device pixels plus a small text oversample
/// so fonts stay sharp when ScrollViewer scales between re-renders.
/// </summary>
internal static class RenderBudget
{
    /// <summary>1.25× device pixels — same oversample the viewer used before the RAM pass.</summary>
    public const float TextOversample = 1.25f;

    /// <summary>Peak decoded page/slide bitmap. ~48 MB holds a letter page near 300 DPI.</summary>
    public const long MaxPageBitmapBytes = 48L * 1024 * 1024;

    /// <summary>Decoded pages kept in the viewer cache (visible + small lookahead).</summary>
    public const long MaxViewerCacheBytes = 160L * 1024 * 1024;

    /// <summary>Sidebar thumbs target ~160 CSS px wide, not full 96-DPI pages.</summary>
    public const float ThumbnailDpi = 48f;

    public const double ThumbnailOfficeScale = 0.2;

    public static float PdfPageDpi(float zoom, double rasterizationScale, double widthPt, double heightPt)
    {
        float dpi = 96f
            * (float)Math.Max(1.0, rasterizationScale)
            * Math.Clamp(zoom, 0.25f, 8f)
            * TextOversample;
        return CapDpi(dpi, widthPt, heightPt);
    }

    public static bool NeedsRerender(float cachedDpi, float targetDpi)
    {
        if (cachedDpi <= 0f || targetDpi <= 0f)
            return true;

        float ratio = cachedDpi / targetDpi;
        // Too low → blurry when zooming in. Too high → leftover high-DPI after zoom-out
        // still looks soft if the bitmap was never rebuilt at the new scale.
        return ratio < 0.92f || ratio > 1.25f;
    }

    public static float CapDpi(float dpi, double widthPt, double heightPt)
    {
        double bytes = PixelBytes(widthPt, heightPt, dpi / 72.0);
        if (bytes <= MaxPageBitmapBytes || bytes <= 0)
            return dpi;

        float scaled = dpi * (float)Math.Sqrt(MaxPageBitmapBytes / bytes);
        return Math.Max(96f, scaled);
    }

    public static double OfficeScale(double zoom, double rasterizationScale, double widthPt, double heightPt)
    {
        double scale = (96.0 / 72.0)
            * Math.Max(1.0, rasterizationScale)
            * Math.Clamp(zoom, 0.25, 8.0)
            * TextOversample;
        double bytes = PixelBytes(widthPt, heightPt, scale);
        if (bytes > MaxPageBitmapBytes && bytes > 0)
            scale *= Math.Sqrt(MaxPageBitmapBytes / bytes);

        return Math.Clamp(scale, 1.0, 6.0);
    }

    public static double PixelBytes(double width, double height, double scale)
        => width * scale * height * scale * 4.0;
}
