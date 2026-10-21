using System;
using Windows.UI;

namespace DocGlance.UI.Controls;

/// <summary>Single source of truth for file-type colour, label and description (shared by both V2 cards).</summary>
public readonly record struct FileTypeStyle(string Ext, string Description, Color Start, Color End);

public static class FileTypePalette
{
    public static string NormalizeExt(string? e) =>
        string.IsNullOrWhiteSpace(e) ? string.Empty : e.Trim().TrimStart('.').ToUpperInvariant();

    public static FileTypeStyle Get(string? fileType)
    {
        string ext = NormalizeExt(fileType);
        static Color C(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

        return ext switch
        {
            "DOC" or "DOCX"                 => new(ext, "Word Document",     C(15, 61, 145),  C(47, 111, 228)),
            "PDF"                           => new(ext, "PDF Document",      C(166, 24, 47),  C(226, 51, 80)),
            "XLS" or "XLSX" or "CSV"        => new(ext, "Excel Spreadsheet", C(15, 107, 61),  C(47, 174, 110)),
            "PPT" or "PPTX"                 => new(ext, "PowerPoint",        C(184, 70, 10),  C(240, 129, 42)),
            "JPG" or "JPEG" or "PNG" or "GIF" or "BMP" or "WEBP" or "SVG"
                                            => new(ext, "Image",             C(91, 42, 158),  C(155, 79, 224)),
            "MP4" or "AVI" or "MKV" or "MOV" or "WMV" or "WEBM"
                                            => new(ext, "Video",             C(61, 31, 140),  C(122, 79, 224)),
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ"
                                            => new(ext, "Archive",           C(11, 95, 114),  C(34, 166, 194)),
            "CS" or "CPP" or "C" or "H" or "PY" or "JS" or "TS" or "HTML" or "CSS" or "JSON" or "XML"
                                            => new(ext, "Code File",         C(13, 77, 130),  C(36, 150, 214)),
            "TXT" or "MD" or "LOG"          => new(ext, "Text Document",     C(51, 64, 79),   C(92, 112, 134)),
            "MP3" or "WAV" or "FLAC" or "AAC" or "M4A"
                                            => new(ext, "Audio",             C(140, 42, 42),  C(210, 96, 74)),
            _                               => new(string.IsNullOrEmpty(ext) ? "FILE" : ext, "Document", C(46, 54, 72), C(74, 85, 104)),
        };
    }
}
