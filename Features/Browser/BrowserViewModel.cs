using QuickView.Services.FileSystem.Scanner;
using QuickView.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace QuickView.Features.Browser;

/// <summary>
/// ViewModel for BrowserPage — manages the current folder path and its contents.
/// </summary>
public sealed class BrowserViewModel
{
    public ObservableCollection<DocumentItem> Items       { get; } = new();
    public string                             CurrentPath { get; private set; } = string.Empty;

    // Track the most recent scan so that rapid navigation cancels the previous one.
    private CancellationTokenSource? _scanCts;

    /// <summary>
    /// Navigates to <paramref name="folderPath"/>, clears the list, and
    /// asynchronously populates it via <see cref="FolderScanner"/>.
    /// Any in-progress scan is cancelled before starting the new one.
    /// </summary>
    public async Task NavigateAsync(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        // Cancel any ongoing scan for the previous folder.
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        CurrentPath = folderPath;
        Items.Clear();

        try
        {
            var files = await FolderScanner.ScanFolderAsync(folderPath, ct);

            if (ct.IsCancellationRequested) return;

            foreach (var item in files)
                Items.Add(item);
        }
        catch (OperationCanceledException)
        {
            // Navigation was superseded — normal, no action needed.
        }
        catch (Exception ex)
        {
            QuickView.Infrastructure.Logging.Logger.Error(
                $"BrowserViewModel.NavigateAsync({folderPath})", ex);
        }
    }

    /// <summary>
    /// Synchronous overload kept for backward compatibility.
    /// Fires-and-forgets the async scan; prefer <see cref="NavigateAsync"/> when possible.
    /// </summary>
    public void Navigate(string folderPath) =>
        _ = NavigateAsync(folderPath);
}
