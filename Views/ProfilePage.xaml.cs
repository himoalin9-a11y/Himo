using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Himo.Views;

public partial class ProfilePage : ContentPage
{
    private const int MaximumPhotoBytes = 4 * 1024 * 1024;
    private readonly ProfileService _profile;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ChatService _chat;
    private readonly INotificationService _notifications;
    private bool _busy;
    private bool _profileChangedLocally;
    private bool _logoutInProgress;

    public ProfilePage(ProfileService profile, HimoApiClient api, AccountService account,
        ChatService chat, INotificationService notifications)
    {
        InitializeComponent();
        _profile = profile;
        _api = api;
        _account = account;
        _chat = chat;
        _notifications = notifications;
        RegisterProfileRoutes();
    }

    private static void RegisterProfileRoutes()
    {
        // SettingsPage registers these too. Ignore duplicate registrations so this
        // page can open the same destinations even when Settings has never opened.
        try { Routing.RegisterRoute("privacy-settings", typeof(PrivacySettingsPage)); } catch { }
        try { Routing.RegisterRoute("notification-settings", typeof(NotificationSettingsPage)); } catch { }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Keep navigation lightweight: the blue icons are defined by the page assets,
        // so do not rescan the entire visual tree or animate every element on entry.
        LoadProfile();
    }

    private async void PageSwiped(object? sender, SwipedEventArgs e)
    {
        if (e.Direction == SwipeDirection.Left)
        {
            await SwipePageTransition.NavigateAsync(this, "settings", SwipeDirection.Left);
            return;
        }

        // Profile follows phone search; the right swipe returns to that home section.
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "phone", SwipeDirection.Right);
    }

    private void LoadProfile()
    {
        _profileChangedLocally = false;
        var value = _profile.Profile;
        if (InitialLabel is not null) InitialLabel.Text = value.Initial;
        UpdateProfileSummary(value.Name, value.Status);
        DisplayPhoto(value.PhotoPath);
        _ = LoadRemoteProfileAsync();
    }

    private void UpdateProfileSummary(string? name, string? status)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "مستخدم Himo" : name.Trim();
        var cleanStatus = string.IsNullOrWhiteSpace(status) ? "متاح على Himo" : status.Trim();
        if (ProfileNameLabel is not null) ProfileNameLabel.Text = cleanName;
        if (ProfileStatusLabel is not null) ProfileStatusLabel.Text = cleanStatus;
    }

    private async Task LoadRemoteProfileAsync()
    {
        if (!_api.HasToken) return;
        try
        {
            var remote = await _api.GetMyProfileAsync();
            if (_profileChangedLocally) return;

            _profile.Update(remote.Name, remote.Status);
            if (InitialLabel is not null) InitialLabel.Text = _profile.Profile.Initial;
            UpdateProfileSummary(remote.Name, remote.Status);

            // Nullable means the server predates profile-photo support; in that case
            // keep the locally cached photo instead of unexpectedly removing it.
            if (remote.HasProfilePhoto == true)
            {
                var photo = await _api.GetMyProfilePhotoAsync();
                if (photo is not null && !_profileChangedLocally)
                {
                    var localPath = _profile.SavePhoto(photo.Bytes, photo.ContentType);
                    DisplayPhoto(localPath);
                }
            }
            else if (remote.HasProfilePhoto == false && !_profileChangedLocally)
            {
                _profile.ClearPhoto();
                DisplayPhoto(null);
            }
        }
        catch
        {
            // The saved local profile remains usable when the network is unavailable.
        }
    }

    private async void EditProfileTapped(object? sender, TappedEventArgs e)
    {
        // Open a dedicated details screen instead of expanding a long edit form
        // underneath the main profile menu.
        try
        {
            await Navigation.PushModalAsync(new ProfileDetailsPage(_profile, _api, _account));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الملف الشخصي", "تعذر فتح تفاصيل الحساب. " + ex.Message, "حسنًا");
        }
    }

    private async void ProfilePhotoTapped(object? sender, TappedEventArgs e)
    {
        // The main avatar opens the dedicated profile-details screen. From there,
        // tapping the avatar opens the full-screen zoomable photo viewer.
        try
        {
            await Navigation.PushModalAsync(new ProfileDetailsPage(_profile, _api, _account));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الملف الشخصي", "تعذر فتح تفاصيل الحساب. " + ex.Message, "حسنًا");
        }
    }

    private async void ShareAccountTapped(object? sender, TappedEventArgs e)
    {
        await UiMotion.PressAsync(sender as VisualElement);
        await OpenProfileRouteAsync("share-account", "تعذر فتح صفحة مشاركة الحساب.");
    }

    private async void SecurityPrivacyTapped(object? sender, TappedEventArgs e) =>
        await OpenProfileRouteAsync("privacy-settings", "تعذر فتح إعدادات الأمان والخصوصية.");

    private async void NotificationsTapped(object? sender, TappedEventArgs e) =>
        await OpenProfileRouteAsync("notification-settings", "تعذر فتح إعدادات الإشعارات.");

    private async void AppearanceTapped(object? sender, TappedEventArgs e) =>
        await OpenProfileRouteAsync("icons-appearance", "تعذر فتح إعدادات المظهر والأيقونات.");

    private async Task OpenProfileRouteAsync(string route, string errorMessage)
    {
        try
        {
            var navigated = await HimoShellNavigator.NavigateAsync(route, false);
            if (!navigated && Shell.Current is null)
                await DisplayAlertAsync("التنقل", errorMessage, "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("التنقل", errorMessage + " " + ex.Message, "حسنًا");
        }
    }

    private async void HelpTapped(object? sender, TappedEventArgs e)
    {
        await DisplayAlertAsync(
            "مساعدة ودعم",
            "إذا واجهت مشكلة في Himo، تحقق أولًا من اتصال الإنترنت وأذونات الإشعارات والكاميرا والميكروفون. يمكنك مراجعة خيارات الخصوصية والمظهر من هذه الصفحة، وإعادة فتح التطبيق إذا توقف تحديث الواجهة.",
            "حسنًا");
    }

    private async void AboutTapped(object? sender, TappedEventArgs e)
    {
        var version = string.IsNullOrWhiteSpace(AppInfo.Current.VersionString)
            ? "غير محدد"
            : AppInfo.Current.VersionString;
        await DisplayAlertAsync("حول Himo", $"Himo — تطبيق للتواصل والمحادثات والمكالمات.\nالإصدار: {version}", "حسنًا");
    }

    private async void LogoutClicked(object? sender, EventArgs e)
    {
        if (_logoutInProgress) return;
        var confirmed = await DisplayAlertAsync(
            "تسجيل الخروج", "هل تريد تسجيل الخروج من هذا الجهاز؟", "خروج", "إلغاء");
        if (!confirmed) return;

        _logoutInProgress = true;
        string? logoutError = null;
        try
        {
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
                Preferences.Default.Remove("himo_registered_fcm_token");
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window is not null)
                    window.Page = new LoginPage(_account, _api);
            }

            if (!string.IsNullOrWhiteSpace(logoutError))
                await DisplayAlertAsync("تسجيل الخروج", "تم تسجيل الخروج من الجهاز، لكن تعذر إكمال العملية على الخادم. يمكنك تسجيل الدخول لاحقًا.", "حسنًا");
        }
        finally
        {
            _logoutInProgress = false;
        }
    }

    private async void ChangePhotoClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        await UiMotion.PressAsync(sender as VisualElement);

        var action = await DisplayActionSheetAsync(
            "صورة الملف الشخصي", "إلغاء", "إزالة الصورة",
            "التقاط صورة بالكاميرا", "اختيار صورة من المعرض");
        if (action == "إزالة الصورة")
        {
            RemovePhotoClicked(sender, EventArgs.Empty);
            return;
        }
        if (action is not ("التقاط صورة بالكاميرا" or "اختيار صورة من المعرض")) return;

        try
        {
            SetBusy(true);
            FileResult? pickedPhoto;
            if (action == "التقاط صورة بالكاميرا")
            {
                pickedPhoto = await MediaPicker.Default.CapturePhotoAsync();
            }
            else
            {
                var pickedPhotos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
                {
                    Title = "اختر صورة الملف الشخصي",
                    SelectionLimit = 1
                });
                pickedPhoto = pickedPhotos is { Count: > 0 } ? pickedPhotos[0] : null;
            }
            if (pickedPhoto is null) return;

            var contentType = ResolvePhotoContentType(pickedPhoto);
            byte[] imageBytes;
            await using (var input = await pickedPhoto.OpenReadAsync())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[32 * 1024];
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length));
                    if (read == 0) break;
                    if (output.Length + read > MaximumPhotoBytes)
                        throw new InvalidDataException("اختر صورة لا يتجاوز حجمها 4 ميغابايت.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
                imageBytes = output.ToArray();
            }

            if (_api.HasToken)
                await _api.UploadMyProfilePhotoAsync(imageBytes, contentType);

            var localPath = _profile.SavePhoto(imageBytes, contentType);
            DisplayPhoto(localPath);
            _profileChangedLocally = true;
            await DisplayAlertAsync("تم تحديث الصورة", "تم تحديث صورة الملف الشخصي بنجاح.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر تحديث الصورة", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemovePhotoClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        var confirmed = await DisplayAlertAsync(
            "إزالة الصورة",
            "هل تريد إزالة صورة الملف الشخصي؟",
            "إزالة",
            "إلغاء");
        if (!confirmed) return;

        try
        {
            SetBusy(true);
            if (_api.HasToken)
                await _api.DeleteMyProfilePhotoAsync();

            _profile.ClearPhoto();
            DisplayPhoto(null);
            _profileChangedLocally = true;
            await DisplayAlertAsync("تمت الإزالة", "تمت إزالة صورة الملف الشخصي.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر إزالة الصورة", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string ResolvePhotoContentType(FileResult photo)
    {
        var contentType = (photo.ContentType ?? string.Empty)
            .Split(';', StringSplitOptions.TrimEntries)[0]
            .ToLowerInvariant();
        if (contentType == "image/jpg") contentType = "image/jpeg";

        if (IsSupportedPhotoType(contentType)) return contentType;

        var extension = Path.GetExtension(photo.FileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".heif" => "image/heif",
            ".gif" => "image/gif",
            _ => throw new InvalidDataException("صيغة الصورة غير مدعومة. اختر صورة بصيغة JPG أو PNG أو WebP أو HEIC.")
        };
    }

    private static bool IsSupportedPhotoType(string contentType) =>
        contentType is "image/jpeg" or "image/png" or "image/webp" or
            "image/heic" or "image/heif" or "image/gif";

    private void DisplayPhoto(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        if (ProfilePhotoImage is not null)
        {
            ProfilePhotoImage.IsVisible = hasPhoto;
            ProfilePhotoImage.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
        }
        if (InitialLabel is not null) InitialLabel.IsVisible = !hasPhoto;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (CameraPhotoButton is not null) CameraPhotoButton.IsEnabled = !busy;
    }

    // Use the same shared navigator as HomePage and SettingsPage so the profile
    // tab does not create a separate NavigationPage stack or bypass transition guards.
    private async void ChatsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "chats", SwipeDirection.Right);

    private async void CallsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "calls", SwipeDirection.Right);

    private async void PeopleNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "phone", SwipeDirection.Right);

    private async void SettingsNavTapped(object? sender, TappedEventArgs e) =>
        await OpenSettingsFromProfileAsync();

    private async void SettingsHeaderClicked(object? sender, EventArgs e) =>
        await OpenSettingsFromProfileAsync();

    private async Task OpenSettingsFromProfileAsync()
    {
        // If Profile was opened from Settings, return to that existing page rather
        // than pushing a duplicate SettingsPage onto the Shell stack.
        if (HasSettingsPageImmediatelyBehindCurrent())
        {
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Left);
            return;
        }

        await SwipePageTransition.NavigateAsync(this, "settings", SwipeDirection.Left);
    }

    private bool HasSettingsPageImmediatelyBehindCurrent()
    {
        var stack = Shell.Current?.Navigation?.NavigationStack;
        if (stack is null || stack.Count < 2)
            return false;

        for (var i = 1; i < stack.Count; i++)
        {
            if (ReferenceEquals(stack[i], this))
                return stack[i - 1] is SettingsPage;
        }

        return false;
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
