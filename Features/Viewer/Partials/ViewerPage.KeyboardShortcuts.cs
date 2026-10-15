using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DocGlance.Features.Viewer.Components;
using System;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private async void ShowKeyboardShortcuts(object? sender, EventArgs e)
    {
        if (XamlRoot == null)
            return;

        var sections = new StackPanel { Spacing = 14 };
        AddKeyboardShortcutSection(sections, "Presentation", new[]
        {
            ("F5", "Start from the beginning"),
            ("Shift+F5", "Start from the current page or sheet"),
            ("F11", "Toggle presentation mode"),
            ("Esc", "Exit presentation mode or close Find"),
            ("Right / Space / Page Down", "Next page or sheet (presentation mode)"),
            ("Left / Backspace / Page Up", "Previous page or sheet (presentation mode)"),
        });
        AddKeyboardShortcutSection(sections, "Viewer", new[]
        {
            ("F9", "Toggle the sidebar"),
            ("Ctrl+F", "Open or close Find"),
            ("Ctrl+I", "Show document information"),
            ("Ctrl+2", "Fit to width"),
            ("Ctrl+0", "Fit page"),
            ("Ctrl+- / Ctrl+Numpad -", "Zoom out"),
            ("Ctrl+Shift+= / Ctrl+Numpad +", "Zoom in"),
            ("Ctrl+R", "Rotate right (PDF)"),
            ("Ctrl+Mouse wheel", "Zoom in or out"),
        });
        AddKeyboardShortcutSection(sections, "Text and Find", new[]
        {
            ("Alt+Z", "Toggle word wrap (text files)"),
            ("Alt+C", "Toggle match case (while Find is open)"),
            ("Enter", "Next search result (while Find is focused)"),
            ("Shift+Enter", "Previous search result (while Find is focused)"),
        });

        var dialog = new ContentDialog
        {
            Title = "Keyboard shortcuts",
            Content = new ScrollViewer
            {
                Content = sections,
                Width = 540,
                MaxHeight = 520,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        ViewerPopupStyle.Apply(dialog);

        await dialog.ShowAsync();
    }

    private static void AddKeyboardShortcutSection(
        StackPanel parent,
        string title,
        (string Shortcut, string Description)[] shortcuts)
    {
        var section = new StackPanel { Spacing = 5 };
        section.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2),
        });

        foreach (var (shortcut, description) in shortcuts)
        {
            var row = new Grid { ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(205) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = shortcut,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var descriptionText = new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(descriptionText, 1);
            row.Children.Add(descriptionText);
            section.Children.Add(row);
        }

        parent.Children.Add(section);
    }
}
