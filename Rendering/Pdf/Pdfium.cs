using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace QuickView.Rendering.Pdf;

/// <summary>
/// Single P/Invoke surface and lock for pdfium.dll. Every native call in this
/// process must run under <see cref="Gate"/> after <see cref="EnsureInitialized"/>.
/// SEC-04: Loads pdfium.dll from application directory to prevent DLL hijacking.
/// </summary>
internal static class Pdfium
{
    private const string Dll = "pdfium.dll";

    internal static readonly object Gate = new();
    private static int _initialized;

    // SEC-04: Static constructor to load pdfium.dll from application directory
    static Pdfium()
    {
        try
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "pdfium.dll");
            if (File.Exists(dllPath))
            {
                // Explicitly load the DLL from the application directory
                NativeLibrary.Load(dllPath);
            }
            // If the DLL doesn't exist in app directory, let it fall through to default search
        }
        catch
        {
            // Ignore errors - P/Invoke will handle loading failures with proper error messages
        }
    }

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

    // ── Text Selection APIs ───────────────────────────────────────────────────

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetCharIndexAtPos(
        IntPtr textPage, double x, double y, double xTolerance, double yTolerance);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFText_GetCharBox(
        IntPtr textPage, int index,
        out double left, out double right, out double bottom, out double top);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_DeviceToPage(
        IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate,
        int deviceX, int deviceY, out double pageX, out double pageY);

    // ── Link Detection APIs ───────────────────────────────────────────────────

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFLink_LoadWebLinks(IntPtr textPage);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFLink_CloseWebLinks(IntPtr linkPage);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFLink_CountWebLinks(IntPtr linkPage);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFLink_GetURL(
        IntPtr linkPage, int linkIndex, [Out] ushort[]? buffer, int buflen);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFLink_CountRects(IntPtr linkPage, int linkIndex);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFLink_GetRect(
        IntPtr linkPage, int linkIndex, int rectIndex,
        out double left, out double top, out double right, out double bottom);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPage_GetAnnotCount(IntPtr page);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDFPage_CloseAnnot(IntPtr annot);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_GetSubtype(IntPtr annot);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFAnnot_GetLink(IntPtr annot);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_GetRect(IntPtr annot, out FS_RECTF rect);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFLink_GetAction(IntPtr link);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFAction_GetType(IntPtr action);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFAction_GetURIPath(
        IntPtr document, IntPtr action, IntPtr buffer, uint buflen);

    // ── Additional Structures ─────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_RECTF
    {
        public float Left;
        public float Bottom;
        public float Right;
        public float Top;
    }

    // ── Annotation Subtypes ───────────────────────────────────────────────────

    public const int FPDF_ANNOT_LINK = 2;
    public const int FPDF_ACTION_GOTO = 1;
    public const int FPDF_ACTION_REMOTEGOTO = 2;
    public const int FPDF_ACTION_URI = 3;

    // ── Annotations (fpdf_annot.h) ────────────────────────────────────────────

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr FPDFPage_CreateAnnot(IntPtr page, int subtype);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFPage_RemoveAnnot(IntPtr page, int index);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_SetRect(IntPtr annot, ref FS_RECTF rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_QUADPOINTSF
    {
        public float X1, Y1;   // upper-left
        public float X2, Y2;   // upper-right
        public float X3, Y3;   // lower-left
        public float X4, Y4;   // lower-right
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_AppendAttachmentPoints(IntPtr annot, ref FS_QUADPOINTSF quadPoints);

    /// <summary>colorType: 0 = fill/stroke color, 1 = interior color.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_SetColor(IntPtr annot, int colorType, uint r, uint g, uint b, uint a);

    /// <summary>key is a narrow FPDF_BYTESTRING; value is UTF-16LE (FPDF_WIDESTRING).</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_SetStringValue(
        IntPtr annot,
        [MarshalAs(UnmanagedType.LPStr)] string key,
        [MarshalAs(UnmanagedType.LPWStr)] string value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_SetFlags(IntPtr annot, int flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_POINTF
    {
        public float X;
        public float Y;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_AddInkStroke(IntPtr annot, [In] FS_POINTF[] points, UIntPtr pointCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_SetBorder(IntPtr annot, float horizontalRadius, float verticalRadius, float borderWidth);

    // Read-back APIs

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_GetColor(IntPtr annot, int colorType, out uint r, out uint g, out uint b, out uint a);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern UIntPtr FPDFAnnot_CountAttachmentPoints(IntPtr annot);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_GetAttachmentPoints(IntPtr annot, UIntPtr quadIndex, out FS_QUADPOINTSF quadPoints);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFAnnot_GetInkListCount(IntPtr annot);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint FPDFAnnot_GetInkListPath(IntPtr annot, uint pathIndex, [Out] FS_POINTF[]? buffer, uint length);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDFAnnot_GetBorder(IntPtr annot, out float horizontalRadius, out float verticalRadius, out float borderWidth);

    public const int FPDF_ANNOT_SUBTYPE_TEXT = 1;
    public const int FPDF_ANNOT_SUBTYPE_HIGHLIGHT = 9;
    public const int FPDF_ANNOT_SUBTYPE_INK = 15;
    public const int FPDF_ANNOT_SUBTYPE_UNDERLINE = 10;
    public const int FPDF_ANNOT_SUBTYPE_STRIKEOUT = 12;
    public const int FPDF_ANNOT_SUBTYPE_STAMP = 14;

    // ── Progressive Rendering API ─────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct IFSDK_PAUSE
    {
        public int version;           // Must be 1
        public IntPtr NeedToPauseNow; // Cdecl function pointer: int(IntPtr)
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int PauseCallback(IntPtr pause);

    public const int FPDF_RENDER_READY         = 0;
    public const int FPDF_RENDER_TOBECONTINUED = 1;
    public const int FPDF_RENDER_DONE          = 2;

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_RenderPage_Start(
        IntPtr bitmap, IntPtr page,
        int start_x, int start_y, int size_x, int size_y,
        int rotate, int flags,
        ref IFSDK_PAUSE pause);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int FPDF_RenderPage_Continue(IntPtr page, ref IFSDK_PAUSE pause);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void FPDF_RenderPage_Close(IntPtr page);
}
