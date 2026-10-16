using System.Collections.Generic;

namespace DocGlance.Features.Viewer.Contracts;

public interface IDocumentViewerFactory
{
    IReadOnlyCollection<string> SupportedExtensions { get; }
    IDocumentViewer Create(ViewerViewModel viewModel);
}
