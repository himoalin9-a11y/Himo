using Himo.Services;

namespace Himo.Views;

public partial class ProfilePage : ContentPage
{
    private const int MaximumPhotoBytes = 4 * 1024 * 1024;
    private readonly ProfileService _profile;
    private readonly HimoApiClient _api;
    private bool _busy;
    private bool _profileChangedLocally;
    private bool _loadingProfileFields;

    public ProfilePage(ProfileService profile, HimoApiClient api)
    {
        InitializeComponent();
        _profile = profile;
        _api = api;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
        _ = SwipePageTransition.AnimateIncomingAsync(this);
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
        _loadingProfileFields = true;
        try
        {
            if (NameEntry is not null) NameEntry.Text = value.Name;
            if (StatusEntry is not null) StatusEntry.Text = value.Status;
            if (InitialLabel is not null) InitialLabel.Text = value.Initial;
            if (ProfileNameLabel is not null) ProfileNameLabel.Text = value.Name;
            DisplayPhoto(value.PhotoPath);
        }
        finally
        {
            _loadingProfileFields = false;
        }
        _ = LoadRemoteProfileAsync();
    }

    private async Task LoadRemoteProfileAsync()
    {
        if (!_api.HasToken) return;
        try
        {
            var remote = await _api.GetMyProfileAsync();
            if (_profileChangedLocally) return;

            _loadingProfileFields = true;
            try
            {
                _profile.Update(remote.Name, remote.Status);
                if (NameEntry is not null) NameEntry.Text = remote.Name;
                if (StatusEntry is not null) StatusEntry.Text = remote.Status;
                if (InitialLabel is not null) InitialLabel.Text = _profile.Profile.Initial;
                if (ProfileNameLabel is not null) ProfileNameLabel.Text = remote.Name;
            }
            finally
            {
                _loadingProfileFields = false;
            }

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

    private void ProfileFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingProfileFields || _busy) return;
        _profileChangedLocally = true;
        if (NameEntry is not null && ReferenceEquals(sender, NameEntry) && ProfileNameLabel is not null)
        {
            var editedName = NameEntry.Text?.Trim();
            ProfileNameLabel.Text = string.IsNullOrWhiteSpace(editedName)
                ? "مستخدم Himo"
                : editedName;
        }
    }

    private async void ChangePhotoClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        await UiMotion.PressAsync(sender as VisualElement);
        try
        {
            SetBusy(true, "جارٍ تحديث الصورة...");
            var pickedPhotos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "اختر صورة الملف الشخصي",
                SelectionLimit = 1
            });
            // Keep single-photo behavior on .NET 10 and handle cancellation safely.
            if (pickedPhotos is null || pickedPhotos.Count == 0) return;
            var pickedPhoto = pickedPhotos[0];

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
            SetBusy(true, "جارٍ إزالة الصورة...");
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
        if (RemovePhotoButton is not null) RemovePhotoButton.IsVisible = hasPhoto;
        if (ChangePhotoButton is not null) ChangePhotoButton.Text = hasPhoto ? "تغيير الصورة" : "إضافة صورة";
    }

    private void SetBusy(bool busy, string? buttonText = null)
    {
        _busy = busy;
        if (SaveButton is not null) SaveButton.IsEnabled = !busy;
        if (ChangePhotoButton is not null) ChangePhotoButton.IsEnabled = !busy;
        if (RemovePhotoButton is not null) RemovePhotoButton.IsEnabled = !busy;
        if (CameraPhotoButton is not null) CameraPhotoButton.IsEnabled = !busy;
        if (SaveButton is not null)
            SaveButton.Text = busy && !string.IsNullOrWhiteSpace(buttonText)
                ? buttonText
                : "حفظ التغييرات";
    }

    private async void SaveClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        await UiMotion.PressAsync(sender as VisualElement);
        try
        {
            SetBusy(true, "جارٍ الحفظ...");
            var name = NameEntry?.Text ?? string.Empty;
            var status = StatusEntry?.Text ?? string.Empty;
            if (_api.HasToken)
            {
                // Keep the local view unchanged unless the server accepted the edit.
                var remote = await _api.UpdateMyProfileAsync(name.Trim(), status.Trim());
                _profile.Update(remote.Name, remote.Status);
                _profileChangedLocally = true;
            }
            else
            {
                _profile.Update(name, status);
                _profileChangedLocally = true;
            }

            if (InitialLabel is not null) InitialLabel.Text = _profile.Profile.Initial;
            if (ProfileNameLabel is not null) ProfileNameLabel.Text = _profile.Profile.Name;
            await DisplayAlertAsync("تم الحفظ", "تم تحديث الملف الشخصي.", "حسنًا");
        }
        catch (ArgumentException ex)
        {
            await DisplayAlertAsync("تنبيه", ex.Message, "حسنًا");
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("تعذر الحفظ", ex.Message, "حسنًا");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync("تعذر الحفظ", "انتهت مهلة الاتصال بالخادم. حاول مرة أخرى.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر الحفظ", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }
}
