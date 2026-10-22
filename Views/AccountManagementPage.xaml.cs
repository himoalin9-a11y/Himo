using Himo.Services;

namespace Himo.Views;

public partial class AccountManagementPage : ContentPage
{
    private static int _routesRegistered;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ProfileService _profile;

    public AccountManagementPage(HimoApiClient api, AccountService account, ProfileService profile)
    {
        InitializeComponent();
        Himo.Services.AppLocalization.AttachToPage(this);
        _api = api;
        _account = account;
        _profile = profile;
        RegisterChildRoutes();
        UpdateSummaryFromLocalAccount();
        DisplayAccountPhoto(_profile.Profile.PhotoPath);
    }

    private static void RegisterChildRoutes()
    {
        if (Interlocked.Exchange(ref _routesRegistered, 1) != 0)
            return;

        try
        {
            Routing.RegisterRoute("account-username", typeof(AccountUsernamePage));
            Routing.RegisterRoute("account-phone", typeof(AccountPhonePage));
            Routing.RegisterRoute("account-login-details", typeof(AccountLoginDetailsPage));
            Routing.RegisterRoute("account-password", typeof(AccountPasswordPage));
            Routing.RegisterRoute("account-add", typeof(AddAccountPage));
        }
        catch
        {
            // Do not repeatedly register route names if Shell already registered a route.
            Interlocked.Exchange(ref _routesRegistered, 0);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
        UpdateSummaryFromLocalAccount();
        DisplayAccountPhoto(_profile.Profile.PhotoPath);
        if (!_api.HasToken)
        {
            AccountLoadStatusLabel.Text = "سجّل الدخول لعرض بيانات الحساب.";
            return;
        }

        try
        {
            AccountLoadStatusLabel.Text = "جارٍ تحديث بيانات الحساب...";
            var remote = await _api.GetMyProfileAsync();
            var name = string.IsNullOrWhiteSpace(remote.Name) ? _account.CurrentAccount?.Name ?? "حسابك في Himo" : remote.Name;
            var email = remote.Email ?? _account.CurrentAccount?.Email ?? string.Empty;
            var status = string.IsNullOrWhiteSpace(remote.Status) ? "متاح على Himo" : remote.Status;
            AccountNameSummaryLabel.Text = name;
            AccountEmailSummaryLabel.Text = email;
            AccountLoadStatusLabel.Text = "حسابك نشط الآن";
            _profile.Update(name, status);

            // Keep the account-management avatar in sync with the profile photo.
            // The locally saved photo is shown immediately; refresh it from the
            // server only when the profile reports that a photo exists.
            if (remote.HasProfilePhoto == true)
            {
                var photo = await _api.GetMyProfilePhotoAsync();
                if (photo is not null)
                {
                    var localPath = _profile.SavePhoto(photo.Bytes, photo.ContentType);
                    DisplayAccountPhoto(localPath);
                }
            }
            else if (remote.HasProfilePhoto == false)
            {
                _profile.ClearPhoto();
                DisplayAccountPhoto(null);
            }

            if (_account.CurrentAccount is not null && !string.IsNullOrWhiteSpace(email))
                _account.SignIn(remote.UserId, email, name);
        }
        catch (Exception ex)
        {
            AccountLoadStatusLabel.Text = "تعذر تحديث البيانات؛ تظهر المعلومات المحفوظة محليًا.";
            System.Diagnostics.Debug.WriteLine($"[Himo Account] Failed to refresh account summary: {ex}");
        }
    }

    private void UpdateSummaryFromLocalAccount()
    {
        var account = _account.CurrentAccount;
        AccountNameSummaryLabel.Text = string.IsNullOrWhiteSpace(account?.Name) ? "حسابك في Himo" : account.Name;
        AccountEmailSummaryLabel.Text = account?.Email ?? string.Empty;
    }

    private void DisplayAccountPhoto(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        if (AccountProfilePhotoImage is not null)
        {
            AccountProfilePhotoImage.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
            AccountProfilePhotoImage.IsVisible = hasPhoto;
        }

        if (AccountPhotoPlaceholderLabel is not null)
            AccountPhotoPlaceholderLabel.IsVisible = !hasPhoto;
    }

    private async Task NavigateAsync(string route, Func<ContentPage> fallbackFactory)
    {
        try
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync(route, false);
            else
                await Navigation.PushAsync(fallbackFactory());
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إدارة الحساب"), "تعذر فتح الصفحة المطلوبة: " + ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void OpenUsernamePage(object? sender, TappedEventArgs e) =>
        await NavigateAsync("account-username", () => new AccountUsernamePage(_api, _account, _profile));

    private async void OpenPhonePage(object? sender, TappedEventArgs e) =>
        await NavigateAsync("account-phone", () => new AccountPhonePage(_api));

    private async void OpenLoginDetailsPage(object? sender, TappedEventArgs e) =>
        await NavigateAsync("account-login-details", () => new AccountLoginDetailsPage(_api, _account, _profile));

    private async void OpenPasswordPage(object? sender, TappedEventArgs e) =>
        await NavigateAsync("account-password", () => new AccountPasswordPage(_api, _account));

    private async void OpenAddAccountPage(object? sender, TappedEventArgs e) =>
        await NavigateAsync("account-add", () => new AddAccountPage(_api, _account, _profile));

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..", false);
        else if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }
}
