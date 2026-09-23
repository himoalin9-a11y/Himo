using Himo.Services;
using Microsoft.Maui.Controls;
#if ANDROID
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif
namespace Himo;

public partial class App : Application
{
    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private const string ThemeKey = "himo_dark_mode";
    private const string PushTokenKey = "himo_registered_fcm_token";
    private const string PendingPushTokenCleanupKey = "himo_pending_fcm_token_cleanup";
    private int _handlingSessionExpiry;
    private int _pushRegistrationInProgress;
    private bool _sessionUnlocked;
    private DateTime _backgroundedAtUtc;
    private readonly AppLockService _appLock;

    public App(AccountService account, HimoApiClient api, ChatService chat, INotificationService notifications, AppLockService appLock)
    {
        InitializeComponent();
        UserAppTheme = Preferences.Default.Get(ThemeKey, false) ? AppTheme.Dark : AppTheme.Light;
        _account = account;
        _api = api;
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;
        _sessionUnlocked = !_appLock.IsEnabled;
        _api.SessionExpired += OnSessionExpired;
        RequestedThemeChanged += (_, _) => ApplyThemeResources();
        ApplyThemeResources();
#if ANDROID
        // Firebase event registration must never be allowed to prevent the MAUI
        // window from being created. Some Android/plugin states can initialize
        // the messaging service later than the application.
        try
        {
            CrossFirebaseCloudMessaging.Current.TokenChanged += OnFcmTokenChanged;
            CrossFirebaseCloudMessaging.Current.NotificationTapped += OnFcmNotificationTapped;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Firebase event registration skipped during startup: {ex}");
        }
#endif
    }


    public void MarkSessionUnlocked() => _sessionUnlocked = true;

    protected override void OnSleep()
    {
        base.OnSleep();
        _backgroundedAtUtc = DateTime.UtcNow;
    }

    protected override void OnResume()
    {
        base.OnResume();
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
    }

#if ANDROID
    public async Task RegisterCurrentPushTokenAsync()
    {
        if (!_api.HasToken) return;
        if (Interlocked.Exchange(ref _pushRegistrationInProgress, 1) != 0) return;

        try
        {
            await CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
            var token = await CrossFirebaseCloudMessaging.Current.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
                await RegisterPushTokenWithRetryAsync(token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FCM token retrieval failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref _pushRegistrationInProgress, 0);
        }
    }

    private async Task RegisterPushTokenWithRetryAsync(string token)
    {
        if (!_api.HasToken || string.IsNullOrWhiteSpace(token)) return;

        var registeredToken = Preferences.Default.Get(PushTokenKey, string.Empty);
        var pendingCleanupToken = Preferences.Default.Get(PendingPushTokenCleanupKey, string.Empty);
        if (string.Equals(registeredToken, token, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(pendingCleanupToken))
            return;

        // Register the current token before removing the previous one. This
        // ordering avoids a gap where a temporary API failure could leave the
        // account with no usable push token at all.
        var registrationSucceeded = false;

        // A device may start the app before the LAN/API is reachable. Retry a
        // few times so the current FCM token is eventually registered without
        // requiring the user to sign out, sign in, or restart the app.
        for (var attempt = 1; attempt <= 3 && _api.HasToken; attempt++)
        {
            try
            {
                await _api.RegisterPushTokenAsync(token);
                registrationSucceeded = true;
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FCM token registration attempt {attempt} failed: {ex}");
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
        }

        if (!registrationSucceeded)
            return;

        // Only remove old tokens after the current token has been accepted by the API.
        // Keep a failed cleanup as a pending item so a later successful registration
        // can retry it instead of permanently leaving a stale token in the database.
        var tokensToCleanup = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(registeredToken) &&
            !string.Equals(registeredToken, token, StringComparison.Ordinal))
            tokensToCleanup.Add(registeredToken);
        if (!string.IsNullOrWhiteSpace(pendingCleanupToken) &&
            !string.Equals(pendingCleanupToken, token, StringComparison.Ordinal) &&
            !tokensToCleanup.Contains(pendingCleanupToken, StringComparer.Ordinal))
            tokensToCleanup.Add(pendingCleanupToken);

        var pendingCleanupFailed = false;
        foreach (var tokenToCleanup in tokensToCleanup)
        {
            try
            {
                await _api.RemovePushTokenAsync(tokenToCleanup);
            }
            catch (Exception ex)
            {
                pendingCleanupFailed = true;
                System.Diagnostics.Debug.WriteLine($"Previous FCM token cleanup failed: {ex}");
            }
        }

        if (pendingCleanupFailed)
        {
            // Keep the most recent old token. A later registration attempt will retry it.
            var latestOldToken = tokensToCleanup.LastOrDefault();
            if (!string.IsNullOrWhiteSpace(latestOldToken))
                Preferences.Default.Set(PendingPushTokenCleanupKey, latestOldToken);
        }
        else
        {
            Preferences.Default.Remove(PendingPushTokenCleanupKey);
        }

        Preferences.Default.Set(PushTokenKey, token);
    }

    private async void OnFcmTokenChanged(object? sender, FCMTokenChangedEventArgs e)
    {
        if (!_api.HasToken || string.IsNullOrWhiteSpace(e.Token)) return;
        if (Interlocked.Exchange(ref _pushRegistrationInProgress, 1) != 0) return;

        try
        {
            // FCM can rotate the token while the app is backgrounded or while the
            // API is temporarily unreachable. Serialize this with startup
            // registration and retry the new token registration.
            await RegisterPushTokenWithRetryAsync(e.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FCM token update failed: {ex}");
        }
        finally
        {
            Volatile.Write(ref _pushRegistrationInProgress, 0);
        }
    }

    private void OnFcmNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        try
        {
            if (e?.Notification?.Data is not null &&
                e.Notification.Data.TryGetValue("conversation_id", out var conversationId) &&
                !string.IsNullOrWhiteSpace(conversationId))
            {
                MainActivity.SetPendingConversation(conversationId);
                MainActivity.TryNavigateToPendingConversation();
                return;
            }

            // Some Android/plugin versions deliver the tap event without the
            // expected Data dictionary (especially after a cold start). The
            // MainActivity intent handler is the fallback and has already
            // captured any conversation_id embedded in the Android Intent.
            MainActivity.TryNavigateToPendingConversation();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FCM notification tap handling failed: {ex}");
        }
    }
#endif

    private void OnSessionExpired(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _handlingSessionExpiry, 1) != 0) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                _account.SignOut();
                _chat.ClearAll();
                Preferences.Default.Remove(PushTokenKey);
                Preferences.Default.Remove(PendingPushTokenCleanupKey);
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

    private async Task ClearNotificationsSafelyAsync()
    {
        try { await _notifications.ClearAllAsync(); } catch { }
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

            if (accountSignedIn != tokenAvailable)
            {
                try { _account.SignOut(); } catch { }
                try { _chat.ClearAll(); } catch { }
                try { _api.ClearToken(); } catch { }
                Preferences.Default.Remove(PushTokenKey);
                Preferences.Default.Remove(PendingPushTokenCleanupKey);
                _ = ClearNotificationsSafelyAsync();
                accountSignedIn = false;
                tokenAvailable = false;
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
