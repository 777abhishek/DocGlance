using DocGlance.Features.Viewer.Components;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace DocGlance.Features.Viewer;

public sealed partial class ViewerPage
{
    private void PlainTextBlock_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        string selectedText = PlainTextBlock.SelectedText;
        if (string.IsNullOrWhiteSpace(selectedText))
            return;

        var menu = new MenuFlyout();
        var copyItem = new MenuFlyoutItem
        {
            Text = "Copy",
            Icon = new SymbolIcon(Symbol.Copy),
        };
        copyItem.Click += (_, _) =>
        {
            var data = new DataPackage();
            data.SetText(selectedText);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        };
        menu.Items.Add(copyItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        var translateItem = new MenuFlyoutItem
        {
            Text = "Translate selection",
            Icon = new SymbolIcon(Symbol.Globe),
        };
        translateItem.Click += (_, _) => _ = ShowTranslationDialogAsync(selectedText);
        menu.Items.Add(translateItem);
        menu.ShowAt(PlainTextBlock, e.GetPosition(PlainTextBlock));
        e.Handled = true;
    }

    private async Task ShowTranslationDialogAsync(string? selectedText)
    {
        if (string.IsNullOrWhiteSpace(selectedText))
            return;

        var dialog = new TranslationDialog(selectedText);
        await dialog.ShowAsync(XamlRoot);
        if (dialog.ManageModelsRequested)
            await TranslationModelsDialog.ShowAsync(XamlRoot);
    }
}
