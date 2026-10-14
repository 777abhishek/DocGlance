using DocGlance.Rendering.Pdf;

namespace DocGlance.Services.Documents;

/// <summary>Result of LoadDocumentAsync operation.</summary>
public sealed record PdfLoadResult(
    PdfDocumentState? Document,
    int PageCount,
    string? ErrorMessage,
    bool Success)
{
    public static PdfLoadResult Ok(PdfDocumentState doc, int pageCount)
        => new(doc, pageCount, null, true);

    public static PdfLoadResult Error(string message)
        => new(null, 0, message, false);
}
