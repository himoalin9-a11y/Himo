using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Himo.Services;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Privacy settings screens. The current backend persists these choices locally;
/// server enforcement is intentionally not implied by these controls.
/// </summary>
public abstract class PrivacyPageBase : Himo.Services.LocalizedContentPage
{
    protected static readonly Color PageColor = Color.FromArgb("#211333");
    protected static readonly Color CardColor = Color.FromArgb("#2D1942");
    protected static readonly Color BorderColor = Color.FromArgb("#594768");
    protected static readonly Color MainText = Color.FromArgb("#F7E9FF");
    protected static readonly Color SecondaryText = Color.FromArgb("#C7B8D8");
    protected static readonly Color SoftAccent = Color.FromArgb("#39204F");
    protected static readonly Color Accent = Color.FromArgb("#C76BFF");

    protected PrivacyPageBase(string title, string subtitle)
    {
        Title = title;
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = PageColor;
        SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        Shell.SetNavBarIsVisible(this, false);
        PageTitle = title;
        PageSubtitle = subtitle;
        _body.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
    }

    private string PageTitle { get; }
        private string PageSubtitle { get; }
    private readonly VerticalStackLayout _body = new()
    {
        Padding = new Thickness(18, 8, 18, 26),
        Spacing = 16,
        FlowDirection = FlowDirection.RightToLeft
    };

    protected void SetPageContent(params View[] items)
    {
        var back = new Border
        {
            WidthRequest = 44,
            HeightRequest = 44,
            Padding = 11,
            BackgroundColor = SoftAccent,
            Stroke = BorderColor,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = "himo_icon_arrow_back_purple.png",
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += async (_, _) => await GoBackAsync();
        back.GestureRecognizers.Add(backTap);

        var titleLabel = new Label
        {
            Text = PageTitle,
            FontSize = 23,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var subtitleLabel = new Label
        {
            Text = PageSubtitle,
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        var heading = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Children = { titleLabel, subtitleLabel }
        };

        var header = new Grid
        {
            Padding = new Thickness(18, 12, 18, 8),
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(48)), new(GridLength.Star), new(new GridLength(48))
            },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight
        };
        header.Add(back, 0, 0);
        header.Add(heading, 1, 0);
        header.Add(PrivacyIconBadge(PageTitle, 42, 22), 2, 0);

        _body.Children.Clear();
        foreach (var item in items)
            _body.Children.Add(item);

        var scroll = new ScrollView
        {
            BackgroundColor = PageColor,
            Content = _body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
        scroll.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        var layout = new Grid
        {
            BackgroundColor = PageColor,
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star)
            }
        };
        layout.Add(header, 0, 0);
        layout.Add(scroll, 0, 1);
        layout.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        Content = layout;
    }

    protected static Border PrivacyIconBadge(string title, double size = 42, double iconSize = 22)
    {
        var safeIconSize = Math.Max(16, Math.Min(iconSize, size - 12));
        var badge = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Padding = Math.Max(4, (size - safeIconSize) / 2),
            BackgroundColor = SoftAccent,
            Stroke = BorderColor,
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(Math.Max(9, size * 0.27)) },
            Content = new Image
            {
                Source = SettingsVisuals.IconForSetting(title),
                WidthRequest = safeIconSize,
                HeightRequest = safeIconSize,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        badge.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoIconBackground");
        badge.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return badge;
    }

    protected static Border Card(View child, Thickness? padding = null)
    {
        var card = new Border
        {
            Content = child,
            BackgroundColor = CardColor,
            Stroke = BorderColor,
            StrokeThickness = 1,
            Padding = padding ?? new Thickness(15),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) }
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    protected static Label Heading(string text, double fontSize = 15)
    {
        var label = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        return label;
    }

    protected static Label Hint(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        return label;
    }

    protected static View Note(string text)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var note = new Border
        {
            Content = label,
            Padding = new Thickness(13, 10),
            Margin = new Thickness(2, 0),
            BackgroundColor = SoftAccent,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }
        };
        note.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        return note;
    }

    protected static View ChoiceSection(string key, string question, IReadOnlyList<string> choices, string defaultChoice)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 10,
            FlowDirection = FlowDirection.RightToLeft
        };
        content.Children.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Padding = new Thickness(2, 0, 2, 5),
            Children = { Heading(question, 16), Hint("اختر خيارًا واحدًا من القائمة التالية.") }
        });

        var selected = Preferences.Default.Get(key, defaultChoice);
        var optionViews = new List<(RadioButton Radio, Border Container, Label Label)>();
        for (var i = 0; i < choices.Count; i++)
        {
            var option = choices[i];
            var isSelected = string.Equals(selected, option, StringComparison.Ordinal);
            var radio = new RadioButton
            {
                GroupName = key,
                Value = option,
                IsChecked = isSelected,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = Colors.Transparent,
                BorderColor = Accent,
                TextColor = MainText,
                Scale = 0.9
            };
            radio.SetDynamicResource(RadioButton.BorderColorProperty, "HimoAccent");
            radio.SetDynamicResource(RadioButton.TextColorProperty, "HimoPrimary");
            var label = new Label
            {
                Text = option,
                FontSize = 14,
                FontAttributes = isSelected ? FontAttributes.Bold : FontAttributes.None,
                TextColor = MainText,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalTextAlignment = TextAlignment.Start,
                FlowDirection = FlowDirection.RightToLeft
            };
            label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

            var row = new Grid
            {
                Padding = new Thickness(8, 4),
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(new GridLength(38)), new(GridLength.Star)
                },
                ColumnSpacing = 8,
                FlowDirection = FlowDirection.LeftToRight,
                MinimumHeightRequest = 46
            };
            row.Add(radio, 0, 0);
            row.Add(label, 1, 0);

            var container = new Border
            {
                Content = row,
                Padding = new Thickness(5, 2),
                BackgroundColor = isSelected ? SoftAccent : CardColor,
                Stroke = isSelected ? Accent : BorderColor,
                StrokeThickness = isSelected ? 1.4 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) }
            };
            container.SetDynamicResource(VisualElement.BackgroundColorProperty, isSelected ? "HimoSoft" : "HimoCard");
            container.SetDynamicResource(Border.StrokeProperty, isSelected ? "HimoAccent" : "HimoBorder");
            optionViews.Add((radio, container, label));
            radio.CheckedChanged += (_, args) =>
            {
                if (!args.Value)
                    return;

                Preferences.Default.Set(key, option);
                foreach (var item in optionViews)
                {
                    var active = item.Radio.IsChecked;
                    item.Container.SetDynamicResource(VisualElement.BackgroundColorProperty, active ? "HimoSoft" : "HimoCard");
                    item.Container.SetDynamicResource(Border.StrokeProperty, active ? "HimoAccent" : "HimoBorder");
                    item.Container.StrokeThickness = active ? 1.4 : 1;
                    item.Label.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
                }
            };
            var rowTap = new TapGestureRecognizer();
            rowTap.Tapped += (_, _) => radio.IsChecked = true;
            container.GestureRecognizers.Add(rowTap);
            content.Children.Add(container);
        }
        return Card(content, new Thickness(14));
    }

    protected static View ToggleSection(string key, string heading, string description, bool defaultValue)
    {
        var toggle = new Switch
        {
            IsToggled = Preferences.Default.Get(key, defaultValue),
            OnColor = Accent,
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        toggle.SetDynamicResource(Switch.OnColorProperty, "HimoAccent");
        toggle.Toggled += (_, args) => Preferences.Default.Set(key, args.Value);
        var labels = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft,
            Children = { Heading(heading, 15), Hint(description) }
        };
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(54)), new(GridLength.Star), new(new GridLength(48))
            },
            ColumnSpacing = 10,
            Padding = new Thickness(2, 3),
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 64
        };
        row.Add(toggle, 0, 0);
        row.Add(labels, 1, 0);
        row.Add(PrivacyIconBadge(heading, 40, 21), 2, 0);
        return Card(row, new Thickness(14));
    }

    protected View MenuRow(string title, string subtitle, string route)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(28)), new(GridLength.Star), new(new GridLength(44))
            },
            ColumnSpacing = 10,
            Padding = new Thickness(8, 11),
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 72
        };
        grid.Add(new Image
        {
            Source = "himo_icon_arrow_back_purple.png",
            WidthRequest = 16,
            HeightRequest = 16,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);
        grid.Add(new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Children = { Heading(title, 15), Hint(subtitle) }
        }, 1, 0);
        grid.Add(PrivacyIconBadge(title, 42, 22), 2, 0);
        var routeTap = new TapGestureRecognizer();
        routeTap.Tapped += async (_, _) => await OpenRouteAsync(route);
        grid.GestureRecognizers.Add(routeTap);
        return grid;
    }

    protected static View Separator()
    {
        var divider = new BoxView
        {
            HeightRequest = 1,
            BackgroundColor = BorderColor,
            Margin = new Thickness(12, 0)
        };
        divider.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
        return divider;
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
        if (shell is not null)
            await shell.GoToAsync("..", false);
        else if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }
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
            Heading("مشاركة الحساب", 16),
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
            Spacing = 12,
            HorizontalOptions = LayoutOptions.Fill,
            Padding = new Thickness(12, 22),
            Children =
            {
                PrivacyIconBadge("الحسابات المحظورة", 66, 34),
                Heading("الحسابات المحظورة", 17),
                Hint("ستظهر قائمة الحسابات المحظورة هنا عند ربطها بواجهة الخادم. حاليًا، يمكنك حظر حساب من خيارات المحادثة.")
            }
        };
        SetPageContent(
            Card(empty, new Thickness(18)),
            Note("الخادم الحالي لا يوفّر بعد واجهة لعرض جميع الحسابات المحظورة وإلغاء حظرها من هذه الصفحة."));
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
            Text = "العودة إلى إعدادات الخصوصية",
            HeightRequest = 48,
            CornerRadius = 15,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Accent,
            TextColor = Colors.White,
            Margin = new Thickness(0, 8, 0, 0)
        };
        button.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        button.Clicked += async (_, _) => await GoBackAsync();

        SetPageContent(
            Card(new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    PrivacyIconBadge("التحقق من الخصوصية", 62, 30),
                    Heading("حماية معلوماتك", 17),
                    Hint("راجع تفضيلات الخصوصية التي اخترتها على هذا الجهاز."),
                    new BoxView { HeightRequest = 1, BackgroundColor = BorderColor, Margin = new Thickness(0, 3) },
                    details,
                    button
                }
            }, new Thickness(16)),
            Note("هذه مراجعة للتفضيلات المحلية. يلزم ربطها بالخادم لتطبيق إعدادات الظهور والحظر فعليًا."));
    }

    private static void AddSummary(VerticalStackLayout parent, string title, string value)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Star), new(new GridLength(44))
            },
            ColumnSpacing = 10,
            Padding = new Thickness(2, 9),
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 52
        };
        var valueLabel = new Label
        {
            Text = value,
            FontSize = 12,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft
        };
        valueLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        row.Add(valueLabel, 0, 0);
        var label = Heading(title, 13);
        row.Add(label, 1, 0);
        row.Add(PrivacyIconBadge(title, 34, 18), 2, 0);
        parent.Children.Add(row);
        parent.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = BorderColor });
    }
}
