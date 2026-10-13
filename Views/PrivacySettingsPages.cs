using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Privacy UI. Values are stored locally until the API exposes privacy-settings endpoints.
/// </summary>
public abstract class PrivacyPageBase : ContentPage
{
    protected static readonly Color PageColor = Color.FromArgb("#080817");
    protected static readonly Color CardColor = Color.FromArgb("#101D3D");
    protected static readonly Color BorderColor = Color.FromArgb("#263B6B");
    protected static readonly Color MainText = Color.FromArgb("#F2F5FF");
    protected static readonly Color SecondaryText = Color.FromArgb("#9EAFDA");

    protected PrivacyPageBase(string title, string subtitle)
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
    private readonly VerticalStackLayout _body = new() { Padding = new Thickness(16, 8, 16, 28), Spacing = 14 };

    protected void SetPageContent(params View[] items)
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

        _body.Children.Clear();
        foreach (var item in items) _body.Children.Add(item);
        var scroll = new ScrollView { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
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
        Text = text, FontSize = 15, FontAttributes = FontAttributes.Bold,
        TextColor = MainText, HorizontalTextAlignment = TextAlignment.End
    };

    protected static Label Hint(string text) => new()
    {
        Text = text, FontSize = 11, TextColor = SecondaryText,
        HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap
    };

    protected static Label Note(string text) => new()
    {
        Text = text, FontSize = 10, TextColor = Color.FromArgb("#7F92BF"),
        HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap,
        Margin = new Thickness(4, 0)
    };

    protected static View ChoiceSection(string key, string question, IReadOnlyList<string> choices, string defaultChoice)
    {
        var content = new VerticalStackLayout { Spacing = 0, FlowDirection = FlowDirection.RightToLeft };
        content.Children.Add(new VerticalStackLayout
        {
            Spacing = 5,
            Padding = new Thickness(2, 2, 2, 10),
            Children = { Heading(question), Hint("اختر خيارًا واحدًا من القائمة التالية.") }
        });

        var selected = Preferences.Default.Get(key, defaultChoice);
        for (var i = 0; i < choices.Count; i++)
        {
            var option = choices[i];
            var radio = new RadioButton
            {
                GroupName = key,
                Value = option,
                IsChecked = string.Equals(selected, option, StringComparison.Ordinal),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent,
                BorderColor = Color.FromArgb("#6C7FB0"),
                TextColor = Colors.White,
                Scale = 0.9
            };
            radio.CheckedChanged += (_, args) =>
            {
                if (args.Value)
                    Preferences.Default.Set(key, option);
            };
            var label = new Label
            {
                Text = option, FontSize = 14, TextColor = MainText,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.End,
                FlowDirection = FlowDirection.RightToLeft
            };
            var row = new Grid
            {
                Padding = new Thickness(4, 8),
                ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(44)), new(GridLength.Star) },
                ColumnSpacing = 8,
                FlowDirection = FlowDirection.LeftToRight,
                MinimumHeightRequest = 48
            };
            row.Add(radio, 0, 0);
            row.Add(label, 1, 0);
            var rowTap = new TapGestureRecognizer();
            rowTap.Tapped += (_, _) => radio.IsChecked = true;
            row.GestureRecognizers.Add(rowTap);
            content.Children.Add(row);
            if (i < choices.Count - 1)
                content.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(2, 0) });
        }
        return Card(content, new Thickness(13));
    }

    protected static View ToggleSection(string key, string heading, string description, bool defaultValue)
    {
        var toggle = new Switch
        {
            IsToggled = Preferences.Default.Get(key, defaultValue),
            OnColor = Color.FromArgb("#7650E8"),
            ThumbColor = Color.FromArgb("#F3F5FF"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };
        toggle.Toggled += (_, args) => Preferences.Default.Set(key, args.Value);
        var labels = new VerticalStackLayout
        {
            Spacing = 4, FlowDirection = FlowDirection.RightToLeft,
            Children = { HeadingLabel(heading), Hint(description) }
        };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 12, Padding = new Thickness(2, 4), FlowDirection = FlowDirection.LeftToRight
        };
        row.Add(toggle, 0, 0);
        row.Add(labels, 1, 0);
        return Card(row, new Thickness(15));
    }

    private static Label HeadingLabel(string text) => new()
    {
        Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold,
        TextColor = MainText, HorizontalTextAlignment = TextAlignment.End
    };

    protected View MenuRow(string icon, string title, string subtitle, string route, Color iconColor)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(34)), new(GridLength.Star), new(new GridLength(52)) },
            ColumnSpacing = 9, Padding = new Thickness(6, 11), FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 72
        };
        grid.Add(new Label { Text = "‹", FontSize = 28, TextColor = Color.FromArgb("#91A9E7"),
            VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center }, 0, 0);
        grid.Add(new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft,
            Children = { Heading(title), Hint(subtitle) }
        }, 1, 0);
        var iconBox = new Border
        {
            WidthRequest = 42, HeightRequest = 42, BackgroundColor = iconColor, StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 21 },
            Content = new Label { Text = icon, FontSize = 21, TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        };
        grid.Add(iconBox, 2, 0);
        var routeTap = new TapGestureRecognizer();
        routeTap.Tapped += async (_, _) => await OpenRouteAsync(route);
        grid.GestureRecognizers.Add(routeTap);
        return grid;
    }

    private async Task OpenRouteAsync(string route)
    {
        var shell = Shell.Current;
        if (shell is not null)
        {
            await shell.GoToAsync(route, false);
            return;
        }

        PrivacyPageBase? target = route switch
        {
            "privacy-last-seen" => new PrivacyLastSeenPage(),
            "privacy-profile-photo" => new PrivacyProfilePhotoPage(),
            "privacy-status" => new PrivacyStatusPage(),
            "privacy-groups" => new PrivacyGroupsPage(),
            "privacy-calls" => new PrivacyCallsPage(),
            "privacy-contacts" => new PrivacyContactsPage(),
            "privacy-checkup" => new PrivacyCheckupPage(),
            _ => null
        };
        if (target is not null)
            await Navigation.PushAsync(target);
    }

    protected async Task GoBackAsync()
    {
        var shell = Shell.Current;
        if (shell is not null) await shell.GoToAsync("..", false);
        else if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync();
    }

    private async void BackClicked(object? sender, EventArgs e) => await GoBackAsync();
}

public sealed class PrivacySettingsPage : PrivacyPageBase
{
    private static readonly Color Purple = Color.FromArgb("#6139C8");
    public PrivacySettingsPage() : base("الخصوصية", "تحكم في ظهور معلوماتك ومن يمكنه التواصل معك")
    {
        var rows = new VerticalStackLayout { Spacing = 0 };
        rows.Children.Add(MenuRow("◷", "آخر ظهور ومتصل الآن", "حدد من يمكنه رؤية آخر ظهورك", "privacy-last-seen", Color.FromArgb("#245AC7")));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("◉", "صورة الملف الشخصي", "تحكم بمن يمكنه رؤية صورتك", "privacy-profile-photo", Color.FromArgb("#6038C7")));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("▤", "الحالة", "رؤية الحالة ومشاركة الحساب", "privacy-status", Color.FromArgb("#079B7D")));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("▦", "المجموعات", "من يمكنه إضافتك إلى مجموعة", "privacy-groups", Color.FromArgb("#B46A19")));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("☎", "المكالمات", "إعدادات المتصلين المجهولين", "privacy-calls", Color.FromArgb("#247F9D")));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("⊘", "جهات الاتصال", "إدارة الحسابات المحظورة", "privacy-contacts", Color.FromArgb("#A43C58")));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("✓", "التحقق من الخصوصية", "راجع إعداداتك لحماية معلوماتك", "privacy-checkup", Purple));
        SetPageContent(
            Card(rows, new Thickness(8, 4)),
            Note("تُحفظ الخيارات محليًا حاليًا؛ تفعيل قواعد الظهور والحظر لجميع المستخدمين يتطلب دعمًا من الخادم."));
    }

    private static View Separator() => new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(12, 0) };
}

public sealed class PrivacyLastSeenPage : PrivacyPageBase
{
    public PrivacyLastSeenPage() : base("آخر ظهور ومتصل الآن", "حدد من يمكنه رؤية وقت نشاطك")
    {
        SetPageContent(
            ChoiceSection("himo_privacy_last_seen", "من يمكنه رؤية آخر ظهور لي", new[] { "الكل", "جهات الاتصال", "لا أحد" }, "الكل"),
            Note("تُحفظ اختياراتك على هذا الجهاز حاليًا. تطبيق قواعد الظهور على بقية المستخدمين يحتاج إلى دعم الخادم."));
    }
}

public sealed class PrivacyProfilePhotoPage : PrivacyPageBase
{
    public PrivacyProfilePhotoPage() : base("صورة الملف الشخصي", "تحكم بمن يستطيع مشاهدة صورتك")
    {
        SetPageContent(
            ChoiceSection("himo_privacy_profile_photo", "من يمكنه رؤية صورة الملف الشخصي", new[] { "الكل", "جهات الاتصال", "لا أحد" }, "الكل"),
            Note("تُحفظ اختياراتك على هذا الجهاز حاليًا. تطبيق قواعد الظهور على بقية المستخدمين يحتاج إلى دعم الخادم."));
    }
}

public sealed class PrivacyStatusPage : PrivacyPageBase
{
    public PrivacyStatusPage() : base("الحالة", "من يمكنه مشاهدة حالتك ومشاركة حسابك")
    {
        SetPageContent(
            ChoiceSection("himo_privacy_status", "من يمكنه رؤية الحالة الخاصة بي", new[] { "الكل", "جهات الاتصال", "لا أحد" }, "جهات الاتصال"),
            Heading("مشاركة الحساب"),
            ToggleSection("himo_privacy_allow_account_sharing", "السماح بالمشاركة", "السماح بمشاركة معلومات حسابك من داخل التطبيق.", true),
            Note("يُحفظ هذا الاختيار على الجهاز حاليًا؛ يحتاج تطبيقه على بقية المستخدمين إلى ربطه بالخادم."));
    }
}

public sealed class PrivacyGroupsPage : PrivacyPageBase
{
    public PrivacyGroupsPage() : base("المجموعات", "تحكم بمن يمكنه إضافتك إلى المجموعات")
    {
        SetPageContent(
            ChoiceSection("himo_privacy_group_add", "من يمكنه إضافتي إلى مجموعة", new[] { "الكل", "الأصدقاء", "جهات الاتصال" }, "جهات الاتصال"),
            Note("تُحفظ اختياراتك على هذا الجهاز حاليًا. منع الإضافة من جانب الخادم يتطلب دعمًا إضافيًا."));
    }
}

public sealed class PrivacyCallsPage : PrivacyPageBase
{
    public PrivacyCallsPage() : base("المكالمات", "خيارات حماية المكالمات الواردة")
    {
        SetPageContent(
            ToggleSection("himo_privacy_block_unknown_callers", "حظر المتصلين المجهولين", "رفض المكالمات من أرقام لا تتعرف عليها.", false),
            Note("هذا الزر يحفظ تفضيلك على الجهاز حاليًا، ولا يمنع المكالمات الواردة من الخادم قبل تفعيل قواعد الحظر."));
    }
}

public sealed class PrivacyContactsPage : PrivacyPageBase
{
    public PrivacyContactsPage() : base("جهات الاتصال", "إدارة الحسابات التي لا تريد التواصل معها")
    {
        var empty = new VerticalStackLayout
        {
            Spacing = 10, HorizontalOptions = LayoutOptions.Fill, Padding = new Thickness(8, 14),
            Children =
            {
                new Label { Text = "⊘", FontSize = 44, TextColor = Color.FromArgb("#A43C58"), HorizontalTextAlignment = TextAlignment.Center },
                new Label { Text = "الحسابات المحظورة", FontSize = 17, FontAttributes = FontAttributes.Bold,
                    TextColor = MainText, HorizontalTextAlignment = TextAlignment.Center },
                Hint("ستظهر قائمة الحسابات المحظورة هنا عند ربطها بواجهة الخادم. حاليًا، يمكنك حظر حساب من خيارات المحادثة.")
            }
        };
        SetPageContent(Card(empty), Note("الخادم الحالي لا يوفّر بعد واجهة لعرض جميع الحسابات المحظورة وإلغاء حظرها من هذه الصفحة."));
    }
}

public sealed class PrivacyCheckupPage : PrivacyPageBase
{
    public PrivacyCheckupPage() : base("التحقق من الخصوصية", "مراجعة سريعة لحماية معلوماتك")
    {
        var details = new VerticalStackLayout { Spacing = 0 };
        AddSummary(details, "آخر ظهور", Preferences.Default.Get("himo_privacy_last_seen", "الكل"));
        AddSummary(details, "صورة الملف الشخصي", Preferences.Default.Get("himo_privacy_profile_photo", "الكل"));
        AddSummary(details, "الحالة", Preferences.Default.Get("himo_privacy_status", "جهات الاتصال"));
        AddSummary(details, "إضافة إلى المجموعات", Preferences.Default.Get("himo_privacy_group_add", "جهات الاتصال"));
        AddSummary(details, "مشاركة الحساب", Preferences.Default.Get("himo_privacy_allow_account_sharing", true) ? "مفعّلة" : "متوقفة");
        AddSummary(details, "حظر المتصلين المجهولين", Preferences.Default.Get("himo_privacy_block_unknown_callers", false) ? "مفعّل" : "متوقف");
        var button = new Button
        {
            Text = "العودة إلى إعدادات الخصوصية", HeightRequest = 46, CornerRadius = 14,
            FontAttributes = FontAttributes.Bold, BackgroundColor = Color.FromArgb("#7650E8"), TextColor = Colors.White
        };
        button.Clicked += async (_, _) => await GoBackAsync();
        SetPageContent(
            Card(new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    new Label { Text = "✓", FontSize = 40, TextColor = Color.FromArgb("#18D18B"), HorizontalTextAlignment = TextAlignment.Center },
                    Heading("حماية معلوماتك"),
                    Hint("راجع تفضيلات الخصوصية التي اخترتها على هذا الجهاز."),
                    new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(0, 3) },
                    details,
                    button
                }
            }),
            Note("هذه مراجعة للتفضيلات المحلية. يلزم ربطها بالخادم لتطبيق إعدادات الظهور والحظر فعليًا."));
    }

    private static void AddSummary(VerticalStackLayout parent, string title, string value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 10, Padding = new Thickness(2, 8), FlowDirection = FlowDirection.LeftToRight
        };
        grid.Add(new Label { Text = value, FontSize = 12, TextColor = Color.FromArgb("#B8C7F0"), VerticalTextAlignment = TextAlignment.Center }, 0, 0);
        grid.Add(new Label { Text = title, FontSize = 13, TextColor = MainText, HorizontalTextAlignment = TextAlignment.End,
            FlowDirection = FlowDirection.RightToLeft, VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        parent.Children.Add(grid);
        parent.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(0, 0, 0, 0) });
    }
}
