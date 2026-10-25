using CommunityToolkit.Maui.Views;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Pages for chat themes, outgoing/incoming bubble colors and chat wallpaper selection.
/// All selections are persisted in Preferences and consumed by ChatPage.
/// </summary>
public sealed class ChatThemePage : Himo.Services.LocalizedContentPage
{
    private static readonly Color PageBackground = Color.FromArgb("#211333");
    private static readonly Color CardBackground = Color.FromArgb("#2D1942");
    private static readonly Color CardBorder = Color.FromArgb("#594768");
    private static readonly Color Muted = Color.FromArgb("#C7B8D8");

    public const string ThemeBackgroundKey = "himo_chat_theme_background_asset";
    public const string BackgroundModeKey = "himo_chat_background_mode";

    // These are abstract color designs, not photographic wallpapers. Keep their
    // assets and preference separate from ChatWallpaperSettingsPage.GlobalWallpaperKey.
    private static readonly ChatThemeOption[] Themes =
    {
        new("تموجات بنفسجية", "شرائط لونية بنفسجية متدرجة", "himo_theme_purple_flow.png", "#D34DFF", "#6814C7", "#53306E", "#241236"),
        new("أمواج زرقاء", "تموجات زرقاء وسماوية لامعة", "himo_theme_blue_ocean.png", "#57C8FF", "#3158E8", "#236A8A", "#142A4F"),
        new("انسياب زمردي", "ألوان خضراء تركوازية متداخلة", "himo_theme_emerald_flow.png", "#32D9A0", "#078A72", "#326E68", "#163C41"),
        new("زجاج وردي", "دوائر ضوئية وتموجات وردية", "himo_theme_rose_glass.png", "#FF78B9", "#D52B92", "#8A577B", "#452A55"),
        new("حرير الغروب", "شرائط ناعمة بألوان الغروب", "himo_theme_sunset_silk.png", "#FFC46D", "#F07545", "#8A5B6D", "#352B4A"),
        new("مدارات نيون", "حلقات وخطوط نيون زرقاء وبنفسجية", "himo_theme_neon_orbit.png", "#AC8BFF", "#6137D6", "#345A83", "#17233D"),
        new("وهج ذهبي", "لمسات ذهبية ودافئة بانسياب ناعم", "himo_theme_gold_glow.png", "#FFD36E", "#E88936", "#8A5B6D", "#352B4A"),
        new("الشفق القطبي", "تموجات مضيئة بالأخضر والأزرق", "himo_theme_aurora.png", "#49F0E7", "#148FA9", "#386C91", "#1C3659")
    };

    public ChatThemePage()
    {
        Title = "سمة الدردشة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageBackground;
        ChatAppearancePageStyle.ApplyPage(this);

        var layout = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = 14 };
        layout.Add(ChatAppearancePageStyle.TitleRow("himo_pro_ui_settings_icon_palette.png", "اختر سمة لونية للدردشة", 20));
        var intro = new Label
        {
            Text = "اختر تصميمًا لونيًا لمحادثاتك. السمات اللونية منفصلة عن الخلفيات الفوتوغرافية، لذلك يمكنك تغيير إحداها دون فقد اختيارك من الأخرى.",
            FontSize = 12,
            LineHeight = 1.35,
            Margin = new Thickness(2, 0, 2, 5)
        };
        ChatAppearancePageStyle.ApplyMutedText(intro);
        layout.Add(intro);

        var themeButtons = new List<(ChatThemeOption Option, Button Button)>();
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        for (var i = 0; i < (Themes.Length + 1) / 2; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < Themes.Length; i++)
        {
            var option = Themes[i];
            var image = new Image { HeightRequest = 142, Aspect = Aspect.AspectFill, Opacity = 1.0 };
            _ = LoadPreviewAsync(image, option.BackgroundAsset);

            var title = new Label { Text = option.Name, FontSize = 14, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
            ChatAppearancePageStyle.ApplyPrimaryText(title);
            var description = new Label { Text = option.Description, FontSize = 10.5, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2 };
            ChatAppearancePageStyle.ApplyMutedText(description);
            var swatches = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 5, HeightRequest = 9 };
            swatches.Add(new BoxView { Color = Color.FromArgb(option.OutStart), CornerRadius = 5 }, 0, 0);
            swatches.Add(new BoxView { Color = Color.FromArgb(option.InStart), CornerRadius = 5 }, 1, 0);

            var themeIsActive = string.Equals(Preferences.Default.Get(BackgroundModeKey, "photo"), "theme", StringComparison.OrdinalIgnoreCase)
                && Preferences.Default.Get("himo_chat_theme_name", string.Empty) == option.Name;
            var apply = new Button
            {
                Text = themeIsActive ? "السمة الحالية ✓" : "تطبيق السمة",
                FontSize = 11,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#7D24EC"),
                BorderColor = Color.FromArgb("#D9C5F4"),
                BorderWidth = 1,
                CornerRadius = 12,
                Padding = new Thickness(8, 8)
            };
            ChatAppearancePageStyle.ApplyPrimaryButton(apply);
            apply.Clicked += async (_, _) =>
            {
                await ApplyThemeAsync(option);
                foreach (var item in themeButtons)
                    item.Button.Text = item.Option.Name == option.Name ? "السمة الحالية ✓" : "تطبيق السمة";
            };
            themeButtons.Add((option, apply));

            var details = new VerticalStackLayout { Padding = new Thickness(11, 10), Spacing = 7 };
            details.Add(title);
            details.Add(description);
            details.Add(swatches);
            details.Add(apply);
            var body = new VerticalStackLayout { Spacing = 0 };
            body.Add(image);
            body.Add(details);
            var card = new Border
            {
                BackgroundColor = CardBackground,
                Stroke = CardBorder,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
                Padding = 0,
                Content = body
            };
            ChatAppearancePageStyle.ApplyCard(card);
            grid.Add(card, i % 2, i / 2);
        }

        layout.Add(grid);
        Content = ChatAppearancePageStyle.CreateScroll(layout);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
    }

    private async Task ApplyThemeAsync(ChatThemeOption theme)
    {
        Preferences.Default.Set("himo_chat_theme_name", theme.Name);
        Preferences.Default.Set(ThemeBackgroundKey, theme.BackgroundAsset);
        Preferences.Default.Set(BackgroundModeKey, "theme");
        // Deliberately do not touch GlobalWallpaperKey: photographic wallpapers
        // are a separate preference and must remain selected in their own page.
        Preferences.Default.Set(ChatBubbleSettingsPage.OutgoingStartKey, theme.OutStart);
        Preferences.Default.Set(ChatBubbleSettingsPage.OutgoingEndKey, theme.OutEnd);
        Preferences.Default.Set(ChatBubbleSettingsPage.IncomingStartKey, theme.InStart);
        Preferences.Default.Set(ChatBubbleSettingsPage.IncomingEndKey, theme.InEnd);
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("سمة الدردشة"), $"تم تطبيق سمة {theme.Name} كخلفية لونية مستقلة. لم يتغير اختيار الخلفية الفوتوغرافية المحفوظ.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    internal static async Task LoadPreviewAsync(Image image, string assetName)
    {
        try
        {
            if (Uri.TryCreate(assetName, UriKind.Absolute, out var previewUri)
                && (previewUri.Scheme == Uri.UriSchemeHttps || previewUri.Scheme == Uri.UriSchemeHttp))
            {
                MainThread.BeginInvokeOnMainThread(() => image.Source = ImageSource.FromUri(previewUri));
                return;
            }

            await using var source = await FileSystem.Current.OpenAppPackageFileAsync(assetName);
            using var memory = new MemoryStream();
            await source.CopyToAsync(memory);
            var bytes = memory.ToArray();
            MainThread.BeginInvokeOnMainThread(() => image.Source = ImageSource.FromStream(() => new MemoryStream(bytes, writable: false)));
        }
        catch
        {
            MainThread.BeginInvokeOnMainThread(() => image.BackgroundColor = Color.FromArgb("#39204F"));
        }
    }

    private sealed record ChatThemeOption(string Name, string Description, string BackgroundAsset, string OutStart, string OutEnd, string InStart, string InEnd);
}

public sealed class ChatBubbleSettingsPage : Himo.Services.LocalizedContentPage
{
    public const string OutgoingStartKey = "himo_chat_bubble_out_start";
    public const string OutgoingEndKey = "himo_chat_bubble_out_end";
    public const string IncomingStartKey = "himo_chat_bubble_in_start";
    public const string IncomingEndKey = "himo_chat_bubble_in_end";

    private static readonly Color PageBackground = Color.FromArgb("#211333");
    private static readonly Color CardBackground = Color.FromArgb("#2D1942");
    private static readonly Color CardBorder = Color.FromArgb("#594768");
    private static readonly Color Muted = Color.FromArgb("#C7B8D8");

    private static readonly BubblePalette[] Palette =
    {
        new("بنفسجي لامع", "#D34DFF", "#6814C7"),
        new("وردي متوهج", "#FF78B9", "#D52B92"),
        new("أزرق كهربائي", "#57C8FF", "#3158E8"),
        new("زمردي", "#42E6B0", "#09866F"),
        new("ذهبي", "#FFD36E", "#E88936"),
        new("أحمر ياقوتي", "#FF6C8A", "#C9285E"),
        new("تركوازي", "#49F0E7", "#148FA9"),
        new("أرجواني ملكي", "#AC8BFF", "#6137D6")
    };

    public ChatBubbleSettingsPage()
    {
        Title = "فقاعة الدردشة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageBackground;
        ChatAppearancePageStyle.ApplyPage(this);

        var layout = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = 14 };
        layout.Add(ChatAppearancePageStyle.TitleRow("himo_pro_ui_settings_icon_palette.png", "ألوان فقاعات الرسائل", 19));
        var intro = new Label { Text = "خصص لون الرسائل المرسلة والمستقبلة بشكل مستقل. ستظل اختياراتك محفوظة عند إعادة فتح التطبيق.", FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
        ChatAppearancePageStyle.ApplyMutedText(intro);
        layout.Add(intro);
        layout.Add(BuildPaletteSection("الرسائل المرسلة", OutgoingStartKey, OutgoingEndKey));
        layout.Add(BuildPaletteSection("الرسائل المستقبلة", IncomingStartKey, IncomingEndKey));
        var note = new Label { Text = "تُحفظ اختياراتك تلقائيًا وتُطبق عند فتح المحادثة.", FontSize = 11, Margin = new Thickness(4, 0) };
        ChatAppearancePageStyle.ApplyMutedText(note);
        layout.Add(note);
        Content = ChatAppearancePageStyle.CreateScroll(layout);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
    }

    private static View BuildPaletteSection(string title, string startKey, string endKey)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        var sectionTitle = new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold };
        ChatAppearancePageStyle.ApplyPrimaryText(sectionTitle);
        stack.Add(sectionTitle);
        var swatches = new Grid { ColumnSpacing = 11, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        for (var i = 0; i < (Palette.Length + 3) / 4; i++) swatches.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < Palette.Length; i++)
        {
            var option = Palette[i];
            var selected = string.Equals(Preferences.Default.Get(startKey, DefaultStart(startKey)), option.Start, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Preferences.Default.Get(endKey, DefaultEnd(endKey)), option.End, StringComparison.OrdinalIgnoreCase);
            var preview = new Border
            {
                WidthRequest = 49,
                HeightRequest = 49,
                HorizontalOptions = LayoutOptions.Center,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(option.Start), 0f),
                        new GradientStop(Color.FromArgb(option.End), 1f)
                    }
                },
                Stroke = selected ? Color.FromArgb("#7D24EC") : Color.FromArgb("#D9C5F4"),
                StrokeThickness = selected ? 2 : 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }
            };
            preview.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
            var label = new Label { Text = option.Name, FontSize = 9.5, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
            ChatAppearancePageStyle.ApplyMutedText(label);
            var item = new VerticalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.Center };
            item.Add(preview);
            item.Add(label);
            var gesture = new TapGestureRecognizer();
            gesture.Tapped += (_, _) =>
            {
                Preferences.Default.Set(startKey, option.Start);
                Preferences.Default.Set(endKey, option.End);
                preview.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
                preview.StrokeThickness = 2;
                // Reset the other swatches to neutral outlines; selected color remains obvious.
                foreach (var child in swatches.Children)
                {
                    if (child is VerticalStackLayout other && other.Children.FirstOrDefault() is Border otherBorder && !ReferenceEquals(otherBorder, preview))
                    {
                        otherBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
                        otherBorder.StrokeThickness = 1;
                    }
                }
            };
            item.GestureRecognizers.Add(gesture);
            swatches.Add(item, i % 4, i / 4);
        }

        var paletteCard = new Border
        {
            BackgroundColor = CardBackground,
            Stroke = CardBorder,
            StrokeThickness = 1,
            Padding = 13,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Content = swatches
        };
        ChatAppearancePageStyle.ApplyCard(paletteCard);
        stack.Add(paletteCard);
        return stack;
    }

    private static string DefaultStart(string key) => key == OutgoingStartKey ? "#D34DFF" : "#2F1946";
    private static string DefaultEnd(string key) => key == OutgoingEndKey ? "#6814C7" : "#1A0D29";

    private sealed record BubblePalette(string Name, string Start, string End);
}

public sealed class ChatWallpaperSettingsPage : Himo.Services.LocalizedContentPage
{
    public const string GlobalWallpaperKey = "himo_chat_wallpaper_global";

    private static readonly Color PageBackground = Color.FromArgb("#211333");
    private static readonly Color CardBackground = Color.FromArgb("#2D1942");
    private static readonly Color CardBorder = Color.FromArgb("#594768");
    private static readonly Color Muted = Color.FromArgb("#C7B8D8");

    private static readonly WallpaperOption[] Wallpapers =
    {
        new("أزهار بنفسجية", "himo_wallpaper_purple_flowers.jpg", "زهور مضيئة بدرجات البنفسجي"),
        new("أزهار قريبة", "himo_wallpaper_close_flowers.jpg", "تفاصيل زهرية وألوان ناعمة"),
        new("مدينة مضيئة", "himo_wallpaper_city_lights.jpg", "مبانٍ حديثة وأضواء ليلية"),
        new("مدينة ساكورا", "himo_wallpaper_sakura_city.jpg", "مبانٍ وأزهار وردية"),
        new("غروب الجبال", "himo_wallpaper_mountain_sunset.jpg", "قمم جبلية بألوان الغروب"),
        new("بحيرة القمر", "himo_wallpaper_moon_lake.jpg", "منظر ليلي وانعكاس القمر"),
        new("بحيرة هادئة", "himo_wallpaper_quiet_lake.jpg", "ماء وجبال وطبيعة هادئة"),
        new("غابة زمردية", "himo_wallpaper_emerald_forest.jpg", "غابة عميقة بوهج أخضر"),
        new("بحر هادئ", "himo_wallpaper_purple_water.jpg", "تموجات بحرية بدرجات ليلية"),
        new("غروب ذهبي", "himo_wallpaper_golden_desert.jpg", "كثبان ذهبية وسماء دافئة")
    };

    private readonly Label _status = new() { TextColor = Muted, FontSize = 11, LineBreakMode = LineBreakMode.WordWrap };
    private readonly List<(string FileName, Button Button, Border Card)> _wallpaperItems = new();

    public ChatWallpaperSettingsPage()
    {
        Title = "خلفية الدردشة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageBackground;
        ChatAppearancePageStyle.ApplyPage(this);
        ChatAppearancePageStyle.ApplyMutedText(_status);

        var layout = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = 14 };
        layout.Add(ChatAppearancePageStyle.TitleRow("himo_pro_ui_settings_icon_media.png", "خلفية الدردشة", 19));
        var intro = new Label { Text = "اختر صورة جاهزة أو صورة من جهازك. تُحفظ الخلفية الفوتوغرافية بصورة مستقلة عن السمات اللونية.", FontSize = 12, LineHeight = 1.3 };
        ChatAppearancePageStyle.ApplyMutedText(intro);
        layout.Add(intro);

        var galleryButton = new Button
        {
            Text = "＋  اختيار صورة من المعرض",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#7D24EC"),
            BorderColor = Color.FromArgb("#CDA5FF"),
            BorderWidth = 1,
            CornerRadius = 15,
            HeightRequest = 50
        };
        ChatAppearancePageStyle.ApplyPrimaryButton(galleryButton);
        galleryButton.ImageSource = "himo_pro_ui_icon_plus.png";
        galleryButton.Clicked += async (_, _) => await PickImageFromGalleryAsync();
        layout.Add(galleryButton);
        layout.Add(_status);

        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        for (var i = 0; i < (Wallpapers.Length + 1) / 2; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < Wallpapers.Length; i++)
        {
            var option = Wallpapers[i];
            var image = new Image { HeightRequest = 178, Aspect = Aspect.AspectFill };
            _ = ChatThemePage.LoadPreviewAsync(image, option.FileName);
            var title = new Label { Text = option.Name, FontSize = 13, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
            ChatAppearancePageStyle.ApplyPrimaryText(title);
            var caption = new Label { Text = option.Description, FontSize = 10, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 };
            ChatAppearancePageStyle.ApplyMutedText(caption);
            var current = string.Equals(Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo"), "photo", StringComparison.OrdinalIgnoreCase)
                && Preferences.Default.Get(GlobalWallpaperKey, "himo_wallpaper_purple_flowers.jpg") == option.FileName;
            var select = new Button
            {
                Text = current ? "الخلفية الحالية ✓" : "استخدام الخلفية",
                FontSize = 10.5,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#7D24EC"),
                BorderColor = Color.FromArgb("#D9C5F4"),
                BorderWidth = 1,
                CornerRadius = 11,
                Padding = new Thickness(6, 7)
            };
            ChatAppearancePageStyle.ApplyPrimaryButton(select);
            select.Clicked += async (_, _) => await ApplyWallpaperAsync(option.FileName, option.Name);
            var details = new VerticalStackLayout { Padding = new Thickness(10, 9), Spacing = 6 };
            details.Add(title);
            details.Add(caption);
            details.Add(select);
            var content = new VerticalStackLayout { Spacing = 0 };
            content.Add(image);
            content.Add(details);
            var border = new Border
            {
                BackgroundColor = CardBackground,
                Stroke = CardBorder,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 17 },
                Padding = 0,
                Content = content
            };
            ChatAppearancePageStyle.ApplyCard(border);
            if (current) border.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
            _wallpaperItems.Add((option.FileName, select, border));
            grid.Add(border, i % 2, i / 2);
        }

        layout.Add(grid);
        Content = ChatAppearancePageStyle.CreateScroll(layout);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
    }

    private async Task ApplyWallpaperAsync(string wallpaperValue, string displayName)
    {
        Preferences.Default.Set(GlobalWallpaperKey, wallpaperValue);
        Preferences.Default.Set(ChatThemePage.BackgroundModeKey, "photo");
        RefreshWallpaperSelection(wallpaperValue);
        _status.Text = $"تم اختيار خلفية {displayName}. ستظهر في المحادثات التي لا تملك خلفية خاصة بها عند فتحها.";
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفية الدردشة"), "تم حفظ الخلفية الجديدة.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    private async Task PickImageFromGalleryAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "اختر صورة لخلفية الدردشة",
                FileTypes = FilePickerFileType.Images
            });
            if (picked is null) return;

            var extension = Path.GetExtension(picked.FileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || !extension.StartsWith('.')) extension = ".img";
            var destination = Path.Combine(FileSystem.Current.AppDataDirectory, $"himo_chat_wallpaper_{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
            await using (var input = await picked.OpenReadAsync())
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output);

            Preferences.Default.Set(GlobalWallpaperKey, "local:" + destination);
            Preferences.Default.Set(ChatThemePage.BackgroundModeKey, "photo");
            RefreshWallpaperSelection("local:" + destination);
            _status.Text = "تم حفظ الصورة المختارة وستُستخدم كخلفية للمحادثات.";
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفية الدردشة"), "تم اختيار الصورة وحفظها بنجاح.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر اختيار الصورة: " + ex.Message;
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفية الدردشة"), _status.Text, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void RefreshWallpaperSelection(string selectedFileName)
    {
        foreach (var item in _wallpaperItems)
        {
            var selected = string.Equals(item.FileName, selectedFileName, StringComparison.OrdinalIgnoreCase);
            item.Button.Text = selected ? "الخلفية الحالية ✓" : "استخدام الخلفية";
            item.Card.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
            item.Card.StrokeThickness = selected ? 2 : 1;
        }
    }

    private sealed record WallpaperOption(string Name, string FileName, string Description);
}


/// <summary>
/// Selects a global looping, muted video wallpaper. The setting is independent
/// from the saved photo and abstract theme, so choosing one does not erase the others.
/// </summary>
public sealed class ChatAnimatedWallpaperPage : Himo.Services.LocalizedContentPage
{
    public const string GlobalAnimatedWallpaperKey = "himo_chat_animated_wallpaper_global";
    public const string PreviousBackgroundModeKey = "himo_chat_background_mode_before_animated";

    private static readonly Color PageBackground = Color.FromArgb("#211333");
    private static readonly Color CardBackground = Color.FromArgb("#2D1942");
    private static readonly Color CardBorder = Color.FromArgb("#594768");
    private static readonly Color Muted = Color.FromArgb("#C7B8D8");
    private static readonly HttpClient VideoHttpClient = new() { Timeout = TimeSpan.FromMinutes(2) };

    // These are real live-action MP4 files hosted by Pexels, not animations generated
    // from still images. Streaming avoids packaging fake/derived clips in the APK.
    // The Pexels pages and authors are documented in README_AR_Real_Video_Wallpapers.txt.
    private static readonly AnimatedWallpaperOption[] Options =
    {
        new("بحيرة وتموّجات الماء", "لقطات حقيقية لتموّجات الماء وانعكاسات الضوء", "https://videos.pexels.com/video-files/5138001/5138001-hd_1280_720_30fps.mp4", "https://images.pexels.com/videos/5138001/abstract-abstract-background-background-lake-5138001.jpeg?auto=compress&dpr=1&h=750&w=1260"),
        new("مدينة ليلية", "فيديو حقيقي لحركة المرور وأضواء المدينة ليلًا", "https://videos.pexels.com/video-files/11372920/11372920-hd_1080_1920_60fps.mp4", "https://images.pexels.com/videos/11372920/pexels-photo-11372920.jpeg?auto=compress&dpr=1&h=750&w=1260"),
        new("حقول الزهور", "زهور حقيقية تتمايل مع الرياح", "https://videos.pexels.com/video-files/8559724/8559724-hd_1080_1920_30fps.mp4", "https://images.pexels.com/videos/8559724/agriculture-bright-country-countryside-8559724.jpeg?auto=compress&dpr=1&h=750&w=1260"),
        new("شلال الغابة", "مياه شلال حقيقية تتدفق بين الصخور", "https://videos.pexels.com/video-files/8859850/8859850-uhd_2160_3840_25fps.mp4", "https://images.pexels.com/videos/8859850/acrobatics-adventure-cliff-climb-8859850.jpeg?auto=compress&dpr=1&h=750&w=1260"),
        new("غروب البحر", "مقطع حقيقي لأمواج البحر عند الغروب", "https://videos.pexels.com/video-files/7975481/7975481-hd_1080_1920_30fps.mp4", "https://images.pexels.com/videos/7975481/pexels-photo-7975481.jpeg?auto=compress&dpr=1&h=750&w=1260")
    };

    private readonly Label _status = new()
    {
        TextColor = Muted,
        FontSize = 12,
        LineBreakMode = LineBreakMode.WordWrap
    };
    private readonly List<(string VideoUrl, Button Button, Border Card)> _animatedItems = new();
    private readonly ContentView _inlinePreviewHost = new()
    {
        IsVisible = false,
        HeightRequest = 0,
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Start
    };
    private MediaElement? _inlinePreviewPlayer;

    public ChatAnimatedWallpaperPage()
    {
        Title = "خلفيات متحركة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageBackground;
        ChatAppearancePageStyle.ApplyPage(this);
        ChatAppearancePageStyle.ApplyMutedText(_status);

        var content = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = 14 };
        content.Add(ChatAppearancePageStyle.TitleRow("himo_pro_ui_settings_icon_media.png", "خلفيات متحركة للدردشة", 20));
        var intro = new Label
        {
            Text = "اختر مقطع فيديو حقيقيًا كخلفية صامتة للمحادثات. تحتاج المقاطع المدمجة إلى الإنترنت، ويتوقف تشغيلها عند مغادرة المحادثة لتقليل استهلاك البطارية.",
            FontSize = 12,
            LineHeight = 1.3
        };
        ChatAppearancePageStyle.ApplyMutedText(intro);
        content.Add(intro);

        var previewHint = new Label
        {
            Text = "لمعاينة أي فيديو، اضغط على صورته؛ ستعمل المعاينة داخل Himo دون فتح مشغل خارجي.",
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap
        };
        ChatAppearancePageStyle.ApplyMutedText(previewHint);
        content.Add(previewHint);
        content.Add(_inlinePreviewHost);

        var current = Preferences.Default.Get(GlobalAnimatedWallpaperKey, string.Empty);
        if (string.Equals(Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo"), "animated", StringComparison.OrdinalIgnoreCase))
        {
            var currentOption = Options.FirstOrDefault(x => string.Equals(current, "url:" + x.VideoUrl, StringComparison.OrdinalIgnoreCase));
            _status.Text = currentOption is null ? "الخلفية المتحركة من جهازك مفعّلة حاليًا." : $"الخلفية الحالية: {currentOption.Name}";
        }
        else
        {
            _status.Text = "لم يتم تفعيل خلفية متحركة بعد.";
        }

        var grid = new Grid
        {
            ColumnSpacing = 10,
            RowSpacing = 12,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }
        };
        for (var i = 0; i < (Options.Length + 1) / 2; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < Options.Length; i++)
        {
            var option = Options[i];
            var preview = new Image
            {
                HeightRequest = 150,
                Aspect = Aspect.AspectFill,
                Opacity = 1
            };
            SemanticProperties.SetDescription(preview, $"معاينة خلفية {option.Name} داخل التطبيق");
            _ = ChatThemePage.LoadPreviewAsync(preview, option.PreviewAsset);
            var previewTap = new TapGestureRecognizer();
            previewTap.Tapped += async (_, _) => await PreviewBuiltInInsideAppAsync(option);
            preview.GestureRecognizers.Add(previewTap);

            var name = new Label { Text = option.Name, FontSize = 14, FontAttributes = FontAttributes.Bold, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
            ChatAppearancePageStyle.ApplyPrimaryText(name);
            var description = new Label { Text = option.Description, FontSize = 10.5, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2 };
            ChatAppearancePageStyle.ApplyMutedText(description);
            var selected = string.Equals(current, "url:" + option.VideoUrl, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo"), "animated", StringComparison.OrdinalIgnoreCase);
            var apply = new Button
            {
                Text = selected ? "الخلفية الحالية ✓" : "استخدام الخلفية",
                FontSize = 11,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#7D24EC"),
                BorderColor = Color.FromArgb("#D9C5F4"),
                BorderWidth = 1,
                CornerRadius = 11,
                Padding = new Thickness(8, 8)
            };
            ChatAppearancePageStyle.ApplyPrimaryButton(apply);
            apply.Clicked += async (_, _) => await ApplyBuiltInAsync(option);

            var details = new VerticalStackLayout { Padding = new Thickness(10, 9), Spacing = 7 };
            details.Add(name);
            details.Add(description);
            details.Add(apply);
            var body = new VerticalStackLayout { Spacing = 0 };
            body.Add(preview);
            body.Add(details);
            var card = new Border
            {
                BackgroundColor = CardBackground,
                Stroke = CardBorder,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 17 },
                Padding = 0,
                Content = body
            };
            ChatAppearancePageStyle.ApplyCard(card);
            if (selected) card.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
            _animatedItems.Add((option.VideoUrl, apply, card));
            grid.Add(card, i % 2, i / 2);
        }

        content.Add(grid);

        var galleryButton = new Button
        {
            Text = "اختيار فيديو MP4 من الجهاز",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#7D24EC"),
            BorderColor = Color.FromArgb("#CDA5FF"),
            BorderWidth = 1,
            CornerRadius = 14,
            HeightRequest = 50
        };
        ChatAppearancePageStyle.ApplyPrimaryButton(galleryButton);
        galleryButton.ImageSource = "himo_pro_ui_icon_plus.png";
        galleryButton.Clicked += async (_, _) => await PickVideoAsync();
        content.Add(galleryButton);

        var stopButton = new Button
        {
            Text = "إيقاف الخلفية المتحركة",
            FontSize = 13,
            TextColor = Color.FromArgb("#300653"),
            BackgroundColor = Color.FromArgb("#39204F"),
            CornerRadius = 13,
            HeightRequest = 46
        };
        ChatAppearancePageStyle.ApplySecondaryButton(stopButton);
        stopButton.Clicked += async (_, _) => await DisableAnimatedWallpaperAsync();
        content.Add(stopButton);
        content.Add(_status);

        Content = ChatAppearancePageStyle.CreateScroll(content);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
    }

    protected override void OnDisappearing()
    {
        StopInlinePreview();
        base.OnDisappearing();
    }

    private async Task PreviewBuiltInInsideAppAsync(AnimatedWallpaperOption option)
    {
        try
        {
            StopInlinePreview();

            if (!Uri.TryCreate(option.VideoUrl, UriKind.Absolute, out var videoUri)
                || videoUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(videoUri.Host, "videos.pexels.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("رابط فيديو المعاينة غير صالح.");

            var player = new MediaElement
            {
                Aspect = Aspect.AspectFill,
                AndroidViewType = CommunityToolkit.Maui.Core.AndroidViewType.TextureView,
                ShouldAutoPlay = true,
                ShouldLoopPlayback = true,
                ShouldShowPlaybackControls = false,
                ShouldKeepScreenOn = false,
                ShouldMute = true,
                Volume = 0,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                HeightRequest = 190
            };

            player.MediaFailed += (_, args) => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!ReferenceEquals(_inlinePreviewPlayer, player)) return;
                StopInlinePreview();
                _status.Text = "تعذرت معاينة الفيديو داخل التطبيق. تحقق من اتصال الإنترنت ثم حاول مجددًا.";
                System.Diagnostics.Debug.WriteLine($"[Himo AnimatedWallpaperPreview] Inline playback failed: {args?.ErrorMessage}");
            });

            _inlinePreviewPlayer = player;
            _inlinePreviewHost.Content = new Border
            {
                HeightRequest = 190,
                Padding = 0,
                BackgroundColor = Colors.Black,
                Stroke = CardBorder,
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
                Content = player
            };
            _inlinePreviewHost.HeightRequest = 190;
            _inlinePreviewHost.IsVisible = true;
            player.Source = MediaSource.FromUri(videoUri);
            _status.Text = $"تُعرض معاينة {option.Name} داخل Himo. اضغط «استخدام الخلفية» لحفظها للمحادثات.";
        }
        catch (Exception ex)
        {
            StopInlinePreview();
            _status.Text = "تعذر تشغيل المعاينة داخل التطبيق: " + ex.Message;
            System.Diagnostics.Debug.WriteLine($"[Himo AnimatedWallpaperPreview] {ex}");
        }
    }

    private void StopInlinePreview()
    {
        try { _inlinePreviewPlayer?.Stop(); } catch { }
        _inlinePreviewPlayer = null;
        _inlinePreviewHost.Content = null;
        _inlinePreviewHost.IsVisible = false;
        _inlinePreviewHost.HeightRequest = 0;
    }

    private async Task ApplyBuiltInAsync(AnimatedWallpaperOption option)
    {
        try
        {
            if (!Uri.TryCreate(option.VideoUrl, UriKind.Absolute, out var videoUri)
                || videoUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(videoUri.Host, "videos.pexels.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("رابط فيديو الخلفية غير صالح.");

            StopInlinePreview();
            RememberPreviousMode();
            Preferences.Default.Set(GlobalAnimatedWallpaperKey, "url:" + option.VideoUrl);
            Preferences.Default.Set(ChatThemePage.BackgroundModeKey, "animated");
            RefreshAnimatedSelection(option.VideoUrl);
            _status.Text = $"تم اختيار فيديو {option.Name}. يلزم اتصال بالإنترنت لتشغيل المقطع الحقيقي داخل المحادثة.";
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), "تم حفظ الفيديو الحقيقي. يحتاج التشغيل إلى الإنترنت ويعمل بصمت في تكرار مستمر.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر اختيار الفيديو: " + ex.Message;
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), _status.Text, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async Task PickVideoAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر فيديو MP4 لخلفية الدردشة" });
            if (picked is null) return;

            if (!string.Equals(Path.GetExtension(picked.FileName), ".mp4", StringComparison.OrdinalIgnoreCase))
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), "اختر ملف فيديو بصيغة MP4 لضمان أفضل توافق مع أجهزة أندرويد.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            await using var input = await picked.OpenReadAsync();
            var destination = Path.Combine(FileSystem.Current.AppDataDirectory, $"himo_chat_animated_{Guid.NewGuid():N}.mp4");
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output);

            var info = new FileInfo(destination);
            if (info.Length == 0 || info.Length > 100L * 1024 * 1024)
            {
                try { File.Delete(destination); } catch { }
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), "حجم الفيديو غير مناسب. اختر فيديو أقل من 100 ميجابايت.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            RememberPreviousMode();
            Preferences.Default.Set(GlobalAnimatedWallpaperKey, "local:" + destination);
            Preferences.Default.Set(ChatThemePage.BackgroundModeKey, "animated");
            RefreshAnimatedSelection(null);
            _status.Text = "تم حفظ الفيديو المختار. سيعمل كخلفية صامتة للمحادثات التي لا تملك خلفية خاصة بها.";
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), Himo.Services.AppLocalization.TranslateSourceText("تم اختيار الفيديو بنجاح."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر استخدام الفيديو: " + ex.Message;
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), _status.Text, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async Task DisableAnimatedWallpaperAsync()
    {
        StopInlinePreview();
        var previousMode = Preferences.Default.Get(PreviousBackgroundModeKey, "photo");
        if (!string.Equals(previousMode, "theme", StringComparison.OrdinalIgnoreCase))
            previousMode = "photo";
        Preferences.Default.Set(ChatThemePage.BackgroundModeKey, previousMode);
        RefreshAnimatedSelection(null);
        _status.Text = previousMode == "theme"
            ? "تم إيقاف الخلفية المتحركة. سيعود استخدام السمة اللونية المحددة سابقًا."
            : "تم إيقاف الخلفية المتحركة. سيعود استخدام الخلفية الفوتوغرافية المحددة سابقًا.";
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفيات متحركة"), _status.Text, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    private void RefreshAnimatedSelection(string? videoUrl)
    {
        foreach (var item in _animatedItems)
        {
            var selected = !string.IsNullOrWhiteSpace(videoUrl)
                && string.Equals(item.VideoUrl, videoUrl, StringComparison.OrdinalIgnoreCase);
            item.Button.Text = selected ? "الخلفية الحالية ✓" : "استخدام الخلفية";
            item.Card.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
            item.Card.StrokeThickness = selected ? 2 : 1;
        }
    }

    private static void RememberPreviousMode()
    {
        var currentMode = Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo");
        if (!string.Equals(currentMode, "animated", StringComparison.OrdinalIgnoreCase))
            Preferences.Default.Set(PreviousBackgroundModeKey, string.Equals(currentMode, "theme", StringComparison.OrdinalIgnoreCase) ? "theme" : "photo");
    }

    /// <summary>
    /// Resolves a selected background to a local video file. Real Pexels MP4 clips
    /// are downloaded once into CacheDirectory and reused on subsequent chat opens.
    /// </summary>
    public static async Task<string> ResolveVideoPathAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("لم يتم اختيار خلفية متحركة.", nameof(value));

        if (value.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = value.Substring("local:".Length);
            if (!File.Exists(localPath)) throw new FileNotFoundException("ملف الفيديو المختار غير موجود.", localPath);
            return localPath;
        }

        if (value.StartsWith("url:", StringComparison.OrdinalIgnoreCase))
        {
            var url = value.Substring("url:".Length);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "videos.pexels.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("رابط فيديو الخلفية غير صالح.");

            return await DownloadRealVideoToCacheAsync(uri);
        }

        // Migrate older saved selections away from the previous image-derived clips.
        var legacyName = value.StartsWith("asset:", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(value.Substring("asset:".Length))
            : Path.GetFileName(value);
        var legacyVideo = Options.FirstOrDefault(x =>
            (legacyName.Equals("himo_animated_lake.mp4", StringComparison.OrdinalIgnoreCase) && x.VideoUrl.Contains("5138001", StringComparison.Ordinal))
            || (legacyName.Equals("himo_animated_city.mp4", StringComparison.OrdinalIgnoreCase) && x.VideoUrl.Contains("11372920", StringComparison.Ordinal))
            || (legacyName.Equals("himo_animated_flowers.mp4", StringComparison.OrdinalIgnoreCase) && x.VideoUrl.Contains("8559724", StringComparison.Ordinal))
            || (legacyName.Equals("himo_animated_waterfall.mp4", StringComparison.OrdinalIgnoreCase) && x.VideoUrl.Contains("8859850", StringComparison.Ordinal))
            || (legacyName.Equals("himo_animated_sunset.mp4", StringComparison.OrdinalIgnoreCase) && x.VideoUrl.Contains("7975481", StringComparison.Ordinal)));
        if (legacyVideo is not null)
            return await ResolveVideoPathAsync("url:" + legacyVideo.VideoUrl);

        throw new InvalidDataException("الخلفية المحفوظة قديمة أو غير مدعومة. اختر أحد الفيديوهات الحقيقية من صفحة الخلفيات المتحركة.");
    }

    private static async Task<string> DownloadRealVideoToCacheAsync(Uri uri)
    {
        var fileName = "himo_pexels_live_v1_" + Path.GetFileName(uri.AbsolutePath);
        var cachedPath = Path.Combine(FileSystem.Current.CacheDirectory, fileName);
        if (File.Exists(cachedPath) && new FileInfo(cachedPath).Length > 0)
            return cachedPath;

        var tempPath = cachedPath + ".download";
        try
        {
            using var response = await VideoHttpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var length = response.Content.Headers.ContentLength;
            if (length.HasValue && (length.Value <= 0 || length.Value > 100L * 1024 * 1024))
                throw new InvalidDataException("حجم فيديو الخلفية غير مناسب.");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType)
                && !mediaType.Contains("video", StringComparison.OrdinalIgnoreCase)
                && !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("المصدر الذي أعاده الخادم ليس ملف فيديو.");

            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output);

            var downloadedLength = new FileInfo(tempPath).Length;
            if (downloadedLength == 0 || downloadedLength > 100L * 1024 * 1024)
                throw new InvalidDataException("تعذر تنزيل فيديو الخلفية كاملًا.");

            File.Move(tempPath, cachedPath, overwrite: true);
            return cachedPath;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private sealed record AnimatedWallpaperOption(string Name, string Description, string VideoUrl, string PreviewAsset);
}


/// <summary>Consistent, palette-aware styling for chat appearance pages.</summary>
internal static class ChatAppearancePageStyle
{
    public static ScrollView CreateScroll(View content)
    {
        if (content is VisualElement visual)
            visual.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        var scroll = new ScrollView
        {
            BackgroundColor = Color.FromArgb("#211333"),
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
        scroll.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        return scroll;
    }

    public static View TitleRow(string assetName, string text, double fontSize)
    {
        var row = SettingsVisuals.TitleRow(assetName, text, fontSize);
        if (row is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Label label)
                    ApplyPrimaryText(label);
            }
        }
        return row;
    }

    public static void ApplyPage(ContentPage page) =>
        page.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

    public static void ApplyPrimaryText(Label label) =>
        label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

    public static void ApplyMutedText(Label label) =>
        label.SetDynamicResource(Label.TextColorProperty, "HimoMuted");

    public static void ApplyCard(Border card)
    {
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
    }

    public static void ApplyPrimaryButton(Button button)
    {
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
    }

    public static void ApplySecondaryButton(Button button)
    {
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        button.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
    }
}
