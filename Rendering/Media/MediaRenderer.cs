using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Core.Interfaces;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Media;

public sealed class MediaRenderer : IFileRenderer
{
    public string[] SupportedExtensions =>
        [".mp4", ".mkv", ".avi", ".mov", ".wmv", ".mp3", ".wav", ".flac", ".aac"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        await Task.CompletedTask;
        var player = new MediaPlayerElement
        {
            AutoPlay = false,
            AreTransportControlsEnabled = true,
        };
        player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new System.Uri(filePath));
        return player;
    }
}
