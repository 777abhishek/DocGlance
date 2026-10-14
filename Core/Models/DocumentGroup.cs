// DocumentGroup.cs — Represents a group of documents with a header

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DocGlance.Core.Models;

/// <summary>
/// Represents a grouped collection of documents with a group header.
/// Used for displaying documents grouped by folder, file type, or date.
/// </summary>
public class DocumentGroup
{
    public string GroupName { get; set; }
    public ObservableCollection<DocumentItem> Items { get; set; }
    public int ItemCount => Items.Count;

    public DocumentGroup(string groupName, IEnumerable<DocumentItem> items)
    {
        GroupName = groupName;
        Items = new ObservableCollection<DocumentItem>(items);
    }
}
