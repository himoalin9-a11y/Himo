using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>Dedicated profile-details screen. All editable values open their own screen.</summary>
internal sealed class ProfileDetailsPage : ProfileEditorPageBase
{
    private readonly ProfileService _profile;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Image _photo = new() { Aspect = Aspect.AspectFill, WidthRequest = 96, HeightRequest = 96 };
    private readonly Label _initial = new() { Text = "H", FontSize = 34, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
    private readonly Label _nameValue = Hint("جارٍ التحميل...");
    private readonly Label _aboutValue = Hint("متاح على Himo");
    private readonly Label _usernameValue = Hint("لم تحدد بعد");
    private readonly Label _phoneValue = Hint("غير مضاف");
    private readonly Label _emailValue = Hint("جارٍ التحميل...");

    public ProfileDetailsPage(ProfileService profile, HimoApiClient api, AccountService account)
        : base("بيانات الملف الشخصي", "حدّث معلومات حسابك كلٌّ على حدة")
    {
        _profile = profile;
        _api = api;
        _account = account;

        var avatar = new Border
        {
            WidthRequest = 108,
            HeightRequest = 108,
            Padding = 4,
            StrokeThickness = 2,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 54 },
            HorizontalOptions = LayoutOptions.Center,
            Content = new Grid
            {
                Children = { _photo, _initial }
            }
        };
        avatar.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        avatar.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
        avatar.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenPhotoPreviewAsync()) });

        var camera = new ImageButton
        {
            Source = "settings_icon_camera.png", WidthRequest = 38, HeightRequest = 38,
            Padding = 9, Aspect = Aspect.AspectFit, CornerRadius = 19,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = Color.FromArgb("#8B48E8"),
            BorderColor = Color.FromArgb("#E9D8FF"), BorderWidth = 1.5
        };
        SemanticProperties.SetDescription(camera, "تغيير الصورة بالكاميرا أو المعرض");
        camera.Clicked += async (_, _) => await ProfilePhotoWorkflow.ShowAsync(this, _profile, _api, UpdatePhotoUi);

        var avatarStack = new VerticalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            Children = { avatar, camera, Hint("اضغط على الصورة لتكبيرها، أو على الكاميرا لتغييرها") }
        };
        SetEditorContent(
            Card(avatarStack, new Thickness(14, 18)),
            Card(new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    FieldRow("الاسم", "الاسم الذي يظهر للآخرين", "himo_s9_user.png", _nameValue, async () => await PushEditorAsync(new ProfileDisplayNamePage(_profile, _api, _account))),
                    Separator(),
                    FieldRow("حول", "حالتك وماذا تفعل الآن", "himo_s9_info.png", _aboutValue, async () => await PushEditorAsync(new ProfileStatusPage(_profile, _api, _account))),
                    Separator(),
                    FieldRow("اسم المستخدم", "اسم فريد لحسابك", "himo_s9_user.png", _usernameValue, async () => await PushEditorAsync(new ProfileUsernamePage(_api))),
                    Separator(),
                    FieldRow("الهاتف", "رقم الاتصال المرتبط بحسابك", "himo_s9_phone.png", _phoneValue, async () => await PushEditorAsync(new AccountPhonePage(_api))),
                    Separator(),
                    FieldRow("البريد الإلكتروني", "البريد المستخدم لتسجيل الدخول", "himo_s9_mail.png", _emailValue, async () => await PushEditorAsync(new ProfileEmailPage(_api, _account)))
                }
            }, new Thickness(12, 4)),
            Hint("تُحفظ التغييرات على الحساب عند نجاح الاتصال بالخادم."));
        UpdateLocalSummary();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateLocalSummary();
        if (!_api.HasToken) return;
        try
        {
            var remote = await _api.GetMyProfileAsync();
            _profile.Update(remote.Name, remote.Status);
            _nameValue.Text = string.IsNullOrWhiteSpace(remote.Name) ? "غير محدد" : remote.Name;
            _aboutValue.Text = string.IsNullOrWhiteSpace(remote.Status) ? "متاح على Himo" : remote.Status;
            _usernameValue.Text = string.IsNullOrWhiteSpace(remote.Username) ? "لم تحدد بعد" : "@" + remote.Username;
            _phoneValue.Text = string.IsNullOrWhiteSpace(remote.ContactPhoneNumber) ? "غير مضاف" : remote.ContactPhoneNumber;
            _emailValue.Text = string.IsNullOrWhiteSpace(remote.Email) ? _account.CurrentAccount?.Email ?? "غير مضاف" : remote.Email;
            if (_account.CurrentAccount is not null && !string.IsNullOrWhiteSpace(remote.Email))
                _account.SignIn(remote.UserId, remote.Email, remote.Name);

            if (remote.HasProfilePhoto == true)
            {
                var downloaded = await _api.GetMyProfilePhotoAsync();
                if (downloaded is not null)
                    UpdatePhotoUi(_profile.SavePhoto(downloaded.Bytes, downloaded.ContentType));
            }
            else if (remote.HasProfilePhoto == false)
            {
                _profile.ClearPhoto();
                UpdatePhotoUi(null);
            }
        }
        catch
        {
            // Keep the locally stored values available while offline.
        }
    }

    private void UpdateLocalSummary()
    {
        var current = _profile.Profile;
        _nameValue.Text = current.Name;
        _aboutValue.Text = current.Status;
        var localPhoto = current.PhotoPath;
        UpdatePhotoUi(localPhoto);
        _emailValue.Text = _account.CurrentAccount?.Email ?? "غير مضاف";
    }

    private void UpdatePhotoUi(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        _photo.IsVisible = hasPhoto;
        _photo.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
        _initial.IsVisible = !hasPhoto;
        _initial.Text = _profile.Profile.Initial;
    }

    private async Task OpenPhotoPreviewAsync()
    {
        var path = _profile.Profile.PhotoPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            await Navigation.PushModalAsync(new ImagePreviewPage(path, "صورة الملف الشخصي"));
        else
            await DisplayAlertAsync("الصورة الشخصية", "لم تضف صورة بعد. اضغط على زر الكاميرا لاختيار صورة.", "حسنًا");
    }

    private async Task PushEditorAsync(ContentPage page)
    {
        try { await Navigation.PushModalAsync(page); }
        catch (Exception ex) { await DisplayAlertAsync("الملف الشخصي", "تعذر فتح الصفحة المطلوبة. " + ex.Message, "حسنًا"); }
    }

    private static View FieldRow(string title, string subtitle, string icon, Label value, Func<Task> onTap)
    {
        value.FontSize = 13;
        value.LineBreakMode = LineBreakMode.TailTruncation;
        value.MaxLines = 1;
        value.HorizontalTextAlignment = TextAlignment.Start;
        value.VerticalTextAlignment = TextAlignment.Center;
        var text = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Children =
            {
                new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start, FlowDirection = FlowDirection.RightToLeft },
                new Label { Text = subtitle, FontSize = 11, HorizontalTextAlignment = TextAlignment.Start, FlowDirection = FlowDirection.RightToLeft }
            }
        };
        ((Label)text.Children[0]).SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        ((Label)text.Children[1]).SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var values = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Fill, FlowDirection = FlowDirection.RightToLeft, Children = { value } };
        var inner = new Grid
        {
            Padding = new Thickness(8, 9),
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(new GridLength(44)) },
            ColumnSpacing = 6,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 70
        };
        var left = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft, Children = { text, values } };
        inner.Add(left, 0, 0);
        var iconBorder = new Border
        {
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 9,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image { Source = icon, Aspect = Aspect.AspectFit, WidthRequest = 22, HeightRequest = 22 }
        };
        iconBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        iconBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        inner.Add(iconBorder, 1, 0);
        inner.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await onTap()) });
        return inner;
    }
}

internal sealed class ProfileDisplayNamePage : ProfileEditorPageBase
{
    private readonly ProfileService _profile;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Entry _entry = MakeEntry("اكتب الاسم الظاهر", maxLength: 60);
    private readonly Label _hint = Hint("يظهر الاسم في ملفك الشخصي والمحادثات.");
    private readonly Button _save;

    public ProfileDisplayNamePage(ProfileService profile, HimoApiClient api, AccountService account)
        : base("الاسم", "الاسم الظاهر للآخرين")
    {
        _profile = profile; _api = api; _account = account;
        _save = PrimaryButton("حفظ الاسم", SaveClicked);
        SetEditorContent(Card(new VerticalStackLayout { Spacing = 12, Children = { Heading("الاسم الظاهر"), EntryCard(_entry), _hint, _save } }));
        _entry.Text = _profile.Profile.Name;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_api.HasToken) return;
        try { _entry.Text = (await _api.GetMyProfileAsync()).Name; } catch { }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        var name = _entry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
        { await DisplayAlertAsync("الاسم", "أدخل اسمًا من حرف واحد إلى 60 حرفًا.", "حسنًا"); return; }
        _save.IsEnabled = false; _save.Text = "جارٍ الحفظ...";
        try
        {
            var status = _profile.Profile.Status;
            if (_api.HasToken)
            {
                var remote = await _api.UpdateMyProfileAsync(name, status);
                name = remote.Name;
                status = remote.Status;
                if (_account.CurrentAccount is not null && !string.IsNullOrWhiteSpace(remote.Email))
                    _account.SignIn(remote.UserId, remote.Email, name);
            }
            _profile.Update(name, status);
            await DisplayAlertAsync("تم الحفظ", "تم تحديث الاسم.", "حسنًا");
            await CloseEditorAsync();
        }
        catch (Exception ex) { _hint.Text = "تعذر حفظ الاسم: " + ex.Message; }
        finally { _save.IsEnabled = true; _save.Text = "حفظ الاسم"; }
    }
}

internal sealed class ProfileStatusPage : ProfileEditorPageBase
{
    private static readonly string[] StatusOptions = { "غير متاح", "اترك رسالة", "نائم", "في العمل", "مسافر", "متاح على Himo" };
    private readonly ProfileService _profile;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly List<Button> _optionButtons = new();
    private readonly Label _hint = Hint("اختر الحالة التي تصف ما تفعله الآن.");
    private readonly Button _save;
    private string _selected;

    public ProfileStatusPage(ProfileService profile, HimoApiClient api, AccountService account)
        : base("حول", "حالتك وماذا تفعل الآن")
    {
        _profile = profile; _api = api; _account = account;
        _selected = StatusOptions.Contains(profile.Profile.Status, StringComparer.Ordinal)
            ? profile.Profile.Status
            : "متاح على Himo";
        var choices = new VerticalStackLayout { Spacing = 8 };
        foreach (var status in StatusOptions)
        {
            var button = SecondaryButton(status, (_, _) => SelectStatus(status));
            button.HorizontalOptions = LayoutOptions.Fill;
            button.HeightRequest = 48;
            _optionButtons.Add(button);
            choices.Children.Add(button);
        }
        _save = PrimaryButton("حفظ الحالة", SaveClicked);
        SetEditorContent(Card(new VerticalStackLayout { Spacing = 11, Children = { Heading("ماذا تفعل الآن؟"), choices, _hint, _save } }));
        UpdateSelection();
    }

    private void SelectStatus(string status) { _selected = status; UpdateSelection(); }

    private void UpdateSelection()
    {
        for (var i = 0; i < _optionButtons.Count; i++)
        {
            var selected = StatusOptions[i] == _selected;
            _optionButtons[i].Text = selected ? "✓  " + StatusOptions[i] : StatusOptions[i];
            _optionButtons[i].BackgroundColor = selected ? Color.FromArgb("#804EC7") : Colors.Transparent;
            _optionButtons[i].BorderColor = selected ? Color.FromArgb("#B882F4") : Color.FromArgb("#76509A");
            _optionButtons[i].TextColor = selected ? Colors.White : Color.FromArgb("#D9C8EB");
        }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        _save.IsEnabled = false; _save.Text = "جارٍ الحفظ...";
        try
        {
            var name = _profile.Profile.Name;
            if (_api.HasToken)
            {
                var remote = await _api.UpdateMyProfileAsync(name, _selected);
                name = remote.Name;
                _selected = string.IsNullOrWhiteSpace(remote.Status) ? _selected : remote.Status;
                if (_account.CurrentAccount is not null && !string.IsNullOrWhiteSpace(remote.Email))
                    _account.SignIn(remote.UserId, remote.Email, name);
            }
            _profile.Update(name, _selected);
            await DisplayAlertAsync("تم الحفظ", "تم تحديث حالتك الشخصية.", "حسنًا");
            await CloseEditorAsync();
        }
        catch (Exception ex) { _hint.Text = "تعذر حفظ الحالة: " + ex.Message; }
        finally { _save.IsEnabled = true; _save.Text = "حفظ الحالة"; }
    }
}

internal sealed class ProfileUsernamePage : ProfileEditorPageBase
{
    private readonly HimoApiClient _api;
    private readonly Entry _entry = MakeEntry("مثال: ahmed_22", maxLength: 24);
    private readonly Label _hint = Hint("من 3 إلى 24 حرفًا إنجليزيًا أو رقمًا أو (_) أو (.)، ويجب أن يكون غير مستخدم.");
    private readonly Button _save;

    public ProfileUsernamePage(HimoApiClient api) : base("اسم المستخدم", "معرّف فريد لحسابك")
    {
        _api = api;
        _save = PrimaryButton("حفظ اسم المستخدم", SaveClicked);
        SetEditorContent(Card(new VerticalStackLayout { Spacing = 12, Children = { Heading("اسم المستخدم"), EntryCard(_entry), _hint, _save } }));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_api.HasToken) { _hint.Text = "سجّل الدخول لتعديل اسم المستخدم."; return; }
        try { var profile = await _api.GetMyProfileAsync(); _entry.Text = profile.Username ?? string.Empty; } catch { }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (!_api.HasToken) { await DisplayAlertAsync("اسم المستخدم", "سجّل الدخول أولًا.", "حسنًا"); return; }
        _save.IsEnabled = false; _save.Text = "جارٍ الحفظ...";
        try
        {
            var result = await _api.UpdateMyUsernameAsync(_entry.Text ?? string.Empty);
            _hint.Text = "تم حفظ اسم المستخدم @" + result.Username;
            await DisplayAlertAsync("تم الحفظ", "تم تحديث اسم المستخدم.", "حسنًا");
            await CloseEditorAsync();
        }
        catch (Exception ex) { _hint.Text = "تعذر الحفظ: " + ex.Message; }
        finally { _save.IsEnabled = true; _save.Text = "حفظ اسم المستخدم"; }
    }
}

internal sealed class ProfileEmailPage : ProfileEditorPageBase
{
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Entry _email = MakeEntry("name@example.com", Keyboard.Email, maxLength: 254);
    private readonly Entry _code = MakeEntry("رمز التحقق المكوّن من 6 أرقام", Keyboard.Numeric, maxLength: 6);
    private readonly Label _currentEmail = Hint("البريد الحالي: جارٍ التحميل...");
    private readonly Label _hint = Hint("سيُرسل رمز تحقق إلى البريد الجديد قبل اعتماد التغيير.");
    private readonly Button _send;
    private readonly Button _verify;

    public ProfileEmailPage(HimoApiClient api, AccountService account) : base("البريد الإلكتروني", "غيّر بريد الدخول بعد التحقق منه")
    {
        _api = api; _account = account;
        _send = SecondaryButton("إرسال رمز التحقق", SendCodeClicked);
        _verify = PrimaryButton("تأكيد وتغيير البريد", VerifyClicked);
        SetEditorContent(
            Card(new VerticalStackLayout { Spacing = 10, Children = { Heading("البريد الجديد"), _currentEmail, EntryCard(_email), _send } }),
            Card(new VerticalStackLayout { Spacing = 10, Children = { Heading("رمز التحقق"), EntryCard(_code), _hint, _verify } }),
            Hint("لا يتغير البريد المسجّل إلا بعد إدخال رمز صحيح يصل إلى البريد الجديد."));
        _email.Text = string.Empty;
        _currentEmail.Text = "البريد الحالي: " + (account.CurrentAccount?.Email ?? "غير متاح");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_api.HasToken) return;
        try
        {
            var profile = await _api.GetMyProfileAsync();
            _currentEmail.Text = "البريد الحالي: " + profile.Email;
        }
        catch { }
    }

    private async void SendCodeClicked(object? sender, EventArgs e)
    {
        _send.IsEnabled = false; _send.Text = "جارٍ الإرسال...";
        try
        {
            var result = await _api.RequestMyEmailChangeAsync(_email.Text ?? string.Empty);
            _hint.Text = result.Message;
            if (!string.IsNullOrWhiteSpace(result.DevelopmentCode))
                _hint.Text += " رمز التطوير: " + result.DevelopmentCode;
        }
        catch (Exception ex) { _hint.Text = "تعذر إرسال الرمز: " + ex.Message; }
        finally { _send.IsEnabled = true; _send.Text = "إرسال رمز التحقق"; }
    }

    private async void VerifyClicked(object? sender, EventArgs e)
    {
        _verify.IsEnabled = false; _verify.Text = "جارٍ التحقق...";
        try
        {
            var updated = await _api.VerifyMyEmailChangeAsync(_email.Text ?? string.Empty, _code.Text ?? string.Empty);
            if (updated.UserId != Guid.Empty && !string.IsNullOrWhiteSpace(updated.Email))
                _account.SignIn(updated.UserId, updated.Email, updated.Name);
            await DisplayAlertAsync("تم التغيير", "تم تأكيد البريد الإلكتروني الجديد وتحديث حسابك.", "حسنًا");
            await CloseEditorAsync();
        }
        catch (Exception ex) { _hint.Text = "تعذر تأكيد البريد: " + ex.Message; }
        finally { _verify.IsEnabled = true; _verify.Text = "تأكيد وتغيير البريد"; }
    }
}

internal abstract class ProfileEditorPageBase : ContentPage
{
    protected ProfileEditorPageBase(string title, string subtitle)
    {
        Title = title;
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#120B22");
        SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        Shell.SetNavBarIsVisible(this, false);
        _title = title;
        _subtitle = subtitle;
    }

    private readonly string _title;
    private readonly string _subtitle;

    protected void SetEditorContent(params View[] views)
    {
        var back = new ImageButton { Source = "himo_s9_arrow_back.png", WidthRequest = 42, HeightRequest = 42, Padding = 10, Aspect = Aspect.AspectFit, CornerRadius = 14, BorderWidth = 1, BorderColor = Color.FromArgb("#76509A") };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.Clicked += async (_, _) => await CloseEditorAsync();
        var title = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = _title, FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End }, new Label { Text = _subtitle, FontSize = 11.5, HorizontalTextAlignment = TextAlignment.End } } };
        ((Label)title.Children[0]).SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        ((Label)title.Children[1]).SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var header = new Grid { Padding = new Thickness(16, 12), ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(44)), new(GridLength.Star) }, ColumnSpacing = 12, FlowDirection = FlowDirection.LeftToRight };
        header.Add(back, 0, 0); header.Add(title, 1, 0);
        var body = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 28), Spacing = 14 };
        foreach (var view in views) body.Children.Add(view);
        var scroll = new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var grid = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) } };
        grid.Add(header, 0, 0); grid.Add(scroll, 0, 1);
        Content = grid;
    }

    protected async Task CloseEditorAsync()
    {
        try { await Navigation.PopModalAsync(); }
        catch
        {
            try { if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync(); } catch { }
        }
    }

    protected static Label Hint(string text)
    {
        var label = new Label { Text = text, FontSize = 12, LineBreakMode = LineBreakMode.WordWrap, HorizontalTextAlignment = TextAlignment.End, FlowDirection = FlowDirection.RightToLeft };
        label.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        return label;
    }

    // Divider used between profile fields. Keep this helper in the same base
    // class as ProfileDetailsPage so all four field separators resolve correctly.
    protected static BoxView Separator()
    {
        var separator = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 3) };
        separator.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBorder");
        return separator;
    }

    protected static View Heading(string text)
    {
        var label = new Label { Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.End, FlowDirection = FlowDirection.RightToLeft };
        label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        return label;
    }

    protected static Border Card(View content, Thickness? padding = null)
    {
        var card = new Border { Content = content, Padding = padding ?? new Thickness(16), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 21 } };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    protected static Border EntryCard(Entry entry)
    {
        var card = new Border { Content = entry, Padding = new Thickness(10, 3), StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 } };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        entry.TextColor = Color.FromArgb("#F6EDFF");
        entry.PlaceholderColor = Color.FromArgb("#BBA8D0");
        return card;
    }

    protected static Entry MakeEntry(string placeholder, Keyboard? keyboard = null, int maxLength = 254) => new()
    {
        Placeholder = placeholder,
        Keyboard = keyboard ?? Keyboard.Default,
        MaxLength = maxLength,
        ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        FlowDirection = FlowDirection.LeftToRight,
        HorizontalTextAlignment = TextAlignment.Start,
        HeightRequest = 48
    };

    protected static Button PrimaryButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, HeightRequest = 50, CornerRadius = 16, FontSize = 14, FontAttributes = FontAttributes.Bold, BackgroundColor = Color.FromArgb("#B565F2"), TextColor = Colors.White };
        button.Clicked += handler;
        return button;
    }

    protected static Button SecondaryButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, HeightRequest = 48, CornerRadius = 14, FontSize = 13, BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#DAC3F3"), BorderColor = Color.FromArgb("#725092"), BorderWidth = 1 };
        button.Clicked += handler;
        return button;
    }
}

internal static class ProfilePhotoWorkflow
{
    private const int MaximumPhotoBytes = 4 * 1024 * 1024;

    public static async Task ShowAsync(Page page, ProfileService profile, HimoApiClient api, Action<string?> updateUi)
    {
        var action = await page.DisplayActionSheetAsync("صورة الملف الشخصي", "إلغاء", "إزالة الصورة", "التقاط صورة بالكاميرا", "اختيار صورة من المعرض");
        if (action == "إزالة الصورة")
        {
            var confirm = await page.DisplayAlertAsync("إزالة الصورة", "هل تريد إزالة صورة الملف الشخصي؟", "إزالة", "إلغاء");
            if (!confirm) return;
            try
            {
                if (api.HasToken) await api.DeleteMyProfilePhotoAsync();
                profile.ClearPhoto(); updateUi(null);
                await page.DisplayAlertAsync("تمت الإزالة", "تمت إزالة صورة الملف الشخصي.", "حسنًا");
            }
            catch (Exception ex) { await page.DisplayAlertAsync("تعذر إزالة الصورة", ex.Message, "حسنًا"); }
            return;
        }
        if (action is not ("التقاط صورة بالكاميرا" or "اختيار صورة من المعرض")) return;

        try
        {
            FileResult? result;
            if (action == "التقاط صورة بالكاميرا")
                result = await MediaPicker.Default.CapturePhotoAsync();
            else
            {
                var photos = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "اختر صورة الملف الشخصي", SelectionLimit = 1 });
                result = photos is { Count: > 0 } ? photos[0] : null;
            }
            if (result is null) return;
            var contentType = (result.ContentType ?? string.Empty).Split(';', StringSplitOptions.TrimEntries)[0].ToLowerInvariant();
            if (contentType == "image/jpg") contentType = "image/jpeg";
            if (contentType is not ("image/jpeg" or "image/png" or "image/webp" or "image/heic" or "image/heif" or "image/gif"))
            {
                contentType = Path.GetExtension(result.FileName ?? string.Empty).ToLowerInvariant() switch
                {
                    ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp",
                    ".heic" => "image/heic", ".heif" => "image/heif", ".gif" => "image/gif",
                    _ => throw new InvalidDataException("صيغة الصورة غير مدعومة. اختر JPG أو PNG أو WebP أو HEIC.")
                };
            }
            byte[] bytes;
            await using (var input = await result.OpenReadAsync())
            using (var output = new MemoryStream())
            {
                var buffer = new byte[32 * 1024];
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length));
                    if (read == 0) break;
                    if (output.Length + read > MaximumPhotoBytes) throw new InvalidDataException("اختر صورة لا يتجاوز حجمها 4 ميغابايت.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
                bytes = output.ToArray();
            }
            if (api.HasToken) await api.UploadMyProfilePhotoAsync(bytes, contentType);
            var path = profile.SavePhoto(bytes, contentType);
            updateUi(path);
            await page.DisplayAlertAsync("تم تحديث الصورة", "تم تحديث صورة الملف الشخصي بنجاح.", "حسنًا");
        }
        catch (Exception ex) { await page.DisplayAlertAsync("تعذر تحديث الصورة", ex.Message, "حسنًا"); }
    }
}
