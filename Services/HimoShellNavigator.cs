using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Himo.Services;

/// <summary>
/// Serializes Shell route changes so a rapid tap on a card/back button cannot
/// start a second navigation while the first Shell transition is still running.
/// This deliberately does not own a second navigation stack: Shell remains the
/// single source of truth for route history.
/// </summary>
public static class HimoShellNavigator
{
    private static int _navigationInProgress;

    public static async Task<bool> NavigateAsync(string route, bool animate = false)
    {
        if (string.IsNullOrWhiteSpace(route))
            throw new ArgumentException("A Shell route is required.", nameof(route));

        // Treat rapid repeated taps as one action rather than queueing duplicate
        // pages and accidentally popping back to the root on the next Back.
        if (Interlocked.CompareExchange(ref _navigationInProgress, 1, 0) != 0)
            return false;

        try
        {
            var shell = Shell.Current;
            if (shell is null)
                return false;

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Shell could have been replaced while the request was waiting
                // for the UI thread (for example after an app-lock flow).
                var currentShell = Shell.Current;
                if (!ReferenceEquals(currentShell, shell))
                    throw new InvalidOperationException("The active Shell changed before navigation completed.");

                await shell.GoToAsync(route, animate);
            });

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Navigation] Route '{route}' failed: {ex}");
            throw;
        }
        finally
        {
            Interlocked.Exchange(ref _navigationInProgress, 0);
        }
    }

    public static Task<bool> BackAsync(bool animate = false) => NavigateAsync("..", animate);
}
