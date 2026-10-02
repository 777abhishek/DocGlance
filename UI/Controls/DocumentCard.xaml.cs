using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;
using Windows.UI;
using QuickView.Core.Models;

namespace QuickView.UI.Controls;

/// <summary>
/// Clickable document card.
/// The outer CardButton covers the full card area — reliable hit-testing.
/// The ContextMenuButton floats on top (ZIndex=10) and does NOT bubble clicks to the card.
/// </summary>
public sealed partial class DocumentCard : UserControl
{
    public DocumentCard()
    {
        InitializeComponent();
    }

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>Fired when bookmark is toggled via context menu.</summary>
    public event EventHandler<BookmarkEventArgs>? BookmarkToggled;

    // ── Dependency Property ───────────────────────────────────────────────────

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(DocumentItem),
            typeof(DocumentCard),
            new PropertyMetadata(null, OnDocumentChanged));

    public DocumentItem? Document
    {
        get => (DocumentItem?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DocumentCard card) return;
        if (e.NewValue is DocumentItem doc) card.UpdateCard(doc);
        else card.ClearCard();
    }

    // ── Card button click — the ONLY reliable way to catch clicks on a WinUI card ──

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (Document == null) return;
        global::QuickView.UI.Shell.TitleBarWindow.Current?.OpenDocument(Document);
    }

    // ── Card visuals ──────────────────────────────────────────────────────────

    private void UpdateCard(DocumentItem doc)
    {
        FileNameText.Text  = string.IsNullOrWhiteSpace(doc.Title)    ? "Unnamed file" : doc.Title;
        FileSizeText.Text  = string.IsNullOrWhiteSpace(doc.FileSize) ? string.Empty   : doc.FileSize;
        ModifiedText.Text  = FormatDate(doc.LastWriteTime);
        ExtensionText.Text = NormalizeExt(doc.FileType);

        var info = GetFileTypeInfo(doc.FileType);
        FileTypeDesc.Text   = info.Description;
        FileTypeIcon.Glyph  = info.Icon;
        GradientStart.Color = info.StartColor;
        GradientEnd.Color   = info.EndColor;

        ThumbnailImage.Source     = null;
        ThumbnailImage.Visibility = Visibility.Collapsed;
        PreviewBackground.Visibility = Visibility.Visible;
    }

    private void ClearCard()
    {
        FileNameText.Text  = string.Empty;
        FileTypeDesc.Text  = string.Empty;
        FileSizeText.Text  = string.Empty;
        ModifiedText.Text  = string.Empty;
        ExtensionText.Text = string.Empty;

        ThumbnailImage.Source        = null;
        ThumbnailImage.Visibility    = Visibility.Collapsed;
        PreviewBackground.Visibility = Visibility.Visible;
    }

    // ── Context menu handlers ─────────────────────────────────────────────────

    /// <summary>"Open" in context menu → navigate to ViewerPage inside the app.</summary>
    private void OpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document == null) return;
        global::QuickView.UI.Shell.TitleBarWindow.Current?.OpenDocument(Document);
    }

    private void BookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document == null) return;
        BookmarkToggled?.Invoke(this, new BookmarkEventArgs(Document, BookmarkToggle.IsChecked));
    }

    private async void ShowInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.FilePath == null) return;
        try
        {
            var file   = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                             System.IO.Path.GetDirectoryName(Document.FilePath)!);
            var opts = new Windows.System.FolderLauncherOptions();
            opts.ItemsToSelect.Add(file);
            await Windows.System.Launcher.LaunchFolderAsync(folder, opts);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Explorer: {ex.Message}"); }
    }

    private async void ShareMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.FilePath == null) return;
        try
        {
            var pkg  = new Windows.ApplicationModel.DataTransfer.DataPackage();
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            pkg.SetStorageItems(new[] { file });
            pkg.Properties.Title = Document.Title ?? "Document";
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Share: {ex.Message}"); }
    }

    /// <summary>Open with system default external app.</summary>
    private async void OpenWithDefaultMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.FilePath == null) return;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            await Windows.System.Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"OpenDefault: {ex.Message}"); }
    }

    /// <summary>Open-with picker — lets user choose any installed app.</summary>
    private async void OpenWithChooseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.FilePath == null) return;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            await Windows.System.Launcher.LaunchFileAsync(
                file,
                new Windows.System.LauncherOptions { DisplayApplicationPicker = true });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"OpenWith: {ex.Message}"); }
    }

    // ── File-type info ────────────────────────────────────────────────────────

    private readonly record struct FileTypeInfo(string Description, string Icon, Color StartColor, Color EndColor);

    private static FileTypeInfo GetFileTypeInfo(string? fileType)
    {
        string ext = NormalizeExt(fileType).TrimStart('.').ToUpperInvariant();
        return ext switch
        {
            "DOC"  or "DOCX" => new("Word Document",            "\uE8A5", Color.FromArgb(255,15,61,145),   Color.FromArgb(255,47,111,228)),
            "PDF"            => new("PDF Document",             "\uEA90", Color.FromArgb(255,166,24,47),   Color.FromArgb(255,226,51,80)),
            "XLS"  or "XLSX" or "CSV"  => new("Excel Spreadsheet",  "\uE8F9", Color.FromArgb(255,15,107,61),  Color.FromArgb(255,47,174,110)),
            "PPT"  or "PPTX" => new("PowerPoint",               "\uE8D1", Color.FromArgb(255,184,70,10),   Color.FromArgb(255,240,129,42)),
            "JPG"  or "JPEG" or "PNG" or "GIF" or "BMP" or "WEBP" or "SVG" => new("Image", "\uEB9F", Color.FromArgb(255,91,42,158), Color.FromArgb(255,155,79,224)),
            "MP4"  or "AVI"  or "MKV" or "MOV" or "WMV" or "WEBM" => new("Video", "\uE8B2", Color.FromArgb(255,61,31,140), Color.FromArgb(255,122,79,224)),
            "ZIP"  or "RAR"  or "7Z"  or "TAR" or "GZ"  => new("Archive", "\uE8B1", Color.FromArgb(255,11,95,114),  Color.FromArgb(255,34,166,194)),
            "CS"   or "CPP"  or "C"   or "H"   or "PY"  or "JS" or "TS" or "HTML" or "CSS" or "JSON" or "XML" => new("Code File", "\uE943", Color.FromArgb(255,13,77,130), Color.FromArgb(255,36,150,214)),
            "TXT"  or "MD"   or "LOG" => new("Text Document",   "\uE8A5", Color.FromArgb(255,51,64,79),    Color.FromArgb(255,92,112,134)),
            "MP3"  or "WAV"  or "FLAC" or "AAC" or "M4A" => new("Audio", "\uE8D6", Color.FromArgb(255,140,42,42),  Color.FromArgb(255,210,96,74)),
            _                => new("Document",                 "\uE8A5", Color.FromArgb(255,46,54,72),    Color.FromArgb(255,74,85,104)),
        };
    }

    private static string NormalizeExt(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return string.Empty;
        string e = t.Trim();
        return (e.StartsWith('.') ? e : "." + e).ToLowerInvariant();
    }

    private static string FormatDate(DateTime d)
    {
        var now  = DateTime.Now;
        var diff = now - d;
        if (diff.TotalSeconds < 0)             return "Just now";
        if (d.Date == now.Date)                return "Today";
        if (d.Date == now.Date.AddDays(-1))    return "Yesterday";
        if (diff.TotalDays < 7) { int n = Math.Max(1,(int)diff.TotalDays); return $"{n} day{(n==1?"":"s")} ago"; }
        return d.Year == now.Year ? d.ToString("MMM dd") : d.ToString("MMM dd, yyyy");
    }
}

public class BookmarkEventArgs : EventArgs
{
    public DocumentItem Document   { get; }
    public bool         IsBookmarked { get; }
    public BookmarkEventArgs(DocumentItem doc, bool bookmarked) { Document = doc; IsBookmarked = bookmarked; }
}
