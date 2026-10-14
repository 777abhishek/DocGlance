using Microsoft.UI.Xaml.Media.Imaging;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Core.Interfaces;

/// <summary>Provides a thumbnail image for a file.</summary>
public interface IThumbnailProvider
{
    string[] SupportedExtensions { get; }
    Task<BitmapImage?> GetThumbnailAsync(string filePath, CancellationToken ct = default);
}
