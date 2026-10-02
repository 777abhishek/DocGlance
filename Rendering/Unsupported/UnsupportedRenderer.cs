using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.UI.Controls;
using QuickView.Core.Interfaces;
using QuickView.Core.Models;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Unsupported;

public sealed class UnsupportedRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [];

    public Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var doc = DocumentItem.FromFileInfo(new FileInfo(filePath));
        return Task.FromResult<UIElement?>(new ViewerInfoCard(doc));
    }
}
