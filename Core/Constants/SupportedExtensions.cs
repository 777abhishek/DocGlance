using System;
using System.Collections.Generic;
using System.Linq;

namespace DocGlance.Core.Constants;

/// <summary>
/// Single source of truth for the file types DocGlance opens. File-association registration
/// and size-limit selection are derived from these lists.
/// Package.appxmanifest cannot reference code; keep its file-type list in sync with <see cref="All"/>.
/// </summary>
public static class SupportedExtensions
{
    public static readonly IReadOnlyList<string> Pdf = new[] { ".pdf" };

    /// <summary>Plain text, config, markup and source-code files (rendered as capped text).</summary>
    public static readonly IReadOnlyList<string> Text = new[]
    {
        ".txt", ".log", ".csv", ".tsv", ".ini", ".cfg", ".conf",
        ".env", ".toml", ".yaml", ".yml", ".properties",
        ".gitignore", ".editorconfig", ".rtf",
        ".cs", ".vb", ".fs", ".js", ".mjs", ".ts", ".tsx", ".jsx",
        ".py", ".java", ".kt", ".cpp", ".c", ".h", ".go", ".rs",
        ".swift", ".rb", ".php", ".sh", ".ps1", ".bat",
        ".html", ".htm", ".css", ".scss", ".json", ".xml", ".xaml",
        ".sql",
    };

    public static readonly IReadOnlyList<string> Markdown = new[]
    {
        ".md", ".markdown", ".mdx", ".mmd",
    };

    public static readonly IReadOnlyList<string> Office = new[]
    {
        ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pptm", ".ppsx",
    };

    public static readonly IReadOnlyList<string> Image = new[]
    {
        ".bmp", ".dds", ".gif", ".hdp", ".ico", ".jpeg", ".jpg", ".jxr",
        ".png", ".svg", ".svgz", ".tif", ".tiff", ".wdp",
    };

    /// <summary>Every supported extension, in registration order.</summary>
    public static readonly IReadOnlyList<string> All =
        Pdf.Concat(Text).Concat(Markdown).Concat(Office).Concat(Image).ToArray();

    private static readonly HashSet<string> TextSet = new(Text, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MarkdownSet = new(Markdown, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OfficeSet = new(Office, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ImageSet = new(Image, StringComparer.OrdinalIgnoreCase);

    public static bool IsPdf(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool IsText(string extension) => TextSet.Contains(extension);

    public static bool IsMarkdown(string extension) => MarkdownSet.Contains(extension);

    public static bool IsOffice(string extension) => OfficeSet.Contains(extension);

    public static bool IsImage(string extension) => ImageSet.Contains(extension);
}
