using Microsoft.UI.Xaml;
using DocGlance.UI.Controls;
using DocGlance.Core.Interfaces;
using DocGlance.Core.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Rendering.Office;

public sealed class DocxRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".docx", ".doc"];

    public Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var fi  = new FileInfo(filePath);
        var doc = DocumentItem.FromFileInfo(fi);
        UIElement card = new ViewerInfoCard(doc);
        return Task.FromResult<UIElement?>(card);
    }

    public Task CleanupAsync(UIElement? element)
    {
        // ViewerInfoCard has no resources to clean up
        return Task.CompletedTask;
    }
}
