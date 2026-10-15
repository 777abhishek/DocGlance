using DocGlance.Core.Models;
using System;
using System.Collections.Generic;
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
}

/// <summary>Creates and removes persistent PDF annotations through PdfCraft.</summary>
public static class PdfAnnotationService
{
    private const int MaximumContentsBytes = 1_048_576;

    public static Task<byte[]> AddAsync(
        PdfDocumentState state,
        int pageIndex,
        PdfTextAnnotationKind kind,
        IReadOnlyList<PdfSearchRect> rectangles,
        string contents,
        CancellationToken cancellationToken = default)
    {
        Validate(state, pageIndex, rectangles);
        if (kind == PdfTextAnnotationKind.Note && string.IsNullOrWhiteSpace(contents))
            throw new ArgumentException("A note cannot be empty.", nameof(contents));

        var rectValues = ToFloatArray(rectangles);
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
                return InvokeWithBuffers(rectValues, contentBytes, (rectPointer, contentPointer) =>
                {
                    var buffer = PdfCraftNative.pc_add_text_annotation(
                        handle, (uint)pageIndex, (uint)kind,
                        rectPointer, (nuint)rectValues.Length,
                        contentPointer, (nuint)contentBytes.Length);
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
    {
        IntPtr rectPointer = Marshal.AllocHGlobal(checked(rectValues.Length * sizeof(float)));
        IntPtr contentPointer = IntPtr.Zero;
        try
        {
            Marshal.Copy(rectValues, 0, rectPointer, rectValues.Length);
            if (contentBytes.Length > 0)
            {
                contentPointer = Marshal.AllocHGlobal(contentBytes.Length);
                Marshal.Copy(contentBytes, 0, contentPointer, contentBytes.Length);
            }
            return action(rectPointer, contentPointer);
        }
        finally
        {
            if (contentPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(contentPointer);
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
}
