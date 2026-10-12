using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO.Compression;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using QuickView.Rendering.Document;
using QuickView.Rendering.Presentation;
using QuickView.Infrastructure.Logging;
using WinRT.Interop;

namespace QuickView.Features.Tools;

public sealed partial class ToolsPage : Page
{
    private string? _wordInputFile;
    private string? _wordOutputFile;
    private string? _pptInputFile;
    private string? _pptOutputFile;
    private string? _excelInputFile;
    private string? _excelOutputFile;
    private string _selectedTool = "WordToPdf";

    public ToolsPage()
    {
        Logger.Log("[ToolsPage] Constructor started.");
        InitializeComponent();
        this.Loaded += ToolsPage_Loaded;
        Logger.Log("[ToolsPage] XAML initialized and Loaded handler attached.");
    }

    private void ToolsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (ToolsList.SelectedIndex < 0)
            ToolsList.SelectedIndex = 0;

        var tag = (ToolsList.SelectedItem as ListViewItem)?.Tag?.ToString();
        _selectedTool = tag ?? "WordToPdf";
        if (WordToolTitle is not null)
            UpdateToolPresentation();
        Logger.Log($"[ToolsPage] Loaded. SelectedIndex={ToolsList.SelectedIndex}, SelectedTool={tag ?? "<none>"}");
        ShowToolPanel(tag);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // NAVIGATION
    // ═══════════════════════════════════════════════════════════════════════════

    private void ToolsList_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        var tag = (ToolsList.SelectedItem as ListViewItem)?.Tag?.ToString();
        _selectedTool = tag ?? "WordToPdf";
        Logger.Log($"[ToolsPage] Tool selection changed: {tag ?? "<none>"}");
        ShowToolPanel(tag);
    }

    private void ShowToolPanel(string? tag)
    {
        // SelectionChanged can fire while InitializeComponent is still
        // creating the named panel controls.
        if (WordToPdfPanel is null || PptToPdfPanel is null || ExcelToCsvPanel is null)
        {
            Logger.Log("[ToolsPage] Ignoring early tool selection before panels were initialized.");
            return;
        }

        // Hide all panels
        WordToPdfPanel.Visibility = Visibility.Collapsed;
        PptToPdfPanel.Visibility = Visibility.Collapsed;
        ExcelToCsvPanel.Visibility = Visibility.Collapsed;

        // Show selected panel
        switch (tag)
        {
            case "WordToPdf":
            case "WordToPng":
                WordToPdfPanel.Visibility = Visibility.Visible;
                break;
            case "PptToPdf":
            case "PptToPng":
                PptToPdfPanel.Visibility = Visibility.Visible;
                break;
            case "ExcelToCsv":
            case "ExcelToPdf":
                ExcelToCsvPanel.Visibility = Visibility.Visible;
                break;
        }

        UpdateToolPresentation();
    }

    private void UpdateToolPresentation()
    {
        if (WordToolTitle is null || PptToolTitle is null || ExcelToolTitle is null)
            return;

        bool wordPng = _selectedTool == "WordToPng";
        bool pptPng = _selectedTool == "PptToPng";
        bool excelPdf = _selectedTool == "ExcelToPdf";

        WordToolTitle.Text = wordPng ? "Word → PNG (ZIP)" : "Word → PDF";
        WordToolDescription.Text = wordPng
            ? "Export every Word document page as a PNG inside a ZIP archive."
            : "Convert Word documents to PDF format using WordCraft.";
        ConvertWordButton.Content = wordPng ? "Export PNG ZIP" : "Convert to PDF";
        WordOutputPath.PlaceholderText = wordPng ? "PNG ZIP will be saved here..." : "PDF will be saved here...";

        PptToolTitle.Text = pptPng ? "PowerPoint → PNG (ZIP)" : "PowerPoint → PDF";
        PptToolDescription.Text = pptPng
            ? "Export every presentation slide as a PNG inside a ZIP archive."
            : "PowerPoint to PDF export is not supported by DeckCraft yet.";
        ConvertPptButton.Content = pptPng ? "Export PNG ZIP" : "PDF export unavailable";
        ConvertPptButton.IsEnabled = pptPng && !string.IsNullOrEmpty(_pptInputFile) && !string.IsNullOrEmpty(_pptOutputFile);
        PptOutputPath.PlaceholderText = pptPng ? "PNG ZIP will be saved here..." : "PDF export is unavailable...";

        ExcelToolTitle.Text = excelPdf ? "Excel → PDF" : "Excel → CSV";
        ExcelToolDescription.Text = excelPdf
            ? "Export the workbook as a PDF using GridCraft."
            : "Export workbook sheet data as a CSV file using GridCraft.";
        ConvertExcelButton.Content = excelPdf ? "Export as PDF" : "Export as CSV";
        ExcelOutputPath.PlaceholderText = excelPdf ? "PDF will be saved here..." : "CSV will be saved here...";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // POWERPOINT → PDF
    // ═══════════════════════════════════════════════════════════════════════════

    private async void BrowseWordInput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeFilter.Add(".docx");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        _wordInputFile = file.Path;
        WordInputPath.Text = file.Path;
        
        // Auto-suggest output path
        _wordOutputFile = Path.ChangeExtension(file.Path, _selectedTool == "WordToPng" ? ".zip" : ".pdf");
        WordOutputPath.Text = _wordOutputFile;

        ConvertWordButton.IsEnabled = !string.IsNullOrEmpty(_wordInputFile) && !string.IsNullOrEmpty(_wordOutputFile);
    }

    private async void BrowseWordOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        
        if (!string.IsNullOrEmpty(_wordInputFile))
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_wordInputFile) +
                (_selectedTool == "WordToPng" ? ".zip" : ".pdf");
        
        var png = _selectedTool == "WordToPng";
        picker.FileTypeChoices.Add(png ? "PNG Images (ZIP)" : "PDF Document", new[] { png ? ".zip" : ".pdf" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        _wordOutputFile = file.Path;
        WordOutputPath.Text = file.Path;

        ConvertWordButton.IsEnabled = !string.IsNullOrEmpty(_wordInputFile) && !string.IsNullOrEmpty(_wordOutputFile);
    }

    private async void ConvertWord_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_wordInputFile) || string.IsNullOrEmpty(_wordOutputFile))
            return;

        ConvertWordButton.IsEnabled = false;
        WordProgress.IsActive = true;
        WordProgress.Visibility = Visibility.Visible;
        WordStatus.Text = "Converting...";

        try
        {
            var doc = await DocxDocument.OpenAsync(_wordInputFile);
            if (doc == null)
            {
                WordStatus.Text = "❌ Failed to open Word document";
                return;
            }

            if (_selectedTool == "WordToPng")
            {
                await ExportWordPngZipAsync(doc, _wordOutputFile);
            }
            else
            {
                var pdfBytes = await doc.ExportToPdfAsync();
                if (pdfBytes == null || pdfBytes.Length == 0) { WordStatus.Text = "Conversion failed"; return; }
                await File.WriteAllBytesAsync(_wordOutputFile, pdfBytes);
            }
            WordStatus.Text = "✓ Converted successfully";
            SetOutputActionsEnabled(OpenWordOutputButton, ShowWordOutputButton, CopyWordOutputButton, true);
        }
        catch (Exception ex)
        {
            WordStatus.Text = $"❌ Error: {ex.Message}";
        }
        finally
        {
            WordProgress.IsActive = false;
            WordProgress.Visibility = Visibility.Collapsed;
            ConvertWordButton.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EXCEL → CSV
    // ═══════════════════════════════════════════════════════════════════════════

    private async void BrowsePptInput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeFilter.Add(".pptx");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        _pptInputFile = file.Path;
        PptInputPath.Text = file.Path;
        
        // Auto-suggest output path
        _pptOutputFile = Path.ChangeExtension(file.Path, _selectedTool == "PptToPng" ? ".zip" : ".pdf");
        PptOutputPath.Text = _pptOutputFile;

        ConvertPptButton.IsEnabled = !string.IsNullOrEmpty(_pptInputFile) && !string.IsNullOrEmpty(_pptOutputFile);
    }

    private async void BrowsePptOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        
        if (!string.IsNullOrEmpty(_pptInputFile))
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_pptInputFile) +
                (_selectedTool == "PptToPng" ? ".zip" : ".pdf");
        
        var png = _selectedTool == "PptToPng";
        picker.FileTypeChoices.Add(png ? "PNG Images (ZIP)" : "PDF Document", new[] { png ? ".zip" : ".pdf" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        _pptOutputFile = file.Path;
        PptOutputPath.Text = file.Path;

        ConvertPptButton.IsEnabled = !string.IsNullOrEmpty(_pptInputFile) && !string.IsNullOrEmpty(_pptOutputFile);
    }

    private async void ConvertPpt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_pptInputFile) || string.IsNullOrEmpty(_pptOutputFile))
            return;

        ConvertPptButton.IsEnabled = false;
        PptProgress.IsActive = true;
        PptProgress.Visibility = Visibility.Visible;
        PptStatus.Text = "Converting...";

        try
        {
            using var pres = await PptxDocument.OpenAsync(_pptInputFile);
            if (pres == null)
            {
                PptStatus.Text = "❌ Failed to open PowerPoint file";
                return;
            }

            if (_selectedTool == "PptToPng")
            {
                await ExportPptPngZipAsync(pres, _pptOutputFile);
                PptStatus.Text = "✓ Converted successfully";
                SetOutputActionsEnabled(OpenPptOutputButton, ShowPptOutputButton, CopyPptOutputButton, true);
            }
            else
                PptStatus.Text = "PowerPoint to PDF export is not supported";
        }
        catch (Exception ex)
        {
            PptStatus.Text = $"❌ Error: {ex.Message}";
        }
        finally
        {
            PptProgress.IsActive = false;
            PptProgress.Visibility = Visibility.Collapsed;
            ConvertPptButton.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EXCEL → CSV
    // ═══════════════════════════════════════════════════════════════════════════

    private async void BrowseExcelInput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeFilter.Add(".xlsx");

        var file = await picker.PickSingleFileAsync();
        if (file == null) return;

        _excelInputFile = file.Path;
        ExcelInputPath.Text = file.Path;
        
        // Auto-suggest output path
        _excelOutputFile = Path.ChangeExtension(file.Path, _selectedTool == "ExcelToPdf" ? ".pdf" : ".csv");
        ExcelOutputPath.Text = _excelOutputFile;

        ConvertExcelButton.IsEnabled = !string.IsNullOrEmpty(_excelInputFile) && !string.IsNullOrEmpty(_excelOutputFile);
    }

    private async void BrowseExcelOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        
        if (!string.IsNullOrEmpty(_excelInputFile))
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_excelInputFile) +
                (_selectedTool == "ExcelToPdf" ? ".pdf" : ".csv");
        
        var pdf = _selectedTool == "ExcelToPdf";
        picker.FileTypeChoices.Add(pdf ? "PDF Document" : "CSV File", new[] { pdf ? ".pdf" : ".csv" });

        var file = await picker.PickSaveFileAsync();
        if (file == null) return;

        _excelOutputFile = file.Path;
        ExcelOutputPath.Text = file.Path;

        ConvertExcelButton.IsEnabled = !string.IsNullOrEmpty(_excelInputFile) && !string.IsNullOrEmpty(_excelOutputFile);
    }

    private async void ConvertExcel_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_excelInputFile) || string.IsNullOrEmpty(_excelOutputFile))
            return;

        ConvertExcelButton.IsEnabled = false;
        ExcelProgress.IsActive = true;
        ExcelProgress.Visibility = Visibility.Visible;
        ExcelStatus.Text = "Converting...";

        try
        {
            using var workbook = await SpreadsheetDocument.OpenAsync(_excelInputFile);
            if (workbook == null) { ExcelStatus.Text = "Failed to open spreadsheet"; return; }
            if (_selectedTool == "ExcelToPdf")
            {
                var pdf = await workbook.ExportToPdfAsync();
                if (pdf == null) { ExcelStatus.Text = "PDF export failed"; return; }
                await File.WriteAllBytesAsync(_excelOutputFile, pdf);
            }
            else
            {
                var csv = new StringBuilder();
                for (var sheet = 0; sheet < workbook.SheetCount; sheet++)
                {
                    if (sheet > 0) csv.AppendLine();
                    csv.AppendLine($"[{workbook.GetSheetName(sheet)}]");
                    csv.AppendLine(workbook.ExtractSheetText(sheet).Replace("\r\n", "\n"));
                }
                await File.WriteAllTextAsync(_excelOutputFile, csv.ToString());
            }
            ExcelStatus.Text = "Converted successfully";
            SetOutputActionsEnabled(OpenExcelOutputButton, ShowExcelOutputButton, CopyExcelOutputButton, true);
        }
        catch (Exception ex)
        {
            ExcelStatus.Text = $"❌ Error: {ex.Message}";
        }
        finally
        {
            ExcelProgress.IsActive = false;
            ExcelProgress.Visibility = Visibility.Collapsed;
            ConvertExcelButton.IsEnabled = true;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private IntPtr GetWindowHandle()
    {
        var window = UI.Shell.TitleBarWindow.Current;
        return window?.Hwnd ?? IntPtr.Zero;
    }

    private static void SetOutputActionsEnabled(Button open, Button folder, Button copy, bool enabled)
    {
        open.IsEnabled = enabled;
        folder.IsEnabled = enabled;
        copy.IsEnabled = enabled;
    }

    private async void OpenWordOutput_Click(object sender, RoutedEventArgs e) => await OpenOutputAsync(_wordOutputFile);
    private async void ShowWordOutput_Click(object sender, RoutedEventArgs e) => await ShowOutputFolderAsync(_wordOutputFile);
    private void CopyWordOutput_Click(object sender, RoutedEventArgs e) => CopyOutputPath(_wordOutputFile);
    private async void OpenPptOutput_Click(object sender, RoutedEventArgs e) => await OpenOutputAsync(_pptOutputFile);
    private async void ShowPptOutput_Click(object sender, RoutedEventArgs e) => await ShowOutputFolderAsync(_pptOutputFile);
    private void CopyPptOutput_Click(object sender, RoutedEventArgs e) => CopyOutputPath(_pptOutputFile);
    private async void OpenExcelOutput_Click(object sender, RoutedEventArgs e) => await OpenOutputAsync(_excelOutputFile);
    private async void ShowExcelOutput_Click(object sender, RoutedEventArgs e) => await ShowOutputFolderAsync(_excelOutputFile);
    private void CopyExcelOutput_Click(object sender, RoutedEventArgs e) => CopyOutputPath(_excelOutputFile);

    private static async Task OpenOutputAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path));
    }

    private static async Task ShowOutputFolderAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var file = await StorageFile.GetFileFromPathAsync(path);
        var folder = await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(path)!);
        var options = new FolderLauncherOptions();
        options.ItemsToSelect.Add(file);
        await Launcher.LaunchFolderAsync(folder, options);
    }

    private static void CopyOutputPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var package = new DataPackage();
        package.SetText(path);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static async Task ExportWordPngZipAsync(DocxDocument document, string outputPath)
    {
        await using var output = File.Create(outputPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        for (var page = 0; page < document.PageCount; page++)
        {
            var bitmap = await document.RenderPageAsync(page, 2.0);
            if (bitmap == null)
                throw new InvalidOperationException($"Failed to render Word page {page + 1}.");

            var png = await EncodeBitmapAsPngAsync(bitmap);
            var entry = archive.CreateEntry($"page-{page + 1:000}.png", CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(png);
        }
    }

    private static async Task ExportPptPngZipAsync(PptxDocument presentation, string outputPath)
    {
        await using var output = File.Create(outputPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        for (var slide = 0; slide < presentation.SlideCount; slide++)
        {
            var bitmap = await presentation.RenderSlideAsync(slide, 2.0);
            if (bitmap == null)
                throw new InvalidOperationException($"Failed to render PowerPoint slide {slide + 1}.");

            var png = await EncodeBitmapAsPngAsync(bitmap);
            var entry = archive.CreateEntry($"slide-{slide + 1:000}.png", CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(png);
        }
    }

    private static async Task<byte[]> EncodeBitmapAsPngAsync(
        Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        using var pixels = bitmap.PixelBuffer.AsStream();
        var bytes = new byte[pixels.Length];
        await pixels.ReadAsync(bytes, 0, bytes.Length);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var encoded = stream.AsStreamForRead();
        using var result = new MemoryStream();
        await encoded.CopyToAsync(result);
        return result.ToArray();
    }
}
