using System.IO.Compression;
using System.Text;
using DocGlance.Rendering.Document;

namespace DocGlance.Tests.Rendering.Document;

public sealed class DocxOutlineTests
{
    [Fact]
    public void ReadOutline_BuildsNestedHeadingsFromParagraphStyles()
    {
        string path = Path.Combine(Path.GetTempPath(), $"docglance-outline-{Guid.NewGuid():N}.docx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "word/styles.xml", """
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:style w:type="paragraph" w:styleId="Heading1">
                        <w:pPr><w:outlineLvl w:val="0"/></w:pPr>
                      </w:style>
                      <w:style w:type="paragraph" w:styleId="Heading2">
                        <w:pPr><w:outlineLvl w:val="1"/></w:pPr>
                      </w:style>
                    </w:styles>
                    """);
                WriteEntry(archive, "word/document.xml", """
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body>
                        <w:p><w:pPr><w:pStyle w:val="Heading1"/></w:pPr><w:r><w:t>Chapter One</w:t></w:r></w:p>
                        <w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:t>Section A</w:t></w:r></w:p>
                        <w:p><w:pPr><w:pStyle w:val="Normal"/></w:pPr><w:r><w:t>Body text</w:t></w:r></w:p>
                      </w:body>
                    </w:document>
                    """);
            }

            var outline = DocxDocument.ReadOutline(path);
            outline.Should().ContainSingle();
            outline[0].Title.Should().Be("Chapter One");
            outline[0].Level.Should().Be(0);
            outline[0].Children.Should().ContainSingle();
            outline[0].Children[0].Title.Should().Be("Section A");
            outline[0].Children[0].Level.Should().Be(1);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ReadOutline_UsesParagraphOutlineLevelWhenPresent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"docglance-outline-{Guid.NewGuid():N}.docx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "word/document.xml", """
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body>
                        <w:p><w:pPr><w:outlineLvl w:val="2"/></w:pPr><w:r><w:t>Custom heading</w:t></w:r></w:p>
                      </w:body>
                    </w:document>
                    """);
            }

            var outline = DocxDocument.ReadOutline(path);
            outline.Should().ContainSingle();
            outline[0].Title.Should().Be("Custom heading");
            outline[0].Level.Should().Be(2);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string contents)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(contents);
    }
}
