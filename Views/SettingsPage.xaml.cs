using System.Threading;
using System.Threading.Tasks;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;

namespace Himo.Views;

public partial class SettingsPage : ContentPage
{

    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ProfileService _profile;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private readonly AppLockService _appLock;
    private bool _initializingAppLockSwitch;
    private bool _initializingNotificationsSwitch;
    private double _appliedSettingsFontScale = 1.0;
    private static int _accountRouteRegistered;
    private static int _privacyRoutesRegistered;

    public SettingsPage(HimoApiClient api, AccountService account, ProfileService profile, ChatService chat, INotificationService notifications, AppLockService appLock)
    {
        InitializeComponent();
        RegisterPrivacyRoutes();
        _api = api;
        _profile = profile;
        _account = account;
        var currentAccount = _account.CurrentAccount;
        var currentAccountName = currentAccount?.Name;
        var currentAccountEmail = currentAccount?.Email;
        if (SettingsAccountNameLabel is not null)
            SettingsAccountNameLabel.Text = string.IsNullOrWhiteSpace(currentAccountName) ? "حسابك في Himo" : currentAccountName;
        if (SettingsAccountEmailLabel is not null)
            SettingsAccountEmailLabel.Text = string.IsNullOrWhiteSpace(currentAccountEmail) ? "إدارة حسابك وتفضيلاتك" : currentAccountEmail;
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;
        _initializingNotificationsSwitch = true;
        if (NotificationsSwitch is not null)
            NotificationsSwitch.IsToggled = _notifications.IsEnabled;
        _initializingNotificationsSwitch = false;
        UpdateNotificationPermissionStatus();
        if (LanguageSummaryLabel is not null)
            LanguageSummaryLabel.Text = GetLanguageDisplayName(Preferences.Default.Get("himo_app_language", "ar"));
        _initializingAppLockSwitch = true;
        if (AppLockSwitch is not null) AppLockSwitch.IsToggled = _appLock.IsEnabled;
        _initializingAppLockSwitch = false;
        UpdateAppLockUi();

        var savedFontScale = Preferences.Default.Get("himo_settings_font_scale", 1.0);
        if (savedFontScale < 0.85 || savedFontScale > 1.25)
            savedFontScale = 1.0;
        ApplySettingsFontScale(savedFontScale);
        UpdateFontSizeButtonText(savedFontScale);
    }

    private void AccountCardTapped(object? sender, TappedEventArgs e) =>
        _ = NavigateToAccountManagementAsync();

    private void AccountManagementRowTapped(object? sender, TappedEventArgs e) =>
        _ = NavigateToAccountManagementAsync();

    private async Task NavigateToAccountManagementAsync()
    {
        try
        {
            if (Interlocked.Exchange(ref _accountRouteRegistered, 1) == 0)
                Routing.RegisterRoute("account-management", typeof(AccountManagementPage));
            var shell = Shell.Current;
            if (shell is not null)
                await HimoShellNavigator.NavigateAsync("account-management", false);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _accountRouteRegistered, 0);
            await DisplayAlertAsync("إدارة الحساب", "تعذر فتح صفحة الحساب: " + ex.Message, "حسنًا");
        }
    }

    private async void PageSwiped(object? sender, SwipedEventArgs e)
    {
        if (e.Direction != SwipeDirection.Right)
            return; // Settings is the final page in the horizontal sequence.

        if (SwipePageTransition.SettingsWasEnteredFromProfile)
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
        else
            await SwipePageTransition.NavigateAsync(this, "profile", SwipeDirection.Right);
    }

    private static void RegisterPrivacyRoutes()
    {
        if (Interlocked.Exchange(ref _privacyRoutesRegistered, 1) != 0)
            return;

        Routing.RegisterRoute("privacy-settings", typeof(PrivacySettingsPage));
        Routing.RegisterRoute("privacy-last-seen", typeof(PrivacyLastSeenPage));
        Routing.RegisterRoute("privacy-profile-photo", typeof(PrivacyProfilePhotoPage));
        Routing.RegisterRoute("privacy-status", typeof(PrivacyStatusPage));
        Routing.RegisterRoute("privacy-groups", typeof(PrivacyGroupsPage));
        Routing.RegisterRoute("privacy-calls", typeof(PrivacyCallsPage));
        Routing.RegisterRoute("privacy-contacts", typeof(PrivacyContactsPage));
        Routing.RegisterRoute("privacy-checkup", typeof(PrivacyCheckupPage));
        Routing.RegisterRoute("notification-settings", typeof(NotificationSettingsPage));
    }

    private async void PrivacyDetailsRowTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var shell = Shell.Current;
            if (shell is not null)
                await HimoShellNavigator.NavigateAsync("privacy-settings", false);
            else
                await Navigation.PushAsync(new PrivacySettingsPage());
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الخصوصية", "تعذر فتح إعدادات الخصوصية: " + ex.Message, "حسنًا");
        }
    }

    private async void ChatSettingsClicked(object? sender, TappedEventArgs e)
    {
        try
        {
            await Navigation.PushAsync(new ChatSettingsPage(_chat));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("إعدادات الدردشات", "تعذر فتح إعدادات الدردشات: " + ex.Message, "حسنًا");
        }
    }

    private static int _storageDataRouteRegistered;

    private async void StorageDataRowTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var shell = Shell.Current;
            if (shell is not null)
            {
                if (Interlocked.Exchange(ref _storageDataRouteRegistered, 1) == 0)
                    Routing.RegisterRoute("storage-data", typeof(StorageDataSettingsPage));
                await HimoShellNavigator.NavigateAsync("storage-data", false);
            }
            else
            {
                await Navigation.PushAsync(new StorageDataSettingsPage(_chat));
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("التخزين والبيانات", "تعذر فتح صفحة التخزين والبيانات: " + ex.Message, "حسنًا");
        }
    }

    private async void NotificationDetailsRowTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var shell = Shell.Current;
            if (shell is not null)
                await HimoShellNavigator.NavigateAsync("notification-settings", false);
            else
                await Navigation.PushAsync(new NotificationSettingsPage(_notifications));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الإشعارات", "تعذر فتح إعدادات الإشعارات: " + ex.Message, "حسنًا");
        }
    }

    private async void ProfileNavTapped(object? sender, TappedEventArgs e)
    {
        if (SwipePageTransition.SettingsWasEnteredFromProfile)
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
        else
            await SwipePageTransition.NavigateAsync(this, "profile", SwipeDirection.Right);
    }

    private async void PeopleNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "phone", SwipeDirection.Right);

    private async void CallsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "calls", SwipeDirection.Right);

    private async void ChatsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "chats", SwipeDirection.Right);

    private async void LanguageRowTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Navigation.PushAsync(new LanguageSettingsPage());
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("اللغة", "تعذر فتح إعدادات اللغة: " + ex.Message, "حسنًا");
        }
    }

    private async void AccountActionsRowTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Navigation.PushAsync(new AccountActionsPage(_api, _account, _chat, _notifications));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الحساب", "تعذر فتح صفحة الحساب: " + ex.Message, "حسنًا");
        }
    }

    private static string GetLanguageDisplayName(string code) => code switch
    {
        "en" => "English",
        "fr" => "Français",
        "tr" => "Türkçe",
        "ur" => "اردو",
        "es" => "Español",
        "id" => "Bahasa Indonesia",
        _ => "العربية"
    };

    private async void IconsAndAppearanceTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (Shell.Current is not null)
                await HimoShellNavigator.NavigateAsync("icons-appearance", true);
            else if (Navigation is not null)
                await Navigation.PushAsync(new IconsAppearanceSettingsPage(), true);
            else
                throw new InvalidOperationException("تعذر الوصول إلى مسار التنقل.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الأيقونات والشكل", "تعذر فتح الصفحة: " + ex.Message, "حسنًا");
        }
    }

    private async void FontSizeRowClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(
            "حجم الخط في صفحة الإعدادات", "إلغاء", null,
            "صغير", "متوسط", "كبير");
        var scale = selected switch
        {
            "صغير" => 0.9,
            "كبير" => 1.15,
            "متوسط" => 1.0,
            _ => _appliedSettingsFontScale
        };
        if (selected is not ("صغير" or "متوسط" or "كبير")) return;

        Preferences.Default.Set("himo_settings_font_scale", scale);
        ApplySettingsFontScale(scale);
        UpdateFontSizeButtonText(scale);
    }

    private void UpdateFontSizeButtonText(double scale)
    {
        if (FontSizeValueButton is null) return;
        FontSizeValueButton.Text = scale < 0.95 ? "صغير" : scale > 1.05 ? "كبير" : "متوسط";
    }

    private void ApplySettingsFontScale(double scale)
    {
        if (Content is null || scale <= 0) return;
        var ratio = scale / _appliedSettingsFontScale;
        foreach (var element in EnumerateVisualElements(Content))
        {
            switch (element)
            {
                case Label label when label.FontSize > 0:
                    label.FontSize = Math.Clamp(label.FontSize * ratio, 8, 40);
                    break;
                case Button button when button.FontSize > 0:
                    button.FontSize = Math.Clamp(button.FontSize * ratio, 8, 40);
                    break;
                case Entry entry when entry.FontSize > 0:
                    entry.FontSize = Math.Clamp(entry.FontSize * ratio, 8, 40);
                    break;
            }
        }
        _appliedSettingsFontScale = scale;
    }

    private static IEnumerable<VisualElement> EnumerateVisualElements(VisualElement root)
    {
        yield return root;
        if (root is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is VisualElement visual)
                {
                    foreach (var descendant in EnumerateVisualElements(visual))
                        yield return descendant;
                }
            }
        }
        else if (root is Border border && border.Content is VisualElement borderContent)
        {
            foreach (var descendant in EnumerateVisualElements(borderContent))
                yield return descendant;
        }
        else if (root is ContentView contentView && contentView.Content is VisualElement content)
        {
            foreach (var descendant in EnumerateVisualElements(content))
                yield return descendant;
        }
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
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this);
        _ = SwipePageTransition.AnimateIncomingAsync(this);
        UpdateNotificationPermissionStatus();
        if (LanguageSummaryLabel is not null)
            LanguageSummaryLabel.Text = GetLanguageDisplayName(Preferences.Default.Get("himo_app_language", "ar"));
        UpdateAccountCard();
        _ = RefreshOwnProfilePhotoAsync();
    }

    private void UpdateAccountCard()
    {
        var current = _account.CurrentAccount;
        var currentName = current?.Name;
        var currentEmail = current?.Email;
        if (SettingsAccountNameLabel is not null)
            SettingsAccountNameLabel.Text = string.IsNullOrWhiteSpace(currentName) ? "حسابك في Himo" : currentName;
        if (SettingsAccountEmailLabel is not null)
            SettingsAccountEmailLabel.Text = string.IsNullOrWhiteSpace(currentEmail) ? "إدارة حسابك وتفضيلاتك" : currentEmail;
        DisplayOwnProfilePhoto(_profile.Profile.PhotoPath);
    }

    private void DisplayOwnProfilePhoto(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        SettingsProfilePhotoImage.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
        SettingsProfilePhotoImage.IsVisible = hasPhoto;
        SettingsProfilePlaceholderIcon.IsVisible = !hasPhoto;
    }

    private async Task RefreshOwnProfilePhotoAsync()
    {
        try
        {
            await _api.TokenInitialization;
            if (!_api.HasToken) return;

            var serverProfile = await _api.GetMyProfileAsync();
            if (serverProfile.HasProfilePhoto == true)
            {
                var photo = await _api.GetMyProfilePhotoAsync();
                if (photo is null) return;
                var path = _profile.SavePhoto(photo.Bytes, photo.ContentType);
                await MainThread.InvokeOnMainThreadAsync(() => DisplayOwnProfilePhoto(path));
            }
            else if (serverProfile.HasProfilePhoto == false)
            {
                _profile.ClearPhoto();
                await MainThread.InvokeOnMainThreadAsync(() => DisplayOwnProfilePhoto(null));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Settings] Profile avatar refresh failed: {ex.Message}");
        }
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
            {
                Himo.MainActivity.RequestNotificationPermissionIfNeeded();
                if (Application.Current is App app)
                    await app.RegisterCurrentPushTokenAsync();
            }
            else
            {
                if (Application.Current is App app)
                    await app.UnregisterCurrentPushTokenAsync();
            }
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

    private async void BackClicked(object sender, EventArgs e)
    {
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
