using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Features.Viewer.Contracts;

public interface ISupportsSearch
{
    Task<SearchResult[]> SearchAsync(string query, CancellationToken cancellationToken);
    void Highlight(SearchResult result);
    void ClearHighlight();
}

public sealed record SearchResult(string Text, IReadOnlyList<SearchLocation> Locations);

public abstract record SearchLocation;

public sealed record TextRangeLocation(int Start, int Length) : SearchLocation;

public sealed record PageRegionLocation(
    int PageIndex,
    double X,
    double Y,
    double Width,
    double Height) : SearchLocation;

public sealed record SpreadsheetCellLocation(int SheetIndex, int Row, int Column) : SearchLocation;
