using System;
using System.IO;
using QuickView.Infrastructure.Configuration;

namespace QuickView.Shared.Validation;

/// <summary>
/// Validates file sizes to prevent DoS attacks from attempting to load extremely large files.
/// </summary>
public static class FileSizeValidator
{
    /// <summary>
    /// Checks if a file is within acceptable size limits based on its type.
    /// </summary>
    /// <param name="filePath">Path to the file</param>
    /// <param name="error">Description of the error if validation fails</param>
    /// <returns>True if file size is acceptable, false otherwise</returns>
    public static bool IsFileSizeAcceptable(string filePath, out string error)
    {
        error = string.Empty;

        try
        {
            if (!File.Exists(filePath))
            {
                error = "File does not exist";
                return false;
            }

            var fileInfo = new FileInfo(filePath);
            var sizeBytes = fileInfo.Length;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            long maxSize = GetMaxSizeForExtension(extension);
            
            if (sizeBytes > maxSize)
            {
                error = $"File size ({FormatBytes(sizeBytes)}) exceeds maximum allowed size ({FormatBytes(maxSize)}) for {extension} files";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to check file size: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Gets the maximum allowed file size for a given extension.
    /// </summary>
    private static long GetMaxSizeForExtension(string extension)
    {
        return extension switch
        {
            // PDF files
            ".pdf" => AppSettings.MaxPdfFileSizeBytes,

            // Text files
            ".txt" or ".log" or ".md" or ".json" or ".xml" or ".csv"
                => AppSettings.MaxTextFileSizeBytes,

            // Code files
            ".cs" or ".js" or ".ts" or ".py" or ".java" or ".cpp" or ".h" or ".css" or ".html"
                => AppSettings.MaxTextFileSizeBytes,

            // Office files
            ".docx" or ".xlsx" or ".pptx" or ".doc" or ".xls" or ".ppt"
                => AppSettings.MaxOfficeFileSizeBytes,

            // Rich text
            ".rtf" => AppSettings.MaxTextFileSizeBytes,

            // Default for unknown types
            _ => AppSettings.MaxGeneralFileSizeBytes
        };
    }

    /// <summary>
    /// Formats bytes into human-readable format (KB, MB, GB).
    /// </summary>
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Validates both path security and file size in one call.
    /// </summary>
    public static bool IsFileValidAndSafe(string filePath, out string error)
    {
        // First validate the path for security
        if (!PathValidator.IsValidFilePath(filePath, out error))
            return false;

        // Then validate the file size
        if (!IsFileSizeAcceptable(filePath, out error))
            return false;

        return true;
    }
}
