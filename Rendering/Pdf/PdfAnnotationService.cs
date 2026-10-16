using DocGlance.Core.Models;
using DocGlance.Interop.PdfCraft;
using DocGlance.Rendering.Pdf.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Rendering.Pdf;

public enum PdfTextAnnotationKind : uint
{
    Highlight,
    Underline,
    Strikeout,
    Note,
    Ink,
    FreeText,
    Image,
}

public sealed record PdfTextAnnotation(
    int PageIndex,
    PdfTextAnnotationKind Kind,
    string Contents,
    IReadOnlyList<PdfSearchRect> Rectangles,
    uint ColorArgb,
    float Opacity,
    float Width,
    IReadOnlyList<(double X, double Y)> Points);

/// <summary>Creates and removes persistent PDF annotations through PdfCraft.</summary>
public static class PdfAnnotationService
{
    private const int MaximumContentsBytes = 1_048_576;

    public static Task<IReadOnlyList<OutlineItem>> GetOutlineAsync(
        PdfDocumentState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ManagedPdfDocument != null)
            return Task.FromResult<IReadOnlyList<OutlineItem>>(Array.Empty<OutlineItem>());

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = GetDocumentBuffer(state, PdfCraftNative.pc_get_document_outline);
            return ParseOutline(bytes);
        }, cancellationToken);
    }

    public static Task<IReadOnlyList<PdfTextAnnotation>> GetAnnotationsAsync(
        PdfDocumentState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ManagedPdfDocument != null)
            return Task.FromResult<IReadOnlyList<PdfTextAnnotation>>(Array.Empty<PdfTextAnnotation>());

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] bytes = GetDocumentBuffer(state, PdfCraftNative.pc_get_text_annotations);
            return ParseAnnotations(bytes, state.PageCount);
        }, cancellationToken);
    }

    public static Task<byte[]> AddAsync(
        PdfDocumentState state,
        int pageIndex,
        PdfTextAnnotationKind kind,
        IReadOnlyList<PdfSearchRect> rectangles,
        string contents,
        uint colorArgb = 0xFFFFDC28,
        float opacity = 0.35f,
        CancellationToken cancellationToken = default)
    {
        Validate(state, pageIndex, rectangles);
        if ((kind is PdfTextAnnotationKind.Note or PdfTextAnnotationKind.FreeText) &&
            string.IsNullOrWhiteSpace(contents))
            throw new ArgumentException("An annotation cannot be empty.", nameof(contents));
        if (!float.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));

        var rectValues = ToFloatArray(rectangles);
        var colorValues = new[]
        {
            ((colorArgb >> 16) & 0xff) / 255f,
            ((colorArgb >> 8) & 0xff) / 255f,
            (colorArgb & 0xff) / 255f,
        };
        var contentBytes = Encoding.UTF8.GetBytes(contents);
        if (contentBytes.Length > MaximumContentsBytes)
            throw new ArgumentException("Annotation text cannot exceed 1 MB.", nameof(contents));
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));

            try
            {
                return InvokeWithBuffers(rectValues, contentBytes, colorValues, (rectPointer, contentPointer, colorPointer) =>
                {
                    var buffer = PdfCraftNative.pc_add_text_annotation(
                        handle, (uint)pageIndex, (uint)kind,
                        rectPointer, (nuint)rectValues.Length,
                        contentPointer, (nuint)contentBytes.Length, colorPointer, opacity);
                    return CopyAndFree(buffer, "PdfCraft could not add the PDF annotation.");
                });
            }
            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    public static Task<int> CountRemovableAsync(
        PdfDocumentState state,
        int pageIndex,
        IReadOnlyList<PdfSearchRect> rectangles,
        CancellationToken cancellationToken = default)
    {
        Validate(state, pageIndex, rectangles);
        var rectValues = ToFloatArray(rectangles);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));

            try
            {
                return InvokeWithBuffers(rectValues, Array.Empty<byte>(), (rectPointer, _) =>
                {
                    uint count = PdfCraftNative.pc_count_removable_annotations(
                        handle, (uint)pageIndex, rectPointer, (nuint)rectValues.Length);
                    if (count == uint.MaxValue)
                        throw new InvalidOperationException("PdfCraft could not inspect annotations on this page.");
                    if (count > int.MaxValue)
                        throw new InvalidOperationException("The page contains too many annotations.");
                    return (int)count;
                });
            }

            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    public static Task<byte[]> AddInkAsync(
        PdfDocumentState state,
        int pageIndex,
        IReadOnlyList<(double X, double Y)> points,
        uint colorArgb,
        float opacity,
        float width,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(points);
        if ((uint)pageIndex >= (uint)state.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (state.ManagedPdfDocument != null)
            throw new NotSupportedException("PDF annotations are unavailable for password-protected PDFs.");
        if (points.Count is < 2 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(points));
        if (!float.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(opacity));
        if (!float.IsFinite(width) || width is < 0.25f or > 72f)
            throw new ArgumentOutOfRangeException(nameof(width));

        var pointValues = new float[checked(points.Count * 2)];
        for (int i = 0; i < points.Count; i++)
        {
            if (!double.IsFinite(points[i].X) || !double.IsFinite(points[i].Y))
                throw new ArgumentException("Ink coordinates must be finite.", nameof(points));
            pointValues[i * 2] = checked((float)points[i].X);
            pointValues[i * 2 + 1] = checked((float)points[i].Y);
        }
        var colorValues = new[]
        {
            ((colorArgb >> 16) & 0xff) / 255f,
            ((colorArgb >> 8) & 0xff) / 255f,
            (colorArgb & 0xff) / 255f,
        };

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));
            try
            {
                return InvokeWithBuffers(pointValues, Array.Empty<byte>(), colorValues,
                    (pointPointer, _, colorPointer) =>
                    {
                        var buffer = PdfCraftNative.pc_add_ink_annotation(
                            handle, (uint)pageIndex, pointPointer, (nuint)pointValues.Length,
                            colorPointer, opacity, width);
                        return CopyAndFree(buffer, "PdfCraft could not add the ink annotation.");
                    });
            }
            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    public static Task<byte[]> AddImageAsync(
        PdfDocumentState state,
        int pageIndex,
        PdfSearchRect placement,
        byte[] jpegBytes,
        uint pixelWidth,
        uint pixelHeight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(jpegBytes);
        Validate(state, pageIndex, new[] { placement });
        if (jpegBytes.Length is 0 or > 20 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(jpegBytes));
        if (pixelWidth is 0 or > 8192 || pixelHeight is 0 or > 8192 ||
            (ulong)pixelWidth * pixelHeight > 50_000_000)
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));

        var rectValues = ToFloatArray(new[] { placement });
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));
            try
            {
                return InvokeWithBuffers(rectValues, jpegBytes, Array.Empty<float>(),
                    (rectPointer, jpegPointer, _) =>
                    {
                        var buffer = PdfCraftNative.pc_add_image_annotation(
                            handle, (uint)pageIndex, rectPointer, (nuint)rectValues.Length,
                            jpegPointer, (nuint)jpegBytes.Length, pixelWidth, pixelHeight);
                        return CopyAndFree(buffer, "PdfCraft could not add the image annotation.");
                    });
            }
            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    public static Task<byte[]> DeleteAsync(
        PdfDocumentState state,
        int pageIndex,
        IReadOnlyList<PdfSearchRect> rectangles,
        CancellationToken cancellationToken = default)
    {
        Validate(state, pageIndex, rectangles);
        var rectValues = ToFloatArray(rectangles);
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (handle, lease) = state.BeginNativeCall();
            if (lease == null)
                throw new ObjectDisposedException(nameof(PdfDocumentState));

            try
            {
                return InvokeWithBuffers(rectValues, Array.Empty<byte>(), (rectPointer, _) =>
                {
                    var buffer = PdfCraftNative.pc_delete_text_annotations(
                        handle, (uint)pageIndex, rectPointer, (nuint)rectValues.Length);
                    return CopyAndFree(buffer, "PdfCraft could not remove the PDF annotation.");
                });
            }
            finally
            {
                lease.Dispose();
            }
        }, cancellationToken);
    }

    private static void Validate(
        PdfDocumentState state,
        int pageIndex,
        IReadOnlyList<PdfSearchRect> rectangles)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rectangles);
        if ((uint)pageIndex >= (uint)state.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        if (state.ManagedPdfDocument != null)
            throw new NotSupportedException("PDF annotations are unavailable for password-protected PDFs.");
        if (rectangles.Count == 0 || rectangles.Count > 4096)
            throw new ArgumentOutOfRangeException(nameof(rectangles));
    }

    private static float[] ToFloatArray(IReadOnlyList<PdfSearchRect> rectangles)
    {
        var values = new float[checked(rectangles.Count * 4)];
        for (int i = 0; i < rectangles.Count; i++)
        {
            var rect = rectangles[i];
            if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) ||
                !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height))
                throw new ArgumentException("Annotation geometry must be finite.", nameof(rectangles));
            values[i * 4] = checked((float)rect.X);
            values[i * 4 + 1] = checked((float)rect.Y);
            values[i * 4 + 2] = checked((float)rect.Width);
            values[i * 4 + 3] = checked((float)rect.Height);
        }
        return values;
    }

    private static T InvokeWithBuffers<T>(
        float[] rectValues,
        byte[] contentBytes,
        Func<IntPtr, IntPtr, T> action)
        => InvokeWithBuffers(rectValues, contentBytes, Array.Empty<float>(),
            (rectPointer, contentPointer, _) => action(rectPointer, contentPointer));

    private static T InvokeWithBuffers<T>(
        float[] rectValues,
        byte[] contentBytes,
        float[] colorValues,
        Func<IntPtr, IntPtr, IntPtr, T> action)
    {
        IntPtr rectPointer = Marshal.AllocHGlobal(checked(rectValues.Length * sizeof(float)));
        IntPtr contentPointer = IntPtr.Zero;
        IntPtr colorPointer = colorValues.Length == 0
            ? IntPtr.Zero
            : Marshal.AllocHGlobal(checked(colorValues.Length * sizeof(float)));
        try
        {
            Marshal.Copy(rectValues, 0, rectPointer, rectValues.Length);
            if (colorPointer != IntPtr.Zero)
                Marshal.Copy(colorValues, 0, colorPointer, colorValues.Length);
            if (contentBytes.Length > 0)
            {
                contentPointer = Marshal.AllocHGlobal(contentBytes.Length);
                Marshal.Copy(contentBytes, 0, contentPointer, contentBytes.Length);
            }
            return action(rectPointer, contentPointer, colorPointer);
        }
        finally
        {
            if (contentPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(contentPointer);
            if (colorPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(colorPointer);
            Marshal.FreeHGlobal(rectPointer);
        }
    }

    private static byte[] CopyAndFree(PdfCraftNative.PcPdfBuffer buffer, string errorMessage)
    {
        if (buffer.Data == IntPtr.Zero || buffer.Len == 0)
            throw new InvalidOperationException(errorMessage);
        if (buffer.Len > int.MaxValue)
        {
            PdfCraftNative.pc_free_pdf_buffer(buffer);
            throw new InvalidOperationException("The updated PDF is too large to save.");
        }

        try
        {
            var bytes = new byte[(int)buffer.Len];
            Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            PdfCraftNative.pc_free_pdf_buffer(buffer);
        }
    }

    private static byte[] GetDocumentBuffer(
        PdfDocumentState state,
        Func<IntPtr, PdfCraftNative.PcPdfBuffer> nativeCall)
    {
        var (handle, lease) = state.BeginNativeCall();
        if (lease == null)
            throw new ObjectDisposedException(nameof(PdfDocumentState));

        try
        {
            return CopyAndFree(nativeCall(handle), "PdfCraft could not read document annotations.");
        }
        finally
        {
            lease.Dispose();
        }
    }

    private static IReadOnlyList<OutlineItem> ParseOutline(byte[] bytes)
    {
        if (bytes.Length == 0)
            return Array.Empty<OutlineItem>();
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        uint count = reader.ReadUInt32();
        if (count > 100_000)
            throw new InvalidDataException("PDF outline contains too many entries.");

        var roots = new List<OutlineItem>();
        var parents = new List<OutlineItem>();
        for (uint i = 0; i < count; i++)
        {
            uint level = reader.ReadUInt32();
            uint page = reader.ReadUInt32();
            uint titleLength = reader.ReadUInt32();
            if (level > 64 || page > int.MaxValue || titleLength > 1_048_576)
                throw new InvalidDataException("PDF outline entry exceeds the supported size.");
            byte[] titleBytes = reader.ReadBytes((int)titleLength);
            if (titleBytes.Length != titleLength)
                throw new EndOfStreamException("PDF outline entry is truncated.");
            var item = new OutlineItem
            {
                Level = (int)level,
                Page = (int)page,
                Title = Encoding.UTF8.GetString(titleBytes),
            };
            while (parents.Count > item.Level)
                parents.RemoveAt(parents.Count - 1);
            if (parents.Count == 0)
                roots.Add(item);
            else
                parents[^1].Children.Add(item);
            parents.Add(item);
        }

        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("PDF outline contains trailing data.");
        return roots;
    }

    private static IReadOnlyList<PdfTextAnnotation> ParseAnnotations(byte[] bytes, int pageCount)
    {
        if (bytes.Length == 0)
            return Array.Empty<PdfTextAnnotation>();
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        uint count = reader.ReadUInt32();
        if (count > 65_536)
            throw new InvalidDataException("PDF contains too many markup annotations.");

        var annotations = new List<PdfTextAnnotation>((int)count);
        for (uint i = 0; i < count; i++)
        {
            uint page = reader.ReadUInt32();
            uint kind = reader.ReadUInt32();
            uint contentsLength = reader.ReadUInt32();
            if (page >= pageCount || kind > (uint)PdfTextAnnotationKind.Image ||
                contentsLength > MaximumContentsBytes)
                throw new InvalidDataException("PDF annotation entry is invalid.");
            byte[] contents = reader.ReadBytes((int)contentsLength);
            if (contents.Length != contentsLength)
                throw new EndOfStreamException("PDF annotation contents are truncated.");

            uint rectangleCount = reader.ReadUInt32();
            if (rectangleCount > 4096)
                throw new InvalidDataException("PDF annotation has too many geometry regions.");
            var rectangles = new PdfSearchRect[(int)rectangleCount];
            for (int rect = 0; rect < rectangles.Length; rect++)
            {
                double x = reader.ReadSingle();
                double y = reader.ReadSingle();
                double width = reader.ReadSingle();
                double height = reader.ReadSingle();
                if (!double.IsFinite(x) || !double.IsFinite(y) ||
                    !double.IsFinite(width) || !double.IsFinite(height) ||
                    width <= 0 || height <= 0)
                    throw new InvalidDataException("PDF annotation geometry is invalid.");
                rectangles[rect] = new PdfSearchRect(x, y, width, height);
            }
            var color = new byte[3];
            for (int component = 0; component < color.Length; component++)
            {
                float value = reader.ReadSingle();
                if (!float.IsFinite(value) || value is < 0 or > 1)
                    throw new InvalidDataException("PDF annotation color is invalid.");
                color[component] = (byte)Math.Round(value * 255);
            }
            float opacity = reader.ReadSingle();
            if (!float.IsFinite(opacity) || opacity is < 0 or > 1)
                throw new InvalidDataException("PDF annotation opacity is invalid.");
            float annotationWidth = reader.ReadSingle();
            if (!float.IsFinite(annotationWidth) || annotationWidth < 0)
                throw new InvalidDataException("PDF annotation width is invalid.");
            uint pointCount = reader.ReadUInt32();
            if (pointCount > 8192 ||
                ((PdfTextAnnotationKind)kind == PdfTextAnnotationKind.Ink && pointCount < 2) ||
                ((PdfTextAnnotationKind)kind != PdfTextAnnotationKind.Ink && pointCount != 0))
                throw new InvalidDataException("PDF ink geometry is invalid.");
            var points = new (double X, double Y)[(int)pointCount];
            for (int point = 0; point < points.Length; point++)
            {
                double x = reader.ReadSingle();
                double y = reader.ReadSingle();
                if (!double.IsFinite(x) || !double.IsFinite(y))
                    throw new InvalidDataException("PDF ink point is invalid.");
                points[point] = (x, y);
            }
            annotations.Add(new PdfTextAnnotation(
                (int)page,
                (PdfTextAnnotationKind)kind,
                Encoding.UTF8.GetString(contents),
                rectangles,
                0xFF000000u | (uint)(color[0] << 16) | (uint)(color[1] << 8) | color[2],
                opacity,
                annotationWidth,
                points));
        }

        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("PDF annotation data contains trailing bytes.");
        return annotations;
    }
}
