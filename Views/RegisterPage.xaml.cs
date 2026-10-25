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
    private bool _closing;
    private bool _verificationPending;
    private string _pendingEmail = string.Empty;
    private string _pendingName = string.Empty;
    private string _pendingPhoneNumber = string.Empty;
    private string _pendingPassword = string.Empty;
    private sealed class CountryDialOption
    {
        public CountryDialOption(string label, string dialCode)
        {
            Label = label;
            DialCode = dialCode;
        }

        public string Label { get; }
        public string DialCode { get; }
    }

    private static readonly CountryDialOption[] CountryDialOptions =
    {
        new("🇸🇦 السعودية (+966)", "+966"),
        new("🇦🇪 الإمارات (+971)", "+971"),
        new("🇰🇼 الكويت (+965)", "+965"),
        new("🇧🇭 البحرين (+973)", "+973"),
        new("🇶🇦 قطر (+974)", "+974"),
        new("🇴🇲 عُمان (+968)", "+968"),
        new("🇾🇪 اليمن (+967)", "+967"),
        new("🇯🇴 الأردن (+962)", "+962"),
        new("🇵🇸 فلسطين (+970)", "+970"),
        new("🇮🇶 العراق (+964)", "+964"),
        new("🇪🇬 مصر (+20)", "+20"),
        new("🇸🇾 سوريا (+963)", "+963"),
        new("🇱🇧 لبنان (+961)", "+961"),
        new("🇸🇩 السودان (+249)", "+249"),
        new("🇲🇦 المغرب (+212)", "+212"),
        new("🇩🇿 الجزائر (+213)", "+213"),
        new("🇹🇳 تونس (+216)", "+216"),
        new("🇱🇾 ليبيا (+218)", "+218"),
        new("🇹🇷 تركيا (+90)", "+90"),
        new("🇵🇰 باكستان (+92)", "+92"),
        new("🇮🇳 الهند (+91)", "+91"),
        new("🇧🇩 بنغلاديش (+880)", "+880"),
        new("🇵🇭 الفلبين (+63)", "+63"),
        new("🇮🇩 إندونيسيا (+62)", "+62"),
        new("🇺🇸 الولايات المتحدة (+1)", "+1"),
        new("🇨🇦 كندا (+1)", "+1"),
        new("🇬🇧 المملكة المتحدة (+44)", "+44"),
        new("🇩🇪 ألمانيا (+49)", "+49"),
        new("🇫🇷 فرنسا (+33)", "+33"),
        new("🇪🇸 إسبانيا (+34)", "+34"),
        new("🇮🇹 إيطاليا (+39)", "+39"),
        new("🇳🇬 نيجيريا (+234)", "+234"),
        new("🇸🇴 الصومال (+252)", "+252"),
        new("🇦🇫 أفغانستان (+93)", "+93"),
        new("🇲🇾 ماليزيا (+60)", "+60"),
        new("🇱🇰 سريلانكا (+94)", "+94")
    };

    public RegisterPage(AccountService account, HimoApiClient api)
    {
        InitializeComponent();
        Himo.Services.AppLocalization.AttachToPage(this);
        _account = account;
        _api = api;
        AnchorX = 1.0;
        AnchorY = 0.5;
        InitializeCountryCodePicker();
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
        var phone = NormalizePhoneNumber(PhoneEntry?.Text, CountryCodePicker?.SelectedItem as CountryDialOption);
        var password = PasswordEntry?.Text ?? string.Empty;
        var confirmPassword = ConfirmPasswordEntry?.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الاسم"), "أدخل اسمًا صحيحًا لا يتجاوز 60 حرفًا.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            NameEntry?.Focus();
            return;
        }
        if (!IsValidEmail(email))
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("البريد الإلكتروني"), Himo.Services.AppLocalization.TranslateSourceText("أدخل بريدًا إلكترونيًا صحيحًا."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            EmailEntry?.Focus();
            return;
        }
        if (!IsValidOptionalPhone(phone))
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("رقم الهاتف"), "أدخل رقم هاتف صحيحًا من 7 إلى 15 رقمًا، أو اترك الحقل فارغًا لأنه اختياري.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            PhoneEntry?.Focus();
            return;
        }
        if (password.Length < 8 || password.Length > 128)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("كلمة المرور"), "يجب أن تتكون كلمة المرور من 8 أحرف على الأقل.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            PasswordEntry?.Focus();
            return;
        }
        if (password != confirmPassword)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تأكيد كلمة المرور"), Himo.Services.AppLocalization.TranslateSourceText("كلمتا المرور غير متطابقتين."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            ConfirmPasswordEntry?.Focus();
            return;
        }
        if (TermsCheckBox?.IsChecked != true)
        {
            SetStatus("يجب الموافقة على شروط الاستخدام وسياسة الخصوصية للمتابعة.");
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الموافقة مطلوبة"), "يرجى قراءة شروط الاستخدام وسياسة الخصوصية والموافقة عليهما قبل إنشاء الحساب.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
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
                Himo.Services.AppLocalization.TranslateSourceText("تحقق البريد الإلكتروني"),
                Himo.Services.AppLocalization.TranslateSourceText("أرسلنا رمز تحقق من 6 أرقام إلى بريدك الإلكتروني. أدخل الرمز لإكمال إنشاء الحساب."),
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            VerificationCodeEntry?.Focus();
        }
        catch (Exception ex)
        {
            SetStatus("تعذر إرسال رمز التحقق.");
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إنشاء الحساب"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("رمز التحقق"), Himo.Services.AppLocalization.TranslateSourceText("أدخل رمز التحقق المكوّن من 6 أرقام."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تحقق البريد الإلكتروني"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
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
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إعادة إرسال الرمز"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
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

    /// <summary>Prepare this native page at the folded edge before it is presented.</summary>
    public void PrepareForPageFlip()
    {
        AnchorX = 1.0;
        AnchorY = 0.5;
        RotationY = -82;
        TranslationX = 18;
        Scale = 0.965;
    }

    /// <summary>Unfold the actual registration form after its modal host is shown.</summary>
    public Task AnimatePageFlipInAsync() => AnimatePageFlipAsync(
        -82, 0, 18, 0, 0.965, 1.0, 260, Easing.CubicOut);

    private Task AnimatePageFlipOutAsync() => AnimatePageFlipAsync(
        0, -82, 0, 18, 1.0, 0.965, 210, Easing.CubicIn);

    private Task AnimatePageFlipAsync(
        double rotationFrom, double rotationTo,
        double translationFrom, double translationTo,
        double scaleFrom, double scaleTo,
        uint duration, Easing easing)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var animation = new Animation();
        animation.Add(0, 1, new Animation(value => RotationY = value, rotationFrom, rotationTo));
        animation.Add(0, 1, new Animation(value => TranslationX = value, translationFrom, translationTo));
        animation.Add(0, 1, new Animation(value => Scale = value, scaleFrom, scaleTo));
        animation.Commit(this, "HimoRegisterPageFlip", 16, duration, easing, (value, cancelled) =>
        {
            if (!cancelled)
            {
                RotationY = rotationTo;
                TranslationX = translationTo;
                Scale = scaleTo;
            }
            completion.TrySetResult(true);
        });
        return completion.Task;
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        if (_busy || _closing) return;
        _closing = true;
        try
        {
            await UiMotion.PressAsync(sender as VisualElement);
            await CloseRegistrationCoreAsync();
        }
        finally
        {
            _closing = false;
        }
    }

    private async Task CloseRegistrationAsync()
    {
        if (_busy || _closing) return;
        _closing = true;
        try
        {
            await CloseRegistrationCoreAsync();
        }
        finally
        {
            _closing = false;
        }
    }

    private async Task CloseRegistrationCoreAsync()
    {
        try
        {
            if (Navigation.ModalStack.Count > 0)
            {
                await AnimatePageFlipOutAsync();
                await Navigation.PopModalAsync(animated: false);
                return;
            }
            if (Navigation.NavigationStack.Count > 1)
            {
                await AnimatePageFlipOutAsync();
                await Navigation.PopAsync(animated: false);
                return;
            }

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new LoginPage(_account, _api);
        }
        catch (InvalidOperationException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo AuthNav] Return-to-login fallback: {ex.Message}");
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new LoginPage(_account, _api);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        if (_busy || _closing) return true;
        _ = CloseRegistrationAsync();
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

    private void InitializeCountryCodePicker()
    {
        if (CountryCodePicker is null) return;
        CountryCodePicker.ItemsSource = CountryDialOptions;
        CountryCodePicker.ItemDisplayBinding = new Binding(nameof(CountryDialOption.Label));
        CountryCodePicker.SelectedItem = CountryDialOptions[0];
    }

    private static string NormalizePhoneNumber(string? value, CountryDialOption? selectedCountry)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        var startsWithPlus = trimmed.StartsWith("+", StringComparison.Ordinal);
        var plusCount = trimmed.Count(ch => ch == '+');
        if (plusCount > 1 || (plusCount == 1 && !startsWithPlus)) return trimmed;

        var digitBuilder = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (char.IsDigit(ch))
            {
                var numeric = (int)char.GetNumericValue(ch);
                if (numeric < 0 || numeric > 9) return trimmed;
                digitBuilder.Append((char)('0' + numeric));
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '(' or ')')
            {
                continue;
            }
            else if (ch == '+' && startsWithPlus)
            {
                continue;
            }
            else
            {
                return trimmed;
            }
        }

        var digits = digitBuilder.ToString();
        if (digits.Length == 0) return trimmed;
        if (startsWithPlus) return "+" + digits;
        if (trimmed.StartsWith("00", StringComparison.Ordinal) && digits.StartsWith("00", StringComparison.Ordinal))
            return "+" + digits[2..];

        var dialCode = selectedCountry?.DialCode ?? "+966";
        var dialDigits = dialCode.TrimStart('+');
        if (digits.StartsWith(dialDigits, StringComparison.Ordinal)) return "+" + digits;
        if (digits.StartsWith("0", StringComparison.Ordinal)) digits = digits[1..];
        return dialCode + digits;
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
        if (CountryCodePicker is not null) CountryCodePicker.IsEnabled = editDetails;
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
