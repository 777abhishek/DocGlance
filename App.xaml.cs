// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using QuickView.Infrastructure.Logging;
using QuickView.Services.Registration;
using QuickView.Core.Models;
using QuickView.Common.Validators;
using QuickView.UI.Shell;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace QuickView;

public partial class App : Application
{
    public Window? MainWindow => m_window;

    // All open TitleBarWindows — used for tab drag-between-windows
    public static readonly System.Collections.Generic.List<TitleBarWindow> AllWindows = new();

    public App()
    {
        try
        {
            InitializeComponent();

            // Global exception handlers
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Handle file-open activations that arrive while the app is already
            // running (e.g. user double-clicks a second file in Explorer while
            // the app is open).  The OS re-activates the existing instance; we
            // receive the new args here and open the file in a new tab.
            AppInstance.GetCurrent().Activated += OnAppActivated;

            Logger.Log("App constructor completed");
        }
        catch (Exception ex)
        {
            try
            {
                // Write crash log to the same place as the regular log for consistency.
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "QuickView", "Logs");
                Directory.CreateDirectory(logDir);
                File.WriteAllText(
                    Path.Combine(logDir, "crash.txt"),
                    $"App constructor crashed:\n{ex.Message}\n{ex.StackTrace}");
            }
            catch { }
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Logger.Error("UNHANDLED EXCEPTION", e.Exception);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Logger.Error("DOMAIN UNHANDLED EXCEPTION", ex);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Logger.Log("OnLaunched called");
            
            // Register file renderers FIRST (needed for file opening)
            RegisterRenderers();
            
            // Create and show window with full initialization
            m_window = new TitleBarWindow();
            App.AllWindows.Add((TitleBarWindow)m_window);
            m_window.Closed += (_, _) => App.AllWindows.Remove((TitleBarWindow)m_window);
            
            // UI-08: Apply backdrop setting from storage
            var micaSetting = Services.Storage.SettingsStore.Load<bool?>("UseMicaBackdrop");
            bool useMica = micaSetting ?? true;
            if (useMica)
            {
                // Use DesktopAcrylic for gray appearance matching Windows 11 File Explorer
                m_window.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
            }
            
            m_window.Activate();
            Logger.Log("Window activated");

#if !PACKAGED
            // Register file associations in background (non-blocking)
            // In packaged builds this is skipped — associations come from Package.appxmanifest.
            _ = RegistrationHelper.EnsureRegisteredAsync();
#endif

            // Check for file-open activation
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (!TryOpenFromActivationArgs(activatedArgs))
                TryOpenFromCommandLine();
        }
        catch (Exception ex)
        {
            Logger.Error("OnLaunched failed", ex);
            throw;
        }
    }

    // ── Re-activation while already running (second file opened from Explorer) ──

    private void OnAppActivated(object? sender, AppActivationArguments args)
    {
        // Must hop to the UI thread before touching any XAML.
        m_window?.DispatcherQueue.TryEnqueue(() =>
        {
            m_window.Activate(); // bring to foreground
            TryOpenFromActivationArgs(args);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A1: Register all IFileRenderer implementations with the RendererRegistry.
    /// This allows ViewerPage to fall back to the registry after checking for
    /// PDF/text/code/Markdown, enabling image and Office format support.
    /// </summary>
    private static void RegisterRenderers()
    {
        // Office renderers (DOCX, XLSX, PPTX) currently show info cards only.
        // Full Office rendering would require COM interop or third-party libraries.
        var docxRenderer = new QuickView.Rendering.Office.DocxRenderer();
        var xlsxRenderer = new QuickView.Rendering.Office.XlsxRenderer();
        var pptxRenderer = new QuickView.Rendering.Office.PptxRenderer();
        
        QuickView.Rendering.RendererRegistry.Register(docxRenderer);
        QuickView.Rendering.RendererRegistry.Register(xlsxRenderer);
        QuickView.Rendering.RendererRegistry.Register(pptxRenderer);
        
        System.Diagnostics.Debug.WriteLine($"[App] Registered DOCX renderer for: {string.Join(", ", docxRenderer.SupportedExtensions)}");
        System.Diagnostics.Debug.WriteLine($"[App] Registered XLSX renderer for: {string.Join(", ", xlsxRenderer.SupportedExtensions)}");
        System.Diagnostics.Debug.WriteLine($"[App] Registered PPTX renderer for: {string.Join(", ", pptxRenderer.SupportedExtensions)}");
    }

    /// <summary>
    /// Inspects AppActivationArguments and opens the file if a path is found.
    /// Returns true when a file was opened.
    /// </summary>
    private static bool TryOpenFromActivationArgs(AppActivationArguments? args)
    {
        if (args == null) return false;

        string? filePath = null;

        if (args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            filePath = fileArgs.Files.OfType<Windows.Storage.StorageFile>()
                                     .FirstOrDefault()?.Path;
        }
        else if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs)
        {
            var raw = launchArgs.Arguments?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(raw) &&
                !raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(raw, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(raw))
            {
                filePath = raw;
            }
        }
        else if (args.Data is Windows.ApplicationModel.Activation.ICommandLineActivatedEventArgs cmdArgs)
        {
            var raw = cmdArgs.Operation?.Arguments?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(raw) &&
                !raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(raw, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(raw))
            {
                filePath = raw;
            }
        }

        if (filePath != null)
        {
            OpenFilePath(filePath);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Fallback: read file path from process command-line arguments.
    /// Handles: QuickView.exe "C:\path\to\file.pdf"
    /// </summary>
    private static void TryOpenFromCommandLine()
    {
        try
        {
            var cmdArgs = Environment.GetCommandLineArgs();
            // cmdArgs[0] is the exe itself; [1] onwards are user-supplied args.
            foreach (var arg in cmdArgs.Skip(1))
            {
                var path = arg.Trim().Trim('"');
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(path))
                {
                    OpenFilePath(path);
                    return; // open only the first valid file
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("TryOpenFromCommandLine", ex);
        }
    }

    /// <summary>
    /// Builds a DocumentItem from a file path and opens it in a new tab.
    /// </summary>
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

            // Validate path for security and file size for DoS prevention
            if (!FileSizeValidator.IsFileValidAndSafe(filePath, out var validationError))
            {
                Logger.Error($"File validation failed for: {filePath} - {validationError}");
                // TODO: Show error dialog to user
                return;
            }

            Logger.Log($"[ACTIVATION] Opening: {filePath}");
            var fi  = new FileInfo(filePath);
            var doc = DocumentItem.FromFileInfo(fi);
            TitleBarWindow.Current?.OpenDocument(doc);
        }
        catch (Exception ex)
        {
            Logger.Error($"OpenFilePath({filePath})", ex);
        }
    }

    public void SetTheme(ElementTheme theme)
    {
        if (m_window?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = theme;
            
            // Update title bar button colors after theme changes.
            // Subscribe to ActualThemeChanged instead of using a timing hack.
            if (m_window is TitleBarWindow titleBarWindow)
            {
                // Trigger immediately, then the event will handle future changes.
                titleBarWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    titleBarWindow.ConfigureTitleBarTheme();
                });
            }
        }
    }

    private Window? m_window;
}
