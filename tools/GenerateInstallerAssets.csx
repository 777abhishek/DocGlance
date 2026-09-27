// GenerateInstallerAssets.csx
// Run with: dotnet script tools/GenerateInstallerAssets.csx
// Or:       dotnet-script tools/GenerateInstallerAssets.csx
//
// Generates:
//   installer/assets/wizard-sidebar.bmp  — 164 × 314 px (left panel on welcome page)
//   installer/assets/wizard-header.bmp   — 497 × 55  px (top banner on inner pages)

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

var root       = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
var iconPath   = Path.Combine(root, "Assets", "android-chrome-512x512.png");
var outDir     = Path.Combine(root, "installer", "assets");
Directory.CreateDirectory(outDir);

var srcIcon = Image.FromFile(iconPath);

// ─────────────────────────────────────────────────────────────────────────────
// 1. SIDEBAR  164 × 314 px
// ─────────────────────────────────────────────────────────────────────────────
{
    int W = 164, H = 314;
    using var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
    using var g   = Graphics.FromImage(bmp);
    g.SmoothingMode      = SmoothingMode.AntiAlias;
    g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
    g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;
    g.CompositingQuality = CompositingQuality.HighQuality;

    // ── background gradient: white → light blue-grey ──
    using (var grad = new LinearGradientBrush(
        new Point(0, 0), new Point(0, H),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(220, 235, 252)))
        g.FillRectangle(grad, 0, 0, W, H);

    // ── left accent bar ──
    using (var b = new SolidBrush(Color.FromArgb(0, 103, 192)))
        g.FillRectangle(b, 0, 0, 4, H);

    // ── icon card (rounded white square) ──
    int iconSize = 96, iconX = (W - iconSize) / 2, iconY = 32;
    int pad = 10, cr = 18;
    var card = new Rectangle(iconX - pad, iconY - pad, iconSize + pad * 2, iconSize + pad * 2);
    using (var path = RoundedRect(card, cr))
    {
        using (var b = new SolidBrush(Color.White))
            g.FillPath(b, path);
        // subtle shadow border
        using (var pen = new Pen(Color.FromArgb(40, 0, 90, 180), 1f))
            g.DrawPath(pen, path);
    }
    g.DrawImage(srcIcon, iconX, iconY, iconSize, iconSize);

    // ── app name ──
    int textTop = iconY + iconSize + pad + 16;
    using (var font = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(15, 15, 15)))
    {
        var sf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("QuickLook", font, b, new RectangleF(0, textTop, W, 22), sf);
    }

    // ── tagline ──
    using (var font = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(75, 75, 75)))
    {
        var sf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("Preview any file. Instantly.", font, b, new RectangleF(0, textTop + 22, W, 18), sf);
    }

    // ── divider ──
    int divY = textTop + 50;
    using (var pen = new Pen(Color.FromArgb(50, 0, 100, 210), 1f))
        g.DrawLine(pen, 16, divY, W - 16, divY);

    // ── feature bullets ──
    var bullets = new[] { "  PDF, DOCX, XLSX", "  Images & Video", "  Code & Markdown", "  ZIP & Archives" };
    using (var font = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(30, 30, 30)))
    using (var dot  = new SolidBrush(Color.FromArgb(0, 103, 192)))
    {
        int by = divY + 10;
        foreach (var line in bullets)
        {
            // blue dot
            g.FillEllipse(dot, 14, by + 4, 5, 5);
            g.DrawString(line, font, b, new RectangleF(22, by, W - 28, 18));
            by += 20;
        }
    }

    // ── version at bottom ──
    using (var font = new Font("Segoe UI", 7f, FontStyle.Regular, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(130, 130, 130)))
    {
        var sf = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString("Version 1.0.0", font, b, new RectangleF(0, H - 20, W, 16), sf);
    }

    var outPath = Path.Combine(outDir, "wizard-sidebar.bmp");
    bmp.Save(outPath, ImageFormat.Bmp);
    Console.WriteLine($"✓  Sidebar saved  → {outPath}  ({W}×{H})");
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. HEADER  497 × 55 px
// ─────────────────────────────────────────────────────────────────────────────
{
    int W = 497, H = 55;
    using var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
    using var g   = Graphics.FromImage(bmp);
    g.SmoothingMode      = SmoothingMode.AntiAlias;
    g.InterpolationMode  = InterpolationMode.HighQualityBicubic;
    g.TextRenderingHint  = TextRenderingHint.ClearTypeGridFit;
    g.CompositingQuality = CompositingQuality.HighQuality;

    // ── white background ──
    g.Clear(Color.White);

    // ── blue bottom border ──
    using (var b = new SolidBrush(Color.FromArgb(0, 103, 192)))
        g.FillRectangle(b, 0, H - 2, W, 2);

    // ── app icon (small, left) ──
    int sz = 36, ix = 12, iy = (H - sz) / 2;
    // small rounded card
    var card = new Rectangle(ix - 3, iy - 3, sz + 6, sz + 6);
    using (var path = RoundedRect(card, 8))
    {
        using (var b = new SolidBrush(Color.FromArgb(240, 246, 255)))
            g.FillPath(b, path);
    }
    g.DrawImage(srcIcon, ix, iy, sz, sz);

    // ── app name ──
    int tx = ix + sz + 10;
    using (var font = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(15, 15, 15)))
        g.DrawString("QuickLook", font, b, tx, 7);

    // ── subtitle ──
    using (var font = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point))
    using (var b    = new SolidBrush(Color.FromArgb(90, 90, 90)))
        g.DrawString("Fast universal file previewer for Windows", font, b, tx + 1, 30);

    var outPath = Path.Combine(outDir, "wizard-header.bmp");
    bmp.Save(outPath, ImageFormat.Bmp);
    Console.WriteLine($"✓  Header  saved  → {outPath}  ({W}×{H})");
}

srcIcon.Dispose();
Console.WriteLine("Done.");

// ── helper: rounded rectangle GraphicsPath ──
static GraphicsPath RoundedRect(Rectangle r, int radius)
{
    var path = new GraphicsPath();
    int d = radius * 2;
    path.AddArc(r.X,              r.Y,              d, d, 180, 90);
    path.AddArc(r.Right - d,      r.Y,              d, d, 270, 90);
    path.AddArc(r.Right - d,      r.Bottom - d,     d, d,   0, 90);
    path.AddArc(r.X,              r.Bottom - d,     d, d,  90, 90);
    path.CloseFigure();
    return path;
}
