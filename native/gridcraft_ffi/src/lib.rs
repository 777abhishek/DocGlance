//! gridcraft_ffi — C-ABI wrapper around GridCraft's XLSX parser, engine, and PDF export.
//!
//! Exposes functions for QuickView via C# P/Invoke:
//!   gc_open_xlsx             — parse an XLSX file from bytes → opaque handle
//!   gc_sheet_count           — number of sheets in workbook
//!   gc_get_sheet_name        — get sheet title/name
//!   gc_get_sheet_size        — get sheet width & height in points
//!   gc_export_sheet_pdf      — export a specific sheet or all sheets to PDF bytes
//!   gc_free_pdf_buffer       — free exported PDF buffer
//!   gc_extract_sheet_text    — extract text from sheet
//!   gc_search_text           — search text across all sheets
//!   gc_get_sheet_data_json   — get rich cell data as JSON (for native grid viewer)
//!   gc_free_json_buffer      — free a JSON buffer
//!   gc_free_document         — free document handle

use std::panic;
use std::slice;

use gridcraft_engine::cmd::print::{layout_sheet, render_pdf, PrintOptions, SheetLayout};
use gridcraft_engine::DocState;

fn leak_ffi_bytes(bytes: Vec<u8>) -> (*mut u8, usize) {
    let boxed = bytes.into_boxed_slice();
    let len = boxed.len();
    (Box::into_raw(boxed) as *mut u8, len)
}

/// # Safety
/// `data` must be a non-null pointer returned by `leak_ffi_bytes`, and `len`
/// must be the unchanged length returned with it. The buffer may be freed once.
unsafe fn free_ffi_bytes(data: *mut u8, len: usize) {
    if !data.is_null() {
        let slice = std::ptr::slice_from_raw_parts_mut(data, len);
        unsafe { drop(Box::from_raw(slice)) };
    }
}

fn push_json_string(output: &mut String, value: &str) {
    output.push('"');
    for ch in value.chars() {
        match ch {
            '"' => output.push_str("\\\""),
            '\\' => output.push_str("\\\\"),
            '\u{0008}' => output.push_str("\\b"),
            '\u{000C}' => output.push_str("\\f"),
            '\n' => output.push_str("\\n"),
            '\r' => output.push_str("\\r"),
            '\t' => output.push_str("\\t"),
            '\u{0000}'..='\u{001F}' => {
                const HEX: &[u8; 16] = b"0123456789ABCDEF";
                let byte = ch as u8;
                output.push_str("\\u00");
                output.push(HEX[(byte >> 4) as usize] as char);
                output.push(HEX[(byte & 0x0F) as usize] as char);
            }
            _ => output.push(ch),
        }
    }
    output.push('"');
}

// ── Public types ──────────────────────────────────────────────────────────────

pub struct GcDocument {
    pub doc: DocState,
}

#[repr(C)]
pub struct GcPdfBuffer {
    pub data: *mut u8,
    pub len: usize,
    pub page_count: u32,
}

impl GcPdfBuffer {
    fn null() -> Self {
        GcPdfBuffer {
            data: std::ptr::null_mut(),
            len: 0,
            page_count: 0,
        }
    }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

macro_rules! catch {
    ($fallback:expr, $body:block) => {
        match panic::catch_unwind(panic::AssertUnwindSafe(|| $body)) {
            Ok(v) => v,
            Err(_) => $fallback,
        }
    };
}

// ── Exported functions ────────────────────────────────────────────────────────

/// Parse an XLSX file from a byte slice.
///
/// # Safety
/// `data` must point to `len` readable bytes for the duration of this call.
#[no_mangle]
pub extern "C" fn gc_open_xlsx(data: *const u8, len: usize) -> *mut GcDocument {
    catch!(std::ptr::null_mut(), {
        if data.is_null() || len == 0 || len > isize::MAX as usize {
            return std::ptr::null_mut();
        }

        let bytes = unsafe { slice::from_raw_parts(data, len) };

        let (wb, _report) = match gridcraft_xlsx::read_xlsx(bytes) {
            Ok(res) => res,
            Err(_) => return std::ptr::null_mut(),
        };

        let doc = DocState::new(wb, None, "Workbook".to_string());
        let gc_doc = Box::new(GcDocument { doc });
        Box::into_raw(gc_doc)
    })
}

/// Returns the number of sheets in the workbook.
#[no_mangle]
pub extern "C" fn gc_sheet_count(doc: *const GcDocument) -> u32 {
    catch!(0, {
        if doc.is_null() {
            return 0;
        }
        let doc = unsafe { &*doc };
        doc.doc.wb.sheets.len() as u32
    })
}

/// Copy sheet name into caller's buffer. Returns bytes written.
#[no_mangle]
pub extern "C" fn gc_get_sheet_name(
    doc: *const GcDocument,
    index: u32,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || buf.is_null() || buf_len == 0 {
            return 0;
        }
        let doc = unsafe { &*doc };
        let Some(sh) = doc.doc.wb.sheet(index as usize) else {
            return 0;
        };

        let bytes = sh.name.as_bytes();
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

/// Writes estimated sheet dimensions in points into out-parameters.
#[no_mangle]
pub extern "C" fn gc_get_sheet_size(
    doc: *const GcDocument,
    index: u32,
    width_pt: *mut f64,
    height_pt: *mut f64,
) {
    catch!((), {
        if width_pt.is_null() || height_pt.is_null() {
            return;
        }
        unsafe {
            *width_pt = 612.0;
            *height_pt = 792.0;
        }
        if doc.is_null() {
            return;
        }
        let doc = unsafe { &*doc };
        let opts = PrintOptions::default();
        if let Some(lay) = layout_sheet(&doc.doc.wb, index as usize, &opts, None) {
            unsafe {
                *width_pt = lay.page_w as f64;
                *height_pt = lay.page_h as f64;
            }
        } else {
            unsafe {
                *width_pt = 612.0;
                *height_pt = 792.0;
            }
        }
    });
}

/// Exports a sheet (or entire workbook if index == u32::MAX) to PDF bytes.
/// Returns GcPdfBuffer. C# caller MUST free with gc_free_pdf_buffer.
#[no_mangle]
pub extern "C" fn gc_export_sheet_pdf(doc: *const GcDocument, sheet_index: u32) -> GcPdfBuffer {
    catch!(GcPdfBuffer::null(), {
        if doc.is_null() {
            return GcPdfBuffer::null();
        }
        let doc = unsafe { &*doc };
        let opts = PrintOptions {
            all_sheets: sheet_index == u32::MAX,
            range: None,
            fit_to_page: None,
        };

        let lays: Vec<SheetLayout> = if sheet_index == u32::MAX {
            (0..doc.doc.wb.sheets.len())
                .filter_map(|si| layout_sheet(&doc.doc.wb, si, &opts, None))
                .collect()
        } else {
            match layout_sheet(&doc.doc.wb, sheet_index as usize, &opts, None) {
                Some(l) => vec![l],
                None => Vec::new(),
            }
        };

        if lays.is_empty() {
            return GcPdfBuffer::null();
        }

        let (pdf_bytes, page_count) = render_pdf(&doc.doc.wb, &lays, &doc.doc.display_title(), "");
        if pdf_bytes.is_empty() {
            return GcPdfBuffer::null();
        }

        let (ptr, len) = leak_ffi_bytes(pdf_bytes);

        GcPdfBuffer {
            data: ptr,
            len,
            page_count: page_count as u32,
        }
    })
}

/// Free a PDF buffer returned by gc_export_sheet_pdf.
///
/// # Safety
/// `buf` must be an unmodified buffer returned by `gc_export_sheet_pdf` and
/// must not have been freed before.
#[no_mangle]
pub extern "C" fn gc_free_pdf_buffer(buf: GcPdfBuffer) {
    if !buf.data.is_null() {
        unsafe { free_ffi_bytes(buf.data, buf.len) };
    }
}

/// Extract text from sheet `index` into caller-provided buffer (UTF-8).
#[no_mangle]
pub extern "C" fn gc_extract_sheet_text(
    doc: *const GcDocument,
    index: u32,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null() || buf.is_null() || buf_len == 0 {
            return 0;
        }
        let doc = unsafe { &*doc };
        let Some(sh) = doc.doc.wb.sheet(index as usize) else {
            return 0;
        };

        let mut text = String::new();
        if let Some(r) = sh.used_range() {
            let limit_col = r.end.col.min(r.start.col + 100);
            for row in r.start.row..=r.end.row {
                let mut row_has_text = false;
                for col in r.start.col..=limit_col {
                    let cell_ref = gridcraft_core::CellRef::new(row, col);
                    let val_str = gridcraft_engine::display::cell_text(&doc.doc.wb, sh, cell_ref);
                    if !val_str.is_empty() {
                        if row_has_text {
                            text.push('\t');
                        }
                        text.push_str(&val_str);
                        row_has_text = true;
                    }
                }
                if row_has_text {
                    text.push('\n');
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

/// Search for `query` across all sheets.
/// Returns semicolon-separated list: "sheetIndex,row,col;..."
#[no_mangle]
pub extern "C" fn gc_search_text(
    doc: *const GcDocument,
    query: *const u8,
    query_len: usize,
    case_sensitive: bool,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    catch!(0, {
        if doc.is_null()
            || query.is_null()
            || query_len == 0
            || query_len > isize::MAX as usize
            || buf.is_null()
            || buf_len == 0
        {
            return 0;
        }
        let doc = unsafe { &*doc };
        let q_bytes = unsafe { slice::from_raw_parts(query, query_len) };
        let Ok(q_str) = std::str::from_utf8(q_bytes) else {
            return 0;
        };

        let mut matches = Vec::new();
        let q_lower = if case_sensitive {
            String::new()
        } else {
            q_str.to_lowercase()
        };

        for (si, sh) in doc.doc.wb.sheets.iter().enumerate() {
            if let Some(r) = sh.used_range() {
                let limit_col = r.end.col.min(r.start.col + 200);
                for row in r.start.row..=r.end.row {
                    for col in r.start.col..=limit_col {
                        let cell_ref = gridcraft_core::CellRef::new(row, col);
                        let val_str =
                            gridcraft_engine::display::cell_text(&doc.doc.wb, sh, cell_ref);
                        if val_str.is_empty() {
                            continue;
                        }

                        let is_match = if case_sensitive {
                            val_str.contains(q_str)
                        } else {
                            val_str.to_lowercase().contains(&q_lower)
                        };

                        if is_match {
                            matches.push(format!("{},{},{}", si, row, col));
                            if matches.len() >= 5000 {
                                break;
                            }
                        }
                    }
                    if matches.len() >= 5000 {
                        break;
                    }
                }
            }
            if matches.len() >= 5000 {
                break;
            }
        }

        let result_str = matches.join(";");
        copy_search_results(result_str.as_bytes(), buf, buf_len)
    })
}

fn copy_search_results(bytes: &[u8], buf: *mut u8, buf_len: usize) -> usize {
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
    use super::{copy_search_results, free_ffi_bytes, leak_ffi_bytes, push_json_string};
    use std::slice;

    #[test]
    fn ffi_byte_buffers_round_trip_and_free() {
        let expected = vec![1, 2, 3, 4, 5];
        let (data, len) = leak_ffi_bytes(expected.clone());

        let returned = unsafe { slice::from_raw_parts(data, len) };
        assert_eq!(returned, expected);
        unsafe { free_ffi_bytes(data, len) };
    }

    #[test]
    fn json_string_writer_escapes_quotes_backslashes_and_controls() {
        let mut output = String::new();

        push_json_string(&mut output, "quote: \" slash: \\\\ control: \u{0001}\n");

        assert_eq!(
            output,
            "\"quote: \\\" slash: \\\\\\\\ control: \\u0001\\n\""
        );
    }

    #[test]
    fn reports_required_search_buffer_capacity() {
        let bytes = b"0,1200,200;";
        let mut output = [0xAA; 4];

        let required = copy_search_results(bytes, output.as_mut_ptr(), output.len());

        assert_eq!(required, bytes.len());
        assert_eq!(output, [0xAA; 4]);
    }

    #[test]
    fn writes_complete_search_results_when_buffer_fits() {
        let bytes = b"0,12,3;1,8,4;";
        let mut output = [0; 32];

        let written = copy_search_results(bytes, output.as_mut_ptr(), output.len());

        assert_eq!(written, bytes.len());
        assert_eq!(&output[..written], bytes);
    }
}

/// Release a document returned by gc_open_xlsx.
///
/// # Safety
/// `doc` must be a live handle returned by `gc_open_xlsx` and must be freed
/// exactly once, after all operations using it have completed.
#[no_mangle]
pub extern "C" fn gc_free_document(doc: *mut GcDocument) {
    if !doc.is_null() {
        unsafe {
            drop(Box::from_raw(doc));
        }
    }
}

// ── JSON sheet-data export ────────────────────────────────────────────────────

/// Returned by gc_get_sheet_data_json. Free exactly once with gc_free_json_buffer.
#[repr(C)]
pub struct GcJsonBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl GcJsonBuffer {
    fn null() -> Self {
        GcJsonBuffer {
            data: std::ptr::null_mut(),
            len: 0,
        }
    }
}

/// Free a JSON buffer returned by gc_get_sheet_data_json.
///
/// # Safety
/// `buf` must be an unmodified buffer returned by `gc_get_sheet_data_json` and
/// must not have been freed before.
#[no_mangle]
pub extern "C" fn gc_free_json_buffer(buf: GcJsonBuffer) {
    if !buf.data.is_null() {
        unsafe { free_ffi_bytes(buf.data, buf.len) };
    }
}

/// Export rich cell data for sheet `index` as compact JSON.
///
/// JSON shape (all fields present even if empty/zero):
/// ```json
/// {
///   "name": "Sheet1",
///   "rowCount": 50,
///   "colCount": 10,
///   "defaultColWidth": 64.0,
///   "defaultRowHeight": 20.0,
///   "freezeRow": 1,
///   "freezeCol": 0,
///   "showGridlines": true,
///   "cols": { "0": 80.0, "3": 120.0 },
///   "rows": { "0": 28.0 },
///   "merges": [{"r":0,"c":0,"rs":1,"cs":3}],
///   "cells": [
///     {"r":0,"c":0,"v":"Hello","bg":"#FFFF00","fg":"#000000","bold":true,"italic":false,
///      "h":"center","vt":"bottom","wrap":false,"indent":0}
///   ]
/// }
/// ```
/// Returns null buffer on error.
#[no_mangle]
pub extern "C" fn gc_get_sheet_data_json(doc: *const GcDocument, index: u32) -> GcJsonBuffer {
    catch!(GcJsonBuffer::null(), {
        if doc.is_null() {
            return GcJsonBuffer::null();
        }
        let doc = unsafe { &*doc };
        let wb = &doc.doc.wb;
        let Some(sh) = wb.sheet(index as usize) else {
            return GcJsonBuffer::null();
        };

        // ── determine used range ─────────────────────────────────────────────
        let used = sh.used_range();
        let (row_count, col_count) = match used {
            Some(r) => (r.end.row as usize + 1, r.end.col as usize + 1),
            None => (0, 0),
        };
        let col_count = col_count.min(256);

        // ── helpers ──────────────────────────────────────────────────────────
        let color_hex = |c: gridcraft_model::style::Color| -> Option<String> {
            match c {
                gridcraft_model::style::Color::Auto => None,
                gridcraft_model::style::Color::Rgb(v) => Some(format!("#{:06X}", v)),
                gridcraft_model::style::Color::Theme(i, tint) => {
                    let base = wb.theme.colors.get(i as usize).copied().unwrap_or(0);
                    let rgb = [
                        ((base >> 16) & 0xFF) as u8,
                        ((base >> 8) & 0xFF) as u8,
                        (base & 0xFF) as u8,
                    ];
                    let rgb = gridcraft_model::style::apply_tint(rgb, tint as f64 / 1000.0);
                    Some(format!("#{:02X}{:02X}{:02X}", rgb[0], rgb[1], rgb[2]))
                }
            }
        };

        let halign_str = |h: gridcraft_model::style::HAlign| -> &'static str {
            use gridcraft_model::style::HAlign;
            match h {
                HAlign::Left => "left",
                HAlign::Center | HAlign::CenterAcross => "center",
                HAlign::Right => "right",
                _ => "general",
            }
        };

        let valign_str = |v: gridcraft_model::style::VAlign| -> &'static str {
            use gridcraft_model::style::VAlign;
            match v {
                VAlign::Top => "top",
                VAlign::Center => "center",
                _ => "bottom",
            }
        };

        // ── build JSON manually ───────────────────────────────────────────────
        let mut json = String::with_capacity(64 * 1024);
        json.push('{');

        // sheet metadata
        json.push_str("\"name\":");
        push_json_string(&mut json, &sh.name);
        json.push(',');
        json.push_str(&format!("\"rowCount\":{},", row_count));
        json.push_str(&format!("\"colCount\":{},", col_count));
        json.push_str(&format!("\"defaultColWidth\":{:.2},", sh.default_col_width));
        json.push_str(&format!(
            "\"defaultRowHeight\":{:.2},",
            sh.default_row_height
        ));

        // freeze
        let (fr, fc) = sh.freeze.unwrap_or((0, 0));
        json.push_str(&format!("\"freezeRow\":{},", fr));
        json.push_str(&format!("\"freezeCol\":{},", fc));
        json.push_str(&format!("\"showGridlines\":{},", sh.show_gridlines));

        // column widths (only custom ones)
        json.push_str("\"cols\":{");
        let mut first = true;
        for (col, info) in &sh.cols {
            if let Some(w) = info.size {
                if !first {
                    json.push(',');
                }
                first = false;
                json.push_str(&format!(
                    "\"{}\":{:.2}",
                    col,
                    if info.hidden { 0.0 } else { w }
                ));
            }
        }
        json.push_str("},");

        // row heights (only custom ones)
        json.push_str("\"rows\":{");
        first = true;
        for (row, info) in &sh.rows {
            if let Some(h) = info.size {
                if !first {
                    json.push(',');
                }
                first = false;
                json.push_str(&format!(
                    "\"{}\":{:.2}",
                    row,
                    if info.hidden { 0.0 } else { h }
                ));
            }
        }
        json.push_str("},");

        // merges
        json.push_str("\"merges\":[");
        first = true;
        for m in &sh.merges {
            if !first {
                json.push(',');
            }
            first = false;
            json.push_str(&format!(
                "{{\"r\":{},\"c\":{},\"rs\":{},\"cs\":{}}}",
                m.start.row,
                m.start.col,
                m.end.row - m.start.row,
                m.end.col - m.start.col,
            ));
        }
        json.push_str("],");

        // cells
        json.push_str("\"cells\":[");
        first = true;
        if let Some(r) = used {
            let end_row = (r.end.row as usize).min(row_count.saturating_sub(1));
            let end_col = (r.end.col as usize).min(col_count.saturating_sub(1));

            for row in (r.start.row as usize)..=end_row {
                for col in (r.start.col as usize)..=end_col {
                    let cref = gridcraft_core::CellRef::new(row as u32, col as u32);
                    let cell = sh.cell(cref);
                    let spill_val = sh.spill.get(&cref);
                    let has_content = cell.map_or(false, |c| !c.is_blank()) || spill_val.is_some();
                    if !has_content {
                        continue;
                    }

                    let text = gridcraft_engine::display::cell_text(wb, sh, cref);
                    let style_id = sh.style_id(cref);
                    let style = wb.styles.get(style_id);

                    let bg = if style.fill.pattern != gridcraft_model::style::PatternType::None {
                        color_hex(style.fill.fg)
                    } else {
                        None
                    };
                    let fg = color_hex(style.font.color);

                    if !first {
                        json.push(',');
                    }
                    first = false;

                    json.push('{');
                    json.push_str(&format!("\"r\":{},\"c\":{}", row, col));
                    if !text.is_empty() {
                        json.push_str(",\"v\":");
                        push_json_string(&mut json, &text);
                    }
                    if let Some(ref b) = bg {
                        json.push_str(&format!(",\"bg\":\"{}\"", b));
                    }
                    if let Some(ref f) = fg {
                        json.push_str(&format!(",\"fg\":\"{}\"", f));
                    }
                    if style.font.bold {
                        json.push_str(",\"bold\":true");
                    }
                    if style.font.italic {
                        json.push_str(",\"italic\":true");
                    }
                    let h = halign_str(style.align.h);
                    if h != "general" {
                        json.push_str(&format!(",\"h\":\"{}\"", h));
                    }
                    let v = valign_str(style.align.v);
                    if v != "bottom" {
                        json.push_str(&format!(",\"vt\":\"{}\"", v));
                    }
                    if style.align.wrap {
                        json.push_str(",\"wrap\":true");
                    }
                    // border summary: presence of any border side (for rendering grid lines)
                    let has_border = !style.borders.left.is_none()
                        || !style.borders.right.is_none()
                        || !style.borders.top.is_none()
                        || !style.borders.bottom.is_none();
                    if has_border {
                        let bl = if !style.borders.left.is_none() {
                            "1"
                        } else {
                            "0"
                        };
                        let br = if !style.borders.right.is_none() {
                            "1"
                        } else {
                            "0"
                        };
                        let bt = if !style.borders.top.is_none() {
                            "1"
                        } else {
                            "0"
                        };
                        let bb = if !style.borders.bottom.is_none() {
                            "1"
                        } else {
                            "0"
                        };
                        json.push_str(&format!(",\"brd\":\"{}{}{}{}\"", bl, br, bt, bb));
                    }
                    json.push('}');
                }
            }
        }
        json.push_str("]}");

        let (ptr, len) = leak_ffi_bytes(json.into_bytes());

        GcJsonBuffer { data: ptr, len }
    })
}
