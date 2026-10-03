using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using QuickView.Rendering.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace QuickView.Rendering.Code;

/// <summary>
/// Syntax-highlighted code file renderer with monospace font and line wrapping option.
/// Supports: C#, JS, TS, Python, Java, C++, Go, Rust, HTML, CSS, JSON, XML, SQL, etc.
/// Uses efficient paragraph-per-line rendering for better performance with large files.
/// </summary>
public static class CodeRenderer
{
    private const int MaxChars = 500_000; // 500 KB display limit
    private const int MaxLines = 2_000;   // above this, caller falls back to plain TextBlock

    public class CodeRenderResult
    {
        public List<Paragraph> Paragraphs { get; set; } = new();
        public FontFamily FontFamily { get; set; } = new FontFamily("Cascadia Code");
        public double FontSize { get; set; } = 13.5;
        public double LineHeight { get; set; } = 22;
        public TextWrapping TextWrapping { get; set; } = TextWrapping.NoWrap;
        public Thickness Padding { get; set; } = new Thickness(16, 12, 16, 12);
        /// <summary>True when the file exceeded MaxLines — caller should offer plain-text fallback.</summary>
        public bool ExceededLineLimit { get; set; }
    }

    public static async Task<CodeRenderResult> RenderAsync(string filePath, bool isDarkTheme, CancellationToken ct = default)
    {
        var loaded = await Task.Run(() => Text.TextFileReader.ReadCapped(filePath, MaxChars), ct);
        string text = loaded.Text;
        bool truncatedBySize = loaded.Truncated;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        var result = new CodeRenderResult
        {
            FontFamily   = new FontFamily("Cascadia Code, Cascadia Mono, Consolas, Courier New"),
            FontSize     = 13.5,
            LineHeight   = 22,
            TextWrapping = TextWrapping.NoWrap,
            Padding      = new Thickness(16, 12, 16, 12),
        };

        // Split into lines and render line-by-line — ONE Run per line.
        // VS Code / Monaco pattern: tokenise for dominant line color but keep the
        // entire line as a single text node. Search highlight is a separate layer
        // that splits Runs at match boundaries — it only works cleanly with 1 Run/line.
        var lines = text.Split('\n');
        bool truncatedByLines = lines.Length > MaxLines;
        int lineCount = Math.Min(lines.Length, MaxLines);

        result.ExceededLineLimit = truncatedByLines;

        for (int lineNum = 0; lineNum < lineCount; lineNum++)
        {
            string line = lines[lineNum];
            var para = new Paragraph { LineHeight = 22, Margin = new Thickness(0) };

            // Determine dominant color for this line from first meaningful token
            var color = GetLineDominantColor(line, ext, isDarkTheme);

            para.Inlines.Add(new Run
            {
                Text       = line,
                Foreground = new SolidColorBrush(color),
            });

            result.Paragraphs.Add(para);
        }

        // Only show truncation warning for size limit — line limit triggers
        // a plain-text fallback in the caller before this result is displayed.
        if (truncatedBySize)
        {
            var warningPara = new Paragraph { LineHeight = 22, Margin = new Thickness(0, 16, 0, 0) };
            string msg = $"File truncated — showing first {MaxChars / 1000} KB of {loaded.FileBytes / 1000} KB";
            warningPara.Inlines.Add(new Run
            {
                Text       = msg,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 140, 60)),
                FontStyle  = Windows.UI.Text.FontStyle.Italic,
                FontWeight = FontWeights.SemiBold,
            });
            result.Paragraphs.Add(warningPara);
        }

        return result;
    }

    /// <summary>
    /// Returns a single color for the whole line based on the first meaningful token.
    /// Rules (in priority order):
    ///   comment line  → comment green
    ///   string-only   → string orange
    ///   starts with keyword → keyword blue
    ///   otherwise     → default text color
    /// This is fast (no full tokenisation) and gives readable coloring with 1 Run/line.
    /// </summary>
    private static Color GetLineDominantColor(string line, string ext, bool dark)
    {
        string trimmed = line.TrimStart();

        // Comment lines
        bool isShell = ext is ".sh" or ".bash" or ".zsh" or ".ps1" or ".bat" or ".cmd";
        bool isPy    = ext is ".py" or ".pyw" or ".r";
        bool isXml   = ext is ".xml" or ".xaml" or ".html" or ".htm" or ".svg" or ".cshtml";

        if (trimmed.StartsWith("//") || trimmed.StartsWith("--") ||
            trimmed.StartsWith("/*") || trimmed.StartsWith("*") ||
            (isShell || isPy) && trimmed.StartsWith("#") ||
            isXml && trimmed.StartsWith("<!--"))
            return Tokeniser.GetColor(TokenKind.Comment, dark);

        // String-only lines (quoted values, JSON strings)
        if (trimmed.Length > 1 &&
            ((trimmed[0] == '"' || trimmed[0] == '\'') ))
            return Tokeniser.GetColor(TokenKind.String, dark);

        // Lines that start with a keyword
        var keywords = GetFirstWordKeywords(ext);
        int wordEnd  = 0;
        while (wordEnd < trimmed.Length && (char.IsLetterOrDigit(trimmed[wordEnd]) || trimmed[wordEnd] == '_'))
            wordEnd++;
        if (wordEnd > 0 && keywords.Contains(trimmed[..wordEnd]))
            return Tokeniser.GetColor(TokenKind.Keyword, dark);

        return Tokeniser.GetColor(TokenKind.Normal, dark);
    }

    // Minimal keyword set for first-word check (fast path only)
    private static HashSet<string> GetFirstWordKeywords(string ext) => ext switch
    {
        ".cs" or ".csx" => new HashSet<string> {
            "using","namespace","class","interface","enum","struct","record",
            "public","private","protected","internal","static","abstract","sealed",
            "override","virtual","readonly","const","void","return","if","else",
            "for","foreach","while","do","switch","case","break","continue",
            "try","catch","finally","throw","new","var","async","await" },
        ".js" or ".mjs" or ".ts" or ".tsx" or ".jsx" => new HashSet<string> {
            "import","export","from","const","let","var","function","class",
            "return","if","else","for","while","switch","case","break",
            "try","catch","finally","throw","new","async","await","type","interface" },
        ".py" or ".pyw" => new HashSet<string> {
            "import","from","def","class","return","if","elif","else",
            "for","while","try","except","finally","raise","with","async","await",
            "pass","break","continue","yield","lambda","global","nonlocal" },
        ".html" or ".htm" or ".xml" or ".xaml" => new HashSet<string> {
            "<!DOCTYPE","<html","<head","<body","<div","<span","<p","<a",
            "<Grid","<StackPanel","<TextBlock","<Button","<Border","<Page" },
        ".css" or ".scss" or ".less" => new HashSet<string> {
            "@import","@media","@keyframes","@mixin","@include","@extend" },
        ".go" => new HashSet<string> {
            "package","import","func","type","struct","interface","var","const",
            "return","if","else","for","switch","case","break","go","defer" },
        ".rs" => new HashSet<string> {
            "use","mod","fn","struct","enum","impl","trait","pub","let","const",
            "return","if","else","for","while","match","async","await" },
        ".java" or ".kt" => new HashSet<string> {
            "import","package","class","interface","enum","public","private",
            "protected","static","void","return","if","else","for","while",
            "try","catch","finally","throw","new","abstract","final" },
        _ => new HashSet<string>(),
    };
}

// ── Token types (minimal set used by GetLineDominantColor) ────────────────────

internal enum TokenKind { Normal, Keyword, String, Comment }

internal static class Tokeniser
{
    public static Color GetColor(TokenKind kind, bool dark) => kind switch
    {
        TokenKind.Keyword  => dark ? Color.FromArgb(255, 86,  156, 214) : Color.FromArgb(255,   0,   0, 255),  // Blue
        TokenKind.String   => dark ? Color.FromArgb(255, 206, 145,  80) : Color.FromArgb(255, 163,  21,  21),  // Orange/Brown
        TokenKind.Comment  => dark ? Color.FromArgb(255, 106, 153,  85) : Color.FromArgb(255,   0, 128,   0),  // Green
        _                  => dark ? Color.FromArgb(255, 220, 220, 220) : Color.FromArgb(255,  30,  30,  30),  // Default text
    };
}
