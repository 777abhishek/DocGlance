using Microsoft.UI.Xaml.Controls;

namespace DocGlance.Features.Browser;

public sealed partial class BrowserPage : Page
{
    public BrowserViewModel ViewModel { get; } = new();

    public BrowserPage() => InitializeComponent();
}
