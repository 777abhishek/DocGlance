namespace QuickView.Core.Enums;

public enum FileCategory
{
    Unknown,
    Document,   // PDF, DOCX, PPTX, XLSX
    Text,       // TXT, MD, RTF, LOG
    Image,      // PNG, JPG, GIF, BMP, WEBP
    Video,      // MP4, MKV, AVI, MOV
    Audio,      // MP3, WAV, FLAC
    Code,       // CS, JS, TS, PY, HTML, CSS, JSON, XML
    Archive,    // ZIP, RAR, 7Z, TAR, GZ
    Executable, // EXE, MSI
}
