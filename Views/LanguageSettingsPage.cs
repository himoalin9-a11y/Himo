using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Persists the user's language choice and applies its culture and text direction.
/// App-wide text translation requires localized string resources; this page does not
/// pretend that changing CultureInfo translates hard-coded Arabic labels by itself.
/// </summary>
public sealed class LanguageSettingsPage : ContentPage
{
    private const string LanguagePreferenceKey = "himo_app_language";

    private sealed record LanguageOption(string Code, string NativeName, string LocalName, bool IsRtl);

    private static readonly LanguageOption[] Languages =
    {
        new("ar", "العربية", "Arabic", true),
        new("en", "English", "الإنجليزية", false),
        new("fr", "Français", "الفرنسية", false),
        new("tr", "Türkçe", "التركية", false),
        new("ur", "اردو", "الأردية", true),
        new("es", "Español", "الإسبانية", false),
        new("id", "Bahasa Indonesia", "الإندونيسية", false)
    };

    private readonly VerticalStackLayout _optionsLayout = new() { Spacing = 9 };
    private readonly Dictionary<string, Border> _optionBorders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Label> _optionChecks = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedCode;

    public LanguageSettingsPage()
    {
        Title = "لغة التطبيق";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        _selectedCode = Preferences.Default.Get(LanguagePreferenceKey, "ar");
        if (!Languages.Any(x => x.Code.Equals(_selectedCode, StringComparison.OrdinalIgnoreCase)))
            _selectedCode = "ar";

        Content = BuildPage();
        RefreshSelectedOption();
    }

    private View BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) },
            RowSpacing = 0
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            Padding = new Thickness(18, 14, 18, 12),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(48)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(48)) },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.RightToLeft
        };

        var back = new Button
        {
            Text = "‹", FontSize = 34, FontAttributes = FontAttributes.Bold,
            WidthRequest = 42, HeightRequest = 42, Padding = 0,
            CornerRadius = 15, BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center
        };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        back.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var titles = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var title = new Label { Text = "لغة التطبيق", FontSize = 23, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subtitle = new Label { Text = "اختر اللغة التي تفضّل استخدامها", FontSize = 12, HorizontalTextAlignment = TextAlignment.Start };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        titles.Add(title);
        titles.Add(subtitle);
        header.Add(titles, 1, 0);

        var scrollContent = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 20), Spacing = 14 };
        var sectionTitle = new Label { Text = "اللغات المتاحة", FontSize = 14, FontAttributes = FontAttributes.Bold };
        sectionTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        scrollContent.Add(sectionTitle);

        foreach (var language in Languages)
            scrollContent.Add(BuildLanguageOption(language));

        var note = new Label
        {
            Text = "سيُحفظ اختيارك. بعض النصوص الحالية مكتوبة مباشرة داخل الواجهات، وستحتاج إلى موارد ترجمة حتى تتبدّل جميع الشاشات بالكامل.",
            FontSize = 11,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalTextAlignment = TextAlignment.Start
        };
        note.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        scrollContent.Add(note);

        var save = new Button
        {
            Text = "حفظ اللغة", HeightRequest = 50, CornerRadius = 16,
            FontSize = 15, FontAttributes = FontAttributes.Bold
        };
        save.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        save.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        save.Clicked += SaveLanguageClicked;
        scrollContent.Add(save);

        var scroll = new ScrollView { Content = scrollContent, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        root.Add(header, 0, 0);
        root.Add(scroll, 0, 1);
        return root;
    }

    private Border BuildLanguageOption(LanguageOption language)
    {
        var border = new Border { Padding = new Thickness(14, 12), StrokeThickness = 1.2 };
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        border.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        border.StrokeShape = new RoundRectangle { CornerRadius = 16 };

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(32) },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight
        };
        var texts = new VerticalStackLayout { Spacing = 2, FlowDirection = FlowDirection.LeftToRight };
        var native = new Label { Text = language.NativeName, FontSize = 15, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Start };
        native.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var local = new Label { Text = language.LocalName, FontSize = 11, HorizontalTextAlignment = TextAlignment.Start };
        local.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        texts.Add(native);
        texts.Add(local);
        grid.Add(texts, 0, 0);

        var check = new Label { Text = "○", FontSize = 22, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        check.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        grid.Add(check, 1, 0);
        border.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _selectedCode = language.Code;
            RefreshSelectedOption();
        };
        border.GestureRecognizers.Add(tap);
        _optionBorders[language.Code] = border;
        _optionChecks[language.Code] = check;
        return border;
    }

    private void RefreshSelectedOption()
    {
        foreach (var language in Languages)
        {
            var selected = language.Code.Equals(_selectedCode, StringComparison.OrdinalIgnoreCase);
            var border = _optionBorders.GetValueOrDefault(language.Code);
            var check = _optionChecks.GetValueOrDefault(language.Code);
            if (border is not null)
            {
                border.StrokeThickness = selected ? 2 : 1;
                border.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, selected ? "HimoSoft" : "HimoCard");
            }
            if (check is not null)
            {
                check.Text = selected ? "✓" : "○";
                check.SetDynamicResource(Label.TextColorProperty, selected ? "HimoAccent" : "HimoTextSecondary");
            }
        }
    }

    private async void SaveLanguageClicked(object? sender, EventArgs e)
    {
        var selected = Languages.First(x => x.Code.Equals(_selectedCode, StringComparison.OrdinalIgnoreCase));
        Preferences.Default.Set(LanguagePreferenceKey, selected.Code);

        try
        {
            var culture = CultureInfo.GetCultureInfo(selected.Code);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // The choice remains saved even if a platform lacks the culture definition.
        }

        var direction = selected.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        if (Application.Current is { } app)
        {
            foreach (var window in app.Windows)
            {
                if (window.Page is not null)
                    window.Page.FlowDirection = direction;
            }
        }

        if (Shell.Current is not null)
            Shell.Current.FlowDirection = direction;

        var message = selected.Code == "en"
            ? "Language preference saved. Some screens still contain fixed Arabic text and need localization resources."
            : "تم حفظ تفضيل اللغة. بعض الشاشات تحتوي نصوصًا عربية ثابتة وتحتاج إلى موارد ترجمة لتغييرها بالكامل.";
        var title = selected.Code == "en" ? "Language saved" : "تم حفظ اللغة";
        await DisplayAlertAsync(title, message, selected.Code == "en" ? "OK" : "حسنًا");
        await BackAsync();
    }

    private async void BackClicked(object? sender, EventArgs e) => await BackAsync();

    private async Task BackAsync()
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..");
    }
}
