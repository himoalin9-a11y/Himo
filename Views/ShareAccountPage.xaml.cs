using Himo.Services;

namespace Himo.Views;

public partial class ShareAccountPage : ContentPage
{
    private readonly ProfileService _profile;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private string? _accountShareUri;
    private string _shareDisplayName = "مستخدم Himo";
    private bool _isLoading;
    private Task? _loadTask;

    public ShareAccountPage(ProfileService profile, HimoApiClient api, AccountService account)
    {
        InitializeComponent();
        _profile = profile;
        _api = api;
        _account = account;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this);
        _ = SwipePageTransition.AnimateIncomingAsync(this);
        _ = LoadShareAccountAsync();
    }

    private Task LoadShareAccountAsync()
    {
        if (_isLoading && _loadTask is not null)
            return _loadTask;

        _isLoading = true;
        _loadTask = LoadShareAccountCoreAsync();
        return _loadTask;
    }

    private async Task LoadShareAccountCoreAsync()
    {
        try
        {
            // Build a usable share link from the cached account immediately. This
            // keeps the screen responsive even when the network is unavailable.
            var cached = _account.CurrentAccount;
            if (cached is not null && cached.UserId != Guid.Empty)
                UpdateShareData(cached.UserId, _profile.Profile.Name);

            // A cold Android start may have created the page before SecureStorage
            // finished loading the bearer token. Wait before concluding that the
            // current account cannot be shared.
            try { await _api.TokenInitialization; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Share] Token initialization: {ex.Message}"); }

            if (_api.HasToken)
            {
                try
                {
                    var remote = await _api.GetMyProfileAsync();
                    if (remote.UserId != Guid.Empty)
                    {
                        _account.SetUserId(remote.UserId);
                        UpdateShareData(remote.UserId, remote.Name);
                    }
                }
                catch (Exception ex)
                {
                    // Keep cached share data available when the server cannot be reached.
                    System.Diagnostics.Debug.WriteLine($"[Himo Share] Profile lookup: {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(_accountShareUri))
            {
                QrPlaceholderLabel.Text = _api.HasToken
                    ? "تعذر تجهيز الرمز. تحقق من اتصالك ثم أعد المحاولة."
                    : "سجّل الدخول أولًا لإنشاء رابط حسابك.";
                QrPlaceholderLabel.IsVisible = true;
                AccountQrCode.IsVisible = false;
                AccountLinkLabel.Text = "رابط الحساب غير متاح حاليًا.";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Share] Unexpected setup failure: {ex}");
            QrPlaceholderLabel.Text = "تعذر تجهيز رمز الحساب. حاول مجددًا.";
            QrPlaceholderLabel.IsVisible = true;
            AccountQrCode.IsVisible = false;
            AccountLinkLabel.Text = "رابط الحساب غير متاح حاليًا.";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateShareData(Guid userId, string? name)
    {
        if (userId == Guid.Empty) return;
        var safeName = string.IsNullOrWhiteSpace(name) ? "مستخدم Himo" : name.Trim();
        _shareDisplayName = safeName;
        var nameToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(safeName))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        _accountShareUri = $"himo://user/{userId:D}?name64={nameToken}";
        AccountLinkLabel.Text = _accountShareUri;
        AccountQrCode.Value = _accountShareUri;
        AccountQrCode.IsVisible = true;
        QrPlaceholderLabel.IsVisible = false;
    }

    private async void ShowQrClicked(object? sender, TappedEventArgs e)
    {
        await UiMotion.PressAsync(sender as VisualElement);
        if (!await EnsureShareLinkAvailableAsync()) return;
        QrPanel.IsVisible = true;
        AccountQrCode.IsVisible = true;
        QrPlaceholderLabel.IsVisible = false;
    }

    private async void ShareProfileClicked(object? sender, TappedEventArgs e)
    {
        await ShareLinkAsync();
    }

    private async Task ShareLinkAsync()
    {
        if (!await EnsureShareLinkAvailableAsync()) return;
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "حسابي على Himo",
                Text = $"تواصل معي على Himo: {_shareDisplayName}\n{_accountShareUri}"
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذرت المشاركة", ex.Message, "حسنًا");
        }
    }

    private async void CopyProfileLinkClicked(object? sender, TappedEventArgs e)
    {
        if (!await EnsureShareLinkAvailableAsync()) return;
        try
        {
            await Clipboard.Default.SetTextAsync(_accountShareUri);
            await DisplayAlertAsync("تم النسخ", "تم نسخ رابط حسابك إلى الحافظة.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر النسخ", ex.Message, "حسنًا");
        }
    }

    private async Task<bool> EnsureShareLinkAvailableAsync()
    {
        if (string.IsNullOrWhiteSpace(_accountShareUri))
            await LoadShareAccountAsync();

        if (!string.IsNullOrWhiteSpace(_accountShareUri))
            return true;

        await DisplayAlertAsync(
            "رابط الحساب",
            _api.HasToken
                ? "تعذر تجهيز رابط الحساب. تحقق من اتصالك ثم أعد المحاولة."
                : "سجّل الدخول إلى Himo أولًا حتى تتمكن من مشاركة حسابك.",
            "حسنًا");
        return false;
    }

    private async void PageSwiped(object? sender, SwipedEventArgs e)
    {
        if (e.Direction == SwipeDirection.Left)
            await SwipePageTransition.NavigateAsync(this, "settings", SwipeDirection.Left);
        else
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }

    private async void ChatsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "chats", SwipeDirection.Right);

    private async void CallsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "calls", SwipeDirection.Right);

    private async void PeopleNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateToHomeSectionAsync(this, "phone", SwipeDirection.Right);

    private async void ProfileNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);

    private async void SettingsNavTapped(object? sender, TappedEventArgs e) =>
        await SwipePageTransition.NavigateAsync(this, "settings", SwipeDirection.Left);

    private async void BackClicked(object? sender, EventArgs e) =>
        await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
}
