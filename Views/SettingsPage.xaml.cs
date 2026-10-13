using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Himo.Services;
using Himo.Models;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;

namespace Himo.Views;

public partial class SettingsPage : ContentPage
{

    private const string PushTokenKey = "himo_registered_fcm_token";
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private readonly AppLockService _appLock;
    private readonly HomeViewModel _home;
    private bool _initializingAppLockSwitch;
    private bool _initializingNotificationsSwitch;
    private bool _initializingThemeSwitch;
    private int _accountActionInProgress;
    private bool _contactLookupBusy;
    private bool _contactMatchBusy;
    private double _appliedSettingsFontScale = 1.0;
    private static int _accountRouteRegistered;
    private static int _privacyRoutesRegistered;

    public SettingsPage(HimoApiClient api, AccountService account, ChatService chat, INotificationService notifications, AppLockService appLock, HomeViewModel home)
    {
        InitializeComponent();
        RegisterPrivacyRoutes();
        _api = api;
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
        _home = home;
        _initializingNotificationsSwitch = true;
        if (NotificationsSwitch is not null)
            NotificationsSwitch.IsToggled = _notifications.IsEnabled;
        _initializingNotificationsSwitch = false;
        if (NotificationPreviewSwitch is not null)
            NotificationPreviewSwitch.IsToggled = Preferences.Default.Get("himo_notification_preview", true);
        UpdateNotificationPermissionStatus();
        _initializingThemeSwitch = true;
        if (DarkModeSwitch is not null)
            DarkModeSwitch.IsToggled = AppRuntimeBridge.IsDarkModeEnabled;
        _initializingThemeSwitch = false;
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
                await shell.GoToAsync("account-management", false);
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
                await shell.GoToAsync("privacy-settings", false);
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
                await shell.GoToAsync("storage-data", false);
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
                await shell.GoToAsync("notification-settings", false);
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

    private void DeleteAccountRowTapped(object? sender, TappedEventArgs e) =>
        DeleteAccountClicked(sender, EventArgs.Empty);

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

    private void NotificationPreviewToggled(object sender, ToggledEventArgs e)
    {
        Preferences.Default.Set("himo_notification_preview", e.Value);
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
        _ = SwipePageTransition.AnimateIncomingAsync(this);
        UpdateNotificationPermissionStatus();
        UpdateAccountCard();
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

    private async void FindContactsClicked(object sender, EventArgs e)
    {
        if (_contactLookupBusy) return;
        if (!_api.HasToken)
        {
            await DisplayAlertAsync("جهات الاتصال", "سجّل الدخول أولًا للبحث عن أصدقائك.", "حسنًا");
            return;
        }

        if (!Preferences.Default.Get("himo_contacts_lookup_consent", false))
        {
            var consent = await DisplayAlertAsync(
                "الخصوصية وجهات الاتصال",
                "سيقرأ Himo أرقام الهاتف المحفوظة في جهات اتصالك ويرسل الأرقام اللازمة للمطابقة عبر اتصال آمن. لن يحفظ الخادم دفتر العناوين؛ سيعيد فقط حسابات Himo التي تطابق أرقامها، وبشرط أن يكون أصحابها قد سمحوا بالعثور عليهم.",
                "متابعة",
                "إلغاء");
            if (!consent) return;
            Preferences.Default.Set("himo_contacts_lookup_consent", true);
        }

        if (ContactsResultsPanel is not null)
            ContactsResultsPanel.IsVisible = true;

#if ANDROID
        try
        {
            _contactLookupBusy = true;
            if (FindContactsButton is not null) FindContactsButton.IsEnabled = false;
            if (ContactsLookupProgress is not null)
            {
                ContactsLookupProgress.IsVisible = true;
                ContactsLookupProgress.IsRunning = true;
            }
            if (ContactsMatchStatusLabel is not null)
                ContactsMatchStatusLabel.Text = "جارٍ قراءة جهات الاتصال والبحث عن الحسابات المسجلة...";
            if (ContactMatchesList is not null)
            {
                ContactMatchesList.IsVisible = false;
                ContactMatchesList.ItemsSource = null;
            }

            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.ContactsRead>();

            if (permission != PermissionStatus.Granted)
            {
                if (ContactsMatchStatusLabel is not null)
                    ContactsMatchStatusLabel.Text = "لم يتم منح إذن جهات الاتصال. يمكنك تفعيله من إعدادات Android ثم المحاولة مجددًا.";
                return;
            }

            var contacts = await Contacts.Default.GetAllAsync();
            if (contacts is null)
            {
                if (ContactsMatchStatusLabel is not null)
                    ContactsMatchStatusLabel.Text = "تعذر قراءة جهات الاتصال من الهاتف.";
                return;
            }

            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contact in contacts)
            {
                if (contact?.Phones is null) continue;
                foreach (var contactPhone in contact.Phones)
                {
                    var normalized = NormalizeContactNumber(contactPhone?.PhoneNumber);
                    if (normalized is not null)
                        numbers.Add(normalized);
                    if (numbers.Count >= 5000) break;
                }
                if (numbers.Count >= 5000) break;
            }

            if (numbers.Count == 0)
            {
                if (ContactsMatchStatusLabel is not null)
                    ContactsMatchStatusLabel.Text = "لم نعثر على أرقام هاتف صالحة في جهات اتصالك.";
                return;
            }

            var matches = new Dictionary<Guid, ContactMatchDto>();
            var totalBatches = (int)Math.Ceiling(numbers.Count / 200d);
            var batchNumber = 0;
            foreach (var batch in numbers.Chunk(200))
            {
                batchNumber++;
                if (ContactsMatchStatusLabel is not null)
                    ContactsMatchStatusLabel.Text = $"جارٍ فحص جهات الاتصال... ({batchNumber}/{totalBatches})";

                var found = await _api.FindUsersByPhoneNumbersAsync(batch);
                foreach (var match in found)
                    matches[match.Id] = match;
            }

            var ordered = matches.Values.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            if (ContactMatchesList is not null)
            {
                ContactMatchesList.ItemsSource = ordered;
                ContactMatchesList.IsVisible = ordered.Count > 0;
            }
            if (ContactsMatchStatusLabel is not null)
                ContactsMatchStatusLabel.Text = ordered.Count == 0
                    ? "لم نعثر على حسابات Himo مطابقة. يمكنك دعوة أصدقائك للانضمام إلى Himo."
                    : $"عُثر على {ordered.Count} حسابًا. اضغط على أي حساب لبدء محادثة.";
        }
        catch (Exception ex)
        {
            if (ContactsMatchStatusLabel is not null)
                ContactsMatchStatusLabel.Text = "تعذر البحث في جهات الاتصال: " + ex.Message;
            await DisplayAlertAsync("جهات الاتصال", ex.Message, "حسنًا");
        }
        finally
        {
            _contactLookupBusy = false;
            if (FindContactsButton is not null) FindContactsButton.IsEnabled = true;
            if (ContactsLookupProgress is not null)
            {
                ContactsLookupProgress.IsRunning = false;
                ContactsLookupProgress.IsVisible = false;
            }
        }
#else
        await DisplayAlertAsync("جهات الاتصال", "ميزة البحث في جهات الاتصال متاحة حاليًا على Android.", "حسنًا");
#endif
    }

    private static string? NormalizeContactNumber(string? value)
    {
        var digits = PhoneNumberUtils.Normalize(value);
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal))
            digits = "966" + digits[1..];
        else if (digits.Length == 9 && digits.StartsWith("5", StringComparison.Ordinal))
            digits = "966" + digits;

        return digits.Length is >= 7 and <= 15 ? digits : null;
    }

    private async void ContactMatchSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_contactMatchBusy || e.CurrentSelection.FirstOrDefault() is not ContactMatchDto match)
            return;
        if (sender is CollectionView collection)
            collection.SelectedItem = null;

        _contactMatchBusy = true;
        try
        {
            var conversation = await _home.CreateConversationWithUserAsync(match.Name, match.Id);
            var remoteId = !string.IsNullOrWhiteSpace(conversation.RemoteId)
                ? conversation.RemoteId
                : conversation.Id.ToString();
            var shell = Shell.Current;
            if (shell is not null)
                await shell.GoToAsync($"chat?id={Uri.EscapeDataString(remoteId)}", false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("بدء المحادثة", ex.Message, "حسنًا");
        }
        finally
        {
            _contactMatchBusy = false;
        }
    }

    private async void DeleteAccountClicked(object? sender, EventArgs e)
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


    private async void LogoutAllClicked(object sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _accountActionInProgress, 1) != 0) return;
        try
        {
            var confirm = await DisplayAlertAsync("تسجيل الخروج من جميع الأجهزة",
                "سيتم إنهاء جميع جلسات Himo وإيقاف إشعارات الأجهزة المسجلة. هل تريد المتابعة؟",
                "متابعة", "إلغاء");
            if (!confirm) return;

            try
            {
                await _api.LogoutAllAsync();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("تسجيل الخروج", $"تعذر تنفيذ العملية: {ex.Message}", "حسنًا");
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
                if (Application.Current is App app)
                    await app.UnregisterCurrentPushTokenAsync();
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

    private async void BackClicked(object sender, EventArgs e)
    {
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
