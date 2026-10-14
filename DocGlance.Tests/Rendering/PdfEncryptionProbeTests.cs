using DocGlance.Rendering.Pdf;
using System.Text;

namespace DocGlance.Tests.Rendering;

public class PdfEncryptionProbeTests
{
    private static byte[] Bytes(string s) => Encoding.ASCII.GetBytes(s);

    [Fact]
    public void TrailerWithEncrypt_IsDetected()
    {
        var pdf = Bytes("%PDF-1.7\n" + new string('x', 200_000) + "\ntrailer\n<< /Size 9 /Root 1 0 R /Encrypt 8 0 R >>\n%%EOF");
        PdfRenderer.LooksEncrypted(pdf).Should().BeTrue();
    }

    [Fact]
    public void LinearizedHeaderWithEncrypt_IsDetected()
    {
        var pdf = Bytes("%PDF-1.7\n<< /Linearized 1 /Encrypt 5 0 R >>\n" + new string('x', 200_000));
        PdfRenderer.LooksEncrypted(pdf).Should().BeTrue();
    }

    [Fact]
    public void PlainPdf_IsNotFlagged()
    {
        var pdf = Bytes("%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Size 2 /Root 1 0 R >>\n%%EOF");
        PdfRenderer.LooksEncrypted(pdf).Should().BeFalse();
    }

    [Fact]
    public void EncryptMentionedOnlyInTheMiddleOfALargeFile_IsNotFlagged()
    {
        var pdf = Bytes(new string('a', 100_000) + "/Encrypt" + new string('b', 100_000));
        PdfRenderer.LooksEncrypted(pdf).Should().BeFalse();
    }

    [Fact]
    public void EmptyInput_IsNotFlagged()
    {
        PdfRenderer.LooksEncrypted(Array.Empty<byte>()).Should().BeFalse();
    }
}
