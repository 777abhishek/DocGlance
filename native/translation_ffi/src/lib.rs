use std::panic::{catch_unwind, AssertUnwindSafe};
use std::path::{Path, PathBuf};
use std::slice;
use std::sync::{Mutex, OnceLock};
use std::{io::Read, io::Write};

use fxtranslate::cache::{Cache, ModelFiles};
use fxtranslate::engine::{Engine, Translation};
use fxtranslate::shortlist::Shortlist;
use sha2::{Digest, Sha256};

const MAX_INPUT_BYTES: usize = 200_000;
const MAX_COMPRESSED_MODEL_BYTES: usize = 64 * 1024 * 1024;
const MAX_DECOMPRESSED_MODEL_BYTES: usize = 128 * 1024 * 1024;

struct CachedTranslation {
    cache_root: PathBuf,
    source: String,
    target: String,
    translation: Translation,
}

static CACHED_TRANSLATION: OnceLock<Mutex<Option<CachedTranslation>>> = OnceLock::new();

fn cached_translation() -> &'static Mutex<Option<CachedTranslation>> {
    CACHED_TRANSLATION.get_or_init(|| Mutex::new(None))
}

fn borrowed_utf8<'a>(ptr: *const u8, len: usize, name: &str) -> Result<&'a str, String> {
    if ptr.is_null() && len != 0 {
        return Err(format!("{name} pointer is null."));
    }
    if len == 0 {
        return Ok("");
    }

    let bytes = unsafe { slice::from_raw_parts(ptr, len) };
    std::str::from_utf8(bytes).map_err(|_| format!("{name} is not valid UTF-8."))
}

fn allocate_output(
    bytes: Vec<u8>,
    output: *mut *mut u8,
    output_len: *mut usize,
) -> Result<(), String> {
    if output.is_null() || output_len.is_null() {
        return Err("Output buffer arguments are null.".into());
    }

    let owned = bytes.into_boxed_slice();
    let len = owned.len();
    let ptr = Box::into_raw(owned) as *mut u8;
    unsafe {
        *output = ptr;
        *output_len = len;
    }
    Ok(())
}

fn write_error(message: &str, output: *mut *mut u8, output_len: *mut usize) {
    if output.is_null() || output_len.is_null() {
        return;
    }
    let _ = allocate_output(message.as_bytes().to_vec(), output, output_len);
}

fn canonical_language(code: &str) -> Result<String, String> {
    let normalized = code.trim().to_ascii_lowercase();
    if normalized.len() != 2 || !normalized.bytes().all(|byte| byte.is_ascii_lowercase()) {
        return Err(format!("Language code '{code}' is invalid."));
    }
    Ok(normalized)
}

fn detect_language(text: &str) -> Result<String, String> {
    if text.trim().is_empty() {
        return Err("Select some text before translating.".into());
    }

    let info = whatlang::detect(text)
        .filter(|info| info.is_reliable() && info.confidence() >= 0.6)
        .ok_or_else(|| {
            "The source language could not be detected confidently. Choose it manually.".to_string()
        })?;

    let code = match info.lang().code() {
        "afr" => "af",
        "ara" => "ar",
        "aze" => "az",
        "bel" => "be",
        "bul" => "bg",
        "bos" => "bs",
        "cat" => "ca",
        "ces" => "cs",
        "deu" => "de",
        "eng" => "en",
        "spa" => "es",
        "est" => "et",
        "eus" => "eu",
        "fin" => "fi",
        "fra" => "fr",
        "glg" => "gl",
        "hrv" => "hr",
        "hun" => "hu",
        "isl" => "is",
        "ita" => "it",
        "jpn" => "ja",
        "kor" => "ko",
        "lit" => "lt",
        "lav" => "lv",
        "mar" => "mr",
        "nld" => "nl",
        "pol" => "pl",
        "por" => "pt",
        "rus" => "ru",
        "slk" => "sk",
        "slv" => "sl",
        "srp" => "sr",
        "tam" => "ta",
        "tha" => "th",
        "tgl" => "tl",
        "uig" => "ug",
        "ukr" => "uk",
        "urd" => "ur",
        "vie" => "vi",
        detected => {
            return Err(format!(
                "Detected language '{detected}' does not have a supported model code. Choose the source language manually."
            ));
        }
    };
    canonical_language(code)
}

fn load_engine(files: ModelFiles) -> Result<Engine, String> {
    let mut engine = Engine::load_mmapped(&files.model, &files.src_vocab, &files.trg_vocab)?;
    if let Some(lex_path) = files.lex {
        let shortlist = Shortlist::load(lex_path)
            .map_err(|error| format!("Could not load the model's lexical shortlist: {error}"))?;
        engine = engine.with_shortlist(shortlist);
    }
    Ok(engine)
}

fn verify_and_decompress_bounded(
    compressed: &[u8],
    expected_hash: &str,
) -> Result<Vec<u8>, String> {
    if compressed.is_empty() || compressed.len() > MAX_COMPRESSED_MODEL_BYTES {
        return Err(format!(
            "Compressed model file must be between 1 byte and {} MiB.",
            MAX_COMPRESSED_MODEL_BYTES / (1024 * 1024)
        ));
    }

    let mut decoder = ruzstd::StreamingDecoder::new(compressed)
        .map_err(|error| format!("Could not open compressed model file: {error}"))?;
    let mut bytes = Vec::new();
    let mut chunk = [0_u8; 64 * 1024];
    loop {
        let read = decoder
            .read(&mut chunk)
            .map_err(|error| format!("Could not decompress model file: {error}"))?;
        if read == 0 {
            break;
        }
        if bytes.len().saturating_add(read) > MAX_DECOMPRESSED_MODEL_BYTES {
            return Err(format!(
                "Decompressed model file exceeds the {} MiB safety limit.",
                MAX_DECOMPRESSED_MODEL_BYTES / (1024 * 1024)
            ));
        }
        bytes
            .write_all(&chunk[..read])
            .map_err(|error| error.to_string())?;
    }

    let digest = Sha256::digest(&bytes);
    let actual_hash = digest
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect::<String>();
    if !actual_hash.eq_ignore_ascii_case(expected_hash) {
        return Err(format!(
            "Model integrity check failed: SHA-256 was {actual_hash}, expected {expected_hash}."
        ));
    }
    Ok(bytes)
}

fn load_translation(cache_root: &Path, source: &str, target: &str) -> Result<Translation, String> {
    let cache = Cache::with_root(cache_root);
    if let Some(files) = cache.cached_model(source, target) {
        return Ok(Translation::Direct(load_engine(files)?));
    }

    if source != "en" && target != "en" {
        if let (Some(first), Some(second)) = (
            cache.cached_model(source, "en"),
            cache.cached_model("en", target),
        ) {
            return Ok(Translation::Pivot {
                pivot: "en".to_string(),
                first: load_engine(first)?,
                second: load_engine(second)?,
            });
        }
    }

    Err(format!(
        "No installed model route for {source} → {target}. Open Manage translation models to install the required pair."
    ))
}

#[no_mangle]
pub extern "C" fn qv_detect_language(
    text_ptr: *const u8,
    text_len: usize,
    output: *mut *mut u8,
    output_len: *mut usize,
    error: *mut *mut u8,
    error_len: *mut usize,
) -> i32 {
    unsafe {
        if !output.is_null() {
            *output = std::ptr::null_mut();
        }
        if !output_len.is_null() {
            *output_len = 0;
        }
        if !error.is_null() {
            *error = std::ptr::null_mut();
        }
        if !error_len.is_null() {
            *error_len = 0;
        }
    }

    let result: Result<String, String> = catch_unwind(AssertUnwindSafe(|| {
        let text = borrowed_utf8(text_ptr, text_len, "Selected text")?;
        if text.len() > MAX_INPUT_BYTES {
            return Err(format!(
                "Selection exceeds the native limit of {MAX_INPUT_BYTES} UTF-8 bytes."
            ));
        }
        detect_language(text)
    }))
    .unwrap_or_else(|_| Err("The native language detector encountered an internal error.".into()));

    match result {
        Ok(code) => match allocate_output(code.as_bytes().to_vec(), output, output_len) {
            Ok(()) => 0,
            Err(message) => {
                write_error(&message, error, error_len);
                1
            }
        },
        Err(message) => {
            write_error(&message, error, error_len);
            1
        }
    }
}

#[no_mangle]
pub extern "C" fn qv_translate(
    text_ptr: *const u8,
    text_len: usize,
    source_ptr: *const u8,
    source_len: usize,
    target_ptr: *const u8,
    target_len: usize,
    cache_root_ptr: *const u8,
    cache_root_len: usize,
    output: *mut *mut u8,
    output_len: *mut usize,
    error: *mut *mut u8,
    error_len: *mut usize,
) -> i32 {
    unsafe {
        if !output.is_null() {
            *output = std::ptr::null_mut();
        }
        if !output_len.is_null() {
            *output_len = 0;
        }
        if !error.is_null() {
            *error = std::ptr::null_mut();
        }
        if !error_len.is_null() {
            *error_len = 0;
        }
    }

    let result: Result<String, String> = catch_unwind(AssertUnwindSafe(|| {
        let text = borrowed_utf8(text_ptr, text_len, "Selected text")?;
        if text.trim().is_empty() {
            return Err("Select some text before translating.".to_string());
        }
        if text.len() > MAX_INPUT_BYTES {
            return Err(format!(
                "Selection exceeds the native limit of {MAX_INPUT_BYTES} UTF-8 bytes."
            ));
        }

        let source = canonical_language(borrowed_utf8(source_ptr, source_len, "Source language")?)?;
        let target = canonical_language(borrowed_utf8(target_ptr, target_len, "Target language")?)?;
        let cache_root = PathBuf::from(borrowed_utf8(
            cache_root_ptr,
            cache_root_len,
            "Model storage path",
        )?);
        if cache_root.as_os_str().is_empty() {
            return Err("Model storage path is empty.".into());
        }
        if source == target {
            return Ok(text.to_string());
        }

        let mut cached = cached_translation()
            .lock()
            .map_err(|_| "The native translation cache is unavailable.".to_string())?;
        let needs_load = !cached.as_ref().is_some_and(|entry| {
            entry.cache_root == cache_root && entry.source == source && entry.target == target
        });
        if needs_load {
            *cached = Some(CachedTranslation {
                cache_root: cache_root.clone(),
                source: source.clone(),
                target: target.clone(),
                translation: load_translation(&cache_root, &source, &target)?,
            });
        }

        let translated = cached
            .as_ref()
            .ok_or_else(|| "The translation engine was not initialized.".to_string())?
            .translation
            .translate_long(text);
        if translated.trim().is_empty() {
            return Err("The local model returned no translated text.".into());
        }
        Ok(translated)
    }))
    .unwrap_or_else(|_| Err("The native translation engine encountered an internal error.".into()));

    match result {
        Ok(text) => match allocate_output(text.into_bytes(), output, output_len) {
            Ok(()) => 0,
            Err(message) => {
                write_error(&message, error, error_len);
                1
            }
        },
        Err(message) => {
            write_error(&message, error, error_len);
            1
        }
    }
}

#[no_mangle]
pub extern "C" fn qv_verify_decompress(
    compressed_ptr: *const u8,
    compressed_len: usize,
    expected_hash_ptr: *const u8,
    expected_hash_len: usize,
    output: *mut *mut u8,
    output_len: *mut usize,
    error: *mut *mut u8,
    error_len: *mut usize,
) -> i32 {
    unsafe {
        if !output.is_null() {
            *output = std::ptr::null_mut();
        }
        if !output_len.is_null() {
            *output_len = 0;
        }
        if !error.is_null() {
            *error = std::ptr::null_mut();
        }
        if !error_len.is_null() {
            *error_len = 0;
        }
    }

    let result = catch_unwind(AssertUnwindSafe(|| {
        let compressed = if compressed_len == 0 {
            &[][..]
        } else {
            if compressed_ptr.is_null() {
                return Err("Compressed model pointer is null.".into());
            }
            unsafe { slice::from_raw_parts(compressed_ptr, compressed_len) }
        };
        let expected_hash =
            borrowed_utf8(expected_hash_ptr, expected_hash_len, "Expected SHA-256")?;
        if expected_hash.len() != 64 || !expected_hash.bytes().all(|byte| byte.is_ascii_hexdigit())
        {
            return Err("The model catalog contains an invalid SHA-256 hash.".to_string());
        }
        verify_and_decompress_bounded(compressed, expected_hash)
    }))
    .unwrap_or_else(|_| Err("The native model verifier encountered an internal error.".into()));

    match result {
        Ok(bytes) => match allocate_output(bytes, output, output_len) {
            Ok(()) => 0,
            Err(message) => {
                write_error(&message, error, error_len);
                1
            }
        },
        Err(message) => {
            write_error(&message, error, error_len);
            1
        }
    }
}

#[no_mangle]
pub extern "C" fn qv_release_cached_model(
    cache_root_ptr: *const u8,
    cache_root_len: usize,
    pair_ptr: *const u8,
    pair_len: usize,
    error: *mut *mut u8,
    error_len: *mut usize,
) -> i32 {
    unsafe {
        if !error.is_null() {
            *error = std::ptr::null_mut();
        }
        if !error_len.is_null() {
            *error_len = 0;
        }
    }
    let result: Result<(), String> = catch_unwind(AssertUnwindSafe(|| {
        let cache_root = PathBuf::from(borrowed_utf8(
            cache_root_ptr,
            cache_root_len,
            "Model storage path",
        )?);
        let pair = borrowed_utf8(pair_ptr, pair_len, "Model pair")?;
        let mut parts = pair.split('-');
        let (Some(source), Some(target), None) = (parts.next(), parts.next(), parts.next()) else {
            return Err("Invalid model pair.".to_string());
        };
        let source = canonical_language(source)?;
        let target = canonical_language(target)?;
        let pair_key = format!("{source}-{target}");
        let mut cached = cached_translation()
            .lock()
            .map_err(|_| "The native translation cache is unavailable.".to_string())?;
        let should_release = cached.as_ref().is_some_and(|entry| {
            if entry.cache_root != cache_root {
                return false;
            }
            let direct = format!("{}-{}", entry.source, entry.target);
            let pivot_first = format!("{}-en", entry.source);
            let pivot_second = format!("en-{}", entry.target);
            pair_key == direct || pair_key == pivot_first || pair_key == pivot_second
        });
        if should_release {
            *cached = None;
        }
        Ok(())
    }))
    .unwrap_or_else(|_| Err("The native model manager encountered an internal error.".into()));

    match result {
        Ok(()) => 0,
        Err(message) => {
            write_error(&message, error, error_len);
            1
        }
    }
}

#[no_mangle]
pub extern "C" fn qv_free_buffer(buffer: *mut u8, len: usize) {
    if buffer.is_null() {
        return;
    }
    let slice = std::ptr::slice_from_raw_parts_mut(buffer, len);
    unsafe {
        drop(Box::from_raw(slice));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn detects_supported_languages() {
        assert_eq!(
            detect_language("Today the weather is pleasant and the sun is shining over the city. People are walking through the park, speaking with friends, and enjoying the warm afternoon. Later, dark clouds may arrive, so we will take umbrellas before heading home.")
                .unwrap(),
            "en"
        );
        assert_eq!(
            detect_language("Hoy el tiempo es agradable y el sol brilla sobre la ciudad. La gente camina por el parque, conversa con sus amigos y disfruta de una tarde cálida. Más tarde podrían llegar nubes oscuras, así que llevaremos paraguas antes de volver a casa.")
                .unwrap(),
            "es"
        );
        assert_eq!(
            detect_language("Aujourd'hui, le temps est agréable et le soleil brille sur la ville. Les habitants se promènent dans le parc, parlent avec leurs amis et profitent d'un après-midi doux. Plus tard, des nuages sombres pourraient arriver, alors nous prendrons des parapluies avant de rentrer chez nous.")
                .unwrap(),
            "fr"
        );
        assert_eq!(
            detect_language("Heute ist das Wetter angenehm und die Sonne scheint über der Stadt. Die Menschen spazieren durch den Park, sprechen mit ihren Freunden und genießen den warmen Nachmittag. Später könnten dunkle Wolken aufziehen, deshalb nehmen wir Regenschirme mit, bevor wir nach Hause gehen.")
                .unwrap(),
            "de"
        );
        assert_eq!(
            detect_language("Сегодня погода ясная, и солнце светит над городом. Люди гуляют по парку, разговаривают с друзьями и наслаждаются теплым днем. Позже могут появиться тучи, поэтому перед возвращением домой мы возьмем зонты.")
                .unwrap(),
            "ru"
        );
    }

    #[test]
    fn rejects_short_or_ambiguous_text_for_auto_detection() {
        assert!(detect_language("OK").is_err());
        assert!(detect_language("   ").is_err());
    }

    #[test]
    fn accepts_catalog_language_codes_and_rejects_invalid_codes() {
        assert!(canonical_language("be").is_ok());
        assert!(canonical_language("EN").is_ok());
        assert!(canonical_language("../").is_err());
    }

    #[test]
    fn rejects_corrupted_model_data() {
        let bad_bytes = [0_u8; 8];
        let error = verify_and_decompress_bounded(&bad_bytes, &"0".repeat(64)).unwrap_err();
        assert!(error.starts_with("Could not open compressed model file:"));
    }

    #[test]
    fn ffi_verifier_returns_owned_error_buffer_for_corrupt_data() {
        let compressed = [0_u8; 8];
        let expected_hash = [b'0'; 64];
        let mut output = std::ptr::null_mut();
        let mut output_len = 0;
        let mut error = std::ptr::null_mut();
        let mut error_len = 0;

        let status = qv_verify_decompress(
            compressed.as_ptr(),
            compressed.len(),
            expected_hash.as_ptr(),
            expected_hash.len(),
            &mut output,
            &mut output_len,
            &mut error,
            &mut error_len,
        );

        assert_eq!(status, 1);
        assert!(output.is_null());
        assert_eq!(output_len, 0);
        assert!(!error.is_null());
        let message = unsafe { slice::from_raw_parts(error, error_len) };
        assert!(std::str::from_utf8(message)
            .unwrap()
            .starts_with("Could not open compressed model file:"));
        qv_free_buffer(error, error_len);
    }

    #[test]
    fn reports_missing_model_without_network_access() {
        let root =
            std::env::temp_dir().join(format!("DocGlance-translation-test-{}", std::process::id()));
        let error = load_translation(&root, "en", "es").err().unwrap();
        assert!(error.contains("No installed model route"));
        let _ = std::fs::remove_dir_all(root);
    }
}
