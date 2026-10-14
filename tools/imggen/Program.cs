// tools/imggen/Program.cs
// Generates:
//   Assets/AppIcon.ico                   — multi-size icon (16,20,24,32,48,64,128,256)
//   installer/assets/wizard-sidebar.bmp  — 164×314 px
//   installer/assets/wizard-header.bmp   — 497×55  px

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

// Resolve workspace root relative to this exe's location (bin/.../net8.0-windows/imggen.exe)
var root     = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var srcPath  = Path.Combine(root, "Assets", "android-chrome-512x512.png");
var icoOut   = Path.Combine(root, "Assets", "AppIcon.ico");
var outDir   = Path.Combine(root, "installer", "assets");
Directory.CreateDirectory(outDir);

using var src = (Bitmap)Image.FromFile(srcPath);

// ─────────────────────────────────────────────────────────────────────────────
// 1.  AppIcon.ico  —  all sizes Windows ever needs
// ─────────────────────────────────────────────────────────────────────────────
//
//  Windows reads the following sizes from an .ico:
//   16  — title bar (classic / accessibility themes)
//   20  — title bar on 125% DPI
//   24  — title bar on 150% DPI
//   32  — taskbar (96 DPI), Alt+Tab
//   48  — large icon view in File Explorer
//   64  — extra-large (some UIs)
//  128  — legacy "huge" icon
//  256  — Windows shell high-DPI / 200% DPI
//
//  The ICO file format is a directory header followed by PNG/BMP payloads.
//  For sizes ≥ 256 Windows expects a compressed PNG payload inside the .ico.
//  For sizes ≤ 128 raw 32bpp BMP (BITMAPINFOHEADER + XOR mask) works fine.
{
    var sizes = new[] { 16, 20, 24, 32, 48, 64, 128, 256 };

    // Render each size from the high-quality source
    var frames = new List<(int size, byte[] data)>();
    foreach (int sz in sizes)
    {
        using var frame = new Bitmap(sz, sz, PixelFormat.Format32bppArgb);
        using var g     = Graphics.FromImage(frame);
        g.SmoothingMode      = SmoothingMode.AntiAlias;
        g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.Clear(Color.Transparent);
        g.DrawImage(src, 0, 0, sz, sz);

        byte[] payload;
        if (sz >= 256)
        {
            // Store as PNG inside the ICO — lossless + compact
            using var ms = new MemoryStream();
            frame.Save(ms, ImageFormat.Png);
            payload = ms.ToArray();
        }
        else
        {
            // Store as raw 32bpp BMP (BITMAPINFOHEADER, no file header)
            payload = BitmapToIcoPayload(frame, sz);
        }
        frames.Add((sz, payload));
    }

    WriteIco(icoOut, frames);
    Console.WriteLine($"✓  AppIcon.ico  ({frames.Count} sizes: {string.Join(", ", sizes)})  →  {icoOut}");
}

// ─────────────────────────────────────────────────────────────────────────────
// 2.  wizard-sidebar.bmp  —  164 × 314 px
// ─────────────────────────────────────────────────────────────────────────────
{
    const int W = 164, H = 314;
    using var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
    using var g   = Graphics.FromImage(bmp);
    Setup(g);

    // background gradient: white → soft blue-grey
    using (var grad = new LinearGradientBrush(
        new Point(0, 0), new Point(0, H),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(218, 232, 252)))
        g.FillRectangle(grad, 0, 0, W, H);

    // left accent bar
    using (var b = new SolidBrush(Color.FromArgb(0, 103, 192)))
        g.FillRectangle(b, 0, 0, 3, H);

    // icon card
    const int iconSize = 96;
    int iconX = (W - iconSize) / 2, iconY = 30;
    const int pad = 10, cr = 18;
    var cardRect = new Rectangle(iconX - pad, iconY - pad, iconSize + pad * 2, iconSize + pad * 2);
    using (var path = RoundedRect(cardRect, cr))
    {
        using (var b = new SolidBrush(Color.White))     g.FillPath(b, path);
        using (var p = new Pen(Color.FromArgb(35, 0, 80, 170), 1f)) g.DrawPath(p, path);
    }
    g.DrawImage(src, iconX, iconY, iconSize, iconSize);

    int textTop = iconY + iconSize + pad + 14;

    // app name
    DrawCentered(g, "DocGlance", "Segoe UI", 10.5f, FontStyle.Bold,
                 Color.FromArgb(12, 12, 12), new RectangleF(0, textTop, W, 22));

    // tagline
    DrawCentered(g, "Preview any file. Instantly.", "Segoe UI", 7.5f, FontStyle.Regular,
                 Color.FromArgb(70, 70, 70), new RectangleF(0, textTop + 22, W, 18));

    // divider
    int divY = textTop + 50;
    using (var p = new Pen(Color.FromArgb(55, 0, 100, 210), 1f))
        g.DrawLine(p, 14, divY, W - 14, divY);

    // feature bullets
    var bullets = new[] { "PDF, DOCX, XLSX", "Images & Video", "Code & Markdown", "ZIP & Archives" };
    using var bulletFont = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
    using var tb   = new SolidBrush(Color.FromArgb(25, 25, 25));
    using var dot  = new SolidBrush(Color.FromArgb(0, 103, 192));
    int by = divY + 11;
    foreach (var line in bullets)
    {
        g.FillEllipse(dot, 14, by + 4, 5, 5);
        g.DrawString(line, bulletFont, tb, new RectangleF(24, by, W - 30, 18));
        by += 21;
    }

    // version
    DrawCentered(g, "Version 1.0.0", "Segoe UI", 6.5f, FontStyle.Regular,
                 Color.FromArgb(135, 135, 135), new RectangleF(0, H - 20, W, 16));

    var outPath = Path.Combine(outDir, "wizard-sidebar.bmp");
    bmp.Save(outPath, ImageFormat.Bmp);
    Console.WriteLine($"✓  Sidebar   {W}×{H}  →  {outPath}");
}

// ─────────────────────────────────────────────────────────────────────────────
// 3.  wizard-header.bmp  —  497 × 55 px
// ─────────────────────────────────────────────────────────────────────────────
{
    const int W = 497, H = 55;
    using var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
    using var g   = Graphics.FromImage(bmp);
    Setup(g);
    g.Clear(Color.White);

    // blue bottom border
    using (var b = new SolidBrush(Color.FromArgb(0, 103, 192)))
        g.FillRectangle(b, 0, H - 2, W, 2);

    // icon (small)
    const int sz = 36;
    int ix = 12, iy = (H - sz) / 2;
    var cardRect = new Rectangle(ix - 3, iy - 3, sz + 6, sz + 6);
    using (var path = RoundedRect(cardRect, 8))
    using (var b = new SolidBrush(Color.FromArgb(235, 244, 255)))
        g.FillPath(b, path);
    g.DrawImage(src, ix, iy, sz, sz);

    int tx = ix + sz + 10;

    using (var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(12, 12, 12)))
        g.DrawString("DocGlance", font, b, tx, 7);

    using (var font = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(88, 88, 88)))
        g.DrawString("Fast universal file previewer for Windows", font, b, tx + 1f, 30f);

    var outPath = Path.Combine(outDir, "wizard-header.bmp");
    bmp.Save(outPath, ImageFormat.Bmp);
    Console.WriteLine($"✓  Header    {W}×{H}  →  {outPath}");
}

Console.WriteLine("Done.");
return;

// ── helpers ───────────────────────────────────────────────────────────────────

static void Setup(Graphics g)
{
    g.SmoothingMode      = SmoothingMode.AntiAlias;
    g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
    g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;
    g.CompositingQuality = CompositingQuality.HighQuality;
}

static void DrawCentered(Graphics g, string text, string family, float pt,
    FontStyle style, Color color, RectangleF rect)
{
    using var font = new Font(family, pt, style, GraphicsUnit.Point);
    using var b    = new SolidBrush(color);
    var sf = new StringFormat { Alignment = StringAlignment.Center };
    g.DrawString(text, font, b, rect, sf);
}

static GraphicsPath RoundedRect(Rectangle r, int radius)
{
    var path = new GraphicsPath();
    int d = radius * 2;
    path.AddArc(r.X,         r.Y,          d, d, 180, 90);
    path.AddArc(r.Right - d, r.Y,          d, d, 270, 90);
    path.AddArc(r.Right - d, r.Bottom - d, d, d,   0, 90);
    path.AddArc(r.X,         r.Bottom - d, d, d,  90, 90);
    path.CloseFigure();
    return path;
}

// Converts a 32bpp ARGB Bitmap into the raw BMP payload used inside .ico files.
// This is a BITMAPINFOHEADER + XOR pixel data (bottom-up, 32bpp) + AND mask (1bpp).
static byte[] BitmapToIcoPayload(Bitmap bmp, int size)
{
    int rowBytes  = size * 4;          // 32bpp, no padding needed (multiple of 4)
    int maskStride = ((size + 31) / 32) * 4;  // 1bpp AND mask row stride
    int totalSize = 40 + rowBytes * size + maskStride * size;
    var buf = new byte[totalSize];
    int pos = 0;

    // BITMAPINFOHEADER (40 bytes)
    Write4(buf, ref pos, 40);                      // biSize
    Write4(buf, ref pos, size);                    // biWidth
    Write4(buf, ref pos, size * 2);                // biHeight (×2 = XOR+AND combined)
    Write2(buf, ref pos, 1);                       // biPlanes
    Write2(buf, ref pos, 32);                      // biBitCount
    Write4(buf, ref pos, 0);                       // biCompression = BI_RGB
    Write4(buf, ref pos, rowBytes * size);         // biSizeImage
    Write4(buf, ref pos, 0); Write4(buf, ref pos, 0); // DPI (don't care)
    Write4(buf, ref pos, 0); Write4(buf, ref pos, 0); // clrUsed, clrImportant

    // XOR pixel data — bottom-up row order
    for (int y = size - 1; y >= 0; y--)
    {
        for (int x = 0; x < size; x++)
        {
            var c = bmp.GetPixel(x, y);
            buf[pos++] = c.B;
            buf[pos++] = c.G;
            buf[pos++] = c.R;
            buf[pos++] = c.A;
        }
    }

    // AND mask — all zeros means "use alpha channel" for 32bpp icons
    pos += maskStride * size;  // already zeroed by new byte[]

    return buf;
}

static void Write2(byte[] buf, ref int pos, int v)
{
    buf[pos++] = (byte)(v & 0xFF);
    buf[pos++] = (byte)((v >> 8) & 0xFF);
}

static void Write4(byte[] buf, ref int pos, int v)
{
    buf[pos++] = (byte)(v & 0xFF);
    buf[pos++] = (byte)((v >> 8) & 0xFF);
    buf[pos++] = (byte)((v >> 16) & 0xFF);
    buf[pos++] = (byte)((v >> 24) & 0xFF);
}

// Writes an ICO file from a list of (size, raw-payload) pairs.
static void WriteIco(string path, List<(int size, byte[] data)> frames)
{
    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var bw = new BinaryWriter(fs);

    // ICO header
    bw.Write((ushort)0);              // reserved
    bw.Write((ushort)1);              // type = ICO
    bw.Write((ushort)frames.Count);  // image count

    // Directory entries — we need to know offsets, which start after the directory
    int dirSize    = 6 + frames.Count * 16;
    int dataOffset = dirSize;

    foreach (var (sz, data) in frames)
    {
        byte w = sz >= 256 ? (byte)0 : (byte)sz;
        byte h = sz >= 256 ? (byte)0 : (byte)sz;
        bw.Write(w);                   // width  (0 = 256)
        bw.Write(h);                   // height (0 = 256)
        bw.Write((byte)0);             // color count (0 = no palette)
        bw.Write((byte)0);             // reserved
        bw.Write((ushort)1);           // planes
        bw.Write((ushort)32);          // bit count
        bw.Write((uint)data.Length);   // size of image data
        bw.Write((uint)dataOffset);    // offset to image data
        dataOffset += data.Length;
    }

    // Pixel payloads
    foreach (var (_, data) in frames)
        bw.Write(data);
}
