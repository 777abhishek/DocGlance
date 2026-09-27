# QuickView Shell Slice Plan

## Source files reviewed

- `C:\Code\WinUI3\WinUIGallery\SampleSupport\SamplePages\TitleBarWindow.xaml`
- `C:\Code\WinUI3\WinUIGallery\SampleSupport\SamplePages\TitleBarWindow.xaml.cs`
- `C:\Code\QuickView\Shell\TitleBarWindow.xaml`
- `C:\Code\QuickView\Shell\TitleBarWindow.xaml.cs`
- `C:\Code\QuickView\Build.md`

## Exact upstream components

- WinUI Gallery `TitleBarWindow.xaml` for the Mica-backed window, TitleBar, search box, NavigationView, and Frame.
- WinUI Gallery `TitleBarWindow.xaml.cs` for title-bar integration, AppWindow icon setup, pane toggling, back navigation, and selection navigation.

## Local integration points

- Local XAML entry point: `Document.Shell.TitleBarWindow`.
- Local navigation targets: `QuickView.Pages.HomePage`, `QuickView.Pages.ViewerPage`, and `QuickView.Pages.SettingsPage`.
- Local app entry point remains the existing `TitleBarWindow` launch path.

## Allowed adaptations

- Change the upstream namespace to `Document.Shell`.
- Change the displayed title and subtitle to QuickView product text.
- Replace Gallery menu items with the existing QuickView page targets.
- Preserve the upstream control structure and event wiring.

## Follow-up UI sources reviewed

- `C:\Code\WinUI3\WinUIGallery\SampleSupport\SamplePages\CardPage.xaml` for Home's GridView reveal cards.
- `C:\Code\WinUI3\WinUIGallery\SampleSupport\SamplePages\SampleCompactSizingPage.xaml` for compact density resources.

## Follow-up integration points

- `HomePage.xaml`: existing `DocumentGrid` and `DocumentItemTemplate`.
- `SettingsPage.xaml`: existing settings control tree.

## Validation

1. Build only `Document.csproj`.
2. Confirm zero build errors.
3. Treat any pre-existing warnings separately from shell changes.
