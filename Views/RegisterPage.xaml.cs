using Himo.Models;
using Himo.Services;

namespace Himo.Views;

public partial class RegisterPage : ContentPage
{
    // Keep these values aligned with Himo.Api/Program.cs and bump them when legal text changes.
    private const string TermsVersion = "terms-2026-10-18-v1";
    private const string PrivacyVersion = "privacy-2026-10-18-v1";

    private readonly AccountService _account;
    private readonly HimoApiClient _api;
    private bool _busy;
    private bool _verificationPending;
    private string _pendingEmail = string.Empty;
    private string _pendingName = string.Empty;
    private string _pendingPhoneNumber = string.Empty;
    private string _pendingPassword = string.Empty;

    public RegisterPage(AccountService account, HimoApiClient api)
    {
        InitializeComponent();
        _account = account;
        _api = api;
        NavigationPage.SetHasNavigationBar(this, false);
        if (PasswordEyeButton is not null)
            PasswordEyeButton.Source = "himo_pro_ui_himo_login_eye_open.png";
        if (ConfirmPasswordEyeButton is not null)
            ConfirmPasswordEyeButton.Source = "himo_pro_ui_himo_login_eye_open.png";
        ApplyVerificationState();
    }

    private async void PrimaryClicked(object sender, EventArgs e)
    {
        if (_busy || PrimaryButton is null) return;
        if (sender is VisualElement visual)
            await UiMotion.PressAsync(visual);

        if (_verificationPending)
        {
            await VerifyEmailAsync();
            return;
        }

        var name = NameEntry?.Text?.Trim() ?? string.Empty;
        var email = EmailEntry?.Text?.Trim() ?? string.Empty;
        var phone = PhoneEntry?.Text?.Trim() ?? string.Empty;
        var password = PasswordEntry?.Text ?? string.Empty;
        var confirmPassword = ConfirmPasswordEntry?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
        {
            await DisplayAlertAsync("الاسم", "أدخل اسمًا صحيحًا لا يتجاوز 60 حرفًا.", "حسنًا");
            NameEntry?.Focus();
            return;
        }
        if (!IsValidEmail(email))
        {
            await DisplayAlertAsync("البريد الإلكتروني", "أدخل بريدًا إلكترونيًا صحيحًا.", "حسنًا");
            EmailEntry?.Focus();
            return;
        }
        if (!IsValidOptionalPhone(phone))
        {
            await DisplayAlertAsync("رقم الهاتف", "أدخل رقم هاتف صحيحًا من 7 إلى 15 رقمًا، أو اترك الحقل فارغًا لأنه اختياري.", "حسنًا");
            PhoneEntry?.Focus();
            return;
        }
        if (password.Length < 8 || password.Length > 128)
        {
            await DisplayAlertAsync("كلمة المرور", "يجب أن تتكون كلمة المرور من 8 أحرف على الأقل.", "حسنًا");
            PasswordEntry?.Focus();
            return;
        }
        if (password != confirmPassword)
        {
            await DisplayAlertAsync("تأكيد كلمة المرور", "كلمتا المرور غير متطابقتين.", "حسنًا");
            ConfirmPasswordEntry?.Focus();
            return;
        }
        if (TermsCheckBox?.IsChecked != true)
        {
            SetStatus("يجب الموافقة على شروط الاستخدام وسياسة الخصوصية للمتابعة.");
            await DisplayAlertAsync("الموافقة مطلوبة", "يرجى قراءة شروط الاستخدام وسياسة الخصوصية والموافقة عليهما قبل إنشاء الحساب.", "حسنًا");
            return;
        }

        _pendingEmail = email.ToLowerInvariant();
        _pendingName = name;
        _pendingPhoneNumber = phone;
        _pendingPassword = password;

        try
        {
            SetBusy(true);
            SetStatus("جارٍ إرسال رمز التحقق إلى بريدك الإلكتروني...");
            await _api.RequestEmailVerificationAsync(
                _pendingEmail,
                _pendingPassword,
                _pendingName,
                contactPhoneNumber: _pendingPhoneNumber,
                termsAccepted: TermsCheckBox?.IsChecked == true,
                privacyAccepted: TermsCheckBox?.IsChecked == true,
                termsVersion: TermsVersion,
                privacyVersion: PrivacyVersion);

            _verificationPending = true;
            ApplyVerificationState();
            SetStatus("أُرسل رمز التحقق. أدخل الرمز لإكمال إنشاء حسابك.");
            await DisplayAlertAsync(
                "تحقق البريد الإلكتروني",
                "أرسلنا رمز تحقق من 6 أرقام إلى بريدك الإلكتروني. أدخل الرمز لإكمال إنشاء الحساب.",
                "حسنًا");
            VerificationCodeEntry?.Focus();
        }
        catch (Exception ex)
        {
            SetStatus("تعذر إرسال رمز التحقق.");
            await DisplayAlertAsync("إنشاء الحساب", ex.Message, "حسنًا");
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
            await DisplayAlertAsync("رمز التحقق", "أدخل رمز التحقق المكوّن من 6 أرقام.", "حسنًا");
            VerificationCodeEntry?.Focus();
            return;
        }

        try
        {
            SetBusy(true);
            SetStatus("جارٍ التحقق من البريد الإلكتروني...");
            var account = await _api.VerifyEmailAsync(_pendingEmail, code);
            await CompleteSignInAsync(account);
        }
        catch (Exception ex)
        {
            SetStatus("رمز التحقق غير صحيح أو منتهي الصلاحية.");
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
            SetStatus("جارٍ إعادة إرسال رمز التحقق...");
            await _api.RequestEmailVerificationAsync(
                _pendingEmail,
                _pendingPassword,
                _pendingName,
                contactPhoneNumber: _pendingPhoneNumber,
                termsAccepted: TermsCheckBox?.IsChecked == true,
                privacyAccepted: TermsCheckBox?.IsChecked == true,
                termsVersion: TermsVersion,
                privacyVersion: PrivacyVersion);
            SetStatus("أُرسل رمز تحقق جديد إلى بريدك الإلكتروني.");
        }
        catch (Exception ex)
        {
            SetStatus("تعذر إعادة إرسال رمز التحقق.");
            await DisplayAlertAsync("إعادة إرسال الرمز", ex.Message, "حسنًا");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CompleteSignInAsync(Account account)
    {
        _account.SignIn(account.UserId, account.Email, account.Name);
#if ANDROID
        _ = AppRuntimeBridge.RegisterCurrentPushTokenAsync();
#endif
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window is not null)
            window.Page = new AppShell();
        AppRuntimeBridge.MarkSessionUnlocked();
        await Task.CompletedTask;
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        if (_busy) return;
        await UiMotion.PressAsync(sender as VisualElement);
        await CloseRegistrationAsync();
    }

    private async Task CloseRegistrationAsync()
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
        if (_busy) return true;
        MainThread.BeginInvokeOnMainThread(async () => await CloseRegistrationAsync());
        return true;
    }

    private async void TermsTapped(object sender, TappedEventArgs e)
    {
        await OpenLegalPageAsync(new TermsOfServicePage());
    }

    private async void PrivacyTapped(object sender, TappedEventArgs e)
    {
        await OpenLegalPageAsync(new PrivacyPolicyPage());
    }

    private async Task OpenLegalPageAsync(ContentPage page)
    {
        if (_busy || page is null) return;
        NavigationPage.SetHasNavigationBar(page, false);
        try
        {
            await Navigation.PushAsync(page, animated: true);
        }
        catch (InvalidOperationException)
        {
            // The registration page should normally live inside a NavigationPage.
            // If the host navigation is unavailable, leave the current page intact.
        }
    }

    private void NameCompleted(object sender, EventArgs e) => EmailEntry?.Focus();
    private void EmailCompleted(object sender, EventArgs e) => PhoneEntry?.Focus();
    private void PhoneCompleted(object sender, EventArgs e) => PasswordEntry?.Focus();
    private void PasswordCompleted(object sender, EventArgs e) => ConfirmPasswordEntry?.Focus();

    private void ConfirmPasswordCompleted(object sender, EventArgs e)
    {
        if (!_busy && PrimaryButton is not null) PrimaryClicked(PrimaryButton, EventArgs.Empty);
    }

    private void VerificationCompleted(object sender, EventArgs e)
    {
        if (!_busy && _verificationPending && PrimaryButton is not null) PrimaryClicked(PrimaryButton, EventArgs.Empty);
    }

    private void TogglePasswordClicked(object sender, EventArgs e)
    {
        if (ReferenceEquals(sender, PasswordEyeButton) && PasswordEyeButton is { } passwordEye && PasswordEntry is { } passwordEntry)
        {
            passwordEntry.IsPassword = !passwordEntry.IsPassword;
            passwordEye.Source = passwordEntry.IsPassword
                ? "himo_pro_ui_himo_login_eye_open.png"
                : "himo_pro_ui_himo_login_eye_closed.png";
            passwordEntry.CursorPosition = passwordEntry.Text?.Length ?? 0;
            passwordEntry.Focus();
        }
        else if (ReferenceEquals(sender, ConfirmPasswordEyeButton) && ConfirmPasswordEyeButton is { } confirmEye && ConfirmPasswordEntry is { } confirmPasswordEntry)
        {
            confirmPasswordEntry.IsPassword = !confirmPasswordEntry.IsPassword;
            confirmEye.Source = confirmPasswordEntry.IsPassword
                ? "himo_pro_ui_himo_login_eye_open.png"
                : "himo_pro_ui_himo_login_eye_closed.png";
            confirmPasswordEntry.CursorPosition = confirmPasswordEntry.Text?.Length ?? 0;
            confirmPasswordEntry.Focus();
        }
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
        if (ReferenceEquals(entry, NameEntry)) return NameBorder;
        if (ReferenceEquals(entry, EmailEntry)) return EmailBorder;
        if (ReferenceEquals(entry, PhoneEntry)) return PhoneBorder;
        if (ReferenceEquals(entry, PasswordEntry)) return PasswordBorder;
        if (ReferenceEquals(entry, ConfirmPasswordEntry)) return ConfirmPasswordBorder;
        return null;
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

    private static bool IsValidOptionalPhone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim();
        if (normalized.Length > 25) return false;
        if (normalized.Any(ch => !char.IsDigit(ch) && ch is not ('+' or '-' or '(' or ')' or ' ') && !char.IsWhiteSpace(ch)))
            return false;
        if (normalized.Count(char.IsDigit) is < 7 or > 15) return false;
        var firstNonSpace = normalized.FirstOrDefault(ch => !char.IsWhiteSpace(ch));
        if (normalized.Count(ch => ch == '+') > 1 || (normalized.Contains('+') && firstNonSpace != '+'))
            return false;
        return true;
    }

    private void SetStatus(string text)
    {
        if (StatusLabel is not null)
            StatusLabel.Text = text;
    }

    private void ApplyVerificationState()
    {
        if (RegistrationFieldsContainer is not null)
            RegistrationFieldsContainer.IsVisible = !_verificationPending;
        if (VerificationContainer is not null)
            VerificationContainer.IsVisible = _verificationPending;
        if (PrimaryButton is not null)
            PrimaryButton.Text = _verificationPending ? "تأكيد البريد الإلكتروني" : "إنشاء الحساب";
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        var editDetails = !value && !_verificationPending;
        if (NameEntry is not null) NameEntry.IsEnabled = editDetails;
        if (EmailEntry is not null) EmailEntry.IsEnabled = editDetails;
        if (PhoneEntry is not null) PhoneEntry.IsEnabled = editDetails;
        if (PasswordEntry is not null) PasswordEntry.IsEnabled = editDetails;
        if (ConfirmPasswordEntry is not null) ConfirmPasswordEntry.IsEnabled = editDetails;
        if (PasswordEyeButton is not null) PasswordEyeButton.IsEnabled = editDetails;
        if (ConfirmPasswordEyeButton is not null) ConfirmPasswordEyeButton.IsEnabled = editDetails;
        if (TermsCheckBox is not null) TermsCheckBox.IsEnabled = editDetails;
        if (VerificationCodeEntry is not null) VerificationCodeEntry.IsEnabled = !value && _verificationPending;
        if (ResendButton is not null) ResendButton.IsEnabled = !value && _verificationPending;
        if (PrimaryButton is not null)
        {
            PrimaryButton.IsEnabled = !value;
            PrimaryButton.Opacity = value ? 0.7 : 1;
        }
        if (BusyIndicator is not null)
        {
            BusyIndicator.IsVisible = value;
            BusyIndicator.IsRunning = value;
        }
        ApplyVerificationState();
    }
}
