using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DocGlance.Core.Models;
using DocGlance.Services.FileSystem;

namespace DocGlance.UI.Controls;

/// <summary>
/// Document card V2: type-coloured gradient + extension badge + watermark, curved info panel.
/// Reuses BookmarkEventArgs from DocumentCard.xaml.cs.
/// </summary>
public sealed partial class DocumentCardV2 : UserControl
{
    private bool _suppressNextOpen;

    public DocumentCardV2() => InitializeComponent();

    public event EventHandler<BookmarkEventArgs>? BookmarkToggled;
    internal event Action<DocumentCardV2>? SelectionToggleRequested;

    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.Register(nameof(Document), typeof(DocumentItem), typeof(DocumentCardV2),
            new PropertyMetadata(null, OnDocumentChanged));

    public DocumentItem? Document
    {
        get => (DocumentItem?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DocumentCardV2),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DocumentCardV2 card)
            card.SelectionToggleButton.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    internal bool IsContextMenuElement(DependencyObject? source)
    {
        while (source != null && !ReferenceEquals(source, this))
        {
            if (ReferenceEquals(source, ContextMenuButton))
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    internal bool IsSelectionToggleElement(DependencyObject? source)
    {
        while (source != null && !ReferenceEquals(source, this))
        {
            if (ReferenceEquals(source, SelectionToggleButton))
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    internal void SuppressNextOpen()
    {
        _suppressNextOpen = true;
    }

    internal void ResetClickSuppression() => _suppressNextOpen = false;

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DocumentCardV2 card) return;
        if (e.NewValue is DocumentItem doc) card.UpdateCard(doc); else card.ClearCard();
    }

    // ── Visuals ───────────────────────────────────────────────────────────────

    private void UpdateCard(DocumentItem doc)
    {
        FileNameText.Text = string.IsNullOrWhiteSpace(doc.Title) ? "Unnamed file" : doc.Title;
        FileSizeText.Text = string.IsNullOrWhiteSpace(doc.FileSize) ? string.Empty : doc.FileSize;
        ModifiedText.Text = FormatDate(doc.LastWriteTime);

        var style = FileTypePalette.Get(doc.FileType);
        FileTypeDesc.Text   = style.Description;
        WatermarkText.Text  = style.Ext.Length > 4 ? style.Ext[..4] : style.Ext;
        GradientStart.Color = style.Start;
        GradientEnd.Color   = style.End;

        ThumbnailImage.Source     = null;
        ThumbnailImage.Visibility = Visibility.Collapsed;
        WatermarkText.Visibility  = Visibility.Visible;
    }

    private void ClearCard()
    {
        FileNameText.Text = FileTypeDesc.Text = FileSizeText.Text = ModifiedText.Text = string.Empty;
        WatermarkText.Text = string.Empty;
        ThumbnailImage.Source = null;
        ThumbnailImage.Visibility = Visibility.Collapsed;
    }

    /// <summary>Call when a real thumbnail is ready: hides the watermark so it doesn't fight the image.</summary>
    public void ShowThumbnail(Microsoft.UI.Xaml.Media.ImageSource source)
    {
        ThumbnailImage.Source = source;
        ThumbnailImage.Visibility = Visibility.Visible;
        WatermarkText.Visibility = Visibility.Collapsed;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void CardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressNextOpen)
        {
            _suppressNextOpen = false;
            return;
        }
        if (Document == null) return;
        global::DocGlance.UI.Shell.TitleBarWindow.Current?.OpenDocument(Document);
    }

    private void SelectionToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressNextOpen = true;
        SelectionToggleRequested?.Invoke(this);
    }

    private void OpenMenuItem_Click(object sender, RoutedEventArgs e) => CardButton_Click(sender, e);

    private void BookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document == null) return;
        BookmarkToggled?.Invoke(this, new BookmarkEventArgs(Document, BookmarkToggle.IsChecked));
    }

    private async void ShowInExplorerMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.FilePath == null) return;
        try
        {
            var file   = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                             System.IO.Path.GetDirectoryName(Document.FilePath)!);
            var opts = new Windows.System.FolderLauncherOptions();
            opts.ItemsToSelect.Add(file);
            await Windows.System.Launcher.LaunchFolderAsync(folder, opts);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Explorer: {ex.Message}"); }
    }

    private async void ShareMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (Document?.FilePath == null) return;
        try
        {
            var pkg  = new Windows.ApplicationModel.DataTransfer.DataPackage();
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            pkg.SetStorageItems(new[] { file });
            pkg.Properties.Title = Document.Title ?? "Document";
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Share: {ex.Message}"); }
    }

    private async void OpenWithDefaultMenuItem_Click(object sender, RoutedEventArgs e) => await LaunchAsync(false);
    private async void OpenWithChooseMenuItem_Click(object sender, RoutedEventArgs e) => await LaunchAsync(true);

    private async System.Threading.Tasks.Task LaunchAsync(bool picker)
    {
        if (Document?.FilePath == null) return;
        if (!ExternalFileSafety.IsAllowed(Document.FilePath)) return;
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Document.FilePath);
            await Windows.System.Launcher.LaunchFileAsync(file,
                new Windows.System.LauncherOptions { DisplayApplicationPicker = picker });
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Launch: {ex.Message}"); }
    }

    private static string FormatDate(DateTime d)
    {
        var now = DateTime.Now;
        var diff = now - d;
        if (diff.TotalSeconds < 0) return "Just now";
        if (d.Date == now.Date) return "Today";
        if (d.Date == now.Date.AddDays(-1)) return "Yesterday";
        if (diff.TotalDays < 7) { int n = Math.Max(1, (int)diff.TotalDays); return $"{n} day{(n == 1 ? "" : "s")} ago"; }
        return d.Year == now.Year ? d.ToString("MMM dd") : d.ToString("MMM dd, yyyy");
    }
}
