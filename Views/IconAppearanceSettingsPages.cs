using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>Entry page for Himo's launcher-icon and in-app theme customization.</summary>
public sealed class IconsAppearanceSettingsPage : SettingsHubPageBase
{
    public IconsAppearanceSettingsPage()
        : base("السمات والأيقونات", "اختر ما تريد تخصيصه في Himo")
    {
        AddIntroCard("تخصيص Himo", "اختر مجموعة أيقونات التطبيق، أو إحدى 6 سمات متكاملة تغيّر ألوان الشاشات والبطاقات والرموز الداخلية.", "himo_pro_ui_settings_icon_palette.png");
        AddNavigationCard("الأيقونات", "اختيار نوع الأيقونات المستخدمة في واجهة Himo", "himo_pro_ui_settings_icon_gear.png",
            async () =>
            {
                if (Shell.Current is not null) await HimoShellNavigator.NavigateAsync("icon-selection", true);
                else await Navigation.PushAsync(new IconSelectionSettingsPage(), true);
            });
        AddNavigationCard("سمات الواجهة", "6 سمات تغيّر ألوان التطبيق وتصميم البطاقات والرموز الداخلية", "himo_pro_ui_settings_icon_palette.png",
            async () =>
            {
                if (Shell.Current is not null) await HimoShellNavigator.NavigateAsync("appearance-settings", true);
                else await Navigation.PushAsync(new AppearanceSettingsPage(), true);
            });
    }
}

/// <summary>Lets the user choose between system icons and professional icons.</summary>
public sealed class IconSelectionSettingsPage : SettingsHubPageBase
{
    public IconSelectionSettingsPage()
        : base("الأيقونات", "اختر مجموعة الأيقونات التي تريد إعدادها")
    {
        AddNavigationCard("أيقونات النظام", "الأيقونات الأساسية البسيطة", "himo_pro_ui_settings_icon_gear.png",
            async () =>
            {
                if (Shell.Current is not null) await HimoShellNavigator.NavigateAsync("system-icons-settings", true);
                else await Navigation.PushAsync(new SystemIconsSettingsPage(), true);
            });
        AddNavigationCard("أيقونات زجاجية", "شعارات Himo زجاجية لامعة بـ15 لونًا", "himo_glass_icon_purple.png",
            async () =>
            {
                if (Shell.Current is not null) await HimoShellNavigator.NavigateAsync("glass-icons-settings", true);
                else await Navigation.PushAsync(new GlassIconsSettingsPage(), true);
            });
        AddNavigationCard("أيقونات احترافية", "25 أيقونة لامعة وفاخرة لهوية Himo", "himo_professional_icon_01.png",
            async () =>
            {
                if (Shell.Current is not null) await HimoShellNavigator.NavigateAsync("professional-icons-settings", true);
                else await Navigation.PushAsync(new ProfessionalIconsSettingsPage(), true);
            });
    }
}

/// <summary>Gallery of Himo logo colorways for the system-icon collection.</summary>
public sealed class SystemIconsSettingsPage : SettingsHubPageBase
{
    private const string PreferenceKey = "Himo.SystemIconVariant";
    private readonly Dictionary<string, Border> _optionCards = new(StringComparer.Ordinal);
    private readonly Label _applyStatus = new()
    {
        FontSize = 12, TextColor = Color.FromArgb("#5B2D91"),
        HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 8)
    };
    private string _selectedKey = "himo_system_icon_original.png";

    private static readonly IconOption[] Options =
    {
        new("himo_system_icon_original.png", "البنفسجي الأصلي"),
        new("himo_system_icon_sapphire.png", "الأزرق الياقوتي"),
        new("himo_system_icon_azure.png", "الأزرق السماوي"),
        new("himo_system_icon_cyan.png", "السماوي اللامع"),
        new("himo_system_icon_turquoise.png", "الفيروزي"),
        new("himo_system_icon_emerald.png", "الأخضر الزمردي"),
        new("himo_system_icon_green.png", "الأخضر الزاهي"),
        new("himo_system_icon_lime.png", "الأخضر الليموني"),
        new("himo_system_icon_gold.png", "الذهبي"),
        new("himo_system_icon_amber.png", "الكهرماني"),
        new("himo_system_icon_orange.png", "البرتقالي اللامع"),
        new("himo_system_icon_ruby.png", "الأحمر الياقوتي"),
        new("himo_system_icon_rose.png", "الوردي اللامع"),
        new("himo_system_icon_magenta.png", "الأرجواني"),
        new("himo_system_icon_indigo.png", "النيلي")
    };

    public SystemIconsSettingsPage()
        : base("أيقونات النظام", "اختر لون شعار Himo مع الحفاظ على تصميمه الأصلي")
    {
        _selectedKey = HimoLauncherIconService.GetSelectedVariant();
        if (!Options.Any(option => string.Equals(option.FileName, _selectedKey, StringComparison.Ordinal)))
            _selectedKey = string.Empty;

        AddIntroCard(
            "15 لونًا للشعار نفسه",
            "اختر اللون الذي تفضله. الضغط على الأيقونة يطبقها على مشغّل Android ويحفظ اختيارك داخل التطبيق.",
            "himo_system_icon_original.png");

        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 10,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill
        };

        for (var column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (var row = 0; row < 5; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < Options.Length; index++)
        {
            var option = Options[index];
            var card = CreateOptionCard(option);
            _optionCards[option.FileName] = card;
            grid.Add(card, index % 3, index / 3);
        }

        Body.Children.Add(grid);
        AddIntroCard(
            "تطبيق الأيقونة",
            "اضغط على أي أيقونة لتطبيقها كأيقونة Himo على الشاشة الرئيسية. قد يحتاج مشغّل الهاتف بضع ثوانٍ لتحديثها.",
            "himo_pro_ui_settings_icon_check.png");
        Body.Children.Add(_applyStatus);
    }

    private Border CreateOptionCard(IconOption option)
    {
        var selected = string.Equals(option.FileName, _selectedKey, StringComparison.Ordinal);
        var image = new Image
        {
            Source = ImageSource.FromFile(option.FileName),
            WidthRequest = 76,
            HeightRequest = 76,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var preview = new Grid
        {
            HeightRequest = 80,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        preview.Add(image);
        var selectedMark = new Border
        {
            WidthRequest = 21,
            HeightRequest = 21,
            Padding = 0,
            BackgroundColor = Color.FromArgb("#6C25D9"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(11) },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = selected,
            Content = new Label
            {
                Text = "✓",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        preview.Add(selectedMark);

        var name = new Label
        {
            Text = option.Title,
            FontSize = 11.5,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = Color.FromArgb("#291841"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumHeightRequest = 32
        };

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(4, 8),
            Spacing = 5,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { preview, name }
        };

        var card = new Border
        {
            BackgroundColor = selected ? Color.FromArgb("#F0E6FF") : Colors.White,
            Stroke = new SolidColorBrush(selected ? Color.FromArgb("#7028E8") : Color.FromArgb("#E3D9F2")),
            StrokeThickness = selected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) },
            Padding = 0,
            Content = content,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectOption(option);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void SelectOption(IconOption option)
    {
        _selectedKey = option.FileName;
        Preferences.Set(PreferenceKey, _selectedKey);
        HimoLauncherIconService.TryApply(option.FileName, out var applyMessage);
        _applyStatus.Text = applyMessage;

        foreach (var candidate in Options)
        {
            if (!_optionCards.TryGetValue(candidate.FileName, out var card))
                continue;

            var selected = string.Equals(candidate.FileName, _selectedKey, StringComparison.Ordinal);
            card.BackgroundColor = selected ? Color.FromArgb("#F0E6FF") : Colors.White;
            card.Stroke = new SolidColorBrush(selected ? Color.FromArgb("#7028E8") : Color.FromArgb("#E3D9F2"));
            card.StrokeThickness = selected ? 2 : 1;
            if (card.Content is VerticalStackLayout layout && layout.Children.FirstOrDefault() is Grid preview)
            {
                foreach (var child in preview.Children)
                {
                    if (child is Border mark)
                        mark.IsVisible = selected;
                }
            }
        }
    }

    private sealed record IconOption(string FileName, string Title);
}

/// <summary>
/// Gallery of glass-style Himo wordmark icons. Selecting a tile applies its image as
/// the Android launcher icon through a dedicated activity-alias component.
/// </summary>
public sealed class GlassIconsSettingsPage : SettingsHubPageBase
{
    private const string PreferenceKey = "Himo.GlassIconVariant";
    private readonly Dictionary<string, Border> _optionCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _optionActions = new(StringComparer.Ordinal);
    private readonly Label _applyStatus = new()
    { FontSize = 12, TextColor = Color.FromArgb("#D6B4FF"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
    private string _selectedKey = "himo_glass_icon_purple.png";

    private static readonly GlassIconOption[] Options =
    {
        new("himo_glass_icon_purple.png", "بنفسجي زجاجي"),
        new("himo_glass_icon_sapphire.png", "أزرق ياقوتي"),
        new("himo_glass_icon_rose.png", "وردي لؤلؤي"),
        new("himo_glass_icon_gold.png", "ذهبي فاخر"),
        new("himo_glass_icon_turquoise.png", "فيروزي كريستالي"),
        new("himo_glass_icon_ruby.png", "ياقوتي أحمر"),
        new("himo_glass_icon_silver.png", "فضي بنفسجي"),
        new("himo_glass_icon_night.png", "ليلي داكن"),
        new("himo_glass_icon_holographic.png", "هولوغرافي"),
        new("himo_glass_icon_emerald.png", "زمردي متوهج"),
        new("himo_glass_icon_icecyan.png", "سماوي جليدي"),
        new("himo_glass_icon_amber.png", "كهرماني ناري"),
        new("himo_glass_icon_indigo.png", "نيلي عميق"),
        new("himo_glass_icon_lime.png", "أخضر ليموني"),
        new("himo_glass_icon_magenta.png", "أرجواني متوهج")
    };

    private static readonly Color CardBackground = Color.FromArgb("#21172C");
    private static readonly Color SelectedBackground = Color.FromArgb("#2D1942");
    private static readonly Color UnselectedStroke = Color.FromArgb("#594768");
    private static readonly Color SelectedStroke = Color.FromArgb("#D66AFF");
    private static readonly Color GlassText = Color.FromArgb("#FCF7FF");
    private static readonly Color GlassAccent = Color.FromArgb("#D6B4FF");

    public GlassIconsSettingsPage()
        : base("أيقونات زجاجية", "اختر شعار Himo بلمسة زجاجية ولون لامع")
    {
        _selectedKey = HimoLauncherIconService.GetSelectedVariant();
        if (!Options.Any(option => string.Equals(option.FileName, _selectedKey, StringComparison.Ordinal)))
            _selectedKey = string.Empty;

        AddIntroCard(
            "15 تصميمًا زجاجيًا",
            "كل الخيارات تحافظ على شكل حرف H وكلمة Himo. اضغط على التصميم لتطبيقه كأيقونة فعلية للتطبيق على الشاشة الرئيسية.",
            "himo_glass_icon_purple.png");

        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 12,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill
        };

        for (var column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (var row = 0; row < (int)Math.Ceiling(Options.Length / 3.0); row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < Options.Length; index++)
        {
            var option = Options[index];
            var card = CreateGlassOptionCard(option);
            _optionCards[option.FileName] = card;
            grid.Add(card, index % 3, index / 3);
        }

        Body.Children.Add(grid);
        Body.Children.Add(_applyStatus);
    }

    private Border CreateGlassOptionCard(GlassIconOption option)
    {
        var selected = string.Equals(option.FileName, _selectedKey, StringComparison.Ordinal);
        var preview = new Grid
        {
            HeightRequest = 82,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };

        preview.Add(new Image
        {
            Source = ImageSource.FromFile(option.FileName),
            WidthRequest = 78,
            HeightRequest = 78,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });

        var check = new Border
        {
            WidthRequest = 20,
            HeightRequest = 20,
            Padding = 0,
            BackgroundColor = SelectedStroke,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = selected,
            Content = new Label
            {
                Text = "✓",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#21172C"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        preview.Add(check);

        var name = new Label
        {
            Text = option.Title,
            FontSize = 11.5,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = GlassText,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumHeightRequest = 34
        };

        var action = new Label
        {
            Text = selected ? "محددة ✓" : "اختيار",
            FontSize = 11.5,
            FontAttributes = FontAttributes.Bold,
            TextColor = selected ? SelectedStroke : GlassAccent,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumHeightRequest = 24
        };
        _optionActions[option.FileName] = action;

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(5, 8, 5, 7),
            Spacing = 5,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { preview, name, action }
        };

        var card = new Border
        {
            BackgroundColor = selected ? SelectedBackground : CardBackground,
            Stroke = new SolidColorBrush(selected ? SelectedStroke : UnselectedStroke),
            StrokeThickness = selected ? 2.5 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) },
            Padding = 0,
            MinimumHeightRequest = 155,
            Content = content,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectGlassOption(option);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void SelectGlassOption(GlassIconOption option)
    {
        _selectedKey = option.FileName;
        Preferences.Set(PreferenceKey, _selectedKey);
        HimoLauncherIconService.TryApply(option.FileName, out var applyMessage);
        _applyStatus.Text = applyMessage;

        foreach (var candidate in Options)
        {
            var selected = string.Equals(candidate.FileName, _selectedKey, StringComparison.Ordinal);
            if (_optionCards.TryGetValue(candidate.FileName, out var card))
            {
                card.BackgroundColor = selected ? SelectedBackground : CardBackground;
                card.Stroke = new SolidColorBrush(selected ? SelectedStroke : UnselectedStroke);
                card.StrokeThickness = selected ? 2.5 : 1;

                if (card.Content is VerticalStackLayout layout && layout.Children.FirstOrDefault() is Grid preview)
                {
                    foreach (var child in preview.Children)
                        if (child is Border mark)
                            mark.IsVisible = selected;
                }
            }

            if (_optionActions.TryGetValue(candidate.FileName, out var action))
            {
                action.Text = selected ? "محددة ✓" : "اختيار";
                action.TextColor = selected ? SelectedStroke : GlassAccent;
            }
        }
    }

    private sealed record GlassIconOption(string FileName, string Title);
}

/// <summary>Gallery of glossy Himo professional icon designs.</summary>
public sealed class ProfessionalIconsSettingsPage : SettingsHubPageBase
{
    private const string PreferenceKey = "Himo.ProfessionalIconVariant";
    private readonly Dictionary<string, Border> _optionCards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Label> _optionActions = new(StringComparer.Ordinal);
    private readonly Label _applyStatus = new()
    { FontSize = 12, TextColor = Color.FromArgb("#F4C66B"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 8) };
    private string _selectedKey = "himo_professional_icon_01.png";

    private static readonly ProfessionalIconOption[] Options =
    {
        new("himo_professional_icon_01.png", "ذهبي لامع"),
        new("himo_professional_icon_02.png", "أزرق كهربائي"),
        new("himo_professional_icon_03.png", "ذهبي كلاسيكي"),
        new("himo_professional_icon_04.png", "بنفسجي كريستالي"),
        new("himo_professional_icon_05.png", "ذهب أسود"),
        new("himo_professional_icon_06.png", "رخام ذهبي"),
        new("himo_professional_icon_07.png", "ياقوت أحمر"),
        new("himo_professional_icon_08.png", "كريستال أزرق"),
        new("himo_professional_icon_09.png", "زمردي ذهبي"),
        new("himo_professional_icon_10.png", "أزرق تقني"),
        new("himo_professional_icon_11.png", "ذهبي متوهج"),
        new("himo_professional_icon_12.png", "فضي لامع"),
        new("himo_professional_icon_13.png", "أحمر داكن"),
        new("himo_professional_icon_14.png", "شروق ناري"),
        new("himo_professional_icon_15.png", "هالة بنفسجية"),
        new("himo_professional_icon_16.png", "زمرد متوهج"),
        new("himo_professional_icon_17.png", "هالة زرقاء"),
        new("himo_professional_icon_18.png", "لؤلؤي ذهبي"),
        new("himo_professional_icon_19.png", "ذهبي سداسي"),
        new("himo_professional_icon_20.png", "أزرق جليدي"),
        new("himo_professional_icon_21.png", "بنفسجي كوني"),
        new("himo_professional_icon_22.png", "شريط ذهبي"),
        new("himo_professional_icon_23.png", "تركواز مضيء"),
        new("himo_professional_icon_24.png", "ذهبي مموج"),
        new("himo_professional_icon_25.png", "عنبر ناري")
    };

    private static readonly Color ProfessionalCard = Color.FromArgb("#171827");
    private static readonly Color ProfessionalSelected = Color.FromArgb("#29213A");
    private static readonly Color ProfessionalStroke = Color.FromArgb("#51415F");
    private static readonly Color ProfessionalGold = Color.FromArgb("#F4C66B");
    private static readonly Color ProfessionalText = Color.FromArgb("#FFF9EE");

    public ProfessionalIconsSettingsPage()
        : base("أيقونات احترافية", "اختر أيقونتك اللامعة المفضلة")
    {
        _selectedKey = HimoLauncherIconService.GetSelectedVariant();
        if (!Options.Any(option => string.Equals(option.FileName, _selectedKey, StringComparison.Ordinal)))
            _selectedKey = string.Empty;

        AddIntroCard(
            "25 تصميمًا لامعًا لهوية Himo",
            "اختر من بين تصاميم ذهبية وفضية وكريستالية وألوان مضيئة. الضغط على أيقونة يطبّقها كأيقونة Himo على الشاشة الرئيسية في Android.",
            "himo_professional_icon_01.png");

        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 12,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill
        };

        for (var column = 0; column < 3; column++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (var row = 0; row < (int)Math.Ceiling(Options.Length / 3.0); row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < Options.Length; index++)
        {
            var option = Options[index];
            var card = CreateProfessionalOptionCard(option);
            _optionCards[option.FileName] = card;
            grid.Add(card, index % 3, index / 3);
        }

        Body.Children.Add(grid);
        Body.Children.Add(_applyStatus);
    }

    private Border CreateProfessionalOptionCard(ProfessionalIconOption option)
    {
        var selected = string.Equals(option.FileName, _selectedKey, StringComparison.Ordinal);
        var preview = new Grid
        {
            HeightRequest = 94,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        preview.Add(new Image
        {
            Source = ImageSource.FromFile(option.FileName),
            WidthRequest = 84,
            HeightRequest = 84,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });

        var check = new Border
        {
            WidthRequest = 21,
            HeightRequest = 21,
            Padding = 0,
            BackgroundColor = ProfessionalGold,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(11) },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = selected,
            Content = new Label
            {
                Text = "✓",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = ProfessionalCard,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        preview.Add(check);

        var name = new Label
        {
            Text = option.Title,
            FontSize = 11.5,
            FontAttributes = FontAttributes.Bold,
            TextColor = ProfessionalText,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            MinimumHeightRequest = 34
        };

        var action = new Label
        {
            Text = selected ? "محددة ✓" : "اختيار",
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = selected ? ProfessionalGold : Color.FromArgb("#C9BED8"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            MinimumHeightRequest = 22
        };
        _optionActions[option.FileName] = action;

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(5, 8, 5, 7),
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { preview, name, action }
        };

        var card = new Border
        {
            BackgroundColor = selected ? ProfessionalSelected : ProfessionalCard,
            Stroke = new SolidColorBrush(selected ? ProfessionalGold : ProfessionalStroke),
            StrokeThickness = selected ? 2.5 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) },
            Padding = 0,
            MinimumHeightRequest = 160,
            Content = content,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectProfessionalOption(option);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void SelectProfessionalOption(ProfessionalIconOption option)
    {
        _selectedKey = option.FileName;
        Preferences.Set(PreferenceKey, _selectedKey);
        HimoLauncherIconService.TryApply(option.FileName, out var applyMessage);
        _applyStatus.Text = applyMessage;

        foreach (var candidate in Options)
        {
            var selected = string.Equals(candidate.FileName, _selectedKey, StringComparison.Ordinal);
            if (_optionCards.TryGetValue(candidate.FileName, out var card))
            {
                card.BackgroundColor = selected ? ProfessionalSelected : ProfessionalCard;
                card.Stroke = new SolidColorBrush(selected ? ProfessionalGold : ProfessionalStroke);
                card.StrokeThickness = selected ? 2.5 : 1;
                if (card.Content is VerticalStackLayout layout && layout.Children.FirstOrDefault() is Grid preview)
                {
                    foreach (var child in preview.Children)
                        if (child is Border mark)
                            mark.IsVisible = selected;
                }
            }

            if (_optionActions.TryGetValue(candidate.FileName, out var action))
            {
                action.Text = selected ? "محددة ✓" : "اختيار";
                action.TextColor = selected ? ProfessionalGold : Color.FromArgb("#C9BED8");
            }
        }
    }

    private sealed record ProfessionalIconOption(string FileName, string Title);
}

/// <summary>
/// One unified gallery of six full-app themes. Choosing a card applies its palette
/// and its matching UI icon pack immediately and persists the selection.
/// </summary>
public sealed class AppearanceSettingsPage : InterfaceStyleGalleryPage
{
    public AppearanceSettingsPage()
        : base(
            "سمات الواجهة",
            "ستة تصاميم متكاملة تغيّر ألوان الشاشات والبطاقات والرموز الداخلية.",
            "Himo.ActiveInterfaceStyle",
            "himo_pro_ui_settings_icon_palette.png",
            new[]
            {
                new InterfaceStyleOption("d_classic", "بنفسجي كلاسيكي", "واجهة فاتحة بطابع Himo الأصلي", "#F7F4FF", "#6C25D9", "#291841"),
                new InterfaceStyleOption("d_sky", "سماء زرقاء", "تصميم هادئ وواضح بدرجات زرقاء", "#EEF7FF", "#287AC4", "#163754"),
                new InterfaceStyleOption("d_mint", "زمرد منعش", "ألوان طبيعية مريحة للعين", "#ECFAF5", "#1A927A", "#17483F"),
                new InterfaceStyleOption("p_royal_gold", "ذهبي ملكي", "أسطح داكنة ولمسات ذهبية فاخرة", "#17151B", "#F4C66B", "#FFF5DC"),
                new InterfaceStyleOption("p_electric_blue", "أزرق ليلي", "أزرق كهربائي على خلفية عميقة", "#101D34", "#59C8FF", "#EAF7FF"),
                new InterfaceStyleOption("p_neon_violet", "بنفسجي نيون", "لمسات بنفسجية مضيئة بطابع عصري", "#211333", "#C76BFF", "#F7E9FF")
            })
    {
    }
}

/// <summary>Default interface style collection.</summary>
public sealed class DefaultInterfacesSettingsPage : InterfaceStyleGalleryPage
{
    public DefaultInterfacesSettingsPage()
        : base(
            "واجهات افتراضية",
            "اختر نمطًا بسيطًا وواضحًا من المجموعة الافتراضية.",
            "Himo.DefaultInterfaceStyle",
            "himo_system_icon_original.png",
            new[]
            {
                new InterfaceStyleOption("classic", "الكلاسيكية", "بنفسجي Himo الهادئ", "#F7F4FF", "#6C25D9", "#291841"),
                new InterfaceStyleOption("sky", "الأزرق الهادئ", "ألوان باردة ومريحة", "#EEF7FF", "#287AC4", "#163754"),
                new InterfaceStyleOption("light", "فاتحة ونظيفة", "مساحات فاتحة وتباين واضح", "#FFFFFF", "#6B47B5", "#282238"),
                new InterfaceStyleOption("graphite", "رمادي متوازن", "طابع محايد وبسيط", "#ECEEF3", "#525D71", "#222936"),
                new InterfaceStyleOption("mint", "أخضر ناعم", "لمسة طبيعية منعشة", "#ECFAF5", "#1A927A", "#17483F"),
                new InterfaceStyleOption("rose", "وردي هادئ", "ألوان دافئة وخفيفة", "#FFF1F6", "#D75E93", "#57283D")
            })
    {
    }
}

/// <summary>Professional interface style collection.</summary>
public sealed class ProfessionalInterfacesSettingsPage : InterfaceStyleGalleryPage
{
    public ProfessionalInterfacesSettingsPage()
        : base(
            "واجهات احترافية",
            "أنماط ذات حضور أقوى وتفاصيل لونية فاخرة.",
            "Himo.ProfessionalInterfaceStyle",
            "himo_professional_icon_01.png",
            new[]
            {
                new InterfaceStyleOption("royal_gold", "ذهبي ملكي", "ذهبي على خلفية داكنة", "#17151B", "#F4C66B", "#FFF5DC"),
                new InterfaceStyleOption("electric_blue", "أزرق كهربائي", "أزرق عميق مع لمسات سماوية", "#101D34", "#59C8FF", "#EAF7FF"),
                new InterfaceStyleOption("neon_violet", "بنفسجي مضيء", "درجات بنفسجية قوية", "#211333", "#C76BFF", "#F7E9FF"),
                new InterfaceStyleOption("emerald", "زمرد فاخر", "أخضر زمردي داكن", "#102923", "#5AE1B0", "#E5FFF5"),
                new InterfaceStyleOption("ruby", "ياقوتي", "أحمر أنيق مع تباين داكن", "#2A1420", "#FF7097", "#FFEAF0"),
                new InterfaceStyleOption("silver", "فضي معدني", "فضي بارد على فحمي", "#252A34", "#D6DEEA", "#F4F6FB"),
                new InterfaceStyleOption("aurora", "الشفق القطبي", "سماوي وتركوازي لامع", "#102B3A", "#48DFD4", "#E7FFFF"),
                new InterfaceStyleOption("rose_gold", "ذهب وردي", "لمسات نحاسية وردية", "#2C1B22", "#EBA58E", "#FFF0E9")
            })
    {
    }
}

/// <summary>
/// Shared gallery used by the default and professional interface pages.
/// Selections are persisted locally so a later appearance application step can
/// read the chosen style without changing unrelated pages prematurely.
/// </summary>
public abstract class InterfaceStyleGalleryPage : SettingsHubPageBase
{
    private readonly string _preferenceKey;
    private readonly InterfaceStyleOption[] _options;
    private readonly Dictionary<string, Border> _cards = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _actionButtons = new(StringComparer.Ordinal);
    private string _selectedKey;

    protected InterfaceStyleGalleryPage(
        string title,
        string subtitle,
        string preferenceKey,
        string iconFile,
        InterfaceStyleOption[] options)
        : base(title, subtitle)
    {
        if (options is null || options.Length == 0)
            throw new ArgumentException("At least one interface style is required.", nameof(options));

        _preferenceKey = preferenceKey;
        _options = options;
        if (string.Equals(_preferenceKey, "Himo.ActiveInterfaceStyle", StringComparison.Ordinal))
        {
            // Use the applied palette as the selection source of truth. Migrate a
            // previous style that is not in the six-theme gallery to the first theme.
            var activeStyle = HimoAppearanceManager.ActiveStyleId;
            _selectedKey = _options.Any(option => string.Equals(option.Key, activeStyle, StringComparison.Ordinal))
                ? activeStyle
                : _options[0].Key;
            Preferences.Default.Set(_preferenceKey, _selectedKey);
            if (!string.Equals(activeStyle, _selectedKey, StringComparison.Ordinal))
                HimoAppearanceManager.SetActiveStyle(_selectedKey);
        }
        else
        {
            _selectedKey = Preferences.Default.Get(_preferenceKey, _options[0].Key);
            if (!_options.Any(option => string.Equals(option.Key, _selectedKey, StringComparison.Ordinal)))
                _selectedKey = _options[0].Key;
        }

        var introDescription = string.Equals(_preferenceKey, "Himo.ActiveInterfaceStyle", StringComparison.Ordinal)
            ? "اختر سمة لتغيير ألوان الواجهة والبطاقات والرموز الداخلية فورًا. سيظل اختيارك محفوظًا بعد إغلاق التطبيق."
            : "اضغط على أحد النماذج لتحديده. يبقى اختيارك محفوظًا على هذا الجهاز.";
        AddIntroCard(title, introDescription, iconFile);

        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 12,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        for (var row = 0; row < (int)Math.Ceiling(_options.Length / 2.0); row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var index = 0; index < _options.Length; index++)
        {
            var option = _options[index];
            var card = CreateStyleCard(option);
            _cards[option.Key] = card;
            grid.Add(card, index % 2, index / 2);
        }

        Body.Children.Add(grid);
    }

    private Border CreateStyleCard(InterfaceStyleOption option)
    {
        var selected = string.Equals(option.Key, _selectedKey, StringComparison.Ordinal);
        var previewBackground = Color.FromArgb(option.PreviewBackground);
        var accent = Color.FromArgb(option.Accent);
        var foreground = Color.FromArgb(option.Foreground);

        // A small, coherent mini-screen makes each choice look like a real UI theme,
        // rather than a row of unrelated decorative glyphs.
        var statusRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            FlowDirection = FlowDirection.LeftToRight,
            ColumnSpacing = 4,
            HeightRequest = 12
        };
        statusRow.Add(new Label
        {
            Text = "9:41",
            FontSize = 7.5,
            TextColor = foreground.WithAlpha(0.72f),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Start
        }, 0, 0);
        statusRow.Add(new Label
        {
            Text = "●  ▰  ▰",
            FontSize = 6.5,
            TextColor = accent,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.End
        }, 1, 0);

        var brandDetails = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft,
            Children =
            {
                new Label
                {
                    Text = "Himo",
                    FontSize = 12.5,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = foreground,
                    HorizontalTextAlignment = TextAlignment.Start
                },
                new Label
                {
                    Text = "مساحتك اليومية",
                    FontSize = 7.5,
                    TextColor = foreground.WithAlpha(0.68f),
                    HorizontalTextAlignment = TextAlignment.Start
                }
            }
        };
        var brandMark = new Border
        {
            WidthRequest = 30,
            HeightRequest = 30,
            Padding = 0,
            BackgroundColor = accent.WithAlpha(0.16f),
            Stroke = new SolidColorBrush(accent.WithAlpha(0.46f)),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "H",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = accent,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var previewHeader = new HorizontalStackLayout
        {
            Spacing = 8,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill,
            HeightRequest = 34,
            Children = { brandMark, brandDetails }
        };

        var iconRow = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.LeftToRight,
            Children =
            {
                CreateThemeIconTile(option.Key, "himo_pro_ui_icon_home.png", accent),
                CreateThemeIconTile(option.Key, "himo_pro_ui_icon_conversations.png", accent),
                CreateThemeIconTile(option.Key, "himo_pro_ui_settings_icon_gear.png", accent)
            }
        };

        var quickActions = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 6,
            FlowDirection = FlowDirection.RightToLeft,
            HeightRequest = 25
        };
        quickActions.Add(CreateThemeMiniChip("المحادثات", accent, foreground), 0, 0);
        quickActions.Add(CreateThemeMiniChip("الإعدادات", accent, foreground), 1, 0);

        var navigationIndicator = new HorizontalStackLayout
        {
            Spacing = 5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                CreateThemeDot(accent, true),
                CreateThemeDot(foreground.WithAlpha(0.26f), false),
                CreateThemeDot(foreground.WithAlpha(0.26f), false),
                CreateThemeDot(foreground.WithAlpha(0.26f), false)
            }
        };

        var sample = new Border
        {
            BackgroundColor = previewBackground,
            Stroke = new SolidColorBrush(accent.WithAlpha(0.40f)),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(17) },
            Padding = new Thickness(10, 8),
            HeightRequest = 148,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children = { statusRow, previewHeader, iconRow, quickActions, navigationIndicator }
            }
        };

        var name = new Label
        {
            Text = option.Title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = foreground,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        // Theme preview text uses the candidate palette, not the currently active palette.

        var description = new Label
        {
            Text = option.Description,
            FontSize = 11,
            TextColor = foreground.WithAlpha(0.72f),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            MinimumHeightRequest = 31,
            MaxLines = 2
        };

        var action = new Button
        {
            AutomationId = "HimoThemeActionButton",
            Text = selected ? "السمة الحالية  ✓" : "تطبيق السمة",
            FontSize = 12.5,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            MinimumHeightRequest = 44,
            Padding = new Thickness(10, 0),
            CornerRadius = 14,
            BackgroundColor = selected ? accent : accent.WithAlpha(0.12f),
            TextColor = selected ? ContrastingText(accent) : accent,
            BorderColor = selected ? accent : accent.WithAlpha(0.55f),
            BorderWidth = selected ? 0 : 1,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        if (selected)
        {
            action.Shadow = new Shadow
            {
                Brush = new SolidColorBrush(accent),
                Offset = new Point(0, 2),
                Radius = 8,
                Opacity = 0.20f
            };
        }
        action.Clicked += (_, _) => SelectStyle(option);
        _actionButtons[option.Key] = action;

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(10),
            Spacing = 9,
            Children = { sample, name, description, action }
        };
        var card = new Border
        {
            // Keep candidate-specific preview colors intact when the selected app
            // palette is broadcast through the visual tree.
            AutomationId = "HimoThemePreview",
            BackgroundColor = selected ? Color.FromArgb("#F0E8FF") : Colors.White,
            Stroke = new SolidColorBrush(selected ? accent : Color.FromArgb("#E3D9F2")),
            StrokeThickness = selected ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(22) },
            Padding = 0,
            Content = content,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        // Do not bind these preview colors to HimoAccent/HimoCard: each card is a
        // live miniature of its own palette and must remain distinct after selection.

        // The whole card is tappable as well as the explicitly labelled button.
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectStyle(option);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private static Border CreateThemeIconTile(string styleId, string baseFileName, Color accent)
    {
        return new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            Padding = 6,
            BackgroundColor = accent.WithAlpha(0.12f),
            Stroke = new SolidColorBrush(accent.WithAlpha(0.28f)),
            StrokeThickness = 0.7,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(11) },
            Content = new Image
            {
                Source = ImageSource.FromFile($"himo_theme_{styleId}_{baseFileName}"),
                WidthRequest = 22,
                HeightRequest = 22,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
    }

    private static Border CreateThemeMiniChip(string text, Color accent, Color foreground)
    {
        return new Border
        {
            BackgroundColor = accent.WithAlpha(0.09f),
            Stroke = new SolidColorBrush(accent.WithAlpha(0.22f)),
            StrokeThickness = 0.7,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(8) },
            Padding = new Thickness(4, 3),
            Content = new Label
            {
                Text = text,
                FontSize = 7.5,
                TextColor = foreground,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }
        };
    }

    private static Border CreateThemeDot(Color color, bool active)
    {
        return new Border
        {
            WidthRequest = active ? 13 : 5,
            HeightRequest = 5,
            Padding = 0,
            BackgroundColor = color,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(3) }
        };
    }

    private static Color ContrastingText(Color background)
    {
        var luminance = 0.2126 * background.Red + 0.7152 * background.Green + 0.0722 * background.Blue;
        return luminance > 0.52 ? Color.FromArgb("#21172B") : Colors.White;
    }

    private void SelectStyle(InterfaceStyleOption option)
    {
        var changed = !string.Equals(option.Key, _selectedKey, StringComparison.Ordinal);
        _selectedKey = option.Key;
        Preferences.Default.Set(_preferenceKey, _selectedKey);

        // The unified gallery stores the complete style ID. Older hidden galleries
        // keep their prefix behavior for backwards compatibility.
        var styleId = string.Equals(_preferenceKey, "Himo.ActiveInterfaceStyle", StringComparison.Ordinal)
            ? option.Key
            : (string.Equals(_preferenceKey, "Himo.DefaultInterfaceStyle", StringComparison.Ordinal) ? "d_" : "p_") + option.Key;

        if (changed)
        {
            HimoAppearanceManager.SetActiveStyle(styleId);
            // Refresh only this gallery now. Pages underneath refresh on appearing.
            HimoAppearanceManager.ApplyToTree(this);
        }

        foreach (var candidate in _options)
        {
            var selected = string.Equals(candidate.Key, _selectedKey, StringComparison.Ordinal);
            var accent = Color.FromArgb(candidate.Accent);
            if (_cards.TryGetValue(candidate.Key, out var card))
            {
                card.BackgroundColor = selected ? Color.FromArgb("#F0E8FF") : Colors.White;
                card.Stroke = new SolidColorBrush(selected ? accent : Color.FromArgb("#E3D9F2"));
                card.StrokeThickness = selected ? 2 : 1;
            }

            if (_actionButtons.TryGetValue(candidate.Key, out var action))
            {
                action.Text = selected ? "السمة الحالية  ✓" : "تطبيق السمة";
                action.BackgroundColor = selected ? accent : accent.WithAlpha(0.12f);
                action.TextColor = selected ? ContrastingText(accent) : accent;
                action.BorderColor = selected ? accent : accent.WithAlpha(0.55f);
                action.BorderWidth = selected ? 0 : 1;
                action.Shadow = selected
                    ? new Shadow { Brush = new SolidColorBrush(accent), Offset = new Point(0, 2), Radius = 8, Opacity = 0.20f }
                    : null;
            }
        }
    }

}

public sealed record InterfaceStyleOption(
    string Key,
    string Title,
    string Description,
    string PreviewBackground,
    string Accent,
    string Foreground);

/// <summary>Shared presentation and navigation for this settings subsection.</summary>
public abstract class SettingsHubPageBase : ContentPage
{
    private static readonly Color PageBackground = Color.FromArgb("#F7F4FF");
    private static readonly Color PrimaryPurple = Color.FromArgb("#6C25D9");
    private static readonly Color HeadingColor = Color.FromArgb("#291841");
    private static readonly Color SecondaryText = Color.FromArgb("#817494");
    private static readonly Color CardStroke = Color.FromArgb("#E3D9F2");
    private static readonly Color IconBackground = Color.FromArgb("#F1E7FF");

    protected readonly VerticalStackLayout Body;
    private int _navigationInProgress;

    protected SettingsHubPageBase(string title, string subtitle)
    {
        Title = title;
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageBackground;
        SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        Shell.SetNavBarIsVisible(this, false);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        var header = new Grid
        {
            Padding = new Thickness(18, 14, 18, 10),
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.LeftToRight
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

        var backButton = new ImageButton
        {
            Source = ImageSource.FromFile("himo_pro_ui_himo_icon_arrow_back_purple.png"),
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 12,
            CornerRadius = 14,
            BackgroundColor = Color.FromArgb("#F0E8FF"),
            BorderColor = Color.FromArgb("#D6C6EE"),
            BorderWidth = 1,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        backButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        backButton.SetDynamicResource(ImageButton.BorderColorProperty, "HimoBorder");
        backButton.Clicked += BackButtonClicked;
        header.Add(backButton, 0, 0);

        var heading = new VerticalStackLayout
        {
            Spacing = 2,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };
        var headingTitle = new Label
        {
            Text = title,
            FontSize = 23,
            FontAttributes = FontAttributes.Bold,
            TextColor = HeadingColor,
            HorizontalTextAlignment = TextAlignment.Start
        };
        headingTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        heading.Children.Add(headingTitle);
        var headingSubtitle = new Label
        {
            Text = subtitle,
            FontSize = 12.5,
            TextColor = SecondaryText,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap
        };
        headingSubtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        heading.Children.Add(headingSubtitle);
        header.Add(heading, 1, 0);
        header.Add(new BoxView { Opacity = 0, WidthRequest = 44, HeightRequest = 44 }, 2, 0);
        root.Add(header, 0, 0);

        Body = new VerticalStackLayout
        {
            Padding = new Thickness(16, 12, 16, 24),
            Spacing = 14,
            FlowDirection = FlowDirection.RightToLeft
        };
        var scroll = new ScrollView
        {
            Content = Body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
        root.Add(scroll, 0, 1);
        Content = root;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this);
    }

    protected void AddIntroCard(string title, string description, string iconFile)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 10,
            Padding = new Thickness(18, 18),
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill
        };
        var iconTile = new Border
        {
            WidthRequest = 54,
            HeightRequest = 54,
            BackgroundColor = IconBackground,
            Stroke = new SolidColorBrush(CardStroke),
            StrokeThickness = 0.8,
            HorizontalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = ImageSource.FromFile(iconFile),
                WidthRequest = 27,
                HeightRequest = 27,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        iconTile.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        iconTile.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        content.Children.Add(iconTile);
        var introTitle = new Label
        {
            Text = title,
            FontSize = 16.5,
            FontAttributes = FontAttributes.Bold,
            TextColor = HeadingColor,
            HorizontalTextAlignment = TextAlignment.Center
        };
        introTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var introDescription = new Label
        {
            Text = description,
            FontSize = 12.5,
            TextColor = SecondaryText,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        introDescription.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        content.Children.Add(introTitle);
        content.Children.Add(introDescription);
        var introBorder = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = new SolidColorBrush(CardStroke),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) },
            Content = content
        };
        introBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        introBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        Body.Children.Add(introBorder);
    }

    protected void AddNavigationCard(string title, string description, string iconFile, Func<Task> navigate)
    {
        var row = new Grid
        {
            Padding = new Thickness(12, 13),
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 82
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

        var arrow = new Image
        {
            Source = ImageSource.FromFile("himo_pro_ui_himo_icon_arrow_back_purple.png"),
            WidthRequest = 14,
            HeightRequest = 18,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Opacity = 0.9
        };
        row.Add(arrow, 0, 0);

        var labels = new VerticalStackLayout
        {
            Spacing = 4,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        };
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = HeadingColor,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var descriptionLabel = new Label
        {
            Text = description,
            FontSize = 12,
            TextColor = SecondaryText,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 2
        };
        descriptionLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Children.Add(titleLabel);
        labels.Children.Add(descriptionLabel);
        row.Add(labels, 1, 0);

        var iconImage = new Image
        {
            Source = ImageSource.FromFile(iconFile),
            WidthRequest = 23,
            HeightRequest = 23,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        var icon = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            BackgroundColor = IconBackground,
            Stroke = new SolidColorBrush(CardStroke),
            StrokeThickness = 0.8,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(14) },
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Content = iconImage
        };
        icon.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        icon.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        row.Add(icon, 2, 0);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = new SolidColorBrush(CardStroke),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(18) },
            Padding = 0,
            Content = row
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (Interlocked.Exchange(ref _navigationInProgress, 1) != 0)
                return;
            try
            {
                await navigate();
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync(title, "تعذر فتح الصفحة: " + ex.Message, "حسنًا");
            }
            finally
            {
                Interlocked.Exchange(ref _navigationInProgress, 0);
            }
        };
        card.GestureRecognizers.Add(tap);
        Body.Children.Add(card);
    }

    private async void BackButtonClicked(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _navigationInProgress, 1) != 0)
            return;

        try
        {
            // Reserve this page's action lock while the shared shell coordinator
            // ensures that no other settings route transition overlaps it.
            var shell = Shell.Current;
            // Theme/settings pages are routed through Shell consistently. Do not
            // guess the active leaf page: Shell.CurrentPage can lag while transitions
            // are settling. Mixing that guess with Navigation.PopAsync created two
            // different stacks and caused later opens to jump back to HomePage.
            if (shell is not null)
            {
                await HimoShellNavigator.BackAsync(false);
                return;
            }

            if (Navigation?.NavigationStack is { Count: > 1 } stack && ReferenceEquals(stack[^1], this))
            {
                await Navigation.PopAsync(false);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Himo Navigation] No local back stack for {GetType().Name}; Shell page is {shell?.CurrentPage?.GetType().Name ?? "(none)"}.");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("رجوع", "تعذر الرجوع للصفحة السابقة: " + ex.Message, "حسنًا");
        }
        finally
        {
            Interlocked.Exchange(ref _navigationInProgress, 0);
        }
    }
}

