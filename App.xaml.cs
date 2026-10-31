using Himo.Services;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Maui.Controls;
namespace Himo;

public partial class App : Application
{
    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private const string ThemeKey = "himo_dark_mode";
    private int _handlingSessionExpiry;
    private bool _sessionUnlocked;
    private DateTime _backgroundedAtUtc;
    private readonly AppLockService _appLock;
    private readonly PushNotificationManager _pushNotifications;
#if DEBUG
    private static int _loggedNullReferenceCount;

    private static void LogFirstChanceNullReference(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is not NullReferenceException ex)
            return;

        // FirstChanceException may fire before Exception.StackTrace has been populated.
        // Capture the live managed stack here so the Output window includes the caller.
        if (Interlocked.Increment(ref _loggedNullReferenceCount) <= 6)
        {
            Debug.WriteLine($"[Himo NullReference Diagnostic] {ex.GetType().FullName}: {ex.Message}");
            Debug.WriteLine($"[Himo NullReference Diagnostic] TargetSite: {ex.TargetSite}");
            Debug.WriteLine("[Himo NullReference Diagnostic] Live managed stack at throw time:");
            Debug.WriteLine(new StackTrace(1, true).ToString());
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                Debug.WriteLine("[Himo NullReference Diagnostic] Exception stack:\n" + ex.StackTrace);
        }
    }

    private static void LogUnhandledException(object? sender, UnhandledExceptionEventArgs args)
    {
        Debug.WriteLine("[Himo Unhandled Exception] IsTerminating=" + args.IsTerminating);
        Debug.WriteLine(args.ExceptionObject?.ToString() ?? "Unknown non-Exception failure");
    }

    private static void LogUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Debug.WriteLine("[Himo Unobserved Task Exception]");
        Debug.WriteLine(args.Exception.ToString());
    }
#endif

    public App(AccountService account, HimoApiClient api, ChatService chat, INotificationService notifications, AppLockService appLock, PushNotificationManager pushNotifications)
    {
        InitializeComponent();
#if DEBUG
        AppDomain.CurrentDomain.FirstChanceException -= LogFirstChanceNullReference;
        AppDomain.CurrentDomain.FirstChanceException += LogFirstChanceNullReference;
        AppDomain.CurrentDomain.UnhandledException -= LogUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += LogUnhandledException;
        TaskScheduler.UnobservedTaskException -= LogUnobservedTaskException;
        TaskScheduler.UnobservedTaskException += LogUnobservedTaskException;
#endif
        // Do not set UserAppTheme during activity creation. Himo owns explicit palette
        // resources; native night-mode changes here can trigger AppCompat configuration
        // queries before the selected launcher component is fully resolved.
        _account = account;
        _api = api;
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;
        _pushNotifications = pushNotifications;
        _sessionUnlocked = !_appLock.IsEnabled;
        _api.SessionExpired += OnSessionExpired;
        // Himo controls its interface through explicit palette resources. Avoid native
        // RequestedTheme changes during app startup; they can force AppCompat to re-resolve
        // the launcher component and recreate the first page.
        ApplyThemeResources();
        _ = SynchronizeAccountIdentityAsync();
    }


    private async Task SynchronizeAccountIdentityAsync()
    {
        if (!_account.IsSignedIn || !_api.HasToken || _account.CurrentAccount?.UserId != Guid.Empty) return;
        try
        {
            var profile = await _api.GetMyProfileAsync();
            _account.SetUserId(profile.UserId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Account identity sync skipped: {ex.Message}");
        }
    }

    public void MarkSessionUnlocked() => _sessionUnlocked = true;

    protected override void OnStart()
    {
        base.OnStart();
#if ANDROID
        _pushNotifications.SetAppForeground(true);
        _ = RegisterCurrentPushTokenAsync();
#endif
    }

    protected override void OnSleep()
    {
        base.OnSleep();
        _backgroundedAtUtc = DateTime.UtcNow;
#if ANDROID
        _pushNotifications.SetAppForeground(false);
#endif
    }

    protected override void OnResume()
    {
        base.OnResume();
#if ANDROID
        _pushNotifications.SetAppForeground(true);
        _ = RegisterCurrentPushTokenAsync();
#endif
        if (!_appLock.IsEnabled || !_account.IsSignedIn || !_api.HasToken) return;

        if (_backgroundedAtUtc != default && DateTime.UtcNow - _backgroundedAtUtc >= TimeSpan.FromSeconds(30))
        {
            _sessionUnlocked = false;
            var window = Windows.FirstOrDefault();
            if (window is not null)
                ShowAppLock(window);
        }
    }

    private void ShowAppLock(Window window)
    {
        try
        {
            window.Page = new Views.AppLockPage(_appLock, async () =>
            {
                _sessionUnlocked = true;
                try
                {
                    window.Page = new AppShell();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AppShell recovery after app-lock failed: {ex}");
                    try { _account.SignOut(); } catch { }
                    try { _api.ClearToken(); } catch { }
                    _sessionUnlocked = false;
                    window.Page = new Views.LoginPage(_account, _api);
                }
                await Task.CompletedTask;
#if ANDROID
                _ = RegisterCurrentPushTokenAsync();
                MainActivity.TryNavigateToPendingConversation();
#endif
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App-lock startup recovery failed: {ex}");
            _sessionUnlocked = false;
            try { _account.SignOut(); } catch { }
            try { _api.ClearToken(); } catch { }
            window.Page = new Views.LoginPage(_account, _api);
        }
    }

    public bool IsDarkModeEnabled => Preferences.Default.Get(ThemeKey, true);

    public void SetDarkMode(bool enabled)
    {
        Preferences.Default.Set(ThemeKey, enabled);
        // Himo uses an explicit palette. Avoid changing native Android UiMode during
        // an active page interaction; the selected mode is applied again on next startup.
        ApplyThemeResources();
    }

    private void ApplyThemeResources()
    {
        // Keep both appearance preferences inside Himo's dark neon-violet identity.
        // The switch adjusts depth/contrast without reverting individual screens to blue/white.
        var dark = IsDarkModeEnabled;
        var resources = Resources;
        resources["HimoBackground"] = Color.FromArgb(dark ? "#07030E" : "#10071A");
        resources["HimoPrimary"] = Color.FromArgb("#FBF6FF");
        resources["HimoMuted"] = Color.FromArgb(dark ? "#B8A1D4" : "#C3B0DB");
        resources["HimoCard"] = Color.FromArgb(dark ? "#140820" : "#1B0D2D");
        resources["HimoInput"] = Color.FromArgb(dark ? "#17092A" : "#201033");
        resources["HimoAccent"] = Color.FromArgb("#A12AFF");
        resources["HimoAccentDark"] = Color.FromArgb("#6517C8");
        resources["HimoBorder"] = Color.FromArgb(dark ? "#4A216F" : "#5B2B80");
        resources["HimoBubbleMine"] = Color.FromArgb("#9228ED");
        resources["HimoBubbleOther"] = Color.FromArgb(dark ? "#21102F" : "#2B1740");
        resources["HimoAvatar"] = Color.FromArgb("#2B1646");
        resources["HimoSoft"] = Color.FromArgb(dark ? "#211039" : "#291543");
        resources["HimoSuccess"] = Color.FromArgb("#31E7A1");
        resources["HimoDanger"] = Color.FromArgb("#FF4678");
        resources["HimoWarning"] = Color.FromArgb("#FFC56C");
        resources["HimoHeader"] = Color.FromArgb("#12061F");
        resources["HimoHeaderSoft"] = Color.FromArgb("#250B3D");
        resources["HimoHeaderTextMuted"] = Color.FromArgb("#D9B7FF");
        resources["HimoTextSecondary"] = Color.FromArgb("#B7A2D2");
        resources["HimoAvatarSoft"] = Color.FromArgb("#2A1643");
        resources["HimoNavMuted"] = Color.FromArgb("#A48AC6");
        resources["HimoDangerSoft"] = Color.FromArgb("#41162F");
        resources["HimoSurfaceElevated"] = Color.FromArgb(dark ? "#1B0C2D" : "#24113A");
        resources["HimoSurface"] = Color.FromArgb(dark ? "#140820" : "#1B0D2D");
        resources["HimoDivider"] = Color.FromArgb("#3A2053");
        resources["HimoFocus"] = Color.FromArgb("#EF73FF");
        resources["HimoGlow"] = Color.FromArgb("#B92EFF");
        resources["HimoGlowSoft"] = Color.FromArgb("#6627FF");
        resources["HimoPanel"] = Color.FromArgb(dark ? "#170A28" : "#1C0C2E");
        resources["HimoPanelRaised"] = Color.FromArgb("#24113A");
        resources["HimoNeonBorder"] = Color.FromArgb("#7427B6");

        // Apply a user-selected interface palette after baseline colors are set.
        // This keeps the selection active after app restart and OS theme changes.
        HimoAppearanceManager.ApplyStoredPalette(resources);
    }

#if ANDROID
    public Task RegisterCurrentPushTokenAsync()
        => _pushNotifications.InitializeAsync();

    public Task UnregisterCurrentPushTokenAsync()
        => _pushNotifications.UnregisterCurrentTokenAsync();
#endif

    private void OnSessionExpired(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _handlingSessionExpiry, 1) != 0) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
#if ANDROID
                try { await UnregisterCurrentPushTokenAsync(); } catch { }
#endif
                _account.SignOut();
                _chat.ClearAll();
                try { await _notifications.ClearAllAsync(); } catch { }

                var window = Windows.FirstOrDefault();
                if (window is not null)
                    window.Page = new Views.LoginPage(_account, _api);
            }
            finally
            {
                Interlocked.Exchange(ref _handlingSessionExpiry, 0);
            }
        });
    }


    /// <summary>
    /// Creates the unlock page without adding an intermediate welcome page to the
    /// startup pipeline. The Android native splash stays visible until this first
    /// real screen is ready, avoiding splash -> welcome -> blank -> destination.
    /// </summary>
    private Page CreateStartupAppLockPage(Window window)
    {
        return new Views.AppLockPage(_appLock, async () =>
        {
            _sessionUnlocked = true;
            try
            {
                window.Page = new AppShell();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AppShell recovery after startup unlock failed: {ex}");
                _sessionUnlocked = false;
                window.Page = new Views.LoginPage(_account, _api);
            }
#if ANDROID
            _ = RegisterCurrentPushTokenAsync();
            MainActivity.TryNavigateToPendingConversation();
#endif
            await Task.CompletedTask;
        });
    }

    private async Task ValidateStartupTokenAsync(Window window, Page initialDestination)
    {
        try
        {
            await _api.TokenInitialization.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Startup] Token initialization failed: {ex.Message}");
        }

        if (_api.HasToken)
        {
#if ANDROID
            _ = RegisterCurrentPushTokenAsync();
#endif
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ReferenceEquals(window.Page, initialDestination) && _account.IsSignedIn)
            {
                _sessionUnlocked = false;
                window.Page = new Views.LoginPage(_account, _api);
            }
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Create the native Window once, then assign the first real MAUI page before
        // returning it. The temporary page is never drawn and is not a second splash.
        var window = new Window(new ContentPage
        {
            BackgroundColor = Color.FromArgb("#10071A")
        });

        try
        {
            // First-run users see the native MAUI flying-saucer launch scene.
            // The animation uses GraphicsView and remains independent of WebView/WebGL.
            if (!_account.IsSignedIn)
            {
                _sessionUnlocked = false;
                window.Page = new Views.LoginPage(_account, _api);
                return window;
            }

            Page initialDestination;
            if (_appLock.IsEnabled && !_sessionUnlocked)
            {
                initialDestination = CreateStartupAppLockPage(window);
            }
            else
            {
                _sessionUnlocked = true;
                initialDestination = new AppShell();
            }

            window.Page = initialDestination;

            // Token restoration/validation remains asynchronous and does not hold
            // the native splash or delay first-page creation.
            _ = ValidateStartupTokenAsync(window, initialDestination);
            System.Diagnostics.Debug.WriteLine($"[Himo Startup] First page created directly: {initialDestination.GetType().Name}");
            return window;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Startup] Initial page creation failed: {ex}");
            window.Page = new ContentPage
            {
                Title = "Himo",
                BackgroundColor = Color.FromArgb("#10071A"),
                Content = new VerticalStackLayout
                {
                    Padding = 28,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label { Text = "Himo", FontSize = 34, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = "تعذر فتح التطبيق. يرجى إعادة المحاولة.", FontSize = 15, TextColor = Color.FromArgb("#E9DEFF"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14) }
                    }
                }
            };
            return window;
        }
    }

}
