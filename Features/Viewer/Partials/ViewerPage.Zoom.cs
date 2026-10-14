using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using DocGlance.Rendering.Pdf;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.System;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    // ── FitMode tracking ──────────────────────────────────────────────────────
    private enum FitMode { None, FitWidth, FitPage }
    private FitMode _fitMode = FitMode.None;

    private void SetFitMode(FitMode mode)
    {
        _fitMode = mode;
        Toolbar.IsFitWidthChecked = mode == FitMode.FitWidth;
        Toolbar.IsFitPageChecked = mode == FitMode.FitPage;
    }
    
    // ── Zoom buttons ──────────────────────────────────────────────────────────

    private void ZoomFitWidth_Click(object sender, RoutedEventArgs e)
    {
        Toolbar_FitWidthRequested(sender, EventArgs.Empty);
    }

    private void ZoomFitPage_Click(object sender, RoutedEventArgs e)
    {
        Toolbar_FitPageRequested(sender, EventArgs.Empty);
    }

    private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        SetFitMode(FitMode.None);
        SetZoom(_zoom / 1.25);
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        SetFitMode(FitMode.None);
        SetZoom(_zoom * 1.25);
    }

    private void ZoomPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item &&
            double.TryParse(item.Tag?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double pct))
        {
            SetFitMode(FitMode.None);
            SetZoom(NaturalZoom * pct);
        }
    }

    // ── Core zoom setter ──────────────────────────────────────────────────────

    /// <summary>
    /// Sets zoom. For PDF the ScrollViewer owns scaling (ZoomMode=Enabled,
    /// canvas sized at zoom 1.0). For PPTX and DOCX, the page canvas is also
    /// sized at zoom 1.0 and scaled by the ScrollViewer.
    /// </summary>
    internal void SetZoom(double z)
    {
        _zoom = Math.Clamp(z, 0.1, 6.0);

        if (_viewportState != null)
            _viewportState.ZoomFactor = (float)_zoom;

        double displayPct = Math.Round(_zoom / NaturalZoom * 100.0);
        ZoomLabel.Text = $"{displayPct}%";
        SyncToViewModel();

        // PDF: let ScrollViewer scale the canvas, then re-render pages at new resolution
        if (_pdfState != null)
        {
            DocumentScrollViewer.ChangeView(null, null, (float)_zoom, disableAnimation: false);
            _ = RenderPagesAsync();
            return;
        }

        // PPTX: same — ScrollViewer scales the pre-sized canvas
        if (_pptxDocument != null)
        {
            DocumentScrollViewer.ChangeView(null, null, (float)_zoom, disableAnimation: false);
            return;
        }

        // DOCX uses the same page-sized canvas and ScrollViewer scaling as PPTX.
        if (_docxDocument != null)
        {
            DocumentScrollViewer.ChangeView(null, null, (float)_zoom, disableAnimation: false);
            return;
        }

        if (_spreadsheetDocument != null)
        {
            SpreadsheetViewerControl.SetZoom(_zoom);
        }
    }

    private Windows.Foundation.Point GetViewportCenter() =>
        new(DocumentScrollViewer.ViewportWidth / 2.0,
            DocumentScrollViewer.ViewportHeight / 2.0);

    // ── Fit-Width ─────────────────────────────────────────────────────────────

    internal void ApplyFitWidth()
    {
        DocumentScrollViewer.UpdateLayout();

        // Handle PDF
        if (_pdfState != null && _viewportState != null)
        {
            // Base width = page width at zoom 1.0 in layout px
            var (ptW, _) = GetRotatedPageSize(0);
            if (ptW <= 0) return;

            double pageWidthLp = ptW * (96.0 / 72.0);

            // Available width in viewport
            const double sidePad = 28.0;
            double avail = DocumentScrollViewer.ViewportWidth - sidePad * 2;
            if (avail <= 0) return;

            // Calculate zoom to fit width
            double targetZoom = avail / pageWidthLp;
            SetZoom(targetZoom);
            return;
        }
        
        // Handle PPTX
        if (_pptxDocument != null)
        {
            const double PtToLp = 96.0 / 72.0;
            var (ptW, _) = _pptxDocument.GetSlideSize(0);
            if (ptW <= 0) return;

            // Convert to layout pixels
            double widthLp = ptW * PtToLp;

            // Available width in viewport
            const double sidePad = 28.0;
            double avail = DocumentScrollViewer.ViewportWidth - sidePad * 2;
            if (avail <= 0) return;

            // Calculate zoom to fit width
            double targetZoom = avail / widthLp;
            
            DocGlance.Infrastructure.Logging.Logger.Log($"ApplyFitWidth PPTX: widthLp={widthLp}, avail={avail}, targetZoom={targetZoom}");
            
            SetZoom(targetZoom);
            return;
        }

        // DOCX page dimensions are reported in points by WordCraft.
        if (_docxDocument != null)
        {
            const double PtToLp = 96.0 / 72.0;
            var (ptW, _) = _docxDocument.GetPageSize(0);
            if (ptW <= 0) return;

            double widthLp = ptW * PtToLp;
            const double sidePad = 28.0;
            double avail = DocumentScrollViewer.ViewportWidth - sidePad * 2;
            if (avail <= 0) return;

            SetZoom(avail / widthLp);
            return;
        }

        if (_spreadsheetDocument != null)
        {
            SetZoom(SpreadsheetViewerControl.GetFitWidthZoom());
        }
    }

    // ── Fit-Page ──────────────────────────────────────────────────────────────

    internal void ApplyFitPage()
    {
        DocumentScrollViewer.UpdateLayout();

        // Handle PDF
        if (_pdfState != null && _viewportState != null)
        {
            // Base dimensions = page size at zoom 1.0 in layout px
            var (ptW, ptH) = GetRotatedPageSize(0);
            if (ptW <= 0 || ptH <= 0) return;

            double widthLp  = ptW * (96.0 / 72.0);
            double heightLp = ptH * (96.0 / 72.0);

            // Available space
            const double sidePad = 28.0;
            const double topPad  = 16.0;
            double aw = DocumentScrollViewer.ViewportWidth  - sidePad * 2;
            double ah = DocumentScrollViewer.ViewportHeight - topPad  * 2;
            if (aw <= 0 || ah <= 0) return;

            // Fit both dimensions
            double targetZoom = Math.Min(aw / widthLp, ah / heightLp);
            SetZoom(targetZoom);
            return;
        }
        
        // Handle PPTX
        if (_pptxDocument != null)
        {
            const double PtToLp = 96.0 / 72.0;
            var (ptW, ptH) = _pptxDocument.GetSlideSize(0);
            if (ptW <= 0 || ptH <= 0) return;

            // Convert to layout pixels
            double widthLp = ptW * PtToLp;
            double heightLp = ptH * PtToLp;

            // Available space
            const double sidePad = 28.0;
            const double topPad  = 16.0;
            double aw = DocumentScrollViewer.ViewportWidth  - sidePad * 2;
            double ah = DocumentScrollViewer.ViewportHeight - topPad  * 2;
            if (aw <= 0 || ah <= 0) return;

            // Fit both dimensions
            double targetZoom = Math.Min(aw / widthLp, ah / heightLp);
            
            DocGlance.Infrastructure.Logging.Logger.Log($"ApplyFitPage PPTX: widthLp={widthLp}, heightLp={heightLp}, aw={aw}, ah={ah}, targetZoom={targetZoom}");
            
            SetZoom(targetZoom);
            return;
        }

        if (_docxDocument != null)
        {
            const double PtToLp = 96.0 / 72.0;
            var (ptW, ptH) = _docxDocument.GetPageSize(0);
            if (ptW <= 0 || ptH <= 0) return;

            double widthLp = ptW * PtToLp;
            double heightLp = ptH * PtToLp;
            const double sidePad = 28.0;
            const double topPad = 16.0;
            double availableWidth = DocumentScrollViewer.ViewportWidth - sidePad * 2;
            double availableHeight = DocumentScrollViewer.ViewportHeight - topPad * 2;
            if (availableWidth <= 0 || availableHeight <= 0) return;

            SetZoom(Math.Min(availableWidth / widthLp, availableHeight / heightLp));
            return;
        }

        if (_spreadsheetDocument != null)
        {
            SetZoom(SpreadsheetViewerControl.GetFitPageZoom());
        }
    }

    // ── Re-apply active fit on viewport change ────────────────────────────────

    internal void ReapplyFitMode()
    {
        switch (_fitMode)
        {
            case FitMode.FitWidth: ApplyFitWidth(); break;
            case FitMode.FitPage:  ApplyFitPage();  break;
        }
    }

    // ── Ctrl+Wheel zoom ───────────────────────────────────────────────────────

    internal void OnViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(DocumentScrollViewer);
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (!ctrl || point.Properties.MouseWheelDelta == 0) return;

        SetFitMode(FitMode.None);

        // One notch = 120; multiplicative so each step feels the same at any zoom
        double steps  = point.Properties.MouseWheelDelta / 120.0;
        double factor = Math.Pow(1.1, steps);

        double newZoom = Math.Clamp(_zoom * factor,
                                    DocumentScrollViewer.MinZoomFactor,
                                    DocumentScrollViewer.MaxZoomFactor);

        SetZoom(newZoom);
        e.Handled = true;
    }
}
