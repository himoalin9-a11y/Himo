using Himo.Services;
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

    public App(AccountService account, HimoApiClient api, ChatService chat, INotificationService notifications, AppLockService appLock, PushNotificationManager pushNotifications)
    {
        InitializeComponent();
        UserAppTheme = Preferences.Default.Get(ThemeKey, true) ? AppTheme.Dark : AppTheme.Light;
        _account = account;
        _api = api;
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;
        _pushNotifications = pushNotifications;
        _sessionUnlocked = !_appLock.IsEnabled;
        _api.SessionExpired += OnSessionExpired;
        // Himo owns its palette and sets UserAppTheme explicitly; rebuilding every
        // resource on RequestedThemeChanged causes unnecessary UI-wide work.
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
        UserAppTheme = enabled ? AppTheme.Dark : AppTheme.Light;
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


    private async Task RestorePersistedSessionAsync(Window window)
    {
        try
        {
            await _api.TokenInitialization.ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (!_account.IsSignedIn || !_api.HasToken)
                {
                    _sessionUnlocked = false;
                    window.Page = new Views.LoginPage(_account, _api);
                    return;
                }

                try
                {
                    if (_appLock.IsEnabled && !_sessionUnlocked)
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
                                System.Diagnostics.Debug.WriteLine($"AppShell recovery after session restore failed: {ex}");
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
                    else
                    {
                        _sessionUnlocked = true;
                        window.Page = new AppShell();
#if ANDROID
                        _ = RegisterCurrentPushTokenAsync();
#endif
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Persisted session restore failed: {ex}");
                    _sessionUnlocked = false;
                    window.Page = new Views.LoginPage(_account, _api);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Persisted token initialization failed: {ex}");
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _sessionUnlocked = false;
                window.Page = new Views.LoginPage(_account, _api);
            });
        }
    }

    private static readonly TimeSpan WelcomeScreenDuration = TimeSpan.FromSeconds(2);

    private ContentPage CreateWelcomePage()
    {
        // Use the approved Himo welcome artwork as the in-app two-second splash.
        // The asset is included as a MauiImage under Resources/Images.
        const string splashBackground = "#060B2D";

        return new ContentPage
        {
            Title = "Himo",
            Padding = 0,
            BackgroundColor = Color.FromArgb(splashBackground),
            FlowDirection = FlowDirection.LeftToRight,
            Content = new Grid
            {
                Padding = 0,
                BackgroundColor = Color.FromArgb(splashBackground),
                Children =
                {
                    new Image
                    {
                        Source = ImageSource.FromFile("himo_welcome.png"),
                        Aspect = Aspect.AspectFill,
                        HorizontalOptions = LayoutOptions.Fill,
                        VerticalOptions = LayoutOptions.Fill
                    }
                }
            }
        };
    }

    private Window CreateWindowWithWelcome(Page destination)
    {
        var welcome = CreateWelcomePage();
        var window = new Window(welcome);
        _ = RevealPageAfterWelcomeAsync(window, welcome, destination);
        return window;
    }

    private async Task RevealPageAfterWelcomeAsync(Window window, Page welcome, Page destination)
    {
        // Keep exactly one in-app welcome screen, then navigate directly to the
        // already-selected destination. AppShell owns its own delayed page prewarm;
        // do not run a second synchronous prewarm from the splash timer because it
        // can block the UI thread and make the welcome screen appear longer than 2s.
        await Task.Delay(WelcomeScreenDuration).ConfigureAwait(false);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (ReferenceEquals(window.Page, welcome))
                window.Page = destination;
        });
    }

    private async Task ShowWelcomeThenRestoreAsync(Window window, Page welcome)
    {
        // Keep a single branded welcome screen while session/token restoration runs.
        // Android's platform splash is handled separately by Resources/Splash/splash.png.
        var minimumWelcomeDuration = Task.Delay(WelcomeScreenDuration);

        try
        {
            await _api.TokenInitialization.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // RestorePersistedSessionAsync handles the failed-token path and shows LoginPage.
            System.Diagnostics.Debug.WriteLine($"Token initialization during welcome failed: {ex.Message}");
        }

        await minimumWelcomeDuration.ConfigureAwait(false);

        // Do not override the page if another navigation path has already taken ownership.
        var stillShowingWelcome = await MainThread.InvokeOnMainThreadAsync(
            () => ReferenceEquals(window.Page, welcome));
        if (stillShowingWelcome)
            await RestorePersistedSessionAsync(window).ConfigureAwait(false);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Startup is deliberately defensive: a stale/corrupt session or an
        // optional page/plugin failure must never terminate the whole app.
        try
        {
            var accountSignedIn = false;
            var tokenAvailable = false;

            try
            {
                accountSignedIn = _account.IsSignedIn;
                tokenAvailable = _api.HasToken;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Session state could not be restored: {ex}");
                accountSignedIn = false;
                tokenAvailable = false;
            }

            // SecureStorage is asynchronous. On a cold Android start the persisted
            // AccountService state can be available a moment before the bearer token.
            // Never treat that temporary mismatch as a logout. Wait for token
            // initialization and then restore the existing session.
            if (accountSignedIn && !_api.TokenInitialization.IsCompleted)
            {
                var welcome = CreateWelcomePage();
                var welcomeWindow = new Window(welcome);
                _ = ShowWelcomeThenRestoreAsync(welcomeWindow, welcome);
                return welcomeWindow;
            }

            Page page;
            if (accountSignedIn && tokenAvailable)
            {
                try
                {
                    if (_appLock.IsEnabled && !_sessionUnlocked)
                    {
                        page = new Views.AppLockPage(_appLock, async () =>
                        {
                            _sessionUnlocked = true;
                            var window = Windows.FirstOrDefault();
                            if (window is not null)
                            {
                                try
                                {
                                    window.Page = new AppShell();
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"AppShell recovery after unlock failed: {ex}");
                                    try { _account.SignOut(); } catch { }
                                    try { _api.ClearToken(); } catch { }
                                    _sessionUnlocked = false;
                                    window.Page = new Views.LoginPage(_account, _api);
                                }
                            }
                            await Task.CompletedTask;
#if ANDROID
                            _ = RegisterCurrentPushTokenAsync();
                            MainActivity.TryNavigateToPendingConversation();
#endif
                        });
                    }
                    else
                    {
                        _sessionUnlocked = true;
                        page = new AppShell();
#if ANDROID
                        _ = RegisterCurrentPushTokenAsync();
#endif
                    }
                }
                catch (Exception ex)
                {
                    // A broken persisted session/AppShell must not cause an
                    // Android cold-start crash loop. Reset only the local
                    // session state and return to the known-good Login page.
                    System.Diagnostics.Debug.WriteLine($"Recovered from startup page failure: {ex}");
                    try { _account.SignOut(); } catch { }
                    try { _api.ClearToken(); } catch { }
                    _sessionUnlocked = false;
                    page = new Views.LoginPage(_account, _api);
                }
            }
            else
            {
                _sessionUnlocked = false;
                page = new Views.LoginPage(_account, _api);
            }

            return CreateWindowWithWelcome(page);
        }
        catch (Exception ex)
        {
            // Last-resort recovery for any startup dependency. This path keeps
            // the process alive and presents a simple, usable login surface.
            System.Diagnostics.Debug.WriteLine($"Fatal startup recovery path: {ex}");
            try
            {
                _sessionUnlocked = false;
                return CreateWindowWithWelcome(new Views.LoginPage(_account, _api));
            }
            catch (Exception loginEx)
            {
                System.Diagnostics.Debug.WriteLine($"Login page recovery failed: {loginEx}");
                return new Window(new ContentPage
                {
                    BackgroundColor = Color.FromArgb("#16052F"),
                    Content = new VerticalStackLayout
                    {
                        Padding = 28,
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "Himo", FontSize = 34, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center },
                            new Label { Text = "تعذر استعادة الجلسة. أعد فتح التطبيق أو سجّل الدخول من جديد.", FontSize = 15, TextColor = Color.FromArgb("#E9DEFF"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 14) }
                        }
                    }
                });
            }
        }
    }
}
