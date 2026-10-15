//! C ABI for the PdfCraft v0.7.1 renderer and text layer.

use std::panic::{self, AssertUnwindSafe};
use std::path::Path;
use std::slice;
use std::sync::{Arc, Mutex};

use lopdf::{Document as LopdfDocument, Object as LopdfObject};
use pdfcraft_cos::Stream;
use pdfcraft_cos::{write_full, write_incremental, Dict, Document, Object, PdfString, SaveOptions};
use pdfcraft_render::{inspect, PageRenderer, RenderConfig, RenderRequest, RequestKind};

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
        Self {
            pixels: std::ptr::null_mut(),
            width: 0,
            height: 0,
            len: 0,
        }
    }
}

#[repr(C)]
pub struct PcPngBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl PcPngBuffer {
    fn null() -> Self {
        Self {
            data: std::ptr::null_mut(),
            len: 0,
        }
    }
}

#[repr(C)]
pub struct PcPdfBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl PcPdfBuffer {
    fn null() -> Self {
        Self {
            data: std::ptr::null_mut(),
            len: 0,
        }
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
    let result = PcPdfBuffer {
        data: bytes.as_mut_ptr(),
        len: bytes.len(),
    };
    std::mem::forget(bytes);
    result
}

fn push_u32(buffer: &mut Vec<u8>, value: u32) {
    buffer.extend_from_slice(&value.to_le_bytes());
}

fn push_f32(buffer: &mut Vec<u8>, value: f32) {
    buffer.extend_from_slice(&value.to_le_bytes());
}

fn read_lopdf_string(value: &[u8]) -> String {
    if value.starts_with(&[0xfe, 0xff]) {
        let units: Vec<u16> = value[2..]
            .chunks_exact(2)
            .map(|pair| u16::from_be_bytes([pair[0], pair[1]]))
            .collect();
        String::from_utf16_lossy(&units)
    } else if value.starts_with(&[0xff, 0xfe]) {
        let units: Vec<u16> = value[2..]
            .chunks_exact(2)
            .map(|pair| u16::from_le_bytes([pair[0], pair[1]]))
            .collect();
        String::from_utf16_lossy(&units)
    } else {
        String::from_utf8_lossy(value).into_owned()
    }
}

fn extract_document_outline(bytes: &[u8]) -> Option<Vec<u8>> {
    let document = LopdfDocument::load_mem(bytes).ok()?;
    if document.catalog().ok()?.get(b"Outlines").is_err() {
        return Some(0u32.to_le_bytes().to_vec());
    }
    let toc = match document.get_toc() {
        Ok(toc) => toc,
        Err(_) => return None,
    };
    let entries = toc.toc;
    let mut output = Vec::new();
    push_u32(&mut output, entries.len().try_into().ok()?);
    for entry in entries {
        let title = entry.title.as_bytes();
        push_u32(&mut output, entry.level.saturating_sub(1).try_into().ok()?);
        push_u32(&mut output, entry.page.try_into().ok()?);
        push_u32(&mut output, title.len().try_into().ok()?);
        output.extend_from_slice(title);
    }
    Some(output)
}

fn annotation_rectangles_in_view(
    dict: &lopdf::Dictionary,
    page: &pdfcraft_render::PageInfo,
) -> Option<Vec<[f32; 4]>> {
    let user_rectangles: Vec<[f32; 4]> =
        if let Ok(values) = dict.get(b"QuadPoints").and_then(LopdfObject::as_array) {
            let mut rects = Vec::new();
            for quad in values.chunks_exact(8) {
                let points: Vec<f32> = quad
                    .iter()
                    .map(|value| value.as_float().ok())
                    .collect::<Option<_>>()?;
                let corners = [
                    page.user_to_view(points[0], points[1]),
                    page.user_to_view(points[2], points[3]),
                    page.user_to_view(points[4], points[5]),
                    page.user_to_view(points[6], points[7]),
                ];
                let x0 = corners
                    .iter()
                    .map(|point| point[0])
                    .fold(f32::INFINITY, f32::min);
                let y0 = corners
                    .iter()
                    .map(|point| point[1])
                    .fold(f32::INFINITY, f32::min);
                let x1 = corners
                    .iter()
                    .map(|point| point[0])
                    .fold(f32::NEG_INFINITY, f32::max);
                let y1 = corners
                    .iter()
                    .map(|point| point[1])
                    .fold(f32::NEG_INFINITY, f32::max);
                if [x0, y0, x1, y1].iter().all(|value| value.is_finite()) && x1 > x0 && y1 > y0 {
                    rects.push([x0, y0, x1 - x0, y1 - y0]);
                }
            }
            rects
        } else {
            let values = dict.get(b"Rect").ok()?.as_array().ok()?;
            if values.len() != 4 {
                return None;
            }
            let coords: Vec<f32> = values
                .iter()
                .map(|value| value.as_float().ok())
                .collect::<Option<_>>()?;
            let corners = [
                page.user_to_view(coords[0], coords[1]),
                page.user_to_view(coords[2], coords[3]),
            ];
            let x0 = corners[0][0].min(corners[1][0]);
            let y0 = corners[0][1].min(corners[1][1]);
            let x1 = corners[0][0].max(corners[1][0]);
            let y1 = corners[0][1].max(corners[1][1]);
            vec![[x0, y0, x1 - x0, y1 - y0]]
        };
    Some(user_rectangles)
}

fn extract_text_annotations(source: &PcDocument) -> Option<Vec<u8>> {
    let document = LopdfDocument::load_mem(source.bytes.as_slice()).ok()?;
    let pages = document.get_pages();
    let mut entries = Vec::new();
    for (page_number, page_id) in pages {
        let page_index = page_number.checked_sub(1)? as usize;
        let page_info = source.info.pages.get(page_index)?;
        let page_dict = document.get_object(page_id).ok()?.as_dict().ok()?;
        let annotations = match page_dict.get(b"Annots") {
            Ok(LopdfObject::Array(items)) => items.clone(),
            Ok(LopdfObject::Reference(reference)) => document
                .get_object(*reference)
                .ok()?
                .as_array()
                .ok()?
                .clone(),
            Ok(_) => continue,
            Err(_) => continue,
        };
        for annotation in annotations {
            let annotation_dict = match annotation {
                LopdfObject::Dictionary(dict) => dict,
                LopdfObject::Reference(reference) => {
                    document.get_object(reference).ok()?.as_dict().ok()?.clone()
                }
                _ => continue,
            };
            let Some(subtype) = annotation_dict
                .get(b"Subtype")
                .ok()
                .and_then(|value| value.as_name().ok())
            else {
                continue;
            };
            let kind = match subtype {
                b"Highlight" => 0u32,
                b"Underline" => 1u32,
                b"StrikeOut" => 2u32,
                b"Text" => 3u32,
                b"Ink" => 4u32,
                b"FreeText" => 5u32,
                b"Stamp" => 6u32,
                _ => continue,
            };
            let content = annotation_dict
                .get(b"Contents")
                .and_then(LopdfObject::as_str)
                .map(read_lopdf_string)
                .unwrap_or_default();
            let color = annotation_dict
                .get(b"C")
                .ok()
                .and_then(|value| value.as_array().ok())
                .and_then(|values| {
                    let values: Vec<f32> = values
                        .iter()
                        .map(|value| value.as_float().ok())
                        .collect::<Option<_>>()?;
                    (values.len() >= 3).then(|| [values[0], values[1], values[2]])
                })
                .filter(|values| {
                    values
                        .iter()
                        .all(|value| value.is_finite() && (0.0..=1.0).contains(value))
                })
                .unwrap_or([1.0, 0.82, 0.1]);
            let opacity = annotation_dict
                .get(b"CA")
                .ok()
                .and_then(|value| value.as_float().ok())
                .filter(|value| value.is_finite() && (0.0..=1.0).contains(value))
                .unwrap_or(1.0);
            let width = annotation_dict
                .get(b"BS")
                .ok()
                .and_then(|value| value.as_dict().ok())
                .and_then(|dict| dict.get(b"W").ok())
                .and_then(|value| value.as_float().ok())
                .filter(|value| value.is_finite() && *value > 0.0)
                .unwrap_or(1.0);
            let points = if kind == 4 {
                let Some(path) = annotation_dict
                    .get(b"InkList")
                    .ok()
                    .and_then(|value| value.as_array().ok())
                    .and_then(|paths| paths.first())
                    .and_then(|value| value.as_array().ok())
                else {
                    continue;
                };
                if path.len() < 4 || path.len() % 2 != 0 || path.len() > 16_384 {
                    continue;
                }
                let mut points = Vec::with_capacity(path.len());
                let mut valid = true;
                for pair in path.chunks_exact(2) {
                    let Some(x) = pair[0].as_float().ok() else {
                        valid = false;
                        break;
                    };
                    let Some(y) = pair[1].as_float().ok() else {
                        valid = false;
                        break;
                    };
                    let point = page_info.user_to_view(x, y);
                    if !point[0].is_finite() || !point[1].is_finite() {
                        valid = false;
                        break;
                    }
                    points.push(point);
                }
                if !valid {
                    continue;
                }
                points
            } else {
                Vec::new()
            };
            let Some(rects) = annotation_rectangles_in_view(&annotation_dict, page_info) else {
                continue;
            };
            if rects.is_empty() || rects.len() > 4096 || content.len() > 1_048_576 {
                continue;
            }
            entries.push((
                page_index as u32,
                kind,
                content,
                rects,
                color,
                opacity,
                width,
                points,
            ));
            if entries.len() > 65_536
                || entries.iter().map(|entry| entry.3.len()).sum::<usize>() > 1_000_000
            {
                return None;
            }
        }
    }

    let mut output = Vec::new();
    push_u32(&mut output, entries.len().try_into().ok()?);
    for (page, kind, content, rects, color, opacity, width, points) in entries {
        push_u32(&mut output, page);
        push_u32(&mut output, kind);
        push_u32(&mut output, content.len().try_into().ok()?);
        output.extend_from_slice(content.as_bytes());
        push_u32(&mut output, rects.len().try_into().ok()?);
        for rect in rects {
            for value in rect {
                push_f32(&mut output, value);
            }
        }
        for value in color {
            push_f32(&mut output, value);
        }
        push_f32(&mut output, opacity);
        push_f32(&mut output, width);
        push_u32(&mut output, points.len().try_into().ok()?);
        for point in points {
            push_f32(&mut output, point[0]);
            push_f32(&mut output, point[1]);
        }
    }
    if output.len() > 64 * 1024 * 1024 {
        return None;
    }
    Some(output)
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
            if ![x, y, width, height].iter().all(|value| value.is_finite())
                || width <= 0.0
                || height <= 0.0
            {
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
            let x_min = points
                .iter()
                .map(|point| point[0])
                .fold(f32::INFINITY, f32::min);
            let y_min = points
                .iter()
                .map(|point| point[1])
                .fold(f32::INFINITY, f32::min);
            let x_max = points
                .iter()
                .map(|point| point[0])
                .fold(f32::NEG_INFINITY, f32::max);
            let y_max = points
                .iter()
                .map(|point| point[1])
                .fold(f32::NEG_INFINITY, f32::max);
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

fn read_annotations(
    doc: &Document,
    page_ref: pdfcraft_cos::ObjRef,
) -> Option<(Dict, Vec<Object>, Option<pdfcraft_cos::ObjRef>)> {
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
    let coords: Vec<f32> = values
        .iter()
        .map(|value| value.as_f64().map(|v| v as f32))
        .collect::<Option<_>>()?;
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
    resolved
        .as_dict()
        .and_then(|dict| dict.name(b"Subtype"))
        .is_some_and(|subtype| {
            matches!(
                subtype,
                b"Highlight"
                    | b"Underline"
                    | b"StrikeOut"
                    | b"Text"
                    | b"Ink"
                    | b"FreeText"
                    | b"Stamp"
            )
        })
}

fn annotation_overlaps_selection(
    doc: &Document,
    annot: &Object,
    selection_rects: &[[f32; 4]],
) -> bool {
    if !is_deletable_annotation(doc, annot) {
        return false;
    }
    let resolved = doc.resolve(annot);
    let Some(dict) = resolved.as_dict() else {
        return false;
    };
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
                values
                    .iter()
                    .step_by(2)
                    .copied()
                    .fold(f32::INFINITY, f32::min),
                values
                    .iter()
                    .skip(1)
                    .step_by(2)
                    .copied()
                    .fold(f32::INFINITY, f32::min),
                values
                    .iter()
                    .step_by(2)
                    .copied()
                    .fold(f32::NEG_INFINITY, f32::max),
                values
                    .iter()
                    .skip(1)
                    .step_by(2)
                    .copied()
                    .fold(f32::NEG_INFINITY, f32::max),
            ])
        });
        return quad_rects.into_iter().any(|rect| {
            selection_rects
                .iter()
                .any(|selected| intersects(rect, *selected))
        });
    }
    annotation_user_rect(doc, annot).is_some_and(|rect| {
        selection_rects
            .iter()
            .any(|selected| intersects(rect, *selected))
    })
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

fn apply_image_annotation(
    source: &PcDocument,
    page_index: usize,
    rect: &[f32],
    jpeg: &[u8],
    pixel_width: u32,
    pixel_height: u32,
) -> Option<Vec<u8>> {
    if rect.len() != 4
        || jpeg.is_empty()
        || jpeg.len() > 20 * 1024 * 1024
        || pixel_width == 0
        || pixel_height == 0
        || pixel_width > 8192
        || pixel_height > 8192
        || u64::from(pixel_width) * u64::from(pixel_height) > 50_000_000
        || !rect.iter().all(|value| value.is_finite())
        || rect[2] <= 0.0
        || rect[3] <= 0.0
    {
        return None;
    }
    let page_info = source.info.pages.get(page_index)?;
    let user_rect = selection_rects_in_user_space(page_info, rect)?.first()?.0;
    let mut document = Document::open(source.bytes.clone()).ok()?;
    if document
        .permissions()
        .is_some_and(|permissions| !permissions.annotate())
    {
        return None;
    }
    let page_ref = pdfcraft_organize::pages(&document)
        .ok()?
        .get(page_index)?
        .obj;
    let (page_dict, mut annots, annots_ref) = read_annotations(&document, page_ref)?;

    let mut image_dict = Dict::new();
    image_dict.set(b"Type".to_vec(), Object::name("XObject"));
    image_dict.set(b"Subtype".to_vec(), Object::name("Image"));
    image_dict.set(b"Width".to_vec(), Object::Int(pixel_width.into()));
    image_dict.set(b"Height".to_vec(), Object::Int(pixel_height.into()));
    image_dict.set(b"ColorSpace".to_vec(), Object::name("DeviceRGB"));
    image_dict.set(b"BitsPerComponent".to_vec(), Object::Int(8));
    image_dict.set(b"Filter".to_vec(), Object::name("DCTDecode"));
    let image_ref = document.add(Object::Stream(Stream::from_raw(image_dict, jpeg.to_vec())));

    let width = user_rect[2] - user_rect[0];
    let height = user_rect[3] - user_rect[1];
    let appearance_content = format!("q {width} 0 0 {height} 0 0 cm /Im0 Do Q").into_bytes();
    let mut xobjects = Dict::new();
    xobjects.set(b"Im0".to_vec(), Object::Ref(image_ref));
    let mut resources = Dict::new();
    resources.set(b"XObject".to_vec(), Object::Dict(xobjects));
    let mut appearance_dict = Dict::new();
    appearance_dict.set(b"Type".to_vec(), Object::name("XObject"));
    appearance_dict.set(b"Subtype".to_vec(), Object::name("Form"));
    appearance_dict.set(b"FormType".to_vec(), Object::Int(1));
    appearance_dict.set(
        b"BBox".to_vec(),
        Object::Array(vec![
            Object::Int(0),
            Object::Int(0),
            Object::Real(f64::from(width)),
            Object::Real(f64::from(height)),
        ]),
    );
    appearance_dict.set(b"Resources".to_vec(), Object::Dict(resources));
    let appearance_ref = document.add(Object::Stream(Stream::from_raw(
        appearance_dict,
        appearance_content,
    )));

    let mut appearance = Dict::new();
    appearance.set(b"N".to_vec(), Object::Ref(appearance_ref));
    let mut annotation = Dict::new();
    annotation.set(b"Type".to_vec(), Object::name("Annot"));
    annotation.set(b"Subtype".to_vec(), Object::name("Stamp"));
    annotation.set(
        b"Rect".to_vec(),
        Object::Array(
            user_rect
                .iter()
                .map(|value| Object::Real(f64::from(*value)))
                .collect(),
        ),
    );
    annotation.set(b"F".to_vec(), Object::Int(4));
    annotation.set(b"T".to_vec(), PdfString::text("DocGlance"));
    annotation.set(b"Contents".to_vec(), PdfString::text("Inserted image"));
    annotation.set(b"AP".to_vec(), Object::Dict(appearance));
    let reference = document.add(Object::Dict(annotation));
    annots.push(Object::Ref(reference));
    set_annotations(&mut document, page_ref, page_dict, annots, annots_ref);
    write_incremental(&document, &SaveOptions::default()).ok()
}

fn apply_annotation(
    source: &PcDocument,
    page_index: usize,
    rects: &[f32],
    kind: u32,
    contents: &str,
    color: [f32; 3],
    opacity: f32,
    width: f32,
) -> Option<Vec<u8>> {
    let page_info = source.info.pages.get(page_index)?;
    if !matches!(kind, 0..=5)
        || (matches!(kind, 3 | 5) && contents.trim().is_empty())
        || !color
            .iter()
            .all(|value| value.is_finite() && (0.0..=1.0).contains(value))
        || !opacity.is_finite()
        || !(0.0..=1.0).contains(&opacity)
    {
        return None;
    }

    if kind == 4 {
        return apply_ink_annotation(source, page_index, rects, color, opacity, width);
    }

    fn apply_ink_annotation(
        source: &PcDocument,
        page_index: usize,
        points: &[f32],
        color: [f32; 3],
        opacity: f32,
        width: f32,
    ) -> Option<Vec<u8>> {
        let page_info = source.info.pages.get(page_index)?;
        if points.len() < 4
            || points.len() % 2 != 0
            || points.len() > 16_384
            || !points.iter().all(|value| value.is_finite())
            || !color
                .iter()
                .all(|value| value.is_finite() && (0.0..=1.0).contains(value))
            || !opacity.is_finite()
            || !(0.0..=1.0).contains(&opacity)
            || !width.is_finite()
            || !(0.25..=72.0).contains(&width)
        {
            return None;
        }
        let user_points: Vec<[f32; 2]> = points
            .chunks_exact(2)
            .map(|point| {
                page_info.view_to_user(
                    point[0].clamp(0.0, page_info.width),
                    point[1].clamp(0.0, page_info.height),
                )
            })
            .collect();
        let mut bounds = [
            user_points
                .iter()
                .map(|point| point[0])
                .fold(f32::INFINITY, f32::min),
            user_points
                .iter()
                .map(|point| point[1])
                .fold(f32::INFINITY, f32::min),
            user_points
                .iter()
                .map(|point| point[0])
                .fold(f32::NEG_INFINITY, f32::max),
            user_points
                .iter()
                .map(|point| point[1])
                .fold(f32::NEG_INFINITY, f32::max),
        ];
        let padding = width / 2.0;
        bounds[0] -= padding;
        bounds[1] -= padding;
        bounds[2] += padding;
        bounds[3] += padding;

        let mut document = Document::open(source.bytes.clone()).ok()?;
        if document
            .permissions()
            .is_some_and(|permissions| !permissions.annotate())
        {
            return None;
        }
        let page_ref = pdfcraft_organize::pages(&document)
            .ok()?
            .get(page_index)?
            .obj;
        let (page_dict, mut annots, annots_ref) = read_annotations(&document, page_ref)?;
        let mut annotation = Dict::new();
        annotation.set(b"Type".to_vec(), Object::name("Annot"));
        annotation.set(b"Subtype".to_vec(), Object::name("Ink"));
        annotation.set(
            b"Rect".to_vec(),
            Object::Array(
                bounds
                    .iter()
                    .map(|value| Object::Real(f64::from(*value)))
                    .collect(),
            ),
        );
        annotation.set(b"F".to_vec(), Object::Int(4));
        annotation.set(b"T".to_vec(), PdfString::text("DocGlance"));
        annotation.set(b"Contents".to_vec(), PdfString::text("Freehand ink"));
        annotation.set(
            b"C".to_vec(),
            Object::Array(
                color
                    .iter()
                    .map(|value| Object::Real(f64::from(*value)))
                    .collect(),
            ),
        );
        annotation.set(b"CA".to_vec(), Object::Real(f64::from(opacity)));
        annotation.set(
            b"BS".to_vec(),
            Object::Dict({
                let mut style = Dict::new();
                style.set(b"W".to_vec(), Object::Real(f64::from(width)));
                style.set(b"S".to_vec(), Object::name("S"));
                style
            }),
        );
        annotation.set(
            b"InkList".to_vec(),
            Object::Array(vec![Object::Array(
                user_points
                    .iter()
                    .flat_map(|point| point.iter().map(|value| Object::Real(f64::from(*value))))
                    .collect(),
            )]),
        );
        let reference = document.add(Object::Dict(annotation));
        annots.push(Object::Ref(reference));
        set_annotations(&mut document, page_ref, page_dict, annots, annots_ref);
        write_incremental(&document, &SaveOptions::default()).ok()
    }
    let mapped_rects = selection_rects_in_user_space(page_info, rects)?;
    let mut document = Document::open(source.bytes.clone()).ok()?;
    if document
        .permissions()
        .is_some_and(|permissions| !permissions.annotate())
    {
        return None;
    }
    let page_ref = pdfcraft_organize::pages(&document)
        .ok()?
        .get(page_index)?
        .obj;
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
    annotation.set(
        b"Rect".to_vec(),
        Object::Array(
            bounds
                .iter()
                .map(|value| Object::Real(f64::from(*value)))
                .collect(),
        ),
    );
    annotation.set(b"F".to_vec(), Object::Int(4));
    annotation.set(b"T".to_vec(), PdfString::text("DocGlance"));
    let unique_id = format!(
        "DocGlance-{}-{}",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .ok()?
            .as_nanos(),
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
            annotation.set(
                b"C".to_vec(),
                Object::Array(
                    color
                        .iter()
                        .map(|value| Object::Real(f64::from(*value)))
                        .collect(),
                ),
            );
            annotation.set(b"CA".to_vec(), Object::Real(f64::from(opacity)));
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
                icon_points
                    .iter()
                    .map(|point| point[0])
                    .fold(f32::INFINITY, f32::min),
                icon_points
                    .iter()
                    .map(|point| point[1])
                    .fold(f32::INFINITY, f32::min),
                icon_points
                    .iter()
                    .map(|point| point[0])
                    .fold(f32::NEG_INFINITY, f32::max),
                icon_points
                    .iter()
                    .map(|point| point[1])
                    .fold(f32::NEG_INFINITY, f32::max),
            ];
            annotation.set(
                b"Rect".to_vec(),
                Object::Array(
                    icon_rect
                        .iter()
                        .map(|value| Object::Real(f64::from(*value)))
                        .collect(),
                ),
            );
            annotation.set(b"Subtype".to_vec(), Object::name("Text"));
            annotation.set(b"Name".to_vec(), Object::name("Comment"));
            annotation.set(b"Open".to_vec(), Object::Bool(false));
            annotation.set(
                b"C".to_vec(),
                Object::Array(
                    color
                        .iter()
                        .map(|value| Object::Real(f64::from(*value)))
                        .collect(),
                ),
            );
            annotation.set(b"CA".to_vec(), Object::Real(f64::from(opacity)));
            annotation.set(b"Contents".to_vec(), PdfString::text(contents));
        }
        5 => {
            annotation.set(b"Subtype".to_vec(), Object::name("FreeText"));
            annotation.set(b"Contents".to_vec(), PdfString::text(contents));
            annotation.set(b"DA".to_vec(), PdfString::text("12 Tf 0 g"));
            annotation.set(b"Q".to_vec(), Object::Int(0));
            annotation.set(
                b"C".to_vec(),
                Object::Array(
                    color
                        .iter()
                        .map(|value| Object::Real(f64::from(*value)))
                        .collect(),
                ),
            );
            annotation.set(b"CA".to_vec(), Object::Real(f64::from(opacity)));
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
    if document
        .permissions()
        .is_some_and(|permissions| !permissions.annotate())
    {
        return None;
    }
    let page_ref = pdfcraft_organize::pages(&document)
        .ok()?
        .get(page_index)?
        .obj;
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
    Some((
        write_incremental(&document, &SaveOptions::default()).ok()?,
        removed,
    ))
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
    Some(Box::into_raw(Box::new(PcDocument {
        bytes,
        info,
        renderer: Mutex::new(renderer),
    })))
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
        let (Some(doc), Some(width), Some(height)) = (
            unsafe { doc.as_ref() },
            unsafe { width_pt.as_mut() },
            unsafe { height_pt.as_mut() },
        ) else {
            return;
        };
        let Some(page) = doc.info.pages.get(index as usize) else {
            return;
        };
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
        if copied < required {
            required
        } else {
            copied
        }
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
    color: *const f32,
    opacity: f32,
) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        let rects = if rects.is_null() || rects_len == 0 || rects_len % 4 != 0 || rects_len > 16_384
        {
            return Some(PcPdfBuffer::null());
        } else {
            unsafe { slice::from_raw_parts(rects, rects_len) }
        };
        if contents_len > 1_048_576 {
            return Some(PcPdfBuffer::null());
        }

        if color.is_null() {
            return Some(PcPdfBuffer::null());
        }
        let color = unsafe { slice::from_raw_parts(color, 3) };
        let contents = std::str::from_utf8(input(contents, contents_len)?).ok()?;
        copy_buffer(apply_annotation(
            doc,
            page_index as usize,
            rects,
            kind,
            contents,
            [color[0], color[1], color[2]],
            opacity,
            0.0,
        )?)
    })
}

/// Add a standard PDF Ink annotation from page-view point pairs.
#[no_mangle]
pub extern "C" fn pc_add_ink_annotation(
    doc: *const PcDocument,
    page_index: u32,
    points: *const f32,
    points_len: usize,
    color: *const f32,
    opacity: f32,
    width: f32,
) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        if points.is_null()
            || color.is_null()
            || points_len < 4
            || points_len % 2 != 0
            || points_len > 16_384
        {
            return Some(PcPdfBuffer::null());
        }
        let points = unsafe { slice::from_raw_parts(points, points_len) };
        let color = unsafe { slice::from_raw_parts(color, 3) };
        copy_buffer(apply_annotation(
            doc,
            page_index as usize,
            points,
            4,
            "",
            [color[0], color[1], color[2]],
            opacity,
            width,
        )?)
    })
}

/// Add an image stamp whose input is an RGB JPEG payload.
#[no_mangle]
pub extern "C" fn pc_add_image_annotation(
    doc: *const PcDocument,
    page_index: u32,
    rect: *const f32,
    rect_len: usize,
    jpeg: *const u8,
    jpeg_len: usize,
    pixel_width: u32,
    pixel_height: u32,
) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        if rect.is_null() || rect_len != 4 || jpeg_len == 0 || jpeg_len > 20 * 1024 * 1024 {
            return Some(PcPdfBuffer::null());
        }
        let rect = unsafe { slice::from_raw_parts(rect, rect_len) };
        let jpeg = input(jpeg, jpeg_len)?;
        copy_buffer(apply_image_annotation(
            doc,
            page_index as usize,
            rect,
            jpeg,
            pixel_width,
            pixel_height,
        )?)
    })
}

/// Return document outline entries as a little-endian binary buffer.
/// Each entry is (level, 1-based page, UTF-8 title length, UTF-8 title).
#[no_mangle]
pub extern "C" fn pc_get_document_outline(doc: *const PcDocument) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        copy_buffer(extract_document_outline(doc.bytes.as_slice())?)
    })
}

/// Return markup and text-note annotations as a binary buffer in page view coordinates.
#[no_mangle]
pub extern "C" fn pc_get_text_annotations(doc: *const PcDocument) -> PcPdfBuffer {
    ffi_opt!(PcPdfBuffer::null(), {
        let doc = unsafe { doc.as_ref() }?;
        copy_buffer(extract_text_annotations(doc)?)
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
        let page_ref = pdfcraft_organize::pages(&parsed)
            .ok()?
            .get(page_index as usize)?
            .obj;
        overlapping_annotation_count(&parsed, page_ref, &mapped)?
            .try_into()
            .ok()?
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
            let Some(text) = text_for_page(doc, page_index) else {
                continue;
            };
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
        let result = PcPngBuffer {
            data: png.as_mut_ptr(),
            len: png.len(),
        };
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
        let pages =
            pdfcraft_organize::split(&source, &pdfcraft_organize::SplitBy::PageCount(1)).ok()?;
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
    unsafe {
        drop(Vec::from_raw_parts(
            image.pixels,
            image.len / 4,
            image.len / 4,
        ))
    };
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
    pub page: u32,      // Page index (0-based)
    pub offset: u32,    // Glyph index within the page
    pub reserved1: u32, // Reserved for future use
    pub reserved2: u32, // Reserved for future use
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
        let Some(doc) = (unsafe { doc.as_ref() }) else {
            return false;
        };
        if out_pos.is_null() {
            return false;
        }

        let page_index = page_index as usize;
        if page_index >= doc.info.pages.len() {
            return false;
        }

        let Some(text) = text_for_page(doc, page_index) else {
            return false;
        };
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
        let Some(doc) = (unsafe { doc.as_ref() }) else {
            return 0;
        };
        let page_index = page_index as usize;

        if page_index >= doc.info.pages.len() {
            return 0;
        }

        let Some(text) = text_for_page(doc, page_index) else {
            return 0;
        };

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
            buf_slice[i * 4] = rect[0] as f32; // x
            buf_slice[i * 4 + 1] = rect[1] as f32; // y
            buf_slice[i * 4 + 2] = (rect[2] - rect[0]) as f32; // width
            buf_slice[i * 4 + 3] = (rect[3] - rect[1]) as f32; // height
        }

        rect_count * 4
    })
}

/// Get the total glyph count for a PDF page.
#[no_mangle]
pub extern "C" fn pc_get_page_char_count(doc: *const PcDocument, page_index: u32) -> u32 {
    ffi!(0, {
        let Some(doc) = (unsafe { doc.as_ref() }) else {
            return 0;
        };
        let page_index = page_index as usize;

        if page_index >= doc.info.pages.len() {
            return 0;
        }

        let Some(text) = text_for_page(doc, page_index) else {
            return 0;
        };
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
        let Some(doc) = (unsafe { doc.as_ref() }) else {
            return 0;
        };
        let page_index = page_index as usize;
        if page_index >= doc.info.pages.len() {
            return 0;
        }

        let Some(text) = text_for_page(doc, page_index) else {
            return 0;
        };
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
        let Some(doc) = (unsafe { doc.as_ref() }) else {
            return 0;
        };
        let page_index = page_index as usize;

        if page_index >= doc.info.pages.len() {
            return 0;
        }

        let Some(text) = text_for_page(doc, page_index) else {
            return 0;
        };
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
        page.set(
            b"MediaBox".to_vec(),
            Object::Array(vec![0.into(), 0.into(), 200.into(), 100.into()]),
        );
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
        let result = pc_get_selection_rects(std::ptr::null(), 0, 0, 10, std::ptr::null_mut(), 0);
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
        let color = [0.1, 0.5, 0.9];
        let opacity = 0.6;
        let Some(marked) =
            apply_annotation(&source, 0, &rects, 0, "Selected text", color, opacity, 0.0)
        else {
            assert!(false, "could not create markup annotation");
            return;
        };
        let Some(marked_doc) = Document::open(Arc::new(marked.clone())).ok() else {
            assert!(false, "could not reopen annotated PDF");
            return;
        };
        let Some(page_ref) = pdfcraft_organize::pages(&marked_doc)
            .ok()
            .and_then(|pages| pages.first().map(|page| page.obj))
        else {
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
        assert!(annotation.get(b"C").is_some());
        assert!(annotation.get(b"CA").is_some());
        assert_eq!(
            overlapping_annotation_count(&marked_doc, page_ref, &[[10.0, 70.0, 40.0, 80.0]]),
            Some(1)
        );

        let Some(note) = apply_annotation(
            &source,
            0,
            &rects,
            3,
            "Review this passage",
            color,
            opacity,
            0.0,
        ) else {
            assert!(false, "could not create note annotation");
            return;
        };
        let Some(note_doc) = Document::open(Arc::new(note.clone())).ok() else {
            assert!(false, "could not reopen PDF with note");
            return;
        };
        let Some(note_page_ref) = pdfcraft_organize::pages(&note_doc)
            .ok()
            .and_then(|pages| pages.first().map(|page| page.obj))
        else {
            assert!(false, "PDF with note has no page");
            return;
        };
        let Some((_, notes, _)) = read_annotations(&note_doc, note_page_ref) else {
            assert!(false, "note page has invalid annotations");
            return;
        };
        assert_eq!(notes.len(), 1);
        let resolved_note = note_doc.resolve(&notes[0]);
        assert_eq!(
            resolved_note
                .as_dict()
                .and_then(|dict| dict.name(b"Subtype")),
            Some(b"Text".as_slice())
        );
        assert_eq!(
            overlapping_annotation_count(&note_doc, note_page_ref, &[[10.0, 70.0, 40.0, 80.0]]),
            Some(1)
        );

        let Some(note_native) = open_document(note) else {
            assert!(false, "could not open PDF with note in renderer");
            return;
        };
        let note_source = unsafe { Box::from_raw(note_native) };
        let Some(serialized_notes) = extract_text_annotations(&note_source) else {
            assert!(false, "could not enumerate saved annotations");
            return;
        };
        let mut note_data = std::io::Cursor::new(serialized_notes);
        assert_eq!(
            u32::from_le_bytes(note_data.get_ref()[0..4].try_into().unwrap()),
            1
        );
        assert_eq!(
            u32::from_le_bytes(note_data.get_ref()[4..8].try_into().unwrap()),
            0
        );
        assert_eq!(
            u32::from_le_bytes(note_data.get_ref()[8..12].try_into().unwrap()),
            3
        );
        let contents_len =
            u32::from_le_bytes(note_data.get_ref()[12..16].try_into().unwrap()) as usize;
        assert_eq!(
            &note_data.get_ref()[16..16 + contents_len],
            b"Review this passage"
        );
        note_data.set_position((16 + contents_len + 4) as u64);
        for _ in 0..4 {
            let mut value = [0; 4];
            std::io::Read::read_exact(&mut note_data, &mut value).unwrap();
            assert!(f32::from_le_bytes(value).is_finite());
        }
        for expected in color {
            let mut value = [0; 4];
            std::io::Read::read_exact(&mut note_data, &mut value).unwrap();
            assert!((f32::from_le_bytes(value) - expected).abs() < 0.0001);
        }
        let mut opacity_bytes = [0; 4];
        std::io::Read::read_exact(&mut note_data, &mut opacity_bytes).unwrap();
        assert!((f32::from_le_bytes(opacity_bytes) - opacity).abs() < 0.0001);

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
        let Some(deleted_page_ref) = pdfcraft_organize::pages(&deleted_doc)
            .ok()
            .and_then(|pages| pages.first().map(|page| page.obj))
        else {
            assert!(false, "deleted PDF has no page");
            return;
        };
        let Some((_, remaining, _)) = read_annotations(&deleted_doc, deleted_page_ref) else {
            assert!(false, "deleted page has invalid annotations");
            return;
        };
        assert!(remaining.is_empty());
    }

    #[test]
    fn ink_annotation_persists_with_view_geometry() {
        let Some(bytes) = one_page_pdf() else {
            assert!(false, "could not build test PDF");
            return;
        };
        let Some(raw_doc) = open_document(bytes) else {
            assert!(false, "could not open test PDF");
            return;
        };
        let source = unsafe { Box::from_raw(raw_doc) };
        let points = [15.0, 20.0, 35.0, 45.0, 60.0, 32.0];
        let color = [0.2, 0.4, 0.8];
        let opacity = 0.7;
        let width = 2.5;
        let Some(updated) = apply_annotation(&source, 0, &points, 4, "", color, opacity, width)
        else {
            assert!(false, "could not create Ink annotation");
            return;
        };
        let Some(updated_doc) = Document::open(Arc::new(updated)).ok() else {
            assert!(false, "could not reopen PDF with Ink annotation");
            return;
        };
        let Some(page_ref) = pdfcraft_organize::pages(&updated_doc)
            .ok()
            .and_then(|pages| pages.first().map(|page| page.obj))
        else {
            assert!(false, "PDF with Ink annotation has no page");
            return;
        };
        let Some((_, annotations, _)) = read_annotations(&updated_doc, page_ref) else {
            assert!(false, "Ink annotation page is invalid");
            return;
        };
        assert_eq!(annotations.len(), 1);
        let annotation = updated_doc.resolve(&annotations[0]);
        assert_eq!(
            annotation.as_dict().and_then(|dict| dict.name(b"Subtype")),
            Some(b"Ink".as_slice())
        );
        assert!(annotation
            .as_dict()
            .is_some_and(|dict| dict.get(b"InkList").is_some()));
    }

    #[test]
    fn image_stamp_annotation_persists_with_appearance() {
        let Some(bytes) = one_page_pdf() else {
            assert!(false, "could not build test PDF");
            return;
        };
        let Some(raw_doc) = open_document(bytes) else {
            assert!(false, "could not open test PDF");
            return;
        };
        let source = unsafe { Box::from_raw(raw_doc) };
        let jpeg = [0xff, 0xd8, 0xff, 0xd9];
        let Some(updated) =
            apply_image_annotation(&source, 0, &[10.0, 20.0, 30.0, 20.0], &jpeg, 1, 1)
        else {
            assert!(false, "could not create image stamp annotation");
            return;
        };
        let Some(updated_doc) = Document::open(Arc::new(updated)).ok() else {
            assert!(false, "could not reopen PDF with image stamp");
            return;
        };
        let Some(page_ref) = pdfcraft_organize::pages(&updated_doc)
            .ok()
            .and_then(|pages| pages.first().map(|page| page.obj))
        else {
            assert!(false, "PDF with image stamp has no page");
            return;
        };
        let Some((_, annotations, _)) = read_annotations(&updated_doc, page_ref) else {
            assert!(false, "image stamp page is invalid");
            return;
        };
        assert_eq!(annotations.len(), 1);
        let annotation = updated_doc.resolve(&annotations[0]);
        assert_eq!(
            annotation.as_dict().and_then(|dict| dict.name(b"Subtype")),
            Some(b"Stamp".as_slice())
        );
        assert!(annotation
            .as_dict()
            .and_then(|dict| dict.get(b"AP"))
            .is_some());
    }

    #[test]
    fn free_text_annotation_persists() {
        let Some(bytes) = one_page_pdf() else {
            assert!(false, "could not build test PDF");
            return;
        };
        let Some(raw_doc) = open_document(bytes) else {
            assert!(false, "could not open test PDF");
            return;
        };
        let source = unsafe { Box::from_raw(raw_doc) };
        let Some(updated) = apply_annotation(
            &source,
            0,
            &[15.0, 20.0, 80.0, 45.0],
            5,
            "Editable page text",
            [0.1, 0.2, 0.3],
            1.0,
            0.0,
        ) else {
            assert!(false, "could not create free text annotation");
            return;
        };
        let Some(updated_doc) = Document::open(Arc::new(updated)).ok() else {
            assert!(false, "could not reopen PDF with free text");
            return;
        };
        let Some(page_ref) = pdfcraft_organize::pages(&updated_doc)
            .ok()
            .and_then(|pages| pages.first().map(|page| page.obj))
        else {
            assert!(false, "PDF with free text has no page");
            return;
        };
        let Some((_, annotations, _)) = read_annotations(&updated_doc, page_ref) else {
            assert!(false, "free text page is invalid");
            return;
        };
        assert_eq!(annotations.len(), 1);
        let annotation = updated_doc.resolve(&annotations[0]);
        assert_eq!(
            annotation.as_dict().and_then(|dict| dict.name(b"Subtype")),
            Some(b"FreeText".as_slice())
        );
    }

    #[test]
    fn outline_without_bookmarks_is_an_empty_serialized_list() {
        let Some(bytes) = one_page_pdf() else {
            assert!(false, "could not build test PDF");
            return;
        };
        let outline = extract_document_outline(&bytes).expect("outline parsing should succeed");
        assert_eq!(outline, 0u32.to_le_bytes());
    }

    #[test]
    fn outline_serializes_title_and_page_number() {
        let Some(bytes) = one_page_pdf() else {
            assert!(false, "could not build test PDF");
            return;
        };
        let mut document = LopdfDocument::load_mem(&bytes).expect("test PDF should parse");
        let page = *document
            .get_pages()
            .get(&1)
            .expect("test PDF should have one page");
        document.add_bookmark(
            lopdf::Bookmark::new("Chapter One".into(), [0.0, 0.0, 0.0], 0, page),
            None,
        );
        let outline_ref = document
            .build_outline()
            .expect("outline object should be built");
        document
            .catalog_mut()
            .expect("catalog should exist")
            .set("Outlines", outline_ref);
        let mut outlined_pdf = Vec::new();
        document
            .save_to(&mut outlined_pdf)
            .expect("outlined PDF should save");

        let serialized = extract_document_outline(&outlined_pdf).expect("outline should parse");
        let mut cursor = std::io::Cursor::new(serialized);
        let mut count = [0; 4];
        std::io::Read::read_exact(&mut cursor, &mut count).unwrap();
        assert_eq!(u32::from_le_bytes(count), 1);
        let mut entry = [0; 12];
        std::io::Read::read_exact(&mut cursor, &mut entry).unwrap();
        assert_eq!(u32::from_le_bytes(entry[0..4].try_into().unwrap()), 0);
        assert_eq!(u32::from_le_bytes(entry[4..8].try_into().unwrap()), 1);
        assert_eq!(u32::from_le_bytes(entry[8..12].try_into().unwrap()), 11);
        let mut title = vec![0; 11];
        std::io::Read::read_exact(&mut cursor, &mut title).unwrap();
        assert_eq!(title, b"Chapter One");
    }
}
