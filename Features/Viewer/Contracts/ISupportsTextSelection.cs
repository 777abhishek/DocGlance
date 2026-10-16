using System;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsTextSelection
{
    event EventHandler? SelectionChanged;
    Task<string?> GetSelectedTextAsync();
    Task CopySelectionAsync();
}
