using System.IO.Compression;
using System.Text;
using DocGlance.Rendering.Shared;

namespace DocGlance.Tests.Rendering.Shared;

public sealed class OfficeArchiveBudgetTests
{
    [Fact]
    public void Validate_RejectsTooManyEntries()
    {
        using var stream = CreateArchive(("first.xml", "a"), ("second.xml", "b"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Action validate = () => OfficeArchiveBudget.Validate(
            archive, maximumEntries: 1, maximumEntryBytes: 10, maximumTotalExpandedBytes: 10);

        validate.Should().Throw<InvalidDataException>().WithMessage("*more than 1 entries*");
    }

    [Fact]
    public void Validate_RejectsAnOversizedEntry()
    {
        using var stream = CreateArchive(("document.xml", "12345"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Action validate = () => OfficeArchiveBudget.Validate(
            archive, maximumEntries: 2, maximumEntryBytes: 4, maximumTotalExpandedBytes: 10);

        validate.Should().Throw<InvalidDataException>().WithMessage("*document.xml*");
    }

    [Fact]
    public void Validate_RejectsExpandedArchiveOverTotalBudget()
    {
        using var stream = CreateArchive(("one.xml", "123"), ("two.xml", "456"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Action validate = () => OfficeArchiveBudget.Validate(
            archive, maximumEntries: 2, maximumEntryBytes: 4, maximumTotalExpandedBytes: 5);

        validate.Should().Throw<InvalidDataException>().WithMessage("*expanded size*");
    }

    private static MemoryStream CreateArchive(params (string Name, string Contents)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, string contents) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(contents);
            }
        }
        stream.Position = 0;
        return stream;
    }
}
