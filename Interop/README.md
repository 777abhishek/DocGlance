# Native interop

The C# declarations in this directory mirror the Rust C ABI. Rust export names,
calling conventions, struct layouts, and ownership remain unchanged.

| Binding | Native library | Export prefix | Native allocations and matching release |
|---|---|---|---|
| `PdfCraft/PdfCraftNative.cs` | `pdfcraft_ffi` | `pc_` | Documents: `pc_free_document`; images: `pc_free_image`; PNG, PDF, and annotation buffers: `pc_free_png_buffer` or `pc_free_pdf_buffer`, matching the returned buffer type |
| `WordCraft/WordCraftNative.cs` | `wordcraft_ffi` | `wc_` | Documents: `wc_free_document`; images: `wc_free_image`; PDF buffers: `wc_free_pdf_buffer` |
| `GridCraft/GridCraftNative.cs` | `gridcraft_ffi` | `gc_` | Documents: `gc_free_document`; PDF buffers: `gc_free_pdf_buffer`; JSON buffers: `gc_free_json_buffer` |
| `DeckCraft/DeckCraftNative.cs` | `deckcraft_ffi` | `dc_` | Documents: `dc_free_document`; images: `dc_free_image` |
| `Translation/TranslationNative.cs` | `translation_ffi` | `qv_` | Returned output and error buffers: `qv_free_buffer` |

Rust allocates returned buffers, images, and document handles. Managed callers
copy returned data as needed and release each native allocation exactly once
with its matching `*_free_*` export. Input buffers are supplied by managed
callers. Opaque document handles are represented by `IntPtr` or the existing
format-specific handle structs; sequential ABI structs retain their matching
C# `StructLayout`.

Document-handle calls and disposal remain serialized by the owning engine's
`NativeCallGate`. Translation is the sole service-level exception: only
`Services/Translation` consumes `Interop/Translation`.
