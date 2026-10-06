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
        UserAppTheme = Preferences.Default.Get(ThemeKey, false) ? AppTheme.Dark : AppTheme.Light;
        _account = account;
        _api = api;
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;
        _pushNotifications = pushNotifications;
        _sessionUnlocked = !_appLock.IsEnabled;
        _api.SessionExpired += OnSessionExpired;
        RequestedThemeChanged += (_, _) => ApplyThemeResources();
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

    public bool IsDarkModeEnabled => Preferences.Default.Get(ThemeKey, false);

    public void SetDarkMode(bool enabled)
    {
        Preferences.Default.Set(ThemeKey, enabled);
        UserAppTheme = enabled ? AppTheme.Dark : AppTheme.Light;
        ApplyThemeResources();
    }

    private void ApplyThemeResources()
    {
        var resources = Resources;
        var dark = IsDarkModeEnabled;
        resources["HimoBackground"] = Color.FromArgb(dark ? "#081526" : "#F5F8FC");
        resources["HimoPrimary"] = Color.FromArgb(dark ? "#F5F9FF" : "#102A43");
        resources["HimoMuted"] = Color.FromArgb(dark ? "#AFC3DA" : "#6B7C93");
        resources["HimoCard"] = Color.FromArgb(dark ? "#102238" : "#FFFFFF");
        resources["HimoAccent"] = Color.FromArgb(dark ? "#3F9BFF" : "#1478F2");
        resources["HimoBorder"] = Color.FromArgb(dark ? "#24425F" : "#DCE6F0");
        resources["HimoBubbleMine"] = Color.FromArgb(dark ? "#1478F2" : "#1478F2");
        resources["HimoBubbleOther"] = Color.FromArgb(dark ? "#182027" : "#FFFFFF");
        resources["HimoAvatar"] = Color.FromArgb(dark ? "#173A5E" : "#DCEEFF");
        resources["HimoSoft"] = Color.FromArgb(dark ? "#14385C" : "#EAF4FF");
        resources["HimoSuccess"] = Color.FromArgb(dark ? "#62D7A4" : "#1F9D68");
        resources["HimoDanger"] = Color.FromArgb(dark ? "#FF8A80" : "#C62828");
        resources["HimoHeader"] = Color.FromArgb(dark ? "#214F8D" : "#632CCB");
        resources["HimoHeaderSoft"] = Color.FromArgb(dark ? "#285D9F" : "#6F3BD2");
        resources["HimoHeaderTextMuted"] = Color.FromArgb(dark ? "#C9DBEF" : "#E8DDF8");
        resources["HimoTextSecondary"] = Color.FromArgb(dark ? "#B2C4D8" : "#766D82");
        resources["HimoAvatarSoft"] = Color.FromArgb(dark ? "#1A3D61" : "#EEE7FA");
        resources["HimoNavMuted"] = Color.FromArgb(dark ? "#8EA8C2" : "#8D8498");
        resources["HimoDangerSoft"] = Color.FromArgb(dark ? "#48222A" : "#FDECEC");
        resources["HimoSurfaceElevated"] = Color.FromArgb(dark ? "#142B42" : "#FFFFFF");
        resources["HimoDivider"] = Color.FromArgb(dark ? "#24425F" : "#ECE7F4");
        resources["HimoFocus"] = Color.FromArgb(dark ? "#6DA8E8" : "#B99AEF");
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
                var restoringPage = new ContentPage
                {
                    BackgroundColor = Color.FromArgb("#F5F8FC"),
                    Content = new ActivityIndicator
                    {
                        IsRunning = true,
                        HorizontalOptions = LayoutOptions.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                };

                var restoringWindow = new Window(restoringPage);
                _ = RestorePersistedSessionAsync(restoringWindow);
                return restoringWindow;
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

            return new Window(page);
        }
        catch (Exception ex)
        {
            // Last-resort recovery for any startup dependency. This path keeps
            // the process alive and presents a simple, usable login surface.
            System.Diagnostics.Debug.WriteLine($"Fatal startup recovery path: {ex}");
            try
            {
                _sessionUnlocked = false;
                return new Window(new Views.LoginPage(_account, _api));
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
