using System.Collections.Generic;

namespace QuickView.Models;

/// <summary>One node in a PDF outline / table of contents tree.</summary>
public sealed class OutlineItem
{
    public string            Title    { get; set; } = string.Empty;
    public int               Page     { get; set; } // 1-based; 0 = unknown
    public int               Level    { get; set; } // 0 = top-level
    public List<OutlineItem> Children { get; set; } = new();
}
