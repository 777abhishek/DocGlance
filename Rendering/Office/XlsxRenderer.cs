using Microsoft.UI.Xaml;
using QuickView.UI.Controls;
using QuickView.Core.Interfaces;
using QuickView.Core.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Office;

public sealed class XlsxRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".xlsx", ".xls", ".csv"];

    public Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var doc = DocumentItem.FromFileInfo(new FileInfo(filePath));
        return Task.FromResult<UIElement?>(new ViewerInfoCard(doc));
    }

    public Task CleanupAsync(UIElement? element)
    {
        // ViewerInfoCard has no resources to clean up
        return Task.CompletedTask;
    }
}
