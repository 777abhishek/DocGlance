using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DocGlance.Features.Viewer.Formats.Images;

internal sealed record LoadedImageSource(
    Microsoft.UI.Xaml.Media.ImageSource Source,
    double Width,
    double Height);

internal static class ImageSourceLoader
{
    private const long MaxDecodedPixels = 32_000_000;
    private const int MaxVectorBytes = 16 * 1024 * 1024;
    private const double DefaultVectorWidth = 1024;
    private const double DefaultVectorHeight = 768;

    public static async Task<LoadedImageSource> LoadAsync(
        string filePath,
        int? decodeWidth,
        CancellationToken cancellationToken)
    {
        string extension = Path.GetExtension(filePath);
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".svgz", StringComparison.OrdinalIgnoreCase))
        {
            return await LoadVectorAsync(filePath, extension, cancellationToken);
        }

        StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        using IRandomAccessStream stream = await file.OpenReadAsync();
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();

        double sourceWidth = decoder.PixelWidth;
        double sourceHeight = decoder.PixelHeight;
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new InvalidOperationException("The image has invalid dimensions.");

        double decodeScale = Math.Min(1, Math.Sqrt(MaxDecodedPixels / (sourceWidth * sourceHeight)));
        if (decodeWidth is int targetWidth && targetWidth > 0)
            decodeScale = Math.Min(decodeScale, targetWidth / sourceWidth);

        int width = Math.Max(1, (int)Math.Floor(sourceWidth * decodeScale));
        int height = Math.Max(1, (int)Math.Floor(sourceHeight * decodeScale));
        stream.Seek(0);

        var bitmap = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Physical,
            DecodePixelWidth = width,
            DecodePixelHeight = height,
        };
        await bitmap.SetSourceAsync(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return new LoadedImageSource(bitmap, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private static async Task<LoadedImageSource> LoadVectorAsync(
        string filePath,
        string extension,
        CancellationToken cancellationToken)
    {
        var vector = new SvgImageSource();
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var info = new FileInfo(filePath);
            if (info.Length > MaxVectorBytes)
                throw new InvalidOperationException("SVG files larger than 16 MB are not supported.");

            StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
            using IRandomAccessStream stream = await file.OpenReadAsync();
            await vector.SetSourceAsync(stream);
        }
        else
        {
            byte[] svgBytes = await DecompressSvgAsync(filePath, cancellationToken);
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(svgBytes));
            stream.Seek(0);
            await vector.SetSourceAsync(stream);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new LoadedImageSource(vector, DefaultVectorWidth, DefaultVectorHeight);
    }

    private static async Task<byte[]> DecompressSvgAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await gzip.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > MaxVectorBytes)
                throw new InvalidOperationException("Decompressed SVGZ content exceeds 16 MB.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }
}
