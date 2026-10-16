using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using DocGlance;
using DocGlance.Common.Validators;
using DocGlance.Core.Models;
using DocGlance.Infrastructure.Logging;
using DocGlance.UI.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocGlance.Services.Activation;

public static class ActivationService
{
    private static TitleBarWindow? MainWindow => (Application.Current as App)?.MainWindow as TitleBarWindow;

    public static void OpenExternalFiles(IReadOnlyList<string> paths)
    {
        var window = MainWindow;
        if (window is null)
        {
            Logger.Error("[ActivationIpc] No main window to open paths in");
            return;
        }

        window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                window.RestoreAndActivate();
                foreach (var path in paths)
                    OpenFilePath(path);
            }
            catch (Exception ex)
            {
                Logger.Error("[ActivationIpc] Failed to open handed-over paths", ex);
            }
        });
    }

    public static bool TryOpenFromActivationArgs(AppActivationArguments? args)
    {
        if (args == null)
        {
            Logger.Warn("[Activation] TryOpenFromActivationArgs: args is null");
            return false;
        }

        Logger.Log($"[Activation] TryOpenFromActivationArgs: Kind={args.Kind}, " +
                   $"DataType={args.Data?.GetType().Name ?? "null"}");

        var filePaths = ActivationPaths.FromActivationArgs(args);

        if (filePaths.Count == 0)
        {
            Logger.Warn("[Activation] No file paths decoded from activation args " +
                        "(redirect payload may have been lost — pipe channel is the fallback)");
            return false;
        }

        foreach (var path in filePaths)
            OpenFilePath(path);
        return true;
    }

    public static bool TryOpenFromCommandLine()
    {
        bool openedAny = false;
        try
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            foreach (var arg in cmdArgs.Skip(1))
            {
                var path = arg.Trim().Trim('"');
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                {
                    OpenFilePath(path);
                    openedAny = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("TryOpenFromCommandLine", ex);
        }

        return openedAny;
    }

    private static void OpenFilePath(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) ||
                filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(filePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var validationError))
            {
                Logger.Error($"File validation failed for: {filePath} - {validationError}");
                ShowOpenError(filePath, validationError);
                return;
            }

            Logger.Log($"[ACTIVATION] Opening: {filePath}");
            var fi = new FileInfo(filePath);
            var doc = DocumentItem.FromFileInfo(fi);
            TitleBarWindow.Current?.OpenDocument(doc);
        }
        catch (Exception ex)
        {
            Logger.Error($"OpenFilePath({filePath})", ex);
        }
    }

    private static void ShowOpenError(string filePath, string reason)
    {
        try
        {
            var window = TitleBarWindow.Current;
            if (window?.Content is not FrameworkElement root || root.XamlRoot == null)
                return;

            window.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "Can't open file",
                        Content = $"{Path.GetFileName(filePath)}\n\n{reason}",
                        CloseButtonText = "OK",
                        XamlRoot = root.XamlRoot,
                    };
                    await dialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    Logger.Warn($"ShowOpenError dialog failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("ShowOpenError", ex);
        }
    }
}
