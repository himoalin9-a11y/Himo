using Himo.Models;
using Himo.Services;

namespace Himo.Views;

public partial class ForgotPasswordPage : ContentPage
{
    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private int _busy;
    private bool _codeRequested;

    public ForgotPasswordPage(AccountService account, HimoApiClient api, string? initialEmail = null)
    {
        InitializeComponent();
        Himo.Services.AppLocalization.AttachToPage(this);
        _account = account;
        _api = api;
        NavigationPage.SetHasNavigationBar(this, false);

        if (EmailEntry is not null)
            EmailEntry.Text = initialEmail?.Trim() ?? string.Empty;
        if (NewPasswordEyeButton is not null)
            NewPasswordEyeButton.Source = "himo_pro_ui_himo_login_eye_open.png";
        if (ConfirmPasswordEyeButton is not null)
            ConfirmPasswordEyeButton.Source = "himo_pro_ui_himo_login_eye_open.png";
    }

    private async void SendCodeClicked(object sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        var email = EmailEntry?.Text?.Trim() ?? string.Empty;
        if (!IsValidEmail(email))
        {
            SetStatus("أدخل بريدًا إلكترونيًا صحيحًا.");
            EmailEntry?.Focus();
            Interlocked.Exchange(ref _busy, 0);
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus(_codeRequested ? "جارٍ إرسال رمز جديد..." : "جارٍ طلب رمز التحقق...");
            await _api.RequestPasswordResetAsync(email);
            _codeRequested = true;
            if (ResetSection is not null) ResetSection.IsVisible = true;
            if (SendCodeButton is not null) SendCodeButton.Text = "إعادة إرسال رمز جديد";
            SetStatus("إذا كان البريد مسجلًا، فسيصلك رمز التحقق. راجع الوارد والرسائل غير المرغوب فيها.");
            CodeEntry?.Focus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Password Reset] Request failed: {ex}");
            SetStatus("تعذر طلب الرمز الآن. تحقق من اتصالك وحاول مرة أخرى.");
        }
        finally
        {
            SetBusy(false);
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async void ResetPasswordClicked(object sender, EventArgs e)
    {
        if (!_codeRequested || Interlocked.Exchange(ref _busy, 1) != 0) return;

        var email = EmailEntry?.Text?.Trim() ?? string.Empty;
        var code = PhoneNumberUtils.NormalizeDigits(CodeEntry?.Text?.Trim());
        var password = NewPasswordEntry?.Text ?? string.Empty;
        var confirmation = ConfirmPasswordEntry?.Text ?? string.Empty;

        if (!IsValidEmail(email))
        {
            SetStatus("أدخل بريدًا إلكترونيًا صحيحًا.");
            EmailEntry?.Focus();
            Interlocked.Exchange(ref _busy, 0);
            return;
        }
        if (!PhoneNumberUtils.IsValidVerificationCode(code))
        {
            SetStatus("أدخل رمز التحقق المكوّن من 6 أرقام.");
            CodeEntry?.Focus();
            Interlocked.Exchange(ref _busy, 0);
            return;
        }
        if (password.Length is < 8 or > 128)
        {
            SetStatus("يجب أن تتراوح كلمة المرور بين 8 و128 حرفًا.");
            NewPasswordEntry?.Focus();
            Interlocked.Exchange(ref _busy, 0);
            return;
        }
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            SetStatus("كلمتا المرور غير متطابقتين.");
            ConfirmPasswordEntry?.Focus();
            Interlocked.Exchange(ref _busy, 0);
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus("جارٍ التحقق وتحديث كلمة المرور...");
            var account = await _api.ResetPasswordAsync(email, code, password);
            _account.SignIn(account.UserId, account.Email, account.Name);
            AppRuntimeBridge.MarkSessionUnlocked();
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new AppShell();
#if ANDROID
            _ = AppRuntimeBridge.RegisterCurrentPushTokenAsync();
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Password Reset] Reset failed: {ex}");
            SetStatus("تعذر تغيير كلمة المرور. تأكد من صحة الرمز وأنه لم تنتهِ صلاحيته.");
        }
        finally
        {
            SetBusy(false);
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private void SendCodeCompleted(object sender, EventArgs e) => SendCodeClicked(sender, e);

    private void ResetPasswordCompleted(object sender, EventArgs e)
    {
        if (ResetPasswordButton is not null)
            ResetPasswordClicked(ResetPasswordButton, EventArgs.Empty);
    }

    private void TogglePasswordClicked(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, NewPasswordEyeButton) && NewPasswordEntry is { } passwordEntry && NewPasswordEyeButton is { } passwordEye)
        {
            passwordEntry.IsPassword = !passwordEntry.IsPassword;
            passwordEye.Source = passwordEntry.IsPassword
                ? "himo_pro_ui_himo_login_eye_open.png"
                : "himo_pro_ui_himo_login_eye_closed.png";
            passwordEntry.Focus();
        }
        else if (ReferenceEquals(sender, ConfirmPasswordEyeButton) && ConfirmPasswordEntry is { } confirmEntry && ConfirmPasswordEyeButton is { } confirmEye)
        {
            confirmEntry.IsPassword = !confirmEntry.IsPassword;
            confirmEye.Source = confirmEntry.IsPassword
                ? "himo_pro_ui_himo_login_eye_open.png"
                : "himo_pro_ui_himo_login_eye_closed.png";
            confirmEntry.Focus();
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        if (Volatile.Read(ref _busy) != 0) return;
        await UiMotion.PressAsync(sender as VisualElement);
        await ClosePageAsync();
    }

    private async Task ClosePageAsync()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0)
                await Navigation.PopModalAsync(animated: true);
            else if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync(animated: true);
            else
            {
                var window = Application.Current?.Windows.FirstOrDefault();
                if (window is not null)
                    window.Page = new LoginPage(_account, _api);
            }
        }
        catch (InvalidOperationException)
        {
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new LoginPage(_account, _api);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (Volatile.Read(ref _busy) != 0) return true;
        MainThread.BeginInvokeOnMainThread(async () => await ClosePageAsync());
        return true;
    }

    private static bool IsValidEmail(string value)
    {
        var email = value.Trim();
        if (email.Length is < 5 or > 254 || email.Contains(' ')) return false;
        var at = email.LastIndexOf('@');
        return at > 0 && at < email.Length - 1 && email[(at + 1)..].Contains('.');
    }

    private void SetStatus(string message)
    {
        if (StatusLabel is not null) StatusLabel.Text = message;
    }

    private void SetBusy(bool value)
    {
        if (SendCodeButton is not null) SendCodeButton.IsEnabled = !value;
        if (ResetPasswordButton is not null) ResetPasswordButton.IsEnabled = !value;
        if (EmailEntry is not null) EmailEntry.IsEnabled = !value;
        if (CodeEntry is not null) CodeEntry.IsEnabled = !value && _codeRequested;
        if (NewPasswordEntry is not null) NewPasswordEntry.IsEnabled = !value && _codeRequested;
        if (ConfirmPasswordEntry is not null) ConfirmPasswordEntry.IsEnabled = !value && _codeRequested;
        if (NewPasswordEyeButton is not null) NewPasswordEyeButton.IsEnabled = !value && _codeRequested;
        if (ConfirmPasswordEyeButton is not null) ConfirmPasswordEyeButton.IsEnabled = !value && _codeRequested;
        if (BusyIndicator is not null)
        {
            BusyIndicator.IsVisible = value;
            BusyIndicator.IsRunning = value;
        }
    }
}
