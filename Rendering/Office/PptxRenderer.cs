using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.UI.Controls;
using QuickView.Core.Interfaces;
using QuickView.Core.Models;
using QuickView.Rendering.Presentation;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Office;

public sealed class PptxRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".pptx", ".ppt", ".pptm", ".ppsx"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        // Debug: Write to file so we can see what's happening
        var logPath = Path.Combine(Path.GetTempPath(), "quickview_pptx_debug.txt");
        
        try
        {
            File.AppendAllText(logPath, $"\n\n=== RENDER START === {DateTime.Now}\n");
            File.AppendAllText(logPath, $"File: {filePath}\n");
            
            // Don't render PPTX in a custom control - let ViewerPage handle it like PDF
            // Return null so ViewerPage.LoadViaRendererAsync knows to handle it specially
            return null;
        }
        catch (Exception ex)
        {
            File.AppendAllText(logPath, $"EXCEPTION: {ex.Message}\n");
            return new ViewerInfoCard(DocumentItem.FromFileInfo(new FileInfo(filePath)));
        }
    }

    public Task CleanupAsync(UIElement? element)
    {
        // Dispose the PptxDocument when the control is unloaded
        if (element is PptxViewerControl control)
        {
            control.Dispose();
        }
        return Task.CompletedTask;
    }
}
