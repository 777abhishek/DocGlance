// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuickView.Core.Extensions;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace QuickView.Core.Models;

public class DocumentItem : INotifyPropertyChanged
{
    public string   Title         { get; set; } = string.Empty;
    public string   FilePath      { get; set; } = string.Empty;
    public string   FileType      { get; set; } = string.Empty;
    public string   FileSize      { get; set; } = string.Empty;
    public long     FileSizeBytes { get; set; }
    public string   LastModified  { get; set; } = string.Empty;
    public DateTime LastWriteTime { get; set; }
    public string   FolderPath    { get; set; } = string.Empty;
    
    // Thumbnail preview image with change notification
    private BitmapImage? _thumbnail;
    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }
    
    public bool HasThumbnail => Thumbnail != null;

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Segoe MDL2 glyph per file category
    public string FileGlyph => FileType switch
    {
        "PDF"                                           => "\uEA90",
        "DOCX" or "DOC"                                 => "\uE8A5",
        "PPTX" or "PPT"                                 => "\uE8D1",
        "XLSX" or "XLS" or "CSV"                        => "\uE8F9",
        "TXT"  or "MD"  or "LOG" or "RTF"               => "\uE8A5",
        "PNG"  or "JPG" or "JPEG" or "BMP"
            or "GIF"   or "WEBP" or "TIFF"              => "\uEB9F",
        "MP4"  or "MKV" or "AVI" or "MOV" or "WMV"     => "\uE8B2",
        "MP3"  or "WAV" or "FLAC" or "AAC" or "OGG"    => "\uE8D6",
        "ZIP"  or "RAR" or "7Z"  or "TAR" or "GZ"      => "\uE8B1",
        "EXE"  or "MSI"                                 => "\uE756",
        "CS"   or "CPP" or "PY"  or "JS"  or "TS"
            or "JSON"  or "XML"  or "HTML" or "CSS"     => "\uE943",
        _                                               => "\uE8A5",
    };

    // Colour brush per category — cached to avoid reallocating on every binding pass
    private SolidColorBrush? _typeColor;
    public SolidColorBrush TypeColor
    {
        get
        {
            if (_typeColor != null) return _typeColor;
            
            var c = FileType switch
            {
                "PDF"                                       => Color.FromArgb(255, 220,  53,  69),
                "DOCX" or "DOC"                             => Color.FromArgb(255,  37, 110, 255),
                "PPTX" or "PPT"                             => Color.FromArgb(255, 209,  74,   0),
                "XLSX" or "XLS" or "CSV"                    => Color.FromArgb(255,  33, 136,  56),
                "PNG"  or "JPG" or "JPEG" or "BMP" or "GIF" => Color.FromArgb(255,  20, 160, 160),
                "MP4"  or "MKV" or "AVI"  or "MOV" or "WMV" => Color.FromArgb(255, 130,  50, 200),
                "MP3"  or "WAV" or "FLAC" or "AAC" or "OGG" => Color.FromArgb(255,  80, 120, 200),
                "ZIP"  or "RAR" or "7Z"   or "TAR" or "GZ"  => Color.FromArgb(255, 160, 120,   0),
                "CS"   or "PY"  or "JS"   or "TS"  or "CPP" => Color.FromArgb(255,  20, 140, 200),
                _                                           => Color.FromArgb(255, 100, 100, 110),
            };
            
            _typeColor = new SolidColorBrush(c);
            return _typeColor;
        }
    }

    public static DocumentItem FromFileInfo(FileInfo fi)
    {
        string ext = fi.Extension.TrimStart('.').ToUpperInvariant();
        return new DocumentItem
        {
            Title         = fi.Name,
            FilePath      = fi.FullName,
            FolderPath    = fi.DirectoryName ?? string.Empty,
            FileType      = ext,
            FileSizeBytes = fi.Length,
            FileSize      = FileSizeFormatter.Format(fi.Length),
            LastWriteTime = fi.LastWriteTime,
            LastModified  = fi.LastWriteTime.ToString("MMM dd, yyyy"),
        };
    }
}
