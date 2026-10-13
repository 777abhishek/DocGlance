using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using QuickView.Rendering.Document;
using QuickView.Rendering.Presentation;
using QuickView.Infrastructure.Logging;
using QuickView.Rendering.Pdf;
using WinRT.Interop;

namespace QuickView.Features.Tools;

public sealed class PdfMergeFileItem
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public int OrderIndex { get; set; }
}

public sealed partial class ToolsPage : Page
{
    private string? _wordInputFile;
    private string? _wordOutputFile;
    private string? _pptInputFile;
    private string? _pptOutputFile;
    private string? _excelInputFile;
    private string? _excelOutputFile;
    private string? _pdfInputFile;
    private string? _pdfOutputFile;
    private readonly ObservableCollection<PdfMergeFileItem> _pdfMergeFiles = new();
    private PdfDocumentState? _pdfSplitPreviewDocument;
    private readonly List<PdfSplitPageItem> _pdfSplitPages = new();
    private string _selectedTool = "WordToPdf";

    private sealed class PdfSplitPageItem
    {
        public required string Label { get; init; }
        public required WriteableBitmap Preview { get; init; }
        public int PageIndex { get; init; }
    }

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
        if (WordToPdfPanel is null || PptToPdfPanel is null || ExcelToCsvPanel is null || PdfToolsPanel is null)
        {
            Logger.Log("[ToolsPage] Ignoring early tool selection before panels were initialized.");
            return;
        }

        // Hide all panels
        WordToPdfPanel.Visibility = Visibility.Collapsed;
        PptToPdfPanel.Visibility = Visibility.Collapsed;
        ExcelToCsvPanel.Visibility = Visibility.Collapsed;
        PdfToolsPanel.Visibility = Visibility.Collapsed;

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
            case "PdfMerge":
            case "PdfSplit":
            case "PdfToPng":
                PdfToolsPanel.Visibility = Visibility.Visible;
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
        bool pdfMerge = _selectedTool == "PdfMerge";
        bool pdfSplit = _selectedTool == "PdfSplit";
        bool pdfPng = _selectedTool == "PdfToPng";

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

        PdfToolTitle.Text = pdfMerge ? "Merge PDFs" : pdfSplit ? "Split PDF" : "PDF → PNG (ZIP)";
        PdfToolDescription.Text = pdfMerge
            ? "Combine multiple PDF files into one PDF using PdfCraft."
            : pdfSplit
                ? "Split a PDF into one PDF file per page using PdfCraft."
                : "Export every PDF page as a PNG inside a ZIP archive using PdfCraft.";
        PdfInputLabel.Text = pdfMerge ? "PDF files to merge" : "Input PDF";
        PdfInputPath.PlaceholderText = pdfMerge ? "Select the first PDF file..." : "Select PDF file...";
        PdfMergeFilesPanel.Visibility = pdfMerge ? Visibility.Visible : Visibility.Collapsed;
        PdfSplitPagesGrid.Visibility = pdfSplit ? Visibility.Visible : Visibility.Collapsed;
        PdfSplitSelectionPanel.Visibility = pdfSplit ? Visibility.Visible : Visibility.Collapsed;
        PdfOutputLabel.Text = "Output file";
        PdfOutputPath.PlaceholderText = pdfMerge ? "Merged PDF will be saved here..." :
            pdfSplit ? "ZIP of selected pages will be saved here..." : "PNG ZIP will be saved here...";
        ProcessPdfButton.Content = pdfMerge ? "Merge PDFs" : pdfSplit ? "Split PDF" : "Export PNG ZIP";
        UpdatePdfProcessButton();
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

    private async void BrowsePdfInput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeFilter.Add(".pdf");

        if (_selectedTool == "PdfMerge")
        {
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                if (!_pdfMergeFiles.Any(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                {
                    _pdfMergeFiles.Add(new PdfMergeFileItem
                    {
                        Path = file.Path,
                        FileName = file.Name,
                        OrderIndex = _pdfMergeFiles.Count + 1
                    });
                }
            }
            RefreshPdfMergeFiles();
            if (_pdfMergeFiles.Count > 0)
                _pdfInputFile = _pdfMergeFiles[0].Path;
        }
        else
        {
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            _pdfInputFile = file.Path;
            PdfInputPath.Text = file.Path;
            if (_selectedTool == "PdfSplit")
            {
                PdfOutputPath.Text = Path.ChangeExtension(file.Path, ".split.zip");
                await LoadPdfSplitPreviewsAsync(file.Path);
            }
            else
                PdfOutputPath.Text = Path.ChangeExtension(file.Path, ".png.zip");
            _pdfOutputFile = PdfOutputPath.Text;
        }

        UpdatePdfProcessButton();
    }

    private async void AddPdfMergeFile_Click(object sender, RoutedEventArgs e)
    {
        await BrowsePdfInputAsync();
    }

    private async Task BrowsePdfInputAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeFilter.Add(".pdf");
        var files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            if (!_pdfMergeFiles.Any(f => f.Path.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
            {
                _pdfMergeFiles.Add(new PdfMergeFileItem
                {
                    Path = file.Path,
                    FileName = file.Name,
                    OrderIndex = _pdfMergeFiles.Count + 1
                });
            }
        }
        RefreshPdfMergeFiles();
        if (_pdfMergeFiles.Count > 0)
            _pdfInputFile = _pdfMergeFiles[0].Path;
        UpdatePdfProcessButton();
    }

    private void RefreshPdfMergeFiles()
    {
        PdfInputPath.Text = _pdfMergeFiles.Count == 0
            ? string.Empty
            : $"{_pdfMergeFiles.Count} PDF file(s) selected";
        UpdatePdfProcessButton();
    }

    private void RemovePdfMergeFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is PdfMergeFileItem item)
        {
            _pdfMergeFiles.Remove(item);
            // Re-index remaining items
            for (int i = 0; i < _pdfMergeFiles.Count; i++)
            {
                _pdfMergeFiles[i].OrderIndex = i + 1;
            }
            RefreshPdfMergeFiles();
        }
    }

    private void PdfMergeFiles_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        // Re-index all items after drag-and-drop reorder
        for (int i = 0; i < _pdfMergeFiles.Count; i++)
        {
            _pdfMergeFiles[i].OrderIndex = i + 1;
        }
    }

    private async void BrowsePdfOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        picker.FileTypeChoices.Add(_selectedTool == "PdfMerge" ? "PDF document" : "ZIP archive",
            _selectedTool == "PdfMerge" ? new[] { ".pdf" } : new[] { ".zip" });
        picker.SuggestedFileName = _selectedTool == "PdfMerge" ? "merged.pdf" :
            _selectedTool == "PdfSplit" ? "pdf-pages.zip" : "pdf-pages.png.zip";
        var file = await picker.PickSaveFileAsync();
        if (file == null) return;
        _pdfOutputFile = file.Path;

        PdfOutputPath.Text = _pdfOutputFile;
        UpdatePdfProcessButton();
    }

    private async void ProcessPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!CanProcessPdf()) return;

        ProcessPdfButton.IsEnabled = false;
        PdfProgress.IsActive = true;
        PdfProgress.Visibility = Visibility.Visible;
        PdfStatus.Text = "Processing...";
        SetOutputActionsEnabled(OpenPdfOutputButton, ShowPdfOutputButton, CopyPdfOutputButton, false);

        try
        {
            if (_selectedTool == "PdfMerge")
                await MergePdfsAsync();
            else if (_selectedTool == "PdfSplit")
                await SplitPdfAsync();
            else
                await ExportPdfPngZipAsync();

            PdfStatus.Text = "Completed successfully";
            SetOutputActionsEnabled(OpenPdfOutputButton, ShowPdfOutputButton, CopyPdfOutputButton,
                _selectedTool != "PdfSplit");
        }
        catch (Exception ex)
        {
            PdfStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            PdfProgress.IsActive = false;
            PdfProgress.Visibility = Visibility.Collapsed;
            UpdatePdfProcessButton();
        }
    }

    private async Task MergePdfsAsync()
    {
        string paths = string.Join(";", _pdfMergeFiles.Select(f => f.Path));
        IntPtr pathBuffer = Marshal.StringToCoTaskMemUTF8(paths);
        try
        {
            var buffer = await Task.Run(() => PdfCraftNative.pc_merge_pdfs(pathBuffer, (nuint)Encoding.UTF8.GetByteCount(paths)));
            try
            {
                if (buffer.Data == IntPtr.Zero || buffer.Len == 0)
                    throw new InvalidOperationException("PdfCraft could not merge the selected PDFs.");
                if (buffer.Len > int.MaxValue)
                    throw new InvalidOperationException("PdfCraft returned an oversized PDF.");
                var bytes = new byte[(int)buffer.Len];
                Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
                await File.WriteAllBytesAsync(_pdfOutputFile!, bytes);
            }
            finally
            {
                PdfCraftNative.pc_free_pdf_buffer(buffer);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pathBuffer);
        }
    }

    private async Task SplitPdfAsync()
    {
        var selected = PdfSplitPagesGrid.SelectedItems
            .OfType<PdfSplitPageItem>()
            .OrderBy(page => page.PageIndex)
            .ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("Select at least one page to split.");

        byte[] bytes = await File.ReadAllBytesAsync(_pdfInputFile!);
        var opened = await PdfRenderer.OpenDocumentFromBytesAsync(bytes);
        if (!opened.Ok)
            throw new InvalidOperationException(opened.Error ?? "PdfCraft could not open the PDF.");
        using var document = opened.Document!;
        string outputDirectory = Path.Combine(Path.GetTempPath(), "QuickView", "pdf-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        IntPtr outputDir = Marshal.StringToCoTaskMemUTF8(outputDirectory);
        string indices = string.Join(",", selected.Select(page => page.PageIndex));
        IntPtr pageIndices = Marshal.StringToCoTaskMemUTF8(indices);
        try
        {
            uint count = await Task.Run(() => PdfCraftNative.pc_split_pdf_selected(
                document.Handle, pageIndices, (nuint)Encoding.UTF8.GetByteCount(indices),
                outputDir, (nuint)Encoding.UTF8.GetByteCount(outputDirectory)));
            if (count == 0)
                throw new InvalidOperationException("PdfCraft could not split the PDF.");
            await using (var output = File.Create(_pdfOutputFile!))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                foreach (var pageFile in Directory.EnumerateFiles(outputDirectory, "*.pdf").OrderBy(path => path))
                {
                    var entry = archive.CreateEntry(Path.GetFileName(pageFile), CompressionLevel.Fastest);
                    await using var entryStream = entry.Open();
                    await using var pageStream = File.OpenRead(pageFile);
                    await pageStream.CopyToAsync(entryStream);
                }
            }
            PdfStatus.Text = $"Created {count} selected page PDF file(s)";
        }
        finally
        {
            Marshal.FreeCoTaskMem(pageIndices);
            Marshal.FreeCoTaskMem(outputDir);
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private async Task ExportPdfPngZipAsync()
    {
        byte[] bytes = await File.ReadAllBytesAsync(_pdfInputFile!);
        var opened = await PdfRenderer.OpenDocumentFromBytesAsync(bytes);
        if (!opened.Ok)
            throw new InvalidOperationException(opened.Error ?? "PdfCraft could not open the PDF.");
        using var document = opened.Document!;
        await using var output = File.Create(_pdfOutputFile!);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        for (uint page = 0; page < document.PageCount; page++)
        {
            var png = await Task.Run(() => PdfCraftNative.pc_export_to_png(document.Handle, page, 144));
            try
            {
                if (png.Data == IntPtr.Zero || png.Len == 0)
                    throw new InvalidOperationException($"PdfCraft failed to export page {page + 1}.");
                if (png.Len > int.MaxValue)
                    throw new InvalidOperationException("PdfCraft returned an oversized PNG.");
                var bytesForPage = new byte[(int)png.Len];
                Marshal.Copy(png.Data, bytesForPage, 0, bytesForPage.Length);
                var entry = archive.CreateEntry($"page-{page + 1:000}.png", CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(bytesForPage);
            }
            finally
            {
                PdfCraftNative.pc_free_png_buffer(png);
            }
        }
    }

    private async Task LoadPdfSplitPreviewsAsync(string path)
    {
        PdfSplitPagesGrid.ItemsSource = null;
        _pdfSplitPages.Clear();
        _pdfSplitPreviewDocument?.Dispose();
        _pdfSplitPreviewDocument = null;
        PdfSplitSelectionStatus.Text = "Loading previews...";

        var opened = await PdfRenderer.OpenDocumentAsync(path);
        if (!opened.Ok)
        {
            PdfStatus.Text = opened.Error ?? "Could not open PDF.";
            PdfSplitSelectionStatus.Text = string.Empty;
            return;
        }

        _pdfSplitPreviewDocument = opened.Document;
        for (var page = 0; page < _pdfSplitPreviewDocument!.PageCount; page++)
        {
            var preview = await PdfRenderer.RenderPageAsync(
                _pdfSplitPreviewDocument, page, PdfRenderer.ThumbnailDpi);
            if (preview != null)
            {
                _pdfSplitPages.Add(new PdfSplitPageItem
                {
                    PageIndex = page,
                    Label = $"Page {page + 1}",
                    Preview = preview
                });
            }
        }

        PdfSplitPagesGrid.ItemsSource = _pdfSplitPages;
        PdfSplitSelectionStatus.Text = $"{_pdfSplitPages.Count} pages";
        UpdatePdfProcessButton();
    }

    private void SelectAllPdfPages_Click(object sender, RoutedEventArgs e)
    {
        if (_pdfSplitPages.Count > 0)
            PdfSplitPagesGrid.SelectRange(new ItemIndexRange(0, (uint)_pdfSplitPages.Count));
        UpdatePdfProcessButton();
    }

    private void ClearPdfPages_Click(object sender, RoutedEventArgs e)
    {
        PdfSplitPagesGrid.SelectedItems.Clear();
        UpdatePdfProcessButton();
    }

    private void PdfSplitPagesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        PdfSplitSelectionStatus.Text = $"{PdfSplitPagesGrid.SelectedItems.Count} selected";
        UpdatePdfProcessButton();
    }

    private bool CanProcessPdf()
        => _selectedTool == "PdfMerge"
            ? _pdfMergeFiles.Count >= 2 && !string.IsNullOrWhiteSpace(_pdfOutputFile)
            : !string.IsNullOrWhiteSpace(_pdfInputFile) &&
              !string.IsNullOrWhiteSpace(_pdfOutputFile) &&
              (_selectedTool != "PdfSplit" || PdfSplitPagesGrid.SelectedItems.Count > 0);

    private void UpdatePdfProcessButton()
        => ProcessPdfButton.IsEnabled = CanProcessPdf();

    private void RemoveMergeFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is PdfMergeFileItem item)
        {
            _pdfMergeFiles.Remove(item);
            RefreshPdfMergeFiles();
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
    private async void OpenPdfOutput_Click(object sender, RoutedEventArgs e) => await OpenOutputAsync(_pdfOutputFile);
    private async void ShowPdfOutput_Click(object sender, RoutedEventArgs e) => await ShowOutputFolderAsync(_pdfOutputFile);
    private void CopyPdfOutput_Click(object sender, RoutedEventArgs e) => CopyOutputPath(_pdfOutputFile);

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
