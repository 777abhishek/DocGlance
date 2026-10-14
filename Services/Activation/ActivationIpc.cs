// ActivationIpc.cs
// Named-pipe channel that carries file paths from a second process (launched by
// Explorer) to the already-running primary instance.
//
// WHY THIS EXISTS
// ───────────────
// The Windows App SDK single-instance pattern redirects activation via
// AppInstance.RedirectActivationToAsync, but for unpackaged apps the redirected
// ILaunchActivatedEventArgs repeatedly arrive at the primary instance with the
// Arguments lost (or the Activated event never fires). When that happens the file
// simply never opens. The pipe is a direct, dependency-free channel that always
// delivers the payload; the redirect is kept purely for foregrounding the window.
//
// Protocol: client connects, writes newline-separated UTF-8 paths, disconnects.
// Server reads until the client disconnects, then opens the files. Per-user pipe
// name + per-user ACL prevent other accounts from talking to this instance.

using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using DocGlance.Infrastructure.Logging;

namespace DocGlance.Services.Activation;

public static class ActivationIpc
{
    private static readonly string PipeName = BuildPipeName();

    private static string BuildPipeName()
    {
        // Namespace the pipe per user so concurrent sessions never cross wires.
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (!string.IsNullOrWhiteSpace(sid))
                return $"DocGlance.Activation.{sid}";
        }
        catch { }
        return $"DocGlance.Activation.{Environment.UserName}";
    }

    // ── Second process: send the paths to the primary instance ───────────────

    /// <summary>
    /// Delivers the given paths to the primary instance. Retries briefly so a
    /// primary instance that is still starting up (pipe server not yet listening)
    /// is still reached. Returns true when the payload was written.
    /// </summary>
    public static bool TrySendPaths(IReadOnlyList<string> paths, TimeSpan overallBudget)
    {
        if (paths.Count == 0)
            return false;

        var payload = string.Join("\n", paths);
        var deadline = DateTime.UtcNow + overallBudget;
        var attemptDelay = TimeSpan.FromMilliseconds(250);

        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName,
                    PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                client.Connect(timeout: 1000);

                var bytes = System.Text.Encoding.UTF8.GetBytes(payload);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                client.WaitForPipeDrain();
                Logger.Log($"[ActivationIpc] Sent {paths.Count} path(s) to primary instance");
                return true;
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow + attemptDelay >= deadline)
                {
                    Logger.Warn($"[ActivationIpc] Send failed after retries: {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
                Thread.Sleep(attemptDelay);
            }
        }
    }

    // ── Primary instance: listen for handover payloads forever ───────────────

    /// <summary>
    /// Starts the pipe server on a background thread. Runs for the life of the
    /// process, serving one connection at a time (single-instance apps only ever
    /// need that). Never throws; each connection failure is logged and the loop
    /// continues.
    /// </summary>
    public static void StartServer(Action<IReadOnlyList<string>> onPathsReceived)
    {
        var thread = new Thread(() => ServerLoop(onPathsReceived))
        {
            IsBackground = true,
            Name = "DocGlance-ActivationIpc",
        };
        thread.Start();
    }

    private static void ServerLoop(Action<IReadOnlyList<string>> onPathsReceived)
    {
        Logger.Log($"[ActivationIpc] Server listening on {PipeName}");

        while (true)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateServer();
                server.WaitForConnection();
                Logger.Log("[ActivationIpc] Client connected");

                // Read until the client disconnects (short-lived client protocol).
                using var reader = new System.IO.StreamReader(server, System.Text.Encoding.UTF8);
                var content = reader.ReadToEnd();

                var paths = content
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => p.Length > 0)
                    .ToList();

                if (paths.Count > 0)
                {
                    Logger.Log($"[ActivationIpc] Received {paths.Count} path(s) from second instance");
                    onPathsReceived(paths);
                }
                else
                {
                    Logger.Warn("[ActivationIpc] Received empty payload from second instance");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[ActivationIpc] Server loop error (continuing): {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { server?.Dispose(); } catch { }
            }
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        // PipeOptions.CurrentUserOnly makes the OS reject connections from any other
        // account, and the pipe name carries the user SID — that is the isolation we
        // need without hand-building a PipeSecurity ACL.
        return new NamedPipeServerStream(PipeName, PipeDirection.In,
            maxNumberOfServerInstances: 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    }
}
