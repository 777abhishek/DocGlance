using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace DocGlance.Rendering.Shared;

/// <summary>
/// Copies a native RGBA8 buffer into a WriteableBitmap without a second managed pixel array.
/// Channel swap and premultiply happen in-place on the native buffer (on the caller's thread),
/// then a single copy into the bitmap's PixelBuffer.
///
/// WriteableBitmap is a XAML object and may only be created on the UI thread. Callers often run
/// on the thread pool (buffer-page renders, thumbnails), so the bitmap creation is always
/// marshalled to the dispatcher captured by <see cref="Initialize"/>; the native buffer must stay
/// alive until the returned task completes.
/// </summary>
internal static class NativeBitmap
{
    private static DispatcherQueue? _ui;

    /// <summary>Call once from the UI thread at startup.</summary>
    public static void Initialize(DispatcherQueue uiDispatcher) => _ui = uiDispatcher;

    public static Task<WriteableBitmap> FromPdfStreamAsync(IRandomAccessStream stream, int width, int height)
    {
        var ui = _ui;
        if (ui == null || ui.HasThreadAccess)
            return CreateBitmapFromStreamAsync(stream, width, height);

        var tcs = new TaskCompletionSource<WriteableBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = ui.TryEnqueue(async () =>
        {
            try { tcs.SetResult(await CreateBitmapFromStreamAsync(stream, width, height)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });

        if (!queued)
            tcs.SetException(new InvalidOperationException("The UI dispatcher is shut down."));

        return tcs.Task;
    }

    private static async Task<WriteableBitmap> CreateBitmapFromStreamAsync(
        IRandomAccessStream stream, int width, int height)
    {
        var bitmap = new WriteableBitmap(width, height);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    public static Task<WriteableBitmap> FromRgbaAsync(IntPtr pixels, uint width, uint height, nuint len)
    {
        unsafe
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

            return CopyToBitmapOnUiAsync(pixels, w, h, needed);
        }
    }

    /// <summary>Native buffer is already premultiplied BGRA8 (DeckCraft / WordCraft).</summary>
    public static Task<WriteableBitmap> FromBgraAsync(IntPtr pixels, uint width, uint height, nuint len)
    {
        unsafe
        {
            int w = (int)width;
            int h = (int)height;
            Validate(pixels, w, h, len, out long needed);
            return CopyToBitmapOnUiAsync(pixels, w, h, needed);
        }
    }

    private static unsafe byte* Validate(IntPtr pixels, int width, int height, nuint len, out long needed)
    {
        if (pixels == IntPtr.Zero || width <= 0 || height <= 0 || len == 0)
            throw new ArgumentException("Native bitmap buffer is empty.");

        needed = (long)width * height * 4;
        if (needed > int.MaxValue)
            throw new InvalidOperationException("Native bitmap is larger than 2 GB.");
        if ((long)len < needed)
            throw new InvalidOperationException("Native bitmap buffer is smaller than width × height × 4.");

        return (byte*)pixels;
    }

    private static Task<WriteableBitmap> CopyToBitmapOnUiAsync(IntPtr pixels, int width, int height, long needed)
    {
        var ui = _ui;
        if (ui == null || ui.HasThreadAccess)
            return Task.FromResult(CopyToBitmap(pixels, width, height, needed));

        var tcs = new TaskCompletionSource<WriteableBitmap>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool queued = ui.TryEnqueue(() =>
        {
            try { tcs.SetResult(CopyToBitmap(pixels, width, height, needed)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });

        if (!queued)
            tcs.SetException(new InvalidOperationException("The UI dispatcher is shut down."));

        return tcs.Task;
    }

    private static unsafe WriteableBitmap CopyToBitmap(IntPtr pixels, int width, int height, long needed)
    {
        var bitmap = new WriteableBitmap(width, height);
        using var src = new UnmanagedMemoryStream((byte*)pixels, needed);
        using var dest = bitmap.PixelBuffer.AsStream();
        src.CopyTo(dest);
        bitmap.Invalidate();
        return bitmap;
    }
}
