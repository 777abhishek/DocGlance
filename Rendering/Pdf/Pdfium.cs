using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Single P/Invoke surface and lock for pdfium.dll. Every native call in this
/// process must run under <see cref="Gate"/> after <see cref="EnsureInitialized"/>.
/// </summary>
internal static class Pdfium
{
    private const string Dll = "pdfium";

    internal static readonly object Gate = new();
    private static int _initialized;

    public const int ErrSuccess  = 0;
    public const int ErrUnknown  = 1;
    public const int ErrFile     = 2;
    public const int ErrFormat   = 3;
    public const int ErrPassword = 4;
    public const int ErrSecurity = 5;
    public const int ErrPage     = 6;

    public const int MatchCase      = 1;
    public const int MatchWholeWord = 2;

    public const int BitmapBgra = 4;
    public const int RenderAnnot   = 1;
    public const int RenderLcdText = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_SIZEF
    {
        public float Width;
        public float Height;
    }

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 0)
            FPDF_InitLibrary();
    }

    public static string DescribeLastError()
    {
        return FPDF_GetLastError() switch
        {
            ErrPassword => "This PDF is password protected.",
            ErrFile     => "Cannot read the PDF file.",
            ErrFormat   => "The file is not a valid PDF.",
            ErrSecurity => "This PDF cannot be opened due to security settings.",
            ErrPage     => "The PDF page data is invalid.",
            _           => "Cannot open PDF with PDFium.",
        };
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_InitLibrary();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDF_GetLastError();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDF_LoadDocument(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filePath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string? password);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int pageIndex, out FS_SIZEF size);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_ClosePage(IntPtr page);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern float FPDF_GetPageWidthF(IntPtr page);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_PageToDevice(
        IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate,
        double pageX, double pageY, out int deviceX, out int deviceY);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFText_LoadPage(IntPtr page);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFText_ClosePage(IntPtr textPage);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_CountChars(IntPtr textPage);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, [Out] ushort[] result);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    public static extern IntPtr FPDFText_FindStart(
        IntPtr textPage, string findWhat, uint flags, int startIndex);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_FindNext(IntPtr search);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFText_FindClose(IntPtr search);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetSchResultIndex(IntPtr search);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetSchCount(IntPtr search);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_CountRects(IntPtr textPage, int start, int count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFText_GetRect(
        IntPtr textPage, int rectIndex,
        out double left, out double top, out double right, out double bottom);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, IntPtr buffer, uint buflen);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFBookmark_GetAction(IntPtr bookmark);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFAction_GetDest(IntPtr document, IntPtr action);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);
}
