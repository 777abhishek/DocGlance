using DocGlance.Core.Models;
using DocGlance.Features.Viewer.Contracts;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer.Formats.Fallback;

public sealed class FallbackViewer : UserControl, IDocumentViewer
{
    private readonly FontIcon _icon;
    private readonly TextBlock _name;
    private readonly TextBlock _metadata;
    private readonly TextBlock _path;

    public FallbackViewer()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        _icon = new FontIcon
        {
            FontFamily = new FontFamily("ms-appx:///Assets/Fonts/FluentSystemIcons-Regular.ttf#FluentSystemIcons-Regular"),
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center,
            Opacity = 0.6,
        };
        _name = new TextBlock
        {
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500,
        };
        _metadata = new TextBlock
        {
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Opacity = 0.8,
        };
        _path = new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500,
            Opacity = 0.6,
            Margin = new Thickness(0, -6, 0, 4),
        };
        var openButton = new Button
        {
            Content = "Open with default app",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(20, 10, 20, 10),
            Style = GetStyle("AccentButtonStyle"),
        };
        openButton.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);

        Content = new StackPanel
        {
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _icon, _name, _metadata, _path, openButton },
        };
    }

    public event EventHandler? OpenRequested;
    public FrameworkElement Control => this;

    public Task LoadAsync(DocumentItem document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _icon.Glyph = document.FileGlyph;
        _icon.Foreground = document.TypeColor;
        _name.Text = document.Title;
        _name.Foreground = GetBrush("TextFillColorPrimaryBrush");
        _metadata.Text = $"{document.FileType?.ToUpperInvariant()}  ·  {document.FileSize}  ·  {document.LastWriteTime:MMM dd, yyyy}";
        _metadata.Foreground = GetBrush("TextFillColorSecondaryBrush");
        _path.Text = document.FilePath;
        _path.Foreground = GetBrush("TextFillColorTertiaryBrush");
        return Task.CompletedTask;
    }

    public Task CleanupAsync() => Task.CompletedTask;

    private static Brush GetBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Colors.Transparent);

    private static Style? GetStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out object value) && value is Style style
            ? style
            : null;
}

public sealed class FallbackViewerFactory : IDocumentViewerFactory
{
    private static readonly IReadOnlyCollection<string> Extensions = new[] { ".xls" };

    public IReadOnlyCollection<string> SupportedExtensions => Extensions;

    public IDocumentViewer Create(ViewerViewModel viewModel) => new FallbackViewer();
}
