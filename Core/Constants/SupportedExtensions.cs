using System;
using System.Collections.Generic;
using System.Linq;

namespace QuickView.Core.Constants;

/// <summary>
/// Single source of truth for the file types QuickView opens. File-association registration
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
        ".sql", ".md", ".markdown",
    };

    public static readonly IReadOnlyList<string> Office = new[]
    {
        ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".pptm", ".ppsx",
    };

    /// <summary>Every supported extension, in registration order.</summary>
    public static readonly IReadOnlyList<string> All =
        Pdf.Concat(Text).Concat(Office).ToArray();

    private static readonly HashSet<string> TextSet = new(Text, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> OfficeSet = new(Office, StringComparer.OrdinalIgnoreCase);

    public static bool IsPdf(string extension) =>
        extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public static bool IsText(string extension) => TextSet.Contains(extension);

    public static bool IsOffice(string extension) => OfficeSet.Contains(extension);
}
