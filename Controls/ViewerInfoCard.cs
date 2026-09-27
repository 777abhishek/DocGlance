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
using QuickView.Models;

namespace QuickView.Controls;

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
        Content             = BuildCard(doc);
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

    // ── card builder ──────────────────────────────────────────────────────────

    private Border BuildCard(DocumentItem doc)
    {
        var card = new Border
        {
            Width           = 560,
            MinHeight       = 380,
            CornerRadius    = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding         = new Thickness(48, 40, 48, 40),
            Background      = Brush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush     = Brush("CardStrokeColorDefaultBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
        };
        card.Shadow      = new ThemeShadow();
        card.Translation = new System.Numerics.Vector3(0, 0, 12);
        card.Child       = BuildContent(doc);
        return card;
    }

    private StackPanel BuildContent(DocumentItem doc)
    {
        var stack = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            Spacing             = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        stack.Children.Add(new FontIcon
        {
            Glyph               = doc.FileGlyph,
            FontSize            = 64,
            Foreground          = doc.TypeColor,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text                = doc.Title,
            FontSize            = 20,
            FontWeight          = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping        = TextWrapping.Wrap,
            MaxWidth            = 460,
            TextAlignment       = TextAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text                = $"{doc.FileType?.ToUpperInvariant()}  ·  {doc.FileSize}  ·  {doc.LastWriteTime:MMM dd, yyyy}",
            Foreground          = Brush("TextFillColorSecondaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment       = TextAlignment.Center,
        });

        stack.Children.Add(new TextBlock
        {
            Text                = doc.FilePath,
            FontSize            = 12,
            Foreground          = Brush("TextFillColorTertiaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping        = TextWrapping.Wrap,
            MaxWidth            = 460,
            TextAlignment       = TextAlignment.Center,
        });

        var btn = new Button
        {
            Content             = "Open with default app",
            HorizontalAlignment = HorizontalAlignment.Center,
            Style               = ResStyle("AccentButtonStyle"),
        };
        btn.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        stack.Children.Add(btn);

        return stack;
    }
}
