using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Services.Translation;

public sealed record TranslationResult(
    string OriginalText,
    string TranslatedText,
    string SourceLanguage,
    string TargetLanguage,
    bool WasSourceDetected);

public interface ITranslationService
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default);
}
