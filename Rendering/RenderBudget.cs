using System;

namespace DocGlance.Rendering;

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

    /// <summary>Sidebar thumbs target ~160 CSS px wide - reduced DPI for memory efficiency.</summary>
    public const float ThumbnailDpi = 72f;

    /// <summary>Office thumbnails at 0.5x scale for better quality while staying memory efficient.</summary>
    public const double ThumbnailOfficeScale = 0.5;

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

    public static bool NeedsRerenderScale(double cachedScale, double targetScale)
    {
        if (cachedScale <= 0 || targetScale <= 0)
            return true;

        double ratio = cachedScale / targetScale;
        // Reuse if within tolerance: too soft (<0.95) or wastefully large (>1.6) triggers re-render
        return ratio < 0.95 || ratio > 1.6;
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
        // Render at exactly the physical pixel scale needed for 1:1 display.
        // WordCraft/DeckCraft use pixels-per-point scale, not DPI
        // scale = 1.0 means 72 DPI (1 pixel = 1 point)
        // For 96 DPI standard screen: 96/72 = 1.333
        // For high-DPI (150% = 144 DPI): 144/72 = 2.0
        
        // Base calculation: convert display DPI to scale factor (1:1 with screen pixels)
        double s = zoom * (96.0 / 72.0) * rasterizationScale * TextOversample;
        
        // Budget is the cap only - no quantization, no minimum floor
        double bytes = PixelBytes(widthPt, heightPt, s);
        if (bytes > MaxPageBitmapBytes && bytes > 0)
            s *= Math.Sqrt(MaxPageBitmapBytes / bytes);

        return s;
    }

    public static double PixelBytes(double width, double height, double scale)
        => width * scale * height * scale * 4.0;
}
