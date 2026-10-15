using DocGlance.Services.Translation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace DocGlance.Features.Viewer.Components;

public sealed class TranslationModelsDialog : ContentDialog
{
    private readonly TranslationModelManager _manager = TranslationModelManager.Instance;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _searchBox = new()
    {
        PlaceholderText = "Search languages, codes, or model files",
        Padding = new Thickness(12, 8, 12, 8),
        MinHeight = 40,
        BorderThickness = new Thickness(0),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly ProgressBar _progress = new()
    {
        Minimum = 0,
        Maximum = 100,
        Visibility = Visibility.Collapsed,
    };
    private readonly Button _refreshButton = new() { Content = "Update catalog" };
    private readonly Button _cancelButton = new()
    {
        Content = "Cancel operation",
        Visibility = Visibility.Collapsed,
        IsEnabled = false,
    };

    private CancellationTokenSource? _operationCts;
    private bool _busy;

    private TranslationModelsDialog()
    {
        Title = "Manage translation models";
        CloseButtonText = "Close";
        DefaultButton = ContentDialogButton.Close;
        MinWidth = 0;

        var root = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(new TextBlock
        {
            Text = "Search and manage offline language models",
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(new TextBlock
        {
            Text = "Firefox Translations models · Review model terms before downloading. Downloads are opt-in; selected text never leaves this device.",
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
        });
        var link = new HyperlinkButton
        {
            Content = "Model details and terms",
            NavigateUri = new Uri("https://mozilla.github.io/translations/firefox-models/"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        root.Children.Add(link);
        root.Children.Add(_searchBox);
        _searchBox.TextChanged += (_, _) => RefreshRows();

        var actions = new Grid
        {
            ColumnSpacing = 8,
        };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _refreshButton.Content = "Refresh language catalog";
        _refreshButton.Click += RefreshCatalog_Click;
        _cancelButton.Click += (_, _) => _operationCts?.Cancel();
        _refreshButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _cancelButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        actions.Children.Add(_refreshButton);
        actions.Children.Add(_cancelButton);
        Grid.SetColumn(_cancelButton, 1);
        root.Children.Add(actions);
        root.Children.Add(_progress);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 340,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            Content = _rows,
        });

        Content = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            Content = root,
        };
        Closing += (_, args) =>
        {
            if (_busy)
            {
                args.Cancel = true;
                _status.Text = "Cancel the current operation or wait for it to finish before closing.";
            }
        };
    }

    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        double availableWidth = Math.Max(280, xamlRoot.Size.Width - 48);
        double availableHeight = Math.Max(240, xamlRoot.Size.Height - 160);
        var dialog = new TranslationModelsDialog
        {
            XamlRoot = xamlRoot,
            MaxWidth = Math.Min(480, availableWidth),
        };
        ViewerPopupStyle.Apply(dialog);
        ViewerPopupStyle.ApplyCompactField(dialog._searchBox);
        if (dialog.Content is ScrollViewer contentScroll)
            contentScroll.MaxHeight = availableHeight;
        dialog.RefreshRows();
        await dialog.ShowAsync();
    }

    private async void RefreshCatalog_Click(object sender, RoutedEventArgs e)
    {
        await RunOperationAsync(async cancellationToken =>
        {
            var progress = new Progress<string>(message => _status.Text = message);
            await _manager.RefreshCatalogAsync(progress, cancellationToken);
            RefreshRows();
        });
    }

    private async Task DownloadAsync(TranslationModelPair pair)
    {
        await RunOperationAsync(async cancellationToken =>
        {
            var progress = new Progress<ModelDownloadProgress>(state =>
            {
                _progress.Value = state.TotalBytes > 0
                    ? Math.Min(100, (double)state.BytesDownloaded / state.TotalBytes * 100)
                    : 0;
                _status.Text = $"Downloading {pair.Key}: {FormatBytes(state.BytesDownloaded)} / {FormatBytes(state.TotalBytes)}\n{state.CurrentFile}";
            });
            await _manager.DownloadPairAsync(
                pair.SourceLanguage,
                pair.TargetLanguage,
                progress,
                cancellationToken);
            _status.Text = $"Installed {pair.Key}. Disk usage: {FormatBytes(_manager.GetInstalledBytes())} total.";
            _progress.Value = 100;
            RefreshRows();
        });
    }

    private async Task RemoveAsync(TranslationModelPair pair)
    {
        await RunOperationAsync(async cancellationToken =>
        {
            await _manager.RemovePairAsync(pair.Key, cancellationToken);
            _status.Text = $"Removed {pair.Key}.";
            RefreshRows();
        });
    }

    private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
    {
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
        SetBusy(true);
        _progress.Visibility = Visibility.Visible;
        _progress.Value = 0;
        try
        {
            await operation(_operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Operation cancelled. Incomplete files were discarded.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
        finally
        {
            _progress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            _operationCts.Dispose();
            _operationCts = null;
            RefreshRows();
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _refreshButton.IsEnabled = !busy;
        _cancelButton.IsEnabled = busy;
        _cancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshRows()
    {
        _rows.Children.Clear();
        var statuses = _manager.GetStatuses();
        string search = _searchBox.Text.Trim();
        if (statuses.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = _manager.CatalogError ??
                    "No model catalog is saved yet. Update the catalog to see downloadable language pairs.",
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        TranslationModelStatus[] filteredStatuses = statuses
            .Where(status => MatchesSearch(status, search))
            .ToArray();
        if (filteredStatuses.Length == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = $"No language models match “{search}”.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 8),
            });
            _status.Text = $"{statuses.Count} model directions available.";
            return;
        }

        foreach (TranslationModelStatus status in filteredStatuses)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel { Spacing = 2 };
            string sourceName = LanguageCatalog.Find(status.Pair.SourceLanguage)?.Name ?? status.Pair.SourceLanguage;
            string targetName = LanguageCatalog.Find(status.Pair.TargetLanguage)?.Name ?? status.Pair.TargetLanguage;
            info.Children.Add(new TextBlock
            {
                Text = $"{sourceName} → {targetName}",
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            info.Children.Add(new TextBlock
            {
                Text = status.IsInstalled
                    ? $"Installed · {FormatBytes(status.InstalledBytes)} on disk"
                    : $"Download · {FormatBytes(status.Pair.DownloadBytes)}",
                Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            });
            Grid.SetColumn(info, 0);
            row.Children.Add(info);

            var action = new Button
            {
                Content = status.IsInstalled ? "Remove" : "Download",
                IsEnabled = !_busy,
                VerticalAlignment = VerticalAlignment.Center,
            };
            action.Click += async (_, _) =>
            {
                if (status.IsInstalled)
                    await RemoveAsync(status.Pair);
                else
                    await DownloadAsync(status.Pair);
            };
            Grid.SetColumn(action, 1);
            row.Children.Add(action);

            var surface = new Border
            {
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(10),
                Child = row,
            };
            _rows.Children.Add(surface);
            ViewerPopupStyle.ApplyCompactSurface(surface);
        }

        DateTimeOffset? fetchedAt = _manager.CatalogFetchedAt;
        _status.Text = fetchedAt is null
            ? "Catalog unavailable."
            : search.Length == 0
                ? $"{TranslationModelCatalog.ProviderName} catalog · updated {fetchedAt.Value.ToLocalTime():g} · {statuses.Count} directions · {FormatBytes(_manager.GetInstalledBytes())} installed"
                : $"{filteredStatuses.Length} of {statuses.Count} model directions · {FormatBytes(_manager.GetInstalledBytes())} installed";
    }

    private static bool MatchesSearch(TranslationModelStatus status, string search)
    {
        if (search.Length == 0)
            return true;

        string searchable = string.Join(
            ' ',
            LanguageCatalog.SearchTerms(status.Pair.SourceLanguage)
                .Concat(LanguageCatalog.SearchTerms(status.Pair.TargetLanguage))
                .Append(status.Pair.Key)
                .Concat(status.Pair.Files.Select(file => file.Name)));
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }
}
