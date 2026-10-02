using QuickView.Core.Extensions;
using System.IO;

namespace QuickView.Tests.Shared.Validation;

public class FileSizeValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public FileSizeValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QuickViewTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    [Fact]
    public void IsFileSizeAcceptable_SmallTextFile_ReturnsTrue()
    {
        var path = CreateFile("test.txt", 1024); // 1 KB
        FileSizeValidator.IsFileSizeAcceptable(path, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void IsFileSizeAcceptable_NonExistentFile_ReturnsFalse()
    {
        FileSizeValidator.IsFileSizeAcceptable(@"C:\does\not\exist.txt", out var error)
            .Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void IsFileSizeAcceptable_SmallPdf_ReturnsTrue()
    {
        var path = CreateFile("test.pdf", 1024 * 1024); // 1 MB
        FileSizeValidator.IsFileSizeAcceptable(path, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void IsFileSizeAcceptable_SmallDocx_ReturnsTrue()
    {
        var path = CreateFile("test.docx", 512 * 1024); // 512 KB
        FileSizeValidator.IsFileSizeAcceptable(path, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }
}
