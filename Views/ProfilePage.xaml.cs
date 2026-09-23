using Himo.Services;

namespace Himo.Views;

public partial class ProfilePage : ContentPage
{
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
        LoadProfile();
    }

    private void LoadProfile()
    {
        var value = _profile.Profile;
        _loadingProfileFields = true;
        try
        {
            if (NameEntry is not null) NameEntry.Text = value.Name;
            if (StatusEntry is not null) StatusEntry.Text = value.Status;
            if (InitialLabel is not null) InitialLabel.Text = value.Initial;
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
            }
            finally
            {
                _loadingProfileFields = false;
            }
        }
        catch { }
    }

    private void ProfileFieldChanged(object sender, TextChangedEventArgs e)
    {
        if (_loadingProfileFields || _busy) return;
        _profileChangedLocally = true;
    }

    private async void SaveClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        try
        {
            _busy = true;
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Text = "جارٍ الحفظ...";
            }

            var name = NameEntry?.Text ?? "";
            var status = StatusEntry?.Text ?? "";
            if (_api.HasToken)
            {
                // Update the local profile only after the server accepts the change.
                // This prevents the UI from showing unsaved data after a network/API failure.
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
            await DisplayAlertAsync("تم الحفظ", "تم تحديث الملف الشخصي.", "حسنًا");
        }
        catch (ArgumentException ex)
        {
            await DisplayAlertAsync("تنبيه", ex.Message, "حسنًا");
        }
        catch (HttpRequestException)
        {
            await DisplayAlertAsync("تعذر الحفظ", "تعذر الاتصال بالخادم. لم يتم تغيير البيانات المحلية.", "حسنًا");
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
            _busy = false;
            if (sender is Button button)
            {
                button.IsEnabled = true;
                button.Text = "حفظ التغييرات";
            }
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("..");
    }
}
