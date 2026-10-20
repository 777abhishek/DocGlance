using DocGlance.Core.Enums;
using System;
using System.Collections.Generic;
using System.IO;

namespace DocGlance.Common.Utilities;

/// <summary>
/// Detects file types and categories based on extension and content.
/// Replaces the legacy FileTypeHelper for clearer naming and expanded functionality.
/// </summary>
public static class FileTypeDetector
{
    /// <summary>
    /// Determines the file category based on extension.
    /// </summary>
    public static FileCategory GetCategory(string? extension)
    {
        return extension?.TrimStart('.').ToUpperInvariant() switch
        {
            "PDF" or "DOCX" or "DOC" or "PPTX" or "PPT" or "XLSX" or "XLS" => FileCategory.Document,
            "TXT" or "MD" or "MARKDOWN" or "MDX" or "MMD" or "RTF" or "LOG" or "CSV" => FileCategory.Text,
            "PNG" or "JPG" or "JPEG" or "GIF" or "BMP" or "WEBP" or "TIFF" or
                "SVG" or "SVGZ" or "DDS" or "HDP" or "ICO" or "JXR" or "WDP" => FileCategory.Image,
            "MP4" or "MKV"  or "AVI" or "MOV"  or "WMV" or "WEBM" or "FLV"  => FileCategory.Video,
            "MP3" or "WAV"  or "FLAC" or "AAC" or "OGG" or "M4A"            => FileCategory.Audio,
            "CS"  or "CPP"  or "C"   or "H"    or "PY"  or "JS"  or "TS"
                or "HTML" or "CSS" or "JSON" or "XML" or "JAVA" or "GO" or "RS" => FileCategory.Code,
            "ZIP" or "RAR"  or "7Z"  or "TAR"  or "GZ"  or "BZ2"            => FileCategory.Archive,
            "EXE" or "MSI"  or "DMG" or "APP"                               => FileCategory.Executable,
            _ => FileCategory.Unknown,
        };
    }

    /// <summary>
    /// Determines file category from file path.
    /// </summary>
    public static FileCategory GetCategoryFromPath(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return FileCategory.Unknown;

        return GetCategory(Path.GetExtension(filePath));
    }

    /// <summary>
    /// Returns the Segoe Fluent Icons glyph for a file extension.
    /// </summary>
    public static string GetIconGlyph(string? extension) =>
        extension?.TrimStart('.').ToUpperInvariant() switch
        {
            "PDF"  => "\uF3AD",  // DocumentPdf
            "DOCX" or "DOC"  => "\uF0C22",  // Page
            "PPTX" or "PPT"  => "\uF12B",  // PPT
            "XLSX" or "XLS" or "CSV" => "\uE54C",  // ExcelDocument
            "PNG" or "JPG" or "JPEG" or "GIF" or "BMP" or "WEBP" or "SVG" or
                "SVGZ" or "DDS" or "HDP" or "ICO" or "JXR" or "WDP" => "\uF489",  // Photo2
            "MP4"  or "MKV" or "AVI" or "MOV" or "WMV" => "\uE8B2",  // Video
            "MP3"  or "WAV" or "FLAC" => "\uE8D6",  // MusicNote
            "ZIP"  or "RAR" or "7Z"   => "\uE8B1",  // ZipFolder
            "CS"   or "PY"  or "JS"  or "TS" or "HTML" or "CSS" => "\uE943",  // Code
            "TXT"  or "LOG" => "\uE8A5",  // Document
            "MD"   => "\uEA4E",  // MarkDownLanguage
            _ => "\uE8A5",  // Generic document
        };

    /// <summary>
    /// Checks if file extension indicates a supported document type.
    /// </summary>
    public static bool IsSupportedDocument(string? extension)
    {
        var category = GetCategory(extension);
        return category is FileCategory.Document or FileCategory.Text or FileCategory.Image or FileCategory.Code;
    }

    /// <summary>
    /// Gets a human-readable description of the file type.
    /// </summary>
    public static string GetTypeDescription(string? extension)
    {
        return extension?.TrimStart('.').ToUpperInvariant() switch
        {
            "PDF" => "PDF Document",
            "DOCX" or "DOC" => "Word Document",
            "PPTX" or "PPT" => "PowerPoint Presentation",
            "XLSX" or "XLS" => "Excel Spreadsheet",
            "CSV" => "CSV File",
            "TXT" => "Text File",
            "MD" => "Markdown File",
            "RTF" => "Rich Text File",
            "LOG" => "Log File",
            "PNG" => "PNG Image",
            "JPG" or "JPEG" => "JPEG Image",
            "GIF" => "GIF Image",
            "BMP" => "Bitmap Image",
            "WEBP" => "WebP Image",
            "TIFF" => "TIFF Image",
            "SVG" => "SVG Vector Image",
            "MP4" => "MP4 Video",
            "MKV" => "MKV Video",
            "AVI" => "AVI Video",
            "MOV" => "QuickTime Video",
            "WMV" => "Windows Media Video",
            "MP3" => "MP3 Audio",
            "WAV" => "WAV Audio",
            "FLAC" => "FLAC Audio",
            "ZIP" => "ZIP Archive",
            "RAR" => "RAR Archive",
            "7Z" => "7-Zip Archive",
            "CS" => "C# Source File",
            "PY" => "Python Script",
            "JS" => "JavaScript File",
            "TS" => "TypeScript File",
            "HTML" => "HTML File",
            "CSS" => "CSS Stylesheet",
            "JSON" => "JSON File",
            "XML" => "XML File",
            _ => extension != null ? $"{extension.ToUpperInvariant()} File" : "File",
        };
    }

    /// <summary>
    /// Mime type mappings for common file extensions.
    /// </summary>
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".doc"] = "application/msword",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".xls"] = "application/vnd.ms-excel",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
        [".csv"] = "text/csv",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".html"] = "text/html",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".svgz"] = "image/svg+xml",
        [".jxr"] = "image/vnd.ms-photo",
        [".wdp"] = "image/vnd.ms-photo",
        [".hdp"] = "image/vnd.ms-photo",
        [".mp4"] = "video/mp4",
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".zip"] = "application/zip",
    };

    /// <summary>
    /// Gets the MIME type for a file extension.
    /// </summary>
    public static string GetMimeType(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
            return "application/octet-stream";

        if (!extension.StartsWith("."))
            extension = "." + extension;

        return MimeTypes.TryGetValue(extension, out var mimeType)
            ? mimeType
            : "application/octet-stream";
    }
}
