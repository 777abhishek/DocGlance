using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DocGlance.Features.Viewer.Formats.Markdown;

internal static class MermaidFlowchartRenderer
{
    private static readonly Regex NodePattern = new(
        @"^\s*(?<id>[A-Za-z0-9_]+)(?:\[(?<box>[^\]]+)\]|\((?<round>[^)]+)\)|\{(?<diamond>[^}]+)\})?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static FrameworkElement? TryRender(string source, Brush foreground, Brush accent)
    {
        var edges = new List<(string From, string To)>();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        string direction = "TD";

        foreach (string rawLine in source.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
                continue;

            if (line.StartsWith("flowchart ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("graph ", StringComparison.OrdinalIgnoreCase))
            {
                direction = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "TD";
                continue;
            }

            if (!line.Contains("-->", StringComparison.Ordinal))
                return null;

            string[] tokens = line.Split("-->", StringSplitOptions.TrimEntries);
            if (tokens.Length < 2)
                return null;

            var ids = new List<string>(tokens.Length);
            foreach (string token in tokens)
            {
                Match match = NodePattern.Match(token);
                if (!match.Success)
                    return null;

                string id = match.Groups["id"].Value;
                ids.Add(id);
                string label = match.Groups["box"].Success
                    ? match.Groups["box"].Value
                    : match.Groups["round"].Success
                        ? match.Groups["round"].Value
                        : match.Groups["diamond"].Success
                            ? match.Groups["diamond"].Value
                            : id;
                labels[id] = label;
            }

            for (int i = 0; i < ids.Count - 1; i++)
                edges.Add((ids[i], ids[i + 1]));
        }

        if (edges.Count == 0)
            return null;

        var incoming = edges.GroupBy(edge => edge.To)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var outgoing = edges.GroupBy(edge => edge.From)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (incoming.Values.Any(count => count > 1) || outgoing.Values.Any(count => count > 1))
            return null;

        var ordered = new List<string>();
        string? current = edges.Select(edge => edge.From)
            .FirstOrDefault(id => !incoming.ContainsKey(id));
        while (current != null && ordered.Count <= edges.Count)
        {
            ordered.Add(current);
            current = edges.FirstOrDefault(edge => edge.From == current).To;
            if (current == default)
                current = null;
        }
        if (ordered.Count != edges.Count + 1 || ordered.Distinct(StringComparer.Ordinal).Count() != ordered.Count)
            return null;

        bool horizontal = direction.Equals("LR", StringComparison.OrdinalIgnoreCase) ||
                         direction.Equals("RL", StringComparison.OrdinalIgnoreCase);
        var flow = new StackPanel
        {
            Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 14),
        };

        for (int i = 0; i < ordered.Count; i++)
        {
            flow.Children.Add(new Border
            {
                BorderBrush = accent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(16, 10, 16, 10),
                MinWidth = 100,
                Child = new TextBlock
                {
                    Text = labels[ordered[i]],
                    Foreground = foreground,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                },
            });
            if (i < ordered.Count - 1)
            {
                flow.Children.Add(new TextBlock
                {
                    Text = horizontal ? "\u2192" : "\u2193",
                    FontSize = 20,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }
        }

        return flow;
    }
}
