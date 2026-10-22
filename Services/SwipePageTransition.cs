using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Himo.Views;

namespace Himo.Services;

/// <summary>
/// Provides one consistent horizontal transition for the home sections,
/// profile page, and settings page.
/// </summary>
public static class SwipePageTransition
{
    private static readonly object StateLock = new();
    private static SwipeDirection? _pendingIncomingDirection;
    private static string? _pendingHomeSection;
    private static bool _settingsWasEnteredFromProfile;
    private static int _navigationInProgress;

    /// <summary>True when Settings was pushed from Profile, so its back/right gesture returns there.</summary>
    public static bool SettingsWasEnteredFromProfile
    {
        get { lock (StateLock) return _settingsWasEnteredFromProfile; }
    }

    public static Task NavigateAsync(ContentPage page, string route, SwipeDirection direction) =>
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync(route, false), route, null, waitForTurn: true);

    public static Task GoBackAsync(ContentPage page, SwipeDirection direction) =>
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync("..", false), null, null, waitForTurn: true);

    /// <summary>Reliable back navigation for call termination, which must not be dropped during a transition.</summary>
    public static Task GoBackForCallAsync(ContentPage page) =>
        NavigateCoreAsync(page, SwipeDirection.Right, async shell => await shell.GoToAsync("..", false), null, null, waitForTurn: true);

    public static Task NavigateToHomeSectionAsync(ContentPage page, string section, SwipeDirection direction) =>
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync("//HomePage", false), "HomePage", section, waitForTurn: true);

    private static async Task NavigateCoreAsync(
        ContentPage page,
        SwipeDirection direction,
        Func<Shell, Task> navigation,
        string? destinationRoute,
        string? homeSection,
        bool waitForTurn = false)
    {
        // All Shell navigation and visual animation must run on the UI thread,
        // including call-completion callbacks that may originate on a worker thread.
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(
                () => NavigateCoreAsync(page, direction, navigation, destinationRoute, homeSection, waitForTurn));
            return;
        }

        // Do not silently drop navigation when another short transition is in
        // progress. Previously QR and Share taps could appear to do nothing because
        // the transition helper returned before Shell was asked to open the route.
        var wasQueued = false;
        if (waitForTurn)
        {
            while (Interlocked.CompareExchange(ref _navigationInProgress, 1, 0) != 0)
            {
                wasQueued = true;
                // Preserve the UI synchronization context; navigation and animation
                // must remain on the MAUI UI thread after waiting for the active route.
                await Task.Delay(16);
            }
        }
        else if (Interlocked.CompareExchange(ref _navigationInProgress, 1, 0) != 0)
        {
            return;
        }

        var shell = Shell.Current;
        var content = page.Content;
        if (shell is null)
        {
            Interlocked.Exchange(ref _navigationInProgress, 0);
            return;
        }

        try
        {
            // If queued behind another route, ignore only stale taps from a page that
            // has already been left. The first request is always allowed to execute;
            // this prevents share/QR routes from being suppressed by a busy transition.
            if (wasQueued && !IsActiveSourcePage(shell, page))
                return;

            // Settings/Profile contain long, deeply nested visual trees. Fading and
            // translating the entire hierarchy creates costly Android off-screen
            // compositing on some GPUs, which matches the multi-second frame stalls
            // in the device log. Keep navigation functional, but skip page-wide
            // animations when entering/leaving either heavy page.
            var skipHeavyPageAnimation =
                page is ProfilePage or SettingsPage ||
                string.Equals(destinationRoute, "profile", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(destinationRoute, "settings", StringComparison.OrdinalIgnoreCase);

            if (content is not null && !skipHeavyPageAnimation)
            {
                var exitX = direction == SwipeDirection.Left ? -34d : 34d;
                await Task.WhenAll(
                    content.TranslateToAsync(exitX, 0, 145, Easing.CubicIn),
                    content.FadeToAsync(0.80, 145, Easing.CubicIn));
            }

            lock (StateLock)
            {
                _pendingIncomingDirection = direction;
                _pendingHomeSection = homeSection;
                if (string.Equals(destinationRoute, "settings", StringComparison.OrdinalIgnoreCase))
                    _settingsWasEnteredFromProfile = page.GetType().Name == "ProfilePage";
            }

            await navigation(shell);
        }
        catch (Exception ex)
        {
            lock (StateLock)
            {
                _pendingIncomingDirection = null;
                _pendingHomeSection = null;
            }
            System.Diagnostics.Debug.WriteLine($"[Himo Swipe] Page transition failed (source={page.GetType().Name}, route={destinationRoute ?? "back"}): {ex}");
            try
            {
                await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(
                    () => page.DisplayAlertAsync("تعذر فتح الصفحة", "لم يتمكن Himo من فتح الصفحة المطلوبة. حاول مرة أخرى.", "حسنًا"));
            }
            catch { /* Keep navigation recovery from causing a second UI failure. */ }
        }
        finally
        {
            if (content is not null)
            {
                content.TranslationX = 0;
                content.Opacity = 1;
            }
            Interlocked.Exchange(ref _navigationInProgress, 0);
        }
    }


    private static bool IsActiveSourcePage(Shell shell, ContentPage page)
    {
        if (ReferenceEquals(shell.CurrentPage, page))
            return true;

        var stack = shell.Navigation?.NavigationStack;
        return stack is { Count: > 0 } && ReferenceEquals(stack[^1], page);
    }

    /// <summary>Called from OnAppearing to animate a page in after a swipe or tab action.</summary>
    public static async Task AnimateIncomingAsync(ContentPage page, SwipeDirection? fallbackDirection = null)
    {
        SwipeDirection? direction;
        string? homeSection;
        lock (StateLock)
        {
            direction = _pendingIncomingDirection ?? fallbackDirection;
            homeSection = _pendingHomeSection;
            _pendingIncomingDirection = null;
            _pendingHomeSection = null;
        }

        if (direction is null)
            return;

        if (homeSection is not null && page is IHomeSwipeSectionHost host)
            host.SelectSectionAfterSwipeReturn(homeSection);

        var content = page.Content;
        if (content is null)
            return;

        // Do not animate the full settings/profile visual tree. These pages contain
        // many Borders, images and controls; the fade forces an expensive full-tree
        // redraw on affected Android devices.
        if (page is ProfilePage or SettingsPage)
        {
            content.TranslationX = 0;
            content.Opacity = 1;
            return;
        }

        content.TranslationX = direction == SwipeDirection.Left ? 44 : -44;
        content.Opacity = 0.78;
        try
        {
            await Task.WhenAll(
                content.TranslateToAsync(0, 0, 190, Easing.CubicOut),
                content.FadeToAsync(1, 190, Easing.CubicOut));
        }
        catch (Exception ex)
        {
            content.TranslationX = 0;
            content.Opacity = 1;
            System.Diagnostics.Debug.WriteLine($"[Himo Swipe] Incoming animation failed: {ex.Message}");
        }
    }
}

public interface IHomeSwipeSectionHost
{
    void SelectSectionAfterSwipeReturn(string section);
}
