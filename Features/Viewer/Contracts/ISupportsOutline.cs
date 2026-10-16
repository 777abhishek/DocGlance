using System.Collections.Generic;
using DocGlance.Core.Models;

namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsOutline
{
    IReadOnlyList<OutlineItem> Outline { get; }
    void NavigateTo(OutlineItem item);
}
