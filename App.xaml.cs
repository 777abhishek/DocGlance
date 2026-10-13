// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using QuickView.Infrastructure.Logging;
using QuickView.Infrastructure.Configuration;
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

            // Global exception handlers (see docs/reliability/CRASH_DIAGNOSTICS.md):
            //  - XAML UnhandledException: logged FATAL, NOT marked handled. Marking handled
            //    previously kept a broken visual tree alive — the reported transparent/blank
            //    window. Letting it crash produces a WER dump and a clean failure instead.
            //  - AppDomain.UnhandledException: last-gasp log + flush before termination.
            //  - UnobservedTaskException: GC-finalizer observation; logged, marked observed
            //    (.NET 8 does not crash on these — preserving that semantics).
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += UnobservedTaskException;

            Infrastructure.Diagnostics.DiagnosticsService.LogEnvironment();

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
                Logger.Fatal("App constructor crashed", ex);
                Logger.Flush(TimeSpan.FromSeconds(3));
            }
            catch { }
            throw;
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Logger.Write(LogLevel.Fatal,
            $"Event=XamlUnhandledException Message={e.Message} HandledRequested=false",
            e.Exception);
        // Flush synchronously — if this exception terminates the process, the queue
        // writer thread may die before flushing its batch.
        Logger.Flush(TimeSpan.FromSeconds(2));
        // Deliberately NOT setting e.Handled = true (see constructor comment).
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Logger.Write(LogLevel.Fatal,
                $"Event=DomainUnhandledException IsTerminating={e.IsTerminating}", ex);
        else
            Logger.Fatal($"Event=DomainUnhandledException IsTerminating={e.IsTerminating} " +
                         $"ExceptionObject={e.ExceptionObject}");

        Logger.Flush(TimeSpan.FromSeconds(2));
    }

    private void UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        Logger.Write(LogLevel.Warning, "Event=UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Logger.Log("OnLaunched called");
            Infrastructure.Diagnostics.DiagnosticsService.Milestone("OnLaunched");

            // Verbose diagnostic logging opt-in (Settings → written to app settings file).
            Logger.VerboseEnabled = Infrastructure.Configuration.AppSettings.VerboseLogging;

            // WriteableBitmaps must be created on the UI thread; renderers use this dispatcher.
            QuickView.Rendering.NativeBitmap.Initialize(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

            // Register file renderers FIRST (needed for file opening)
            RegisterRenderers();
            
            // Create and show window with full initialization
            m_window = new TitleBarWindow();
            App.AllWindows.Add((TitleBarWindow)m_window);
            m_window.Closed += (_, _) => App.AllWindows.Remove((TitleBarWindow)m_window);
            
            // TitleBarWindow applies its dark-theme Acrylic surface during construction.
            
            m_window.Activate();
            Logger.Log("Window activated");
            Infrastructure.Diagnostics.DiagnosticsService.Milestone("WindowActivated");

#if !PACKAGED
            // Register file associations in background (non-blocking)
            // In packaged builds this is skipped — associations come from Package.appxmanifest.
            _ = RegistrationHelper.EnsureRegisteredAsync();
#endif

            // Check for file-open activation
            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (!TryOpenFromActivationArgs(activatedArgs))
            {
                TryOpenFromCommandLine();
                
                // If no files opened and RestoreLastSession is enabled, restore previous session
                if (m_window is TitleBarWindow titleBarWindow && AppSettings.RestoreLastSession)
                {
                    titleBarWindow.RestoreSession();
                }
            }
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

        // These registry renderers are info-card fallbacks only; actual DOCX/XLSX/PPTX
        // rendering goes through the native wrappers (DocxDocument, SpreadsheetDocument, PptxDocument).
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
    /// Inspects AppActivationArguments and opens all files found.
    /// Returns true when at least one file was opened.
    /// </summary>
    private static bool TryOpenFromActivationArgs(AppActivationArguments? args)
    {
        if (args == null) return false;

        var filePaths = new System.Collections.Generic.List<string>();

        if (args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            // Handle multiple files from File Explorer
            filePaths.AddRange(fileArgs.Files
                .OfType<Windows.Storage.StorageFile>()
                .Select(f => f.Path)
                .Where(p => !string.IsNullOrEmpty(p)));
        }
        else if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs)
        {
            var raw = launchArgs.Arguments?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(raw) &&
                !raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(raw, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(raw))
            {
                filePaths.Add(raw);
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
                filePaths.Add(raw);
            }
        }

        if (filePaths.Count > 0)
        {
            foreach (var path in filePaths)
                OpenFilePath(path);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Fallback: read file path(s) from process command-line arguments.
    /// Handles: QuickView.exe "C:\path\to\file.pdf" "C:\path\to\file2.docx"
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
                ShowOpenError(filePath, validationError);
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

    /// <summary>Tells the user why a file could not be opened (instead of failing silently).</summary>
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
                        Title           = "Can't open file",
                        Content         = $"{Path.GetFileName(filePath)}\n\n{reason}",
                        CloseButtonText = "OK",
                        XamlRoot        = root.XamlRoot,
                    };
                    await dialog.ShowAsync();
                }
                catch (Exception ex)
                {
                    // e.g. another dialog is already open
                    Logger.Warn($"ShowOpenError dialog failed: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Error("ShowOpenError", ex);
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
