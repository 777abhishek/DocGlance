# QuickView Project Restructure Plan

This plan restructures the QuickView codebase to match the architecture defined in `Tree.md`.

## Current State Analysis

**Files that need moving:**
- `Infrastructure\Update\*` → `Services\Updates\`
- `Infrastructure\Storage\*` → `Services\Storage\`
- `Infrastructure\Registration\*` → `Services\Registration\`
- `Controls\*` → `UI\Controls\`
- `Shell\*` → `UI\Shell\`
- `Resources\Styles\*` → `UI\Styles\`
- `Resources\Templates\*` → `UI\Templates\`
- `Resources\Icons\*` → `UI\Icons\`
- `Models\*` → `Core\Models\`
- `Shared\Collections\*` → `Core\Extensions\` (or delete if empty)
- `Shared\Detection\*` → `Core\Extensions\`
- `Shared\Extensions\*` → `Core\Extensions\`
- `Shared\Formatters\*` → `Core\Extensions\`
- `Shared\Threading\*` → `Core\Extensions\`
- `Shared\Validation\*` → `Core\Extensions\`
- `Thumbnails\*` → `Services\Thumbnails\`
- `Features\Viewer\Components\ViewerPage.*.cs` → Feature-specific subdirectories

**Empty/deprecated folders to clean up:**
- `Collections\` (appears empty)
- `Renderers\` (old folder, content is in `Rendering\`)
- `Core\Helpers\` (if empty)

---

## Phase 1: Move Services (Infrastructure → Services)

### 1.1 Move Update Service

- [ ] **1.1.1** Create `Services\Updates\` directory and move update-related files.
      - Current: `Infrastructure\Update\UpdateService.cs`, `Infrastructure\Update\UpdateBanner.xaml`, `Infrastructure\Update\UpdateBanner.xaml.cs`
      - Target: `Services\Updates\UpdateService.cs`, `Services\Updates\UpdateBanner.xaml`, `Services\Updates\UpdateBanner.xaml.cs`
      - Change namespace from `QuickView.Infrastructure.Update` to `QuickView.Services.Updates` in all 3 files
      - Update x:Class in UpdateBanner.xaml: `QuickView.Services.Updates.UpdateBanner`
      - Files to modify:
        - `Services\Updates\UpdateService.cs` (namespace)
        - `Services\Updates\UpdateBanner.xaml` (x:Class)
        - `Services\Updates\UpdateBanner.xaml.cs` (namespace)
      - Verify: `dotnet build` succeeds

### 1.2 Update imports referencing Infrastructure.Update

- [ ] **1.2.1** Update all files importing `QuickView.Infrastructure.Update` namespace.
      - Files to modify:
        - `Shell\TitleBarWindow.xaml` (change xmlns:update to `using:QuickView.Services.Updates`)
        - `Shell\TitleBarWindow.xaml.cs` (if it has the using)
        - `App.xaml.cs` (if it references UpdateService)
      - Search for: `using QuickView.Infrastructure.Update;`
      - Replace with: `using QuickView.Services.Updates;`
      - Verify: `dotnet build` succeeds

### 1.3 Move Storage Services

- [ ] **1.3.1** Create `Services\Storage\` directory and move storage files.
      - Current: `Infrastructure\Storage\BookmarkStore.cs`, `Infrastructure\Storage\RecentFilesStore.cs`, `Infrastructure\Storage\SettingsStore.cs`
      - Target: `Services\Storage\BookmarkStore.cs`, `Services\Storage\RecentFilesStore.cs`, `Services\Storage\SettingsStore.cs`
      - Change namespace from `QuickView.Infrastructure.Storage` to `QuickView.Services.Storage` in all 3 files
      - Verify: `dotnet build` succeeds

### 1.4 Update imports referencing Infrastructure.Storage

- [ ] **1.4.1** Update all files importing `QuickView.Infrastructure.Storage` namespace.
      - Files to modify (search results show these reference Infrastructure.Storage):
        - `Infrastructure\Configuration\UserPreferences.cs`
        - `Infrastructure\Configuration\AppSettings.cs`
        - `Infrastructure\Registration\RegistrationHelper.cs`
        - `Features\Home\HomePage.xaml.cs`
        - `Features\Viewer\Components\ViewerPage.Toolbar.cs`
        - `Features\Viewer\Components\ViewerPage.Sidebar.cs`
      - Search for: `using QuickView.Infrastructure.Storage;`
      - Replace with: `using QuickView.Services.Storage;`
      - Verify: `dotnet build` succeeds

### 1.5 Move Registration Services

- [ ] **1.5.1** Create `Services\Registration\` directory and move registration files.
      - Current: `Infrastructure\Registration\AppRegistration.cs`, `Infrastructure\Registration\RegistrationHelper.cs`
      - Target: `Services\Registration\AppRegistration.cs`, `Services\Registration\RegistrationHelper.cs`
      - Change namespace from `QuickView.Infrastructure.Registration` to `QuickView.Services.Registration` in both files
      - Verify: `dotnet build` succeeds

### 1.6 Update imports referencing Infrastructure.Registration

- [ ] **1.6.1** Update all files importing `QuickView.Infrastructure.Registration` namespace.
      - Files to modify:
        - `App.xaml.cs`
        - `Features\Settings\SettingsPage.xaml.cs`
      - Search for: `using QuickView.Infrastructure.Registration;`
      - Replace with: `using QuickView.Services.Registration;`
      - Verify: `dotnet build` succeeds

---

## Phase 2: Move UI Components (Controls, Shell, Resources → UI)

### 2.1 Move Controls

- [ ] **2.1.1** Create `UI\Controls\` directory and move all control files.
      - Current: `Controls\DocumentCard.xaml`, `Controls\DocumentCard.xaml.cs`, `Controls\PageThumbnailSidebar.xaml`, `Controls\PageThumbnailSidebar.xaml.cs`, `Controls\PdfActionBar.xaml`, `Controls\PdfActionBar.xaml.cs`, `Controls\PdfTextSelectionOverlay.xaml`, `Controls\PdfTextSelectionOverlay.xaml.cs`, `Controls\ViewerInfoCard.cs`
      - Target: `UI\Controls\` (same filenames)
      - Change namespace from `QuickView.Controls` to `QuickView.UI.Controls` in all .cs files
      - Update x:Class in all XAML files to `QuickView.UI.Controls.*`
      - Verify: `dotnet build` succeeds

### 2.2 Update imports referencing Controls namespace

- [ ] **2.2.1** Update all files importing `QuickView.Controls` namespace.
      - Files to modify (from grep results):
        - `Features\Viewer\Components\ViewerPage.Pdf.cs`
        - `Rendering\Unsupported\UnsupportedRenderer.cs`
        - `Rendering\Office\DocxRenderer.cs`
      - Search for: `using QuickView.Controls;`
      - Replace with: `using QuickView.UI.Controls;`
      - Verify: `dotnet build` succeeds

### 2.3 Move Shell

- [ ] **2.3.1** Create `UI\Shell\` directory and move shell files.
      - Current: `Shell\TitleBarWindow.xaml`, `Shell\TitleBarWindow.xaml.cs`
      - Target: `UI\Shell\TitleBarWindow.xaml`, `UI\Shell\TitleBarWindow.xaml.cs`
      - Change namespace from `QuickView.Shell` to `QuickView.UI.Shell` in .cs file
      - Update x:Class in XAML to `QuickView.UI.Shell.TitleBarWindow`
      - Verify: `dotnet build` succeeds

### 2.4 Update imports referencing Shell namespace

- [ ] **2.4.1** Update all files importing `QuickView.Shell` namespace.
      - Files to modify:
        - `App.xaml.cs`
      - Search for: `using QuickView.Shell;`
      - Replace with: `using QuickView.UI.Shell;`
      - Verify: `dotnet build` succeeds

### 2.5 Move Resources to UI

- [ ] **2.5.1** Create `UI\Styles\` directory and move style files.
      - Current: `Resources\Styles\Colors.xaml`, `Resources\Styles\Controls.xaml`, `Resources\Styles\Typography.xaml`
      - Target: `UI\Styles\Colors.xaml`, `UI\Styles\Controls.xaml`, `UI\Styles\Typography.xaml`
      - No namespace changes needed (XAML resource dictionaries)
      - Verify: `dotnet build` succeeds

- [ ] **2.5.2** Create `UI\Templates\` directory and move template files.
      - Current: `Resources\Templates\FileTemplates.xaml`
      - Target: `UI\Templates\FileTemplates.xaml`
      - No namespace changes needed
      - Verify: `dotnet build` succeeds

- [ ] **2.5.3** Create `UI\Icons\` directory and move icon files.
      - Current: `Resources\Icons\FileTypeIcons.xaml`
      - Target: `UI\Icons\FileTypeIcons.xaml`
      - No namespace changes needed
      - Verify: `dotnet build` succeeds

### 2.6 Update App.xaml resource dictionary references

- [ ] **2.6.1** Update App.xaml to reference new resource paths.
      - File to modify: `App.xaml`
      - Update MergedDictionaries paths:
        - `Resources/Styles/Colors.xaml` → `UI/Styles/Colors.xaml`
        - `Resources/Styles/Typography.xaml` → `UI/Styles/Typography.xaml`
        - `Resources/Styles/Controls.xaml` → `UI/Styles/Controls.xaml`
        - `Resources/Templates/FileTemplates.xaml` → `UI/Templates/FileTemplates.xaml`
        - `Resources/Icons/FileTypeIcons.xaml` → `UI/Icons/FileTypeIcons.xaml`
      - Verify: `dotnet build` succeeds and app runs without XAML parse errors

---

## Phase 3: Move Core Shared Utilities (Models, Shared → Core)

### 3.1 Move Models to Core

- [ ] **3.1.1** Move all model files from `Models\` to `Core\Models\`.
      - Current: `Models\BookmarkItem.cs`, `Models\DocumentItem.cs`, `Models\FileTypeInfo.cs`, `Models\OutlineItem.cs`, `Models\PageThumbnailItem.cs`, `Models\PdfLink.cs`, `Models\PdfSearchResult.cs`
      - Target: `Core\Models\` (same filenames)
      - Change namespace from `QuickView.Models` to `QuickView.Core.Models` in all files
      - Verify: `dotnet build` succeeds

### 3.2 Update imports referencing Models namespace

- [ ] **3.2.1** Update all files importing `QuickView.Models` namespace.
      - Many files import Models. Batch update:
        - `Core\Interfaces\IFileScanner.cs`
        - `Core\Interfaces\IFileRenderer.cs`
        - `Features\Browser\BrowserViewModel.cs`
        - `Features\Home\HomeViewModel.cs`
        - `Features\Home\HomePage.xaml.cs`
        - `Features\Viewer\ViewerViewModel.cs`
        - `Features\Viewer\ViewerPage.xaml.cs`
        - `Features\Viewer\Components\DocumentInfoDialog.cs`
        - `Features\Viewer\Components\ViewerPage.Search.cs`
        - `Features\Viewer\Components\ViewerPage.Pdf.cs`
        - `Features\Viewer\Components\ViewerPage.Sidebar.cs`
        - `FileSystem\Scanner\FolderScanner.cs`
        - `FileSystem\Index\FileIndexService.cs`
        - `Infrastructure\Storage\BookmarkStore.cs` (now `Services\Storage\BookmarkStore.cs`)
        - `Shell\TitleBarWindow.xaml.cs` (now `UI\Shell\TitleBarWindow.xaml.cs`)
        - `Controls\*` (now `UI\Controls\*`)
        - `Rendering\Pdf\PdfLinkService.cs`
        - `Rendering\Pdf\PdfTextService.cs`
        - `Rendering\Pdf\PdfOutlineReader.cs`
        - `Rendering\Unsupported\UnsupportedRenderer.cs`
        - `Rendering\Office\DocxRenderer.cs`
        - `App.xaml.cs`
      - Search for: `using QuickView.Models;`
      - Replace with: `using QuickView.Core.Models;`
      - Verify: `dotnet build` succeeds

### 3.3 Move Shared utilities to Core\Extensions

- [ ] **3.3.1** Move Detection utilities.
      - Current: `Shared\Detection\FileTypeDetector.cs`
      - Target: `Core\Extensions\FileTypeDetector.cs`
      - Change namespace from `QuickView.Shared.Detection` to `QuickView.Core.Extensions`
      - Files importing it: (search for `using QuickView.Shared.Detection;`) - update to `using QuickView.Core.Extensions;`
      - Verify: `dotnet build` succeeds

- [ ] **3.3.2** Move Formatters.
      - Current: `Shared\Formatters\DateFormatter.cs`, `Shared\Formatters\FileSizeFormatter.cs`
      - Target: `Core\Extensions\DateFormatter.cs`, `Core\Extensions\FileSizeFormatter.cs`
      - Change namespace from `QuickView.Shared.Formatters` to `QuickView.Core.Extensions`
      - Files importing it (from grep): `Models\DocumentItem.cs` (now `Core\Models\DocumentItem.cs`), `Features\Viewer\Components\DocumentInfoDialog.cs`
      - Search for: `using QuickView.Shared.Formatters;`
      - Replace with: `using QuickView.Core.Extensions;`
      - Also update test file: `QuickView.Tests\Shared\Formatters\FileSizeFormatterTests.cs` → move to `QuickView.Tests\Core\Extensions\FileSizeFormatterTests.cs` and update namespace to `QuickView.Tests.Core.Extensions`
      - Verify: `dotnet build` succeeds

- [ ] **3.3.3** Move Validation utilities.
      - Current: `Shared\Validation\FileSizeValidator.cs`, `Shared\Validation\PathValidator.cs`
      - Target: `Core\Extensions\FileSizeValidator.cs`, `Core\Extensions\PathValidator.cs`
      - Change namespace from `QuickView.Shared.Validation` to `QuickView.Core.Extensions`
      - Files importing it: `App.xaml.cs`, `FileSystem\Scanner\FolderScanner.cs`, `Rendering\Text\TextFileReader.cs`
      - Search for: `using QuickView.Shared.Validation;`
      - Replace with: `using QuickView.Core.Extensions;`
      - Also update test files: `QuickView.Tests\Shared\Validation\FileSizeValidatorTests.cs` → `QuickView.Tests\Core\Extensions\FileSizeValidatorTests.cs`
      - Verify: `dotnet build` succeeds

- [ ] **3.3.4** Move Threading utilities.
      - Current: `Shared\Threading\UIThreadHelper.cs`
      - Target: `Core\Extensions\UIThreadHelper.cs`
      - Change namespace from `QuickView.Shared.Threading` to `QuickView.Core.Extensions`
      - Search for any imports: `using QuickView.Shared.Threading;` and update
      - Verify: `dotnet build` succeeds

- [ ] **3.3.5** Move or delete Collections utilities.
      - Current: `Shared\Collections\IncrementalLoadingCollection.cs`
      - Target: `Core\Extensions\IncrementalLoadingCollection.cs`
      - Change namespace from `QuickView.Shared.Collections` to `QuickView.Core.Extensions`
      - If file is unused, delete it instead
      - Search for any imports and update
      - Verify: `dotnet build` succeeds

- [ ] **3.3.6** Move any Shared\Extensions files.
      - Current: Check if `Shared\Extensions\` has any files
      - Target: `Core\Extensions\`
      - Update namespaces from `QuickView.Shared.Extensions` to `QuickView.Core.Extensions`
      - Verify: `dotnet build` succeeds

---

## Phase 4: Move Thumbnails to Services

### 4.1 Move Thumbnail Services

- [ ] **4.1.1** Move thumbnails to Services\Thumbnails.
      - Current: `Thumbnails\ThumbnailService.cs`, `Thumbnails\ThumbnailCache.cs`, `Thumbnails\Providers\GenericThumbnailProvider.cs`, `Thumbnails\Providers\OfficeThumbnailProvider.cs`, `Thumbnails\Providers\PdfThumbnailProvider.cs`
      - Target: `Services\Thumbnails\ThumbnailService.cs`, `Services\Thumbnails\ThumbnailCache.cs`, `Services\Thumbnails\Providers\GenericThumbnailProvider.cs`, `Services\Thumbnails\Providers\OfficeThumbnailProvider.cs`, `Services\Thumbnails\Providers\PdfThumbnailProvider.cs`
      - Change namespace from `QuickView.Thumbnails` to `QuickView.Services.Thumbnails`
      - Change namespace from `QuickView.Thumbnails.Providers` to `QuickView.Services.Thumbnails.Providers`
      - Update internal imports: `ThumbnailService.cs` imports `QuickView.Thumbnails.Providers` → change to `QuickView.Services.Thumbnails.Providers`
      - Verify: `dotnet build` succeeds

### 4.2 Update imports referencing Thumbnails namespace

- [ ] **4.2.1** Update all files importing `QuickView.Thumbnails` namespace.
      - Files to modify:
        - `Features\Home\HomePage.xaml.cs`
      - Search for: `using QuickView.Thumbnails;`
      - Replace with: `using QuickView.Services.Thumbnails;`
      - Verify: `dotnet build` succeeds

---

## Phase 5: Reorganize Viewer Partials (Feature-specific subdirectories)

### 5.1 Move PDF-related partials

- [ ] **5.1.1** Create `Features\Viewer\Pdf\` directory and move PDF partials.
      - Current: `Features\Viewer\Components\ViewerPage.Pdf.cs`
      - Target: `Features\Viewer\Pdf\ViewerPage.Pdf.cs`
      - Namespace stays `QuickView.Features.Viewer` (partial class)
      - No import updates needed (same namespace)
      - Verify: `dotnet build` succeeds

### 5.2 Move Text-related partials

- [ ] **5.2.1** Create `Features\Viewer\Text\` directory and move text partials.
      - Current: `Features\Viewer\Components\ViewerPage.Text.cs`
      - Target: `Features\Viewer\Text\ViewerPage.Text.cs`
      - Namespace stays `QuickView.Features.Viewer` (partial class)
      - Verify: `dotnet build` succeeds

### 5.3 Move Search-related partials

- [ ] **5.3.1** Create `Features\Viewer\Search\` directory and move search partials.
      - Current: `Features\Viewer\Components\ViewerPage.Search.cs`
      - Target: `Features\Viewer\Search\ViewerPage.Search.cs`
      - Namespace stays `QuickView.Features.Viewer` (partial class)
      - Verify: `dotnet build` succeeds

### 5.4 Keep shared components in Components folder

- [ ] **5.4.1** Leave these files in `Features\Viewer\Components\`:
      - `ViewerPage.Helpers.cs`
      - `ViewerPage.Sidebar.cs`
      - `ViewerPage.Toolbar.cs`
      - `ViewerPage.Zoom.cs`
      - `ViewerSidebar.xaml`, `ViewerSidebar.xaml.cs`
      - `ViewerToolbar.xaml`, `ViewerToolbar.xaml.cs`
      - `DocumentInfoDialog.cs`
      - These are cross-cutting UI components, not feature-specific
      - No changes needed
      - Verify: `dotnet build` succeeds

---

## Phase 6: Clean Up Empty/Deprecated Folders

### 6.1 Remove old folders

- [ ] **6.1.1** Delete empty or deprecated folders.
      - Check and delete if empty:
        - `Collections\` (should be empty after moving IncrementalLoadingCollection)
        - `Renderers\` (old folder, content is in `Rendering\`)
        - `Models\` (should be empty after moving to Core\Models)
        - `Controls\` (should be empty after moving to UI\Controls)
        - `Shell\` (should be empty after moving to UI\Shell)
        - `Resources\` (should be empty after moving to UI\Styles, UI\Templates, UI\Icons)
        - `Shared\` (should be empty after moving to Core\Extensions)
        - `Thumbnails\` (should be empty after moving to Services\Thumbnails)
        - `Infrastructure\Update\` (should be empty)
        - `Infrastructure\Storage\` (should be empty)
        - `Infrastructure\Registration\` (should be empty)
      - Do NOT delete folders that still have content
      - Verify: folder structure matches Tree.md

---

## Phase 7: Final Verification

### 7.1 Build and test

- [ ] **7.1.1** Clean and rebuild the entire solution.
      - Command: `dotnet clean && dotnet build -c Debug`
      - Expected: 0 errors, 0 warnings (except acceptable WMC1509 from WinUI SDK)
      - If errors occur, review and fix namespace/import issues
      - Verify: Build succeeds

- [ ] **7.1.2** Run all unit tests.
      - Command: `dotnet test QuickView.Tests\QuickView.Tests.csproj`
      - Expected: All 126 tests pass
      - If failures occur, check for namespace updates needed in test files
      - Verify: All tests pass

- [ ] **7.1.3** Launch the application and test core workflows.
      - Command: `.\build.ps1`
      - Test scenarios:
        - App launches without crashes
        - Home page displays
        - Open a PDF file → PDF renders correctly
        - Open a text file → Text displays correctly
        - Open Settings page → Settings load
        - Thumbnails load on home page
      - Verify: All core features work

### 7.2 Commit the restructure

- [ ] **7.2.1** Stage and commit all changes.
      - Command: `git add -A && git commit -m "refactor: restructure project to match Tree.md architecture"`
      - Commit message should reference Tree.md and summarize the major moves
      - Verify: Changes committed to version control

---

## Notes

**Namespace Mapping:**
- `QuickView.Infrastructure.Update` → `QuickView.Services.Updates`
- `QuickView.Infrastructure.Storage` → `QuickView.Services.Storage`
- `QuickView.Infrastructure.Registration` → `QuickView.Services.Registration`
- `QuickView.Controls` → `QuickView.UI.Controls`
- `QuickView.Shell` → `QuickView.UI.Shell`
- `QuickView.Models` → `QuickView.Core.Models`
- `QuickView.Shared.Detection` → `QuickView.Core.Extensions`
- `QuickView.Shared.Formatters` → `QuickView.Core.Extensions`
- `QuickView.Shared.Validation` → `QuickView.Core.Extensions`
- `QuickView.Shared.Threading` → `QuickView.Core.Extensions`
- `QuickView.Shared.Collections` → `QuickView.Core.Extensions`
- `QuickView.Shared.Extensions` → `QuickView.Core.Extensions`
- `QuickView.Thumbnails` → `QuickView.Services.Thumbnails`
- `QuickView.Thumbnails.Providers` → `QuickView.Services.Thumbnails.Providers`

**XAML x:Class Updates:**
- `QuickView.Infrastructure.Update.UpdateBanner` → `QuickView.Services.Updates.UpdateBanner`
- `QuickView.Controls.*` → `QuickView.UI.Controls.*`
- `QuickView.Shell.TitleBarWindow` → `QuickView.UI.Shell.TitleBarWindow`

**XAML Resource Dictionary Paths:**
- `Resources/Styles/*.xaml` → `UI/Styles/*.xaml`
- `Resources/Templates/*.xaml` → `UI/Templates/*.xaml`
- `Resources/Icons/*.xaml` → `UI/Icons/*.xaml`

**Files NOT moved (stay in current location):**
- `App.xaml`, `App.xaml.cs` (root)
- `Program.cs` (root)
- `Core\*` (stays in Core)
- `Features\*` (stays in Features, except ViewerPage partials reorganized)
- `FileSystem\*` (stays in FileSystem)
- `Infrastructure\Configuration\*` (stays - configuration is infrastructure)
- `Infrastructure\Logging\*` (stays - logging is infrastructure)
- `Rendering\*` (stays in Rendering)
- `Services\Documents\*` (already in correct location)
- `Assets\*` (stays in Assets)
- Test files stay in `QuickView.Tests\` with matching structure

**Build Commands:**
- Build: `dotnet build` or `.\build.ps1`
- Test: `dotnet test QuickView.Tests\QuickView.Tests.csproj`
- Clean: `dotnet clean`

**Estimated Impact:**
- ~90 files to move
- ~50+ files with namespace changes
- ~30+ files with import updates
- 5 XAML files with x:Class updates
- 5 resource dictionaries to update paths in App.xaml

**Risk Mitigation:**
- Each phase has a build verification step
- Changes are grouped by dependency (Services first, then UI, then Core)
- Partial classes stay in same namespace (no breakage)
- XAML resource paths are absolute from project root
