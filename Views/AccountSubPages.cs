using Himo.Models;
using Himo.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Himo.Views;

internal static class AccountCountryCatalog
{
    public static readonly IReadOnlyList<CountryDialCode> Countries = new List<CountryDialCode>
    {
new("المملكة العربية السعودية", "🇸🇦", "+966"),
        new("الإمارات العربية المتحدة", "🇦🇪", "+971"),
        new("الكويت", "🇰🇼", "+965"),
        new("قطر", "🇶🇦", "+974"),
        new("البحرين", "🇧🇭", "+973"),
        new("عُمان", "🇴🇲", "+968"),
        new("اليمن", "🇾🇪", "+967"),
        new("الأردن", "🇯🇴", "+962"),
        new("فلسطين", "🇵🇸", "+970"),
        new("العراق", "🇮🇶", "+964"),
        new("سوريا", "🇸🇾", "+963"),
        new("لبنان", "🇱🇧", "+961"),
        new("مصر", "🇪🇬", "+20"),
        new("السودان", "🇸🇩", "+249"),
        new("جنوب السودان", "🇸🇸", "+211"),
        new("ليبيا", "🇱🇾", "+218"),
        new("تونس", "🇹🇳", "+216"),
        new("الجزائر", "🇩🇿", "+213"),
        new("المغرب", "🇲🇦", "+212"),
        new("موريتانيا", "🇲🇷", "+222"),
        new("الصومال", "🇸🇴", "+252"),
        new("جيبوتي", "🇩🇯", "+253"),
        new("جزر القمر", "🇰🇲", "+269"),
        new("تركيا", "🇹🇷", "+90"),
        new("إيران", "🇮🇷", "+98"),
        new("أفغانستان", "🇦🇫", "+93"),
        new("باكستان", "🇵🇰", "+92"),
        new("الهند", "🇮🇳", "+91"),
        new("بنغلاديش", "🇧🇩", "+880"),
        new("سريلانكا", "🇱🇰", "+94"),
        new("نيبال", "🇳🇵", "+977"),
        new("الصين", "🇨🇳", "+86"),
        new("هونغ كونغ", "🇭🇰", "+852"),
        new("ماكاو", "🇲🇴", "+853"),
        new("تايوان", "🇹🇼", "+886"),
        new("اليابان", "🇯🇵", "+81"),
        new("كوريا الجنوبية", "🇰🇷", "+82"),
        new("كوريا الشمالية", "🇰🇵", "+850"),
        new("منغوليا", "🇲🇳", "+976"),
        new("كازاخستان", "🇰🇿", "+7"),
        new("أوزبكستان", "🇺🇿", "+998"),
        new("قيرغيزستان", "🇰🇬", "+996"),
        new("طاجيكستان", "🇹🇯", "+992"),
        new("تركمانستان", "🇹🇲", "+993"),
        new("أذربيجان", "🇦🇿", "+994"),
        new("أرمينيا", "🇦🇲", "+374"),
        new("جورجيا", "🇬🇪", "+995"),
        new("روسيا", "🇷🇺", "+7"),
        new("إندونيسيا", "🇮🇩", "+62"),
        new("ماليزيا", "🇲🇾", "+60"),
        new("سنغافورة", "🇸🇬", "+65"),
        new("تايلاند", "🇹🇭", "+66"),
        new("فيتنام", "🇻🇳", "+84"),
        new("الفلبين", "🇵🇭", "+63"),
        new("كمبوديا", "🇰🇭", "+855"),
        new("لاوس", "🇱🇦", "+856"),
        new("ميانمار", "🇲🇲", "+95"),
        new("بروناي", "🇧🇳", "+673"),
        new("تيمور الشرقية", "🇹🇱", "+670"),
        new("بوتان", "🇧🇹", "+975"),
        new("المالديف", "🇲🇻", "+960"),
        new("المملكة المتحدة", "🇬🇧", "+44"),
        new("أيرلندا", "🇮🇪", "+353"),
        new("فرنسا", "🇫🇷", "+33"),
        new("ألمانيا", "🇩🇪", "+49"),
        new("إيطاليا", "🇮🇹", "+39"),
        new("إسبانيا", "🇪🇸", "+34"),
        new("البرتغال", "🇵🇹", "+351"),
        new("هولندا", "🇳🇱", "+31"),
        new("بلجيكا", "🇧🇪", "+32"),
        new("سويسرا", "🇨🇭", "+41"),
        new("النمسا", "🇦🇹", "+43"),
        new("السويد", "🇸🇪", "+46"),
        new("النرويج", "🇳🇴", "+47"),
        new("الدنمارك", "🇩🇰", "+45"),
        new("فنلندا", "🇫🇮", "+358"),
        new("آيسلندا", "🇮🇸", "+354"),
        new("اليونان", "🇬🇷", "+30"),
        new("قبرص", "🇨🇾", "+357"),
        new("مالطا", "🇲🇹", "+356"),
        new("بولندا", "🇵🇱", "+48"),
        new("التشيك", "🇨🇿", "+420"),
        new("سلوفاكيا", "🇸🇰", "+421"),
        new("المجر", "🇭🇺", "+36"),
        new("رومانيا", "🇷🇴", "+40"),
        new("بلغاريا", "🇧🇬", "+359"),
        new("أوكرانيا", "🇺🇦", "+380"),
        new("بيلاروس", "🇧🇾", "+375"),
        new("مولدوفا", "🇲🇩", "+373"),
        new("صربيا", "🇷🇸", "+381"),
        new("كرواتيا", "🇭🇷", "+385"),
        new("البوسنة والهرسك", "🇧🇦", "+387"),
        new("سلوفينيا", "🇸🇮", "+386"),
        new("الجبل الأسود", "🇲🇪", "+382"),
        new("مقدونيا الشمالية", "🇲🇰", "+389"),
        new("ألبانيا", "🇦🇱", "+355"),
        new("إستونيا", "🇪🇪", "+372"),
        new("لاتفيا", "🇱🇻", "+371"),
        new("ليتوانيا", "🇱🇹", "+370"),
        new("لوكسمبورغ", "🇱🇺", "+352"),
        new("أندورا", "🇦🇩", "+376"),
        new("موناكو", "🇲🇨", "+377"),
        new("ليختنشتاين", "🇱🇮", "+423"),
        new("سان مارينو", "🇸🇲", "+378"),
        new("أيسلندا", "🇮🇸", "+354"),
        new("الولايات المتحدة", "🇺🇸", "+1"),
        new("كندا", "🇨🇦", "+1"),
        new("المكسيك", "🇲🇽", "+52"),
        new("غواتيمالا", "🇬🇹", "+502"),
        new("بليز", "🇧🇿", "+501"),
        new("هندوراس", "🇭🇳", "+504"),
        new("السلفادور", "🇸🇻", "+503"),
        new("نيكاراغوا", "🇳🇮", "+505"),
        new("كوستاريكا", "🇨🇷", "+506"),
        new("بنما", "🇵🇦", "+507"),
        new("كوبا", "🇨🇺", "+53"),
        new("جامايكا", "🇯🇲", "+1"),
        new("هايتي", "🇭🇹", "+509"),
        new("جمهورية الدومينيكان", "🇩🇴", "+1"),
        new("ترينيداد وتوباغو", "🇹🇹", "+1"),
        new("باهاماس", "🇧🇸", "+1"),
        new("باربادوس", "🇧🇧", "+1"),
        new("كولومبيا", "🇨🇴", "+57"),
        new("فنزويلا", "🇻🇪", "+58"),
        new("الإكوادور", "🇪🇨", "+593"),
        new("بيرو", "🇵🇪", "+51"),
        new("بوليفيا", "🇧🇴", "+591"),
        new("تشيلي", "🇨🇱", "+56"),
        new("الأرجنتين", "🇦🇷", "+54"),
        new("البرازيل", "🇧🇷", "+55"),
        new("أوروغواي", "🇺🇾", "+598"),
        new("باراغواي", "🇵🇾", "+595"),
        new("غيانا", "🇬🇾", "+592"),
        new("سورينام", "🇸🇷", "+597"),
        new("بورتوريكو", "🇵🇷", "+1"),
        new("جنوب أفريقيا", "🇿🇦", "+27"),
        new("ناميبيا", "🇳🇦", "+264"),
        new("بوتسوانا", "🇧🇼", "+267"),
        new("زيمبابوي", "🇿🇼", "+263"),
        new("زامبيا", "🇿🇲", "+260"),
        new("ملاوي", "🇲🇼", "+265"),
        new("موزمبيق", "🇲🇿", "+258"),
        new("أنغولا", "🇦🇴", "+244"),
        new("تنزانيا", "🇹🇿", "+255"),
        new("كينيا", "🇰🇪", "+254"),
        new("أوغندا", "🇺🇬", "+256"),
        new("رواندا", "🇷🇼", "+250"),
        new("بوروندي", "🇧🇮", "+257"),
        new("إثيوبيا", "🇪🇹", "+251"),
        new("إريتريا", "🇪🇷", "+291"),
        new("غانا", "🇬🇭", "+233"),
        new("نيجيريا", "🇳🇬", "+234"),
        new("النيجر", "🇳🇪", "+227"),
        new("مالي", "🇲🇱", "+223"),
        new("السنغال", "🇸🇳", "+221"),
        new("غامبيا", "🇬🇲", "+220"),
        new("غينيا", "🇬🇳", "+224"),
        new("غينيا بيساو", "🇬🇼", "+245"),
        new("سيراليون", "🇸🇱", "+232"),
        new("ليبيريا", "🇱🇷", "+231"),
        new("ساحل العاج", "🇨🇮", "+225"),
        new("توغو", "🇹🇬", "+228"),
        new("بنين", "🇧🇯", "+229"),
        new("الكاميرون", "🇨🇲", "+237"),
        new("الغابون", "🇬🇦", "+241"),
        new("غينيا الاستوائية", "🇬🇶", "+240"),
        new("جمهورية الكونغو", "🇨🇬", "+242"),
        new("الكونغو الديمقراطية", "🇨🇩", "+243"),
        new("تشاد", "🇹🇩", "+235"),
        new("أفريقيا الوسطى", "🇨🇫", "+236"),
        new("مدغشقر", "🇲🇬", "+261"),
        new("موريشيوس", "🇲🇺", "+230"),
        new("سيشل", "🇸🇨", "+248"),
        new("ليسوتو", "🇱🇸", "+266"),
        new("إسواتيني", "🇸🇿", "+268"),
        new("الرأس الأخضر", "🇨🇻", "+238"),
        new("أستراليا", "🇦🇺", "+61"),
        new("نيوزيلندا", "🇳🇿", "+64"),
        new("فيجي", "🇫🇯", "+679"),
        new("بابوا غينيا الجديدة", "🇵🇬", "+675"),
        new("ساموا", "🇼🇸", "+685"),
        new("تونغا", "🇹🇴", "+676"),
        new("جزر سليمان", "🇸🇧", "+677"),
        new("فانواتو", "🇻🇺", "+678"),
        new("كيريباتي", "🇰🇮", "+686"),
        new("ناورو", "🇳🇷", "+674"),
        new("توفالو", "🇹🇻", "+688"),
    };
}

public abstract class AccountSubPageBase : ContentPage
{
    protected static readonly Color PageColor = Color.FromArgb("#080817");
    protected static readonly Color CardColor = Color.FromArgb("#101D3D");
    protected static readonly Color BorderColor = Color.FromArgb("#263B6B");
    protected static readonly Color MainTextColor = Color.FromArgb("#F2F5FF");
    protected static readonly Color SecondaryTextColor = Color.FromArgb("#9EAFDA");

    protected AccountSubPageBase(string title, string subtitle)
    {
        Title = title;
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageColor;
        Shell.SetNavBarIsVisible(this, false);
        PageTitle = title;
        PageSubtitle = subtitle;
    }

    private string PageTitle { get; }
    private string PageSubtitle { get; }

    protected void SetContent(params View[] items)
    {
        var back = new Button
        {
            Text = "‹", FontSize = 34, FontAttributes = FontAttributes.Bold,
            HeightRequest = 42, WidthRequest = 42, Padding = 0,
            BackgroundColor = Color.FromArgb("#1B2850"), TextColor = Color.FromArgb("#E7ECFF"),
            BorderColor = Color.FromArgb("#334A82"), BorderWidth = 1, CornerRadius = 15,
            VerticalOptions = LayoutOptions.Center
        };
        back.Clicked += BackClicked;
        var heading = new VerticalStackLayout
        {
            Spacing = 1, VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End, FlowDirection = FlowDirection.RightToLeft,
            Children =
            {
                new Label { Text = PageTitle, FontSize = 23, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.End },
                new Label { Text = PageSubtitle, FontSize = 12, TextColor = Color.FromArgb("#A6B9E9"),
                    HorizontalTextAlignment = TextAlignment.End }
            }
        };
        var header = new Grid
        {
            Padding = new Thickness(18, 12, 18, 10),
            ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(48)), new(GridLength.Star), new(new GridLength(48)) },
            ColumnSpacing = 8, FlowDirection = FlowDirection.LeftToRight
        };
        header.Add(back, 0, 0);
        header.Add(heading, 1, 0);
        header.Add(new BoxView { WidthRequest = 42, HeightRequest = 42, Opacity = 0 }, 2, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 28), Spacing = 14 };
        foreach (var item in items) body.Children.Add(item);
        var scroll = new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var layout = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) } };
        layout.Add(header, 0, 0);
        layout.Add(scroll, 0, 1);
        Content = layout;
    }

    protected static Border Card(View child, Thickness? padding = null) => new()
    {
        Content = child,
        BackgroundColor = CardColor,
        Stroke = BorderColor,
        StrokeThickness = 1,
        Padding = padding ?? new Thickness(15),
        StrokeShape = new RoundRectangle { CornerRadius = 20 }
    };

    protected static Label Heading(string text) => new()
    {
        Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold,
        TextColor = MainTextColor, HorizontalTextAlignment = TextAlignment.End
    };

    protected static Label Hint(string text) => new()
    {
        Text = text, FontSize = 11, TextColor = SecondaryTextColor,
        HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap
    };

    protected static Entry MakeEntry(string placeholder, Keyboard? keyboard = null, bool password = false, int maxLength = 254) => new()
    {
        Placeholder = placeholder, Keyboard = keyboard ?? Keyboard.Default, IsPassword = password,
        MaxLength = maxLength, ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        FlowDirection = FlowDirection.RightToLeft, HorizontalTextAlignment = TextAlignment.End,
        TextColor = Colors.White, PlaceholderColor = Color.FromArgb("#8296C6"),
        BackgroundColor = Colors.Transparent, FontSize = 15, VerticalOptions = LayoutOptions.Center
    };

    protected static Border EntryBox(Entry entry) => new()
    {
        Content = entry, BackgroundColor = Color.FromArgb("#09132C"), Stroke = Color.FromArgb("#334A7A"),
        StrokeThickness = 1, Padding = new Thickness(10, 1), MinimumHeightRequest = 50,
        StrokeShape = new RoundRectangle { CornerRadius = 14 }
    };

    protected static Button PrimaryButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text, HeightRequest = 46, CornerRadius = 14, FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#7650E8"), TextColor = Colors.White
        };
        button.Clicked += handler;
        return button;
    }

    protected static Button SecondaryButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text, HeightRequest = 43, CornerRadius = 13,
            BackgroundColor = Color.FromArgb("#1B2B55"), TextColor = Color.FromArgb("#DCE4FF")
        };
        button.Clicked += handler;
        return button;
    }

    protected async Task GoBackAsync()
    {
        if (Shell.Current is not null) await Shell.Current.GoToAsync("..", false);
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }

    private async void BackClicked(object? sender, EventArgs e) => await GoBackAsync();
}

public sealed class AccountUsernamePage : AccountSubPageBase
{
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ProfileService _profile;
    private readonly Entry _nameEntry = MakeEntry("اكتب اسم المستخدم", maxLength: 60);
    private readonly Label _statusLabel = Hint("جارٍ تحميل بيانات الحساب...");
    private readonly Button _saveButton;
    private string _status = "متاح على Himo";
    private int _saving;

    public AccountUsernamePage(HimoApiClient api, AccountService account, ProfileService profile)
        : base("اسم المستخدم", "عدّل الاسم الذي يظهر للآخرين")
    {
        _api = api; _account = account; _profile = profile;
        _saveButton = PrimaryButton("حفظ اسم المستخدم", SaveClicked);
        SetContent(
            Card(new VerticalStackLayout { Spacing = 10, Children = { Heading("الاسم الظاهر"), Hint("استخدم اسمًا واضحًا من حرف إلى 60 حرفًا."), EntryBox(_nameEntry), _statusLabel, _saveButton } }),
            Hint("لن يتم اعتماد الاسم إلا بعد نجاح الحفظ من الخادم."));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_api.HasToken) { _statusLabel.Text = "سجّل الدخول أولًا لعرض بياناتك."; return; }
        try
        {
            var remote = await _api.GetMyProfileAsync();
            _nameEntry.Text = remote.Name ?? _account.CurrentAccount?.Name ?? string.Empty;
            _status = string.IsNullOrWhiteSpace(remote.Status) ? "متاح على Himo" : remote.Status;
            _statusLabel.Text = "يمكنك تعديل اسمك ثم حفظه.";
        }
        catch (Exception ex) { _statusLabel.Text = "تعذر تحميل الاسم: " + ex.Message; }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _saving, 1) != 0) return;
        var name = _nameEntry.Text?.Trim() ?? string.Empty;
        if (!_api.HasToken) { await DisplayAlertAsync("اسم المستخدم", "سجّل الدخول أولًا.", "حسنًا"); Interlocked.Exchange(ref _saving, 0); return; }
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60)
        {
            await DisplayAlertAsync("اسم المستخدم", "أدخل اسمًا من حرف واحد إلى 60 حرفًا.", "حسنًا");
            Interlocked.Exchange(ref _saving, 0); return;
        }
        _saveButton.IsEnabled = false;
        _saveButton.Text = "جارٍ الحفظ...";
        try
        {
            var updated = await _api.UpdateMyProfileAsync(name, _status);
            var normalizedName = string.IsNullOrWhiteSpace(updated.Name) ? name : updated.Name;
            var normalizedStatus = string.IsNullOrWhiteSpace(updated.Status) ? _status : updated.Status;
            var email = string.IsNullOrWhiteSpace(updated.Email) ? _account.CurrentAccount?.Email : updated.Email;
            if (updated.UserId != Guid.Empty && !string.IsNullOrWhiteSpace(email))
                _account.SignIn(updated.UserId, email, normalizedName);
            _profile.Update(normalizedName, normalizedStatus);
            _statusLabel.Text = "تم حفظ اسم المستخدم بنجاح.";
            await DisplayAlertAsync("تم الحفظ", "تم تحديث اسم المستخدم.", "حسنًا");
        }
        catch (Exception ex) { _statusLabel.Text = "تعذر حفظ الاسم."; await DisplayAlertAsync("تعذر الحفظ", ex.Message, "حسنًا"); }
        finally { _saveButton.IsEnabled = true; _saveButton.Text = "حفظ اسم المستخدم"; Interlocked.Exchange(ref _saving, 0); }
    }
}

public sealed class AccountPhonePage : AccountSubPageBase
{
    private readonly HimoApiClient _api;
    private CountryDialCode _selectedCountry = AccountCountryCatalog.Countries[0];
    private readonly Button _countryButton;
    private readonly Entry _phoneEntry = MakeEntry("5xxxxxxxx", Keyboard.Telephone, maxLength: 20);
    private readonly Label _preview = Hint("سيظهر الرقم الدولي هنا قبل الحفظ.");
    private readonly Label _status = Hint("جارٍ تحميل رقم الهاتف...");
    private readonly Switch _discoverable = new();
    private readonly Button _saveButton;
    private bool _phoneDiscoverable;
    private int _saving;

    public AccountPhonePage(HimoApiClient api) : base("رقم الهاتف", "رمز الدولة، الرقم، وخيارات الخصوصية")
    {
        _api = api;
        var savedName = Preferences.Default.Get("himo_contact_country_name", AccountCountryCatalog.Countries[0].Name);
        _selectedCountry = AccountCountryCatalog.Countries.FirstOrDefault(c => c.Name == savedName) ?? AccountCountryCatalog.Countries[0];
        _countryButton = SecondaryButton(_selectedCountry.CompactDisplay, CountryClicked);
        _phoneEntry.TextChanged += (_, _) => UpdatePreview();
        var phoneGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(126)), new(GridLength.Star) },
            ColumnSpacing = 8, FlowDirection = FlowDirection.LeftToRight
        };
        phoneGrid.Add(EntryBox(_phoneEntry), 1, 0);
        phoneGrid.Add(new Border
        {
            Content = _countryButton, BackgroundColor = Color.FromArgb("#142344"), Stroke = Color.FromArgb("#5265A5"),
            StrokeThickness = 1, Padding = 2, StrokeShape = new RoundRectangle { CornerRadius = 14 },
            VerticalOptions = LayoutOptions.Fill
        }, 0, 0);
        var previewBorder = new Border
        {
            Content = _preview, BackgroundColor = Color.FromArgb("#0D1934"), Stroke = BorderColor,
            StrokeThickness = 1, Padding = new Thickness(10, 8), StrokeShape = new RoundRectangle { CornerRadius = 11 }
        };
        var privacyRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 10, FlowDirection = FlowDirection.LeftToRight,
            Children =
            {
                new VerticalStackLayout { Spacing = 3, FlowDirection = FlowDirection.RightToLeft,
                    Children = { Heading("السماح بالعثور على حسابي عبر الهاتف"), Hint("سيظهر اسم حسابك للمطابقين، وليس رقمك.") } },
                _discoverable
            }
        };
        Grid.SetColumn(_discoverable, 1);
        _saveButton = PrimaryButton("حفظ رقم الهاتف", SaveClicked);
        SetContent(
            Card(new VerticalStackLayout { Spacing = 11, Children = { Heading("رقم الهاتف"), Hint("اختر الدولة من العلم والرمز، ثم اكتب الرقم المحلي دون رمز الدولة."), phoneGrid, previewBorder, privacyRow, _status, _saveButton } }),
            Hint("يُحفظ الرقم بصيغة دولية مثل ‎+9665xxxxxxxx.")
        );
        UpdateCountryButton();
        UpdatePreview();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_api.HasToken) { _status.Text = "سجّل الدخول أولًا لإدارة رقم الهاتف."; return; }
        try
        {
            var profile = await _api.GetMyProfileAsync();
            ApplyStoredPhone(profile.ContactPhoneNumber);
            _phoneDiscoverable = profile.PhoneDiscoverable;
            _discoverable.IsToggled = profile.PhoneDiscoverable;
            _status.Text = profile.PhoneDiscoverable ? "العثور على حسابك عبر الهاتف مفعّل." : "يمكنك تفعيل العثور عبر الهاتف اختياريًا.";
        }
        catch (Exception ex) { _status.Text = "تعذر تحميل الرقم: " + ex.Message; }
    }

    private async void CountryClicked(object? sender, EventArgs e)
    {
        var picker = new CountryDialCodePickerPage(AccountCountryCatalog.Countries, _selectedCountry);
        await Navigation.PushModalAsync(picker);
        var selected = await picker.SelectionTask;
        if (Navigation.ModalStack.Contains(picker)) await Navigation.PopModalAsync();
        if (selected is null) return;
        _selectedCountry = selected;
        Preferences.Default.Set("himo_contact_country_name", selected.Name);
        Preferences.Default.Set("himo_contact_country_code", selected.DialCode);
        UpdateCountryButton();
        UpdatePreview();
    }

    private void UpdateCountryButton() => _countryButton.Text = _selectedCountry.CompactDisplay;

    private void UpdatePreview()
    {
        var digits = PhoneNumberUtils.Normalize(_phoneEntry.Text);
        _preview.Text = string.IsNullOrWhiteSpace(digits)
            ? "سيظهر الرقم الدولي هنا قبل الحفظ."
            : $"{_selectedCountry.DialCode}{digits.TrimStart('0')}";
        _preview.FlowDirection = FlowDirection.LeftToRight;
        _preview.HorizontalTextAlignment = TextAlignment.Start;
    }

    private void ApplyStoredPhone(string? storedPhone)
    {
        var original = storedPhone?.Trim() ?? string.Empty;
        var digits = PhoneNumberUtils.Normalize(original);
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (string.IsNullOrWhiteSpace(digits))
        {
            _phoneEntry.Text = string.Empty;
            UpdateCountryButton(); UpdatePreview(); return;
        }

        var explicitInternational = original.StartsWith("+", StringComparison.Ordinal) || original.StartsWith("00", StringComparison.Ordinal);
        if (explicitInternational || digits.StartsWith("966", StringComparison.Ordinal))
        {
            var match = AccountCountryCatalog.Countries
                .Where(c => digits.StartsWith(PhoneNumberUtils.Normalize(c.DialCode), StringComparison.Ordinal)
                    && digits.Length - PhoneNumberUtils.Normalize(c.DialCode).Length >= 6)
                .OrderByDescending(c => PhoneNumberUtils.Normalize(c.DialCode).Length)
                .FirstOrDefault();
            if (match is not null)
            {
                _selectedCountry = match;
                digits = digits[PhoneNumberUtils.Normalize(match.DialCode).Length..];
            }
        }
        _phoneEntry.Text = digits;
        UpdateCountryButton(); UpdatePreview();
    }

    private (string? Phone, string? Error) BuildInternationalPhone()
    {
        var raw = PhoneNumberUtils.NormalizeDigits(_phoneEntry.Text?.Trim());
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var digits = PhoneNumberUtils.Normalize(raw);
        var dialDigits = PhoneNumberUtils.Normalize(_selectedCountry.DialCode);
        if (digits.StartsWith(dialDigits, StringComparison.Ordinal) && digits.Length >= dialDigits.Length + 7)
            digits = digits[dialDigits.Length..];
        if (dialDigits != "39" && digits.StartsWith("0", StringComparison.Ordinal) && digits.Length > 7)
            digits = digits[1..];
        var fullDigits = dialDigits + digits;
        if (digits.Length == 0 || fullDigits.Length is < 7 or > 15)
            return (null, "أدخل رقمًا صحيحًا يتكون من 7 إلى 15 رقمًا مع رمز الدولة.");
        return ($"+{fullDigits}", null);
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _saving, 1) != 0) return;
        if (!_api.HasToken) { await DisplayAlertAsync("رقم الهاتف", "سجّل الدخول أولًا.", "حسنًا"); Interlocked.Exchange(ref _saving, 0); return; }
        var number = BuildInternationalPhone();
        if (number.Error is not null) { await DisplayAlertAsync("رقم الهاتف", number.Error, "حسنًا"); Interlocked.Exchange(ref _saving, 0); return; }
        var discoverable = _discoverable.IsToggled;
        if (discoverable && string.IsNullOrWhiteSpace(number.Phone))
        {
            await DisplayAlertAsync("رقم الهاتف", "أدخل رقم الهاتف قبل تفعيل العثور عبر الهاتف.", "حسنًا");
            Interlocked.Exchange(ref _saving, 0); return;
        }
        if (discoverable && !_phoneDiscoverable)
        {
            var accepted = await DisplayAlertAsync("الخصوصية", "سيتمكن من لديه رقمك في جهات الاتصال من العثور على حسابك، ولن يظهر له رقم هاتفك.", "تفعيل", "إلغاء");
            if (!accepted) { _discoverable.IsToggled = false; Interlocked.Exchange(ref _saving, 0); return; }
        }
        _saveButton.IsEnabled = false; _saveButton.Text = "جارٍ الحفظ...";
        try
        {
            var result = await _api.UpdateMyContactPhoneAsync(number.Phone, discoverable);
            _phoneDiscoverable = result.PhoneDiscoverable;
            _discoverable.IsToggled = result.PhoneDiscoverable;
            ApplyStoredPhone(result.ContactPhoneNumber);
            _status.Text = result.ContactPhoneNumber is null ? "تم حذف رقم الهاتف من الحساب." : result.PhoneDiscoverable ? "تم الحفظ والعثور عبر الهاتف مفعّل." : "تم حفظ الرقم؛ العثور عبر الهاتف متوقف.";
            await DisplayAlertAsync("تم الحفظ", "تم تحديث إعداد رقم الهاتف.", "حسنًا");
        }
        catch (Exception ex) { _status.Text = "تعذر حفظ رقم الهاتف."; await DisplayAlertAsync("تعذر الحفظ", ex.Message, "حسنًا"); }
        finally { _saveButton.IsEnabled = true; _saveButton.Text = "حفظ رقم الهاتف"; Interlocked.Exchange(ref _saving, 0); }
    }
}

public sealed class AccountLoginDetailsPage : AccountSubPageBase
{
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ProfileService _profile;
    private readonly Label _emailLabel = Hint("جارٍ تحميل البريد الإلكتروني...");

    public AccountLoginDetailsPage(HimoApiClient api, AccountService account, ProfileService profile)
        : base("بيانات تسجيل الدخول", "البريد الإلكتروني ووسائل الدخول")
    {
        _api = api; _account = account; _profile = profile;
        var changeEmail = SecondaryButton("تغيير البريد الإلكتروني", ChangeEmailClicked);
        var password = PrimaryButton("إدارة كلمة المرور", OpenPasswordClicked);
        var addAccount = SecondaryButton("تسجيل الدخول بحساب آخر", OpenAddAccountClicked);
        SetContent(Card(new VerticalStackLayout { Spacing = 11, Children = { Heading("البريد الإلكتروني المسجّل"), _emailLabel, changeEmail, Hint("تغيير البريد الإلكتروني غير مفعّل في الخادم الحالي؛ يتطلب إضافة تحقق آمن من العنوان الجديد."), new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(0, 3) }, Heading("أمان الحساب"), password, addAccount } }));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var localEmail = _account.CurrentAccount?.Email ?? string.Empty;
        if (!_api.HasToken) { _emailLabel.Text = localEmail.Length == 0 ? "لا يوجد حساب مسجّل الدخول." : localEmail; return; }
        try { var profile = await _api.GetMyProfileAsync(); _emailLabel.Text = profile.Email ?? localEmail; _emailLabel.FlowDirection = FlowDirection.LeftToRight; _emailLabel.HorizontalTextAlignment = TextAlignment.Start; }
        catch { _emailLabel.Text = localEmail.Length == 0 ? "تعذر تحميل البريد الإلكتروني." : localEmail; }
    }

    private async void ChangeEmailClicked(object? sender, EventArgs e) => await DisplayAlertAsync("تغيير البريد الإلكتروني", "الخادم الحالي لا يحتوي على إجراء يغيّر البريد بأمان بعد التحقق من العنوان الجديد. لم يتم تغيير أي شيء.", "حسنًا");

    private async void OpenPasswordClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null) await Shell.Current.GoToAsync("account-password", false);
        else await Navigation.PushAsync(new AccountPasswordPage(_api, _account));
    }

    private async void OpenAddAccountClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null) await Shell.Current.GoToAsync("account-add", false);
        else await Navigation.PushAsync(new AddAccountPage(_api, _account, _profile));
    }
}

public sealed class AccountPasswordPage : AccountSubPageBase
{
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Entry _code = MakeEntry("رمز التحقق المكوّن من 6 أرقام", Keyboard.Numeric, maxLength: 6);
    private readonly Entry _newPassword = MakeEntry("كلمة المرور الجديدة (8 أحرف على الأقل)", password: true, maxLength: 128);
    private readonly Entry _confirmPassword = MakeEntry("تأكيد كلمة المرور الجديدة", password: true, maxLength: 128);
    private readonly Label _emailLabel = Hint("جارٍ تحديد البريد الإلكتروني...");
    private readonly Label _status = Hint(string.Empty);
    private readonly Button _sendButton;
    private readonly Button _saveButton;
    private string _email = string.Empty;
    private int _busy;

    public AccountPasswordPage(HimoApiClient api, AccountService account)
        : base("كلمة المرور", "استعادة كلمة المرور بطريقة آمنة")
    {
        _api = api; _account = account;
        _sendButton = SecondaryButton("إرسال رمز التحقق إلى البريد", SendCodeClicked);
        _saveButton = PrimaryButton("حفظ كلمة المرور الجديدة", SaveClicked);
        SetContent(
            Card(new VerticalStackLayout { Spacing = 10, Children = { Heading("البريد المرتبط بالحساب"), _emailLabel, _sendButton } }),
            Card(new VerticalStackLayout { Spacing = 10, Children = { Heading("رمز التحقق"), EntryBox(_code), Heading("كلمة المرور الجديدة"), EntryBox(_newPassword), Heading("تأكيد كلمة المرور"), EntryBox(_confirmPassword), _status, _saveButton } }),
            Hint("إذا لم يصل الرمز، تحقق من البريد غير المرغوب فيه قبل طلب رمز آخر.")
        );
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _email = _account.CurrentAccount?.Email ?? string.Empty;
        if (_api.HasToken)
        {
            try { var profile = await _api.GetMyProfileAsync(); _email = profile.Email ?? _email; }
            catch { }
        }
        _emailLabel.Text = string.IsNullOrWhiteSpace(_email) ? "تعذر تحديد البريد؛ سجّل الدخول مجددًا." : _email;
        _emailLabel.FlowDirection = FlowDirection.LeftToRight;
        _emailLabel.HorizontalTextAlignment = TextAlignment.Start;
    }

    private async void SendCodeClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        if (string.IsNullOrWhiteSpace(_email)) { _status.Text = "تعذر تحديد البريد الإلكتروني الحالي."; Interlocked.Exchange(ref _busy, 0); return; }
        _sendButton.IsEnabled = false; _sendButton.Text = "جارٍ الإرسال...";
        try
        {
            await _api.RequestPasswordResetAsync(_email);
            _status.Text = "إذا كان البريد مسجلًا، فسيصلك رمز التحقق. تحقق من الوارد والرسائل غير المرغوب فيها.";
        }
        catch (Exception ex) { _status.Text = "تعذر إرسال الرمز."; await DisplayAlertAsync("تعذر إرسال الرمز", ex.Message, "حسنًا"); }
        finally { _sendButton.IsEnabled = true; _sendButton.Text = "إرسال رمز التحقق إلى البريد"; Interlocked.Exchange(ref _busy, 0); }
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        var code = PhoneNumberUtils.NormalizeDigits(_code.Text?.Trim());
        var password = _newPassword.Text ?? string.Empty;
        if (!PhoneNumberUtils.IsValidVerificationCode(code)) { await DisplayAlertAsync("كلمة المرور", "أدخل رمز تحقق مكوّنًا من 6 أرقام.", "حسنًا"); Interlocked.Exchange(ref _busy, 0); return; }
        if (password.Length is < 8 or > 128) { await DisplayAlertAsync("كلمة المرور", "يجب أن تتراوح كلمة المرور بين 8 و128 حرفًا.", "حسنًا"); Interlocked.Exchange(ref _busy, 0); return; }
        if (!string.Equals(password, _confirmPassword.Text, StringComparison.Ordinal)) { await DisplayAlertAsync("كلمة المرور", "كلمتا المرور غير متطابقتين.", "حسنًا"); Interlocked.Exchange(ref _busy, 0); return; }
        _saveButton.IsEnabled = false; _saveButton.Text = "جارٍ التحديث...";
        try
        {
            var account = await _api.ResetPasswordAsync(_email, code, password);
            _account.SignIn(account.UserId, account.Email, account.Name);
            _code.Text = _newPassword.Text = _confirmPassword.Text = string.Empty;
            _status.Text = "تم تغيير كلمة المرور بنجاح.";
            await DisplayAlertAsync("تم التحديث", "تم تغيير كلمة المرور بنجاح.", "حسنًا");
        }
        catch (Exception ex) { _status.Text = "تعذر تغيير كلمة المرور."; await DisplayAlertAsync("تعذر تغيير كلمة المرور", ex.Message, "حسنًا"); }
        finally { _saveButton.IsEnabled = true; _saveButton.Text = "حفظ كلمة المرور الجديدة"; Interlocked.Exchange(ref _busy, 0); }
    }
}

public sealed class AddAccountPage : AccountSubPageBase
{
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly ProfileService _profile;
    private readonly Entry _email = MakeEntry("البريد الإلكتروني", Keyboard.Email, maxLength: 254);
    private readonly Entry _password = MakeEntry("كلمة المرور", password: true, maxLength: 128);
    private readonly Label _status = Hint("أدخل بيانات الحساب الذي تريد تسجيل الدخول به.");
    private readonly Button _loginButton;
    private int _busy;

    public AddAccountPage(HimoApiClient api, AccountService account, ProfileService profile)
        : base("إضافة حساب", "تسجيل الدخول باستخدام حساب آخر")
    {
        _api = api; _account = account; _profile = profile;
        _loginButton = PrimaryButton("تسجيل الدخول", LoginClicked);
        SetContent(
            Card(new VerticalStackLayout { Spacing = 11, Children = { Heading("البريد الإلكتروني"), EntryBox(_email), Heading("كلمة المرور"), EntryBox(_password), _status, _loginButton } }),
            Card(new VerticalStackLayout { Spacing = 6, Children = { Heading("معلومة مهمة"), Hint("الإصدار الحالي يحتفظ بجلسة نشطة واحدة فقط. عند نجاح تسجيل الدخول بحساب آخر، سيصبح الحساب الجديد هو النشط بدل الحساب الحالي. لن يتم تنفيذ التبديل إلا بعد تأكيدك.") } })
        );
        _email.Keyboard = Keyboard.Email;
        _email.FlowDirection = FlowDirection.LeftToRight;
        _email.HorizontalTextAlignment = TextAlignment.Start;
        _password.FlowDirection = FlowDirection.LeftToRight;
        _password.HorizontalTextAlignment = TextAlignment.Start;
    }

    private async void LoginClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _busy, 1) != 0) return;
        var email = _email.Text?.Trim() ?? string.Empty;
        var password = _password.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 254)
        {
            await DisplayAlertAsync("تسجيل الدخول", "أدخل بريدًا إلكترونيًا صحيحًا.", "حسنًا"); Interlocked.Exchange(ref _busy, 0); return;
        }
        if (password.Length < 8 || password.Length > 128)
        {
            await DisplayAlertAsync("تسجيل الدخول", "أدخل كلمة مرور صحيحة لا تقل عن 8 أحرف.", "حسنًا"); Interlocked.Exchange(ref _busy, 0); return;
        }
        var approved = await DisplayAlertAsync("تبديل الحساب", "سيؤدي تسجيل الدخول إلى جعل الحساب الجديد هو النشط على هذا الجهاز. هل تريد المتابعة؟", "متابعة", "إلغاء");
        if (!approved) { Interlocked.Exchange(ref _busy, 0); return; }
        _loginButton.IsEnabled = false; _loginButton.Text = "جارٍ تسجيل الدخول...";
        try
        {
            var loggedIn = await _api.LoginAsync(email, password);
            _account.SignIn(loggedIn.UserId, loggedIn.Email, loggedIn.Name);
            _profile.Update(loggedIn.Name, "متاح على Himo");
#if ANDROID
            _ = AppRuntimeBridge.RegisterCurrentPushTokenAsync();
#endif
            AppRuntimeBridge.MarkSessionUnlocked();
            var window = Application.Current?.Windows.FirstOrDefault();
            if (window is not null)
                window.Page = new Himo.AppShell();
            else
                _status.Text = "تم تسجيل الدخول، لكن تعذر إعادة فتح الواجهة الرئيسية تلقائيًا.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر تسجيل الدخول بالحساب الآخر.";
            await DisplayAlertAsync("تعذر تسجيل الدخول", ex.Message, "حسنًا");
        }
        finally { _loginButton.IsEnabled = true; _loginButton.Text = "تسجيل الدخول"; Interlocked.Exchange(ref _busy, 0); }
    }
}
