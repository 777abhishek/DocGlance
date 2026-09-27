using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Core.Helpers;
using QuickView.Models;
using System;
using System.IO;

namespace QuickView.Features.Viewer.Components;

/// <summary>
/// Clean document info popup — shows file name, type, size, pages, path, dates.
/// </summary>
internal static class DocumentInfoDialog
{
    public static ContentDialog Build(DocumentItem doc, int totalPages, XamlRoot root)
    {
        var fi = File.Exists(doc.FilePath) ? new FileInfo(doc.FilePath) : null;

        var grid = new Grid { RowSpacing = 4 };
        for (int i = 0; i < 8; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "\uE8A5", "File name",     Path.GetFileName(doc.FilePath) ?? "—");
        AddRow(grid, 1, "\uE8EC", "Type",           (doc.FileType ?? "—").TrimStart('.').ToUpperInvariant());
        AddRow(grid, 2, "\uE2B2", "Size",           fi != null ? FileSizeHelper.Format(fi.Length) : "—");
        AddRow(grid, 3, "\uE9D5", "Pages",          totalPages > 1 ? $"{totalPages} pages" : "—");
        AddRow(grid, 4, "\uE8DA", "Location",       doc.FolderPath ?? "—", wrap: true);
        AddRow(grid, 5, "\uE787", "Modified",       fi?.LastWriteTime.ToString("MMM d, yyyy  h:mm tt") ?? "—");
        AddRow(grid, 6, "\uE8B5", "Created",        fi?.CreationTime.ToString("MMM d, yyyy  h:mm tt") ?? "—");
        AddRow(grid, 7, "\uE71C", "Full path",      doc.FilePath ?? "—", wrap: true);

        var dialog = new ContentDialog
        {
            Title           = "Document Info",
            Content         = new ScrollViewer
            {
                Content            = grid,
                MaxHeight          = 480,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding            = new Thickness(0, 12, 0, 12),
            },
            CloseButtonText = "Close",
            DefaultButton   = ContentDialogButton.Close,
            XamlRoot        = root,
            MinWidth        = 500,
        };

        return dialog;
    }

    private static void AddRow(Grid grid, int row, string glyph, string label, string value, bool wrap = false)
    {
        var inner = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 8, 0, 8) };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });       // icon
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });   // label
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // value

        var icon = new FontIcon
        {
            Glyph              = glyph,
            FontSize           = 16,
            VerticalAlignment  = VerticalAlignment.Top,
            Margin             = new Thickness(0, 2, 0, 0),
            Foreground         = (Microsoft.UI.Xaml.Media.Brush)
                                 Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
        };

        var labelBlock = new TextBlock
        {
            Text              = label,
            FontSize          = 13,
            FontWeight        = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground        = (Microsoft.UI.Xaml.Media.Brush)
                                Application.Current.Resources["TextFillColorSecondaryBrush"],
        };

        var valueBlock = new TextBlock
        {
            Text            = value,
            FontSize        = 13,
            TextWrapping    = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming    = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = true,
            VerticalAlignment = VerticalAlignment.Top,
        };

        Grid.SetColumn(icon,       0);
        Grid.SetColumn(labelBlock, 1);
        Grid.SetColumn(valueBlock, 2);
        inner.Children.Add(icon);
        inner.Children.Add(labelBlock);
        inner.Children.Add(valueBlock);

        Grid.SetRow(inner, row);
        grid.Children.Add(inner);
    }
}
