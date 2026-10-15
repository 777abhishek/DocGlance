# Offline translation

QuickView translation runs on your device. Your selected text is passed to a
local translation engine; QuickView does not upload document contents or
selected text.

## First use

1. Open **Manage translation models** from the translation dialog.
2. Select **Refresh language catalog** to explicitly check which supported models
   are available.
3. Download the source/target model pair(s) you want. Downloads are optional
   and are kept outside the application installation.
4. Once installed, translation works without a network connection. Model
   downloads and catalog refresh are the only translation-related network
   operations.

The language picker is populated from the latest refreshed Firefox Translations
catalog instead of a hard-coded list. It offers languages only when the
provider has complete, version-matched model files for them. Models are
directional; translating both ways may require two model pairs. Refreshing the
catalog is an explicit network action. **Manage translation models** includes
search by language name, language code, or model filename. Language tags with
multiple subtags are not currently shown.

## Model storage and removal

Models are stored in QuickView's LocalApplicationData model directory, separate
from the application binaries. The model manager shows download size and actual
installed disk use, supports cancellation/retry, and can remove installed
models. Incomplete downloads are not treated as installed.

Firefox Translations model files have terms separate from the Rust engine.
QuickView does not bundle or redistribute model weights. Review the upstream
model information and applicable terms before downloading or using a model.
Mozilla's model CDN is not guaranteed to be a stable third-party hosting
service.

## Supported selection surfaces

Text selection can be translated from PDF, Word, PowerPoint, spreadsheets, and
plain-text viewers. Code viewers and Markdown do not yet expose a selection
bridge to the translator. Scanned/image-only PDF pages without an extractable
text layer cannot be translated. Binary/visual-only content is not sent through
OCR by this feature.

## Limits

Translation is CPU-based and model quality varies by language pair. Very large
selections are rejected with an actionable message rather than silently
truncated. Model loading and inference run off the UI thread; the first
translation after selecting a new pair may take longer while its model is
loaded.
