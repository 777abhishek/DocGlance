using QuickView.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Core.Interfaces;

/// <summary>Scans a folder and returns a list of discovered files.</summary>
public interface IFileScanner
{
    Task<List<DocumentItem>> ScanAsync(string folderPath, CancellationToken ct = default);
}
