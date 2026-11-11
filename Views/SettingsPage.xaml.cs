using System.Threading;
using System.Threading.Tasks;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Himo.Views;

public partial class SettingsPage : ContentPage
{

    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ProfileService _profile;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private readonly AppLockService _appLock;
    private readonly Dictionary<VisualElement, double> _originalFontSizes = new();
    private bool _initializingNotificationsSwitch;
    private static int _accountRouteRegistered;
    private static int _privacyRoutesRegistered;

    public SettingsPage(HimoApiClient api, AccountService account, ProfileService profile, ChatService chat, INotificationService notifications, AppLockService appLock, HomeViewModel home)
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
        {
            SettingsAccountNameLabel.AutomationId = string.IsNullOrWhiteSpace(currentAccountName) ? string.Empty : "Himo.UserSuppliedText";
            SettingsAccountNameLabel.Text = string.IsNullOrWhiteSpace(currentAccountName) ? "حسابك في Himo" : currentAccountName;
        }
        if (SettingsAccountEmailLabel is not null)
        {
            SettingsAccountEmailLabel.AutomationId = string.IsNullOrWhiteSpace(currentAccountEmail) ? string.Empty : "Himo.UserSuppliedText";
            SettingsAccountEmailLabel.Text = string.IsNullOrWhiteSpace(currentAccountEmail) ? "إدارة حسابك وتفضيلاتك" : currentAccountEmail;
        }
        _chat = chat;
        _notifications = notifications;
        _appLock = appLock;


        CaptureOriginalFontSizes(this);

        _initializingNotificationsSwitch = true;
        if (NotificationsSwitch is not null)
            NotificationsSwitch.IsToggled = _notifications.IsEnabled;
        _initializingNotificationsSwitch = false;
        UpdateNotificationPermissionStatus();

    }

    private void AccountCardTapped(object? sender, TappedEventArgs e) =>
        _ = NavigateToAccountManagementAsync();

    private void AccountManagementRowTapped(object? sender, TappedEventArgs e) =>
        _ = NavigateToAccountManagementAsync();

    private async void LinkedDevicesRowTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Navigation.PushAsync(new LinkedDevicesPage(_api), true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("الأجهزة المرتبطة"), AppLocalization.TranslateSourceText("تعذر فتح صفحة الأجهزة المرتبطة: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

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
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("إدارة الحساب"), AppLocalization.TranslateSourceText("تعذر فتح صفحة الحساب: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("الخصوصية"), AppLocalization.TranslateSourceText("تعذر فتح إعدادات الخصوصية: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("إعدادات الدردشات"), AppLocalization.TranslateSourceText("تعذر فتح إعدادات الدردشات: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("التخزين والبيانات"), AppLocalization.TranslateSourceText("تعذر فتح صفحة التخزين والبيانات: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("الإشعارات"), AppLocalization.TranslateSourceText("تعذر فتح إعدادات الإشعارات: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void ProfileNavTapped(object? sender, TappedEventArgs e)
    {
        if (SwipePageTransition.SettingsWasEnteredFromProfile)
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
        else
            await SwipePageTransition.NavigateAsync(this, "profile", SwipeDirection.Right);
    }

    private async void CallsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "calls", SwipeDirection.Right);

    private async void ChatsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "chats", SwipeDirection.Right);


    private async void AppearanceExperienceTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Navigation.PushAsync(new AppearanceExperiencePage(), true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("المظهر والتجربة"), AppLocalization.TranslateSourceText("تعذر فتح الصفحة: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void FeaturesTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            await Navigation.PushAsync(new FeaturesSettingsPage(_api, _appLock), true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("الميزات"), AppLocalization.TranslateSourceText("تعذر فتح الصفحة: ") + Himo.Services.AppLocalization.TranslateSourceText(ex.Message), AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyFontScale(Preferences.Default.Get("himo_settings_font_scale", 1.0));
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this, batchSize: 48);
        UpdateNotificationPermissionStatus();
        UpdateAccountCard();
        _ = RefreshOwnProfilePhotoAsync();
    }

    private void CaptureOriginalFontSizes(VisualElement element)
    {
        var fontSize = GetFontSize(element);
        if (fontSize is > 0 && double.IsFinite(fontSize.Value))
            _originalFontSizes.TryAdd(element, fontSize.Value);

        if (element is IVisualTreeElement visualTree)
        {
            foreach (var child in visualTree.GetVisualChildren())
            {
                if (child is VisualElement visualChild)
                    CaptureOriginalFontSizes(visualChild);
            }
        }
    }

    private static double? GetFontSize(VisualElement element) => element switch
    {
        Label label => label.FontSize,
        Button button => button.FontSize,
        Entry entry => entry.FontSize,
        Editor editor => editor.FontSize,
        SearchBar searchBar => searchBar.FontSize,
        Picker picker => picker.FontSize,
        _ => null
    };

    private static void SetFontSize(VisualElement element, double fontSize)
    {
        switch (element)
        {
            case Label label: label.FontSize = fontSize; break;
            case Button button: button.FontSize = fontSize; break;
            case Entry entry: entry.FontSize = fontSize; break;
            case Editor editor: editor.FontSize = fontSize; break;
            case SearchBar searchBar: searchBar.FontSize = fontSize; break;
            case Picker picker: picker.FontSize = fontSize; break;
        }
    }

    private void ApplyFontScale(double scale)
    {
        scale = Math.Clamp(scale, 0.85, 1.20);
        foreach (var item in _originalFontSizes)
        {
            if (item.Key.Handler is not null)
                SetFontSize(item.Key, Math.Round(item.Value * scale, 1));
        }
    }

    private void UpdateAccountCard()
    {
        var current = _account.CurrentAccount;
        var currentName = current?.Name;
        var currentEmail = current?.Email;
        // AutomationId is assigned once in the constructor. MAUI does not allow
        // changing it after the element has been initialized. Only refresh text here.
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
            await DisplayAlertAsync(AppLocalization.TranslateSourceText("الإشعارات"), $"{AppLocalization.TranslateSourceText("تعذر تغيير إعداد الإشعارات: ")}{Himo.Services.AppLocalization.TranslateSourceText(ex.Message)}", AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
