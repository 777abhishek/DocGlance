using System;
using System.Threading.Tasks;

namespace DocGlance.Infrastructure.Logging;

/// <summary>
/// Extensions for intentionally-unawaited tasks ("fire and forget").
/// Discarding a Task with `_ = SomeAsync()` loses its exceptions to the finalizer
/// thread; this wrapper routes them into the log instead.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Runs the task without awaiting it, logging any exception (except
    /// OperationCanceledException, which is logged at Debug) with full detail.
    /// Use for background work whose failure must be observable but which the
    /// caller deliberately does not await.
    /// </summary>
    public static void SafeFireAndForget(this Task task, string? operationName = null)
    {
        if (task == null) return;

        task.ContinueWith(t =>
        {
            // Order matters: an aggregate may wrap a cancellation.
            if (t.Exception is not null)
            {
                var flattened = t.Exception.Flatten();
                if (flattened.InnerExceptions.Count == 1 &&
                    flattened.InnerExceptions[0] is OperationCanceledException)
                {
                    Logger.Write(LogLevel.Debug,
                        $"Event=FireAndForgetCancelled Operation={operationName ?? "unknown"} " +
                        flattened.InnerExceptions[0].Message);
                    return;
                }

                Logger.Write(LogLevel.Error,
                    $"Event=FireAndForgetFailed Operation={operationName ?? "unknown"}",
                    flattened.InnerExceptions.Count == 1 ? flattened.InnerExceptions[0] : t.Exception);
            }
        }, TaskScheduler.Default);
    }
}
