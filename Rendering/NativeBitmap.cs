using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;

namespace QuickView.Rendering;

/// <summary>
/// Copies a native RGBA8 buffer into a WriteableBitmap without a second managed pixel array.
/// Channel swap and premultiply happen in-place on the native buffer, then a single copy
/// into the bitmap's PixelBuffer.
/// </summary>
internal static class NativeBitmap
{
    public static unsafe WriteableBitmap FromRgba(IntPtr pixels, uint width, uint height, nuint len)
    {
        int w = (int)width;
        int h = (int)height;
        byte* p = Validate(pixels, w, h, len, out long needed);

        int n = (int)needed;
        for (int i = 0; i < n; i += 4)
        {
            byte r = p[i];
            byte g = p[i + 1];
            byte b = p[i + 2];
            byte a = p[i + 3];

            if (a == 0)
            {
                p[i] = 0;
                p[i + 1] = 0;
                p[i + 2] = 0;
                continue;
            }

            if (a != 255)
            {
                b = (byte)((b * a + 127) / 255);
                g = (byte)((g * a + 127) / 255);
                r = (byte)((r * a + 127) / 255);
            }

            p[i] = b;
            p[i + 1] = g;
            p[i + 2] = r;
        }

        return CopyToBitmap(p, w, h, needed);
    }

    /// <summary>Native buffer is already premultiplied BGRA8 (DeckCraft / WordCraft).</summary>
    public static unsafe WriteableBitmap FromBgra(IntPtr pixels, uint width, uint height, nuint len)
    {
        int w = (int)width;
        int h = (int)height;
        byte* p = Validate(pixels, w, h, len, out long needed);
        return CopyToBitmap(p, w, h, needed);
    }

    private static unsafe byte* Validate(IntPtr pixels, int width, int height, nuint len, out long needed)
    {
        if (pixels == IntPtr.Zero || width <= 0 || height <= 0 || len == 0)
            throw new ArgumentException("Native bitmap buffer is empty.");

        needed = (long)width * height * 4;
        if ((long)len < needed)
            throw new InvalidOperationException("Native bitmap buffer is smaller than width × height × 4.");

        return (byte*)pixels;
    }

    private static unsafe WriteableBitmap CopyToBitmap(byte* pixels, int width, int height, long needed)
    {
        var bitmap = new WriteableBitmap(width, height);
        using var src = new UnmanagedMemoryStream(pixels, needed);
        using var dest = bitmap.PixelBuffer.AsStream();
        src.CopyTo(dest);
        bitmap.Invalidate();
        return bitmap;
    }
}
