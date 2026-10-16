namespace DocGlance.Rendering.Pdf.Models;

public class PdfPermissions
{
    public bool CanPrint { get; set; }
    public bool CanModify { get; set; }
    public bool CanCopy { get; set; }
    public bool CanAnnotate { get; set; }
    public bool CanFillForms { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsRestricted => !CanCopy;
    public static PdfPermissions Unrestricted => new() { CanPrint = true, CanModify = true, CanCopy = true, CanAnnotate = true, CanFillForms = true };
}
