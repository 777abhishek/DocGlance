using Microsoft.UI.Xaml;
using QuickView.Models;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Core.Interfaces;

/// <summary>Renders a file into a UIElement for display in the ViewerPage canvas.</summary>
public interface IFileRenderer
{
    /// <summary>File extensions this renderer handles, e.g. ".pdf", ".docx".</summary>
    string[] SupportedExtensions { get; }

    /// <summary>Render the file at <paramref name="filePath"/> and return the root UI element.</summary>
    Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default);
}
