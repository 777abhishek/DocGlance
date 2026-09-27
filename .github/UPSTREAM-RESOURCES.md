# QuickView Upstream Resources

Repository: https://github.com/microsoft/WinUI-Gallery
Local: C:\Code\WinUI3\WinUIGallery

## Slice 1: WinUI Shell (COMPLETED ✓)

### Used Components

- TitleBar sample: C:\Code\WinUI3\WinUIGallery\SampleSupport\SamplePages\TitleBarWindow.xaml
- TitleBar sample: C:\Code\WinUI3\WinUIGallery\SampleSupport\SamplePages\TitleBarWindow.xaml.cs

### Integration Points

- Namespace: `WinUIGallery.Samples.SamplePages` → `Document.Shell`
- App entry point: launches TitleBarWindow directly
- Windows App SDK: 2.4.0

## Slice 2: File Browser (NEXT)

### Planned Components

To be determined based on WinUI Gallery available samples:
- TreeView for folder navigation
- ListView/GridView for file display
- Breadcrumb navigation

## Resource Rules

- Fetch exact raw files from C:\Code\WinUI3\WinUIGallery before reuse
- Copy only the smallest required dependency set for the current slice
- Preserve upstream license and copyright notices
- Never copy the full Gallery project, tests, source generator, or unrelated sample tree
- Never substitute custom code when a requested upstream file is unavailable; stop and report the blocker
