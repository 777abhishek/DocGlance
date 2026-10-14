using Microsoft.UI.Xaml;
using DocGlance.UI.Controls;
using DocGlance.Core.Interfaces;
using DocGlance.Core.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Rendering.Office;

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
