//! C ABI for the PdfCraft v0.7.1 renderer and text layer.

use std::panic::{self, AssertUnwindSafe};
use std::path::Path;
use std::slice;
use std::sync::{Arc, Mutex};

use pdfcraft_cos::{write_full, write_incremental, Dict, Document, Object, PdfString, SaveOptions};
use pdfcraft_render::{
    inspect, PageRenderer, RenderConfig, RenderRequest, RequestKind,
};

pub struct PcDocument {
    bytes: Arc<Vec<u8>>,
    info: pdfcraft_render::DocInfo,
    renderer: Mutex<PageRenderer>,
}

#[repr(C)]
pub struct PcImage {
    pub pixels: *mut u8,
    pub width: u32,
    pub height: u32,
    pub len: usize,
}

impl PcImage {
    fn null() -> Self {
        Self { pixels: std::ptr::null_mut(), width: 0, height: 0, len: 0 }
    }
}

#[repr(C)]
pub struct PcPngBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl PcPngBuffer {
    fn null() -> Self {
        Self { data: std::ptr::null_mut(), len: 0 }
    }
}

#[repr(C)]
pub struct PcPdfBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl PcPdfBuffer {
    fn null() -> Self {
        Self { data: std::ptr::null_mut(), len: 0 }
    }
}

macro_rules! ffi {
    ($fallback:expr, $body:block) => {
        match panic::catch_unwind(AssertUnwindSafe(|| $body)) {
            Ok(value) => value,
            Err(_) => $fallback,
        }
    };
}

macro_rules! ffi_opt {
    ($fallback:expr, $body:block) => {
        match panic::catch_unwind(AssertUnwindSafe(|| -> Option<_> { Some($body) })) {
            Ok(Some(value)) => value,
            Ok(None) => $fallback,
            Err(_) => $fallback,
        }
    };
}

fn input<'a>(ptr: *const u8, len: usize) -> Option<&'a [u8]> {
    if ptr.is_null() || len == 0 {
        None
    } else {
        Some(unsafe { slice::from_raw_parts(ptr, len) })
    }
}

fn copy_buffer(bytes: Vec<u8>) -> PcPdfBuffer {
    let mut bytes = bytes;
    let result = PcPdfBuffer { data: bytes.as_mut_ptr(), len: bytes.len() };
    std::mem::forget(bytes);
    result
}

fn selection_rects_in_user_space(
    page: &pdfcraft_render::PageInfo,
    rects: &[f32],
) -> Option<Vec<([f32; 4], [f32; 8])>> {
    if rects.is_empty() || rects.len() % 4 != 0 || rects.len() > 16_384 {
        return None;
    }

    rects
        .chunks_exact(4)
        .map(|rect| {
            let [x, y, width, height] = [rect[0], rect[1], rect[2], rect[3]];
            if ![x, y, width, height].iter().all(|value| value.is_finite()) || width <= 0.0 || height <= 0.0 {
                return None;
            }

            let points = [
                page.view_to_user(x, y),
                page.view_to_user(x + width, y),
                page.view_to_user(x, y + height),
                page.view_to_user(x + width, y + height),
            ];
            if !points.iter().flatten().all(|value| value.is_finite()) {
                return None;
            }
            let x_min = points.iter().map(|point| point[0]).fold(f32::INFINITY, f32::min);
            let y_min = points.iter().map(|point| point[1]).fold(f32::INFINITY, f32::min);
            let x_max = points.iter().map(|point| point[0]).fold(f32::NEG_INFINITY, f32::max);
            let y_max = points.iter().map(|point| point[1]).fold(f32::NEG_INFINITY, f32::max);
            let quad = [
                points[0][0],
                points[0][1],
                points[1][0],
                points[1][1],
                points[2][0],
                points[2][1],
                points[3][0],
                points[3][1],
            ];
            Some(([x_min, y_min, x_max, y_max], quad))
        })
        .collect()
}

fn read_annotations(doc: &Document, page_ref: pdfcraft_cos::ObjRef) -> Option<(Dict, Vec<Object>, Option<pdfcraft_cos::ObjRef>)> {
    let page = doc.get(page_ref);
    let page_dict = page.as_dict()?.clone();
    let annots_ref = page_dict.get(b"Annots").and_then(Object::as_ref);
    let annots = match page_dict.get(b"Annots") {
        None => Vec::new(),
        Some(Object::Array(items)) => items.clone(),
        Some(Object::Ref(reference)) => doc.get(*reference).as_array()?.clone(),
        _ => return None,
    };
    Some((page_dict, annots, annots_ref))
}

fn set_annotations(
    doc: &mut Document,
    page_ref: pdfcraft_cos::ObjRef,
    mut page_dict: Dict,
    annots: Vec<Object>,
    annots_ref: Option<pdfcraft_cos::ObjRef>,
) {
    if let Some(reference) = annots_ref {
        doc.set(reference, Object::Array(annots));
    } else {
        page_dict.set(b"Annots".to_vec(), Object::Array(annots));
        doc.set(page_ref, page_dict);
    }
}

fn annotation_user_rect(doc: &Document, annot: &Object) -> Option<[f32; 4]> {
    let resolved = doc.resolve(annot);
    let dict = resolved.as_dict()?;
    let values = dict.get(b"Rect").and_then(Object::as_array)?;
    if values.len() != 4 {
        return None;
    }
    let coords: Vec<f32> = values.iter().map(|value| value.as_f64().map(|v| v as f32)).collect::<Option<_>>()?;
    if !coords.iter().all(|value| value.is_finite()) {
        return None;
    }
    Some([
        coords[0].min(coords[2]),
        coords[1].min(coords[3]),
        coords[0].max(coords[2]),
        coords[1].max(coords[3]),
    ])
}

fn is_deletable_annotation(doc: &Document, annot: &Object) -> bool {
    let resolved = doc.resolve(annot);
    resolved.as_dict().and_then(|dict| dict.name(b"Subtype")).is_some_and(|subtype| {
        matches!(subtype, b"Highlight" | b"Underline" | b"StrikeOut" | b"Text")
    })
}

fn annotation_overlaps_selection(doc: &Document, annot: &Object, selection_rects: &[[f32; 4]]) -> bool {
    if !is_deletable_annotation(doc, annot) {
        return false;
    }
    let resolved = doc.resolve(annot);
    let Some(dict) = resolved.as_dict() else { return false };
    if let Some(points) = dict.get(b"QuadPoints").and_then(Object::as_array) {
        let quad_rects = points.chunks_exact(8).filter_map(|quad| {
            let values: Vec<f32> = quad
                .iter()
                .map(|value| value.as_f64().map(|value| value as f32))
                .collect::<Option<_>>()?;
            if !values.iter().all(|value| value.is_finite()) {
                return None;
            }
            Some([
                values.iter().step_by(2).copied().fold(f32::INFINITY, f32::min),
                values.iter().skip(1).step_by(2).copied().fold(f32::INFINITY, f32::min),
                values.iter().step_by(2).copied().fold(f32::NEG_INFINITY, f32::max),
                values.iter().skip(1).step_by(2).copied().fold(f32::NEG_INFINITY, f32::max),
            ])
        });
        return quad_rects.into_iter().any(|rect| selection_rects.iter().any(|selected| intersects(rect, *selected)));
    }
    annotation_user_rect(doc, annot)
        .is_some_and(|rect| selection_rects.iter().any(|selected| intersects(rect, *selected)))
}

fn intersects(a: [f32; 4], b: [f32; 4]) -> bool {
    a[0] < b[2] && b[0] < a[2] && a[1] < b[3] && b[1] < a[3]
}

fn overlapping_annotation_count(
    doc: &Document,
    page_ref: pdfcraft_cos::ObjRef,
    selection_rects: &[[f32; 4]],
) -> Option<usize> {
    let (_, annots, _) = read_annotations(doc, page_ref)?;
    Some(
        annots
            .iter()
            .filter(|annot| annotation_overlaps_selection(doc, annot, selection_rects))
            .count(),
    )
}

fn apply_annotation(
    source: &PcDocument,
    page_index: usize,
    rects: &[f32],
    kind: u32,
    contents: &str,
) -> Option<Vec<u8>> {
    let page_info = source.info.pages.get(page_index)?;
    if !matches!(kind, 0..=3) || (kind == 3 && contents.trim().is_empty()) {
        return None;
    }
    let mapped_rects = selection_rects_in_user_space(page_info, rects)?;
    let mut document = Document::open(source.bytes.clone()).ok()?;
    if document.permissions().is_some_and(|permissions| !permissions.annotate()) {
        return None;
    }
    let page_ref = pdfcraft_organize::pages(&document).ok()?.get(page_index)?.obj;
    let (page_dict, mut annots, annots_ref) = read_annotations(&document, page_ref)?;
    let first_rect = mapped_rects.first()?.0;
    let mut bounds = first_rect;
    for (rect, _) in mapped_rects.iter().skip(1) {
        bounds = [
            bounds[0].min(rect[0]),
            bounds[1].min(rect[1]),
            bounds[2].max(rect[2]),
            bounds[3].max(rect[3]),
        ];
    }

    let mut annotation = Dict::new();
    annotation.set(b"Type".to_vec(), Object::name("Annot"));
    annotation.set(b"Rect".to_vec(), Object::Array(bounds.iter().map(|value| Object::Real(f64::from(*value))).collect()));
    annotation.set(b"F".to_vec(), Object::Int(4));
    annotation.set(b"T".to_vec(), PdfString::text("DocGlance"));
    let unique_id = format!(
        "DocGlance-{}-{}",
        std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).ok()?.as_nanos(),
        NEXT_ANNOTATION_ID.fetch_add(1, std::sync::atomic::Ordering::Relaxed)
    );
    annotation.set(b"NM".to_vec(), PdfString::text(&unique_id));

    match kind {
        0..=2 => {
            let subtype = match kind {
                0 => "Highlight",
                1 => "Underline",
                _ => "StrikeOut",
            };
            let quads: Vec<Object> = mapped_rects
                .iter()
                .flat_map(|(_, quad)| quad.iter().map(|value| Object::Real(f64::from(*value))))
                .collect();
            annotation.set(b"Subtype".to_vec(), Object::name(subtype));
            annotation.set(b"QuadPoints".to_vec(), Object::Array(quads));
            annotation.set(b"C".to_vec(), Object::Array(vec![1.0.into(), 0.82.into(), 0.1.into()]));
            annotation.set(b"CA".to_vec(), Object::Real(0.35));
            annotation.set(b"Contents".to_vec(), PdfString::text(contents));
        }
        3 => {
            let icon_size = 18.0_f32;
            let x = rects[0].min(page_info.width - icon_size).max(0.0);
            let y = rects[1].min(page_info.height - icon_size).max(0.0);
            let icon_points = [
                page_info.view_to_user(x, y),
                page_info.view_to_user(x + icon_size, y),
                page_info.view_to_user(x, y + icon_size),
                page_info.view_to_user(x + icon_size, y + icon_size),
            ];
            let icon_rect = [
                icon_points.iter().map(|point| point[0]).fold(f32::INFINITY, f32::min),
                icon_points.iter().map(|point| point[1]).fold(f32::INFINITY, f32::min),
                icon_points.iter().map(|point| point[0]).fold(f32::NEG_INFINITY, f32::max),
                icon_points.iter().map(|point| point[1]).fold(f32::NEG_INFINITY, f32::max),
            ];
            annotation.set(
                b"Rect".to_vec(),
                Object::Array(icon_rect.iter().map(|value| Object::Real(f64::from(*value))).collect()),
            );
            annotation.set(b"Subtype".to_vec(), Object::name("Text"));
            annotation.set(b"Name".to_vec(), Object::name("Comment"));
            annotation.set(b"Open".to_vec(), Object::Bool(false));
            annotation.set(b"C".to_vec(), Object::Array(vec![1.0.into(), 0.82.into(), 0.1.into()]));
            annotation.set(b"Contents".to_vec(), PdfString::text(contents));
        }
        _ => return None,
    }

    let reference = document.add(Object::Dict(annotation));
    annots.push(Object::Ref(reference));
    set_annotations(&mut document, page_ref, page_dict, annots, annots_ref);
    write_incremental(&document, &SaveOptions::default()).ok()
}

fn remove_annotations(
    source: &PcDocument,
    page_index: usize,
    rects: &[f32],
) -> Option<(Vec<u8>, usize)> {
    let page_info = source.info.pages.get(page_index)?;
    let selection_rects: Vec<[f32; 4]> = selection_rects_in_user_space(page_info, rects)?
        .into_iter()
        .map(|(rect, _)| rect)
        .collect();
    let mut document = Document::open(source.bytes.clone()).ok()?;
    if document.permissions().is_some_and(|permissions| !permissions.annotate()) {
        return None;
    }
    let page_ref = pdfcraft_organize::pages(&document).ok()?.get(page_index)?.obj;
    let (page_dict, annots, annots_ref) = read_annotations(&document, page_ref)?;
    let removed = overlapping_annotation_count(&document, page_ref, &selection_rects)?;
    if removed == 0 {
        return None;
    }
    let remaining = annots
        .into_iter()
        .filter(|annot| !annotation_overlaps_selection(&document, annot, &selection_rects))
        .collect();
    set_annotations(&mut document, page_ref, page_dict, remaining, annots_ref);
    Some((write_incremental(&document, &SaveOptions::default()).ok()?, removed))
}

static NEXT_ANNOTATION_ID: std::sync::atomic::AtomicU64 = std::sync::atomic::AtomicU64::new(1);

#[no_mangle]
pub extern "C" fn pc_open_pdf_from_path(path: *const u8, path_len: usize) -> *mut PcDocument {
    ffi_opt!(std::ptr::null_mut(), {
        let bytes = input(path, path_len)?;
        let path = std::str::from_utf8(bytes).ok()?;
        let data = std::fs::read(path).ok()?;
        open_document(data).unwrap_or(std::ptr::null_mut())
    })
}

#[no_mangle]
pub extern "C" fn pc_open_pdf_from_bytes(data: *const u8, len: usize) -> *mut PcDocument {
    ffi_opt!(std::ptr::null_mut(), {
        let data = input(data, len)?;
        open_document(data.to_vec()).unwrap_or(std::ptr::null_mut())
    })
}

fn open_document(data: Vec<u8>) -> Option<*mut PcDocument> {
    let bytes = Arc::new(data);
    let info = inspect(bytes.clone(), None).ok()?;
    let renderer = PageRenderer::new(bytes.clone(), RenderConfig::default());
    if renderer.page_count() != info.pages.len() {
        return None;
    }
    Some(Box::into_raw(Box::new(PcDocument { bytes, info, renderer: Mutex::new(renderer) })))
}

#[no_mangle]
pub extern "C" fn pc_page_count(doc: *const PcDocument) -> u32 {
    ffi_opt!(0, {
        let doc = unsafe { doc.as_ref() }?;
        doc.info.pages.len().try_into().ok()?
    })
}

#[no_mangle]
pub extern "C" fn pc_get_page_size(
    doc: *const PcDocument,
    index: u32,
    width_pt: *mut f64,
    height_pt: *mut f64,
) {
    ffi!((), {
        let (Some(doc), Some(width), Some(height)) =
            (unsafe { doc.as_ref() }, unsafe { width_pt.as_mut() }, unsafe { height_pt.as_mut() })
        else {
            return;
        };
        let Some(page) = doc.info.pages.get(index as usize) else { return };
        *width = f64::from(page.width);
        *height = f64::from(page.height);
    });
}

#[no_mangle]
pub extern "C" fn pc_render_page(doc: *const PcDocument, index: u32, dpi: f64) -> PcImage {
    ffi_opt!(PcImage::null(), {
        let doc = unsafe { doc.as_ref() }?;
        if !dpi.is_finite() || dpi <= 0.0 {
            return Some(PcImage::null());
        }
        let scale = (dpi / 72.0) as f32;
        let request = RenderRequest {
            page: index as usize,
            kind: RequestKind::Pixels,
            scale,
            ..RenderRequest::default()
        };
        let mut renderer = doc.renderer.lock().ok()?;
        let rendered = renderer.render(request);
        if rendered.error.is_some() || rendered.rgba.is_empty() {
            return Some(PcImage::null());
        }
        let width = rendered.width;
        let height = rendered.height;
        let mut pixels = rendered.rgba.into_words();
        let len = pixels.len().checked_mul(4)?;
        let result = PcImage {
            pixels: pixels.as_mut_ptr().cast(),
            width,
            height,
            len,
        };
        std::mem::forget(pixels);
        result
    })
}

fn text_for_page(doc: &PcDocument, index: usize) -> Option<pdfcraft_render::PageText> {
    let request = RenderRequest {
        page: index,
        kind: RequestKind::Text,
        ..RenderRequest::default()
    };
    let mut renderer = doc.renderer.lock().ok()?;
    let rendered = renderer.render(request);
    if rendered.error.is_some() {
        return None;
    }
    rendered.text.map(|text| (*text).clone())
}

#[no_mangle]
pub extern "C" fn pc_extract_page_text(
    doc: *const PcDocument,
    index: u32,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    ffi_opt!(0, {
        let doc = unsafe { doc.as_ref() }?;
        if buf.is_null() || buf_len == 0 {
            return Some(0);
        }
        let buf = unsafe { slice::from_raw_parts_mut(buf, buf_len) };
        let text = text_for_page(doc, index as usize)?.plain_text();
        let required = text.len();
        let copied = required.min(buf.len());
        buf[..copied].copy_from_slice(&text.as_bytes()[..copied]);
        if copied < required { required } else { copied }
    })
}

/// Add a PDF markup or text-note annotation to a selection expressed in page view coordinates.
/// Kind values: 0 highlight, 1 underline, 2 strikeout, 3 text note.
#[no_mangle]
pub extern "C" fn pc_add_text_annotation(
    doc: *const PcDocument,
    page_index: u32,
    kind: u32,
    rects: *const f32,
    rects_len: usize,
    contents: *const u8,
    contents_len: usize,
) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        let rects = if rects.is_null() || rects_len == 0 || rects_len % 4 != 0 || rects_len > 16_384 {
            return Some(PcPdfBuffer::null());
        } else {
            unsafe { slice::from_raw_parts(rects, rects_len) }
        };
        if contents_len > 1_048_576 {
            return Some(PcPdfBuffer::null());
        }
        let contents = std::str::from_utf8(input(contents, contents_len)?).ok()?;
        copy_buffer(apply_annotation(doc, page_index as usize, rects, kind, contents)?)
    })
}

/// Count existing markup/note annotations that overlap a selection, for enabling Delete.
#[no_mangle]
pub extern "C" fn pc_count_removable_annotations(
    doc: *const PcDocument,
    page_index: u32,
    rects: *const f32,
    rects_len: usize,
) -> u32 {
    ffi_opt!(u32::MAX, {
        let doc = unsafe { doc.as_ref() }?;
        if rects.is_null() || rects_len == 0 || rects_len % 4 != 0 || rects_len > 16_384 {
            return Some(u32::MAX);
        }
        let rects = unsafe { slice::from_raw_parts(rects, rects_len) };
        let page_info = doc.info.pages.get(page_index as usize)?;
        let mapped: Vec<[f32; 4]> = selection_rects_in_user_space(page_info, rects)?
            .into_iter()
            .map(|(rect, _)| rect)
            .collect();
        let parsed = Document::open(doc.bytes.clone()).ok()?;
        let page_ref = pdfcraft_organize::pages(&parsed).ok()?.get(page_index as usize)?.obj;
        overlapping_annotation_count(&parsed, page_ref, &mapped)?.try_into().ok()?
    })
}

/// Remove markup/note annotations overlapping a selection and return the rewritten PDF.
#[no_mangle]
pub extern "C" fn pc_delete_text_annotations(
    doc: *const PcDocument,
    page_index: u32,
    rects: *const f32,
    rects_len: usize,
) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        if rects.is_null() || rects_len == 0 || rects_len % 4 != 0 || rects_len > 16_384 {
            return Some(PcPdfBuffer::null());
        }
        let rects = unsafe { slice::from_raw_parts(rects, rects_len) };
        let (bytes, _) = remove_annotations(doc, page_index as usize, rects)?;
        copy_buffer(bytes)
    })
}

#[no_mangle]
pub extern "C" fn pc_search_text(
    doc: *const PcDocument,
    query: *const u8,
    query_len: usize,
    case_sensitive: bool,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    ffi_opt!(0, {
        let doc = unsafe { doc.as_ref() }?;
        let query = std::str::from_utf8(input(query, query_len)?).ok()?;
        if query.is_empty() || buf.is_null() {
            return Some(0);
        }
        let mut output = String::new();
        let mut match_index = 0usize;
        for page_index in 0..doc.info.pages.len() {
            let Some(text) = text_for_page(doc, page_index) else { continue };
            for range in text.find_opts(query, case_sensitive, false) {
                let glyphs: Vec<usize> = range.collect();
                for rect in text.glyph_rects(&glyphs) {
                    use std::fmt::Write;
                    let _ = writeln!(
                        output,
                        "{},{},{:.2},{:.2},{:.2},{:.2};",
                        match_index,
                        page_index,
                        rect[0],
                        rect[1],
                        rect[2] - rect[0],
                        rect[3] - rect[1]
                    );
                }
                match_index += 1;
            }
        }
        let bytes = output.as_bytes();
        if buf_len < bytes.len() {
            return Some(bytes.len());
        }
        unsafe { std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, bytes.len()) };
        bytes.len()
    })
}

#[no_mangle]
pub extern "C" fn pc_export_to_png(
    doc: *const PcDocument,
    page_index: u32,
    dpi: f64,
) -> PcPngBuffer {
    ffi_opt!(PcPngBuffer::null(), {
        let image = pc_render_page(doc, page_index, dpi);
        if image.pixels.is_null() {
            return Some(PcPngBuffer::null());
        }
        let bytes = unsafe { slice::from_raw_parts(image.pixels, image.len) };
        let mut png = Vec::new();
        let mut encoder = png::Encoder::new(&mut png, image.width, image.height);
        encoder.set_color(png::ColorType::Rgba);
        encoder.set_depth(png::BitDepth::Eight);
        let mut writer = encoder.write_header().ok()?;
        writer.write_image_data(bytes).ok()?;
        drop(writer);
        pc_free_image(image);
        let result = PcPngBuffer { data: png.as_mut_ptr(), len: png.len() };
        std::mem::forget(png);
        result
    })
}

#[no_mangle]
pub extern "C" fn pc_merge_pdfs(input_paths: *const u8, input_paths_len: usize) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let paths = std::str::from_utf8(input(input_paths, input_paths_len)?).ok()?;
        let mut sources = Vec::new();
        for path in paths.split(';').filter(|path| !path.is_empty()) {
            let bytes = std::fs::read(path).ok()?;
            sources.push((path, Document::open(Arc::new(bytes)).ok()?));
        }
        let refs: Vec<(&str, &Document)> = sources.iter().map(|(path, doc)| (*path, doc)).collect();
        let merged = pdfcraft_organize::combine(&refs).ok()?;
        copy_buffer(write_full(&merged, &SaveOptions::default()).ok()?)
    })
}

#[no_mangle]
pub extern "C" fn pc_split_pdf(
    doc: *const PcDocument,
    output_dir: *const u8,
    output_dir_len: usize,
) -> u32 {
    ffi_opt!(0, {
        let doc = unsafe { doc.as_ref() }?;
        let dir = std::str::from_utf8(input(output_dir, output_dir_len)?).ok()?;
        std::fs::create_dir_all(dir).ok()?;
        let source = Document::open(doc.bytes.clone()).ok()?;
        let pages = pdfcraft_organize::split(&source, &pdfcraft_organize::SplitBy::PageCount(1)).ok()?;
        for (index, page) in pages.iter().enumerate() {
            let bytes = write_full(page, &SaveOptions::default()).ok()?;
            let path = Path::new(dir).join(format!("page-{}.pdf", index + 1));
            std::fs::write(path, bytes).ok()?;
        }
        pages.len().try_into().ok()?
    })
}

#[no_mangle]
pub extern "C" fn pc_split_pdf_selected(
    doc: *const PcDocument,
    page_indices: *const u8,
    page_indices_len: usize,
    output_dir: *const u8,
    output_dir_len: usize,
) -> u32 {
    ffi_opt!(0, {
        let doc = unsafe { doc.as_ref() }?;
        let indices = std::str::from_utf8(input(page_indices, page_indices_len)?).ok()?;
        let dir = std::str::from_utf8(input(output_dir, output_dir_len)?).ok()?;
        let selected: Vec<usize> = indices
            .split(',')
            .filter_map(|value| value.trim().parse::<usize>().ok())
            .collect();
        if selected.is_empty() {
            0
        } else {
            std::fs::create_dir_all(dir).ok()?;
            let source = Document::open(doc.bytes.clone()).ok()?;
            let total = pdfcraft_organize::page_count(&source).ok()?;
            let mut written = 0u32;
            for (position, page_index) in selected.iter().enumerate() {
                if *page_index >= total {
                    continue;
                }
                let page = pdfcraft_organize::extract_pages(&source, &[*page_index]).ok()?;
                let bytes = write_full(&page, &SaveOptions::default()).ok()?;
                let path = Path::new(dir).join(format!("page-{}.pdf", position + 1));
                std::fs::write(path, bytes).ok()?;
                written = written.saturating_add(1);
            }
            written
        }
    })
}

#[no_mangle]
pub extern "C" fn pc_free_image(image: PcImage) {
    if image.pixels.is_null() || image.len == 0 {
        return;
    }
    unsafe { drop(Vec::from_raw_parts(image.pixels, image.len / 4, image.len / 4)) };
}

#[no_mangle]
pub extern "C" fn pc_free_png_buffer(buffer: PcPngBuffer) {
    if buffer.data.is_null() || buffer.len == 0 {
        return;
    }
    unsafe { drop(Vec::from_raw_parts(buffer.data, buffer.len, buffer.len)) };
}

#[no_mangle]
pub extern "C" fn pc_free_pdf_buffer(buffer: PcPdfBuffer) {
    if buffer.data.is_null() || buffer.len == 0 {
        return;
    }
    unsafe { drop(Vec::from_raw_parts(buffer.data, buffer.len, buffer.len)) };
}

#[no_mangle]
pub extern "C" fn pc_free_document(doc: *mut PcDocument) {
    if !doc.is_null() {
        unsafe { drop(Box::from_raw(doc)) };
    }
}

// ── Text Selection APIs ───────────────────────────────────────────────────────

/// Text position in PDF for text selection.
/// Offsets are zero-based glyph indices within a page.
#[repr(C)]
pub struct PcTextPosition {
    pub page: u32,       // Page index (0-based)
    pub offset: u32,     // Glyph index within the page
    pub reserved1: u32,  // Reserved for future use
    pub reserved2: u32,  // Reserved for future use
}

/// Hit test: find the text position nearest to the given point on a PDF page.
/// 
/// Returns true if a valid position was found, false otherwise.
/// Coordinates are in PDF user space points (typically 1/72 inch).
#[no_mangle]
pub extern "C" fn pc_hit_test(
    doc: *const PcDocument,
    page_index: u32,
    x: f64,
    y: f64,
    out_pos: *mut PcTextPosition,
) -> bool {
    ffi!(false, {
        let Some(doc) = (unsafe { doc.as_ref() }) else { return false };
        if out_pos.is_null() {
            return false;
        }
        
        let page_index = page_index as usize;
        if page_index >= doc.info.pages.len() {
            return false;
        }
        
        let Some(text) = text_for_page(doc, page_index) else { return false };
        if text.glyphs.is_empty() {
            return false;
        }

        let mut best_offset = 0usize;
        let mut best_distance = f64::MAX;

        for (i, glyph) in text.glyphs.iter().enumerate() {
            let rect = glyph.rect;
            let dx = if x < f64::from(rect[0]) {
                f64::from(rect[0]) - x
            } else if x > f64::from(rect[2]) {
                x - f64::from(rect[2])
            } else {
                0.0
            };
            let dy = if y < f64::from(rect[1]) {
                f64::from(rect[1]) - y
            } else if y > f64::from(rect[3]) {
                y - f64::from(rect[3])
            } else {
                0.0
            };
            let distance = dx * dx + dy * dy;

            if distance < best_distance {
                best_distance = distance;
                best_offset = i;
            }
        }
        
        let out = unsafe { &mut *out_pos };
        out.page = page_index as u32;
        out.offset = best_offset as u32;
        out.reserved1 = 0;
        out.reserved2 = 0;
        
        true
    })
}

/// Get selection rectangles for a text range within a PDF page.
/// 
/// Returns the number of floats written to buffer (or required if buffer is too small).
/// Each rectangle is 4 floats: x, y, width, height (in PDF points).
/// 
/// Buffer layout: [x0, y0, w0, h0, x1, y1, w1, h1, ...]
#[no_mangle]
pub extern "C" fn pc_get_selection_rects(
    doc: *const PcDocument,
    page_index: u32,
    start_offset: u32,
    end_offset: u32,
    buf: *mut f32,
    buf_capacity: usize,
) -> usize {
    ffi!(0, {
        let Some(doc) = (unsafe { doc.as_ref() }) else { return 0 };
        let page_index = page_index as usize;
        
        if page_index >= doc.info.pages.len() {
            return 0;
        }
        
        let Some(text) = text_for_page(doc, page_index) else { return 0 };
        
        // Normalize offsets
        let start = start_offset.min(end_offset) as usize;
        let end = start_offset.max(end_offset) as usize;
        
        if start >= end {
            return 0;
        }
        
        // Get glyph rectangles for the selection range
        let glyph_indices: Vec<usize> = (start..end.min(text.glyphs.len())).collect();
        let rects = text.glyph_rects(&glyph_indices);
        
        let rect_count = rects.len();
        
        // If buffer is null or too small, return required size
        if buf.is_null() || buf_capacity < rect_count * 4 {
            return rect_count * 4;
        }
        
        // Write rectangles to buffer
        let buf_slice = unsafe { slice::from_raw_parts_mut(buf, rect_count * 4) };
        
        for (i, rect) in rects.iter().enumerate() {
            buf_slice[i * 4] = rect[0] as f32;     // x
            buf_slice[i * 4 + 1] = rect[1] as f32; // y
            buf_slice[i * 4 + 2] = (rect[2] - rect[0]) as f32; // width
            buf_slice[i * 4 + 3] = (rect[3] - rect[1]) as f32; // height
        }
        
        rect_count * 4
    })
}

/// Get the total glyph count for a PDF page.
#[no_mangle]
pub extern "C" fn pc_get_page_char_count(
    doc: *const PcDocument,
    page_index: u32,
) -> u32 {
    ffi!(0, {
        let Some(doc) = (unsafe { doc.as_ref() }) else { return 0 };
        let page_index = page_index as usize;
        
        if page_index >= doc.info.pages.len() {
            return 0;
        }
        
        let Some(text) = text_for_page(doc, page_index) else { return 0 };
        text.glyphs.len().try_into().unwrap_or(u32::MAX)
    })
}

/// Get the bounding boxes for every glyph on a page.
///
/// Returns the number of floats written (or required when the buffer is too small).
/// Each glyph occupies four floats: x, y, width, height in page points.
#[no_mangle]
pub extern "C" fn pc_get_page_glyph_rects(
    doc: *const PcDocument,
    page_index: u32,
    buf: *mut f32,
    buf_capacity: usize,
) -> usize {
    ffi!(0, {
        let Some(doc) = (unsafe { doc.as_ref() }) else { return 0 };
        let page_index = page_index as usize;
        if page_index >= doc.info.pages.len() {
            return 0;
        }

        let Some(text) = text_for_page(doc, page_index) else { return 0 };
        let required = text.glyphs.len().saturating_mul(4);
        if buf.is_null() || buf_capacity < required {
            return required;
        }

        let output = unsafe { slice::from_raw_parts_mut(buf, required) };
        for (i, glyph) in text.glyphs.iter().enumerate() {
            let rect = glyph.rect;
            output[i * 4] = rect[0];
            output[i * 4 + 1] = rect[1];
            output[i * 4 + 2] = rect[2] - rect[0];
            output[i * 4 + 3] = rect[3] - rect[1];
        }

        required
    })
}

/// Extract text covered by a half-open glyph range on a PDF page.
/// 
/// Returns the number of bytes written (or required if buffer is too small).
/// The text is UTF-8 encoded and NOT null-terminated.
/// Offsets are glyph indices, and the end offset is exclusive.
#[no_mangle]
pub extern "C" fn pc_get_text_range(
    doc: *const PcDocument,
    page_index: u32,
    start_offset: u32,
    end_offset: u32,
    buf: *mut u8,
    buf_len: usize,
) -> usize {
    ffi!(0, {
        let Some(doc) = (unsafe { doc.as_ref() }) else { return 0 };
        let page_index = page_index as usize;
        
        if page_index >= doc.info.pages.len() {
            return 0;
        }
        
        let Some(text) = text_for_page(doc, page_index) else { return 0 };
        let start = start_offset.min(end_offset) as usize;
        let end = (start_offset.max(end_offset) as usize).min(text.glyphs.len());

        if start >= end {
            return 0;
        }

        let selected = text.text_of(start..end);
        let bytes = selected.as_bytes();
        
        // If buffer is null or too small, return required size
        if buf.is_null() || buf_len < bytes.len() {
            return bytes.len();
        }
        
        unsafe {
            std::ptr::copy_nonoverlapping(bytes.as_ptr(), buf, bytes.len());
        }
        
        bytes.len()
    })
}

#[cfg(test)]
mod pdf_text_selection_tests {
    use super::*;

    fn one_page_pdf() -> Option<Vec<u8>> {
        let mut document = Document::new_empty();
        let root = document.root()?;
        let catalog = document.get(root);
        let pages_ref = catalog.as_dict()?.reference(b"Pages")?;

        let mut page = Dict::new();
        page.set(b"Type".to_vec(), Object::name("Page"));
        page.set(b"Parent".to_vec(), Object::Ref(pages_ref));
        page.set(b"MediaBox".to_vec(), Object::Array(vec![0.into(), 0.into(), 200.into(), 100.into()]));
        let page_ref = document.add(Object::Dict(page));
        document
            .update_dict(pages_ref, |dict| {
                dict.set(b"Kids".to_vec(), Object::Array(vec![Object::Ref(page_ref)]));
                dict.set(b"Count".to_vec(), Object::Int(1));
            })
            .ok()?;
        write_full(&document, &SaveOptions::default()).ok()
    }

    #[test]
    fn text_position_initializes_correctly() {
        let pos = PcTextPosition {
            page: 0,
            offset: 0,
            reserved1: 0,
            reserved2: 0,
        };
        assert_eq!(pos.page, 0);
    }

    #[test]
    fn hit_test_returns_false_for_null_doc() {
        let mut pos = PcTextPosition {
            page: 0,
            offset: 0,
            reserved1: 0,
            reserved2: 0,
        };
        let result = pc_hit_test(std::ptr::null(), 0, 0.0, 0.0, &mut pos);
        assert!(!result);
    }

    #[test]
    fn get_selection_rects_returns_zero_for_null_doc() {
        let result = pc_get_selection_rects(
            std::ptr::null(),
            0,
            0,
            10,
            std::ptr::null_mut(),
            0,
        );
        assert_eq!(result, 0);
    }

    #[test]
    fn markup_annotation_persists_and_can_be_removed() {
        let Some(bytes) = one_page_pdf() else {
            assert!(false, "could not build test PDF");
            return;
        };
        let Some(raw_doc) = open_document(bytes) else {
            assert!(false, "could not open test PDF");
            return;
        };
        let source = unsafe { Box::from_raw(raw_doc) };
        let rects = [10.0, 20.0, 30.0, 10.0];
        let Some(marked) = apply_annotation(&source, 0, &rects, 0, "Selected text") else {
            assert!(false, "could not create markup annotation");
            return;
        };
        let Some(marked_doc) = Document::open(Arc::new(marked.clone())).ok() else {
            assert!(false, "could not reopen annotated PDF");
            return;
        };
        let Some(page_ref) = pdfcraft_organize::pages(&marked_doc).ok().and_then(|pages| pages.first().map(|page| page.obj)) else {
            assert!(false, "annotated PDF has no page");
            return;
        };
        let Some((_, annots, _)) = read_annotations(&marked_doc, page_ref) else {
            assert!(false, "annotated page has invalid annotations");
            return;
        };
        assert_eq!(annots.len(), 1);
        let resolved = marked_doc.resolve(&annots[0]);
        let Some(annotation) = resolved.as_dict() else {
            assert!(false, "annotation is not a dictionary");
            return;
        };
        assert_eq!(annotation.name(b"Subtype"), Some(b"Highlight".as_slice()));
        assert_eq!(overlapping_annotation_count(&marked_doc, page_ref, &[[10.0, 70.0, 40.0, 80.0]]), Some(1));

        let Some(note) = apply_annotation(&source, 0, &rects, 3, "Review this passage") else {
            assert!(false, "could not create note annotation");
            return;
        };
        let Some(note_doc) = Document::open(Arc::new(note)).ok() else {
            assert!(false, "could not reopen PDF with note");
            return;
        };
        let Some(note_page_ref) = pdfcraft_organize::pages(&note_doc).ok().and_then(|pages| pages.first().map(|page| page.obj)) else {
            assert!(false, "PDF with note has no page");
            return;
        };
        let Some((_, notes, _)) = read_annotations(&note_doc, note_page_ref) else {
            assert!(false, "note page has invalid annotations");
            return;
        };
        assert_eq!(notes.len(), 1);
        let resolved_note = note_doc.resolve(&notes[0]);
        assert_eq!(resolved_note.as_dict().and_then(|dict| dict.name(b"Subtype")), Some(b"Text".as_slice()));
        assert_eq!(overlapping_annotation_count(&note_doc, note_page_ref, &[[10.0, 70.0, 40.0, 80.0]]), Some(1));

        let Some(marked_native) = open_document(marked) else {
            assert!(false, "could not open annotated PDF in renderer");
            return;
        };
        let marked_source = unsafe { Box::from_raw(marked_native) };
        let Some((deleted, removed)) = remove_annotations(&marked_source, 0, &rects) else {
            assert!(false, "could not remove markup annotation");
            return;
        };
        assert_eq!(removed, 1);
        let Some(deleted_doc) = Document::open(Arc::new(deleted)).ok() else {
            assert!(false, "could not reopen PDF after deletion");
            return;
        };
        let Some(deleted_page_ref) = pdfcraft_organize::pages(&deleted_doc).ok().and_then(|pages| pages.first().map(|page| page.obj)) else {
            assert!(false, "deleted PDF has no page");
            return;
        };
        let Some((_, remaining, _)) = read_annotations(&deleted_doc, deleted_page_ref) else {
            assert!(false, "deleted page has invalid annotations");
            return;
        };
        assert!(remaining.is_empty());
    }
}
