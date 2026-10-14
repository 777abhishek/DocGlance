using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using DocGlance.Infrastructure.Logging;

namespace DocGlance.Infrastructure.Diagnostics;

/// <summary>
/// Records environment, startup milestones and memory checkpoints so a crash or hang
/// in app.log can be attributed to a build, an OS/runtime combination, or memory pressure.
/// Checkpoints are event-driven (document open/close, render batches) — there is no
/// polling timer and nothing runs in the rendering hot path.
/// </summary>
public static class DiagnosticsService
{
    private static readonly Stopwatch SinceProcessStart = Stopwatch.StartNew();
    private static bool _environmentLogged;

    /// <summary>Name + correlation id of the operation in flight, used as crash context.</summary>
    private static volatile string _currentOperation = "none";

    public static string CurrentOperation => _currentOperation;

    public static void SetCurrentOperation(string? operation)
        => _currentOperation = operation ?? "none";

    /// <summary>
    /// Logs the process environment once. Safe to call repeatedly (first call wins).
    /// Never throws — a diagnostics failure must not affect startup.
    /// </summary>
    public static void LogEnvironment()
    {
        if (_environmentLogged) return;
        _environmentLogged = true;

        try
        {
            string version;
            string configuration;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString() ?? "unknown";
                configuration =
#if DEBUG
                    "Debug";
#else
                    "Release";
#endif
            }
            catch { version = "unknown"; configuration = "unknown"; }

            string winAppSdk = "unknown";
            try
            {
                winAppSdk = typeof(Microsoft.UI.Xaml.Application).Assembly.GetName().Version?.ToString() ?? "unknown";
            }
            catch { /* non-fatal */ }

            Logger.Write(LogLevel.Info,
                "Event=Environment | " +
                $"AppVersion={version} Configuration={configuration} " +
                $"OS={Environment.OSVersion.VersionString} " +
                $"ProcessArch={RuntimeInformation.ProcessArchitecture} " +
                $"Runtime={Environment.Version} " +
                $"WinAppSDK={winAppSdk} " +
                $"DpiAwareness=PerMonitorV2 " +
                $"LogDir={Logger.LogDirectory}");
        }
        catch
        {
            // Diagnostics must never break startup.
        }
    }

    /// <summary>Logs a startup milestone with elapsed ms since process start.</summary>
    public static void Milestone(string name)
        => Logger.Write(LogLevel.Info,
            $"Event=StartupMilestone Milestone={name} ElapsedMs={SinceProcessStart.ElapsedMilliseconds}");

    /// <summary>
    /// Logs managed heap state at a diagnostic checkpoint (document open/close, cache eviction).
    /// Gen-2 collections and a steep upward trend across checkpoints indicate a leak.
    /// </summary>
    public static void MemoryCheckpoint(string reason)
    {
        try
        {
            Logger.Write(LogLevel.Debug,
                "Event=MemoryCheckpoint | " +
                $"Reason={reason} " +
                $"ManagedBytes={GC.GetTotalMemory(forceFullCollection: false)} " +
                $"WorkingSetBytes={Environment.WorkingSet} " +
                $"GC0={GC.CollectionCount(0)} GC1={GC.CollectionCount(1)} GC2={GC.CollectionCount(2)}");
        }
        catch
        {
            // Never throw from diagnostics.
        }
    }

    /// <summary>Logs a native-library availability check result (name, found?, path).</summary>
    public static void NativeLibrary(string name, bool available, string path)
        => Logger.Write(available ? LogLevel.Info : LogLevel.Warning,
            $"Event=NativeLibraryCheck Name={name} Available={available} Path={path}");
}
