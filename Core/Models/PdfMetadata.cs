using System;

namespace QuickView.Core.Models;

public class PdfMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Keywords { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string Producer { get; set; } = string.Empty;
    public DateTime? CreationDate { get; set; }
    public DateTime? ModificationDate { get; set; }
    public bool HasAnyMetadata => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Author) || CreationDate.HasValue;
}
