using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>Groups the app-wide appearance and experience preferences.</summary>
public sealed class AppearanceExperiencePage : ContentPage
{
    private readonly Switch _darkModeSwitch = new();
    private readonly Button _fontSizeButton = new();
    private bool _initializingDarkMode;
    private readonly Dictionary<VisualElement, double> _originalFontSizes = new();

    public AppearanceExperiencePage()
    {
        Title = "المظهر والتجربة";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            BackgroundColor = Colors.Transparent
        };
        root.Add(CreateHeaderForFeatures("المظهر والتجربة", "تحكم في طريقة ظهور Himo واستخدامه", async () => await Navigation.PopAsync(true)), 0, 0);

        var content = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 22), Spacing = 12 };
        content.Add(CreateSectionLabel("تفضيلات العرض"));
        content.Add(CreateCard(BuildDarkModeRow()));
        content.Add(CreateCard(CreateActionRow("لغة التطبيق", "اختر لغة واجهات Himo", "himo_pro_ui_settings_icon_globe.png", async () => await Navigation.PushAsync(new LanguageSettingsPage(), true))));
        content.Add(CreateCard(BuildFontSizeRow()));
        content.Add(CreateSectionLabel("التخصيص"));
        content.Add(CreateCard(CreateActionRow("السمات والأيقونات", "اختيار ألوان الواجهات ومجموعة الأيقونات", "himo_pro_ui_settings_icon_palette.png", async () => await Navigation.PushAsync(new IconsAppearanceSettingsPage(), true))));

        root.Add(new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        Content = root;
        AppLocalization.AttachToPage(this);
        _initializingDarkMode = true;
        _darkModeSwitch.IsToggled = HimoAppearanceManager.IsActiveStyleDark;
        _initializingDarkMode = false;
        UpdateFontSizeLabel();
        CaptureOriginalFontSizes(this);
    }

    private View BuildDarkModeRow()
    {
        var row = CreateRowShell("الوضع الداكن", "تشغيل المظهر الداكن أو الفاتح", "himo_pro_ui_settings_icon_palette.png");
        _darkModeSwitch.HorizontalOptions = LayoutOptions.Start;
        _darkModeSwitch.VerticalOptions = LayoutOptions.Center;
        _darkModeSwitch.Toggled += DarkModeToggled;
        row.Add(_darkModeSwitch, 0, 0);
        return row;
    }

    private View BuildFontSizeRow()
    {
        var row = CreateRowShell("حجم الخط", "تعديل حجم النص في واجهة الإعدادات", "himo_pro_ui_settings_icon_text.png");
        _fontSizeButton.Text = "متوسط";
        _fontSizeButton.FontSize = 10.5;
        _fontSizeButton.Padding = new Thickness(7, 0);
        _fontSizeButton.HeightRequest = 32;
        _fontSizeButton.CornerRadius = 12;
        _fontSizeButton.VerticalOptions = LayoutOptions.Center;
        _fontSizeButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        _fontSizeButton.SetDynamicResource(Button.TextColorProperty, "HimoTextSecondary");
        _fontSizeButton.Clicked += FontSizeClicked;
        row.Add(_fontSizeButton, 0, 0);
        return row;
    }

    private void DarkModeToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializingDarkMode) return;
        HimoAppearanceManager.SetDarkModeEnabled(e.Value);
        _initializingDarkMode = true;
        _darkModeSwitch.IsToggled = HimoAppearanceManager.IsActiveStyleDark;
        _initializingDarkMode = false;
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this, batchSize: 48);
    }

    private async void FontSizeClicked(object? sender, EventArgs e)
    {
        var current = Preferences.Default.Get("himo_settings_font_scale", 1.0);
        var selectedDisplay = await DisplayActionSheetAsync(
            AppLocalization.TranslateSourceText("حجم الخط"), AppLocalization.TranslateSourceText("إلغاء"), null,
            AppLocalization.TranslateSourceText("صغير · 90%"), AppLocalization.TranslateSourceText("متوسط · 100%"), AppLocalization.TranslateSourceText("كبير · 115%"));
        var selected = AppLocalization.ResolveSourceText(selectedDisplay, "صغير · 90%", "متوسط · 100%", "كبير · 115%");
        var scale = selected switch
        {
            "صغير · 90%" => 0.90,
            "متوسط · 100%" => 1.00,
            "كبير · 115%" => 1.15,
            _ => current
        };
        if (selected is not ("صغير · 90%" or "متوسط · 100%" or "كبير · 115%")) return;
        Preferences.Default.Set("himo_settings_font_scale", scale);
        UpdateFontSizeLabel();
        ApplyFontScale(scale);
    }

    private void UpdateFontSizeLabel()
    {
        var scale = Preferences.Default.Get("himo_settings_font_scale", 1.0);
        _fontSizeButton.Text = scale < 0.95 ? "صغير" : scale > 1.05 ? "كبير" : "متوسط";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _initializingDarkMode = true;
        _darkModeSwitch.IsToggled = HimoAppearanceManager.IsActiveStyleDark;
        _initializingDarkMode = false;
        ApplyFontScale(Preferences.Default.Get("himo_settings_font_scale", 1.0));
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this, batchSize: 48);
    }

    private void CaptureOriginalFontSizes(VisualElement element)
    {
        var size = element switch
        {
            Label label => label.FontSize,
            Button button => button.FontSize,
            Entry entry => entry.FontSize,
            Editor editor => editor.FontSize,
            SearchBar search => search.FontSize,
            Picker picker => picker.FontSize,
            _ => -1
        };
        if (size > 0 && double.IsFinite(size)) _originalFontSizes.TryAdd(element, size);
        if (element is IVisualTreeElement visualTree)
            foreach (var child in visualTree.GetVisualChildren())
                if (child is VisualElement visualChild) CaptureOriginalFontSizes(visualChild);
    }

    private void ApplyFontScale(double scale)
    {
        scale = Math.Clamp(scale, 0.85, 1.20);
        foreach (var item in _originalFontSizes)
        {
            if (item.Key.Handler is null) continue;
            var value = Math.Round(item.Value * scale, 1);
            switch (item.Key)
            {
                case Label label: label.FontSize = value; break;
                case Button button: button.FontSize = value; break;
                case Entry entry: entry.FontSize = value; break;
                case Editor editor: editor.FontSize = value; break;
                case SearchBar search: search.FontSize = value; break;
                case Picker picker: picker.FontSize = value; break;
            }
        }
    }

    public static View CreateHeaderForFeatures(string titleText, string subtitleText, Func<Task> backAction)
    {
        var header = new Grid
        {
            Padding = new Thickness(18, 14, 18, 12),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(48)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(48)) },
            ColumnSpacing = 8, FlowDirection = FlowDirection.RightToLeft
        };
        var back = new Button { Text = "‹", FontSize = 34, FontAttributes = FontAttributes.Bold, HeightRequest = 42, WidthRequest = 42, Padding = 0, CornerRadius = 15, BorderWidth = 1 };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        back.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        back.Clicked += async (_, _) => await backAction();
        header.Add(back, 0, 0);
        var titles = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        var title = new Label { Text = titleText, FontSize = 23, FontAttributes = FontAttributes.Bold };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subtitle = new Label { Text = subtitleText, FontSize = 12 };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        titles.Add(title); titles.Add(subtitle);
        header.Add(titles, 1, 0);
        return header;
    }

    private static Label CreateSectionLabel(string text)
    {
        var label = new Label { Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold, Margin = new Thickness(2, 2, 2, 0) };
        label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        return label;
    }

    public static Border CreateCard(View content)
    {
        var card = new Border { Padding = 0, StrokeThickness = 1, Content = content };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 };
        return card;
    }

    private static Grid CreateActionRow(string title, string subtitle, string icon, Func<Task> action)
    {
        var row = CreateRowShell(title, subtitle, icon);
        var chevron = new Image { Source = "himo_pro_ui_settings_icon_chevron.png", WidthRequest = 16, HeightRequest = 16, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        row.Add(chevron, 0, 0);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private static Grid CreateRowShell(string title, string subtitle, string icon)
    {
        var row = new Grid
        {
            Padding = new Thickness(13, 11), ColumnDefinitions = { new ColumnDefinition(new GridLength(30)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(42)) },
            ColumnSpacing = 0, FlowDirection = FlowDirection.LeftToRight, MinimumHeightRequest = 68
        };
        var iconBorder = new Border { WidthRequest = 42, HeightRequest = 42, StrokeThickness = 0, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        iconBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        iconBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 21 };
        iconBorder.Content = new Image { Source = icon, WidthRequest = 23, HeightRequest = 23, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        row.Add(iconBorder, 2, 0);
        var texts = new VerticalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var heading = new Label { Text = title, FontSize = 14.5, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = subtitle, FontSize = 11, HorizontalTextAlignment = TextAlignment.Start };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        texts.Add(heading); texts.Add(detail);
        row.Add(texts, 1, 0);
        return row;
    }
}

/// <summary>Contains optional app features: application lock and the app download link.</summary>
public sealed class FeaturesSettingsPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly AppLockService _appLock;
    private readonly Switch _appLockSwitch = new();
    private readonly Entry _pinEntry = new() { Placeholder = "رمز جديد (4-6 أرقام)", Keyboard = Keyboard.Numeric, IsPassword = true, MaxLength = 6, IsVisible = false };
    private readonly Button _savePinButton = new() { Text = "حفظ رمز القفل", HeightRequest = 40, CornerRadius = 13, IsVisible = false };
    private readonly Label _lockStatus = new() { FontSize = 10.5 };
    private bool _initializingLockSwitch;

    public FeaturesSettingsPage(HimoApiClient api, AppLockService appLock)
    {
        _api = api;
        _appLock = appLock;
        Title = "الميزات";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            BackgroundColor = Colors.Transparent
        };
        root.Add(AppearanceExperiencePage.CreateHeaderForFeatures("الميزات", "إعدادات إضافية لحماية التطبيق ومشاركته", async () => await Navigation.PopAsync(true)), 0, 0);
        var content = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 22), Spacing = 12 };
        var title = new Label { Text = "حماية التطبيق", FontSize = 14, FontAttributes = FontAttributes.Bold, Margin = new Thickness(2, 2, 2, 0) };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        content.Add(title);
        content.Add(BuildLockCard());
        var shareTitle = new Label { Text = "المشاركة والتنزيل", FontSize = 14, FontAttributes = FontAttributes.Bold, Margin = new Thickness(2, 6, 2, 0) };
        shareTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        content.Add(shareTitle);
        content.Add(BuildDownloadLinkCard());
        root.Add(new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        Content = root;
        AppLocalization.AttachToPage(this);

        _appLockSwitch.Toggled += AppLockToggled;
        _savePinButton.Clicked += SavePinClicked;
        ApplyControlColors();
        UpdateLockState();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateLockState();
    }

    private View BuildLockCard()
    {
        var stack = new VerticalStackLayout { Spacing = 0 };
        var row = new Grid { Padding = new Thickness(13, 11), ColumnDefinitions = { new ColumnDefinition(new GridLength(30)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(42)) }, ColumnSpacing = 0, FlowDirection = FlowDirection.LeftToRight, MinimumHeightRequest = 68 };
        var icon = new Border { WidthRequest = 42, HeightRequest = 42, StrokeThickness = 0, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        icon.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        icon.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 21 };
        icon.Content = new Image { Source = "himo_pro_ui_settings_icon_lock.png", WidthRequest = 23, HeightRequest = 23, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        row.Add(icon, 2, 0);
        var texts = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var heading = new Label { Text = "قفل التطبيق", FontSize = 14.5, FontAttributes = FontAttributes.Bold };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = "تعيين رمز مرور لحماية التطبيق", FontSize = 11 };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        texts.Add(heading); texts.Add(detail);
        row.Add(texts, 1, 0);
        _appLockSwitch.HorizontalOptions = LayoutOptions.Start;
        _appLockSwitch.VerticalOptions = LayoutOptions.Center;
        row.Add(_appLockSwitch, 0, 0);
        stack.Add(row);

        var form = new VerticalStackLayout { Padding = new Thickness(14, 0, 14, 12), Spacing = 8 };
        _pinEntry.TextColor = ResolveResourceColor("HimoPrimary");
        _pinEntry.PlaceholderColor = ResolveResourceColor("HimoTextSecondary");
        _pinEntry.BackgroundColor = ResolveResourceColor("HimoBackground");
        _savePinButton.BackgroundColor = ResolveResourceColor("HimoAccent");
        _savePinButton.TextColor = ResolveResourceColor("HimoOnAccent");
        _lockStatus.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        form.Add(_pinEntry); form.Add(_savePinButton); form.Add(_lockStatus);
        stack.Add(form);
        return CreateCard(stack);
    }

    private View BuildDownloadLinkCard()
    {
        var row = new Grid { Padding = new Thickness(13, 11), ColumnDefinitions = { new ColumnDefinition(new GridLength(30)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(42)) }, ColumnSpacing = 0, FlowDirection = FlowDirection.LeftToRight, MinimumHeightRequest = 68 };
        var icon = new Border { WidthRequest = 42, HeightRequest = 42, StrokeThickness = 0, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
        icon.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        icon.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 21 };
        icon.Content = new Image { Source = "himo_pro_ui_settings_icon_globe.png", WidthRequest = 23, HeightRequest = 23, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        row.Add(icon, 2, 0);
        var texts = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var heading = new Label { Text = "رابط التطبيق", FontSize = 14.5, FontAttributes = FontAttributes.Bold };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = "مشاركة رابط تنزيل Himo", FontSize = 11 };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        texts.Add(heading); texts.Add(detail); row.Add(texts, 1, 0);
        row.Add(new Image { Source = "himo_pro_ui_settings_icon_chevron.png", WidthRequest = 16, HeightRequest = 16, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }, 0, 0);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try { await Navigation.PushAsync(new AppDownloadLinkPage(_api), true); }
            catch (Exception ex) { await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("رابط التطبيق"),Himo.Services.AppLocalization.TranslateSourceText("تعذر فتح صفحة رابط التطبيق: " + Himo.Services.AppLocalization.TranslateSourceText(ex.Message)),Himo.Services.AppLocalization.TranslateSourceText("حسنًا")); }
        };
        row.GestureRecognizers.Add(tap);
        return CreateCard(row);
    }

    private async void AppLockToggled(object? sender, ToggledEventArgs e)
    {
        if (_initializingLockSwitch) return;
        if (e.Value)
        {
            _pinEntry.IsVisible = true;
            _savePinButton.IsVisible = true;
            _lockStatus.Text = "أدخل رمزًا جديدًا ثم اضغط حفظ.";
            return;
        }

        await _appLock.DisableAsync();
        _pinEntry.Text = string.Empty;
        _pinEntry.IsVisible = false;
        _savePinButton.IsVisible = false;
        _lockStatus.Text = "قفل التطبيق متوقف.";
    }

    private async void SavePinClicked(object? sender, EventArgs e)
    {
        try
        {
            await _appLock.SetPinAsync(_pinEntry.Text?.Trim() ?? string.Empty);
            AppRuntimeBridge.MarkSessionUnlocked();
            _pinEntry.Text = string.Empty;
            _pinEntry.IsVisible = false;
            _savePinButton.IsVisible = false;
            _lockStatus.Text = "تم تفعيل قفل التطبيق.";
            _initializingLockSwitch = true;
            _appLockSwitch.IsToggled = true;
            _initializingLockSwitch = false;
        }
        catch (Exception ex)
        {
            _lockStatus.Text = ex.Message;
        }
    }

    private void UpdateLockState()
    {
        var enabled = _appLock.IsEnabled;
        _initializingLockSwitch = true;
        _appLockSwitch.IsToggled = enabled;
        _initializingLockSwitch = false;
        _pinEntry.IsVisible = false;
        _savePinButton.IsVisible = false;
        _lockStatus.Text = enabled ? "قفل التطبيق مفعل." : "قفل التطبيق متوقف.";
    }

    private void ApplyControlColors()
    {
        _savePinButton.BackgroundColor = ResolveResourceColor("HimoAccent");
        _savePinButton.TextColor = ResolveResourceColor("HimoOnAccent");
        _pinEntry.BackgroundColor = ResolveResourceColor("HimoBackground");
        _pinEntry.TextColor = ResolveResourceColor("HimoPrimary");
        _pinEntry.PlaceholderColor = ResolveResourceColor("HimoTextSecondary");
    }

    private static Color ResolveResourceColor(string key)
    {
        var app = Application.Current;
        if (app is not null && app.Resources.TryGetValue(key, out var value) && value is Color color)
            return color;
        return Colors.Transparent;
    }

    public static Border CreateCard(View content)
    {
        var card = new Border { Padding = 0, StrokeThickness = 1, Content = content };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 };
        return card;
    }
}
