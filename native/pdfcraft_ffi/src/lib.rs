//! C ABI for the PdfCraft v0.7.1 renderer and text layer.

use std::panic::{self, AssertUnwindSafe};
use std::path::Path;
use std::slice;
use std::sync::{Arc, Mutex};

use pdfcraft_cos::{write_full, Document, SaveOptions};
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
        let mut pixels = rendered.rgba.into_words();
        let len = pixels.len().checked_mul(4)?;
        let result = PcImage {
            pixels: pixels.as_mut_ptr().cast(),
            width: rendered.width,
            height: rendered.height,
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
