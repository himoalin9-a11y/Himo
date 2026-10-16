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
    protected static readonly Color PageColor = Color.FromArgb("#F3EDFF");
    protected static readonly Color CardColor = Color.FromArgb("#FFFFFF");
    protected static readonly Color BorderColor = Color.FromArgb("#D9C5F4");
    protected static readonly Color MainText = Color.FromArgb("#300653");
    protected static readonly Color SecondaryText = Color.FromArgb("#7A679B");

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
            BackgroundColor = Color.FromArgb("#EDE1FF"), TextColor = Color.FromArgb("#5C16CC"),
            BorderColor = Color.FromArgb("#D9C5F4"), BorderWidth = 1, CornerRadius = 15,
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
                    TextColor = MainText, HorizontalTextAlignment = TextAlignment.End },
                new Label { Text = PageSubtitle, FontSize = 12, TextColor = Color.FromArgb("#7A679B"),
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
        header.Add(SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(PageTitle), 42, 24), 2, 0);

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
        Text = text, FontSize = 10, TextColor = Color.FromArgb("#7A679B"),
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
                BorderColor = Color.FromArgb("#D9C5F4"),
                TextColor = MainText,
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
            OnColor = Color.FromArgb("#7D24EC"),
            ThumbColor = Color.FromArgb("#FFFFFF"),
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

    protected View MenuRow(string title, string subtitle, string route)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(34)), new(GridLength.Star), new(new GridLength(52)) },
            ColumnSpacing = 9, Padding = new Thickness(6, 11), FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 72
        };
        grid.Add(new Label { Text = "‹", FontSize = 28, TextColor = Color.FromArgb("#7A679B"),
            VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center }, 0, 0);
        grid.Add(new VerticalStackLayout
        {
            Spacing = 4, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft,
            Children = { Heading(title), Hint(subtitle) }
        }, 1, 0);
        var iconBox = SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(title), 42, 24);
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
    public PrivacySettingsPage() : base("الخصوصية", "تحكم في ظهور معلوماتك ومن يمكنه التواصل معك")
    {
        var rows = new VerticalStackLayout { Spacing = 0 };
        rows.Children.Add(MenuRow("آخر ظهور ومتصل الآن", "حدد من يمكنه رؤية آخر ظهورك", "privacy-last-seen"));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("صورة الملف الشخصي", "تحكم بمن يمكنه رؤية صورتك", "privacy-profile-photo"));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("الحالة", "رؤية الحالة ومشاركة الحساب", "privacy-status"));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("المجموعات", "من يمكنه إضافتك إلى مجموعة", "privacy-groups"));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("المكالمات", "إعدادات المتصلين المجهولين", "privacy-calls"));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("جهات الاتصال", "إدارة الحسابات المحظورة", "privacy-contacts"));
        rows.Children.Add(Separator());
        rows.Children.Add(MenuRow("التحقق من الخصوصية", "راجع إعداداتك لحماية معلوماتك", "privacy-checkup"));
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
                new Image { Source = "settings_icon_blocked.png", WidthRequest = 58, HeightRequest = 58, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
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
            FontAttributes = FontAttributes.Bold, BackgroundColor = Color.FromArgb("#7D24EC"), TextColor = Colors.White
        };
        button.Clicked += async (_, _) => await GoBackAsync();
        SetPageContent(
            Card(new VerticalStackLayout
            {
                Spacing = 9,
                Children =
                {
                    new Image { Source = "settings_icon_check.png", WidthRequest = 52, HeightRequest = 52, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center },
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
        grid.Add(new Label { Text = value, FontSize = 12, TextColor = Color.FromArgb("#7A679B"), VerticalTextAlignment = TextAlignment.Center }, 0, 0);
        grid.Add(new Label { Text = title, FontSize = 13, TextColor = MainText, HorizontalTextAlignment = TextAlignment.End,
            FlowDirection = FlowDirection.RightToLeft, VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        parent.Children.Add(grid);
        parent.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(0, 0, 0, 0) });
    }
}
