using System;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DocGlance.Features.Viewer.Components;

internal static class ViewerPopupStyle
{
    public static void Apply(ContentDialog dialog)
    {
        dialog.Background = GetBrush(dialog, "DocGlanceSurfaceBrush");
        dialog.BorderBrush = new SolidColorBrush(Colors.Transparent);
        dialog.BorderThickness = new Thickness(0);
    }

    public static void ApplyCompactField(Control control)
    {
        control.Background = GetBrush(control, "DocGlanceSurfaceBrush");
        control.BorderBrush = new SolidColorBrush(Colors.Transparent);
        control.BorderThickness = new Thickness(0);
    }

    public static void ApplyCompactSurface(Border border)
    {
        border.Background = GetBrush(border, "DocGlanceSurfaceBrush");
        border.BorderBrush = new SolidColorBrush(Colors.Transparent);
        border.BorderThickness = new Thickness(0);
    }

    private static Brush GetBrush(FrameworkElement element, string key)
    {
        ElementTheme theme = element.ActualTheme;
        bool dark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default &&
             Application.Current.RequestedTheme == ApplicationTheme.Dark);
        string dictionaryKey = dark ? "Dark" : "Light";
        ResourceDictionary? themeDictionary =
            Application.Current.Resources.ThemeDictionaries[dictionaryKey] as ResourceDictionary;

        if (themeDictionary?.ContainsKey(key) == true &&
            themeDictionary[key] is Brush brush)
            return brush;

        return Application.Current.Resources[key] as Brush ??
            throw new InvalidOperationException($"The popup theme resource '{key}' is missing.");
    }
}
