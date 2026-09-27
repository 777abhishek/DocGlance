using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI;

namespace QuickView.Rendering.Text;

/// <summary>
/// Plain text file renderer with word wrap, line numbers, and encoding detection.
/// Supports: TXT, LOG, CSV, INI, CFG, ENV, YAML, etc.
/// Uses efficient paragraph splitting for better performance with large files.
/// </summary>
public static class TextRenderer
{
    private const int MaxChars = 1_000_000; // 1 MB display limit (increased from 500KB)
    private const int CharsPerParagraph = 10_000; // Split into paragraphs for better performance

    public class TextRenderResult
    {
        public List<Paragraph> Paragraphs { get; set; } = new();
        public FontFamily FontFamily { get; set; } = new FontFamily("Segoe UI");
        public double FontSize { get; set; } = 14;
        public double LineHeight { get; set; } = 24;
        public TextWrapping TextWrapping { get; set; } = TextWrapping.Wrap;
        public Thickness Padding { get; set; } = new Thickness(16, 12, 16, 12);
    }

    public static async Task<TextRenderResult> RenderAsync(string filePath, CancellationToken ct = default)
    {
        var loaded = await Task.Run(() => TextFileReader.ReadCapped(filePath, MaxChars), ct);
        string text = loaded.Text;
        bool truncated = loaded.Truncated;

        // Detect if this is a log file or structured data
        bool isLog = Path.GetExtension(filePath).Equals(".log", StringComparison.OrdinalIgnoreCase);
        bool isCsv = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase);

        var result = new TextRenderResult
        {
            FontFamily   = isCsv ? new FontFamily("Cascadia Code, Consolas, Courier New") 
                                 : new FontFamily("Segoe UI, Calibri, Arial"),
            FontSize     = 14,
            LineHeight   = 24,
            TextWrapping = isCsv ? TextWrapping.NoWrap : TextWrapping.Wrap,
            Padding      = new Thickness(16, 12, 16, 12),
        };

        // Split text into chunks for better RichTextBlock performance
        int chunkSize = CharsPerParagraph;
        int pos = 0;

        while (pos < text.Length)
        {
            int remainingChars = text.Length - pos;
            int take = Math.Min(chunkSize, remainingChars);
            
            // Try to break at newline for cleaner splits
            if (take < remainingChars && take > 100)
            {
                int searchStart = Math.Max(0, take - 100);
                int searchLen = Math.Min(100, take - searchStart);
                int lastNewline = text.LastIndexOf('\n', pos + take, searchLen);
                if (lastNewline > pos)
                    take = lastNewline - pos + 1;
            }

            string chunk = text.Substring(pos, take);
            var para = new Paragraph { LineHeight = 24, Margin = new Thickness(0) };
            
            // Apply subtle syntax highlighting for log files
            if (isLog)
            {
                AddLogFormatting(para, chunk);
            }
            else
            {
                para.Inlines.Add(new Run { Text = chunk });
            }
            
            result.Paragraphs.Add(para);
            pos += take;
        }

        if (truncated)
        {
            var warningPara = new Paragraph { LineHeight = 24, Margin = new Thickness(0, 16, 0, 0) };
            warningPara.Inlines.Add(new Run
            {
                Text       = $"File truncated — showing first {MaxChars / 1000} KB of {loaded.FileBytes / 1000} KB",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 200, 140, 60)),
                FontStyle  = Windows.UI.Text.FontStyle.Italic,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            result.Paragraphs.Add(warningPara);
        }

        return result;
    }

    /// <summary>
    /// Apply subtle color highlighting to common log patterns (ERROR, WARN, INFO, etc.)
    /// </summary>
    private static void AddLogFormatting(Paragraph para, string text)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            
            // Detect log level and apply color
            Color? color = null;
            if (line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                line.Contains("[ERR]", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("FATAL", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.FromArgb(255, 244, 67, 54); // Red
            }
            else if (line.Contains("WARN", StringComparison.OrdinalIgnoreCase) || 
                     line.Contains("[WRN]", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.FromArgb(255, 255, 152, 0); // Orange
            }
            else if (line.Contains("INFO", StringComparison.OrdinalIgnoreCase) || 
                     line.Contains("[INF]", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.FromArgb(255, 33, 150, 243); // Blue
            }
            else if (line.Contains("DEBUG", StringComparison.OrdinalIgnoreCase) || 
                     line.Contains("[DBG]", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.FromArgb(255, 158, 158, 158); // Gray
            }
            else if (line.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("[OK]", StringComparison.OrdinalIgnoreCase))
            {
                color = Color.FromArgb(255, 76, 175, 80); // Green
            }

            var run = new Run { Text = line };
            if (color.HasValue)
            {
                run.Foreground = new SolidColorBrush(color.Value);
            }
            
            para.Inlines.Add(run);
            
            if (i < lines.Length - 1)
                para.Inlines.Add(new Run { Text = "\n" });
        }
    }
}
