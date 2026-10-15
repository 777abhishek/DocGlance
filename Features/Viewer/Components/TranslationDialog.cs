using DocGlance.Services.Translation;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace DocGlance.Features.Viewer.Components;

public sealed class TranslationDialog : ContentDialog
{
    private readonly string _originalText;
    private readonly ITranslationService _translationService = TranslationService.Instance;
    private readonly ComboBox _sourceLanguage = new();
    private readonly ComboBox _targetLanguage = new();
    private readonly TextBox _translatedText = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        AcceptsReturn = true,
        MinHeight = 100,
        MaxHeight = 210,
        Padding = new Thickness(10, 8, 10, 8),
        BorderThickness = new Thickness(0),
        Background = new SolidColorBrush(Colors.Transparent),
    };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressRing _progress = new()
    {
        IsActive = false,
        Visibility = Visibility.Collapsed,
        Width = 22,
        Height = 22,
    };
    private readonly Button _translateButton = new() { Content = "Translate" };
    private readonly Button _copyTranslationButton = new()
    {
        Content = "Copy translation",
        IsEnabled = false,
    };
    private readonly Border _originalSurface;
    private readonly Border _translationSurface;
    private CancellationTokenSource? _translationCts;
    private bool _isBusy;

    public TranslationDialog(string originalText)
    {
        _originalText = originalText ?? throw new ArgumentNullException(nameof(originalText));
        Title = "Translate";
        CloseButtonText = "Done";
        DefaultButton = ContentDialogButton.None;
        MinWidth = 0;

        var root = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var originalSection = new StackPanel { Spacing = 6 };
        originalSection.Children.Add(new TextBlock
        {
            Text = "ORIGINAL TEXT",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var originalTextBox = new TextBox
        {
            Text = originalText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 50,
            MaxHeight = 112,
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        originalSection.Children.Add(originalTextBox);
        _originalSurface = CreateSurface(originalSection);
        root.Children.Add(_originalSurface);

        var languages = new Grid
        {
            ColumnSpacing = 8,
        };
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        languages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _sourceLanguage.Items.Add(new ComboBoxItem
        {
            Content = "Auto-detect",
            Tag = "auto",
        });
        foreach (TranslationLanguage language in LanguageCatalog.Languages)
        {
            _sourceLanguage.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language.Code });
            _targetLanguage.Items.Add(new ComboBoxItem { Content = language.Name, Tag = language.Code });
        }
        _sourceLanguage.SelectedItem = FindItem(_sourceLanguage, TranslationSettings.SourceLanguage);
        _targetLanguage.SelectedItem = FindItem(_targetLanguage, TranslationSettings.TargetLanguage);
        _sourceLanguage.MinWidth = 0;
        _targetLanguage.MinWidth = 0;
        _sourceLanguage.HorizontalAlignment = HorizontalAlignment.Stretch;
        _targetLanguage.HorizontalAlignment = HorizontalAlignment.Stretch;
        ViewerPopupStyle.ApplyCompactField(_sourceLanguage);
        ViewerPopupStyle.ApplyCompactField(_targetLanguage);
        _sourceLanguage.SelectionChanged += Language_SelectionChanged;
        _targetLanguage.SelectionChanged += Language_SelectionChanged;
        languages.Children.Add(_sourceLanguage);
        var languageArrow = new FontIcon
        {
            Glyph = "\uE72A",
            VerticalAlignment = VerticalAlignment.Center,
        };
        languages.Children.Add(languageArrow);
        languages.Children.Add(_targetLanguage);
        Grid.SetColumn(languageArrow, 1);
        Grid.SetColumn(_targetLanguage, 2);
        root.Children.Add(languages);

        var translateRow = new Grid
        {
            ColumnSpacing = 10,
        };
        translateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        translateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        translateRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _translateButton.HorizontalAlignment = HorizontalAlignment.Left;
        _translateButton.Click += TranslateButton_Click;
        translateRow.Children.Add(_translateButton);
        translateRow.Children.Add(_progress);
        var privacyNote = new TextBlock
        {
            Text = "Runs privately on this device",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        translateRow.Children.Add(privacyNote);
        Grid.SetColumn(_progress, 1);
        Grid.SetColumn(privacyNote, 2);
        root.Children.Add(translateRow);

        var resultSection = new StackPanel { Spacing = 6 };
        resultSection.Children.Add(new TextBlock
        {
            Text = "TRANSLATION",
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        resultSection.Children.Add(_translatedText);
        _translationSurface = CreateSurface(resultSection);
        root.Children.Add(_translationSurface);

        var copyRow = new Grid
        {
            ColumnSpacing = 6,
        };
        for (int i = 0; i < 3; i++)
            copyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var copyOriginal = new Button
        {
            Content = new TextBlock { Text = "Copy original", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            MinWidth = 0,
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        copyOriginal.Click += (_, _) => CopyText(_originalText);
        _copyTranslationButton.Content = new TextBlock { Text = "Copy result", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        _copyTranslationButton.MinWidth = 0;
        _copyTranslationButton.Padding = new Thickness(6, 4, 6, 4);
        _copyTranslationButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _copyTranslationButton.Click += (_, _) => CopyText(_translatedText.Text);
        var manage = new Button
        {
            Content = new TextBlock { Text = "Models", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            MinWidth = 0,
            Padding = new Thickness(6, 4, 6, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        manage.Click += (_, _) =>
        {
            ManageModelsRequested = true;
            _translationCts?.Cancel();
            Hide();
        };
        copyRow.Children.Add(copyOriginal);
        copyRow.Children.Add(_copyTranslationButton);
        copyRow.Children.Add(manage);
        Grid.SetColumn(_copyTranslationButton, 1);
        Grid.SetColumn(manage, 2);
        root.Children.Add(copyRow);
        root.Children.Add(_status);

        Content = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            Content = root,
        };
        Closing += (_, _) => _translationCts?.Cancel();
        TranslationModelManager.Instance.EnsureCatalogLoaded();
    }

    public bool ManageModelsRequested { get; private set; }

    public async Task ShowAsync(XamlRoot xamlRoot)
    {
        XamlRoot = xamlRoot;
        MaxWidth = Math.Min(460, Math.Max(280, xamlRoot.Size.Width - 48));
        ViewerPopupStyle.Apply(this);
        ViewerPopupStyle.ApplyCompactField(_sourceLanguage);
        ViewerPopupStyle.ApplyCompactField(_targetLanguage);
        ViewerPopupStyle.ApplyCompactSurface(_originalSurface);
        ViewerPopupStyle.ApplyCompactSurface(_translationSurface);
        if (Content is ScrollViewer contentScroll)
            contentScroll.MaxHeight = Math.Max(240, xamlRoot.Size.Height - 160);
        await base.ShowAsync();
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        string source = SelectedCode(_sourceLanguage);
        string target = SelectedCode(_targetLanguage);
        if (string.IsNullOrEmpty(target))
        {
            _status.Text = "Choose a target language.";
            return;
        }

        _translationCts?.Dispose();
        _translationCts = new CancellationTokenSource();
        SetBusy(true);
        _translatedText.Text = string.Empty;
        _copyTranslationButton.IsEnabled = false;
        _status.Text = "Translating on this device…";
        try
        {
            TranslationResult result = await _translationService.TranslateAsync(
                _originalText,
                source,
                target,
                _translationCts.Token);
            _translatedText.Text = result.TranslatedText;
            _copyTranslationButton.IsEnabled = true;
            string sourceName = LanguageCatalog.Find(result.SourceLanguage)?.Name ?? result.SourceLanguage;
            _status.Text = result.WasSourceDetected
                ? $"Detected source language: {sourceName}. Translation was processed locally."
                : $"Source: {sourceName}. Translation was processed locally.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Translation cancelled. No partial result was kept.";
            _translatedText.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            _translatedText.Text = string.Empty;
        }
        finally
        {
            SetBusy(false);
            _translationCts?.Dispose();
            _translationCts = null;
        }
    }

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        string source = SelectedCode(_sourceLanguage);
        string target = SelectedCode(_targetLanguage);
        if (!string.IsNullOrEmpty(source))
            TranslationSettings.SourceLanguage = source;
        if (!string.IsNullOrEmpty(target))
            TranslationSettings.TargetLanguage = target;

        _translationCts?.Cancel();
        _translatedText.Text = string.Empty;
        _copyTranslationButton.IsEnabled = false;
        _status.Text = string.Empty;
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        _sourceLanguage.IsEnabled = !busy;
        _targetLanguage.IsEnabled = !busy;
        _translateButton.IsEnabled = !busy;
        _progress.IsActive = busy;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ComboBoxItem? FindItem(ComboBox comboBox, string code) =>
        comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, code, StringComparison.OrdinalIgnoreCase));

    private static string SelectedCode(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private static Border CreateSurface(UIElement child)
    {
        var border = new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Child = child,
        };
        return border;
    }

    private static void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
