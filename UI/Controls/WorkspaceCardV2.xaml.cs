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
    /// Workspace card V2: wallet of format-coloured document cards (each labelled PDF/DOCX/...)
    /// behind the same curved info panel as DocumentCardV2. Chips show "EXT ×count" with the type colour.
    /// DataContext (read by reflection): Name, StartupStatus, DocumentPreview, DocumentCountLabel,
    /// StartupActionLabel, and optionally a collection of documents/paths.
    /// </summary>
    public sealed partial class WorkspaceCardV2 : UserControl
    {
        private const int MaxCards = 3;
        private const int MaxChips = 2;

        public WorkspaceCardV2()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // ── Binding ───────────────────────────────────────────────────────────

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
                var preview = GetString(ctx, "DocumentPreview");
                foreach (var part in preview.Split(new[] { ',', ';', '·', '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var ext = FileTypePalette.NormalizeExt(System.IO.Path.GetExtension(part.Trim()));
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
            if (item is string s) return FileTypePalette.NormalizeExt(System.IO.Path.GetExtension(s));

            var t = item.GetType();
            var ext = t.GetProperty("FileType")?.GetValue(item)?.ToString();
            if (string.IsNullOrWhiteSpace(ext))
            {
                var path = t.GetProperty("FilePath")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Path")?.GetValue(item)?.ToString()
                        ?? t.GetProperty("Title")?.GetValue(item)?.ToString();
                ext = System.IO.Path.GetExtension(path ?? string.Empty);
            }
            return FileTypePalette.NormalizeExt(ext);
        }

        // ── Wallet cards ──────────────────────────────────────────────────────

        private void BuildCards(List<(string Ext, int Count)> types)
        {
            CardsHost.Children.Clear();

            bool placeholder = types.Count == 0;
            List<string> exts;

            if (placeholder)
            {
                exts = new List<string> { "PDF", "DOCX", "XLSX" };   // centre, left, right
            }
            else
            {
                exts = types.Take(MaxCards).Select(t => t.Ext).ToList();
                int total = types.Sum(t => t.Count);
                while (exts.Count < Math.Min(MaxCards, total))
                    exts.Add(types[0].Ext);
            }

            double[] slots = { 0, -1, 1 };   // centre, left, right
            var placed = exts.Select((e, i) => (Ext: e, Slot: slots[Math.Min(i, 2)])).ToList();

            foreach (var (ext, slot) in placed.OrderByDescending(p => Math.Abs(p.Slot)))
            {
                var card = CreateCard(ext, placeholder);
                card.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 1.0);
                card.RenderTransform = new RotateTransform { Angle = slot * 8 };
                Canvas.SetLeft(card, 140 - 48 + slot * 62);
                Canvas.SetTop(card, slot == 0 ? 30 : 42);
                CardsHost.Children.Add(card);
            }
        }

        private static Border CreateCard(string ext, bool placeholder)
        {
            var style = FileTypePalette.Get(ext);
            string label = style.Ext.Length > 4 ? style.Ext[..4] : style.Ext;

            var content = new Grid { Padding = new Thickness(10, 9, 10, 0) };
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tag = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                Foreground = new SolidColorBrush(Colors.White)
            };
            Grid.SetRow(tag, 0);

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

            content.Children.Add(tag);
            content.Children.Add(lines);

            return new Border
            {
                Width = 96, Height = 118,
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
                        new GradientStop { Color = style.Start, Offset = 0 },
                        new GradientStop { Color = style.End,   Offset = 1 }
                    }
                },
                Child = content
            };
        }

        // ── Chips (on the light/dark theme panel, so tinted with the type colour) ──

        private void BuildChips(List<(string Ext, int Count)> types)
        {
            ChipsPanel.Children.Clear();

            foreach (var (ext, count) in types.Take(MaxChips))
                ChipsPanel.Children.Add(CreateChip(count > 1 ? $"{ext} ×{count}" : ext, FileTypePalette.Get(ext).End));

            int extra = types.Count - MaxChips;
            if (extra > 0)
                ChipsPanel.Children.Add(CreateChip($"+{extra}", Color.FromArgb(255, 140, 148, 160)));
        }

        private static Border CreateChip(string text, Color accent)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(new Ellipse
            {
                Width = 6, Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = new SolidColorBrush(accent)
            });
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
            });

            return new Border
            {
                Padding = new Thickness(6, 2, 7, 2),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
                Child = row
            };
        }

        private void ApplyBackdrop(List<(string Ext, int Count)> types)
        {
            var style = FileTypePalette.Get(types.Count == 0 ? null : types[0].Ext);
            GradientStart.Color = style.Start;
            GradientEnd.Color   = style.End;
        }

        // ── Click handlers (same contract as WorkspaceCard) ───────────────────

        private void CardButton_Click(object sender, RoutedEventArgs e)       => RaiseWorkspaceEvent("OpenWorkspace", DataContext);
        private void UpdateMenuItem_Click(object sender, RoutedEventArgs e)   => RaiseWorkspaceEvent("UpdateWorkspace", DataContext);
        private void StartupMenuItem_Click(object sender, RoutedEventArgs e)  => RaiseWorkspaceEvent("SetStartupWorkspace", DataContext);
        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)   => RaiseWorkspaceEvent("DeleteWorkspace", DataContext);

        private void RaiseWorkspaceEvent(string eventName, object workspace)
        {
            DependencyObject? parent = Parent;
            while (parent != null && parent is not Page)
                parent = (parent as FrameworkElement)?.Parent;

            if (parent is Page page)
            {
                var method = page.GetType().GetMethod($"{eventName}_Click",
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public);

                method?.Invoke(page, new object[] { new Button { Tag = workspace }, new RoutedEventArgs() });
            }
        }
    }
}
