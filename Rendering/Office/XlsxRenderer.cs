using Microsoft.UI.Xaml;
using QuickView.Controls;
using QuickView.Core.Interfaces;
using QuickView.Models;
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
}
