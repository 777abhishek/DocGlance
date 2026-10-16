using System.Collections.Generic;
using DocGlance.Core.Models;

namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsThumbnails
{
    IReadOnlyList<PageThumbnailItem> Thumbnails { get; }
}
