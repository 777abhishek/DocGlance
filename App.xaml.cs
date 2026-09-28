// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using QuickView.Shell;
using System;
using System.IO;
using System.Linq;
using QuickView.Infrastructure.Logging;
using QuickView.Infrastructure.Registration;
using QuickView.Models;

namespace QuickView;

public partial class App : Application
{
    public Window? MainWindow => m_window;

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
                File.WriteAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "crash.txt"),
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
            m_window.Activate();
            Logger.Log("Window activated");

            // Register file associations in background (non-blocking)
            _ = RegistrationHelper.EnsureRegisteredAsync();

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
        QuickView.Rendering.RendererRegistry.Register(new QuickView.Rendering.Images.ImageRenderer());
        
        // F7: Office, Media, and HTML renderers enabled
        // Note: Office renderers (DOCX, XLSX, PPTX) currently show info cards only.
        // Full Office rendering would require COM interop or third-party libraries.
        QuickView.Rendering.RendererRegistry.Register(new QuickView.Rendering.Office.DocxRenderer());
        QuickView.Rendering.RendererRegistry.Register(new QuickView.Rendering.Office.XlsxRenderer());
        QuickView.Rendering.RendererRegistry.Register(new QuickView.Rendering.Office.PptxRenderer());
        QuickView.Rendering.RendererRegistry.Register(new QuickView.Rendering.Media.MediaRenderer());
        QuickView.Rendering.RendererRegistry.Register(new QuickView.Rendering.Web.HtmlRenderer());
    }

    /// <summary>
    /// Inspects AppActivationArguments and opens the file if a path is found.
    /// Returns true when a file was opened.
    /// </summary>
    private static bool TryOpenFromActivationArgs(AppActivationArguments? args)
    {
        if (args == null) return false;

        string? filePath = null;

        if (args.Kind == ExtendedActivationKind.File &&
            args.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs)
        {
            filePath = fileArgs.Files.OfType<Windows.Storage.StorageFile>()
                                     .FirstOrDefault()?.Path;
        }
        else if (args.Kind == ExtendedActivationKind.Launch &&
                 args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs)
        {
            // Shell passes the file path as the launch arguments string
            var raw = launchArgs.Arguments?.Trim().Trim('"');
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
            rootElement.RequestedTheme = theme;
    }

    private Window? m_window;
}
