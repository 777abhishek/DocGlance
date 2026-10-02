namespace QuickView.Core.Models;

/// <summary>A user-created bookmark — one page within one document.</summary>
public sealed class BookmarkItem
{
    public string FilePath   { get; set; } = string.Empty;
    public int    PageNumber { get; set; }
    public string Label      { get; set; } = string.Empty; // e.g. "Page 12" or user-edited title
    public string CreatedAt  { get; set; } = string.Empty; // ISO-8601 string
}
