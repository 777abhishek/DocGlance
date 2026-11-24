using System;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Windows.Storage;
using Windows.Storage.AccessCache;
using DocGlance.Infrastructure.Logging;

namespace DocGlance.Services.Storage;

public static class StorageAccessStore
{
    private static readonly ConcurrentDictionary<string, StorageFile> ActiveFiles =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, StorageFolder> ActiveFolders =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool HasRememberedFile(string filePath) =>
        ActiveFiles.ContainsKey(filePath) ||
        IsUnderActiveFolder(filePath) ||
        IsInFutureAccessList(CreateToken("file", filePath));

    public static void RememberFile(StorageFile file)
    {
        ActiveFiles[file.Path] = file;
        TryRemember(CreateToken("file", file.Path), file);
    }

    public static void RememberFolder(StorageFolder folder)
    {
        ActiveFolders[folder.Path] = folder;
        TryRemember(CreateToken("folder", folder.Path), folder);
    }

    public static async System.Threading.Tasks.Task<StorageFile> GetFileAsync(string filePath)
    {
        if (ActiveFiles.TryGetValue(filePath, out StorageFile? activeFile))
            return activeFile;

        StorageFolder? activeRoot = FindActiveFolderForPath(filePath);
        if (activeRoot != null)
            return await GetFileFromFolderAsync(activeRoot, filePath);

        string token = CreateToken("file", filePath);
        var accessList = StorageApplicationPermissions.FutureAccessList;
        if (accessList.ContainsItem(token))
            return await accessList.GetFileAsync(token);

        StorageFile file = await StorageFile.GetFileFromPathAsync(filePath);
        RememberFile(file);
        return file;
    }

    public static async System.Threading.Tasks.Task<StorageFolder> GetFolderAsync(string folderPath)
    {
        if (ActiveFolders.TryGetValue(folderPath, out StorageFolder? activeFolder))
            return activeFolder;

        string token = CreateToken("folder", folderPath);
        var accessList = StorageApplicationPermissions.FutureAccessList;
        if (!accessList.ContainsItem(token))
            throw new UnauthorizedAccessException(
                $"Folder access is no longer available for '{folderPath}'. Remove and add the folder again.");

        StorageFolder folder = await accessList.GetFolderAsync(token);
        ActiveFolders[folder.Path] = folder;
        return folder;
    }

    public static void ForgetFolder(string folderPath)
    {
        string token = CreateToken("folder", folderPath);
        ActiveFolders.TryRemove(folderPath, out _);
        try
        {
            var accessList = StorageApplicationPermissions.FutureAccessList;
            if (accessList.ContainsItem(token))
                accessList.Remove(token);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[StorageAccess] Could not remove saved folder access: {ex.Message}");
        }
    }

    private static void TryRemember(string token, IStorageItem item)
    {
        try
        {
            StorageApplicationPermissions.FutureAccessList.AddOrReplace(token, item);
        }
        catch (Exception ex)
        {
            Logger.Warn(
                $"[StorageAccess] Could not persist access to '{item.Path}'; " +
                $"the picker grant remains available until the app closes. " +
                $"HRESULT=0x{ex.HResult:X8}: {ex.Message}");
        }
    }

    private static bool IsInFutureAccessList(string token)
    {
        try
        {
            return StorageApplicationPermissions.FutureAccessList.ContainsItem(token);
        }
        catch (Exception ex)
        {
            Logger.Warn($"[StorageAccess] Could not check saved access: {ex.Message}");
            return false;
        }
    }

    private static bool IsUnderActiveFolder(string filePath) =>
        FindActiveFolderForPath(filePath) != null;

    private static StorageFolder? FindActiveFolderForPath(string filePath)
    {
        return ActiveFolders
            .Where(entry => IsPathWithinRoot(filePath, entry.Key))
            .OrderByDescending(entry => entry.Key.Length)
            .Select(entry => entry.Value)
            .FirstOrDefault();
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        try
        {
            string relativePath = Path.GetRelativePath(root, path);
            return !Path.IsPathRooted(relativePath) &&
                   relativePath != "." &&
                   relativePath != ".." &&
                   !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static async System.Threading.Tasks.Task<StorageFile> GetFileFromFolderAsync(
        StorageFolder root, string filePath)
    {
        string relativePath = Path.GetRelativePath(root.Path, filePath);
        string[] segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new FileNotFoundException($"The path does not identify a file: '{filePath}'.");

        StorageFolder folder = root;
        for (int i = 0; i < segments.Length - 1; i++)
            folder = await folder.GetFolderAsync(segments[i]);

        StorageFile file = await folder.GetFileAsync(segments[^1]);
        ActiveFiles[file.Path] = file;
        return file;
    }

    private static string CreateToken(string kind, string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant()));
        return $"dg-{kind}-{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }
}
