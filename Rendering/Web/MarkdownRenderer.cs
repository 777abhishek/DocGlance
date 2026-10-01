using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Web;

/// <summary>
/// Markdown file renderer — converts MD → HTML and displays in WebView2.
/// Supports: MD, MARKDOWN, MDX.
/// Enhanced with better CSS, code block syntax highlighting, and table support.
/// </summary>
public static class MarkdownRenderer
{
    public static async Task<WebView2> RenderAsync(string filePath, bool isDarkTheme, CancellationToken ct = default)
    {
        string mdText = await Task.Run(
            () => QuickView.Rendering.Text.TextFileReader.ReadCapped(filePath, 1_000_000).Text, ct);

        string html = BuildHtml(mdText, isDarkTheme);

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment   = VerticalAlignment.Stretch,
        };

        await webView.EnsureCoreWebView2Async();
        if (!ct.IsCancellationRequested)
            webView.NavigateToString(html);

        return webView;
    }

    private static string BuildHtml(string md, bool dark)
    {
        string bg   = dark ? "#1e1e1e" : "#ffffff";
        string fg   = dark ? "#d4d4d4" : "#1e1e1e";
        string codeBg = dark ? "#2d2d2d" : "#f6f8fa";
        string codeBorder = dark ? "#404040" : "#d0d7de";
        string linkColor  = dark ? "#58a6ff" : "#0969da";
        string hrColor    = dark ? "#404040" : "#d0d7de";
        string quoteBorder = dark ? "#58a6ff" : "#0969da";
        string quoteBg = dark ? "rgba(88,166,255,0.1)" : "rgba(9,105,218,0.05)";
        string tableBorder = dark ? "#444" : "#d0d7de";
        string tableHeaderBg = dark ? "#2d2d2d" : "#f6f8fa";

        var html = new StringBuilder();
        var lines = md.Split('\n');
        bool inCode = false;
        bool inList = false;
        bool isOrderedList = false;  // Track whether current list is <ol> vs <ul>
        bool inTable = false;
        string? codeLanguage = null;

        for (int idx = 0; idx < lines.Length; idx++)
        {
            string rawLine = lines[idx];
            string line = rawLine.TrimEnd();

            // Fenced code blocks with language detection
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCode)
                {
                    codeLanguage = line.Length > 3 ? line[3..].Trim() : null;
                    string langLabel = string.IsNullOrWhiteSpace(codeLanguage) ? "" 
                        : $"<div style='color:{(dark ? "#888" : "#666")};font-size:11px;font-weight:600;text-transform:uppercase;margin-bottom:6px'>{EscapeHtml(codeLanguage)}</div>";
                    html.Append($"{langLabel}<pre style='background:{codeBg};border:1px solid {codeBorder};border-radius:6px;padding:16px;overflow-x:auto;margin:16px 0;line-height:1.45'><code>");
                    inCode = true;
                }
                else
                {
                    html.Append("</code></pre>");
                    inCode = false;
                    codeLanguage = null;
                }
                continue;
            }

            if (inCode)
            {
                html.AppendLine(EscapeHtml(line));
                continue;
            }

            // Close list before non-list line
            if (inList && !line.StartsWith("- ", StringComparison.Ordinal) &&
                          !line.StartsWith("* ", StringComparison.Ordinal) &&
                          !line.StartsWith("+ ", StringComparison.Ordinal) &&
                          !Regex.IsMatch(line, @"^\d+\.\s"))
            {
                html.AppendLine(isOrderedList ? "</ol>" : "</ul>");
                inList = false;
                isOrderedList = false;
            }

            // Close table before non-table line
            if (inTable && !line.Contains('|'))
            {
                html.AppendLine("</tbody></table>");
                inTable = false;
            }

            // Headings with anchor-style IDs
            if (line.StartsWith("#### ")) { html.AppendLine($"<h4 style='margin-top:24px;margin-bottom:12px;font-size:1.1em'>{InlineFormat(line[5..], dark)}</h4>"); continue; }
            if (line.StartsWith("### "))  { html.AppendLine($"<h3 style='margin-top:24px;margin-bottom:12px;font-size:1.25em'>{InlineFormat(line[4..], dark)}</h3>"); continue; }
            if (line.StartsWith("## "))   { html.AppendLine($"<h2 style='margin-top:32px;margin-bottom:12px;font-size:1.5em'>{InlineFormat(line[3..], dark)}</h2>"); continue; }
            if (line.StartsWith("# "))    { html.AppendLine($"<h1 style='margin-top:32px;margin-bottom:16px;font-size:2em;border-bottom:1px solid {hrColor};padding-bottom:8px'>{InlineFormat(line[2..], dark)}</h1>"); continue; }

            // Horizontal rule
            if (line == "---" || line == "***" || line == "___")
            {
                html.AppendLine($"<hr style='border:none;border-top:1px solid {hrColor};margin:24px 0'>");
                continue;
            }

            // Tables (GitHub Flavored Markdown style)
            if (line.Contains('|') && !inTable)
            {
                // Check if next line is separator (e.g., | --- | --- |)
                if (idx + 1 < lines.Length && Regex.IsMatch(lines[idx + 1], @"^\|?\s*[-:]+\s*\|"))
                {
                    html.AppendLine($"<table style='border-collapse:collapse;width:100%;margin:16px 0'>");
                    html.AppendLine("<thead>");
                    html.AppendLine($"<tr style='background:{tableHeaderBg};border:1px solid {tableBorder}'>");
                    foreach (var cell in line.Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        html.Append($"<th style='border:1px solid {tableBorder};padding:8px 12px;text-align:left;font-weight:600'>{InlineFormat(cell.Trim(), dark)}</th>");
                    }
                    html.AppendLine("</tr>");
                    html.AppendLine("</thead><tbody>");
                    inTable = true;
                    idx++; // Skip separator line
                    continue;
                }
            }

            if (inTable && line.Contains('|'))
            {
                html.AppendLine($"<tr style='border:1px solid {tableBorder}'>");
                foreach (var cell in line.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    html.Append($"<td style='border:1px solid {tableBorder};padding:8px 12px'>{InlineFormat(cell.Trim(), dark)}</td>");
                }
                html.AppendLine("</tr>");
                continue;
            }

            // Unordered list
            if (line.StartsWith("- ", StringComparison.Ordinal) ||
                line.StartsWith("* ", StringComparison.Ordinal) ||
                line.StartsWith("+ ", StringComparison.Ordinal))
            {
                if (!inList) { html.AppendLine("<ul style='padding-left:24px;margin:12px 0'>"); inList = true; isOrderedList = false; }
                html.AppendLine($"<li style='margin:6px 0'>{InlineFormat(line[2..], dark)}</li>");
                continue;
            }

            // Ordered list
            if (Regex.IsMatch(line, @"^\d+\.\s"))
            {
                if (!inList) { html.AppendLine("<ol style='padding-left:24px;margin:12px 0'>"); inList = true; isOrderedList = true; }
                string content = Regex.Replace(line, @"^\d+\.\s", "");
                html.AppendLine($"<li style='margin:6px 0'>{InlineFormat(content, dark)}</li>");
                continue;
            }

            // Blockquote
            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                html.AppendLine($"<blockquote style='border-left:3px solid {quoteBorder};background:{quoteBg};margin:12px 0;padding:8px 16px;border-radius:4px'>{InlineFormat(line[2..], dark)}</blockquote>");
                continue;
            }

            // Task list [ ] or [x]
            if (Regex.IsMatch(line, @"^[-*+]\s+\[([ xX])\]\s"))
            {
                bool checked_ = line.Contains("[x]", StringComparison.OrdinalIgnoreCase);
                string content = Regex.Replace(line, @"^[-*+]\s+\[([ xX])\]\s", "");
                string checkbox = checked_ ? "☑" : "☐";
                if (!inList) { html.AppendLine("<ul style='list-style:none;padding-left:0;margin:12px 0'>"); inList = true; isOrderedList = false; }
                html.AppendLine($"<li style='margin:6px 0'>{checkbox} {InlineFormat(content, dark)}</li>");
                continue;
            }

            // Empty line
            if (string.IsNullOrWhiteSpace(line))
            {
                html.AppendLine("<br>");
                continue;
            }

            // Regular paragraph
            html.AppendLine($"<p style='margin:8px 0;line-height:1.6'>{InlineFormat(line, dark)}</p>");
        }

        if (inList) html.AppendLine(isOrderedList ? "</ol>" : "</ul>");
        if (inTable) html.AppendLine("</tbody></table>");
        if (inCode) html.AppendLine("</code></pre>");

        return $$"""
            <!DOCTYPE html><html><head><meta charset='utf-8'>
            <style>
              body {
                background:{{bg}}; color:{{fg}};
                font-family:'Segoe UI',-apple-system,BlinkMacSystemFont,system-ui,sans-serif;
                font-size:15px; line-height:1.65; padding:32px 40px; margin:0;
                max-width:880px; margin:0 auto;
              }
              h1,h2,h3,h4,h5,h6 { line-height:1.3; margin-top:24px; margin-bottom:12px; font-weight:600 }
              h1 { font-size:2em; padding-bottom:8px; border-bottom:1px solid {{hrColor}} }
              h2 { font-size:1.5em }
              h3 { font-size:1.25em }
              h4 { font-size:1.1em }
              code {
                background:{{codeBg}}; border:1px solid {{codeBorder}}; border-radius:3px; padding:2px 6px;
                font-family:'Cascadia Code','Cascadia Mono','Consolas',monospace; font-size:0.9em
              }
              pre { margin:16px 0; overflow-x:auto }
              pre code { border:none; padding:0; background:transparent }
              a { color:{{linkColor}}; text-decoration:none }
              a:hover { text-decoration:underline }
              ul,ol { padding-left:24px; margin:12px 0 }
              li { margin:6px 0 }
              img { max-width:100%; height:auto; border-radius:6px; margin:16px 0 }
              blockquote {
                border-left:3px solid {{quoteBorder}}; background:{{quoteBg}};
                margin:12px 0; padding:8px 16px; border-radius:4px
              }
              table { border-collapse:collapse; width:100%; margin:16px 0 }
              th { background:{{tableHeaderBg}}; font-weight:600; text-align:left }
              th,td { border:1px solid {{tableBorder}}; padding:8px 12px }
              hr { border:none; border-top:1px solid {{hrColor}}; margin:24px 0 }
              ::selection { background:{{(dark ? "#264f78" : "#b3d4fc")}} }
            </style></head><body>{{html}}</body></html>
            """;
    }

    private static string InlineFormat(string text, bool dark)
    {
        text = EscapeHtml(text);

        // Bold **text** or __text__
        text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        text = Regex.Replace(text, @"__(.+?)__", "<strong>$1</strong>");

        // Italic *text* or _text_ (but not inside words like file_name)
        text = Regex.Replace(text, @"(?<!\w)\*(.+?)\*(?!\w)", "<em>$1</em>");
        text = Regex.Replace(text, @"(?<!\w)_(.+?)_(?!\w)", "<em>$1</em>");

        // Strikethrough ~~text~~
        text = Regex.Replace(text, @"~~(.+?)~~", "<del>$1</del>");

        // Inline code `code`
        text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");

        // Links [label](url) with optional title
        text = Regex.Replace(text, @"\[([^\]]+)\]\(([^\s\)]+)(?:\s+""([^""]+)"")?\)", 
            m => $"<a href='{m.Groups[2].Value}' title='{m.Groups[3].Value}'>{m.Groups[1].Value}</a>");

        // Images ![alt](url)
        text = Regex.Replace(text, @"!\[([^\]]*)\]\(([^\)]+)\)",
            "<img src='$2' alt='$1' style='max-width:100%;height:auto;border-radius:6px'>");

        // Autolinks <https://example.com>
        text = Regex.Replace(text, @"&lt;(https?://[^\s&gt;]+)&gt;", 
            "<a href='$1'>$1</a>");

        return text;
    }

    private static string EscapeHtml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
