using System;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsAnnotations
{
    event EventHandler<bool>? AnnotationToolChanged;
    bool IsAnnotationToolActive { get; }
    Task FlushPendingAsync();
}
