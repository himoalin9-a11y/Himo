using Himo.Models;
using Himo.Services;

namespace Himo.Views;

public partial class LoginPage : ContentPage
{
    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private bool _busy;

    public LoginPage(AccountService account, HimoApiClient api)
    {
        InitializeComponent();
        _account = account;
        _api = api;
        if (PasswordEyeButton is not null)
        {
            PasswordEyeButton.Source = "himo_pro_ui_himo_login_eye_open.png";
            PasswordEyeButton.IsVisible = true;
            PasswordEyeButton.Opacity = 1;
        }
    }

    private async void PrimaryClicked(object sender, EventArgs e)
    {
        var primaryButton = PrimaryButton;
        var emailEntry = EmailEntry;
        var passwordEntry = PasswordEntry;
        if (_busy || primaryButton is null || emailEntry is null || passwordEntry is null)
            return;

        if (sender is VisualElement visual)
            await UiMotion.PressAsync(visual);
        var email = emailEntry.Text?.Trim() ?? string.Empty;
        var password = passwordEntry.Text ?? string.Empty;

        if (!IsValidEmail(email))
        {
            await DisplayAlertAsync("تنبيه", "أدخل بريدًا إلكترونيًا صحيحًا.", "حسنًا");
            emailEntry.Focus();
            return;
        }
        if (password.Length < 8)
        {
            await DisplayAlertAsync("تنبيه", "كلمة المرور يجب أن تكون 8 أحرف على الأقل.", "حسنًا");
            passwordEntry.Focus();
            return;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Login] Starting login for {email}");
            SetBusy(true);
            SetStatus("جارٍ تسجيل الدخول...");
            var account = await _api.LoginAsync(email, password);
            System.Diagnostics.Debug.WriteLine($"[Himo Login] Login succeeded for {email}");
            await CompleteSignInAsync(account);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Login] Failed: {ex}");
            SetStatus("تعذر تسجيل الدخول.");
            await DisplayAlertAsync("تسجيل الدخول", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private Task CompleteSignInAsync(Account account)
    {
        _account.SignIn(account.UserId, account.Email, account.Name);
#if ANDROID
        _ = AppRuntimeBridge.RegisterCurrentPushTokenAsync();
#endif
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is not null)
            window.Page = new AppShell();
        AppRuntimeBridge.MarkSessionUnlocked();
        return Task.CompletedTask;
    }

    private async void ForgotPasswordClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        if (sender is VisualElement visual)
            await UiMotion.PressAsync(visual);

        try
        {
            var page = new ForgotPasswordPage(_account, _api, EmailEntry?.Text);
            NavigationPage.SetHasNavigationBar(page, false);
            await Navigation.PushModalAsync(new NavigationPage(page), animated: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Password Reset] Unable to open recovery page: {ex}");
            await DisplayAlertAsync("استعادة كلمة المرور", "تعذر فتح صفحة الاستعادة. حاول مرة أخرى.", "حسنًا");
        }
    }

    private void TogglePasswordClicked(object sender, EventArgs e)
    {
        if (PasswordEntry is not { } passwordEntry || PasswordEyeButton is not { } eyeButton) return;
        passwordEntry.IsPassword = !passwordEntry.IsPassword;
        eyeButton.Source = passwordEntry.IsPassword
            ? "himo_pro_ui_himo_login_eye_open.png"
            : "himo_pro_ui_himo_login_eye_closed.png";
        eyeButton.IsVisible = true;
        eyeButton.Opacity = 1;
        passwordEntry.CursorPosition = passwordEntry.Text?.Length ?? 0;
        passwordEntry.Focus();
    }

    private void EmailCompleted(object sender, EventArgs e) => PasswordEntry?.Focus();

    private void PasswordCompleted(object sender, EventArgs e)
    {
        if (!_busy && PrimaryButton is not null)
            PrimaryClicked(PrimaryButton, EventArgs.Empty);
    }

    private void InputFocused(object sender, FocusEventArgs e)
    {
        if (sender is not Entry entry) return;
        var border = GetBorderForEntry(entry);
        if (border is null) return;
        border.Stroke = new SolidColorBrush(GetThemeColor("HimoAccent", Colors.MediumPurple));
        border.StrokeThickness = 1.8;
    }

    private void InputUnfocused(object sender, FocusEventArgs e)
    {
        if (sender is not Entry entry) return;
        var border = GetBorderForEntry(entry);
        if (border is null) return;
        border.Stroke = new SolidColorBrush(GetThemeColor("HimoBorder", Colors.LightGray));
        border.StrokeThickness = 1;
    }

    private Border? GetBorderForEntry(Entry entry)
    {
        if (ReferenceEquals(entry, EmailEntry)) return EmailBorder;
        if (ReferenceEquals(entry, PasswordEntry)) return PasswordBorder;
        return null;
    }

    private async void ModeClicked(object sender, EventArgs e)
    {
        if (_busy || ModeButton is null) return;
        if (sender is VisualElement visual)
            await UiMotion.PressAsync(visual);
        var registrationPage = new RegisterPage(_account, _api);
        NavigationPage.SetHasNavigationBar(registrationPage, false);
        await Navigation.PushModalAsync(new NavigationPage(registrationPage), animated: true);
    }

    private void SetStatus(string text)
    {
        if (StatusLabel is not null)
            StatusLabel.Text = text;
    }

    private static Color GetThemeColor(string key, Color fallback)
    {
        var resources = Application.Current?.Resources;
        return resources is not null && resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : fallback;
    }

    private static bool IsValidEmail(string value)
    {
        var email = value.Trim();
        if (email.Length is < 5 or > 254 || email.Contains(' ')) return false;
        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1 && email[(at + 1)..].Contains('.');
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        if (PrimaryButton is not null) PrimaryButton.IsEnabled = !value;
        if (ModeButton is not null) ModeButton.IsEnabled = !value;
        if (ForgotPasswordButton is not null) ForgotPasswordButton.IsEnabled = !value;
        if (EmailEntry is not null) EmailEntry.IsEnabled = !value;
        if (PasswordEntry is not null) PasswordEntry.IsEnabled = !value;
        if (BusyIndicator is not null)
        {
            BusyIndicator.IsVisible = value;
            BusyIndicator.IsRunning = value;
        }
        if (PrimaryButton is not null) PrimaryButton.Opacity = value ? 0.65 : 1;
    }
}
