# QuickView Development Skill

## Before Making Any Code Changes

1. **Read Tree.md** - Understand the mandatory project structure
2. **Check project-structure.md steering file** - See how directories map to namespaces
3. **Locate existing files** - Use grep/file_search to find related code
4. **Read existing implementations** - Never guess, always read first

## File Reading Strategy

### When working with PDF features:
```
1. Read Rendering/Pdf/PdfRenderer.cs - understand rendering API
2. Read Rendering/Pdf/PdfDocumentState.cs - understand document handle
3. Read Rendering/Pdf/Pdfium.cs - understand native layer (DON'T MODIFY - SEC-04)
4. Read Features/Viewer/Pdf/ViewerPage.Pdf.cs - understand UI layer
5. Read Services/Documents/PdfViewerService.cs - understand service layer
```

### When working with other features:
```
1. Read Core/Interfaces/ - find relevant interface
2. Read Rendering/{Format}/ - find relevant renderer
3. Read Features/{Feature}/ - find relevant UI
4. Read Services/{Service}/ - find relevant service
```

## Namespace Convention

**Directory → Namespace mapping:**
```
Core/Models/BookmarkItem.cs           → QuickView.Core.Models
Core/Extensions/FileSizeValidator.cs  → QuickView.Core.Extensions
Rendering/Pdf/PdfRenderer.cs          → QuickView.Rendering.Pdf
Services/Documents/PdfViewerService.cs → QuickView.Services.Documents
Features/Home/HomePage.xaml.cs        → QuickView.Features.Home
Features/Viewer/Pdf/ViewerPage.Pdf.cs → QuickView.Features.Viewer (partial!)
UI/Controls/DocumentCard.xaml.cs      → QuickView.UI.Controls
```

## Security Rules (DON'T BREAK THESE)

### SEC-04: Secure pdfium.dll Loading
- **File:** `Rendering/Pdf/Pdfium.cs`
- **Rule:** NEVER modify the static constructor
- **Reason:** Loads pdfium.dll from application directory only (prevents DLL hijacking)
- **Chain:** UI → Service → Renderer → Pdfium (static ctor runs once)

### SEC-01: JavaScript Disabled
- **Files:** `Rendering/Web/HtmlRenderer.cs`, `Rendering/Web/MarkdownRenderer.cs`
- **Rule:** Keep `IsScriptEnabled = false`

### SEC-02: URL Sanitization
- **File:** `Rendering/Web/MarkdownRenderer.cs`
- **Rule:** Keep Markdig library, URL sanitization (blocks javascript:, data:, vbscript:)

### SEC-03: SHA-256 Verification
- **File:** `Services/Updates/UpdateService.cs`
- **Rule:** Always verify SHA-256 before Authenticode check

### SEC-05: Path Validation
- **File:** `Core/Extensions/PathValidator.cs`
- **Rule:** Always call `PathValidator.IsValidFilePath()` before file operations

## Sprint 5 Guidelines

### Current Goal
Extract PDF logic from `Features/Viewer/Pdf/ViewerPage.Pdf.cs` into `Services/Documents/PdfViewerService.cs`

### What Moves to Service
- PDF document loading (with password handling)
- Progressive rendering orchestration
- Prefetch and eviction logic
- Memory management

### What Stays in ViewerPage
- UI operations (XAML manipulation)
- Event handlers
- ScrollViewer positioning
- Toolbar/Sidebar updates
- Password dialog creation (UI callback)

### Pattern: Service + UI Callback
```csharp
// In PdfViewerService.cs
public async Task<PdfLoadResult> LoadDocumentAsync(
    string filePath,
    Func<Task<string?>>? passwordPrompt = null,  // UI callback
    CancellationToken ct = default)
{
    // Service handles PDF operations
    // Calls passwordPrompt() when password needed
}

// In ViewerPage.Pdf.cs
private async Task LoadPdfAsync(string filePath, CancellationToken ct)
{
    var result = await _pdfService.LoadDocumentAsync(
        filePath,
        ShowPasswordDialogAsync,  // Pass UI callback
        ct);
    
    // UI operations stay here
    UpdateToolbar();
    CreatePlaceholders();
}
```

## Testing Requirements

After any change:
1. Build: `dotnet build c:\Code\QuickView\QuickView.csproj`
2. Test: `dotnet test c:\Code\QuickView\QuickView.Tests\QuickView.Tests.csproj`
3. Expected: 0 errors, 126 tests pass

## Common Mistakes to Avoid

1. ❌ Creating files outside Tree.md structure
2. ❌ Using wrong namespace for location
3. ❌ Modifying Pdfium.cs (breaks SEC-04)
4. ❌ Not reading existing code before changes
5. ❌ Moving UI operations to service layer
6. ❌ Breaking the service → renderer → native chain
7. ❌ Forgetting to update XAML x:Class when moving files
8. ❌ Using relative paths (use absolute paths with Tree.md structure)

## Workflow Rules

1. **No workflows** - Work directly with tools
2. **Read first, code second** - Always understand before changing
3. **One phase at a time** - Complete and verify each phase before next
4. **Commit after each phase** - Keep git history clean
5. **Test after every change** - Never skip build/test verification
