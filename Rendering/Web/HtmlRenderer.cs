using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Web;

public sealed class HtmlRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".html", ".htm"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        var wv = new WebView2();
        wv.Source = new System.Uri(filePath);
        return wv;
    }
}
