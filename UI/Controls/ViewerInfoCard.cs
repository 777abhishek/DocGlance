// ViewerInfoCard.cs
// Code-only ContentControl — no XAML file needed.
// Renders a centred info card for non-PDF files in the ViewerPage canvas.
// Theme brushes are resolved at construction time via Application.Current.Resources.

using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using QuickView.Core.Models;

namespace QuickView.UI.Controls;

/// <summary>
/// A centred card showing file icon, name, metadata, path and
/// an "Open with default app" button.
/// </summary>
public sealed class ViewerInfoCard : ContentControl
{
    /// <summary>Fired when the user clicks "Open with default app".</summary>
    public event EventHandler? OpenRequested;

    public ViewerInfoCard(DocumentItem doc)
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment   = VerticalAlignment.Center;
        Content             = BuildSimpleContent(doc);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static Brush Brush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b) return b;
        return new SolidColorBrush(Colors.Transparent);
    }

    private static Microsoft.UI.Xaml.Style? ResStyle(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is Microsoft.UI.Xaml.Style s) return s;
        return null;
    }

    // ── content builder ───────────────────────────────────────────────────────

    private StackPanel BuildSimpleContent(DocumentItem doc)
    {
        var stack = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            Spacing             = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };

        stack.Children.Add(new FontIcon
        {
            Glyph               = doc.FileGlyph,
            FontSize            = 48,
            FontFamily          = new FontFamily("ms-appx:///Assets/Fonts/FluentSystemIcons-Regular.ttf#FluentSystemIcons-Regular"),
            Foreground          = doc.TypeColor,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity             = 0.6,
        });

        stack.Children.Add(new TextBlock
        {
            Text                = doc.Title,
            FontSize            = 18,
            FontWeight          = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping        = TextWrapping.Wrap,
            MaxWidth            = 500,
            TextAlignment       = TextAlignment.Center,
            Foreground          = Brush("TextFillColorPrimaryBrush"),
        });

        stack.Children.Add(new TextBlock
        {
            Text                = $"{doc.FileType?.ToUpperInvariant()}  ·  {doc.FileSize}  ·  {doc.LastWriteTime:MMM dd, yyyy}",
            FontSize            = 13,
            Foreground          = Brush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment       = TextAlignment.Center,
            Opacity             = 0.8,
        });

        stack.Children.Add(new TextBlock
        {
            Text                = doc.FilePath,
            FontSize            = 12,
            Foreground          = Brush("TextFillColorTertiaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping        = TextWrapping.Wrap,
            MaxWidth            = 500,
            TextAlignment       = TextAlignment.Center,
            Opacity             = 0.6,
            Margin              = new Thickness(0, -6, 0, 4),
        });

        var btn = new Button
        {
            Content             = "Open with default app",
            HorizontalAlignment = HorizontalAlignment.Center,
            Style               = ResStyle("AccentButtonStyle"),
            Padding             = new Thickness(20, 10, 20, 10),
        };
        btn.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(btn);

        return stack;
    }
}
