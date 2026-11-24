using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using DocGlance.Core.Enums;
using DocGlance.Features.Viewer.Components;
using DocGlance.Features.Viewer.Formats.Images;
using System;
using Windows.System;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private const VirtualKey OemMinusKey = (VirtualKey)0xBD;
    private const VirtualKey OemPlusKey = (VirtualKey)0xBB;

    private void InitializeViewerShortcuts()
    {
        // F11 disabled - caused unwanted popups
        // AddViewerShortcut(VirtualKey.F11, VirtualKeyModifiers.None,
        //     () => _doc != null, TogglePresentationMode);
        AddViewerShortcut(VirtualKey.Escape, VirtualKeyModifiers.None,
            () => _isPresentationMode, ExitPresentationMode);

        AddViewerShortcut(VirtualKey.Right, VirtualKeyModifiers.None,
            () => _isPresentationMode && _total > 1, () => NavigatePresentationPage(1));
        AddViewerShortcut(VirtualKey.PageDown, VirtualKeyModifiers.None,
            () => _isPresentationMode && _total > 1, () => NavigatePresentationPage(1));
        AddViewerShortcut(VirtualKey.Space, VirtualKeyModifiers.None,
            () => _isPresentationMode && _total > 1, () => NavigatePresentationPage(1));
        AddViewerShortcut(VirtualKey.Left, VirtualKeyModifiers.None,
            () => _isPresentationMode && _total > 1, () => NavigatePresentationPage(-1));
        AddViewerShortcut(VirtualKey.PageUp, VirtualKeyModifiers.None,
            () => _isPresentationMode && _total > 1, () => NavigatePresentationPage(-1));
        AddViewerShortcut(VirtualKey.Back, VirtualKeyModifiers.None,
            () => _isPresentationMode && _total > 1, () => NavigatePresentationPage(-1));

        AddViewerShortcut(VirtualKey.F9, VirtualKeyModifiers.None,
            () => Toolbar.SidebarButtonVisibility == Visibility.Visible,
            () =>
            {
                Toolbar.SidebarIsChecked = !(Toolbar.SidebarIsChecked ?? false);
                Toolbar_SidebarToggled(this, EventArgs.Empty);
            });
        AddViewerShortcut(VirtualKey.F1, VirtualKeyModifiers.None,
            () => _doc != null, () => ShowKeyboardShortcuts(this, EventArgs.Empty));
        AddViewerShortcut(VirtualKey.F, VirtualKeyModifiers.Control,
            () => _doc != null, () => Toolbar_FindRequested(this, EventArgs.Empty));
        AddViewerShortcut(VirtualKey.I, VirtualKeyModifiers.Control,
            () => _doc != null, () => DocInfoButton_Click(this, EventArgs.Empty));
        AddViewerShortcut(VirtualKey.Number2, VirtualKeyModifiers.Control,
            () => Toolbar.FitWidthButtonVisibility == Visibility.Visible,
            () => Toolbar_FitWidthRequested(this, EventArgs.Empty));
        AddViewerShortcut(VirtualKey.Number0, VirtualKeyModifiers.Control,
            () => Toolbar.FitPageButtonVisibility == Visibility.Visible,
            () => Toolbar_FitPageRequested(this, EventArgs.Empty));
        AddViewerShortcut(VirtualKey.R, VirtualKeyModifiers.Control,
            () => _pdfState != null, () => Rotate(90));
        AddViewerShortcut(VirtualKey.Z, VirtualKeyModifiers.Menu,
            () => Toolbar.WordWrapVisibility == Visibility.Visible, ToggleWordWrap);
        AddViewerShortcut(VirtualKey.C, VirtualKeyModifiers.Menu,
            () => FindBar.Visibility == Visibility.Visible, ToggleMatchCase);
        AddViewerShortcut(VirtualKey.Z, VirtualKeyModifiers.Control,
            CanUndoPdfAnnotationFromShortcut, () => _ = RestorePdfAnnotationHistoryAsync(undo: true));
        AddViewerShortcut(VirtualKey.Y, VirtualKeyModifiers.Control,
            CanRedoPdfAnnotationFromShortcut, () => _ = RestorePdfAnnotationHistoryAsync(undo: false));
        AddViewerShortcut(VirtualKey.Z, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            CanRedoPdfAnnotationFromShortcut, () => _ = RestorePdfAnnotationHistoryAsync(undo: false));

        AddViewerShortcut(OemMinusKey, VirtualKeyModifiers.Control,
            CanZoomCurrentDocument, ZoomOutFromShortcut);
        AddViewerShortcut(VirtualKey.Subtract, VirtualKeyModifiers.Control,
            CanZoomCurrentDocument, ZoomOutFromShortcut);
        AddViewerShortcut(OemPlusKey, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            CanZoomCurrentDocument, ZoomInFromShortcut);
        AddViewerShortcut(VirtualKey.Add, VirtualKeyModifiers.Control,
            CanZoomCurrentDocument, ZoomInFromShortcut);
    }

    private void AddViewerShortcut(
        VirtualKey key,
        VirtualKeyModifiers modifiers,
        Func<bool> canExecute,
        Action action)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = modifiers,
            IsEnabled = true,
            // Disable the visual key indicator popup
            ScopeOwner = null,
        };
        accelerator.Invoked += (_, args) =>
        {
            if (!canExecute())
                return;

            action();
            args.Handled = true;
        };
        ViewerRoot.KeyboardAccelerators.Add(accelerator);
    }

    private bool CanZoomCurrentDocument() =>
        _pdfState != null || _pptxDocument != null || _docxDocument != null ||
        _spreadsheetDocument != null || _activeViewer is ImageViewer;

    private bool CanUndoPdfAnnotationFromShortcut() =>
        _pdfState != null && _pdfUndoSnapshots.Count > 0 && !IsTextEntryFocused();

    private bool CanRedoPdfAnnotationFromShortcut() =>
        _pdfState != null && _pdfRedoSnapshots.Count > 0 && !IsTextEntryFocused();

    private bool IsTextEntryFocused() =>
        XamlRoot != null &&
        Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) is
            TextBox or RichEditBox or PasswordBox or AutoSuggestBox or NumberBox;

    private void ToggleWordWrap()
    {
        Toolbar.IsWordWrapChecked = !Toolbar.IsWordWrapChecked;
        WordWrapButton_Click(this, EventArgs.Empty);
    }

    private void ToggleMatchCase()
    {
        MatchCaseButton.IsChecked = !(MatchCaseButton.IsChecked ?? false);
        MatchCaseButton_Click(MatchCaseButton, null!);
    }

    private void ZoomOutFromShortcut()
    {
        SetFitMode(FitMode.None);
        SetZoom(_zoom / 1.25);
    }

    private void ZoomInFromShortcut()
    {
        SetFitMode(FitMode.None);
        SetZoom(_zoom * 1.25);
    }

    private async void ShowKeyboardShortcuts(object? sender, EventArgs e)
    {
        if (XamlRoot == null)
            return;

        var sections = new StackPanel { Spacing = 14 };
        AddKeyboardShortcutSection(sections, "Presentation", new[]
        {
            // F11 disabled - caused unwanted popups
            // ("F11", "Toggle presentation mode"),
            ("Esc", "Exit presentation mode or close Find"),
            ("Right / Space / Page Down", "Next page or sheet (presentation mode)"),
            ("Left / Backspace / Page Up", "Previous page or sheet (presentation mode)"),
        });
        AddKeyboardShortcutSection(sections, "Viewer", new[]
        {
            ("F1", "Show this keyboard shortcuts guide"),
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
        AddKeyboardShortcutSection(sections, "PDF annotations", new[]
        {
            ("Ctrl+Z", "Undo the last annotation change"),
            ("Ctrl+Y / Ctrl+Shift+Z", "Redo an annotation change"),
        });
        AddKeyboardShortcutSection(sections, "Text selection", new[]
        {
            ("Ctrl+C", "Copy selected PDF, Word, or PowerPoint text, or selected spreadsheet cells"),
        });
        AddKeyboardShortcutSection(sections, "Text and Find", new[]
        {
            ("Alt+Z", "Toggle word wrap (text files)"),
            ("Alt+C", "Toggle match case (while Find is open)"),
            ("Enter", "Next search result (while Find is focused)"),
            ("Shift+Enter", "Previous search result (while Find is focused)"),
            ("Down / Right", "Next search result (while Find is focused)"),
            ("Up / Left", "Previous search result (while Find is focused)"),
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
