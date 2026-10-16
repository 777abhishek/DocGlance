using Microsoft.UI.Xaml.Controls;

namespace DocGlance.Features.Viewer.Formats.Pdf;

public sealed partial class PdfViewer : UserControl
{
    public PdfViewer()
    {
        InitializeComponent();
    }

    public ScrollViewer DocumentScrollViewerControl => DocumentScrollViewer;
    public Canvas PdfViewportCanvasControl => PdfViewportCanvas;
    public Image ViewportImageControl => ViewportImage;
    public Canvas SearchHighlightCanvasControl => SearchHighlightCanvas;
    public Canvas PdfAnnotationCanvasControl => PdfAnnotationCanvas;
    public Canvas PdfSelectionCanvasControl => PdfSelectionCanvas;
}
