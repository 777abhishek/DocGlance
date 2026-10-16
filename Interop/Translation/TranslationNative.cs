using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace DocGlance.Interop.Translation;

internal static partial class TranslationNative
{
    private const string Library = "translation_ffi.dll";

    public static string DetectLanguage(string text)
    {
        byte[] input = Encoding.UTF8.GetBytes(text);
        IntPtr output = IntPtr.Zero;
        nuint outputLength = 0;
        IntPtr error = IntPtr.Zero;
        nuint errorLength = 0;
        try
        {
            int result = Detect(
                input, (nuint)input.Length,
                out output, out outputLength,
                out error, out errorLength);
            if (result != 0)
                throw new InvalidOperationException(ReadUtf8(error, errorLength) ??
                    "The local language detector failed.");
            return ReadUtf8(output, outputLength) ??
                throw new InvalidOperationException("The local language detector returned no result.");
        }
        catch (DllNotFoundException ex)
        {
            throw MissingEngine(ex);
        }
        finally
        {
            Free(output, outputLength);
            Free(error, errorLength);
        }
    }

    public static string Translate(string text, string source, string target, string modelRoot)
    {
        byte[] input = Encoding.UTF8.GetBytes(text);
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
        byte[] targetBytes = Encoding.UTF8.GetBytes(target);
        byte[] rootBytes = Encoding.UTF8.GetBytes(modelRoot);
        IntPtr output = IntPtr.Zero;
        nuint outputLength = 0;
        IntPtr error = IntPtr.Zero;
        nuint errorLength = 0;
        try
        {
            int result = TranslateNative(
                input, (nuint)input.Length,
                sourceBytes, (nuint)sourceBytes.Length,
                targetBytes, (nuint)targetBytes.Length,
                rootBytes, (nuint)rootBytes.Length,
                out output, out outputLength,
                out error, out errorLength);
            if (result != 0)
                throw new InvalidOperationException(ReadUtf8(error, errorLength) ??
                    "The local translation engine failed.");
            return ReadUtf8(output, outputLength) ??
                throw new InvalidOperationException("The local translation engine returned no result.");
        }
        catch (DllNotFoundException ex)
        {
            throw MissingEngine(ex);
        }
        finally
        {
            Free(output, outputLength);
            Free(error, errorLength);
        }
    }

    public static byte[] VerifyAndDecompress(byte[] compressed, string expectedSha256)
    {
        byte[] hash = Encoding.ASCII.GetBytes(expectedSha256);
        IntPtr output = IntPtr.Zero;
        nuint outputLength = 0;
        IntPtr error = IntPtr.Zero;
        nuint errorLength = 0;
        try
        {
            int result = VerifyDecompress(
                compressed, (nuint)compressed.Length,
                hash, (nuint)hash.Length,
                out output, out outputLength,
                out error, out errorLength);
            if (result != 0)
                throw new InvalidOperationException(ReadUtf8(error, errorLength) ??
                    "The downloaded model file failed integrity verification.");

            var bytes = new byte[checked((int)outputLength)];
            Marshal.Copy(output, bytes, 0, bytes.Length);
            return bytes;
        }
        catch (DllNotFoundException ex)
        {
            throw MissingEngine(ex);
        }
        finally
        {
            Free(output, outputLength);
            Free(error, errorLength);
        }
    }

    public static void ReleaseCachedPair(string modelRoot, string pair)
    {
        byte[] root = Encoding.UTF8.GetBytes(modelRoot);
        byte[] pairBytes = Encoding.UTF8.GetBytes(pair);
        IntPtr error = IntPtr.Zero;
        nuint errorLength = 0;
        try
        {
            int result = ReleaseCachedModel(
                root, (nuint)root.Length,
                pairBytes, (nuint)pairBytes.Length,
                out error, out errorLength);
            if (result != 0)
                throw new InvalidOperationException(ReadUtf8(error, errorLength) ??
                    "The loaded translation model could not be released.");
        }
        catch (DllNotFoundException ex)
        {
            throw MissingEngine(ex);
        }
        finally
        {
            Free(error, errorLength);
        }
    }

    private static string? ReadUtf8(IntPtr buffer, nuint length)
    {
        if (buffer == IntPtr.Zero)
            return null;
        return Marshal.PtrToStringUTF8(buffer, checked((int)length));
    }

    private static void Free(IntPtr buffer, nuint length)
    {
        if (buffer != IntPtr.Zero)
            FreeBuffer(buffer, length);
    }

    private static InvalidOperationException MissingEngine(DllNotFoundException inner) =>
        new("The offline translation engine is not installed. Build the native libraries with native\\build-native.ps1, then restart DocGlance.", inner);

    [LibraryImport(Library, EntryPoint = "qv_detect_language")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int Detect(
        [In] byte[] text, nuint textLength,
        out IntPtr output, out nuint outputLength,
        out IntPtr error, out nuint errorLength);

    [LibraryImport(Library, EntryPoint = "qv_translate")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int TranslateNative(
        [In] byte[] text, nuint textLength,
        [In] byte[] source, nuint sourceLength,
        [In] byte[] target, nuint targetLength,
        [In] byte[] modelRoot, nuint modelRootLength,
        out IntPtr output, out nuint outputLength,
        out IntPtr error, out nuint errorLength);

    [LibraryImport(Library, EntryPoint = "qv_verify_decompress")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int VerifyDecompress(
        [In] byte[] compressed, nuint compressedLength,
        [In] byte[] expectedHash, nuint expectedHashLength,
        out IntPtr output, out nuint outputLength,
        out IntPtr error, out nuint errorLength);

    [LibraryImport(Library, EntryPoint = "qv_release_cached_model")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial int ReleaseCachedModel(
        [In] byte[] modelRoot, nuint modelRootLength,
        [In] byte[] pair, nuint pairLength,
        out IntPtr error, out nuint errorLength);

    [LibraryImport(Library, EntryPoint = "qv_free_buffer")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static partial void FreeBuffer(IntPtr buffer, nuint length);
}
