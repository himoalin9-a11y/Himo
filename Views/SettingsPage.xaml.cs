using Himo.Services;

namespace Himo.Views;

public partial class SettingsPage : ContentPage
{
    private const string PushTokenKey = "himo_registered_fcm_token";
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private readonly AppLockService _appLock;
    private bool _initializingAppLockSwitch;
    private bool _initializingNotificationsSwitch;
    private bool _initializingThemeSwitch;
    private int _accountActionInProgress;

    public SettingsPage(HimoApiClient api, AccountService account, ChatService chat, INotificationService notifications, AppLockService appLock)
    {
        InitializeComponent();
        _api = api;
        _account = account;
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;
        _initializingNotificationsSwitch = true;
        if (NotificationsSwitch is not null)
            NotificationsSwitch.IsToggled = _notifications.IsEnabled;
        _initializingNotificationsSwitch = false;
        UpdateNotificationPermissionStatus();
        _initializingThemeSwitch = true;
        if (DarkModeSwitch is not null)
            DarkModeSwitch.IsToggled = AppRuntimeBridge.IsDarkModeEnabled;
        _initializingThemeSwitch = false;
        _initializingAppLockSwitch = true;
        if (AppLockSwitch is not null) AppLockSwitch.IsToggled = _appLock.IsEnabled;
        _initializingAppLockSwitch = false;
        UpdateAppLockUi();
    }

    private async void ProfileClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("profile");
    }

    private void DarkModeToggled(object sender, ToggledEventArgs e)
    {
        if (_initializingThemeSwitch) return;
        AppRuntimeBridge.SetDarkMode(e.Value);
    }

    private void AppLockToggled(object sender, ToggledEventArgs e)
    {
        if (_initializingAppLockSwitch) return;
        if (e.Value)
        {
            if (AppLockPinEntry is not null) AppLockPinEntry.IsVisible = true;
            if (SaveAppLockButton is not null) SaveAppLockButton.IsVisible = true;
            if (AppLockStatusLabel is not null) AppLockStatusLabel.Text = "أدخل رمزًا جديدًا ثم اضغط حفظ.";
        }
        else
        {
            _ = DisableAppLockAsync();
        }
    }

    private async void SaveAppLockClicked(object sender, EventArgs e)
    {
        try
        {
            var pin = AppLockPinEntry?.Text?.Trim() ?? string.Empty;
            await _appLock.SetPinAsync(pin);
            AppRuntimeBridge.MarkSessionUnlocked();
            if (AppLockStatusLabel is not null) AppLockStatusLabel.Text = "تم تفعيل قفل التطبيق.";
            if (AppLockPinEntry is not null) { AppLockPinEntry.Text = string.Empty; AppLockPinEntry.IsVisible = false; }
            if (SaveAppLockButton is not null) SaveAppLockButton.IsVisible = false;
        }
        catch (Exception ex)
        {
            if (AppLockStatusLabel is not null) AppLockStatusLabel.Text = ex.Message;
        }
    }

    private async Task DisableAppLockAsync()
    {
        await _appLock.DisableAsync();
        if (AppLockPinEntry is not null) { AppLockPinEntry.Text = string.Empty; AppLockPinEntry.IsVisible = false; }
        if (SaveAppLockButton is not null) SaveAppLockButton.IsVisible = false;
        if (AppLockStatusLabel is not null) AppLockStatusLabel.Text = "قفل التطبيق متوقف.";
    }

    private void UpdateAppLockUi()
    {
        var enabled = _appLock.IsEnabled;
        if (AppLockSwitch is not null) AppLockSwitch.IsToggled = enabled;
        if (AppLockPinEntry is not null) AppLockPinEntry.IsVisible = enabled && false;
        if (SaveAppLockButton is not null) SaveAppLockButton.IsVisible = false;
        if (AppLockStatusLabel is not null) AppLockStatusLabel.Text = enabled ? "قفل التطبيق مفعل." : "قفل التطبيق متوقف.";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateNotificationPermissionStatus();
    }

    private void UpdateNotificationPermissionStatus()
    {
#if ANDROID
        if (NotificationPermissionStatusLabel is null) return;
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            NotificationPermissionStatusLabel.Text = "إذن النظام غير مطلوب على هذا الإصدار من Android.";
            if (OpenNotificationSettingsButton is not null)
                OpenNotificationSettingsButton.IsVisible = false;
            return;
        }

        var granted = Himo.MainActivity.IsNotificationPermissionGranted();
        var appEnabled = _notifications.IsEnabled;
        if (!appEnabled)
        {
            NotificationPermissionStatusLabel.Text = "إشعارات Himo متوقفة من داخل التطبيق.";
        }
        else
        {
            NotificationPermissionStatusLabel.Text = granted
                ? "حالة إذن النظام: مسموح ✓"
                : "حالة إذن النظام: غير مسموح — فعّل الإذن من إعدادات Android إذا لم تظهر الإشعارات.";
        }

        if (OpenNotificationSettingsButton is not null)
            OpenNotificationSettingsButton.IsVisible = appEnabled && !granted;
#else
        if (NotificationPermissionStatusLabel is not null)
            NotificationPermissionStatusLabel.Text = string.Empty;
#endif
    }

    private void OpenNotificationSettingsClicked(object sender, EventArgs e)
    {
#if ANDROID
        Himo.MainActivity.OpenNotificationSettings();
#endif
    }

    private async void NotificationsToggled(object sender, ToggledEventArgs e)
    {
        if (_initializingNotificationsSwitch) return;
        try
        {
            await _notifications.SetEnabledAsync(e.Value);
#if ANDROID
            if (e.Value)
                Himo.MainActivity.RequestNotificationPermissionIfNeeded();
            UpdateNotificationPermissionStatus();
#endif
        }
        catch (Exception ex)
        {
            if (NotificationsSwitch is not null)
                NotificationsSwitch.IsToggled = !e.Value;
            await DisplayAlertAsync("الإشعارات", $"تعذر تغيير إعداد الإشعارات: {ex.Message}", "حسنًا");
        }
    }

    private async void DeleteAccountClicked(object sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _accountActionInProgress, 1) != 0) return;
        try
        {
            var confirm = await DisplayAlertAsync(
                "حذف الحساب",
                "سيتم حذف حسابك وبياناته ومحادثاتك من الخادم نهائيًا. لا يمكن التراجع عن هذه العملية.",
                "حذف نهائي",
                "إلغاء");
            if (!confirm) return;

            try
            {
                await _api.DeleteAccountAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("حذف الحساب", $"تعذر حذف الحساب: {ex.Message}", "حسنًا");
                return;
            }

            try { await _notifications.ClearAllAsync(); } catch { }
            _account.SignOut();
            _chat.ClearAll();
            _api.ClearToken();
            Preferences.Default.Remove(PushTokenKey);

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new LoginPage(_account, _api);
        }
        finally
        {
            Interlocked.Exchange(ref _accountActionInProgress, 0);
        }
    }

    private async void LogoutClicked(object sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _accountActionInProgress, 1) != 0) return;
        try
        {
            var confirm = await DisplayAlertAsync("تسجيل الخروج", "هل تريد تسجيل الخروج من هذا الجهاز؟", "خروج", "إلغاء");
            if (!confirm) return;

            string? logoutError = null;
            try
            {
#if ANDROID
                var tokensToRemove = new HashSet<string>(StringComparer.Ordinal);
                var registeredToken = Preferences.Default.Get(PushTokenKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(registeredToken))
                    tokensToRemove.Add(registeredToken);

                try
                {
                    await Plugin.Firebase.CloudMessaging.CrossFirebaseCloudMessaging.Current.CheckIfValidAsync();
                    var currentToken = await Plugin.Firebase.CloudMessaging.CrossFirebaseCloudMessaging.Current.GetTokenAsync();
                    if (!string.IsNullOrWhiteSpace(currentToken))
                        tokensToRemove.Add(currentToken);
                }
                catch { }

                foreach (var token in tokensToRemove)
                {
                    try { await _api.RemovePushTokenAsync(token); } catch { }
                }
#endif
                await _api.LogoutAsync();
            }
            catch (Exception ex)
            {
                logoutError = ex.Message;
            }
            finally
            {
                try { await _notifications.ClearAllAsync(); } catch { }
                _account.SignOut();
                _chat.ClearAll();
                _api.ClearToken();
                Preferences.Default.Remove(PushTokenKey);

                var window = Application.Current?.Windows.FirstOrDefault();
                if (window is not null)
                    window.Page = new LoginPage(_account, _api);
            }

            if (!string.IsNullOrWhiteSpace(logoutError))
                await DisplayAlertAsync("تسجيل الخروج", "تم تسجيل الخروج من هذا الجهاز، لكن تعذر إكمال تسجيل الخروج من الخادم. يمكنك المتابعة وتسجيل الدخول لاحقًا.", "حسنًا");
        }
        finally
        {
            Interlocked.Exchange(ref _accountActionInProgress, 0);
        }
    }

    private async void ServerSettingsClicked(object sender, EventArgs e)
    {
        var current = _api.BaseUrl;
        var value = await DisplayPromptAsync(
            "إعداد الخادم",
            "أدخل عنوان الخادم لاستخدامه في التطبيق.",
            "حفظ واختبار",
            "إلغاء",
            "عنوان الخادم",
            180,
            Keyboard.Url,
            current);

        if (string.IsNullOrWhiteSpace(value)) return;

        try
        {
            await _api.SetBaseUrlAsync(value);
            var connected = await _api.HealthAsync();
            await DisplayAlertAsync(
                "اتصال الخادم",
                connected ? "تم الاتصال بالخادم بنجاح ✓" : "تم حفظ العنوان، لكن الخادم لم يستجب.",
                "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("إعداد الخادم", ex.Message, "حسنًا");
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("..");
    }
}
