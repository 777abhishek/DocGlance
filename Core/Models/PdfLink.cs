using System.Collections.Generic;

namespace QuickView.Core.Models;

/// <summary>
/// Represents a clickable link in a PDF document (web URL, email, or internal page reference).
/// </summary>
public sealed class PdfLink
{
    public PdfLinkType Type { get; init; }
    public string Url { get; init; } = string.Empty;
    public int? TargetPageIndex { get; init; }
    public List<PdfLinkRect> BoundingRects { get; init; } = new();
}

public enum PdfLinkType
{
    WebUrl,      // External web link (http://, https://)
    Email,       // Email link (mailto:)
    InternalPage // Internal page navigation
}

/// <summary>
/// Bounding rectangle for a link (in device coordinates: pixels).
/// </summary>
public readonly record struct PdfLinkRect(double X, double Y, double Width, double Height);
