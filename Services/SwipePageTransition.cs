using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.Controls;

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
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync(route, false), route, null);

    public static Task GoBackAsync(ContentPage page, SwipeDirection direction) =>
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync("..", false), null, null);

    /// <summary>Reliable back navigation for call termination, which must not be dropped during a transition.</summary>
    public static Task GoBackForCallAsync(ContentPage page) =>
        NavigateCoreAsync(page, SwipeDirection.Right, async shell => await shell.GoToAsync("..", false), null, null, waitForTurn: true);

    public static Task NavigateToHomeSectionAsync(ContentPage page, string section, SwipeDirection direction) =>
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync("//HomePage", false), "HomePage", section);

    private static async Task NavigateCoreAsync(
        ContentPage page,
        SwipeDirection direction,
        Func<Shell, Task> navigation,
        string? destinationRoute,
        string? homeSection,
        bool waitForTurn = false)
    {
        if (waitForTurn)
        {
            while (Interlocked.CompareExchange(ref _navigationInProgress, 1, 0) != 0)
                await Task.Delay(16).ConfigureAwait(false);
        }
        else if (Interlocked.CompareExchange(ref _navigationInProgress, 1, 0) != 0)
        {
            return;
        }

        var shell = Shell.Current;
        var transitionTargets = GetTransitionTargets(page);
        if (shell is null)
        {
            Interlocked.Exchange(ref _navigationInProgress, 0);
            return;
        }

        try
        {
            if (transitionTargets.Count > 0)
            {
                // Profile and Settings have larger visual trees than the home tabs.
                // Keep their exit brief and movement-led, so navigation starts before
                // a long fade can make the tap feel delayed. Other routes retain the
                // established transition that already feels smooth for chats/search/calls.
                var isAccountSection = IsAccountSectionRoute(destinationRoute);
                var exitX = direction == SwipeDirection.Left ? -28d : 28d;

                if (isAccountSection)
                {
                    await Task.WhenAll(transitionTargets.Select(target =>
                        target.TranslateToAsync(exitX * 0.72d, 0, 78, Easing.CubicOut)));
                }
                else
                {
                    await Task.WhenAll(transitionTargets.Select(target => Task.WhenAll(
                        target.TranslateToAsync(exitX, 0, 150, Easing.CubicIn),
                        target.FadeToAsync(0, 135, Easing.CubicIn))));
                }
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
            System.Diagnostics.Debug.WriteLine($"[Himo Swipe] Page transition failed: {ex.Message}");
        }
        finally
        {
            foreach (var target in transitionTargets)
            {
                target.TranslationX = 0;
                target.Opacity = 1;
            }
            Interlocked.Exchange(ref _navigationInProgress, 0);
        }
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

        var transitionTargets = GetTransitionTargets(page);
        if (transitionTargets.Count == 0)
            return;

        try
        {
            var isAccountSection = IsAccountSectionPage(page);
            var offset = isAccountSection ? 28d : 48d;
            var duration = isAccountSection ? 125u : 190u;

            foreach (var target in transitionTargets)
            {
                target.TranslationX = direction == SwipeDirection.Left ? offset : -offset;
                target.Opacity = 0;
            }

            await Task.WhenAll(transitionTargets.Select(target => Task.WhenAll(
                target.TranslateToAsync(0, 0, duration, Easing.CubicOut),
                target.FadeToAsync(1, duration, Easing.CubicOut))));
        }
        catch (Exception ex)
        {
            foreach (var target in transitionTargets)
            {
                target.TranslationX = 0;
                target.Opacity = 1;
            }
            System.Diagnostics.Debug.WriteLine($"[Himo Swipe] Incoming animation failed: {ex.Message}");
        }
    }

    private static bool IsAccountSectionRoute(string? route) =>
        string.Equals(route, "profile", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(route, "settings", StringComparison.OrdinalIgnoreCase);

    private static bool IsAccountSectionPage(ContentPage page) =>
        page.GetType().Name is "ProfilePage" or "SettingsPage";

    /// <summary>
    /// For the five primary tabs, keep the bottom bar stationary and animate only
    /// the header plus the main content. Other routes retain a full-page transition.
    /// </summary>
    private static List<VisualElement> GetTransitionTargets(ContentPage page)
    {
        string[] names =
        {
            "HomeHeader", "ChatSearchCard", "MainSectionHost",
            "ProfileHeader", "ProfileScrollContent",
            "SettingsHeader", "SettingsScrollContent"
        };

        var targets = new List<VisualElement>();
        foreach (var name in names)
        {
            var target = page.FindByName<VisualElement>(name);
            if (target is not null && target.IsVisible && !targets.Contains(target))
                targets.Add(target);
        }

        // Chat/call/search and secondary pages keep their established full-page motion.
        if (targets.Count == 0 && page.Content is VisualElement content)
            targets.Add(content);

        return targets;
    }
}

public interface IHomeSwipeSectionHost
{
    void SelectSectionAfterSwipeReturn(string section);
}
