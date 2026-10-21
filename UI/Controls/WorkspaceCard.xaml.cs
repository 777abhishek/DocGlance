using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace DocGlance.UI.Controls
{
    /// <summary>
    /// Wallet-style workspace card: same 280x200 footprint as DocumentCard, with format-coloured
    /// document cards (red PDF, blue Word, green Excel...) peeking out of a translucent pocket.
    /// DataContext (read by reflection): Name, StartupStatus, DocumentPreview, DocumentCountLabel,
    /// StartupActionLabel and, optionally, a collection of documents/paths.
    /// </summary>
    public sealed partial class WorkspaceCard : UserControl
    {
        private const int MaxCards = 3;
        private const int MaxChips = 2;   // keeps chips clear of the "N documents" label

        public WorkspaceCard()
        {
            this.InitializeComponent();
            this.DataContextChanged += OnDataContextChanged;
        }

        // ── Data binding ──────────────────────────────────────────────────────

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            var ctx = args.NewValue ?? DataContext;
            if (ctx == null) { Clear(); return; }

            WorkspaceName.Text      = GetString(ctx, "Name");
            DocumentPreview.Text    = GetString(ctx, "DocumentPreview");
            DocumentCountLabel.Text = GetString(ctx, "DocumentCountLabel");
            StartupMenuItem.Text    = GetString(ctx, "StartupActionLabel");

            var status = GetString(ctx, "StartupStatus");
            StartupStatus.Text = status;
            StartupPill.Visibility = string.IsNullOrWhiteSpace(status) ? Visibility.Collapsed : Visibility.Visible;

            var types = ResolveFileTypes(ctx);
            BuildCards(types);
            BuildChips(types);
            ApplyBackdrop(types);
        }

        private void Clear()
        {
            WorkspaceName.Text = DocumentPreview.Text = DocumentCountLabel.Text = StartupStatus.Text = string.Empty;
            StartupPill.Visibility = Visibility.Collapsed;
            CardsHost.Children.Clear();
            ChipsPanel.Children.Clear();
        }

        private static string GetString(object ctx, string prop) =>
            ctx.GetType().GetProperty(prop)?.GetValue(ctx)?.ToString() ?? string.Empty;

        // ── File-type discovery ───────────────────────────────────────────────

        private static List<(string Ext, int Count)> ResolveFileTypes(object ctx)
        {
            var exts = new List<string>();

            foreach (var name in new[] { "Documents", "DocumentItems", "Items", "Files", "FilePaths", "Paths", "Tabs" })
            {
                if (ctx.GetType().GetProperty(name)?.GetValue(ctx) is IEnumerable en)
                {
                    foreach (var item in en)
                    {
                        var ext = ExtOf(item);
                        if (ext.Length > 0) exts.Add(ext);
                    }
                    if (exts.Count > 0) break;
                }
            }

            if (exts.Count == 0)
            {
                // Fallback: preview text only helps if it contains file names with extensions
                var preview = GetString(ctx, "DocumentPreview");
                foreach (var part in preview.Split(new[] { ',', ';', '·', '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ext = NormalizeExt(System.IO.Path.GetExtension(part.Trim()));
                    if (ext.Length > 0) exts.Add(ext);
                }
            }

            return exts.GroupBy(e => e)
                       .Select(g => (Ext: g.Key, Count: g.Count()))
                       .OrderByDescending(g => g.Count)
                       .ToList();
        }

        private static string ExtOf(object? item)
        {
            if (item == null) return string.Empty;
            if (item is string s) return NormalizeExt(System.IO.Path.GetExtension(s));

            var t = item.GetType();
            var ext = t.GetProperty("FileType")?.GetValue(item)?.ToString();
            if (string.IsNullOrWhiteSpace(ext))
            {
                var path = t.GetProperty("FilePath")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Path")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Title")?.GetValue(item)?.ToString();
                ext = System.IO.Path.GetExtension(path ?? string.Empty);
            }
            return NormalizeExt(ext);
        }

        private static string NormalizeExt(string? e) =>
            string.IsNullOrWhiteSpace(e) ? string.Empty : e.Trim().TrimStart('.').ToUpperInvariant();

        // ── Wallet cards ──────────────────────────────────────────────────────

        private void BuildCards(List<(string Ext, int Count)> types)
        {
            CardsHost.Children.Clear();

            bool placeholder = types.Count == 0;
            List<string> exts;

            if (placeholder)
            {
                // Centre card first: PDF in the middle, Word left, Excel right
                exts = new List<string> { "PDF", "DOCX", "XLSX" };
            }
            else
            {
                exts = types.Take(MaxCards).Select(t => t.Ext).ToList();
                int total = types.Sum(t => t.Count);
                // Pad with the dominant format so a one-format workspace still looks like a full wallet
                while (exts.Count < Math.Min(MaxCards, total))
                    exts.Add(types[0].Ext);
            }

            // Slot order: centre, left, right
            double[] slots = { 0, -1, 1 };
            var placed = exts.Select((e, i) => (Ext: e, Slot: slots[Math.Min(i, 2)])).ToList();

            // Sides first, centre last (top of z-order)
            foreach (var (ext, slot) in placed.OrderByDescending(p => Math.Abs(p.Slot)))
            {
                var card = CreateCard(ext, placeholder);
                card.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1.0);
                card.RenderTransform = new RotateTransform { Angle = slot * 8 };
                Canvas.SetLeft(card, 140 - 48 + slot * 60);
                Canvas.SetTop(card, slot == 0 ? 26 : 38);
                CardsHost.Children.Add(card);
            }
        }

        private static Border CreateCard(string ext, bool placeholder)
        {
            var (start, end, _) = GetTypeColors(ext);

            var content = new Grid { Padding = new Thickness(10, 9, 10, 0) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = ext.Length > 4 ? ext[..4] : ext,
                FontSize = 12,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetRow(label, 0);

            var lines = new StackPanel { Spacing = 5, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var w in new double[] { 60, 70, 48 })
                lines.Children.Add(new Border
                {
                    Height = 4, Width = w,
                    CornerRadius = new CornerRadius(2),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255))
                });
            Grid.SetRow(lines, 1);

            content.Children.Add(label);
            content.Children.Add(lines);

            return new Border
            {
                Width = 96,
                Height = 118,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                Opacity = placeholder ? 0.6 : 1,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = start, Offset = 0 },
                        new GradientStop { Color = end,   Offset = 1 }
                    }
                },
                Child = content
            };
        }

        // ── Chips ─────────────────────────────────────────────────────────────

        private void BuildChips(List<(string Ext, int Count)> types)
        {
            ChipsPanel.Children.Clear();

            foreach (var (ext, count) in types.Take(MaxChips))
                ChipsPanel.Children.Add(CreateChip(count > 1 ? $"{ext} ×{count}" : ext, GetTypeColors(ext).End));

            int extra = types.Count - MaxChips;
            if (extra > 0)
                ChipsPanel.Children.Add(CreateChip($"+{extra}", Color.FromArgb(255, 200, 205, 215)));
        }

        private static Border CreateChip(string text, Color dot)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(new Ellipse
            {
                Width = 6, Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(dot)
            });
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            });

            return new Border
            {
                Padding = new Thickness(6, 2, 7, 2),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                Child = row
            };
        }

        // ── Backdrop ──────────────────────────────────────────────────────────

        private void ApplyBackdrop(List<(string Ext, int Count)> types)
        {
            if (types.Count == 0)
            {
                GradientStart.Color = Color.FromArgb(255, 46, 54, 72);
                GradientEnd.Color   = Color.FromArgb(255, 74, 85, 104);
                return;
            }
            var (s, e, _) = GetTypeColors(types[0].Ext);
            GradientStart.Color = s;
            GradientEnd.Color   = e;
        }

        // Same palette as DocumentCard
        private static (Color Start, Color End, string Label) GetTypeColors(string ext) => ext switch
        {
            "DOC" or "DOCX"                                  => (Color.FromArgb(255, 15, 61, 145),  Color.FromArgb(255, 47, 111, 228), "Word"),
            "PDF"                                            => (Color.FromArgb(255, 166, 24, 47),  Color.FromArgb(255, 226, 51, 80),  "PDF"),
            "XLS" or "XLSX" or "CSV"                         => (Color.FromArgb(255, 15, 107, 61),  Color.FromArgb(255, 47, 174, 110), "Excel"),
            "PPT" or "PPTX"                                  => (Color.FromArgb(255, 184, 70, 10),  Color.FromArgb(255, 240, 129, 42), "PowerPoint"),
            "JPG" or "JPEG" or "PNG" or "GIF" or "BMP" or "WEBP" or "SVG"
                                                             => (Color.FromArgb(255, 91, 42, 158),  Color.FromArgb(255, 155, 79, 224), "Image"),
            "MP4" or "AVI" or "MKV" or "MOV" or "WMV" or "WEBM"
                                                             => (Color.FromArgb(255, 61, 31, 140),  Color.FromArgb(255, 122, 79, 224), "Video"),
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ"          => (Color.FromArgb(255, 11, 95, 114),  Color.FromArgb(255, 34, 166, 194), "Archive"),
            "CS" or "CPP" or "C" or "H" or "PY" or "JS" or "TS" or "HTML" or "CSS" or "JSON" or "XML"
                                                             => (Color.FromArgb(255, 13, 77, 130),  Color.FromArgb(255, 36, 150, 214), "Code"),
            "TXT" or "MD" or "LOG"                           => (Color.FromArgb(255, 51, 64, 79),   Color.FromArgb(255, 92, 112, 134), "Text"),
            "MP3" or "WAV" or "FLAC" or "AAC" or "M4A"       => (Color.FromArgb(255, 140, 42, 42),  Color.FromArgb(255, 210, 96, 74),  "Audio"),
            _                                                => (Color.FromArgb(255, 46, 54, 72),   Color.FromArgb(255, 74, 85, 104),  "File"),
        };

        // ── Click handlers (same contract as before) ──────────────────────────

        private void CardButton_Click(object sender, RoutedEventArgs e) =>
            RaiseWorkspaceEvent("OpenWorkspace", DataContext);

        private void UpdateMenuItem_Click(object sender, RoutedEventArgs e) =>
            RaiseWorkspaceEvent("UpdateWorkspace", DataContext);

        private void StartupMenuItem_Click(object sender, RoutedEventArgs e) =>
            RaiseWorkspaceEvent("SetStartupWorkspace", DataContext);

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
            RaiseWorkspaceEvent("DeleteWorkspace", DataContext);

        private void RaiseWorkspaceEvent(string eventName, object workspace)
        {
            DependencyObject? parent = this.Parent;
            while (parent != null && parent is not Page)
                parent = (parent as FrameworkElement)?.Parent;

            if (parent is Page page)
            {
                var method = page.GetType().GetMethod(
                    $"{eventName}_Click",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

                method?.Invoke(page, new object[] { new Button { Tag = workspace }, new RoutedEventArgs() });
            }
        }
    }
}
