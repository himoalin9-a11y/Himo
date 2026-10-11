using System;
using System.Threading;
using System.Threading.Tasks;
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

    public static Task NavigateToHomeSectionAsync(ContentPage page, string section, SwipeDirection direction) =>
        NavigateCoreAsync(page, direction, async shell => await shell.GoToAsync("//HomePage", false), "HomePage", section);

    private static async Task NavigateCoreAsync(
        ContentPage page,
        SwipeDirection direction,
        Func<Shell, Task> navigation,
        string? destinationRoute,
        string? homeSection)
    {
        if (Interlocked.CompareExchange(ref _navigationInProgress, 1, 0) != 0)
            return;

        var shell = Shell.Current;
        var content = page.Content;
        if (shell is null)
        {
            Interlocked.Exchange(ref _navigationInProgress, 0);
            return;
        }

        try
        {
            if (content is not null)
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
            System.Diagnostics.Debug.WriteLine($"[Himo Swipe] Page transition failed: {ex.Message}");
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

    /// <summary>Called from OnAppearing to animate a page in after a swipe or tab action.</summary>
    public static async Task AnimateIncomingAsync(ContentPage page)
    {
        SwipeDirection? direction;
        string? homeSection;
        lock (StateLock)
        {
            direction = _pendingIncomingDirection;
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
