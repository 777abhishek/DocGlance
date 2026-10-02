using Microsoft.UI.Xaml;
using QuickView.UI.Controls;
using QuickView.Core.Interfaces;
using QuickView.Core.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Office;

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
}
