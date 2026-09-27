QuickView viewer review
PDF + text/code paths, read 2026-09-25. Source: Rendering/Pdf, Features/Viewer, Rendering/Code, Rendering/Text, Rendering/Web.

Ship-blocker before images or Office
PDFium is the right engine and first-page-first is the right load sequence. The remaining cost is architectural: one global PDFium lock is split across two APIs, every page is laid out at bitmap pixels inside a non-virtualized StackPanel, and text/code ReadToEnd the whole file before truncating. Fix those three and open time plus RAM drop by a large factor on real documents.
5
Crash or RAM defects
8
High cost on large files
192
Claimed render DPI
768
Zoom re-raster cap (DPI)
What is already working
Direct PDFium BGRA into a rented buffer, then WriteableBitmap, is the correct replacement for Windows.Data.Pdf PNG round-trips. Page sizes come from FPDF_GetPageSizeByIndexF without loading content. First page paints before prefetch. Visible pages are prioritized, far pages are evicted (±6). Text/code load on a worker thread. Those choices should stay.

Hot path as it runs today
Derived from ViewerPage.Pdf.cs, PdfRenderer.cs, PdfTextService.cs, ViewerPage.Text.cs. Letter page = 612×792 pt.

Stage	What happens	Cost on a 200-page PDF
Open	FPDF_LoadDocument + size loop; then N Border+Grid placeholders in a StackPanel	UI thread walks 200 visual nodes before first paint finishes settling
First page	BGRA @ RenderDpi, copy into WriteableBitmap on UI thread	~7.4 MiB bitmap (612×792 × 192/96 × 4 bytes)
Scroll	Prefetch ±2, keep ±6, drop Image sources outside window	~13 pages × 7.4 MiB ≈ 96 MiB live page bitmaps
Zoom	Keep layout size, re-raster up to 768 DPI, Stretch.Fill	One page at cap ≈ 118 MiB; a few visible pages can spike hundreds of MiB
Find	PdfTextService opens the file again via PDFiumCore (second init + second lock)	Full-doc parse concurrent with render; PDFium is not safe for that
Severity map
ID	Area	Issue	Why it hurts
C1
PDF	Two PDFium entry points, two locks	PdfRenderer uses custom P/Invoke + PdfiumLock. PdfTextService uses PDFiumCore + SemaphoreSlim. Search, copy-text, and raster can run on the same pdfium.dll without a shared gate. That is undefined behavior and a crash class, not a polish item.
C2
PDF	Dispose races the renderer	CancelLoad / new document disposes PdfDocumentState while Task.Run still holds the IntPtr. Dispose does not zero Document, so a second Dispose can CloseDocument twice. Prefetch and zoom re-render are fire-and-forget.
C3
PDF	FPDF_LoadDocument as LPStr	Ansi marshalling. Paths with non-ANSI characters fail silently (null handle). Use UTF-8, FPDF_LoadMemDocument, or a file-access callback.
C4
Text/code	ReadToEnd then truncate	MaxChars is 1e6 / 5e5, but StreamReader.ReadToEnd loads the entire file first. A 200 MB log still allocates the full string. LoadTextFileAsync then assigns that string to a TextBox with no cap at all.
C5
PDF layout	No virtualization	PagesPanel is a StackPanel. Every page gets a Border whose Grid is sized to physical pixels (letter about 1224x1584). An 800-page doc builds 800 layout nodes plus 800 thumbnail VMs on open. Evicting the Image does not evict the layout slot.
H1
PDF zoom	Bitmap space != layout space	NaturalZoom = 96/RenderDpi, so the visual tree is huge and ScrollViewer.ZoomMode scales it down. Chrome/PDF.js layout in CSS pixels and rasterize at devicePixels x zoom. ReRenderVisiblePagesAsync then allocates up to 768 DPI into the same slot.
H2
Code	One Paragraph per line in RichTextBlock	10k Paragraph + Run + line-number TextBlock is a full visual tree, not a viewport. Search splits Runs into InlineUIContainer borders on every keystroke with no debounce.
H3
PDF search	Re-opens the file from disk	SearchCore / ExtractTextCore call FPDF_LoadDocument again instead of using _pdfState. Outline uses a third parser (PdfSharpCore). Three copies of document structure in memory.
H4
PDF RAM	Thumbnails never evicted	_renderedThumbs only grows. Sidebar prefetch +/-4 is fine; visited pages keep 96 DPI WriteableBitmaps forever.
H5
Markdown	WebView2 per file	EnsureCoreWebView2Async starts a Chromium process for a .md preview. That fights the low-RAM goal. A RichTextBlock or Win2D markdown path is cheaper for Quick Look.
M1
PDF	DPI uses /96 on PDF points	PDF user space is 1/72 in. Formula pt * dpi / 96 yields 144 dpi on an 8.5 in page when RenderDpi is 192. Fit-width comments still mention 384. Decide DIP layout vs true inches and use one scale everywhere (render, search rects, NaturalZoom).
M2
PDF	FPDF_LCD_TEXT + Stretch.Fill	Subpixel glyphs baked at one size, then scaled. At non-100% zoom, text fringes. Use grayscale AA when the blit will be scaled; LCD only when 1:1 device pixels.
Memory model (derived)
BGRA uncompressed. Formula used in code: px = round(pt × dpi / 96). Letter 612×792. Source: PdfRenderer.RenderPageAsync + ViewerPage.Zoom.cs cap.

Raster	Pixels	Bytes / page	When it happens
Thumbnail 96 DPI	612 × 792	1.9 MiB	Sidebar, never freed
Base 192 DPI	1224 × 1584	7.4 MiB	Normal view, ±6 kept
200% zoom (384)	2448 × 3168	29.6 MiB	ReRenderVisiblePagesAsync
Cap 768 DPI	4896 × 6336	118.4 MiB	High zoom, visible pages only
True 192 dpi using /72 would be 1632×2112 (13.1 MiB). The /96 shortcut under-renders and still over-lays out because layout uses those pixel counts as XAML Width/Height.

PDF: faster, lower RAM
1. One PDFium session
Delete the duplicate P/Invoke surface or wrap PDFiumCore only. One FPDF_InitLibrary. One lock around every native call (render, text, outline, close). Search and copy use the open FpdfDocumentT, not a second LoadDocument. FPDFBookmark_* for TOC; drop PdfSharpCore from the hot path.

2. Layout in DIPs, raster at device pixels
Page slot size = page points mapped to DIPs (or 72-dpi inches converted once). Image.Width/Height stay at that DIP size. Bitmap size = DIP × XamlRoot.RasterizationScale × userZoom, capped (e.g. 2.5× display, not 768 DPI absolute). ScrollViewer ZoomMode should be Disabled; you already implement Ctrl+wheel. This is how Sumatra, Chrome PDFium, and Rune-class viewers avoid a 160k-tall layout tree.

3. Virtualize pages
ItemsRepeater + StackLayout (or a custom VirtualizingLayout with GetPageSize heights). Realize ±2 viewports of Borders. Prefetch bitmaps into an LRU of decoded frames keyed by (page, dpiBucket). Evict WriteableBitmap by dropping Source and letting GC collect; do the same for thumbs.

4. Raster pipeline
Keep FPDFBitmap_CreateEx into pooled buffers. Prefer SoftwareBitmap / CanvasVirtualImageSource / Composition surface over WriteableBitmap.PixelBuffer.AsStream() copies on the UI thread. Optional: paint a 96 DPI preview in the slot, then replace with display-DPI (progressive). Reload pages with FPDF_RenderPageBitmapWithMatrix instead of closing/reloading the page handle every time; cache loaded pages for the visible set.

5. Lifecycle
Generation counter on the document. Render tasks capture gen; if gen != current, return the ArrayPool buffer and skip UI apply. Dispose zeros the handle after CloseDocument. Prompt for password via FPDF_GetLastError / FPDF_PASSWORD. Copy pdfium.dll next to the exe is already implied by DllImport name “pdfium”.

Text and code
Stream, do not slurp
FileStream with SequentialScan, read at most MaxChars (or a byte budget) with a Decoder. Never ReadToEnd. Show a truncation banner with file length from FileInfo, which you already compute after the damage is done.

Virtualize lines
For Quick Look, an ItemsRepeater of lines (plain TextBlock) beats RichTextBlock-with-10k-paragraphs and beats WebView2 Monaco. Keep ~200 realized rows. Line numbers from the realized index, not a 10k-line string. Syntax color: one brush per line is fine; Tokeniser exists but is unused in the hot path — that is actually good until lines are virtualized.

Search
Debounce code find like PDF (300 ms). Index matches as (line, column) on the raw string, not by mutating the visual tree. Highlight only the visible line containers. TextBox Select() is OK for small files; for large files overlay a canvas or per-row background.

Editing
SaveButton never enables: _textHasChanges is set false and never true; TextBox is IsReadOnly. Either drop Save from a viewer, or make it explicitly an editor with change tracking. GetCodeText reconstructing from Runs will drop unmatched newlines and break files after search highlighting.

Dead architecture
IFileRenderer, RendererRegistry, and TextRenderer are not on the open path. ViewerPage.LoadDocumentAsync switches on extension. That duplication will hurt when images and Office land. Wire the registry or delete the stubs so the next format cannot fork a third path.
Bugs that are not “optimization”
Symptom	Cause in code
Search highlights misaligned after DPI comments drifted	PdfTextService converts with RenderDpi/96 into pageW/pageH device space; DrawHighlights comments still say 384 and “no scale”. Today it happens to match because both use RenderDpi — it will break the moment layout DIPs and bitmap pixels diverge (which they should).
Rotate distorts or gaps	Rotate() sets Border.Width = physW / DisplayScale while placeholders were created at physW. Mixed DIP and pixel spaces.
Home PDF thumbs still slow	PdfThumbnailProvider still uses Windows.Data.Pdf PNG fallback after Shell thumb fails.
Namespaces mixed	App is Document.App; Save As uses (Application.Current as Document.App). Easy null and a packaging confusion, not a render bug.
Password PDFs	FPDF_LoadDocument(path, null) only. No retry UI.
Recommended order after this session
Do first	Change	Payoff
1	Single PDFium facade + shared lock; search/copy on open doc	Stops native crashes; cuts search RAM
2	Capped streaming read for text/code; cap LoadTextFileAsync	Stops log-file OOM
3	Document generation + safe dispose; zero native handle	Stops use-after-free on tab close / file switch
4	DIP page slots + disable ScrollViewer zoom; raster at display×zoom with a real cap (e.g. 2×)	Biggest RAM and scroll smoothness win
5	ItemsRepeater pages + LRU bitmaps/thumbs	Large PDFs open instantly
6	Virtualized line list for code; debounce find	Large source files usable
7	Then images (DecodePixelWidth, background decode) and Office	Do not add WebView2 or COM Office until 1–6 land
What not to do
Do not raise RenderDpi back toward 384. Do not keep ZoomMode Enabled on a physical-pixel canvas. Do not tokenize every character into XAML Runs. Do not add another PDF library. Do not start Office COM or a second WebView2 instance until the viewer shell is virtualized.