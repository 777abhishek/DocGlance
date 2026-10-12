using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using QuickView.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI;

namespace QuickView.Features.Viewer.TextSelection;

/// <summary>
/// Transparent overlay canvas that handles mouse drag text selection.
/// Works with ITextSelectionProvider to support PDF, DOCX, PPTX, and XLSX.
/// 
/// Interaction flow:
/// 1. PointerPressed → Capture pointer, record start position & char index
/// 2. PointerMoved → Update drag rectangle, update selection preview
/// 3. PointerReleased → Show highlight rectangles, enable context menu
/// 4. RightTapped → Show Copy menu if selection exists
/// </summary>
public sealed partial class TextSelectionOverlay : Canvas
{
    private ITextSelectionProvider? _provider;
    private Point? _startPoint;
    private Point? _currentPoint;
    private int? _startCharIndex;
    private int? _endCharIndex;
    private TextSelection? _currentSelection;
    private readonly List<Rectangle> _highlightRectangles = new();
    private bool _isDragging;

    public event EventHandler<TextSelection>? TextSelected;

    /// <summary>
    /// Gets the current text selection provider.
    /// </summary>
    public ITextSelectionProvider? Provider => _provider;

    public TextSelectionOverlay()
    {
        InitializeComponent();
        // Use IBeam cursor to indicate text selection is available
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.IBeam);
    }

    /// <summary>
    /// Set the text selection provider for the current document type.
    /// </summary>
    public void SetProvider(ITextSelectionProvider? provider)
    {
        ClearSelection();
        _provider = provider;
        IsHitTestVisible = provider != null;
    }

    /// <summary>
    /// Clear all selection state and visual highlights.
    /// </summary>
    public void ClearSelection()
    {
        _startPoint = null;
        _currentPoint = null;
        _startCharIndex = null;
        _endCharIndex = null;
        _currentSelection = null;
        _isDragging = false;
        DragRectangle.Visibility = Visibility.Collapsed;

        foreach (var rect in _highlightRectangles)
            Children.Remove(rect);
        _highlightRectangles.Clear();
    }

    // ── Mouse Interaction ─────────────────────────────────────────────────────

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_provider == null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        // Clear previous selection
        ClearSelection();

        // Capture pointer for drag tracking
        _startPoint = e.GetCurrentPoint(this).Position;
        _startCharIndex = _provider.GetCharIndexAtPosition(_startPoint.Value);

        if (_startCharIndex.HasValue)
        {
            _isDragging = true;
            CapturePointer(e.Pointer);
            Logger.Log($"[TextSelection] Start at char {_startCharIndex.Value}");
        }

        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _provider == null || !_startPoint.HasValue) return;

        _currentPoint = e.GetCurrentPoint(this).Position;
        _endCharIndex = _provider.GetCharIndexAtPosition(_currentPoint.Value);

        // Update drag rectangle
        UpdateDragRectangle(_startPoint.Value, _currentPoint.Value);

        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        ReleasePointerCapture(e.Pointer);
        _isDragging = false;
        DragRectangle.Visibility = Visibility.Collapsed;

        CompleteSelection();
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;

        ReleasePointerCapture(e.Pointer);
        ClearSelection();
        e.Handled = true;
    }

    // ── Selection Completion ──────────────────────────────────────────────────

    private void CompleteSelection()
    {
        if (_provider == null || !_startCharIndex.HasValue || !_endCharIndex.HasValue)
        {
            ClearSelection();
            return;
        }

        // Normalize indices (start <= end)
        int start = Math.Min(_startCharIndex.Value, _endCharIndex.Value);
        int end = Math.Max(_startCharIndex.Value, _endCharIndex.Value);

        // Require at least one character selected
        if (start == end)
        {
            ClearSelection();
            return;
        }

        try
        {
            // Get selection rectangles and text from provider
            var rects = _provider.GetSelectionRectangles(start, end);
            var text = _provider.ExtractText(start, end);

            if (string.IsNullOrWhiteSpace(text) || rects.Count == 0)
            {
                ClearSelection();
                return;
            }

            _currentSelection = new TextSelection(start, end, text, rects);

            // Draw highlight rectangles
            DrawHighlightRectangles(rects);

            Logger.Log($"[TextSelection] Selected {text.Length} chars: \"{text.Substring(0, Math.Min(50, text.Length))}...\"");

            // Notify subscribers
            TextSelected?.Invoke(this, _currentSelection);
        }
        catch (Exception ex)
        {
            Logger.Error("[TextSelection] CompleteSelection failed", ex);
            ClearSelection();
        }
    }

    // ── Visual Rendering ──────────────────────────────────────────────────────

    private void UpdateDragRectangle(Point start, Point current)
    {
        double x = Math.Min(start.X, current.X);
        double y = Math.Min(start.Y, current.Y);
        double width = Math.Abs(current.X - start.X);
        double height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(DragRectangle, x);
        Canvas.SetTop(DragRectangle, y);
        DragRectangle.Width = Math.Max(2, width);
        DragRectangle.Height = Math.Max(2, height);
        DragRectangle.Visibility = Visibility.Visible;
    }

    private void DrawHighlightRectangles(IReadOnlyList<Rect> rects)
    {
        // Clear previous highlights
        foreach (var rect in _highlightRectangles)
            Children.Remove(rect);
        _highlightRectangles.Clear();

        // Draw new highlight rectangles
        foreach (var rect in rects)
        {
            var highlight = new Rectangle
            {
                Width = rect.Width,
                Height = rect.Height,
                Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 0)), // Semi-transparent yellow
                Stroke = new SolidColorBrush(Color.FromArgb(120, 255, 200, 0)),
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2,
                IsHitTestVisible = false
            };

            Canvas.SetLeft(highlight, rect.X);
            Canvas.SetTop(highlight, rect.Y);
            Children.Add(highlight);
            _highlightRectangles.Add(highlight);
        }
    }

    // ── Context Menu ──────────────────────────────────────────────────────────

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_currentSelection != null && !string.IsNullOrWhiteSpace(_currentSelection.Text))
        {
            SelectionMenu.ShowAt(this, e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (_currentSelection == null) return;

        try
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(_currentSelection.Text);
            Clipboard.SetContent(dataPackage);
            Logger.Log($"[TextSelection] Copied {_currentSelection.Text.Length} chars to clipboard");

            // Clear selection after copy
            ClearSelection();
        }
        catch (Exception ex)
        {
            Logger.Error("[TextSelection] Copy to clipboard failed", ex);
        }
    }
}
