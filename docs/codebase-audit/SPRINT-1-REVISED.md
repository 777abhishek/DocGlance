# Sprint 1 — REVISED: Remove WebView2

> **Decision:** QuickView is a file viewer, not a web browser. Remove WebView2 entirely and treat HTML/Markdown as viewable source code.

---

## 🎯 Goal

Remove all WebView2 dependencies and treat HTML/Markdown files as **source code to view**, not web content to execute.

**Estimated time:** 2 days (down from 5 days)

---

## ✅ Benefits of This Approach

1. **Eliminates all WebView2 security vulnerabilities** (SEC-01, SEC-02, SEC-08, PERF-05)
2. **Reduces memory footprint** — no browser process
3. **Faster startup** — no WebView2 initialization
4. **Consistent UX** — all text files use the same viewer
5. **Simpler codebase** — less code to maintain
6. **No JavaScript risk** — impossible to execute malicious scripts

---

## 📋 Implementation Tasks

### Task 1: Update HtmlRenderer (0.5 days)

**Current:** Loads HTML in WebView2  
**New:** Display HTML source code with syntax highlighting

- [ ] Open `Rendering/Web/HtmlRenderer.cs`
- [ ] Change return type from `WebView2` to use `CodeRenderer`
- [ ] Remove `WebView2` instantiation
- [ ] Call `CodeRenderer.RenderAsync()` instead
- [ ] Update `SupportedExtensions` to include `.html`, `.htm`

**New code:**
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuickView.Core.Interfaces;
using QuickView.Rendering.Code;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Web;

public sealed class HtmlRenderer : IFileRenderer
{
    public string[] SupportedExtensions => [".html", ".htm"];

    public async Task<UIElement?> RenderAsync(string filePath, CancellationToken ct = default)
    {
        // Render HTML as source code, not executed content
        var result = await CodeRenderer.RenderAsync(filePath, isDarkTheme: true, ct);
        
        var richTextBlock = new RichTextBlock
        {
            FontFamily = result.FontFamily,
            FontSize = result.FontSize,
            LineHeight = result.LineHeight,
            TextWrapping = result.TextWrapping,
            Padding = result.Padding,
            IsTextSelectionEnabled = true,
        };

        foreach (var para in result.Paragraphs)
            richTextBlock.Blocks.Add(para);

        var scrollViewer = new ScrollViewer
        {
            Content = richTextBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        return scrollViewer;
    }
}
```

---

### Task 2: Update MarkdownRenderer (1 day)

**Current:** Converts MD → HTML, displays in WebView2  
**New:** Display Markdown source code with syntax highlighting

**Option A: Simple (Recommended)**
Show raw Markdown source with syntax highlighting

- [ ] Open `Rendering/Web/MarkdownRenderer.cs`
- [ ] Remove all HTML conversion logic (~300 lines)
- [ ] Remove `BuildHtml`, `InlineFormat`, `EscapeHtml` methods
- [ ] Change to use `CodeRenderer.RenderAsync()`
- [ ] Update supported extensions

**New code (Option A):**
```csharp
using Microsoft.UI.Xaml.Controls;
using QuickView.Rendering.Code;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Rendering.Web;

/// <summary>
/// Markdown file renderer — displays source code with syntax highlighting.
/// Supports: .md, .markdown, .mdx
/// </summary>
public static class MarkdownRenderer
{
    public static async Task<ScrollViewer> RenderAsync(string filePath, bool isDarkTheme, CancellationToken ct = default)
    {
        // Render Markdown as source code
        var result = await CodeRenderer.RenderAsync(filePath, isDarkTheme, ct);
        
        var richTextBlock = new RichTextBlock
        {
            FontFamily = result.FontFamily,
            FontSize = result.FontSize,
            LineHeight = result.LineHeight,
            TextWrapping = result.TextWrapping,
            Padding = result.Padding,
            IsTextSelectionEnabled = true,
        };

        foreach (var para in result.Paragraphs)
            richTextBlock.Blocks.Add(para);

        return new ScrollViewer
        {
            Content = richTextBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }
}
```

**Option B: Formatted Preview (Future Enhancement)**
Use RichTextBlock with Markdig to render Markdown natively (no HTML/WebView2)
- Keep for Phase 5 if you want formatted Markdown preview
- Would require parsing Markdig AST → WinUI Paragraph/Run objects

---

### Task 3: Update CodeRenderer Extensions (0.25 days)

Add `.md`, `.markdown`, `.html`, `.htm` to CodeRenderer's supported extensions

- [ ] Open `Rendering/Code/CodeRenderer.cs`
- [ ] Update `GetFirstWordKeywords` to include Markdown patterns
- [ ] Add basic Markdown syntax highlighting (optional)

**Code change:**
```csharp
private static HashSet<string> GetFirstWordKeywords(string ext) => ext switch
{
    // ... existing cases ...
    ".md" or ".markdown" or ".mdx" => new HashSet<string> {
        "##", "###", "####", "#####", "######",  // Headers
        "-", "*", "+", "1.", "2.", "3.",  // Lists
        ">", "```", "---", "***"  // Quotes, code blocks, horizontal rules
    },
    // ... rest of cases ...
```

---

### Task 4: Remove WebView2 from ViewerPage (0.25 days)

Remove MarkdownViewerHost and WebView2 handling

- [ ] Open `Features/Viewer/ViewerPage.xaml`
- [ ] Remove `<Grid x:Name="MarkdownViewerHost">` section
- [ ] Markdown files will now use `CodeViewerHost` (same as code files)

- [ ] Open `Features/Viewer/ViewerPage.xaml.cs` (or relevant partial)
- [ ] Remove WebView2-specific loading code
- [ ] Markdown/HTML now route through CodeRenderer path

---

### Task 5: Update RendererRegistry (0.25 days)

- [ ] Open `Rendering/RendererRegistry.cs`
- [ ] Verify `.html`/`.htm` route to `HtmlRenderer`
- [ ] Verify `.md`/`.markdown` route to `MarkdownRenderer`
- [ ] Both should return the same type as `CodeRenderer`

---

### Task 6: Remove WebView2 NuGet Package (0.25 days)

- [ ] Open `QuickView.csproj`
- [ ] Remove `Microsoft.Web.WebView2` package reference (if present)
- [ ] Clean and rebuild solution
- [ ] Verify no WebView2 references remain

**Verification:**
```powershell
# Search for any remaining WebView2 references
Select-String -Path *.cs,*.xaml -Pattern "WebView2" -Recurse
```

---

### Task 7: Test All File Types (0.5 days)

- [ ] Test opening `.html` file → should show source code
- [ ] Test opening `.htm` file → should show source code
- [ ] Test opening `.md` file → should show Markdown source
- [ ] Test opening `.markdown` file → should show Markdown source
- [ ] Test text selection works
- [ ] Test scrolling works
- [ ] Test search works (if implemented)
- [ ] Test with large HTML/MD files (truncation warning)
- [ ] Test with malformed HTML/MD (should display as-is)

---

## 🗑️ Files to Delete (Optional Cleanup)

After implementation:
- [ ] Delete `Rendering/Web/HtmlRenderer.cs` (replaced with new version)
- [ ] Delete `Rendering/Web/MarkdownRenderer.cs` (replaced with new version)
- Or keep but with drastically simplified code

---

## ✅ Sprint 1 Complete Criteria

- [ ] HtmlRenderer shows HTML source code (no WebView2)
- [ ] MarkdownRenderer shows Markdown source code (no WebView2)
- [ ] All WebView2 references removed from codebase
- [ ] WebView2 NuGet package removed (if exists)
- [ ] Manual testing passed (HTML, Markdown files display correctly)
- [ ] Build succeeds with 0 errors, 0 warnings
- [ ] Code committed with message: "refactor: remove WebView2, treat HTML/Markdown as source code"

---

## 📊 Impact Assessment

### Security Findings Closed
- ✅ **SEC-01:** JavaScript in WebView2 (eliminated)
- ✅ **SEC-02:** XSS in Markdown parser (eliminated - no HTML conversion)
- ✅ **SEC-08:** Missing CSP (eliminated - no HTML rendering)
- ✅ **PERF-05:** WebView2 memory leak (eliminated)

### Remaining Security Tasks
- **SEC-03:** SHA-256 installer verification (move to Sprint 2)
- **SEC-04:** pdfium.dll loading (move to Sprint 2)
- **SEC-05:** PathValidator in scanner (move to Sprint 2)

### Technical Debt Closed
- ✅ **CODE-01:** Custom Markdown parser eliminated
- ✅ **PERF-05:** WebView2 disposal issue eliminated
- ✅ **SEC-13:** WebView2 not disposed (eliminated)

---

## 🎉 Benefits Summary

**Before:**
- WebView2 dependencies
- Security vulnerabilities in web rendering
- Memory leaks from browser process
- Complex HTML/MD parsing
- ~500+ lines of custom Markdown parser

**After:**
- Zero WebView2 dependencies
- Zero web-related security vulnerabilities
- Lower memory footprint
- Simple code viewing
- Consistent UX across all text formats

---

## 🤔 User Impact

**What users see:**

| Before | After |
|--------|-------|
| HTML rendered in browser | HTML source code with syntax highlighting |
| Markdown rendered as formatted text | Markdown source code with syntax highlighting |
| Risk of malicious scripts | No script execution possible |

**User benefits:**
- Can see exactly what's in the HTML/Markdown file
- Can copy/paste source code easily
- Consistent experience with other code files
- Faster loading (no browser startup)

---

## 🔄 Future Enhancement (Optional)

**If you later want formatted Markdown preview:**

Sprint 5+: Add native Markdown rendering using Markdig + RichTextBlock
- Use `Markdig` to parse Markdown AST
- Convert AST → WinUI `Paragraph`/`Run`/`Bold`/`Italic` objects
- Display in `RichTextBlock` (native WinUI, no HTML/WebView2)
- Still zero JavaScript risk
- Would give formatted output without browser

**This is optional and can be done later if users request it.**

---

**Total Sprint 1 effort: 2 days** (down from 5 days)  
**Security vulnerabilities closed: 4 High/Medium findings**  
**Lines of code removed: ~400-500 (Markdown parser + WebView2 handling)**
