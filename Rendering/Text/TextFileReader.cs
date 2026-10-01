using System.IO;
using System.Text;
using QuickView.Shared.Validation;

namespace QuickView.Rendering.Text;

/// <summary>
/// Reads at most <paramref name="maxChars"/> characters. Never slurps the whole file.
/// </summary>
public static class TextFileReader
{
    public readonly record struct Result(string Text, bool Truncated, long FileBytes, int LineCount);

    public static Result ReadCapped(string filePath, int maxChars)
    {
        // Validate path for security and file size
        if (!FileSizeValidator.IsFileValidAndSafe(filePath, out _))
            return new Result(string.Empty, false, 0, 0);

        var info = new FileInfo(filePath);
        long fileBytes = info.Exists ? info.Length : 0;

        using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            bufferSize: 64 * 1024, FileOptions.SequentialScan);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        char[] buffer = new char[maxChars];
        int n = sr.ReadBlock(buffer, 0, maxChars);
        bool truncated = n == maxChars && sr.Peek() >= 0;
        string text = n == 0 ? string.Empty : new string(buffer, 0, n);

        int lineCount = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') lineCount++;

        return new Result(text, truncated, fileBytes, lineCount);
    }
}
