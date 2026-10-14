using DocGlance.Core.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Core.Interfaces;

/// <summary>Scans a folder and returns a list of discovered files.</summary>
public interface IFileScanner
{
    Task<List<DocumentItem>> ScanAsync(string folderPath, CancellationToken ct = default);
}
