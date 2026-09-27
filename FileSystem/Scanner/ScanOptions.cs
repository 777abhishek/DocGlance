using System.Collections.Generic;

namespace QuickView.FileSystem.Scanner;

/// <summary>Configuration for a folder scan operation.</summary>
public sealed class ScanOptions
{
    /// <summary>Root folder paths to scan.</summary>
    public List<string> FolderPaths { get; set; } = new();

    /// <summary>When true, subdirectories are scanned recursively.</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>Skip files/folders with Hidden or System attributes.</summary>
    public bool SkipHidden { get; set; } = true;

    /// <summary>
    /// Maximum parallel degree. Defaults to <c>ProcessorCount / 2</c>
    /// to leave headroom for UI and disk I/O.
    /// </summary>
    public int MaxDegreeOfParallelism { get; set; } =
        System.Environment.ProcessorCount / 2;
}
