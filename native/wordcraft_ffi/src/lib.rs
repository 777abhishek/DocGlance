//! wordcraft_ffi — C-ABI wrapper around WordCraft's DOCX parser, layout engine, and renderer.
//!
//! Exposes 8 functions that QuickView calls via C# P/Invoke:
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
        };

        let rendered = render_page(&doc.doc, page, scale as f32, &opts);

        // Convert premultiplied RGBA → straight RGBA
        let mut straight = rendered.to_straight();
        straight.shrink_to_fit();

        let w = rendered.width;
        let h = rendered.height;
        let len = straight.len();
        let ptr = straight.as_mut_ptr();
        std::mem::forget(straight); // C# now owns the buffer

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
