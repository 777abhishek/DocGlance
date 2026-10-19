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
