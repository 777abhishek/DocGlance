//! wordcraft_ffi — C-ABI wrapper around WordCraft's DOCX parser, layout engine, and renderer.
//!
//! Exposes 8 functions that DocGlance calls via C# P/Invoke:
//!   wc_open_docx          — parse a DOCX file from bytes, layout document → opaque handle
//!   wc_page_count         — number of laid-out pages
//!   wc_get_page_size      — page dimensions in PDF points (width × height)
//!   wc_render_page        — rasterise one page → straight RGBA8 pixel buffer
//!   wc_extract_page_text  — extract all text on a page into caller-supplied UTF-8 buffer
//!   wc_search_text        — search for text across all pages, return matches with bounding rects
//!   wc_free_image         — free a WcImage pixel buffer
//!   wc_free_document      — free the opaque WcDocument handle

use std::panic;
use std::slice;
use std::fmt::Write as FmtWrite;

use wordcraft_doc::props::Rgb;
use wordcraft_doc::{Document, Pos, StoryRef};
use wordcraft_layout::display::DisplayOptions;
use wordcraft_layout::{DocLayout, LayoutCache, LayoutOptions, Placed, ViewMode};
use wordcraft_render::{render_page, RenderOptions};

// ── Public types ──────────────────────────────────────────────────────────────

/// Opaque handle returned by wc_open_docx.
pub struct WcDocument {
    pub doc: Document,
    pub layout: DocLayout,
}

/// Pixel buffer returned by wc_render_page.
/// pixels: heap-allocated straight RGBA8 bytes (width × height × 4).
/// C# must call wc_free_image to release.
#[repr(C)]
pub struct WcImage {
    pub pixels: *mut u8,
    pub width: u32,
    pub height: u32,
    pub len: usize, // == width * height * 4
}

impl WcImage {
    fn null() -> Self {
        WcImage {
            pixels: std::ptr::null_mut(),
            width: 0,
            height: 0,
            len: 0,
        }
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Catch any Rust panic that crosses the FFI boundary (UB otherwise).
macro_rules! catch {
    ($fallback:expr, $body:block) => {
        match panic::catch_unwind(panic::AssertUnwindSafe(|| $body)) {
            Ok(v) => v,
            Err(_) => $fallback,
        }
    };
}

fn rgba_to_premul_bgra_inplace(buf: &mut [u8]) {
    for px in buf.chunks_exact_mut(4) {
        let r = px[0];
        let g = px[1];
        let b = px[2];
        let a = px[3];
        if a == 255 {
            px[0] = b;
            px[1] = g;
            px[2] = r;
        } else if a == 0 {
            px[0] = 0;
            px[1] = 0;
            px[2] = 0;
        } else {
            px[0] = ((b as u16 * a as u16 + 127) / 255) as u8;
            px[1] = ((g as u16 * a as u16 + 127) / 255) as u8;
            px[2] = ((r as u16 * a as u16 + 127) / 255) as u8;
        }
    }
}

// ── Exported functions ────────────────────────────────────────────────────────

/// Parse a DOCX file from a byte slice and compute layout.
///
/// Returns an opaque pointer on success, null on failure.
/// Caller must free with wc_free_document.
#[no_mangle]
pub extern "C" fn wc_open_docx(data: *const u8, len: usize) -> *mut WcDocument {
    catch!(std::ptr::null_mut(), {
        if data.is_null() || len == 0 {
            return std::ptr::null_mut();
        }

        // SAFETY: C# caller has pinned the managed byte[] for the duration of this call.
        let bytes = unsafe { slice::from_raw_parts(data, len) };

        let doc = match wordcraft_docx::read(bytes) {
            Ok(d) => d,
            Err(_) => return std::ptr::null_mut(),
        };

        let mut cache = LayoutCache::new();
        let opts = LayoutOptions {
            view: ViewMode::Print,
            web_width: 0.0,
            show_hidden: false,
            hide_deleted: true,
            proofing: false,
            comment_balloons: false,
            deletions_in_balloons: false,
            field_codes: false,
            ..Default::default()
        };

        let layout = wordcraft_layout::layout(&doc, &mut cache, &opts);

        let wc_doc = Box::new(WcDocument { doc, layout });
        Box::into_raw(wc_doc)
    })
}

/// Returns the number of pages in the laid out DOCX document.
#[no_mangle]
pub extern "C" fn wc_page_count(doc: *const WcDocument) -> u32 {
    catch!(0, {
        if doc.is_null() {
            return 0;
        }
        let doc = unsafe { &*doc };
        doc.layout.pages.len() as u32
    })
}

/// Writes the page's width and height in points into the out-parameters.
#[no_mangle]
pub extern "C" fn wc_get_page_size(
    doc: *const WcDocument,
    index: u32,
    width_pt: *mut f64,
    height_pt: *mut f64,
) {
    catch!((), {
        if doc.is_null() || width_pt.is_null() || height_pt.is_null() {
            return;
        }
        let doc = unsafe { &*doc };
        let pages = &doc.layout.pages;

        if let Some(page) = pages.get(index as usize) {
            unsafe {
                *width_pt = page.w as f64;
                *height_pt = page.h as f64;
            }
        } else if let Some(first) = pages.first() {
            unsafe {
                *width_pt = first.w as f64;
                *height_pt = first.h as f64;
            }
        } else {
            // Default US Letter 8.5 x 11 in points
            unsafe {
                *width_pt = 612.0;
                *height_pt = 792.0;
            }
        }
    });
}

/// Rasterise page `index` at `scale` pixels-per-point.
///
/// scale = 1.0 → standard 72 dpi
/// scale = 2.0 → crisp on 1080p / high resolution
///
/// Returns a WcImage whose `pixels` buffer is straight RGBA8.
/// C# MUST call wc_free_image after copying to a WriteableBitmap.
/// Returns WcImage::null() on error.
#[no_mangle]
pub extern "C" fn wc_render_page(doc: *const WcDocument, index: u32, scale: f64) -> WcImage {
    catch!(WcImage::null(), {
        if doc.is_null() {
            return WcImage::null();
        }
        let doc = unsafe { &*doc };
        let pages = &doc.layout.pages;

        let Some(page) = pages.get(index as usize) else {
            return WcImage::null();
        };

        let opts = RenderOptions {
            display: DisplayOptions::default(),
            paper: Rgb(255, 255, 255),
            mark_color: Rgb(128, 128, 128),
            text_darkening: false,
            dark: false,
            dark_paper: wordcraft_render::DARK_PAPER,
            read_paper: None,
        };

        let rendered = render_page(&doc.doc, page, scale as f32, &opts);
        let w = rendered.width;
        let h = rendered.height;
        let mut pixels = rendered.to_straight();
        drop(rendered);
        rgba_to_premul_bgra_inplace(&mut pixels);

        let len = pixels.len();
        let ptr = pixels.as_mut_ptr();
        std::mem::forget(pixels);

        WcImage {
            pixels: ptr,
            width: w,
            height: h,
            len,
        }
    })
}

/// Extract all text on page `index` into `buf` (UTF-8, NOT null-terminated).
/// Returns the number of bytes written.
#[no_mangle]
pub extern "C" fn wc_extract_page_text(
    doc: *const WcDocument,
    index: u32,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || buf.is_null() || buf_len == 0 {
            return 0;
        }
        let doc = unsafe { &*doc };
        let pages = &doc.layout.pages;

        let Some(page) = pages.get(index as usize) else {
            return 0;
        };

        let mut text = String::new();
        for item in &page.items {
            if let Placed::Lines { story, path, para, l0, l1, .. } = item {
                if let Some(p) = doc.doc.para(*story, path) {
                    for li in *l0..*l1 {
                        if let Some(l) = para.lines.get(li) {
                            if let Some(sub) = p.text.get(l.start..l.stop) {
                                text.push_str(sub);
                                text.push('\n');
                            }
                        }
                    }
                }
            }
        }

        let bytes = text.as_bytes();
        let copy = bytes.len().min(buf_len);
        if copy == 0 {
            return 0;
        }

        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copy);
        }
        copy
    })
}

/// Search for `query` text across all pages.
/// Returns semicolon-separated rectangles: "matchIndex,pageIndex,x,y,width,height;..."
/// Write results to `buf` (UTF-8). Returns bytes written, or required capacity if too small.
#[no_mangle]
pub extern "C" fn wc_search_text(
    doc: *const WcDocument,
    query: *const u8,
    query_len: usize,
    case_sensitive: bool,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || query.is_null() || buf.is_null() {
            return 0;
        }
        if query_len == 0 || buf_len == 0 {
            return 0;
        }

        let doc = unsafe { &*doc };
        let query_bytes = unsafe { slice::from_raw_parts(query, query_len) };
        let query_str = match std::str::from_utf8(query_bytes) {
            Ok(s) => s,
            Err(_) => return 0,
        };

        if query_str.is_empty() {
            return 0;
        }

        let pat = regex::escape(query_str);
        let re = match regex::RegexBuilder::new(&pat)
            .case_insensitive(!case_sensitive)
            .build()
        {
            Ok(r) => r,
            Err(_) => return 0,
        };

        let mut results = String::new();
        let mut match_index = 0usize;

        // Search body story paragraphs
        for path in doc.doc.para_paths(StoryRef::Body) {
            let Some(p) = doc.doc.para(StoryRef::Body, &path) else {
                continue;
            };

            let mut live = String::new();
            let mut spans: Vec<(usize, usize, usize)> = Vec::new();
            for (r, c) in p.run_ranges() {
                let Some(t) = p.text.get(r.clone()).filter(|_| c.del.is_none()) else {
                    continue;
                };
                match spans.last_mut() {
                    Some((_, raw, n)) if *raw + *n == r.start => *n += t.len(),
                    _ => spans.push((live.len(), r.start, t.len())),
                }
                live.push_str(t);
            }

            for (at, raw, n) in spans {
                if at + n > live.len() {
                    continue;
                }
                let live_slice = &live[at..at + n];
                for m in re.find_iter(live_slice) {
                    if m.is_empty() {
                        continue;
                    }
                    let a_off = raw + m.start();
                    let b_off = raw + m.end();

                    let a_pos = Pos {
                        story: StoryRef::Body,
                        path: path.clone(),
                        off: a_off,
                    };
                    let b_pos = Pos {
                        story: StoryRef::Body,
                        path: path.clone(),
                        off: b_off,
                    };

                    // Compute highlight rects per page
                    let rects = doc.layout.selection_rects(&doc.doc, &a_pos, &b_pos, 0);

                    let mut match_rects = Vec::new();
                    for (page_idx, r) in rects {
                        let w = (r.w as f64).max(4.0);
                        let h = (r.h as f64).max(4.0);
                        match_rects.push((page_idx, r.x, r.y, w, h));
                    }

                    if !match_rects.is_empty() {
                        for (page_idx, x, y, width, height) in match_rects {
                            let _ = write!(
                                results,
                                "{},{},{:.2},{:.2},{:.2},{:.2};",
                                match_index, page_idx, x, y, width, height
                            );
                        }
                        match_index += 1;
                    }

                    if match_index >= 20_000 {
                        break;
                    }
                }
                if match_index >= 20_000 {
                    break;
                }
            }
            if match_index >= 20_000 {
                break;
            }
        }

        copy_result_bytes(results.as_bytes(), buf, buf_len)
    })
}

fn copy_result_bytes(bytes: &[u8], buf: *mut u8, buf_len: usize) -> usize {
    if bytes.len() > buf_len {
        return bytes.len();
    }
    if bytes.is_empty() {
        return 0;
    }

    unsafe {
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, bytes.len());
    }
    bytes.len()
}

#[cfg(test)]
mod tests {
    use super::copy_result_bytes;

    #[test]
    fn reports_required_capacity_without_writing_partial_utf8() {
        let bytes = "résultat".as_bytes();
        let mut output = [0xAA; 3];

        let required = copy_result_bytes(bytes, output.as_mut_ptr(), output.len());

        assert_eq!(required, bytes.len());
        assert_eq!(output, [0xAA; 3]);
    }

    #[test]
    fn writes_complete_search_result_when_capacity_is_sufficient() {
        let bytes = b"0,2,10.00,20.00,30.00,8.00;";
        let mut output = [0; 64];

        let written = copy_result_bytes(bytes, output.as_mut_ptr(), output.len());

        assert_eq!(written, bytes.len());
        assert_eq!(&output[..written], bytes);
    }
}

/// Free a WcImage pixel buffer.
/// MUST be called after C# has finished copying from WcImage.pixels.
#[no_mangle]
pub extern "C" fn wc_free_image(img: WcImage) {
    if img.pixels.is_null() || img.len == 0 {
        return;
    }
    // SAFETY: pixels was allocated by Rust Vec<u8> with capacity == len.
    unsafe {
        drop(Vec::from_raw_parts(img.pixels, img.len, img.len));
    }
}

/// Free a WcDocument handle obtained from wc_open_docx.
#[no_mangle]
pub extern "C" fn wc_free_document(doc: *mut WcDocument) {
    if doc.is_null() {
        return;
    }
    unsafe {
        drop(Box::from_raw(doc));
    }
}

// ── PDF Export ────────────────────────────────────────────────────────────────

/// PDF buffer returned by wc_export_to_pdf.
/// C# must call wc_free_pdf_buffer to release.
#[repr(C)]
pub struct WcPdfBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl WcPdfBuffer {
    fn null() -> Self {
        WcPdfBuffer {
            data: std::ptr::null_mut(),
            len: 0,
        }
    }
}

/// Export document to PDF.
/// Returns WcPdfBuffer. C# MUST call wc_free_pdf_buffer after copying bytes.
/// Returns WcPdfBuffer::null() on error.
#[no_mangle]
pub extern "C" fn wc_export_to_pdf(doc: *const WcDocument) -> WcPdfBuffer {
    catch!(WcPdfBuffer::null(), {
        if doc.is_null() {
            return WcPdfBuffer::null();
        }
        let doc = unsafe { &*doc };

        // Export to PDF using WordCraft's PDF engine with default options
        let opts = wordcraft_pdf::PdfOptions::default();
        let mut pdf_bytes = match wordcraft_pdf::export(&doc.doc, &opts) {
            Ok(bytes) => bytes,
            Err(_) => return WcPdfBuffer::null(),
        };

        if pdf_bytes.is_empty() {
            return WcPdfBuffer::null();
        }

        pdf_bytes.shrink_to_fit();
        let len = pdf_bytes.len();
        let ptr = pdf_bytes.as_mut_ptr();
        std::mem::forget(pdf_bytes); // C# now owns the buffer

        WcPdfBuffer { data: ptr, len }
    })
}

/// Free a PDF buffer returned by wc_export_to_pdf.
#[no_mangle]
pub extern "C" fn wc_free_pdf_buffer(buf: WcPdfBuffer) {
    if buf.data.is_null() || buf.len == 0 {
        return;
    }
    unsafe {
        drop(Vec::from_raw_parts(buf.data, buf.len, buf.len));
    }
}

// ── Text Selection APIs ───────────────────────────────────────────────────────

/// Text position in document for text selection.
/// Represents a position between characters (like a text cursor).
/// For Word documents, offset is a page-relative UTF-8 byte offset.
#[repr(C)]
pub struct WcTextPosition {
    pub page: u32,       // Page index where this position appears
    pub offset: u32,     // Character offset within the page (simplified)
    pub reserved1: u32,  // Reserved for future use (story index)
    pub reserved2: u32,  // Reserved for future use (path length)
}

impl WcTextPosition {
    #[allow(dead_code)]
    fn null() -> Self {
        WcTextPosition {
            page: u32::MAX,
            offset: 0,
            reserved1: 0,
            reserved2: 0,
        }
    }
}

/// Hit test: find the approximate text position for a given point on a page.
/// 
/// Returns true if text was found near the point, false otherwise.
/// The position is written to out_pos as a page-relative UTF-8 byte offset.
/// 
/// Coordinates are in PDF points relative to the page's top-left corner.
/// 
/// Uses WordCraft's page layout to map the point to the nearest body-text caret.
#[no_mangle]
pub extern "C" fn wc_hit_test(
    doc: *const WcDocument,
    page_index: u32,
    x: f64,
    y: f64,
    out_pos: *mut WcTextPosition,
) -> bool {
    catch!(false, {
        if doc.is_null() || out_pos.is_null() {
            return false;
        }
        
        let doc = unsafe { &*doc };
        let page_index = page_index as usize;
        
        if page_index >= doc.layout.pages.len() {
            return false;
        }
        
        let page = &doc.layout.pages[page_index];
        let pos = wordcraft_layout::hit::page_lines(page, StoryRef::Body)
            .into_iter()
            .min_by(|left, right| {
                let left_distance =
                    line_hit_distance(left.top, left.bottom, left.left, left.right, x, y);
                let right_distance =
                    line_hit_distance(right.top, right.bottom, right.left, right.right, x, y);
                left_distance
                    .0
                    .total_cmp(&right_distance.0)
                    .then_with(|| left_distance.1.total_cmp(&right_distance.1))
            })
            .map(|line| Pos {
                story: line.story,
                path: line.path.clone(),
                off: line.para.off_at_x(line.li, x as f32 - line.x),
            })
            .or_else(|| doc.layout.hit(page_index, x as f32, y as f32, StoryRef::Body));
        let Some(pos) = pos else {
            return false;
        };

        let mut current_offset = 0u32;
        for item in &page.items {
            if let Placed::Lines { story, path, para, l0, l1, .. } = item {
                for li in *l0..*l1 {
                    let Some(line) = para.lines.get(li) else {
                        continue;
                    };

                    let line_len = line.stop.saturating_sub(line.start) as u32;
                    if *story == pos.story
                        && *path == pos.path
                        && line.start <= pos.off
                        && pos.off <= line.stop
                    {
                        let out = unsafe { &mut *out_pos };
                        out.page = page_index as u32;
                        out.offset = current_offset.saturating_add(
                            (pos.off - line.start) as u32,
                        );
                        out.reserved1 = 0;
                        out.reserved2 = 0;
                        return true;
                    }

                    current_offset = current_offset.saturating_add(line_len);
                }
            }
        }

        false
    })
}

/// Get selection rectangles for a text range within a single page.
/// 
/// Offsets are UTF-8 byte offsets from the beginning of the page's laid-out text.
/// 
/// Returns the number of floats written to buffer (or required if buffer is too small).
/// Each rectangle is 4 floats: x, y, width, height (in PDF points).
/// 
/// Buffer layout: [x0, y0, w0, h0, x1, y1, w1, h1, ...]
/// 
/// Note: This is a best-effort implementation. For precise selection, the calling
/// code should use the actual Pos (story/path/offset) from WordCraft's document model.
#[no_mangle]
pub extern "C" fn wc_get_selection_rects(
    doc: *const WcDocument,
    page_index: u32,
    start_offset: u32,
    end_offset: u32,
    buf: *mut f32,
    buf_capacity: usize, // Total number of floats, not rectangles
) -> usize {
    catch!(0, {
        if doc.is_null() {
            return 0;
        }
        
        let doc = unsafe { &*doc };
        let page_index = page_index as usize;
        
        if page_index >= doc.layout.pages.len() {
            return 0;
        }
        
        // Normalize offsets
        let start = start_offset.min(end_offset) as usize;
        let end = start_offset.max(end_offset) as usize;
        
        if start == end {
            return 0;
        }
        
        let page = &doc.layout.pages[page_index];
        
        // Build a mapping of page-relative offsets to document Pos
        // by iterating through all text on the page
        let mut offset_to_pos = Vec::new();
        
        for item in &page.items {
            if let Placed::Lines { story, path, para, l0, l1, .. } = item {
                let Some(_p) = doc.doc.para(*story, path) else {
                    continue;
                };
                
                for li in *l0..*l1 {
                    let Some(line) = para.lines.get(li) else {
                        continue;
                    };
                    
                    // Map each character in this line
                    for char_off in line.start..line.stop {
                        offset_to_pos.push(Pos {
                            story: *story,
                            path: path.clone(),
                            off: char_off,
                        });
                    }
                }
            }
        }
        
        // Get start and end Pos
        let Some(start_pos) = offset_to_pos.get(start) else {
            return 0;
        };
        let Some(mut end_pos) = offset_to_pos.get(end.saturating_sub(1)).cloned() else {
            return 0;
        };
        end_pos.off = end_pos.off.saturating_add(1);
        
        // Use WordCraft's selection_rects to get the actual geometry
        let rects = doc.layout.selection_rects(&doc.doc, start_pos, &end_pos, 0);
        
        // Filter to only this page and collect rectangles
        let page_rects: Vec<_> = rects
            .into_iter()
            .filter(|(pi, _)| *pi == page_index)
            .map(|(_, r)| (r.x, r.y, r.w, r.h))
            .collect();
        
        let rect_count = page_rects.len();
        
        // If buffer is null or too small, just return required size
        if buf.is_null() || buf_capacity < rect_count * 4 {
            return rect_count * 4; // Return required float count
        }
        
        // Write rectangles to buffer
        let buf_slice = unsafe { slice::from_raw_parts_mut(buf, rect_count * 4) };
        
        for (i, (x, y, w, h)) in page_rects.iter().enumerate() {
            buf_slice[i * 4] = *x;
            buf_slice[i * 4 + 1] = *y;
            buf_slice[i * 4 + 2] = *w;
            buf_slice[i * 4 + 3] = *h;
        }
        
        rect_count * 4 // Return number of floats written
    })
}

/// Get the page's laid-out text length in UTF-8 bytes.
#[no_mangle]
pub extern "C" fn wc_get_page_char_count(
    doc: *const WcDocument,
    page_index: u32,
) -> u32 {
    catch!(0, {
        if doc.is_null() {
            return 0;
        }
        
        let doc = unsafe { &*doc };
        let page_index = page_index as usize;
        
        if page_index >= doc.layout.pages.len() {
            return 0;
        }
        
        let page = &doc.layout.pages[page_index];
        let mut char_count = 0u32;
        
        for item in &page.items {
            if let Placed::Lines { story, path, para, l0, l1, .. } = item {
                let Some(_p) = doc.doc.para(*story, path) else {
                    continue;
                };
                
                for li in *l0..*l1 {
                    let Some(line) = para.lines.get(li) else {
                        continue;
                    };
                    
                    let line_chars = line.stop.saturating_sub(line.start) as u32;
                    char_count = char_count.saturating_add(line_chars);
                }
            }
        }
        
        char_count
    })
}

/// Extract text between two UTF-8 byte offsets on a page.
/// This is useful for getting the selected text to put on the clipboard.
/// 
/// Returns the number of bytes written (or required if buffer is too small).
/// The text is UTF-8 encoded and NOT null-terminated.
#[no_mangle]
pub extern "C" fn wc_get_text_range(
    doc: *const WcDocument,
    page_index: u32,
    start_offset: u32,
    end_offset: u32,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() {
            return 0;
        }
        
        let doc = unsafe { &*doc };
        let page_index = page_index as usize;
        
        if page_index >= doc.layout.pages.len() {
            return 0;
        }
        
        let page = &doc.layout.pages[page_index];
        let start = start_offset.min(end_offset) as usize;
        let end = start_offset.max(end_offset) as usize;
        
        let mut text = String::new();
        let mut current_offset = 0usize;
        
        for item in &page.items {
            if let Placed::Lines { story, path, para, l0, l1, .. } = item {
                let Some(p) = doc.doc.para(*story, path) else {
                    continue;
                };
                
                for li in *l0..*l1 {
                    let Some(line) = para.lines.get(li) else {
                        continue;
                    };
                    
                    let line_start = current_offset;
                    let line_end = current_offset + line.stop.saturating_sub(line.start);
                    
                    // Check if this line overlaps with selection range
                    if line_end > start && line_start < end {
                        if let Some(line_text) = p.text.get(line.start..line.stop) {
                            // Calculate overlap
                            let extract_start = if line_start >= start { 0 } else { start - line_start };
                            let extract_end = if line_end <= end { 
                                line_text.len() 
                            } else { 
                                line_text.len().saturating_sub(line_end - end)
                            };
                            
                            if let Some(extract) = line_text.get(extract_start..extract_end) {
                                text.push_str(extract);
                            }
                        }
                        
                        // Add newline between lines (but not after the last one)
                        if line_end < end {
                            text.push('\n');
                        }
                    }
                    
                    current_offset = line_end;
                    
                    if current_offset >= end {
                        break;
                    }
                }
                
                if current_offset >= end {
                    break;
                }
            }
        }
        
        let bytes = text.as_bytes();
        
        // If buffer is null or too small, return required size
        if buf.is_null() || buf_len < bytes.len() {
            return bytes.len();
        }
        
        let copy_len = bytes.len().min(buf_len);
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copy_len);
        }
        
        copy_len
    })
}

fn line_hit_distance(
    top: f32,
    bottom: f32,
    left: f32,
    right: f32,
    x: f64,
    y: f64,
) -> (f64, f64) {
    let vertical = if y < top as f64 {
        top as f64 - y
    } else if y > bottom as f64 {
        y - bottom as f64
    } else {
        0.0
    };
    let horizontal = if x < left as f64 {
        left as f64 - x
    } else if x > right as f64 {
        x - right as f64
    } else {
        0.0
    };
    (vertical, horizontal)
}

#[cfg(test)]
mod text_selection_tests {
    use super::*;

    #[test]
    fn text_position_null_initializes_correctly() {
        let pos = WcTextPosition::null();
        assert_eq!(pos.page, u32::MAX);
        assert_eq!(pos.offset, 0);
        assert_eq!(pos.reserved1, 0);
        assert_eq!(pos.reserved2, 0);
    }

    #[test]
    fn hit_test_returns_false_for_null_doc() {
        let mut pos = WcTextPosition::null();
        let result = wc_hit_test(std::ptr::null(), 0, 0.0, 0.0, &mut pos);
        assert!(!result);
    }

    #[test]
    fn hit_test_prefers_nearest_line_over_horizontal_proximity() {
        let first_line = line_hit_distance(10.0, 20.0, 70.0, 220.0, 80.0, 25.0);
        let shorter_second_line = line_hit_distance(24.0, 34.0, 20.0, 50.0, 80.0, 25.0);

        assert!(shorter_second_line < first_line);
    }

    #[test]
    fn get_selection_rects_returns_zero_for_null_doc() {
        let result = wc_get_selection_rects(
            std::ptr::null(),
            0,
            0,
            10,
            std::ptr::null_mut(),
            0,
        );
        assert_eq!(result, 0);
    }
}
