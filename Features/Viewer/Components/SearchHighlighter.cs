// SearchHighlighter.cs
// Two strategies, one interface:
//
//  PLAIN TEXT  (TextBox)
//    • FindInText()   — scans raw string, stores (charOffset, length) per hit
//    • ApplyToTextBox() — calls TextBox.Select(offset, len) for the active hit
//                         SelectionHighlightColorWhenNotFocused keeps color visible
//
//  CODE  (RichTextBlock, 1 Run per Paragraph)
//    • FindInCode()      — scans paragraph text, stores (paraIdx, offset, len) per hit
//    • ApplyToRichText() — splits the Run at match boundaries, wraps matched segment
//                          in InlineUIContainer→Border (yellow/orange background)
//    • ClearRichText()   — restores every paragraph to its original single Run
//
//  Colors: yellow passive, orange active — same as PDF viewer.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.UI;

namespace QuickView.Features.Viewer.Components;

// ── Hit ───────────────────────────────────────────────────────────────────────

public sealed class SearchHit
{
    // For plain text: CharOffset = start in raw string, Length = query length
    public int CharOffset { get; init; }
    public int Length     { get; init; }

    // For code: which paragraph and where inside it
    public int ParaIndex  { get; init; }
    public int IntraOffset{ get; init; }
}

// ── State ─────────────────────────────────────────────────────────────────────

public sealed class SearchState
{
    public string Query     { get; init; } = "";
    public bool   MatchCase { get; init; }
    public int    ActiveIndex { get; set; } = -1;
    public List<SearchHit> Hits { get; } = new();

    public int  Count   => Hits.Count;
    public bool HasHits => Hits.Count > 0;

    public SearchHit? Navigate(int direction)
    {
        if (Hits.Count == 0) return null;
        ActiveIndex = (ActiveIndex + direction + Hits.Count) % Hits.Count;
        return Hits[ActiveIndex];
    }
}

// ── Engine ────────────────────────────────────────────────────────────────────

public static class SearchHighlighter
{
    // yellow passive, orange active — matches PDF viewer
    private static readonly Color _passive = Color.FromArgb(140, 255, 220,   0);
    private static readonly Color _active  = Color.FromArgb(200, 255, 140,   0);

    // ── Plain text ────────────────────────────────────────────────────────────

    /// <summary>Scan raw string, return state with one hit per match.</summary>
    public static SearchState FindInText(string text, string query, bool matchCase)
    {
        var state = new SearchState { Query = query, MatchCase = matchCase };
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(text))
            return state;

        var cmp = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int idx = 0;
        while (true)
        {
            idx = text.IndexOf(query, idx, cmp);
            if (idx < 0) break;
            state.Hits.Add(new SearchHit { CharOffset = idx, Length = query.Length });
            idx += query.Length;
        }
        if (state.HasHits) state.ActiveIndex = 0;
        return state;
    }

    /// <summary>
    /// Select the active hit in the TextBox (native blue highlight).
    /// Sets SelectionHighlightColorWhenNotFocused so color persists when focus moves
    /// to the FindBox. Call this after every navigate and after search.
    /// </summary>
    public static void ApplyToTextBox(SearchState state, TextBox textBox)
    {
        if (!state.HasHits || state.ActiveIndex < 0) return;
        var hit = state.Hits[state.ActiveIndex];

        // Keep highlight visible even when TextBox loses focus to the FindBox
        textBox.SelectionHighlightColorWhenNotFocused =
            new SolidColorBrush(Color.FromArgb(180, 255, 140, 0)); // orange

        textBox.Select(hit.CharOffset, hit.Length);
        // Don't steal focus — just scroll the inner SV to the selection
        ScrollTextBoxToSelection(textBox, hit.CharOffset);
    }

    // Walk the visual tree to find the inner ScrollViewer and scroll to the
    // approximate line of the selected character.
    private static void ScrollTextBoxToSelection(TextBox tb, int charOffset)
    {
        string text = tb.Text;
        int line = 0;
        for (int i = 0; i < charOffset && i < text.Length; i++)
            if (text[i] == '\n') line++;

        var sv = FindChildSv(tb);
        if (sv == null) return;
        double lineH   = tb.FontSize * 1.4; // approximate
        double targetY = Math.Max(0, line * lineH - sv.ViewportHeight / 2);
        sv.ChangeView(null, targetY, null, false);
    }

    private static ScrollViewer? FindChildSv(DependencyObject parent)
    {
        int n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var c = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (c is ScrollViewer sv) return sv;
            var found = FindChildSv(c);
            if (found != null) return found;
        }
        return null;
    }

    // ── Code (RichTextBlock, 1 Run per Paragraph) ─────────────────────────────

    /// <summary>Scan every paragraph's single Run text, one hit per match.</summary>
    public static SearchState FindInCode(RichTextBlock rtb, string query, bool matchCase)
    {
        var state = new SearchState { Query = query, MatchCase = matchCase };
        if (string.IsNullOrEmpty(query)) return state;

        var cmp    = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int paraIdx = 0;
        foreach (var block in rtb.Blocks)
        {
            if (block is Paragraph para)
            {
                string lineText = GetRunText(para);
                int idx = 0;
                while (true)
                {
                    idx = lineText.IndexOf(query, idx, cmp);
                    if (idx < 0) break;
                    state.Hits.Add(new SearchHit
                    {
                        ParaIndex   = paraIdx,
                        IntraOffset = idx,
                        Length      = query.Length,
                    });
                    idx += query.Length;
                }
                paraIdx++;
            }
        }
        if (state.HasHits) state.ActiveIndex = 0;
        return state;
    }

    /// <summary>
    /// Injects InlineUIContainer highlight borders into every matched paragraph.
    /// Each paragraph with 1+ hits is rebuilt as:
    ///   Run(before) + [Border(match)]... + Run(after) ...
    /// Active hit gets orange background, all others yellow.
    /// Call ClearRichText() first if re-applying after a navigate.
    /// </summary>
    public static void ApplyToRichText(SearchState state, RichTextBlock rtb, ScrollViewer scroll)
    {
        if (!state.HasHits) return;

        // Group hits by paragraph
        var byPara = new Dictionary<int, List<SearchHit>>();
        for (int i = 0; i < state.Hits.Count; i++)
        {
            var h = state.Hits[i];
            if (!byPara.TryGetValue(h.ParaIndex, out var list))
                byPara[h.ParaIndex] = list = new List<SearchHit>();
            list.Add(h);
        }

        int paraIdx = 0;
        foreach (var block in rtb.Blocks)
        {
            if (block is not Paragraph para) continue;
            if (!byPara.TryGetValue(paraIdx, out var hits))
            {
                paraIdx++; continue;
            }

            // Save original text + foreground from the single Run
            string         lineText = GetRunText(para);
            SolidColorBrush? fg     = GetRunFg(para);

            para.Inlines.Clear();

            int cursor = 0;
            // Sort hits by intra-offset (they should already be in order)
            hits.Sort((a, b) => a.IntraOffset.CompareTo(b.IntraOffset));

            foreach (var hit in hits)
            {
                // Text before this hit
                if (hit.IntraOffset > cursor)
                    para.Inlines.Add(MakeRun(lineText[cursor..hit.IntraOffset], fg));

                // Determine color — is this the active hit?
                bool isActive = state.Hits.IndexOf(hit) == state.ActiveIndex;
                Color bg      = isActive ? _active : _passive;

                // Force high-contrast text color on highlight background
                // Use black text on yellow/orange backgrounds for maximum visibility
                var highlightFg = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));

                // Highlighted match via InlineUIContainer
                var border   = new Border
                {
                    Background      = new SolidColorBrush(bg),
                    CornerRadius    = new CornerRadius(2),
                    BorderBrush     = new SolidColorBrush(isActive 
                        ? Color.FromArgb(255, 230, 100, 0)   // darker orange border for active
                        : Color.FromArgb(180, 200, 160, 0)), // darker yellow border for passive
                    BorderThickness = new Thickness(1),
                    Child           = new TextBlock
                    {
                        Text         = lineText[hit.IntraOffset..(hit.IntraOffset + hit.Length)],
                        Foreground   = highlightFg,
                        FontFamily   = rtb.FontFamily,
                        FontSize     = rtb.FontSize,
                        FontWeight   = isActive ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
                        Padding      = new Thickness(0),
                    },
                };
                para.Inlines.Add(new InlineUIContainer { Child = border });
                cursor = hit.IntraOffset + hit.Length;
            }

            // Remaining text after last hit
            if (cursor < lineText.Length)
                para.Inlines.Add(MakeRun(lineText[cursor..], fg));

            paraIdx++;
        }

        // Scroll active hit into view
        ScrollCodeToHit(state, rtb, scroll);
    }

    /// <summary>
    /// Restores every paragraph in rtb to a single plain Run.
    /// Rebuilds text from whatever inlines are currently there (Run or InlineUIContainer).
    /// </summary>
    public static void ClearRichText(RichTextBlock rtb)
    {
        foreach (var block in rtb.Blocks)
        {
            if (block is not Paragraph para) continue;
            // Already a single clean Run — nothing to do
            if (para.Inlines.Count == 1 && para.Inlines[0] is Run) continue;

            string         text = ExtractAllText(para);
            SolidColorBrush? fg = GetRunFg(para) ?? GetContainerFg(para);
            para.Inlines.Clear();
            para.Inlines.Add(MakeRun(text, fg));
        }
    }

    // ── Scroll helper ─────────────────────────────────────────────────────────

    private static void ScrollCodeToHit(SearchState state, RichTextBlock rtb, ScrollViewer scroll)
    {
        if (!state.HasHits || state.ActiveIndex < 0) return;
        var hit    = state.Hits[state.ActiveIndex];
        double lineH = rtb.LineHeight > 0 ? rtb.LineHeight : rtb.FontSize * 1.4;
        double top   = rtb.Margin.Top + hit.ParaIndex * lineH;
        double target = Math.Max(0, top - scroll.ViewportHeight / 2);
        scroll.ChangeView(null, target, null, false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Run MakeRun(string text, SolidColorBrush? fg)
    {
        var r = new Run { Text = text };
        if (fg != null) r.Foreground = fg;
        return r;
    }

    private static string GetRunText(Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in para.Inlines)
        {
            if (inline is Run r)
                sb.Append(r.Text);
            else if (inline is InlineUIContainer c && c.Child is Border b && b.Child is TextBlock tb)
                sb.Append(tb.Text);
        }
        return sb.ToString();
    }

    private static string ExtractAllText(Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in para.Inlines)
        {
            if (inline is Run r) sb.Append(r.Text);
            else if (inline is InlineUIContainer c && c.Child is Border b && b.Child is TextBlock tb)
                sb.Append(tb.Text);
        }
        return sb.ToString();
    }

    private static SolidColorBrush? GetRunFg(Paragraph para)
    {
        foreach (var inline in para.Inlines)
            if (inline is Run r && r.Foreground is SolidColorBrush b) return b;
        return null;
    }

    private static SolidColorBrush? GetContainerFg(Paragraph para)
    {
        foreach (var inline in para.Inlines)
            if (inline is InlineUIContainer c && c.Child is Border b && b.Child is TextBlock tb
                && tb.Foreground is SolidColorBrush brush) return brush;
        return null;
    }
}
