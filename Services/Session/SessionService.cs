using Microsoft.UI.Dispatching;
using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.Services.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DocGlance.Services.Session;

public static class SessionService
{
    public static void SaveSession(IEnumerable<DocumentItem> documents, int selectedIndex)
    {
        try
        {
            var filePaths = documents
                .Where(document => !string.IsNullOrEmpty(document.FilePath) &&
                                   StorageAccessStore.HasRememberedFile(document.FilePath))
                .Select(document => document.FilePath)
                .ToList();

            SessionStateStore.SaveOpenTabs(filePaths, selectedIndex);
            Logger.Log($"[Session] Saved {filePaths.Count} open tabs");
        }
        catch (Exception ex)
        {
            Logger.Error("SaveSession failed", ex);
        }
    }

    public static void RestoreSession(
        DispatcherQueue dispatcherQueue,
        Action<IReadOnlyList<DocumentItem>, int> restoreDocuments)
    {
        DocGlance.Infrastructure.Diagnostics.DiagnosticsService.Milestone("SessionRestoreStarted");

        _ = Task.Run(async () =>
        {
            try
            {
                var state = SessionStateStore.LoadSessionState();
                var documents = new List<DocumentItem>();
                int selectedDocumentIndex = -1;

                if (state != null)
                {
                    for (int i = 0; i < state.OpenFiles.Count; i++)
                    {
                        var filePath = state.OpenFiles[i];
                        try
                        {
                            var document = await CreateDocumentAsync(filePath);
                            if (i == state.SelectedTabIndex)
                                selectedDocumentIndex = documents.Count;
                            documents.Add(document);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Failed to prepare restored tab for: {filePath}", ex);
                        }
                    }
                }

                if (!dispatcherQueue.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        try
                        {
                            if (documents.Count == 0)
                            {
                                Logger.Log("[Session] No previous session to restore");
                                return;
                            }

                            Logger.Log($"[Session] Restoring {documents.Count} tabs");
                            restoreDocuments(documents, selectedDocumentIndex);
                        }
                        finally
                        {
                            DocGlance.Infrastructure.Diagnostics.DiagnosticsService.Milestone("SessionRestoreCompleted");
                        }
                    }))
                {
                    Logger.Warn("[Session] UI dispatcher unavailable; restored session was not applied");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("RestoreSession failed", ex);
                DocGlance.Infrastructure.Diagnostics.DiagnosticsService.Milestone("SessionRestoreFailed");
            }
        });
    }

    public static void RestoreWorkspace(
        DispatcherQueue dispatcherQueue,
        IReadOnlyList<string> filePaths,
        int selectedIndex,
        Action<IReadOnlyList<DocumentItem>, int> restoreDocuments)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var documents = new List<DocumentItem>();
                int restoredSelectedIndex = -1;
                for (int i = 0; i < filePaths.Count; i++)
                {
                    string filePath = filePaths[i];
                    try
                    {
                        DocumentItem document = await CreateDocumentAsync(filePath);
                        if (i == selectedIndex)
                            restoredSelectedIndex = documents.Count;
                        documents.Add(document);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Failed to prepare workspace tab for: {filePath}", ex);
                    }
                }

                if (!dispatcherQueue.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () =>
                    {
                        try
                        {
                            restoreDocuments(documents, restoredSelectedIndex);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Failed to restore workspace tabs", ex);
                        }
                    }))
                {
                    Logger.Warn("[Workspace] UI dispatcher unavailable; workspace was not opened");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Workspace restore failed", ex);
            }
        });
    }

    private static async Task<DocumentItem> CreateDocumentAsync(string filePath)
    {
        Windows.Storage.StorageFile file =
            await StorageAccessStore.GetFileAsync(filePath);
        var properties = await file.GetBasicPropertiesAsync();
        long size = checked((long)properties.Size);
        DateTime modified = properties.DateModified.DateTime;

        return new DocumentItem
        {
            FilePath = file.Path,
            Title = file.Name,
            FolderPath = Path.GetDirectoryName(file.Path) ?? string.Empty,
            FileType = file.FileType.TrimStart('.').ToUpperInvariant(),
            FileSizeBytes = size,
            FileSize = Common.Formatters.FileSizeFormatter.Format(size),
            LastWriteTime = modified,
            LastModified = modified.ToString("MMM dd, yyyy"),
        };
    }
}
