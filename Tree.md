# QuickView — Project Structure

```text
QuickView/
│
├── App.xaml
├── App.xaml.cs
├── Program.cs
├── QuickView.csproj
├── Package.appxmanifest
├── app.manifest
│
├── Assets/
│   ├── Icons/
│   ├── Images/
│   ├── Logos/
│   └── FileTypes/
│
├── Core/
│   ├── Models/
│   ├── Interfaces/
│   ├── Enums/
│   ├── Constants/
│   └── Extensions/
│
├── Features/
│   ├── Home/
│   │   ├── HomePage.xaml
│   │   ├── HomePage.xaml.cs
│   │   ├── HomeViewModel.cs
│   │   └── Components/
│   │
│   ├── Browser/
│   │   ├── BrowserPage.xaml
│   │   ├── BrowserPage.xaml.cs
│   │   ├── BrowserViewModel.cs
│   │   └── Components/
│   │
│   ├── Viewer/
│   │   ├── ViewerPage.xaml
│   │   ├── ViewerPage.xaml.cs
│   │   ├── ViewerViewModel.cs
│   │   ├── Components/
│   │   ├── Pdf/
│   │   ├── Text/
│   │   └── Search/
│   │
│   ├── Search/
│   │   ├── SearchHighlighter.cs
│   │   └── Components/
│   │
│   └── Settings/
│       ├── SettingsPage.xaml
│       ├── SettingsPage.xaml.cs
│       ├── SettingsViewModel.cs
│       └── Components/
│
├── Rendering/
│   ├── Common/
│   ├── Pdf/
│   ├── Text/
│   ├── Code/
│   ├── Office/
│   ├── Web/
│   ├── Data/
│   ├── Unsupported/
│   └── RendererRegistry.cs
│
├── FileSystem/
│   ├── Scanner/
│   ├── Index/
│   └── Watcher/
│
├── Services/
│   ├── Documents/
│   ├── Thumbnails/
│   │   ├── Providers/
│   │   ├── ThumbnailService.cs
│   │   └── ThumbnailCache.cs
│   ├── Storage/
│   ├── Updates/
│   └── Registration/
│
├── UI/
│   ├── Controls/
│   ├── Shell/
│   ├── Styles/
│   ├── Templates/
│   └── Icons/
│
├── Infrastructure/
│   ├── Configuration/
│   ├── Logging/
│   └── DependencyInjection/
│
├── Tests/
│   ├── Features/
│   ├── Rendering/
│   ├── Services/
│   └── Core/
│
├── Installer/
│   ├── Scripts/
│   ├── Winget/
│   ├── Output/
│   └── QuickView.iss
│
├── Tools/
│   ├── EmbedIcon/
│   └── ImageGenerator/
│
├── Docs/
│   ├── Architecture/
│   ├── Audits/
│   └── Plans/
│
├── .agents/
│   └── tasks/
│
├── .gitignore
└── Tree.md
```

## Architecture Rules

* **Core:** Fundamental models, interfaces, enums and shared contracts.
* **Features:** UI screens and feature-specific logic.
* **Rendering:** File format rendering engines.
* **FileSystem:** File scanning, indexing and monitoring.
* **Services:** Reusable application services.
* **UI:** Shared visual components and styling.
* **Infrastructure:** Configuration, logging and application infrastructure.
* **Tests:** Automated tests.
* **Installer:** Packaging and distribution.
* **Tools:** Development utilities.
* **Docs:** Project documentation.

## Organization Principles

1. Keep feature-specific code inside its owning feature.
2. Avoid duplicate models and interfaces.
3. Keep rendering engines independent of UI pages.
4. Keep reusable services independent of individual features.
5. Avoid unnecessary folders containing only one file.
6. Use dependency injection for service dependencies.
7. Keep tests separate from production code.
8. Do not introduce folders without a clear architectural purpose.

**Note:** This is the target architecture. Move existing files gradually and update namespaces, project references and XAML resource paths. Do not delete existing files just because they are absent from this proposed tree.
