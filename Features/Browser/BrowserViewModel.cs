using QuickView.Models;
using System.Collections.ObjectModel;

namespace QuickView.Features.Browser;

/// <summary>
/// ViewModel for BrowserPage — manages the current folder path and its contents.
/// </summary>
public sealed class BrowserViewModel
{
    public ObservableCollection<DocumentItem> Items       { get; } = new();
    public string                             CurrentPath { get; private set; } = string.Empty;

    public void Navigate(string folderPath)
    {
        CurrentPath = folderPath;
        Items.Clear();
        // TODO: enumerate folder contents via FileSystem.Scanner
    }
}
