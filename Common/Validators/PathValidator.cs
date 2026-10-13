using System;
using System.IO;

namespace QuickView.Common.Validators;

/// <summary>
/// Provides security validation for file paths to prevent symlink, junction, and UNC path attacks.
/// Non-link reparse points (for example OneDrive / cloud-files placeholders) are allowed.
/// </summary>
public static class PathValidator
{

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

            // Check for symlinks / junctions on the file itself
            if (IsSymlinkOrJunction(new FileInfo(fullPath)))
            {
                error = "Symbolic links and junctions are not supported for security reasons";
                return false;
            }

            // Check parent directories for symlinks / junctions
            var directory = Path.GetDirectoryName(fullPath);
            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(directory) && IsSymlinkOrJunction(new DirectoryInfo(directory)))
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
    /// True when the entry is a real symbolic link or junction. A reparse point that is not a
    /// link (OneDrive Files On-Demand placeholders, dedup, etc.) has no <c>LinkTarget</c> and is
    /// allowed. Returns TRUE (fail-safe) when the attributes cannot be read.
    /// </summary>
    private static bool IsSymlinkOrJunction(FileSystemInfo info)
    {
        try
        {
            if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                return false;

            return info.LinkTarget != null;
        }
        catch
        {
            // Access-denied or other errors: block the path we cannot verify.
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
