using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using QuickView.Rendering.Document;
using QuickView.Rendering.Presentation;
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

    public ToolsPage()
    {
        System.Diagnostics.Debug.WriteLine("ToolsPage constructor called");
        InitializeComponent();
        this.Loaded += ToolsPage_Loaded;
    }

    private void ToolsPage_Loaded(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("ToolsPage Loaded event fired");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // NAVIGATION
    // ═══════════════════════════════════════════════════════════════════════════

    private void ToolsList_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        if (ToolsList.SelectedItem is not Microsoft.UI.Xaml.Controls.ListViewItem item)
            return;

        var tag = item.Tag?.ToString();

        // Hide all panels
        WordToPdfPanel.Visibility = Visibility.Collapsed;
        PptToPdfPanel.Visibility = Visibility.Collapsed;
        ExcelToCsvPanel.Visibility = Visibility.Collapsed;

        // Show selected panel
        switch (tag)
        {
            case "WordToPdf":
                WordToPdfPanel.Visibility = Visibility.Visible;
                break;
            case "PptToPdf":
                PptToPdfPanel.Visibility = Visibility.Visible;
                break;
            case "ExcelToCsv":
                ExcelToCsvPanel.Visibility = Visibility.Visible;
                break;
        }
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
        if (string.IsNullOrEmpty(_wordOutputFile))
        {
            _wordOutputFile = Path.ChangeExtension(file.Path, ".pdf");
            WordOutputPath.Text = _wordOutputFile;
        }

        ConvertWordButton.IsEnabled = !string.IsNullOrEmpty(_wordInputFile) && !string.IsNullOrEmpty(_wordOutputFile);
    }

    private async void BrowseWordOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        
        if (!string.IsNullOrEmpty(_wordInputFile))
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_wordInputFile) + ".pdf";
        
        picker.FileTypeChoices.Add("PDF Document", new[] { ".pdf" });

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

            var pdfBytes = await doc.ExportToPdfAsync();
            if (pdfBytes == null || pdfBytes.Length == 0)
            {
                WordStatus.Text = "❌ Conversion failed";
                return;
            }

            await File.WriteAllBytesAsync(_wordOutputFile, pdfBytes);
            WordStatus.Text = "✓ Converted successfully";
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
        if (string.IsNullOrEmpty(_pptOutputFile))
        {
            _pptOutputFile = Path.ChangeExtension(file.Path, ".pdf");
            PptOutputPath.Text = _pptOutputFile;
        }

        ConvertPptButton.IsEnabled = !string.IsNullOrEmpty(_pptInputFile) && !string.IsNullOrEmpty(_pptOutputFile);
    }

    private async void BrowsePptOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        
        if (!string.IsNullOrEmpty(_pptInputFile))
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_pptInputFile) + ".pdf";
        
        picker.FileTypeChoices.Add("PDF Document", new[] { ".pdf" });

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

            PptStatus.Text = "❌ PowerPoint to PDF export is not supported";
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
        if (string.IsNullOrEmpty(_excelOutputFile))
        {
            _excelOutputFile = Path.ChangeExtension(file.Path, ".csv");
            ExcelOutputPath.Text = _excelOutputFile;
        }

        ConvertExcelButton.IsEnabled = !string.IsNullOrEmpty(_excelInputFile) && !string.IsNullOrEmpty(_excelOutputFile);
    }

    private async void BrowseExcelOutput_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, GetWindowHandle());
        
        if (!string.IsNullOrEmpty(_excelInputFile))
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_excelInputFile) + ".csv";
        
        picker.FileTypeChoices.Add("CSV File", new[] { ".csv" });

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
            // TODO: Implement Excel→CSV using GridCraft FFI
            await Task.Delay(1000); // Simulate work
            ExcelStatus.Text = "❌ Not implemented yet";
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
}
