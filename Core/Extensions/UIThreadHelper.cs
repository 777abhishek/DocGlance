using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace DocGlance.Core.Extensions;

/// <summary>
/// Helper for marshalling operations to the UI thread with consistent error handling.
/// Eliminates duplicate DispatcherQueue.TryEnqueue patterns throughout the codebase.
/// </summary>
public static class UIThreadHelper
{
    /// <summary>
    /// Executes an action on the UI thread. Returns immediately if already on UI thread.
    /// </summary>
    public static void RunOnUIThread(this DispatcherQueue dispatcher, Action action)
    {
        if (dispatcher == null)
        {
            // Fallback: run synchronously if no dispatcher
            action();
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            dispatcher.TryEnqueue(DispatcherQueuePriority.Normal, () => action());
        }
    }

    /// <summary>
    /// Executes an action on the UI thread with specified priority.
    /// </summary>
    public static void RunOnUIThread(
        this DispatcherQueue dispatcher, 
        DispatcherQueuePriority priority, 
        Action action)
    {
        if (dispatcher == null)
        {
            action();
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            action();
        }
        else
        {
            dispatcher.TryEnqueue(priority, () => action());
        }
    }

    /// <summary>
    /// Schedules an action to run on UI thread with low priority after current operations.
    /// Useful for deferred updates that don't need immediate execution.
    /// </summary>
    public static void RunOnUIThreadLowPriority(this DispatcherQueue dispatcher, Action action)
    {
        dispatcher?.TryEnqueue(DispatcherQueuePriority.Low, () => action());
    }

    /// <summary>
    /// Executes an async operation on the UI thread and awaits its completion.
    /// </summary>
    public static async Task RunOnUIThreadAsync(this DispatcherQueue dispatcher, Func<Task> asyncAction)
    {
        if (dispatcher == null)
        {
            await asyncAction();
            return;
        }

        if (dispatcher.HasThreadAccess)
        {
            await asyncAction();
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            
            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await asyncAction();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            await tcs.Task;
        }
    }

    /// <summary>
    /// Executes a function on the UI thread and returns its result.
    /// </summary>
    public static async Task<TResult> RunOnUIThreadAsync<TResult>(
        this DispatcherQueue dispatcher, 
        Func<TResult> function)
    {
        if (dispatcher == null)
        {
            return function();
        }

        if (dispatcher.HasThreadAccess)
        {
            return function();
        }

        var tcs = new TaskCompletionSource<TResult>();
        
        dispatcher.TryEnqueue(() =>
        {
            try
            {
                var result = function();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return await tcs.Task;
    }

    /// <summary>
    /// Throttles rapid UI updates by debouncing with a delay.
    /// Useful for scroll position updates, search-as-you-type, etc.
    /// </summary>
    public static async Task ThrottleUIUpdateAsync(
        this DispatcherQueue dispatcher,
        Action action,
        int delayMilliseconds = 100)
    {
        await Task.Delay(delayMilliseconds);
        dispatcher.RunOnUIThread(action);
    }
}
