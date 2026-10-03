# QuickView — Project Structure

C:.
│   .gitignore
│   app.manifest
│   App.xaml
│   App.xaml.cs
│   build.log
│   build.ps1
│   Package.appxmanifest
│   Program.cs
│   QuickView.csproj
│   Tree.md
│   
├───.agents
│   └───tasks
│           plan.md
│           restructure-plan.md
│           sprint5-pdfservice-plan.md
│           
├───.kiro
│   ├───skills
│   │       quickview-development.md
│   │       
│   └───steering
│           project-structure.md
│           
├───Assets
│   │   AppIcon.ico
│   │   BadgeLogo.png
│   │   HOME_DARK.png
│   │   HOME_LIGHT.png
│   │   SETTINGDARK.png
│   │   SmallTile.png
│   │   SplashScreen.png
│   │   Square150x150Logo.png
│   │   Square310x310Logo.png
│   │   Square44x44Logo.png
│   │   StoreLogo.png
│   │   TEXT_viewer.png
│   │   Viewer.png
│   │   Wide310x150Logo.png
│   │   
│   └───FileTypeIcons
│           Code.ico
│           Code.png
│           CSV.ico
│           CSV.png
│           Excel.ico
│           Excel.png
│           JSON.ico
│           JSON.png
│           LOG.ico
│           LOG.png
│           Markdown.ico
│           Markdown.png
│           PDF.ico
│           PDF.png
│           PowerPoint.ico
│           PowerPoint.png
│           RichText.ico
│           RichText.png
│           Text.ico
│           Text.png
│           Word.ico
│           Word.png
│           XML.ico
│           XML.png
│           
├───Core
│   ├───Constants
│   │       FileExtensions.cs
│   │       
│   ├───Enums
│   │       FileCategory.cs
│   │       ViewMode.cs
│   │       
│   ├───Extensions
│   │       DateFormatter.cs
│   │       FileSizeFormatter.cs
│   │       FileSizeValidator.cs
│   │       FileTypeDetector.cs
│   │       IncrementalLoadingCollection.cs
│   │       PathValidator.cs
│   │       UIThreadHelper.cs
│   │       
│   ├───Helpers
│   ├───Interfaces
│   │       IFileRenderer.cs
│   │       IFileScanner.cs
│   │       IThumbnailProvider.cs
│   │       
│   └───Models
│           BookmarkItem.cs
│           DocumentItem.cs
│           FileTypeInfo.cs
│           OutlineItem.cs
│           PageThumbnailItem.cs
│           PdfLink.cs
│           PdfSearchResult.cs
│           
├───docs
│   │   AUDIT-COMPLETE.md
│   │   
│   └───codebase-audit
│           architecture-recommendation.md
│           codebase-overview.md
│           executive-summary.md
│           FINDINGS-SUMMARY.md
│           IMPLEMENTATION-CHECKLIST.md
│           QUICK-REFERENCE.md
│           README.md
│           refactoring-roadmap.md
│           security-audit.md
│           technical-debt.md
│           
├───Features
│   ├───Browser
│   │       BrowserPage.xaml
│   │       BrowserPage.xaml.cs
│   │       BrowserViewModel.cs
│   │       
│   ├───Home
│   │       HomePage.xaml
│   │       HomePage.xaml.cs
│   │       HomeViewModel.cs
│   │       
│   ├───Search
│   │       SearchHighlighter.cs
│   │       
│   ├───Settings
│   │       SettingsPage.xaml
│   │       SettingsPage.xaml.cs
│   │       
│   └───Viewer
│       │   ViewerPage.xaml
│       │   ViewerPage.xaml.cs
│       │   ViewerViewModel.cs
│       │   
│       ├───Components
│       │       DocumentInfoDialog.cs
│       │       ViewerPage.Helpers.cs
│       │       ViewerPage.Sidebar.cs
│       │       ViewerPage.Toolbar.cs
│       │       ViewerPage.Zoom.cs
│       │       ViewerSidebar.xaml
│       │       ViewerSidebar.xaml.cs
│       │       ViewerToolbar.xaml
│       │       ViewerToolbar.xaml.cs
│       │       
│       ├───Pdf
│       │       ViewerPage.Pdf.cs
│       │       
│       ├───Search
│       │       ViewerPage.Search.cs
│       │       
│       └───Text
│               ViewerPage.Text.cs
│               
├───FileSystem
│   ├───Index
│   │       FileIndexContext.cs
│   │       FileIndexRepository.cs
│   │       FileIndexService.cs
│   │       
│   ├───Scanner
│   │       FolderScanner.cs
│   │       ScanOptions.cs
│   │       
│   └───Watcher
│           FileWatcherService.cs
│           
├───Infrastructure
│   ├───Configuration
│   │       AppSettings.cs
│   │       UserPreferences.cs
│   │       
│   └───Logging
│           Logger.cs
│           
├───installer
│   │   generate-winget-manifest.ps1
│   │   QuickView.iss
│   │   
│   ├───output
│   │   │   QuickView-Setup-x64.exe
│   │   │   QuickView_1.0.0.0_x64.msix
│   │   │   
│   │   └───QuickView_1.0.0.0_x64_Test
│   │       │   Add-AppDevPackage.ps1
│   │       │   Install.ps1
│   │       │   QuickView_1.0.0.0_x64.msix
│   │       │   
│   │       └───Add-AppDevPackage.resources
│   │           │   Add-AppDevPackage.psd1
│   │           │   
│   │           ├───cs-CZ
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───de-DE
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───en-US
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───es-ES
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───fr-FR
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───it-IT
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───ja-JP
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───ko-KR
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───pl-PL
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───pt-BR
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───ru-RU
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───tr-TR
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           ├───zh-CN
│   │           │       Add-AppDevPackage.psd1
│   │           │       
│   │           └───zh-TW
│   │                   Add-AppDevPackage.psd1
│   │                   
│   └───winget
│       └───manifests
│           └───q
│               └───QuickView
│                   └───QuickView
│                       └───1.0.0
│                               QuickView.QuickView.installer.yaml
│                               QuickView.QuickView.locale.en-US.yaml
│                               QuickView.QuickView.yaml
│                               
├───QuickView.Tests
│   │   QuickView.Tests.csproj
│   │   
│   ├───Features
│   │   ├───Home
│   │   │       HomeViewModelTests.cs
│   │   │       
│   │   └───Viewer
│   │           ViewerViewModelTests.cs
│   │           
│   ├───Rendering
│   │   └───Web
│   │           WebSecurityTests.cs
│   │           
│   └───Shared
│       ├───Formatters
│       │       DateFormatterTests.cs
│       │       FileSizeFormatterTests.cs
│       │       
│       └───Validation
│               FileSizeValidatorTests.cs
│               
├───Rendering
│   │   RendererRegistry.cs
│   │   
│   ├───Code
│   │       CodeRenderer.cs
│   │       
│   ├───Data
│   ├───Office
│   │       DocxRenderer.cs
│   │       PptxRenderer.cs
│   │       XlsxRenderer.cs
│   │       
│   ├───Pdf
│   │       PdfAnnotationService.cs
│   │       PdfDocumentState.cs
│   │       Pdfium.cs
│   │       PdfLinkService.cs
│   │       PdfOperationBase.cs
│   │       PdfOutlineReader.cs
│   │       PdfRenderer.cs
│   │       PdfTextService.cs
│   │       
│   ├───Search
│   ├───Text
│   │       TextFileReader.cs
│   │       
│   ├───Unsupported
│   │       UnsupportedRenderer.cs
│   │       
│   └───Web
│           HtmlRenderer.cs
│           MarkdownRenderer.cs
│           
├───Services
│   ├───Documents
│   │       IPdfViewerService.cs
│   │       PdfLoadResult.cs
│   │       PdfViewerService.cs
│   │       
│   ├───Registration
│   │       AppRegistration.cs
│   │       RegistrationHelper.cs
│   │       
│   ├───Storage
│   │       BookmarkStore.cs
│   │       RecentFilesStore.cs
│   │       SettingsStore.cs
│   │       
│   ├───Thumbnails
│   │   │   ThumbnailCache.cs
│   │   │   ThumbnailService.cs
│   │   │   
│   │   └───Providers
│   │           GenericThumbnailProvider.cs
│   │           OfficeThumbnailProvider.cs
│   │           PdfThumbnailProvider.cs
│   │           
│   └───Updates
│           UpdateBanner.xaml
│           UpdateBanner.xaml.cs
│           UpdateService.cs
│           
├───tools
│   │   GenerateInstallerAssets.csx
│   │   rcedit.exe
│   │   
│   ├───EmbedIcon
│   │       EmbedIcon.cs
│   │       EmbedIcon.csproj
│   │       
│   └───imggen
│           imggen.csproj
│           Program.cs
│           
└───UI
    ├───Controls
    │       DocumentCard.xaml
    │       DocumentCard.xaml.cs
    │       PageThumbnailSidebar.xaml
    │       PageThumbnailSidebar.xaml.cs
    │       PdfActionBar.xaml
    │       PdfActionBar.xaml.cs
    │       PdfTextSelectionOverlay.xaml
    │       PdfTextSelectionOverlay.xaml.cs
    │       ViewerInfoCard.cs
    │       
    ├───Icons
    │       FileTypeIcons.xaml
    │       
    ├───Shell
    │       TitleBarWindow.xaml
    │       TitleBarWindow.xaml.cs
    │       
    ├───Styles
    │       Colors.xaml
    │       Controls.xaml
    │       Typography.xaml
    │       
    └───Templates
            FileTemplates.xaml
            
PS C:\Code\QuickView> 

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
