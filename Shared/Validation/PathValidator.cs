using System;
using System.IO;
using System.Runtime.InteropServices;

namespace QuickView.Shared.Validation;

/// <summary>
/// Provides security validation for file paths to prevent symlink, junction, and UNC path attacks.
/// </summary>
public static class PathValidator
{
    private const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFileAttributes(string lpFileName);

    /// <summary>
    /// Validates that a file path is safe to open.
    /// Checks for: symlinks, junctions, UNC paths, invalid characters, and path traversal.
    /// </summary>
    /// <param name="path">The file path to validate</param>
    /// <param name="error">Description of validation failure if path is invalid</param>
    /// <returns>True if path is safe to use, false otherwise</returns>
    public static bool IsValidFilePath(string? path, out string error)
    {
        error = string.Empty;

        // Check for null or empty
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is null or empty";
            return false;
        }

        try
        {
            // Get the full absolute path
            var fullPath = Path.GetFullPath(path);

            // Reject UNC paths (\\server\share)
            if (fullPath.StartsWith(@"\\", StringComparison.Ordinal))
            {
                error = "UNC paths are not supported for security reasons";
                return false;
            }

            // Reject paths with invalid characters
            if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = "Path contains invalid characters";
                return false;
            }

            // Check if file exists
            if (!File.Exists(fullPath))
            {
                error = "File does not exist";
                return false;
            }

            // Check for reparse points (symlinks, junctions, mount points)
            if (IsReparsePoint(fullPath))
            {
                error = "Symbolic links and junctions are not supported for security reasons";
                return false;
            }

            // Check parent directories for reparse points
            var directory = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(directory) && IsReparsePoint(directory))
                {
                    error = "Parent directory contains symbolic links or junctions";
                    return false;
                }

                // Stop at drive root
                var parent = Path.GetDirectoryName(directory);
                if (parent == directory)
                    break;

                directory = parent;
            }

            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"Invalid path format: {ex.Message}";
            return false;
        }
        catch (PathTooLongException)
        {
            error = "Path is too long";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "Path format is not supported";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Path validation failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Checks if a path is a reparse point (symlink, junction, mount point).
    /// Returns TRUE (fail-safe) on access-denied or other errors to block
    /// potentially suspicious paths we can't verify.
    /// </summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = GetFileAttributes(path);
            
            // INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF (means GetFileAttributes failed)
            if (attributes == 0xFFFFFFFF)
            {
                // Could be access-denied, non-existent path, or a protected symlink.
                // Fail-safe: treat as suspicious and block it.
                return true;
            }

            return (attributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;
        }
        catch
        {
            // Any exception (access-denied, security error, etc.) should block the path.
            return true;
        }
    }

    /// <summary>
    /// Validates and returns the full path if valid, or null if invalid.
    /// </summary>
    public static string? GetValidatedPath(string? path)
    {
        return IsValidFilePath(path, out _) ? Path.GetFullPath(path!) : null;
    }
}
