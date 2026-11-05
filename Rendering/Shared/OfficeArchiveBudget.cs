using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using DocGlance.Infrastructure.Diagnostics;

namespace DocGlance.Rendering.Shared;

internal static class OfficeArchiveBudget
{
    internal const int MaximumEntries = 10_000;
    internal const long MaximumEntryBytes = 128L * 1024 * 1024;
    internal const long MaximumTotalExpandedBytes = 512L * 1024 * 1024;

    internal static void Validate(string filePath, CancellationToken cancellationToken = default)
    {
        DocumentOpenTrace.Current?.Mark("OfficeArchiveValidationStarted");
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(filePath);
            Validate(archive, MaximumEntries, MaximumEntryBytes, MaximumTotalExpandedBytes, cancellationToken);
            DocumentOpenTrace.Current?.Mark("OfficeArchiveValidationCompleted",
                $"Entries={archive.Entries.Count}");
        }
        catch (OperationCanceledException)
        {
            DocumentOpenTrace.Current?.Mark("OfficeArchiveValidationCanceled");
            throw;
        }
        catch
        {
            DocumentOpenTrace.Current?.Mark("OfficeArchiveValidationFailed");
            throw;
        }
    }

    internal static void Validate(
        ZipArchive archive,
        int maximumEntries,
        long maximumEntryBytes,
        long maximumTotalExpandedBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Entries.Count > maximumEntries)
            throw new InvalidDataException($"Office archive contains more than {maximumEntries:N0} entries.");

        long totalExpandedBytes = 0;
        byte[] buffer = new byte[64 * 1024];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.Length > maximumEntryBytes)
                throw new InvalidDataException(
                    $"Office archive entry '{entry.FullName}' exceeds the {maximumEntryBytes:N0}-byte limit.");

            long entryExpandedBytes = 0;
            using Stream input = entry.Open();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = input.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;

                if (read > maximumEntryBytes - entryExpandedBytes)
                    throw new InvalidDataException(
                        $"Office archive entry '{entry.FullName}' exceeds the {maximumEntryBytes:N0}-byte limit.");
                if (read > maximumTotalExpandedBytes - totalExpandedBytes)
                    throw new InvalidDataException(
                        $"Office archive expanded size exceeds the {maximumTotalExpandedBytes:N0}-byte limit.");

                entryExpandedBytes += read;
                totalExpandedBytes += read;
            }

            if (entryExpandedBytes != entry.Length)
                throw new InvalidDataException(
                    $"Office archive entry '{entry.FullName}' has an invalid expanded size.");
        }
    }
}
