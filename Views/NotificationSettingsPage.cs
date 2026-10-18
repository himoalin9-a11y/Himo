using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Notification preferences UI. Values are stored locally in Preferences.
/// Android delivery channels should read the selected tone/vibration keys.
/// </summary>
public sealed class NotificationSettingsPage : ContentPage
{
    private const string ConversationSoundsKey = "himo_notification_conversation_sounds";
    private const string RemindersKey = "himo_notification_reminders";

    private const string MessagesToneKey = "himo_notification_messages_tone";
    private const string MessagesVibrationKey = "himo_notification_messages_vibration";
    private const string GroupsToneKey = "himo_notification_groups_tone";
    private const string GroupsVibrationKey = "himo_notification_groups_vibration";
    private const string CallsToneKey = "himo_notification_calls_tone";
    private const string CallsVibrationKey = "himo_notification_calls_vibration";

    private static readonly string[] ToneOptions =
    {
        "النغمة الافتراضية",
        "نغمة هادئة",
        "نغمة كلاسيكية",
        "نغمة قصيرة",
        "بدون نغمة"
    };

    private static readonly string[] VibrationOptions =
    {
        "إيقاف",
        "تلقائي",
        "قصير",
        "طويل"
    };

    private readonly INotificationService _notifications;
    private Microsoft.Maui.Controls.Switch? _appNotificationsSwitch;
    private Label? _appNotificationsStatus;
    private Button? _openAndroidSettingsButton;

    public NotificationSettingsPage(INotificationService notifications)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Title = "الإشعارات";
        FlowDirection = FlowDirection.RightToLeft;
        Shell.SetNavBarIsVisible(this, false);
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");
        Content = BuildPage();
    }

    private View BuildPage()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            RowSpacing = 0
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            Padding = new Thickness(18, 14, 18, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight
        };

        var backButton = new Button
        {
            Text = string.Empty,
            ImageSource = "himo_s7_notif_back.png",
            Padding = 0,
            WidthRequest = 46,
            HeightRequest = 46,
            MinimumWidthRequest = 46,
            MinimumHeightRequest = 46,
            CornerRadius = 15,
            BorderWidth = 1,
            VerticalOptions = LayoutOptions.Center
        };
        backButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        backButton.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        backButton.Clicked += async (_, _) => await GoBackAsync();
        header.Add(backButton, 0, 0);

        var title = new Label
        {
            Text = "الإشعارات",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var subtitle = new Label
        {
            Text = "خصّص الأصوات والتنبيهات والاهتزاز",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap,
            FlowDirection = FlowDirection.RightToLeft
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        var titleStack = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Children = { title, subtitle }
        };
        header.Add(titleStack, 1, 0);
        header.Add(IconBadge("himo_s7_notif_bell.png", 46, 24), 2, 0);
        root.Add(header, 0, 0);

        var contentStack = new VerticalStackLayout
        {
            Padding = new Thickness(16, 4, 16, 26),
            Spacing = 17,
            FlowDirection = FlowDirection.RightToLeft
        };

        contentStack.Children.Add(CreateSection(
            "الإعدادات العامة",
            "himo_s7_notif_adjustments.png",
            CreateGlobalNotificationsRow(),
            CreateToggleRow(
                "نغمة المحادثات",
                "تشغيل الأصوات للرسائل الواردة والصادرة",
                ConversationSoundsKey,
                true,
                "himo_s7_notif_sound.png"),
            CreateToggleRow(
                "التذكيرات",
                "تذكير بالرسائل أو المكالمات أو الحالات الجديدة",
                RemindersKey,
                true,
                "himo_s7_notif_clock.png")));

        contentStack.Children.Add(CreateSection(
            "للرسائل",
            "himo_s7_notif_message.png",
            CreateChoiceRow("نغمة الإشعارات", MessagesToneKey, "النغمة الافتراضية", isVibration: false),
            CreateChoiceRow("الاهتزاز", MessagesVibrationKey, "تلقائي", isVibration: true)));

        contentStack.Children.Add(CreateSection(
            "للمجموعات",
            "himo_s7_notif_group.png",
            CreateChoiceRow("نغمة الإشعارات", GroupsToneKey, "النغمة الافتراضية", isVibration: false),
            CreateChoiceRow("الاهتزاز", GroupsVibrationKey, "تلقائي", isVibration: true)));

        contentStack.Children.Add(CreateSection(
            "للمكالمات",
            "himo_s7_notif_phone.png",
            CreateChoiceRow("نغمة الرنين", CallsToneKey, "النغمة الافتراضية", isVibration: false),
            CreateChoiceRow("الاهتزاز", CallsVibrationKey, "تلقائي", isVibration: true)));

        var scroll = new ScrollView
        {
            Content = contentStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        };
        root.Add(scroll, 0, 1);
        return root;
    }

    private View CreateGlobalNotificationsRow()
    {
        var description = CreateDescriptionStack(
            "إشعارات التطبيق",
            "التحكم في السماح بإشعارات Himo على هذا الجهاز",
            14,
            11);

        _appNotificationsStatus = new Label
        {
            FontSize = 10.5,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _appNotificationsStatus.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        description.Children.Add(_appNotificationsStatus);

        _appNotificationsSwitch = new Microsoft.Maui.Controls.Switch
        {
            IsToggled = _notifications.IsEnabled,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        _appNotificationsSwitch.Toggled += async (_, args) => await SetAppNotificationsEnabledAsync(args.Value);

        _openAndroidSettingsButton = new Button
        {
            Text = "فتح إعدادات Android",
            IsVisible = false,
            HeightRequest = 38,
            MinimumHeightRequest = 38,
            CornerRadius = 12,
            FontSize = 11,
            Padding = new Thickness(14, 0),
            HorizontalOptions = LayoutOptions.Start
        };
        _openAndroidSettingsButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        _openAndroidSettingsButton.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        _openAndroidSettingsButton.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        _openAndroidSettingsButton.BorderWidth = 1;
#if ANDROID
        _openAndroidSettingsButton.Clicked += (_, _) => Himo.MainActivity.OpenNotificationSettings();
#else
        _openAndroidSettingsButton.IsVisible = false;
#endif

        var row = new Grid
        {
            Padding = new Thickness(14, 13),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 82
        };
        row.Add(_appNotificationsSwitch, 0, 0);
        row.Add(description, 1, 0);
        row.Add(IconBadge("himo_s7_notif_bell.png", 42, 22), 2, 0);

        var stack = new VerticalStackLayout { Spacing = 8 };
        stack.Children.Add(row);
        stack.Children.Add(new HorizontalStackLayout
        {
            Padding = new Thickness(14, 0, 14, 10),
            Children = { _openAndroidSettingsButton }
        });

        UpdateAppNotificationStatus();
        return stack;
    }

    private View CreateToggleRow(string title, string subtitle, string preferenceKey, bool defaultValue, string iconName)
    {
        var text = CreateDescriptionStack(title, subtitle, 14, 11);
        var toggle = new Microsoft.Maui.Controls.Switch
        {
            IsToggled = Preferences.Default.Get(preferenceKey, defaultValue),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        toggle.Toggled += (_, args) => Preferences.Default.Set(preferenceKey, args.Value);

        var row = new Grid
        {
            Padding = new Thickness(14, 13),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 76
        };
        row.Add(toggle, 0, 0);
        row.Add(text, 1, 0);
        row.Add(IconBadge(iconName, 40, 22), 2, 0);
        return row;
    }

    private View CreateChoiceRow(string title, string preferenceKey, string defaultValue, bool isVibration)
    {
        var textStack = CreateDescriptionStack(
            title,
            Preferences.Default.Get(preferenceKey, defaultValue),
            14,
            11);
        var valueLabel = (Label)textStack.Children[1];

        var chevron = new Image
        {
            Source = "himo_s7_notif_back.png",
            WidthRequest = 18,
            HeightRequest = 18,
            Aspect = Aspect.AspectFit,
            Opacity = 0.74,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        var row = new Grid
        {
            Padding = new Thickness(14, 13),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 70
        };
        row.Add(chevron, 0, 0);
        row.Add(textStack, 1, 0);
        row.Add(IconBadge(isVibration ? "himo_s7_notif_vibration.png" : "himo_s7_notif_sound.png", 40, 22), 2, 0);

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            if (isVibration)
            {
                var initialValue = Preferences.Default.Get(preferenceKey, defaultValue);
                var picker = new VibrationPickerPage(initialValue, VibrationOptions);
                await Navigation.PushModalAsync(picker, true);
                var selected = await picker.Selection;
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    Preferences.Default.Set(preferenceKey, selected);
                    valueLabel.Text = selected;
                }
            }
            else
            {
                var selected = await DisplayActionSheetAsync("اختر " + title, "إلغاء", null, ToneOptions);
                if (string.IsNullOrWhiteSpace(selected) || selected == "إلغاء")
                    return;

                Preferences.Default.Set(preferenceKey, selected);
                valueLabel.Text = selected;
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private static VerticalStackLayout CreateDescriptionStack(string title, string subtitle, double titleSize, double subtitleSize)
    {
        var titleLabel = new Label
        {
            Text = title,
            FontSize = titleSize,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var subtitleLabel = new Label
        {
            Text = subtitle,
            FontSize = subtitleSize,
            HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        subtitleLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        return new VerticalStackLayout
        {
            Spacing = 4,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, subtitleLabel }
        };
    }

    private static Border IconBadge(string assetName, double size, double iconSize)
    {
        var icon = new Image
        {
            Source = assetName,
            WidthRequest = iconSize,
            HeightRequest = iconSize,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var badge = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Padding = Math.Max(4, (size - iconSize) / 2),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(size / 2) },
            Content = icon
        };
        badge.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        badge.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return badge;
    }

    private View CreateSection(string title, string iconName, params View[] rows)
    {
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var heading = new HorizontalStackLayout
        {
            Spacing = 10,
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            Children = { IconBadge(iconName, 34, 18), titleLabel }
        };

        var content = new VerticalStackLayout { Spacing = 0 };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                var separator = new BoxView
                {
                    HeightRequest = 1,
                    Margin = new Thickness(14, 0),
                    Opacity = 0.85
                };
                separator.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
                content.Children.Add(separator);
            }
            content.Children.Add(rows[i]);
        }

        var card = new Border
        {
            Padding = 0,
            StrokeThickness = 1,
            Content = content,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) }
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");

        return new VerticalStackLayout
        {
            Spacing = 9,
            Children = { heading, card }
        };
    }

    private async Task SetAppNotificationsEnabledAsync(bool enabled)
    {
        try
        {
            await _notifications.SetEnabledAsync(enabled);
#if ANDROID
            if (enabled)
            {
                Himo.MainActivity.RequestNotificationPermissionIfNeeded();
                if (Application.Current is App app)
                    await app.RegisterCurrentPushTokenAsync();
            }
            else
            {
                if (Application.Current is App app)
                    await app.UnregisterCurrentPushTokenAsync();
            }
#endif
            UpdateAppNotificationStatus();
        }
        catch (Exception ex)
        {
            if (_appNotificationsSwitch is not null)
                _appNotificationsSwitch.IsToggled = !enabled;
            await DisplayAlertAsync("الإشعارات", "تعذر تغيير إعداد الإشعارات: " + ex.Message, "حسنًا");
            UpdateAppNotificationStatus();
        }
    }

    private void UpdateAppNotificationStatus()
    {
        if (_appNotificationsStatus is null)
            return;

#if ANDROID
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            _appNotificationsStatus.Text = _notifications.IsEnabled
                ? "إشعارات التطبيق مفعّلة."
                : "إشعارات التطبيق متوقفة.";
            if (_openAndroidSettingsButton is not null)
                _openAndroidSettingsButton.IsVisible = false;
            return;
        }

        var granted = Himo.MainActivity.IsNotificationPermissionGranted();
        _appNotificationsStatus.Text = !_notifications.IsEnabled
            ? "إشعارات التطبيق متوقفة."
            : granted
                ? "إشعارات التطبيق مفعّلة وإذن النظام مسموح."
                : "الإشعارات مفعّلة داخل التطبيق، لكن إذن Android غير مسموح.";
        if (_openAndroidSettingsButton is not null)
            _openAndroidSettingsButton.IsVisible = _notifications.IsEnabled && !granted;
#else
        _appNotificationsStatus.Text = _notifications.IsEnabled
            ? "إشعارات التطبيق مفعّلة."
            : "إشعارات التطبيق متوقفة.";
        if (_openAndroidSettingsButton is not null)
            _openAndroidSettingsButton.IsVisible = false;
#endif
    }

    private async Task GoBackAsync()
    {
        try
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("..", false);
            else
                await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("الإشعارات", "تعذر الرجوع: " + ex.Message, "حسنًا");
        }
    }
}

/// <summary>A modal picker for vibration preferences with explicit cancel and confirm actions.</summary>
internal sealed class VibrationPickerPage : ContentPage
{
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _selected;

    public Task<string?> Selection => _completion.Task;

    public VibrationPickerPage(string initialValue, IReadOnlyList<string> options)
    {
        _selected = options.Contains(initialValue) ? initialValue : "تلقائي";
        Title = "الاهتزاز";
        FlowDirection = FlowDirection.RightToLeft;
        SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var optionStack = new VerticalStackLayout { Spacing = 4 };
        foreach (var option in options)
        {
            var radio = new RadioButton
            {
                Content = option,
                GroupName = "vibration-choice",
                IsChecked = option == _selected,
                FlowDirection = FlowDirection.RightToLeft,
                HorizontalOptions = LayoutOptions.Fill,
                Margin = new Thickness(0, 2),
                Padding = new Thickness(8, 6)
            };
            radio.SetDynamicResource(RadioButton.TextColorProperty, "HimoPrimary");
            radio.CheckedChanged += (_, args) =>
            {
                if (args.Value)
                    _selected = option;
            };
            optionStack.Children.Add(radio);
        }

        var cancelButton = new Button
        {
            Text = "إلغاء",
            BorderWidth = 1,
            CornerRadius = 13,
            HeightRequest = 46
        };
        cancelButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        cancelButton.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        cancelButton.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        cancelButton.Clicked += async (_, _) => await CloseAsync(null);

        var okButton = new Button
        {
            Text = "موافق",
            CornerRadius = 13,
            HeightRequest = 46
        };
        okButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        okButton.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        okButton.Clicked += async (_, _) => await CloseAsync(_selected);

        var buttons = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight
        };
        buttons.Add(cancelButton, 0, 0);
        buttons.Add(okButton, 1, 0);

        var title = new Label
        {
            Text = "الاهتزاز",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Start
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var description = new Label
        {
            Text = "اختر نمط الاهتزاز ثم اضغط موافق لحفظ الاختيار.",
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.WordWrap
        };
        description.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        var modalContent = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(20),
            Children = { title, description, optionStack, buttons }
        };

        var card = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            Content = modalContent,
            WidthRequest = 340,
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");

        Content = new Grid
        {
            Padding = new Thickness(18),
            Children = { card }
        };
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }

    private async Task CloseAsync(string? value)
    {
        if (!_completion.TrySetResult(value))
            return;

        try
        {
            await Navigation.PopModalAsync(true);
        }
        catch
        {
            // Complete the selection even if the modal was already dismissed elsewhere.
        }
    }
}
