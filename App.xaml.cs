// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using DocGlance.Infrastructure.Logging;
using DocGlance.Infrastructure.Configuration;
using DocGlance.Services.Registration;
using DocGlance.Core.Models;
using DocGlance.Common.Validators;
using DocGlance.UI.Shell;
using DocGlance.Services.Activation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace DocGlance;

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

            // Global exception handlers are configured here so unhandled failures are logged consistently:
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
            DocGlance.Rendering.Shared.NativeBitmap.Initialize(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

            // Create the shell first; file activation is handled after its first frame.
            var titleBarWindow = new TitleBarWindow();
            m_window = titleBarWindow;
            App.AllWindows.Add(titleBarWindow);
            m_window.Closed += (_, _) => App.AllWindows.Remove((TitleBarWindow)m_window);

            // Listen for file handovers from second processes (Explorer double-click
            // while running). The Windows App SDK redirect is unreliable for unpackaged
            // apps — this pipe is the guaranteed payload channel (see Program.cs).
            DocGlance.Services.Activation.ActivationIpc.StartServer(ActivationService.OpenExternalFiles);

#if !PACKAGED
            // Register file associations in background (non-blocking)
            // In packaged builds this is skipped — associations come from Package.appxmanifest.
            _ = RegistrationHelper.EnsureRegisteredAsync();
#endif

            var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            bool hasFileLaunch = ActivationPaths.FromActivationArgs(activatedArgs).Count > 0 ||
                                 ActivationPaths.FromCommandLine(
                                     Environment.GetCommandLineArgs().Skip(1)).Any();
            if (hasFileLaunch)
                titleBarWindow.DeferHomePageInitializationForFileActivation();

            // Show the shell before doing file validation and constructing the viewer.
            // The low-priority callback runs after the first frame and still precedes
            // Home's low-priority initialization, so file launches open directly.
            m_window.Activate();
            Logger.Log("Window activated");
            Infrastructure.Diagnostics.DiagnosticsService.Milestone("WindowActivated");

            if (!m_window.DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    () =>
                    {
                        try
                        {
                            bool openedFromActivation =
                                ActivationService.TryOpenFromActivationArgs(activatedArgs);
                            bool openedFromCommandLine =
                                !openedFromActivation && ActivationService.TryOpenFromCommandLine();

                            // File activation takes precedence. Session I/O runs off-thread.
                            if (!openedFromActivation && !openedFromCommandLine &&
                                m_window is TitleBarWindow restoreWindow &&
                                AppSettings.RestoreLastSession)
                            {
                                Infrastructure.Diagnostics.DiagnosticsService.Milestone(
                                    "SessionRestoreScheduled");
                                restoreWindow.RestoreSession();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Deferred startup activation failed", ex);
                        }
                        finally
                        {
                            if (hasFileLaunch)
                                titleBarWindow.CompleteFileActivationStartup();
                        }
                    }))
            {
                Logger.Error("Could not queue startup activation processing");
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
        var data = args?.Data;
        Logger.Log($"[Reactivation] Redirect delivered: Kind={args?.Kind}, " +
                   $"DataType={data?.GetType().Name ?? "null"}");

        if (m_window is null)
        {
            Logger.Error("[Reactivation] m_window is null — activation dropped");
            return;
        }

        if (ActivationPaths.FromActivationArgs(args).Count == 0)
        {
            Logger.Log("[Reactivation] No file path in redirected args; waiting for activation pipe");
            return;
        }

        m_window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                bool opened = ActivationService.TryOpenFromActivationArgs(args);
                ((TitleBarWindow)m_window).RestoreAndActivate();
                Logger.Log($"[Reactivation] Redirect-args handling: {(opened ? "file(s) opened" : "no usable file in redirected args")}");
            }
            catch (Exception ex)
            {
                Logger.Error("[Reactivation] Failed to handle redirected activation", ex);
            }
        });
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
