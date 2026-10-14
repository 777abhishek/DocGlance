// Program.cs — Custom entry point for DocGlance (WinUI 3 unpackaged).
//
// WHY THIS EXISTS
// ───────────────
// WinUI 3 unpackaged apps use a XAML-compiler-generated Main() by default.
// That generated main bootstraps the Windows App SDK BEFORE the process DPI
// context is set, so the compositor treats the process as "DPI unaware" and
// bitmap-stretches the entire window — making every pixel soft on 4K / Hi-DPI
// monitors (125 %, 150 %, 200 % scaling).
//
// SOLUTION (official Microsoft pattern — see docs.microsoft.com/windows/apps/
// windows-app-sdk/applifecycle/applifecycle-single-instance)
// ─────────────────────────────────────────────────────────────────────────────
// 1. Define DISABLE_XAML_GENERATED_MAIN in the .csproj so the XAML compiler
//    emits the partial Program class stub WITHOUT the Main() body.
// 2. Supply our own partial Program class with Main() that:
//      a) Calls SetProcessDpiAwarenessContext() as the very first thing.
//      b) Does the exact same WinUI 3 bootstrap the generated code would have done.
//
// DPI AWARENESS FALLBACK CHAIN (best → least capable)
// ─────────────────────────────────────────────────────
// a) SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)
//    Win 10 1703+ (Build 15063). Re-renders at the physical DPI of each monitor.
//    Handles moving the window between monitors with different scale factors.
// b) SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE)
//    Win 8.1+ via user32. Per-monitor crisp; no automatic WM_DPICHANGED handling.
// c) SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE) via shcore.dll
//    Win 8.1+ older API surface — same behaviour as (b).
// d) SetProcessDPIAware() — Win Vista+.
//    System-DPI aware: better than nothing but still blurry on secondary
//    monitors that have a different scale factor from the primary.
//
// app.manifest also declares PerMonitorV2 (belt-and-braces).
// Windows uses whichever declaration it sees first; the API call wins.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using DocGlance.Infrastructure.Diagnostics;
using DocGlance.Infrastructure.Logging;
using DocGlance.Services.Activation;

namespace DocGlance;

// IMPORTANT: When DISABLE_XAML_GENERATED_MAIN is defined, the XAML compiler
// generates a separate internal XamlGeneratedProgram class (not Program).
// This Program class stands alone and provides the actual entry point.
public static class Program
{
    // ── DPI awareness constants (from windef.h) ───────────────────────────────

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
        = new IntPtr(-4);   // Win 10 1703+ (Build 15063) — best quality

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE
        = new IntPtr(-3);   // Win 8.1 + user32 — per-monitor, no auto WM_DPICHANGED

    private const int PROCESS_PER_MONITOR_DPI_AWARE = 2; // shcore.dll enum

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("shcore.dll", SetLastError = true, ExactSpelling = true)]
    private static extern int SetProcessDpiAwareness(int value);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint dwProcessId);

    // ── Entry point ───────────────────────────────────────────────────────────

    [STAThread]
    public static void Main(string[] args)
    {
        // STEP 0 — Crash capture before anything else can fail silently. The static
        // Logger is usable without XAML; a bootstrap crash must still reach app.log.
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    DocGlance.Infrastructure.Logging.Logger.Write(
                        DocGlance.Infrastructure.Logging.LogLevel.Fatal,
                        $"Event=BootstrapUnhandledException IsTerminating={e.IsTerminating}", ex);
                DocGlance.Infrastructure.Logging.Logger.Flush(TimeSpan.FromSeconds(2));
            };
            DocGlance.Infrastructure.Logging.Logger.Log("Process started");
            DocGlance.Infrastructure.Diagnostics.DiagnosticsService.Milestone("ProcessStart");
        }
        catch
        {
            // Logging infrastructure unavailable — proceed; WER will still capture the crash.
        }

        // STEP 1 — DPI awareness: must be the absolute first call, before any
        // Win32, WinUI, or Windows App SDK initialisation occurs.
        EnsurePerMonitorDpiAwareness();

        // STEP 2 — Standard WinUI 3 / Windows App SDK bootstrap.
        // This mirrors exactly what the XAML-generated Main() does.
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // STEP 3 — Single-instancing: if an instance is already running,
        // hand the activation (e.g. file double-clicked in Explorer) to it and exit.
        //
        // Payload delivery strategy (two channels, deliberately redundant):
        //  1. Named pipe (ActivationIpc): the file path(s) are sent directly to the
        //     running instance. This is the RELIABLE channel — redirected
        //     ILaunchActivatedEventArgs are known to lose their Arguments for
        //     unpackaged apps, so the redirect below must not be trusted with the path.
        //  2. RedirectActivationToAsync: best effort, used only to bring the running
        //     window to the foreground. Failure here is logged and ignored.
        try
        {
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("DocGlanceSingleInstance");
            if (!mainInstance.IsCurrent)
            {
                Logger.Log("[SingleInstance] Existing instance detected — handing over");
                DiagnosticsService.Milestone("SecondInstanceHandover");

                var activatedArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                Logger.Log($"[SingleInstance] Redirect args: Kind={activatedArgs?.Kind}, " +
                           $"DataType={activatedArgs?.Data?.GetType().Name ?? "null"}, " +
                           $"CmdLine={string.Join(' ', Environment.GetCommandLineArgs().Skip(1))}");

                // The new process was launched by the user's file-open action. Transfer
                // its foreground permission to the existing instance before handing over
                // activation; otherwise Windows may accept the file but leave its window
                // behind the app that was previously in front.
                uint primaryProcessId = unchecked((uint)mainInstance.ProcessId);
                if (AllowSetForegroundWindow(primaryProcessId))
                    Logger.Log($"[SingleInstance] Foreground permission granted to PID {primaryProcessId}");
                else
                    Logger.Warn($"[SingleInstance] Could not grant foreground permission to PID {primaryProcessId}: Win32Error={Marshal.GetLastWin32Error()}");

                // Collect the file path(s) this second process was launched with.
                var paths = ActivationPaths.FromCurrentLaunch();
                Logger.Log($"[SingleInstance] Extracted paths: {(paths.Count > 0 ? string.Join(" | ", paths) : "(none)")}");

                // Send the payload over the pipe while the redirect runs in parallel.
                // Both are bounded so a hung or dying primary instance cannot hang us.
                var pipeTask = System.Threading.Tasks.Task.Run(
                    () => ActivationIpc.TrySendPaths(paths, TimeSpan.FromSeconds(5)));

                var redirected = System.Threading.Tasks.Task
                    .Run(() => mainInstance.RedirectActivationToAsync(activatedArgs).AsTask())
                    .Wait(TimeSpan.FromSeconds(10));

                Logger.Log($"[SingleInstance] Redirect: {(redirected ? "completed" : "TIMEOUT or failed (ignored)")}, " +
                           $"Pipe delivery: {(pipeTask.Result ? "succeeded" : "failed")}");

                // The Logger writes on a background thread — flush before this
                // short-lived process exits or the entries above may be lost.
                Logger.Flush(TimeSpan.FromSeconds(3));
                return;
            }
            Logger.Log("[SingleInstance] This process is the primary instance");
        }
        catch (Exception ex)
        {
            Logger.Error("[SingleInstance] Handover check failed — continuing as primary", ex);
            Logger.Flush(TimeSpan.FromSeconds(2));
        }

        Microsoft.UI.Xaml.Application.Start(p =>
        {
            var syncCtx = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(syncCtx);
            _ = new App();
        });
    }

    // ── DPI helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks the DPI awareness fallback chain and applies the best mode available
    /// on the current OS version. Never throws — a failure here must not prevent
    /// the app from starting.
    /// </summary>
    private static void EnsurePerMonitorDpiAwareness()
    {
        try
        {
            // (a) PerMonitorV2 — Win 10 1703+ (Build 15063)
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2))
            {
                Debug.WriteLine("[DPI] PerMonitorV2 set — crisp rendering on all monitors");
                return;
            }

            // (b) PerMonitor V1 via user32 — earlier Win 10 builds
            if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE))
            {
                Debug.WriteLine("[DPI] PerMonitor V1 set via user32");
                return;
            }

            // (c) PerMonitor via shcore.dll — Win 8.1+
            // S_OK = 0x0, E_ACCESSDENIED = 0x80070005 (already set = fine)
            int hr = SetProcessDpiAwareness(PROCESS_PER_MONITOR_DPI_AWARE);
            if (hr == 0 || hr == unchecked((int)0x80070005))
            {
                Debug.WriteLine("[DPI] PerMonitor set via shcore.dll");
                return;
            }

            // (d) System-DPI aware — Vista+ fallback
            SetProcessDPIAware();
            Debug.WriteLine("[DPI] System-DPI aware fallback applied");
        }
        catch (EntryPointNotFoundException)
        {
            Debug.WriteLine("[DPI] API unavailable on this OS — manifest declaration only");
        }
        catch (DllNotFoundException)
        {
            Debug.WriteLine("[DPI] shcore.dll not found — skipping that step");
        }
        catch (Exception ex)
        {
            // Never crash the app over a DPI setup failure.
            Debug.WriteLine($"[DPI] Unexpected error during DPI setup: {ex.Message}");
        }
    }
}
