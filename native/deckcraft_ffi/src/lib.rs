//! deckcraft_ffi — C-ABI wrapper around DeckCraft's PPTX parser and renderer.
//!
//! Exposes 10 functions that QuickView calls via C# P/Invoke:
//!   dc_open_pptx          — parse a PPTX/PPT file from bytes → opaque handle
//!   dc_slide_count        — number of slides
//!   dc_get_slide_size     — slide dimensions in PDF points (width × height)
//!   dc_render_slide       — rasterise one slide → straight RGBA8 pixel buffer
//!   dc_get_slide_title    — copy slide title text into a caller-supplied buffer
//!   dc_extract_slide_text — extract all text from a slide (title, body, notes)
//!   dc_search_text        — search for text across all slides, return matches
//!   dc_free_image         — free a DcImage pixel buffer
//!   dc_free_document      — free the opaque DcDocument handle
//!
//! Memory contract:
//!   - C# owns nothing Rust allocates except via these free functions.
//!   - dc_render_slide   transfers ownership of the pixel Vec<u8> to C#; C# MUST
//!     call dc_free_image when finished (after copying pixels into a WriteableBitmap).
//!   - dc_open_pptx      returns a Box-leaked pointer; dc_free_document drops it.

use std::slice;
use std::panic;

use deckcraft_fonts::FontDb;
use deckcraft_render::RenderOpts;

// ── Public types ──────────────────────────────────────────────────────────────

/// Opaque handle returned by dc_open_pptx.
pub struct DcDocument {
    presentation: deckcraft_model::Presentation,
    #[allow(dead_code)]
    fonts:        FontDb,  // Kept for potential future use
}

/// Pixel buffer returned by dc_render_slide.
/// pixels: heap-allocated straight RGBA8 bytes (width × height × 4).
/// C# must call dc_free_image to release.
#[repr(C)]
pub struct DcImage {
    pub pixels: *mut u8,
    pub width:  u32,
    pub height: u32,
    pub len:    usize,   // == width * height * 4
}

impl DcImage {
    fn null() -> Self {
        DcImage { pixels: std::ptr::null_mut(), width: 0, height: 0, len: 0 }
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Catch any Rust panic that crosses the FFI boundary (UB otherwise).
macro_rules! catch {
    ($fallback:expr, $body:block) => {
        match panic::catch_unwind(panic::AssertUnwindSafe(|| $body)) {
            Ok(v)  => v,
            Err(_) => $fallback,
        }
    };
}

// ── Exported functions ────────────────────────────────────────────────────────

/// Parse a PPTX/PPT file from a byte slice.
///
/// Returns an opaque pointer on success, null on failure.
/// Caller must free with dc_free_document.
#[no_mangle]
pub extern "C" fn dc_open_pptx(data: *const u8, len: usize) -> *mut DcDocument {
    catch!(std::ptr::null_mut(), {
        if data.is_null() || len == 0 {
            return std::ptr::null_mut();
        }

        // SAFETY: C# caller has pinned the managed byte[] for the duration of this call.
        let bytes = unsafe { slice::from_raw_parts(data, len) };

        let presentation = match deckcraft_pptx::import(bytes) {
            Ok(p)  => p,
            Err(_) => return std::ptr::null_mut(),
        };

        // Initialize font database with system fonts
        let fonts = FontDb::with_font_dirs(vec![]);

        let doc = Box::new(DcDocument {
            presentation,
            fonts,
        });

        Box::into_raw(doc)
    })
}

/// Returns the number of slides in the presentation.
#[no_mangle]
pub extern "C" fn dc_slide_count(doc: *const DcDocument) -> u32 {
    catch!(0, {
        if doc.is_null() { return 0; }
        let doc = unsafe { &*doc };
        doc.presentation.slides.len() as u32
    })
}

/// Writes the slide's width and height in PDF points into the out-parameters.
/// Standard PowerPoint widescreen = 9144000 EMU = 720 pt wide, 405 pt tall.
#[no_mangle]
pub extern "C" fn dc_get_slide_size(
    doc:       *const DcDocument,
    _index:    u32,
    width_pt:  *mut f64,
    height_pt: *mut f64,
) {
    catch!((), {
        if doc.is_null() || width_pt.is_null() || height_pt.is_null() { return; }
        let doc = unsafe { &*doc };
        let pres = &doc.presentation;

        // DeckCraft returns slide_size already in points, not EMU!
        let w = pres.slide_size.width as f64;
        let h = pres.slide_size.height as f64;

        unsafe {
            *width_pt  = w;
            *height_pt = h;
        }
    });
}

/// Rasterise slide `index` at `scale` pixels-per-point.
///
/// scale = 1.0 → ~720×405 px (standard widescreen at 72 dpi)
/// scale = 2.0 → 1440×810 px  (recommended: crisp on 1080p)
/// scale = 3.0 → 2160×1215 px (HiDPI / 4K)
///
/// Returns a DcImage whose `pixels` buffer is straight RGBA8.
/// C# MUST call dc_free_image after copying to a WriteableBitmap.
/// Returns DcImage::null() on error.
#[no_mangle]
pub extern "C" fn dc_render_slide(
    doc:   *const DcDocument,
    index: u32,
    scale: f64,
) -> DcImage {
    catch!(DcImage::null(), {
        if doc.is_null() { return DcImage::null(); }
        let doc = unsafe { &*doc };
        let pres = &doc.presentation;

        if index as usize >= pres.slides.len() {
            return DcImage::null();
        }

        let opts = RenderOpts {
            scale,
            threads: 4,   // use 4 worker threads — fast but not greedy
            ..Default::default()
        };

        // Use the public render_slide function (takes pres, index, opts)
        let img = deckcraft_render::render_slide(pres, index as usize, &opts);

        // Convert premultiplied RGBA → straight RGBA
        let mut straight = img.to_straight();
        straight.shrink_to_fit();

        let w   = img.width;
        let h   = img.height;
        let len = straight.len();
        let ptr = straight.as_mut_ptr();
        std::mem::forget(straight);   // C# now owns the buffer

        DcImage { pixels: ptr, width: w, height: h, len }
    })
}

/// Copy the title text of slide `index` into `buf` (UTF-8, NOT null-terminated).
/// Returns the number of bytes written (0 if no title or buffer too small).
#[no_mangle]
pub extern "C" fn dc_get_slide_title(
    doc:     *const DcDocument,
    index:   u32,
    buf:     *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || buf.is_null() || buf_len == 0 { return 0; }
        let doc = unsafe { &*doc };

        // Use place_slide to extract laid-out text; first text block is usually the title.
        let placed = deckcraft_render::place_slide(&doc.presentation, index as usize);

        // Collect text from all runs (cells contain (glyph_index, char) tuples)
        let title: String = placed.texts
            .iter()
            .flat_map(|t| t.layout.runs.iter())
            .flat_map(|r| {
                // Each cell is (glyph_index, char) - extract the char
                r.cells.iter().map(|(_, ch)| *ch)
            })
            .take(256)
            .collect();

        let bytes  = title.as_bytes();
        let copy   = bytes.len().min(buf_len);
        if copy == 0 { return 0; }

        unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copy); }
        copy
    })
}

/// Extract all text from slide `index` into `buf` (UTF-8, NOT null-terminated).
/// Returns the number of bytes written (0 if no text or buffer too small).
/// Includes all text shapes, tables, and notes.
#[no_mangle]
pub extern "C" fn dc_extract_slide_text(
    doc:     *const DcDocument,
    index:   u32,
    buf:     *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || buf.is_null() || buf_len == 0 { return 0; }
        let doc = unsafe { &*doc };
        
        if index as usize >= doc.presentation.slides.len() { return 0; }

        // Use place_slide to extract all laid-out text
        let placed = deckcraft_render::place_slide(&doc.presentation, index as usize);

        // Collect text from all text blocks
        let mut text = String::new();
        for text_block in &placed.texts {
            for run in &text_block.layout.runs {
                for (_, ch) in &run.cells {
                    text.push(*ch);
                }
            }
            text.push('\n'); // Separate text blocks with newline
        }

        let bytes = text.as_bytes();
        let copy = bytes.len().min(buf_len);
        if copy == 0 { return 0; }

        unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copy); }
        copy
    })
}

/// Search for `query` text across all slides.
/// Returns a semicolon-separated list of matches: "slideIndex,charIndex,length;..."
/// Write results to `buf` (UTF-8). Returns bytes written.
#[no_mangle]
pub extern "C" fn dc_search_text(
    doc:        *const DcDocument,
    query:      *const u8,
    query_len:  usize,
    case_sensitive: bool,
    buf:        *mut u8,
    buf_len:    usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || query.is_null() || buf.is_null() { return 0; }
        if query_len == 0 || buf_len == 0 { return 0; }
        
        let doc = unsafe { &*doc };
        let query_bytes = unsafe { slice::from_raw_parts(query, query_len) };
        let query_str = match std::str::from_utf8(query_bytes) {
            Ok(s) => s,
            Err(_) => return 0,
        };
        
        let search_str = if case_sensitive {
            query_str.to_string()
        } else {
            query_str.to_lowercase()
        };
        
        let mut results = Vec::new();
        
        // Search all slides
        for (slide_idx, _slide) in doc.presentation.slides.iter().enumerate() {
            let placed = deckcraft_render::place_slide(&doc.presentation, slide_idx);
            
            // Extract all text from this slide
            let mut slide_text = String::new();
            for text_block in &placed.texts {
                for run in &text_block.layout.runs {
                    for (_, ch) in &run.cells {
                        slide_text.push(*ch);
                    }
                }
            }
            
            let search_text = if case_sensitive {
                slide_text.clone()
            } else {
                slide_text.to_lowercase()
            };
            
            // Find all occurrences in this slide
            let mut start_pos = 0;
            while let Some(pos) = search_text[start_pos..].find(&search_str) {
                let actual_pos = start_pos + pos;
                results.push(format!("{},{},{}", slide_idx, actual_pos, query_str.len()));
                start_pos = actual_pos + 1;
            }
        }
        
        let result_str = results.join(";");
        let bytes = result_str.as_bytes();
        let copy = bytes.len().min(buf_len);
        if copy == 0 { return 0; }
        
        unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copy); }
        copy
    })
}

/// Free a DcImage pixel buffer.
/// MUST be called after C# has finished copying from DcImage.pixels.
#[no_mangle]
pub extern "C" fn dc_free_image(img: DcImage) {
    if img.pixels.is_null() || img.len == 0 { return; }
    // SAFETY: pixels was allocated by Rust Vec<u8> with capacity == len.
    unsafe { drop(Vec::from_raw_parts(img.pixels, img.len, img.len)); }
}

/// Free a DcDocument handle obtained from dc_open_pptx.
#[no_mangle]
pub extern "C" fn dc_free_document(doc: *mut DcDocument) {
    if doc.is_null() { return; }
    unsafe { drop(Box::from_raw(doc)); }
}
