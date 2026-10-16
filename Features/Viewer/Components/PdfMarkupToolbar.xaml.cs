using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace DocGlance.Features.Viewer.Components;

public sealed partial class PdfMarkupToolbar : UserControl
{
    public event Action<PdfAnnotationTool?>? ToolSelected;
    public event EventHandler? MarkupSettingsChanged;
    public event EventHandler? UndoRequested;
    public event EventHandler? RedoRequested;

    public PdfMarkupToolbar() => InitializeComponent();

    public uint MarkupColorArgb { get; private set; } = 0xFFFFDC28;
    public float MarkupOpacity => (float)(MarkupOpacitySlider.Value / 100.0);
    public uint MarkupKind { get; private set; }
    public Visibility ToolsVisibility
    {
        get => PdfMarkupTools.Visibility;
        set => PdfMarkupTools.Visibility = value;
    }

    public PdfAnnotationTool? SelectedTool { get; private set; }

    public void SetSelectedTool(PdfAnnotationTool? tool)
    {
        if (SelectedTool == tool)
            return;

        SelectedTool = tool;
        MarkupToolButton.IsChecked = tool == PdfAnnotationTool.Markup;
        PenToolButton.IsChecked = tool == PdfAnnotationTool.Pen;
        EraserToolButton.IsChecked = tool == PdfAnnotationTool.Eraser;
        NoteToolButton.IsChecked = tool == PdfAnnotationTool.Note;
        TextToolButton.IsChecked = tool == PdfAnnotationTool.Text;
        SignatureToolButton.IsChecked = tool == PdfAnnotationTool.Signature;
        ImageToolButton.IsChecked = tool == PdfAnnotationTool.Image;
        ToolSelected?.Invoke(tool);
    }

    public void SetHistoryAvailability(bool canUndo, bool canRedo)
    {
        UndoAnnotationButton.IsEnabled = canUndo;
        RedoAnnotationButton.IsEnabled = canRedo;
    }

    private void MarkupToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(MarkupToolButton.IsChecked == true ? PdfAnnotationTool.Markup : null);

    private void PenToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(PenToolButton.IsChecked == true ? PdfAnnotationTool.Pen : null);

    private void EraserToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(EraserToolButton.IsChecked == true ? PdfAnnotationTool.Eraser : null);

    private void NoteToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(NoteToolButton.IsChecked == true ? PdfAnnotationTool.Note : null);

    private void TextToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(TextToolButton.IsChecked == true ? PdfAnnotationTool.Text : null);

    private void SignatureToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(SignatureToolButton.IsChecked == true ? PdfAnnotationTool.Signature : null);

    private void ImageToolButton_Click(object sender, RoutedEventArgs e) =>
        SetSelectedTool(ImageToolButton.IsChecked == true ? PdfAnnotationTool.Image : null);

    private void UndoAnnotationButton_Click(object sender, RoutedEventArgs e) =>
        UndoRequested?.Invoke(this, EventArgs.Empty);

    private void RedoAnnotationButton_Click(object sender, RoutedEventArgs e) =>
        RedoRequested?.Invoke(this, EventArgs.Empty);

    private void MarkupStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && uint.TryParse(tag, out uint kind))
        {
            MarkupKind = kind;
            MarkupSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MarkupColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string value } &&
            uint.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint argb))
        {
            MarkupColorArgb = argb;
            MarkupSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MarkupOpacitySlider_ValueChanged(
        object sender,
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) =>
        MarkupSettingsChanged?.Invoke(this, EventArgs.Empty);
}
