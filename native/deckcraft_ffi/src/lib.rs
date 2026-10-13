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

use std::panic;
use std::slice;
use std::sync::Mutex;

use deckcraft_render::RenderOpts;

// ── Public types ──────────────────────────────────────────────────────────────

/// Opaque handle returned by dc_open_pptx.
pub struct DcDocument {
    presentation: deckcraft_model::Presentation,
    slide_text_cache: Mutex<Vec<Option<SlideText>>>,
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

#[derive(Clone, Copy, Debug)]
struct SearchRect {
    x: f64,
    y: f64,
    width: f64,
    height: f64,
}

struct SlideText {
    text: String,
    char_rects: Vec<Option<SearchRect>>,
}

fn extract_slide_search_data(presentation: &deckcraft_model::Presentation, index: usize) -> SlideText {
    let placed = deckcraft_render::place_slide(presentation, index);
    let mut text = String::new();
    let mut char_rects = Vec::new();

    for text_block in &placed.texts {
        for run in &text_block.layout.runs {
            for (char_index, ch) in &run.cells {
                text.push(*ch);
                let rect = text_block.layout.lines.iter()
                    .find(|line| line.para == run.para && *char_index >= line.start && *char_index < line.end)
                    .and_then(|line| {
                        let (x0, x1) = line.edges.get(*char_index - line.start)?;
                        let local = deckcraft_geom::Rect::new(*x0, line.top, *x1, line.bottom);
                        let slide = text_block.transform.transform_rect_bbox(local);
                        (slide.width() > 0.0 && slide.height() > 0.0).then_some(SearchRect {
                            x: slide.x0,
                            y: slide.y0,
                            width: slide.width(),
                            height: slide.height(),
                        })
                    });
                char_rects.push(rect);
            }
        }
        text.push('\n');
        char_rects.push(None);
    }

    SlideText { text, char_rects }
}

fn slide_text<'a>(doc: &'a DcDocument, index: usize) -> Option<std::sync::MutexGuard<'a, Vec<Option<SlideText>>>> {
    let mut cache = doc.slide_text_cache.lock().unwrap_or_else(|e| e.into_inner());
    if index >= cache.len() {
        return None;
    }
    if cache[index].is_none() {
        cache[index] = Some(extract_slide_search_data(&doc.presentation, index));
    }
    Some(cache)
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

fn find_matches(text: &str, query: &str, case_sensitive: bool) -> Vec<(usize, usize)> {
    if query.is_empty() {
        return Vec::new();
    }

    let mut folded_offsets = Vec::new();
    let lower_text = (!case_sensitive).then(|| {
        let mut folded = String::new();
        for (source_index, ch) in text.chars().enumerate() {
            for lower_ch in ch.to_lowercase() {
                folded.push(lower_ch);
                folded_offsets.push(source_index);
            }
        }
        folded
    });
    let lower_query = (!case_sensitive).then(|| query.to_lowercase());
    let search_text = lower_text.as_deref().unwrap_or(text);
    let search_query = lower_query.as_deref().unwrap_or(query);
    let folded_query_len = search_query.chars().count();
    let mut matches = Vec::new();
    let mut start_pos = 0;

    while start_pos < search_text.len() {
        let Some(relative_pos) = search_text[start_pos..].find(search_query) else { break; };
        let actual_pos = start_pos + relative_pos;
        let folded_char_index = search_text[..actual_pos].chars().count();
        if case_sensitive {
            matches.push((folded_char_index, folded_query_len));
        } else if let (Some(&first_source_char), Some(&last_source_char)) = (
            folded_offsets.get(folded_char_index),
            folded_offsets.get(folded_char_index + folded_query_len.saturating_sub(1)),
        ) {
            matches.push((first_source_char, last_source_char - first_source_char + 1));
        }

        let next_char_len = search_text[actual_pos..]
            .chars()
            .next()
            .map(char::len_utf8)
            .unwrap_or(1);
        start_pos = actual_pos + next_char_len;
    }

    matches
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

        let slide_count = presentation.slides.len();
        let doc = Box::new(DcDocument {
            presentation,
            slide_text_cache: Mutex::new((0..slide_count).map(|_| None).collect()),
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
            // Two threads keep peak tile buffers down vs 4 workers; visual output is identical.
            threads: 2,
            ..Default::default()
        };

        let img = deckcraft_render::render_slide(pres, index as usize, &opts);
        let w = img.width;
        let h = img.height;
        let mut pixels = img.to_straight();
        drop(img);
        rgba_to_premul_bgra_inplace(&mut pixels);

        let len = pixels.len();
        let ptr = pixels.as_mut_ptr();
        std::mem::forget(pixels);

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

        let Some(cache) = slide_text(doc, index as usize) else { return 0; };
        let Some(slide_text) = cache.get(index as usize).and_then(|s| s.as_ref()) else { return 0; };

        let bytes = slide_text.text.as_bytes();
        let copy = bytes.len().min(buf_len);
        if copy == 0 { return 0; }

        unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, copy); }
        copy
    })
}

/// Search for `query` text across all slides.
/// Returns semicolon-separated match rectangles:
/// "slideIndex,charIndex,length,x,y,width,height;..."
/// Write results to `buf` (UTF-8). Returns bytes written, or required buffer size
/// if `buf` is too small.
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
        
        let mut results = Vec::new();
        let slide_count = doc.presentation.slides.len();

        for slide_idx in 0..slide_count {
            let Some(cache) = slide_text(doc, slide_idx) else { continue; };
            let Some(slide_text) = cache.get(slide_idx).and_then(|s| s.as_ref()) else { continue; };
            for (char_index, length) in find_matches(&slide_text.text, query_str, case_sensitive) {
                let match_end = char_index.saturating_add(length).min(slide_text.char_rects.len());
                let mut rects: Vec<SearchRect> = Vec::new();
                for rect in slide_text.char_rects[char_index.min(match_end)..match_end].iter().flatten() {
                    if let Some(last) = rects.last_mut() {
                        let same_line = (last.y - rect.y).abs() < 1.0
                            && (last.height - rect.height).abs() < 1.0;
                        if same_line && rect.x <= last.x + last.width + 2.0 {
                            let right = (last.x + last.width).max(rect.x + rect.width);
                            last.x = last.x.min(rect.x);
                            last.width = right - last.x;
                            continue;
                        }
                    }
                    rects.push(*rect);
                }

                if rects.is_empty() {
                    results.push(format!("{},{},{},0,0,0,0", slide_idx, char_index, length));
                } else {
                    for rect in rects {
                        results.push(format!(
                            "{},{},{},{:.3},{:.3},{:.3},{:.3}",
                            slide_idx, char_index, length, rect.x, rect.y, rect.width, rect.height
                        ));
                    }
                }
            }
        }
        
        let result_str = results.join(";");
        let bytes = result_str.as_bytes();
        if bytes.len() > buf_len { return bytes.len(); }
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

#[cfg(test)]
mod tests {
    use super::find_matches;

    #[test]
    fn finds_overlapping_matches() {
        assert_eq!(find_matches("aaaa", "aa", true), vec![(0, 2), (1, 2), (2, 2)]);
    }

    #[test]
    fn case_insensitive_search_finds_ascii_variants() {
        assert_eq!(find_matches("Title title", "TITLE", false), vec![(0, 5), (6, 5)]);
    }

    #[test]
    fn search_advances_at_utf8_character_boundaries() {
        assert_eq!(find_matches("éaéa", "a", true), vec![(1, 1), (3, 1)]);
    }

    #[test]
    fn case_insensitive_expanding_lowercase_keeps_source_character_offsets() {
        assert_eq!(find_matches("İX", "x", false), vec![(1, 1)]);
    }
}
