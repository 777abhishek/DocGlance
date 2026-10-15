using DocGlance.Infrastructure.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DocGlance.Services.Translation;

public sealed class TranslationService : ITranslationService
{
    public static TranslationService Instance { get; } = new();

    private readonly SemaphoreSlim _translationGate = new(1, 1);

    private TranslationService()
    {
    }

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > TranslationTextChunker.MaxSelectionCharacters)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"The selection is {text.Length:N0} characters. Translate up to {TranslationTextChunker.MaxSelectionCharacters:N0} characters at a time.");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Select some text before translating.", nameof(text));

        string normalizedTarget = LanguageCatalog.Find(targetLanguage)?.Code ??
            throw new ArgumentException("Choose a supported target language.", nameof(targetLanguage));
        bool detectSource = string.Equals(sourceLanguage, "auto", StringComparison.OrdinalIgnoreCase);
        string normalizedSource;
        if (detectSource)
        {
            normalizedSource = await Task.Run(
                () => TranslationEngineNative.DetectLanguage(text),
                cancellationToken);
        }
        else
        {
            normalizedSource = LanguageCatalog.Find(sourceLanguage)?.Code ??
                throw new ArgumentException("Choose Auto or a supported source language.", nameof(sourceLanguage));
        }

        if (!LanguageCatalog.IsSupportedPair(normalizedSource, normalizedTarget) &&
            !string.Equals(normalizedSource, normalizedTarget, StringComparison.Ordinal))
            throw new ArgumentException("The detected or selected source language is not supported.");

        cancellationToken.ThrowIfCancellationRequested();
        if (normalizedSource == normalizedTarget)
            return new TranslationResult(text, text, normalizedSource, normalizedTarget, detectSource);

        IReadOnlyList<TranslationTextChunk> chunks = TranslationTextChunker.Split(text);
        var translations = new List<string>(chunks.Count);
        await _translationGate.WaitAsync(cancellationToken);
        try
        {
            foreach (TranslationTextChunk chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string translated = await Task.Run(
                    () => TranslationEngineNative.Translate(
                        chunk.Text,
                        normalizedSource,
                        normalizedTarget,
                        TranslationModelManager.ModelRoot),
                    CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(translated))
                    throw new InvalidOperationException("The local model returned an incomplete translation.");
                translations.Add(translated);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error("[Translation] Local translation failed.", ex);
            throw;
        }
        finally
        {
            _translationGate.Release();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new TranslationResult(
            text,
            TranslationTextChunker.Reassemble(chunks, translations),
            normalizedSource,
            normalizedTarget,
            detectSource);
    }

    internal async Task ReleaseCachedPairAsync(
        string pair,
        CancellationToken cancellationToken = default)
    {
        await _translationGate.WaitAsync(cancellationToken);
        try
        {
            await Task.Run(
                () => TranslationEngineNative.ReleaseCachedPair(
                    TranslationModelManager.ModelRoot,
                    pair),
                cancellationToken);
        }
        finally
        {
            _translationGate.Release();
        }
    }
}
