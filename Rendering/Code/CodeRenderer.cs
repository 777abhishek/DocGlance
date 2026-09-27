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
    private const int MaxLines = 10_000;  // Max lines to render

    public class CodeRenderResult
    {
        public List<Paragraph> Paragraphs { get; set; } = new();
        public FontFamily FontFamily { get; set; } = new FontFamily("Cascadia Code");
        public double FontSize { get; set; } = 13.5;
        public double LineHeight { get; set; } = 22;
        public TextWrapping TextWrapping { get; set; } = TextWrapping.NoWrap;
        public Thickness Padding { get; set; } = new Thickness(16, 12, 16, 12);
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

        // Truncation warning
        if (truncatedBySize || truncatedByLines)
        {
            var warningPara = new Paragraph { LineHeight = 22, Margin = new Thickness(0, 16, 0, 0) };
            string msg = truncatedBySize 
                ? $"File truncated — showing first {MaxChars / 1000} KB of {loaded.FileBytes / 1000} KB"
                : $"File truncated — showing first {MaxLines:N0} lines of {lines.Length:N0} lines";
            
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

// ── Token types ────────────────────────────────────────────────────────────────

internal enum TokenKind { Normal, Keyword, String, Comment, Number, Operator, Punctuation, Type }

// ── Improved syntax tokeniser ──────────────────────────────────────────────────

internal static class Tokeniser
{
    /// <summary>
    /// Tokenize a single line of code (line-by-line is faster for large files)
    /// </summary>
    public static List<(string Text, TokenKind Kind)> TokeniseLine(string line, string ext)
    {
        var result = new List<(string, TokenKind)>(line.Length / 4);
        bool isShell = ext is ".sh" or ".bash" or ".zsh" or ".ps1" or ".bat" or ".cmd";
        bool isPython = ext is ".py" or ".pyw";
        bool isJson = ext is ".json" or ".jsonc" or ".json5";
        bool isXml = ext is ".xml" or ".xaml" or ".html" or ".htm" or ".svg" or ".cshtml" or ".razor";

        var keywords = GetKeywords(ext);
        var types = GetBuiltInTypes(ext);

        int i = 0;
        while (i < line.Length)
        {
            // Line comment: // or # (shell/python/ruby)
            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '/')
            {
                result.Add((line[i..], TokenKind.Comment));
                break;
            }
            if ((isPython || isShell) && line[i] == '#')
            {
                result.Add((line[i..], TokenKind.Comment));
                break;
            }

            // Block comment start /* (note: doesn't track multi-line state, simple version)
            if (i + 1 < line.Length && line[i] == '/' && line[i + 1] == '*')
            {
                int end = line.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) end = line.Length - 2; else end += 2;
                result.Add((line[i..end], TokenKind.Comment));
                i = end;
                continue;
            }

            // XML/HTML comments <!-- -->
            if (isXml && i + 3 < line.Length && line[i..].StartsWith("<!--", StringComparison.Ordinal))
            {
                int end = line.IndexOf("-->", i + 4, StringComparison.Ordinal);
                if (end < 0) end = line.Length - 3; else end += 3;
                result.Add((line[i..end], TokenKind.Comment));
                i = end;
                continue;
            }

            // String literals " … " and ' … '
            if (line[i] == '"' || line[i] == '\'')
            {
                char q = line[i];
                int j = i + 1;
                while (j < line.Length && line[j] != q)
                {
                    if (line[j] == '\\' && j + 1 < line.Length) j++; // skip escaped char
                    j++;
                }
                if (j < line.Length && line[j] == q) j++;
                result.Add((line[i..j], TokenKind.String));
                i = j;
                continue;
            }

            // Backtick template strings (JS/TS)
            if (line[i] == '`')
            {
                int j = i + 1;
                while (j < line.Length && line[j] != '`')
                {
                    if (line[j] == '\\' && j + 1 < line.Length) j++;
                    j++;
                }
                if (j < line.Length) j++;
                result.Add((line[i..j], TokenKind.String));
                i = j;
                continue;
            }

            // Numbers (including hex 0x, floats, negative in JSON)
            if (char.IsDigit(line[i]) || 
                (line[i] == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1]) && isJson) ||
                (line[i] == '0' && i + 1 < line.Length && (line[i + 1] == 'x' || line[i + 1] == 'X')))
            {
                int j = i;
                if (line[j] == '-') j++;
                if (j + 1 < line.Length && line[j] == '0' && (line[j + 1] == 'x' || line[j + 1] == 'X'))
                {
                    j += 2;
                    while (j < line.Length && "0123456789abcdefABCDEF".Contains(line[j])) j++;
                }
                else
                {
                    while (j < line.Length && (char.IsDigit(line[j]) || line[j] == '.' || line[j] == '_' || 
                           line[j] == 'e' || line[j] == 'E' || line[j] == 'f' || line[j] == 'F' || 
                           line[j] == 'L' || line[j] == 'l' || line[j] == 'u' || line[j] == 'U'))
                        j++;
                }
                result.Add((line[i..j], TokenKind.Number));
                i = j;
                continue;
            }

            // Identifiers / keywords / types
            if (char.IsLetter(line[i]) || line[i] == '_' || line[i] == '@')
            {
                int j = i;
                if (line[j] == '@') j++; // C# verbatim identifier
                while (j < line.Length && (char.IsLetterOrDigit(line[j]) || line[j] == '_')) j++;
                string word = line[i..j];
                
                TokenKind kind = TokenKind.Normal;
                if (keywords.Contains(word))
                    kind = TokenKind.Keyword;
                else if (types.Contains(word))
                    kind = TokenKind.Type;
                else if (char.IsUpper(word[0]) && word.Length > 1) // PascalCase likely type
                    kind = TokenKind.Type;
                
                result.Add((word, kind));
                i = j;
                continue;
            }

            // Operators
            if ("+-*/<>=!&|^~%".Contains(line[i]))
            {
                // Check for multi-char operators
                if (i + 1 < line.Length)
                {
                    string twoChar = line.Substring(i, 2);
                    if (twoChar is "==" or "!=" or "<=" or ">=" or "++" or "--" or "&&" or "||" or 
                        "<<" or ">>" or "+=" or "-=" or "*=" or "/=" or "%=" or "&=" or "|=" or "^=" or
                        "=>" or "::" or "->")
                    {
                        result.Add((twoChar, TokenKind.Operator));
                        i += 2;
                        continue;
                    }
                }
                result.Add((line[i].ToString(), TokenKind.Operator));
                i++;
                continue;
            }

            // Punctuation
            if ("()[]{},.;:".Contains(line[i]))
            {
                result.Add((line[i].ToString(), TokenKind.Punctuation));
                i++;
                continue;
            }

            // Whitespace and everything else
            result.Add((line[i].ToString(), TokenKind.Normal));
            i++;
        }

        return result;
    }

    private static HashSet<string> GetKeywords(string ext) => ext switch
    {
        ".cs" or ".csx" => new HashSet<string> {
            "abstract","as","base","bool","break","byte","case","catch","char","checked",
            "class","const","continue","decimal","default","delegate","do","double","else",
            "enum","event","explicit","extern","false","finally","fixed","float","for",
            "foreach","goto","if","implicit","in","int","interface","internal","is","lock",
            "long","namespace","new","null","object","operator","out","override","params",
            "private","protected","public","readonly","ref","return","sbyte","sealed",
            "short","sizeof","stackalloc","static","string","struct","switch","this",
            "throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
            "using","virtual","void","volatile","while","var","async","await","record",
            "init","with","required","file","scoped","nint","nuint","get","set","value",
            "partial","yield","where","select","from","orderby","group","join","into","let" },
        ".js" or ".mjs" or ".cjs" or ".jsx" or ".ts" or ".tsx" => new HashSet<string> {
            "break","case","catch","class","const","continue","debugger","default","delete",
            "do","else","export","extends","false","finally","for","function","if","import",
            "in","instanceof","let","new","null","return","static","super","switch","this",
            "throw","true","try","typeof","undefined","var","void","while","with","yield",
            "async","await","of","from","as","type","interface","enum","implements",
            "abstract","declare","namespace","readonly","override","public","private",
            "protected","keyof","infer","never","unknown","any","boolean","number","string",
            "symbol","bigint","get","set","constructor" },
        ".py" or ".pyw" => new HashSet<string> {
            "False","None","True","and","as","assert","async","await","break","class",
            "continue","def","del","elif","else","except","finally","for","from","global",
            "if","import","in","is","lambda","nonlocal","not","or","pass","raise","return",
            "try","while","with","yield","self","cls" },
        ".java" or ".kt" => new HashSet<string> {
            "abstract","assert","boolean","break","byte","case","catch","char","class",
            "const","continue","default","do","double","else","enum","extends","final",
            "finally","float","for","goto","if","implements","import","instanceof","int",
            "interface","long","native","new","null","package","private","protected",
            "public","return","short","static","strictfp","super","switch","synchronized",
            "this","throw","throws","transient","try","void","volatile","while","true","false",
            "fun","val","var","object","companion","data","sealed","open","override","when" },
        ".json" or ".jsonc" or ".json5" => new HashSet<string> { "true","false","null" },
        ".html" or ".htm" or ".cshtml" or ".xaml" => new HashSet<string> {
            "html","head","body","div","span","p","a","h1","h2","h3","h4","h5","h6",
            "ul","ol","li","table","tr","td","th","form","input","button","select","option",
            "textarea","label","script","style","link","meta","title","header","footer",
            "nav","main","section","article","aside","img","video","audio","canvas",
            "svg","path","rect","circle","line","polyline","polygon" },
        ".css" or ".scss" or ".less" => new HashSet<string> {
            "important","auto","none","inherit","initial","unset","revert","normal","bold",
            "italic","block","inline","flex","grid","absolute","relative","fixed","sticky",
            "hidden","visible","scroll","solid","dashed","dotted","double","groove","ridge",
            "inset","outset","var","calc","url","rgba","rgb","hsl","hsla" },
        ".go" => new HashSet<string> {
            "break","case","chan","const","continue","default","defer","else","fallthrough",
            "for","func","go","goto","if","import","interface","map","package","range",
            "return","select","struct","switch","type","var","true","false","nil" },
        ".rs" => new HashSet<string> {
            "as","break","const","continue","crate","else","enum","extern","false","fn",
            "for","if","impl","in","let","loop","match","mod","move","mut","pub","ref",
            "return","self","Self","static","struct","super","trait","true","type","unsafe",
            "use","where","while","async","await","dyn" },
        ".cpp" or ".c" or ".h" or ".hpp" or ".cc" => new HashSet<string> {
            "auto","break","case","char","const","continue","default","do","double","else",
            "enum","extern","float","for","goto","if","inline","int","long","register",
            "restrict","return","short","signed","sizeof","static","struct","switch",
            "typedef","union","unsigned","void","volatile","while","bool","true","false",
            "class","namespace","public","private","protected","virtual","override","final",
            "template","typename","nullptr","constexpr","decltype","noexcept" },
        ".sql" => new HashSet<string> {
            "SELECT","FROM","WHERE","INSERT","UPDATE","DELETE","CREATE","ALTER","DROP",
            "TABLE","INDEX","VIEW","JOIN","INNER","LEFT","RIGHT","OUTER","ON","AS","AND",
            "OR","NOT","NULL","IS","IN","BETWEEN","LIKE","ORDER","BY","GROUP","HAVING",
            "LIMIT","OFFSET","UNION","ALL","DISTINCT","COUNT","SUM","AVG","MIN","MAX",
            "PRIMARY","KEY","FOREIGN","REFERENCES","CONSTRAINT","DEFAULT","CHECK","UNIQUE" },
        _ => new HashSet<string>(),
    };

    private static HashSet<string> GetBuiltInTypes(string ext) => ext switch
    {
        ".cs" or ".csx" => new HashSet<string> {
            "String","Int32","Int64","Boolean","Double","Single","Decimal","Char","Byte",
            "DateTime","TimeSpan","Guid","Object","Array","List","Dictionary","Task",
            "IEnumerable","ICollection","IList","Func","Action","ValueTask","Nullable" },
        ".ts" or ".tsx" => new HashSet<string> {
            "String","Number","Boolean","Array","Object","Function","Date","RegExp",
            "Promise","Map","Set","WeakMap","WeakSet","Symbol","BigInt" },
        ".py" or ".pyw" => new HashSet<string> {
            "int","float","str","bool","list","tuple","dict","set","frozenset","bytes",
            "bytearray","range","object","type","None" },
        _ => new HashSet<string>(),
    };

    public static Color GetColor(TokenKind kind, bool dark) => kind switch
    {
        TokenKind.Keyword  => dark ? Color.FromArgb(255, 86,  156, 214) : Color.FromArgb(255,   0,   0, 255),  // Blue
        TokenKind.Type     => dark ? Color.FromArgb(255, 78,  201, 176) : Color.FromArgb(255,  43, 145, 175),  // Cyan
        TokenKind.String   => dark ? Color.FromArgb(255, 206, 145,  80) : Color.FromArgb(255, 163,  21,  21),  // Orange/Brown
        TokenKind.Comment  => dark ? Color.FromArgb(255, 106, 153,  85) : Color.FromArgb(255,   0, 128,   0),  // Green
        TokenKind.Number   => dark ? Color.FromArgb(255, 181, 206, 168) : Color.FromArgb(255,   9, 134,  88),  // Light green
        TokenKind.Operator => dark ? Color.FromArgb(255, 212, 212, 212) : Color.FromArgb(255,  80,  80,  80),  // Gray
        TokenKind.Punctuation => dark ? Color.FromArgb(255, 212, 212, 212) : Color.FromArgb(255,  80,  80,  80),
        _                  => dark ? Color.FromArgb(255, 220, 220, 220) : Color.FromArgb(255,  30,  30,  30),  // Default text
    };
}
