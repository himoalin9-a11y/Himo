using Himo.Models;
using Himo.Services;

namespace Himo.Views;

public partial class LoginPage : ContentPage
{
    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private bool _registerMode;
    private bool _verificationPending;
    private bool _busy;
    private string _pendingEmail = string.Empty;
    private string _pendingName = string.Empty;
    private string _pendingPassword = string.Empty;

    public LoginPage(AccountService account, HimoApiClient api)
    {
        InitializeComponent();
        _account = account;
        _api = api;
        PasswordEyeButton.Source = "eye_open.png";
        PasswordEyeButton.IsVisible = true;
        PasswordEyeButton.Opacity = 1;
        UpdateModeUi();
    }

    private async void PrimaryClicked(object sender, EventArgs e)
    {
        if (_busy) return;

        if (_registerMode && _verificationPending)
        {
            await VerifyEmailAsync();
            return;
        }

        var email = EmailEntry?.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry?.Text ?? string.Empty;
        var name = NameEntry?.Text?.Trim() ?? string.Empty;

        if (!IsValidEmail(email))
        {
            await DisplayAlertAsync("تنبيه", "أدخل بريدًا إلكترونيًا صحيحًا.", "حسنًا");
            return;
        }
        if (password.Length < 8)
        {
            await DisplayAlertAsync("تنبيه", "كلمة المرور يجب أن تكون 8 أحرف على الأقل.", "حسنًا");
            return;
        }
        if (_registerMode && string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("تنبيه", "الاسم مطلوب عند إنشاء الحساب.", "حسنًا");
            return;
        }

        try
        {
            SetBusy(true);
            if (_registerMode)
            {
                _pendingEmail = email.ToLowerInvariant();
                _pendingName = name;
                _pendingPassword = password;
                StatusLabel.Text = "جارٍ إرسال رمز التحقق إلى بريدك الإلكتروني...";
                await _api.RequestEmailVerificationAsync(_pendingEmail, _pendingPassword, _pendingName);
                _verificationPending = true;
                UpdateModeUi();
                VerificationCodeEntry.Focus();
                StatusLabel.Text = "تم إرسال رمز التحقق. أدخل الرمز لإكمال إنشاء الحساب.";
                await DisplayAlertAsync("تحقق البريد الإلكتروني", "أرسلنا رمز تحقق من 6 أرقام إلى بريدك الإلكتروني. أدخل الرمز لإكمال إنشاء الحساب.", "حسنًا");
            }
            else
            {
                StatusLabel.Text = "جارٍ تسجيل الدخول...";
                var account = await _api.LoginAsync(email, password);
                await CompleteSignInAsync(account);
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = _registerMode ? "تعذر إرسال رمز التحقق." : "تعذر تسجيل الدخول.";
            await DisplayAlertAsync(_registerMode ? "تحقق البريد الإلكتروني" : "تسجيل الدخول", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task VerifyEmailAsync()
    {
        var code = VerificationCodeEntry?.Text?.Trim() ?? string.Empty;
        if (code.Length != 6 || code.Any(ch => ch < '0' || ch > '9'))
        {
            await DisplayAlertAsync("تنبيه", "أدخل رمز التحقق المكوّن من 6 أرقام.", "حسنًا");
            return;
        }

        try
        {
            SetBusy(true);
            StatusLabel.Text = "جارٍ التحقق من البريد الإلكتروني...";
            var account = await _api.VerifyEmailAsync(_pendingEmail, code);
            _verificationPending = false;
            await CompleteSignInAsync(account);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "رمز التحقق غير صحيح أو منتهي.";
            await DisplayAlertAsync("تحقق البريد الإلكتروني", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ResendClicked(object sender, EventArgs e)
    {
        if (_busy || !_verificationPending) return;
        try
        {
            SetBusy(true);
            StatusLabel.Text = "جارٍ إعادة إرسال رمز التحقق...";
            await _api.RequestEmailVerificationAsync(_pendingEmail, _pendingPassword, _pendingName);
            StatusLabel.Text = "تم إرسال رمز تحقق جديد إلى بريدك الإلكتروني.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "تعذر إعادة إرسال الرمز.";
            await DisplayAlertAsync("إعادة إرسال الرمز", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CompleteSignInAsync(Account account)
    {
        _account.SignIn(account.Email, account.Name);
#if ANDROID
        _ = AppRuntimeBridge.RegisterCurrentPushTokenAsync();
#endif
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window != null) window.Page = new AppShell();
        AppRuntimeBridge.MarkSessionUnlocked();
        await Task.CompletedTask;
    }

    private async void ForgotPasswordClicked(object sender, EventArgs e)
    {
        if (_busy || _registerMode) return;
        var email = await DisplayPromptAsync("استعادة كلمة المرور", "أدخل بريدك الإلكتروني لإرسال رمز إعادة التعيين.", "إرسال الرمز", "إلغاء", "name@example.com", 254, Keyboard.Email, EmailEntry?.Text);
        if (string.IsNullOrWhiteSpace(email)) return;
        email = email.Trim();
        try
        {
            SetBusy(true);
            StatusLabel.Text = "جارٍ إرسال رمز إعادة التعيين...";
            await _api.RequestPasswordResetAsync(email);
            var code = await DisplayPromptAsync("رمز إعادة التعيين", "أدخل رمز التحقق المكوّن من 6 أرقام المرسل إلى بريدك.", "متابعة", "إلغاء", "123456", 6, Keyboard.Numeric);
            if (string.IsNullOrWhiteSpace(code)) return;
            var newPassword = await DisplayPromptAsync("كلمة مرور جديدة", "أدخل كلمة المرور الجديدة (8 أحرف على الأقل).", "تغيير كلمة المرور", "إلغاء", "كلمة المرور", 128, Keyboard.Default);
            if (string.IsNullOrWhiteSpace(newPassword)) return;
            var confirm = await DisplayPromptAsync("تأكيد كلمة المرور", "أعد إدخال كلمة المرور الجديدة.", "تأكيد", "إلغاء", "كلمة المرور", 128, Keyboard.Default);
            if (newPassword != confirm)
            {
                await DisplayAlertAsync("استعادة كلمة المرور", "كلمتا المرور غير متطابقتين.", "حسنًا");
                return;
            }
            StatusLabel.Text = "جارٍ تحديث كلمة المرور...";
            var account = await _api.ResetPasswordAsync(email, code.Trim(), newPassword);
            await CompleteSignInAsync(account);
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "تعذر استعادة كلمة المرور.";
            await DisplayAlertAsync("استعادة كلمة المرور", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void TogglePasswordClicked(object sender, EventArgs e)
    {
        if (PasswordEntry is null || PasswordEyeButton is null) return;

        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        PasswordEyeButton.Source = PasswordEntry.IsPassword ? "eye_open.png" : "eye_closed.png";
        PasswordEyeButton.IsVisible = true;
        PasswordEyeButton.Opacity = 1;
        PasswordEntry.CursorPosition = PasswordEntry.Text?.Length ?? 0;
        PasswordEntry.Focus();
    }

    private void ModeClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        _registerMode = !_registerMode;
        _verificationPending = false;
        _pendingEmail = string.Empty;
        _pendingName = string.Empty;
        _pendingPassword = string.Empty;
        VerificationCodeEntry.Text = string.Empty;
        UpdateModeUi();
        StatusLabel.Text = string.Empty;
    }

    private void UpdateModeUi()
    {
        if (PrimaryButton is null || ModeButton is null || NameBorder is null || NameLabel is null) return;
        PrimaryButton.Text = _registerMode ? (_verificationPending ? "تأكيد البريد الإلكتروني" : "إرسال رمز التحقق") : "تسجيل الدخول";
        ModeButton.Text = _registerMode ? "لدي حساب بالفعل" : "إنشاء حساب جديد";
        NameBorder.IsVisible = _registerMode;
        NameLabel.IsVisible = _registerMode;
        VerificationBorder.IsVisible = _registerMode && _verificationPending;
        VerificationLabel.IsVisible = _registerMode && _verificationPending;
        ResendButton.IsVisible = _registerMode && _verificationPending;
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
        PrimaryButton.IsEnabled = !value;
        ModeButton.IsEnabled = !value;
        ForgotPasswordButton.IsEnabled = !value;
        ResendButton.IsEnabled = !value;
        EmailEntry.IsEnabled = !value && !_verificationPending;
        NameEntry.IsEnabled = !value && !_verificationPending;
        PasswordEntry.IsEnabled = !value && !_verificationPending;
        VerificationCodeEntry.IsEnabled = !value;
        BusyIndicator.IsVisible = value;
        BusyIndicator.IsRunning = value;
        PrimaryButton.Opacity = value ? 0.6 : 1;
    }
}
