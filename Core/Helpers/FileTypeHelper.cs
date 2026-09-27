using QuickView.Core.Enums;
using System;

namespace QuickView.Core.Helpers;

public static class FileTypeHelper
{
    public static FileCategory GetCategory(string? extension)
    {
        return extension?.TrimStart('.').ToUpperInvariant() switch
        {
            "PDF" or "DOCX" or "DOC" or "PPTX" or "PPT" or "XLSX" or "XLS" => FileCategory.Document,
            "TXT" or "MD"   or "RTF" or "LOG"  or "CSV"                     => FileCategory.Text,
            "PNG" or "JPG"  or "JPEG" or "GIF" or "BMP" or "WEBP" or "TIFF" => FileCategory.Image,
            "MP4" or "MKV"  or "AVI" or "MOV"  or "WMV" or "WEBM"           => FileCategory.Video,
            "MP3" or "WAV"  or "FLAC" or "AAC" or "OGG"                     => FileCategory.Audio,
            "CS"  or "CPP"  or "C"   or "H"    or "PY"  or "JS"  or "TS"
                or "HTML" or "CSS" or "JSON" or "XML"                        => FileCategory.Code,
            "ZIP" or "RAR"  or "7Z"  or "TAR"  or "GZ"                      => FileCategory.Archive,
            "EXE" or "MSI"                                                   => FileCategory.Executable,
            _ => FileCategory.Unknown,
        };
    }

    /// <summary>Returns the Segoe Fluent Icons glyph for a file extension.</summary>
    public static string GetGlyph(string? extension) =>
        extension?.TrimStart('.').ToUpperInvariant() switch
        {
            "PDF"  => "\uEA90",
            "DOCX" or "DOC"  => "\uE8A5",
            "PPTX" or "PPT"  => "\uE8D1",
            "XLSX" or "XLS" or "CSV" => "\uE8F9",
            "PNG"  or "JPG" or "JPEG" or "GIF" or "BMP" or "WEBP" => "\uEB9F",
            "MP4"  or "MKV" or "AVI" or "MOV" or "WMV" => "\uE8B2",
            "MP3"  or "WAV" or "FLAC" => "\uE8D6",
            "ZIP"  or "RAR" or "7Z"   => "\uE8B1",
            "CS"   or "PY"  or "JS"  or "TS" => "\uE943",
            _ => "\uE8A5",
        };
}
