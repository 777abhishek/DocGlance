using DocGlance.Services.FileSystem.Scanner;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DocGlance.Tests.Services.FileSystem.Scanner;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), $"docglance_scan_{Guid.NewGuid():N}");

    public FolderScannerTests()
    {
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public async Task ScanDirectoryAsync_FindsNestedSupportedFilesAndReflectsDeletion()
    {
        string nestedPath = Directory.CreateDirectory(Path.Combine(_rootPath, "Nested")).FullName;
        string rootPdf = Path.Combine(_rootPath, "root.pdf");
        string nestedPdf = Path.Combine(nestedPath, "nested.pdf");
        string unsupported = Path.Combine(nestedPath, "notes.exe");
        File.WriteAllText(rootPdf, "pdf");
        File.WriteAllText(nestedPdf, "pdf");
        File.WriteAllText(unsupported, "not indexed");

        FolderScanResult firstScan = await FolderScanner.ScanDirectoryAsync(_rootPath);

        Assert.True(firstScan.IsComplete);
        Assert.Equal(2, firstScan.Items.Count);
        Assert.Contains(firstScan.Items, item =>
            string.Equals(item.FilePath, rootPdf, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(firstScan.Items, item =>
            string.Equals(item.FilePath, nestedPdf, StringComparison.OrdinalIgnoreCase));

        File.Delete(nestedPdf);
        FolderScanResult secondScan = await FolderScanner.ScanDirectoryAsync(_rootPath);

        Assert.True(secondScan.IsComplete);
        Assert.DoesNotContain(secondScan.Items, item =>
            string.Equals(item.FilePath, nestedPdf, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ScanDirectoryAsync_ReportsDiscoveredFilesInBatches()
    {
        for (int i = 0; i < 130; i++)
            File.WriteAllText(Path.Combine(_rootPath, $"{i:D3}.txt"), "text");
        var batches = new List<IReadOnlyList<DocGlance.Core.Models.DocumentItem>>();

        FolderScanResult result = await FolderScanner.ScanDirectoryAsync(
            _rootPath, batch => batches.Add(batch));

        Assert.True(result.IsComplete);
        Assert.Equal(130, result.Items.Count);
        Assert.Equal(130, batches.Sum(batch => batch.Count));
        Assert.All(batches.Take(batches.Count - 1), batch => Assert.Equal(64, batch.Count));
        Assert.InRange(batches[^1].Count, 1, 64);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }
}
