using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocGlance.Services.Storage;

public sealed class SavedWorkspace
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public List<string> FilePaths { get; set; } = new();
    public int SelectedIndex { get; set; }
    public bool IsStartup { get; set; }

    public int DocumentCount => FilePaths.Count;
    public string DocumentCountLabel => DocumentCount == 1
        ? "1 document"
        : $"{DocumentCount} documents";
    public string StartupStatus => IsStartup ? "Opens on startup" : "Saved workspace";
    public string StartupActionLabel => IsStartup ? "Clear startup workspace" : "Open on startup";
    public string DocumentPreview => string.Join(
        " · ",
        FilePaths.Take(4).Select(path => Path.GetFileNameWithoutExtension(path))
            .Concat(FilePaths.Count > 4 ? new[] { $"and {FilePaths.Count - 4} more" } : Array.Empty<string>()));
}

public static class WorkspaceStore
{
    private const string Key = "SavedWorkspaces";
    private static readonly object Gate = new();

    public static List<SavedWorkspace> Load()
    {
        lock (Gate)
        {
            var workspaces = SettingsStore.Load<List<SavedWorkspace>>(Key) ?? new();
            foreach (SavedWorkspace workspace in workspaces)
            {
                workspace.FilePaths ??= new List<string>();
                workspace.Id = string.IsNullOrWhiteSpace(workspace.Id)
                    ? Guid.NewGuid().ToString("N")
                    : workspace.Id;
            }

            return workspaces;
        }
    }

    public static SavedWorkspace Save(
        string name,
        IEnumerable<string> filePaths,
        int selectedIndex,
        string? workspaceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(filePaths);

        string normalizedName = name.Trim();
        List<string> paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) &&
                           StorageAccessStore.HasRememberedFile(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
            throw new InvalidOperationException("Open at least one existing document before saving a workspace.");

        lock (Gate)
        {
            List<SavedWorkspace> workspaces = Load();
            if (workspaces.Any(workspace =>
                    !string.Equals(workspace.Id, workspaceId, StringComparison.Ordinal) &&
                    string.Equals(workspace.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A workspace with this name already exists.");

            SavedWorkspace? workspace = workspaceId == null
                ? null
                : workspaces.FirstOrDefault(item =>
                    string.Equals(item.Id, workspaceId, StringComparison.Ordinal));

            if (workspace == null)
            {
                workspace = new SavedWorkspace { Id = Guid.NewGuid().ToString("N") };
                workspaces.Add(workspace);
            }

            workspace.Name = normalizedName;
            workspace.FilePaths = paths;
            workspace.SelectedIndex = Math.Clamp(selectedIndex, 0, paths.Count - 1);
            SaveAll(workspaces);
            return workspace;
        }
    }

    public static void Delete(string workspaceId)
    {
        lock (Gate)
        {
            List<SavedWorkspace> workspaces = Load();
            if (workspaces.RemoveAll(item =>
                    string.Equals(item.Id, workspaceId, StringComparison.Ordinal)) == 0)
                return;
            SaveAll(workspaces);
        }
    }

    public static void AddDocument(string workspaceId, string filePath)
        => AddDocuments(workspaceId, new[] { filePath });

    public static void AddDocuments(string workspaceId, IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        List<string> paths = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
            throw new InvalidOperationException("Select at least one document to add to a workspace.");
        if (paths.Any(path => !StorageAccessStore.HasRememberedFile(path)))
            throw new InvalidOperationException(
                "A document's access permission has expired. Reopen it before adding it to a workspace.");

        lock (Gate)
        {
            List<SavedWorkspace> workspaces = Load();
            SavedWorkspace? workspace = workspaces.FirstOrDefault(item =>
                string.Equals(item.Id, workspaceId, StringComparison.Ordinal));
            if (workspace == null)
                throw new InvalidOperationException("The workspace no longer exists.");

            var existingPaths = new HashSet<string>(
                workspace.FilePaths, StringComparer.OrdinalIgnoreCase);
            workspace.FilePaths.AddRange(paths.Where(existingPaths.Add));
            SaveAll(workspaces);
        }
    }

    public static void SetStartup(string? workspaceId)
    {
        lock (Gate)
        {
            List<SavedWorkspace> workspaces = Load();
            foreach (SavedWorkspace workspace in workspaces)
                workspace.IsStartup = string.Equals(
                    workspace.Id, workspaceId, StringComparison.Ordinal);
            SaveAll(workspaces);
        }
    }

    private static void SaveAll(List<SavedWorkspace> workspaces) =>
        SettingsStore.Save(Key, workspaces);
}
