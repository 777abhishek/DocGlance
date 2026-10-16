using DocGlance.Core.Models;
using Microsoft.UI.Xaml;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer.Contracts;

public interface IDocumentViewer
{
    FrameworkElement Control { get; }
    Task LoadAsync(DocumentItem document, CancellationToken cancellationToken);
    Task CleanupAsync();
}
