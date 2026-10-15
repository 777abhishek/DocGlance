using System;
using System.Collections.Generic;
using System.Text;

namespace DocGlance.Services.Translation;

public readonly record struct TranslationTextChunk(string Text, string SeparatorAfter);

public static class TranslationTextChunker
{
    public const int MaxSelectionCharacters = 50_000;
    public const int MaxChunkCharacters = 1_200;

    public static IReadOnlyList<TranslationTextChunk> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaxSelectionCharacters)
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"The selection is {text.Length:N0} characters. Translate up to {MaxSelectionCharacters:N0} characters at a time.");
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Select some text before translating.", nameof(text));

        var chunks = new List<TranslationTextChunk>();
        int offset = 0;
        while (offset < text.Length)
        {
            int maxEnd = Math.Min(text.Length, offset + MaxChunkCharacters);
            if (maxEnd < text.Length &&
                char.IsHighSurrogate(text[maxEnd - 1]) &&
                char.IsLowSurrogate(text[maxEnd]))
                maxEnd--;

            if (maxEnd == text.Length)
            {
                chunks.Add(new TranslationTextChunk(text[offset..], string.Empty));
                break;
            }

            int boundary = FindBoundary(text, offset, maxEnd);
            if (boundary <= offset)
                boundary = maxEnd;

            int next = boundary;
            while (next < text.Length && char.IsWhiteSpace(text[next]))
                next++;

            string separator = text[boundary..next];
            chunks.Add(new TranslationTextChunk(text[offset..boundary], separator));
            offset = next;
        }

        return chunks;
    }

    public static string Reassemble(
        IReadOnlyList<TranslationTextChunk> chunks,
        IReadOnlyList<string> translatedChunks)
    {
        if (chunks.Count != translatedChunks.Count)
            throw new ArgumentException("Each source chunk must have one complete translation.", nameof(translatedChunks));

        var result = new StringBuilder();
        for (int index = 0; index < chunks.Count; index++)
        {
            result.Append(translatedChunks[index]);
            result.Append(chunks[index].SeparatorAfter);
        }
        return result.ToString();
    }

    private static int FindBoundary(string text, int start, int maxEnd)
    {
        int lastWhitespace = -1;
        int lastSentenceBoundary = -1;
        for (int index = start + 1; index < maxEnd; index++)
        {
            if (!char.IsWhiteSpace(text[index]))
                continue;

            lastWhitespace = index;
            int punctuation = index - 1;
            while (punctuation >= start && IsClosingMark(text[punctuation]))
                punctuation--;
            if (punctuation >= start && text[punctuation] is '.' or '!' or '?' or '\u3002' or '\uff01' or '\uff1f')
                lastSentenceBoundary = index;
        }

        return lastSentenceBoundary > start ? lastSentenceBoundary : lastWhitespace;
    }

    private static bool IsClosingMark(char value) =>
        value is ')' or ']' or '}' or '"' or '\'' or '»' or '”' or '’';
}
