using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using Windows.UI;
using DocGlance.UI.Shell;

namespace DocGlance.Services.Themes;

public class ThemeService
{
    private static ThemeService? _instance;
    public static ThemeService Instance => _instance ??= new ThemeService();

    private ThemeService() { }

    public void ApplyPalette(string paletteName)
    {
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        // Find and remove existing palette (should be at app level, before Theme.xaml)
        var existingPalette = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.Contains("Palettes/Palette.") == true);
        
        int insertIndex = 0;
        if (existingPalette != null)
        {
            insertIndex = app.Resources.MergedDictionaries.IndexOf(existingPalette);
            app.Resources.MergedDictionaries.Remove(existingPalette);
        }
        else
        {
            // Find Theme.xaml and insert palette before it
            var themeIndex = app.Resources.MergedDictionaries
                .Select((d, i) => new { Dictionary = d, Index = i })
                .FirstOrDefault(x => x.Dictionary.Source?.OriginalString?.Contains("Theme.xaml") == true)?.Index;
            
            insertIndex = themeIndex ?? 0;
        }

        // Add new palette at the correct position
        var paletteUri = new Uri($"ms-appx:///UI/Styles/Palettes/Palette.{paletteName}.xaml");
        var newPalette = new ResourceDictionary { Source = paletteUri };
        app.Resources.MergedDictionaries.Insert(insertIndex, newPalette);

        // Force theme refresh by toggling RequestedTheme
        if (TitleBarWindow.Current?.Content is FrameworkElement rootElement)
        {
            var current = rootElement.RequestedTheme;
            rootElement.RequestedTheme = current == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            rootElement.RequestedTheme = current;
        }
    }

    public void ApplyTheme(ElementTheme theme)
    {
        if (TitleBarWindow.Current?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
        }
    }

    public Color GetColorResource(string resourceKey)
    {
        if (Application.Current?.Resources.TryGetValue(resourceKey, out var resource) == true)
        {
            if (resource is SolidColorBrush brush)
                return brush.Color;
            if (resource is Color color)
                return color;
        }
        return Microsoft.UI.Colors.Transparent;
    }

    public string GetCurrentPalette()
    {
        var app = Application.Current;
        if (app?.Resources == null)
            return "Midnight"; // default

        var palette = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.Contains("Palettes/Palette.") == true);

        if (palette?.Source != null)
        {
            var uri = palette.Source.OriginalString;
            var start = uri.LastIndexOf("Palette.") + 8;
            var end = uri.LastIndexOf(".xaml");
            if (start > 7 && end > start)
            {
                return uri.Substring(start, end - start);
            }
        }

        return "Midnight";
    }
}
