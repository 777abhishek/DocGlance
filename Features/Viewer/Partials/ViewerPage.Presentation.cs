using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using DocGlance.Core.Enums;
using System;
using Windows.System;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private bool _isPresentationMode;
    private FitMode _fitModeBeforePresentation;
    private double _zoomBeforePresentation;
    private Visibility _toolbarVisibilityBeforePresentation;
    private Visibility _sidebarVisibilityBeforePresentation;
    private Visibility _zoomPillVisibilityBeforePresentation;
    private GridLength _sidebarWidthBeforePresentation;
    private double _plainTextFontSizeBeforePresentation;
    private double _lineNumberFontSizeBeforePresentation;
    private double _lineNumberHeightBeforePresentation;

    private const VirtualKey OemMinusKey = (VirtualKey)0xBD;
    private const VirtualKey OemPlusKey = (VirtualKey)0xBB;

    private void InitializeViewerShortcuts()
    {
        AddViewerShortcut(VirtualKey.F5, VirtualKeyModifiers.None,
            () => _doc != null, StartPresentationFromBeginning);
        AddViewerShortcut(VirtualKey.F5, VirtualKeyModifiers.Shift,
            () => _doc != null, EnterPresentationMode);
        AddViewerShortcut(VirtualKey.F11, VirtualKeyModifiers.None,
            () => _doc != null, TogglePresentationMode);
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
        _spreadsheetDocument != null;

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

    private void StartPresentationFromBeginning()
    {
        GoToPage(1);
        DocumentScrollViewer.ChangeView(0, 0, null, disableAnimation: true);
        if (TextViewerHost.Visibility == Visibility.Visible)
            TextViewerControl.TextBox.Select(0, 0);
        if (SpreadsheetViewerHost.Visibility == Visibility.Visible)
            SpreadsheetViewerControl.ResetScrollPosition();

        EnterPresentationMode();
    }

    private void TogglePresentationMode()
    {
        if (_isPresentationMode)
            ExitPresentationMode();
        else
            EnterPresentationMode();
    }

    private void EnterPresentationMode()
    {
        if (_isPresentationMode || _doc == null)
            return;

        var window = DocGlance.UI.Shell.TitleBarWindow.Current;
        if (window == null || !window.SetPresentationMode(true))
            return;

        _fitModeBeforePresentation = _fitMode;
        _zoomBeforePresentation = _zoom;
        _toolbarVisibilityBeforePresentation = Toolbar.Visibility;
        _sidebarVisibilityBeforePresentation = SidebarCard.Visibility;
        _zoomPillVisibilityBeforePresentation = ZoomPill.Visibility;
        _sidebarWidthBeforePresentation = SidebarColumn.Width;
        _plainTextFontSizeBeforePresentation = TextViewerControl.TextBox.FontSize;
        _lineNumberFontSizeBeforePresentation = TextViewerControl.LineNumbers.FontSize;
        _lineNumberHeightBeforePresentation = TextViewerControl.LineNumbers.LineHeight;
        _isPresentationMode = true;

        FindBar.Visibility = Visibility.Collapsed;
        FindBox.Text = string.Empty;
        ClearSearchHighlights();

        Toolbar.Visibility = Visibility.Collapsed;
        SidebarCard.Visibility = Visibility.Collapsed;
        ZoomPill.Visibility = Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(0);
        SetFitMode(_spreadsheetDocument != null ? FitMode.FitWidth : FitMode.FitPage);

        if (TextViewerHost.Visibility == Visibility.Visible)
        {
            TextViewerControl.TextBox.FontSize = 22;
            TextViewerControl.LineNumbers.FontSize = 22;
            TextViewerControl.LineNumbers.LineHeight = 36;
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!_isPresentationMode)
                return;

            UpdateLayout();
            ApplyPresentationFit();
        });
    }

    private void ExitPresentationMode()
    {
        if (!_isPresentationMode)
            return;

        var window = DocGlance.UI.Shell.TitleBarWindow.Current;
        if (window == null || !window.SetPresentationMode(false))
            return;

        _isPresentationMode = false;
        Toolbar.Visibility = _toolbarVisibilityBeforePresentation;
        SidebarCard.Visibility = _sidebarVisibilityBeforePresentation;
        ZoomPill.Visibility = _zoomPillVisibilityBeforePresentation;
        SidebarColumn.Width = _sidebarWidthBeforePresentation;
        TextViewerControl.TextBox.FontSize = _plainTextFontSizeBeforePresentation;
        TextViewerControl.LineNumbers.FontSize = _lineNumberFontSizeBeforePresentation;
        TextViewerControl.LineNumbers.LineHeight = _lineNumberHeightBeforePresentation;

        SetFitMode(_fitModeBeforePresentation);
        if (_fitModeBeforePresentation == FitMode.None)
            SetZoom(_zoomBeforePresentation);
        else
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ReapplyFitMode);
    }

    private void NavigatePresentationPage(int direction)
    {
        GoToPage(Math.Clamp(_page + direction, 1, Math.Max(1, _total)));
    }

    private void ApplyPresentationFit()
    {
        if (_spreadsheetDocument != null)
        {
            ApplyFitWidth();
            return;
        }

        ApplyFitPage();
    }
}
