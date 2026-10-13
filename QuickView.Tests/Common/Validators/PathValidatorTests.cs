using QuickView.Common.Validators;
using System.IO;

namespace QuickView.Tests.Common.Validators;

public class PathValidatorTests : IDisposable
{
    private readonly string _tempDir;

    public PathValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QuickViewPathTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void RegularFile_IsValid()
    {
        var path = Path.Combine(_tempDir, "a.txt");
        File.WriteAllText(path, "hi");

        PathValidator.IsValidFilePath(path, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        PathValidator.IsValidFilePath(Path.Combine(_tempDir, "nope.txt"), out var error).Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrBlank_IsRejected(string? path)
    {
        PathValidator.IsValidFilePath(path, out _).Should().BeFalse();
    }

    [Fact]
    public void UncPath_IsRejected()
    {
        PathValidator.IsValidFilePath(@"\\server\share\file.pdf", out var error).Should().BeFalse();
        error.Should().Contain("UNC");
    }

    [Fact]
    public void SymbolicLinkToFile_IsRejected_WhenSymlinksCanBeCreated()
    {
        var target = Path.Combine(_tempDir, "real.txt");
        File.WriteAllText(target, "x");
        var link = Path.Combine(_tempDir, "link.txt");

        try { File.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return; // creating symlinks needs developer mode / elevation; nothing to assert here
        }

        PathValidator.IsValidFilePath(link, out var error).Should().BeFalse();
        error.Should().Contain("Symbolic links");
    }
}
