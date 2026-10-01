using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;
using Windows.UI;
using QuickView.Models;

namespace QuickView.Controls;

/// <summary>
/// List-view row that shares the same dark design language as DocumentCard.
/// Driven by the same FileTypeInfo record — same colours, same icons, no hover.
/// </summary>
public sealed partial class DocumentListItem : UserControl
{
    public DocumentListItem()
    {
        InitializeComponent();
        UpdateThemeColors();
        ActualThemeChanged += (s, e) => UpdateThemeColors();
    }

    private void UpdateThemeColors()
    {
        bool isDark = ActualTheme == ElementTheme.Dark;

        // Row background - dark in dark theme, light in light theme
        var rowBackgroundColor = isDark
            ? Color.FromArgb(255, 36, 36, 36)     // #FF242424 - dark
            : Color.FromArgb(255, 250, 250, 250); // #FFFAFAFA - light

        // Border color - subtle in both themes
        var borderColor = isDark
            ? Color.FromArgb(24, 255, 255, 255)   // #18FFFFFF - subtle white
            : Color.FromArgb(30, 0, 0, 0);        // #1E000000 - subtle black

        RowRoot.Background = new SolidColorBrush(rowBackgroundColor);
        RowRoot.BorderBrush = new SolidColorBrush(borderColor);

        // Text colors
        var primaryTextColor = isDark
            ? Microsoft.UI.Colors.White
            : Color.FromArgb(255, 32, 32, 32);

        var secondaryTextColor = isDark
            ? Color.FromArgb(128, 255, 255, 255)  // #80FFFFFF
            : Color.FromArgb(128, 64, 64, 64);    // #80404040

        var tertiaryTextColor = isDark
            ? Color.FromArgb(96, 255, 255, 255)   // #60FFFFFF
            : Color.FromArgb(96, 96, 96, 96);     // #60606060

        var sizeTextColor = isDark
            ? Color.FromArgb(184, 255, 255, 255)  // #B8FFFFFF
            : Color.FromArgb(184, 32, 32, 32);    // #B8202020

        var iconColor = isDark
            ? Color.FromArgb(216, 255, 255, 255)  // #D8FFFFFF
            : Color.FromArgb(216, 32, 32, 32);    // #D8202020

        FileNameText.Foreground = new SolidColorBrush(primaryTextColor);
        FileTypeDesc.Foreground = new SolidColorBrush(secondaryTextColor);
        ModifiedText.Foreground = new SolidColorBrush(secondaryTextColor);
        FileSizeText.Foreground = new SolidColorBrush(sizeTextColor);

        // Update icons
        if (this.FindName("ModifiedIcon") is FontIcon modIcon)
        {
            modIcon.Foreground = new SolidColorBrush(tertiaryTextColor);
        }
        if (this.FindName("MenuIcon") is FontIcon menuIcon)
        {
            menuIcon.Foreground = new SolidColorBrush(iconColor);
        }
    }


    // ========================================================================
    // DEPENDENCY PROPERTY
    // ========================================================================

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(
            nameof(Document),
            typeof(DocumentItem),
            typeof(DocumentListItem),
            new PropertyMetadata(null, OnDocumentChanged));

    public DocumentItem? Document
    {
        get => (DocumentItem?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentListItem item && e.NewValue is DocumentItem doc)
            item.UpdateItem(doc);
    }


    // ========================================================================
    // UPDATE
    // ========================================================================

    private void UpdateItem(DocumentItem doc)
    {
        FileNameText.Text  = string.IsNullOrWhiteSpace(doc.Title) ? "Unnamed file" : doc.Title;
        FileSizeText.Text  = doc.FileSize ?? string.Empty;
        ModifiedText.Text  = FormatDate(doc.LastWriteTime);
        ExtensionText.Text = NormalizeExtension(doc.FileType);

        FileTypeInfo info  = GetFileTypeInfo(doc.FileType);
        FileTypeDesc.Text  = info.Description;
        FileTypeIcon.Glyph = info.Icon;

        // Drive the preview gradient with the same colours as the card
        GradientStart.Color = info.StartColor;
        GradientEnd.Color   = info.EndColor;

        // Show thumbnail if available, hide icon overlay
        if (doc.Thumbnail != null)
        {
            ThumbnailImage.Source     = doc.Thumbnail;
            ThumbnailImage.Visibility = Visibility.Visible;
            IconOverlay.Visibility    = Visibility.Collapsed;
        }
        else
        {
            ThumbnailImage.Source     = null;
            ThumbnailImage.Visibility = Visibility.Collapsed;
            IconOverlay.Visibility    = Visibility.Visible;
        }
    }


    // ========================================================================
    // FILE TYPE INFO  (mirrors DocumentCard exactly)
    // ========================================================================

    private readonly record struct FileTypeInfo(
        string Description,
        string Icon,
        Color  StartColor,
        Color  EndColor);

    private static FileTypeInfo GetFileTypeInfo(string? fileType)
    {
        string ext = NormalizeExtension(fileType).TrimStart('.').ToUpperInvariant();

        return ext switch
        {
            "DOC" or "DOCX" =>
                new("Word Document",           "\uE8A5",
                    Color.FromArgb(255, 25,  103, 210),
                    Color.FromArgb(255, 62,  140, 235)),

            "PDF" =>
                new("PDF Document",            "\uEA90",
                    Color.FromArgb(255, 218,  42,  65),
                    Color.FromArgb(255, 245,  76,  96)),

            "XLS" or "XLSX" or "CSV" =>
                new("Excel Spreadsheet",       "\uE8F9",
                    Color.FromArgb(255, 25,  130,  70),
                    Color.FromArgb(255, 48,  175, 100)),

            "PPT" or "PPTX" =>
                new("PowerPoint",              "\uE8D1",
                    Color.FromArgb(255, 210,  70,  10),
                    Color.FromArgb(255, 245, 115,  35)),

            "JPG" or "JPEG" or "PNG" or "GIF" or "BMP" or "WEBP" or "SVG" =>
                new("Image",                   "\uEB9F",
                    Color.FromArgb(255, 105,  45, 195),
                    Color.FromArgb(255, 165,  70, 225)),

            "MP4" or "AVI" or "MKV" or "MOV" or "WMV" or "WEBM" =>
                new("Video",                   "\uE8B2",
                    Color.FromArgb(255,  85,  45, 175),
                    Color.FromArgb(255, 145,  75, 225)),

            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" =>
                new("Archive",                 "\uE8B1",
                    Color.FromArgb(255, 10,  145, 170),
                    Color.FromArgb(255, 30,  195, 215)),

            "CS" or "CPP" or "C" or "H" or "PY" or "JS" or "TS"
            or "HTML" or "CSS" or "JSON" or "XML" =>
                new("Code File",               "\uE943",
                    Color.FromArgb(255, 15,  115, 195),
                    Color.FromArgb(255, 35,  175, 225)),

            "TXT" or "MD" or "LOG" =>
                new("Text Document",           "\uE8A5",
                    Color.FromArgb(255, 80,   95, 120),
                    Color.FromArgb(255, 125, 145, 175)),

            "MP3" or "WAV" or "FLAC" or "AAC" or "M4A" =>
                new("Audio",                   "\uE8D6",
                    Color.FromArgb(255, 185,  70,  55),
                    Color.FromArgb(255, 225, 110,  75)),

            _ =>
                new("Document",                "\uE8A5",
                    Color.FromArgb(255, 75,   90, 115),
                    Color.FromArgb(255, 110, 130, 160))
        };
    }


    // ========================================================================
    // HELPERS
    // ========================================================================

    private static string NormalizeExtension(string? fileType)
    {
        if (string.IsNullOrWhiteSpace(fileType)) return string.Empty;
        string ext = fileType.Trim();
        if (!ext.StartsWith('.')) ext = "." + ext;
        return ext.ToLowerInvariant();
    }

    private static string FormatDate(DateTime date)
    {
        DateTime now  = DateTime.Now;
        TimeSpan diff = now - date;

        if (diff.TotalSeconds < 0)             return "Just now";
        if (date.Date == now.Date)             return "Today";
        if (date.Date == now.Date.AddDays(-1)) return "Yesterday";
        if (diff.TotalDays < 7)
        {
            int d = Math.Max(1, (int)diff.TotalDays);
            return $"{d} day{(d == 1 ? "" : "s")} ago";
        }
        return date.Year == now.Year
            ? date.ToString("MMM dd")
            : date.ToString("MMM dd, yyyy");
    }


    // ========================================================================
    // CONTEXT MENU
    // ========================================================================

    public event EventHandler? ContextMenuRequested;

    private void ContextMenuButton_Click(object sender, RoutedEventArgs e)
        => ContextMenuRequested?.Invoke(this, EventArgs.Empty);
}
