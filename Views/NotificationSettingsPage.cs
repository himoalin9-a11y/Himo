using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Notification preferences UI. Values are persisted locally using Preferences.
/// Android delivery channels must read the chosen tone/vibration keys to apply them to OS notifications.
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
    private Switch? _appNotificationsSwitch;
    private Label? _appNotificationsStatus;
    private Button? _openAndroidSettingsButton;

    public NotificationSettingsPage(INotificationService notifications)
    {
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Title = "الإشعارات";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#080F24");
        Shell.SetNavBarIsVisible(this, false);
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
            RowSpacing = 0,
            BackgroundColor = Color.FromArgb("#080F24")
        };

        var header = new Grid
        {
            Padding = new Thickness(18, 12, 18, 12),
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
            Text = "‹",
            FontSize = 32,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#EAF0FF"),
            BackgroundColor = Color.FromArgb("#19294D"),
            BorderColor = Color.FromArgb("#344B7D"),
            BorderWidth = 1,
            CornerRadius = 15,
            Padding = 0,
            WidthRequest = 44,
            HeightRequest = 44,
            VerticalOptions = LayoutOptions.Center
        };
        backButton.Clicked += async (_, _) => await GoBackAsync();
        header.Add(backButton, 0, 0);

        var titleStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            FlowDirection = FlowDirection.RightToLeft,
            Children =
            {
                new Label
                {
                    Text = "الإشعارات",
                    FontSize = 24,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.End
                },
                new Label
                {
                    Text = "خصّص الأصوات والتنبيهات والاهتزاز",
                    FontSize = 12,
                    TextColor = Color.FromArgb("#9FB3E2"),
                    HorizontalTextAlignment = TextAlignment.End
                }
            }
        };
        header.Add(titleStack, 1, 0);
        header.Add(new BoxView { WidthRequest = 42, Opacity = 0 }, 2, 0);
        root.Add(header, 0, 0);

        var contentStack = new VerticalStackLayout
        {
            Padding = new Thickness(16, 4, 16, 24),
            Spacing = 14,
            FlowDirection = FlowDirection.RightToLeft
        };

        contentStack.Children.Add(CreateSection(
            "الإعدادات العامة",
            CreateGlobalNotificationsRow(),
            CreateToggleRow(
                "نغمة المحادثات",
                "تشغيل الأصوات للرسائل الواردة والصادرة",
                ConversationSoundsKey,
                true),
            CreateToggleRow(
                "التذكيرات",
                "تذكير بالرسائل أو المكالمات أو الحالات الجديدة التي لم تشاهدها",
                RemindersKey,
                true)));

        contentStack.Children.Add(CreateSection(
            "للرسائل",
            CreateChoiceRow(
                "نغمة الإشعارات",
                MessagesToneKey,
                "النغمة الافتراضية",
                isVibration: false),
            CreateChoiceRow(
                "اهتزاز",
                MessagesVibrationKey,
                "تلقائي",
                isVibration: true)));

        contentStack.Children.Add(CreateSection(
            "للمجموعات",
            CreateChoiceRow(
                "نغمة الإشعارات",
                GroupsToneKey,
                "النغمة الافتراضية",
                isVibration: false),
            CreateChoiceRow(
                "اهتزاز",
                GroupsVibrationKey,
                "تلقائي",
                isVibration: true)));

        contentStack.Children.Add(CreateSection(
            "للمكالمات",
            CreateChoiceRow(
                "نغمة الرنين",
                CallsToneKey,
                "النغمة الافتراضية",
                isVibration: false),
            CreateChoiceRow(
                "اهتزاز",
                CallsVibrationKey,
                "تلقائي",
                isVibration: true)));

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
        var description = new VerticalStackLayout
        {
            Spacing = 3,
            FlowDirection = FlowDirection.RightToLeft,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "إشعارات التطبيق",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#F1F4FF"),
                    HorizontalTextAlignment = TextAlignment.End
                }
            }
        };

        _appNotificationsStatus = new Label
        {
            FontSize = 10.5,
            TextColor = Color.FromArgb("#A1B2D9"),
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.WordWrap
        };
        description.Children.Add(_appNotificationsStatus);

        _appNotificationsSwitch = new Switch
        {
            IsToggled = _notifications.IsEnabled,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        _appNotificationsSwitch.Toggled += async (_, args) =>
            await SetAppNotificationsEnabledAsync(args.Value);

        _openAndroidSettingsButton = new Button
        {
            Text = "فتح إعدادات Android",
            IsVisible = false,
            HeightRequest = 36,
            CornerRadius = 12,
            FontSize = 11,
            BackgroundColor = Color.FromArgb("#1B2B55"),
            TextColor = Color.FromArgb("#C5D5FF")
        };
#if ANDROID
        _openAndroidSettingsButton.Clicked += (_, _) => Himo.MainActivity.OpenNotificationSettings();
#else
        _openAndroidSettingsButton.IsVisible = false;
#endif

        var stack = new VerticalStackLayout { Spacing = 8 };
        var row = new Grid
        {
            Padding = new Thickness(13, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 70
        };
        row.Add(description, 0, 0);
        row.Add(_appNotificationsSwitch, 1, 0);
        stack.Children.Add(row);
        stack.Children.Add(_openAndroidSettingsButton);
        UpdateAppNotificationStatus();
        return stack;
    }

    private View CreateToggleRow(string title, string subtitle, string preferenceKey, bool defaultValue)
    {
        var text = new VerticalStackLayout
        {
            Spacing = 3,
            FlowDirection = FlowDirection.RightToLeft,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#F1F4FF"),
                    HorizontalTextAlignment = TextAlignment.End
                },
                new Label
                {
                    Text = subtitle,
                    FontSize = 11,
                    TextColor = Color.FromArgb("#9FAFD5"),
                    HorizontalTextAlignment = TextAlignment.End,
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

        var toggle = new Switch
        {
            IsToggled = Preferences.Default.Get(preferenceKey, defaultValue),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        toggle.Toggled += (_, args) => Preferences.Default.Set(preferenceKey, args.Value);

        var row = new Grid
        {
            Padding = new Thickness(13, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 70
        };
        row.Add(text, 0, 0);
        row.Add(toggle, 1, 0);
        return row;
    }

    private View CreateChoiceRow(string title, string preferenceKey, string defaultValue, bool isVibration)
    {
        var textStack = new VerticalStackLayout
        {
            Spacing = 3,
            FlowDirection = FlowDirection.RightToLeft,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = title,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#F1F4FF"),
                    HorizontalTextAlignment = TextAlignment.End
                }
            }
        };

        var valueLabel = new Label
        {
            Text = Preferences.Default.Get(preferenceKey, defaultValue),
            FontSize = 11,
            TextColor = Color.FromArgb("#A5B7E2"),
            HorizontalTextAlignment = TextAlignment.End
        };
        textStack.Children.Add(valueLabel);

        var arrow = new Label
        {
            Text = "‹",
            FontSize = 27,
            TextColor = Color.FromArgb("#8CA6E8"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };

        var row = new Grid
        {
            Padding = new Thickness(13, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 64
        };
        row.Add(arrow, 0, 0);
        row.Add(textStack, 1, 0);
        row.Add(new Label { Text = isVibration ? "〰" : "♫", FontSize = 21, TextColor = Color.FromArgb("#B8A5FF"), VerticalTextAlignment = TextAlignment.Center }, 2, 0);

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
                var initialValue = Preferences.Default.Get(preferenceKey, defaultValue);
                var selected = await DisplayActionSheetAsync("اختر " + title, "إلغاء", null, ToneOptions);
                if (selected is null || selected == "إلغاء")
                    return;

                Preferences.Default.Set(preferenceKey, selected);
                valueLabel.Text = selected;
            }
        };
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private View CreateSection(string title, params View[] rows)
    {
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#EAF0FF"),
            HorizontalTextAlignment = TextAlignment.End,
            Margin = new Thickness(2, 2, 2, 1)
        };

        var content = new VerticalStackLayout { Spacing = 0 };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
                content.Children.Add(new BoxView
                {
                    HeightRequest = 1,
                    Margin = new Thickness(12, 0),
                    BackgroundColor = Color.FromArgb("#28395F")
                });
            content.Children.Add(rows[i]);
        }

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#101D3D"),
            Stroke = Color.FromArgb("#283D6B"),
            StrokeThickness = 1,
            Padding = 0,
            Content = content,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) }
        };

        return new VerticalStackLayout
        {
            Spacing = 8,
            Children = { titleLabel, card }
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
                ? "إشعارات التطبيق مفعّلة وإذن النظام مسموح ✓"
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

/// <summary>A small modal chooser with an explicit Cancel and OK action.</summary>
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
        BackgroundColor = Color.FromArgb("#E9081228");

        var optionStack = new VerticalStackLayout { Spacing = 2 };
        foreach (var option in options)
        {
            var radio = new RadioButton
            {
                Content = option,
                GroupName = "vibration-choice",
                IsChecked = option == _selected,
                TextColor = Color.FromArgb("#EFF3FF"),
                FlowDirection = FlowDirection.RightToLeft,
                HorizontalOptions = LayoutOptions.Fill,
                Margin = new Thickness(0, 2)
            };
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
            BackgroundColor = Color.FromArgb("#1A294B"),
            TextColor = Color.FromArgb("#D4DFFF"),
            BorderColor = Color.FromArgb("#3B4F79"),
            BorderWidth = 1,
            CornerRadius = 13,
            HeightRequest = 46
        };
        cancelButton.Clicked += async (_, _) => await CloseAsync(null);

        var okButton = new Button
        {
            Text = "موافق",
            BackgroundColor = Color.FromArgb("#6E4DE6"),
            TextColor = Colors.White,
            CornerRadius = 13,
            HeightRequest = 46
        };
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

        var modalContent = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(20),
            Children =
            {
                new Label
                {
                    Text = "الاهتزاز",
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalTextAlignment = TextAlignment.End
                },
                new Label
                {
                    Text = "اختر نمط الاهتزاز ثم اضغط موافق لحفظ الاختيار.",
                    FontSize = 12,
                    TextColor = Color.FromArgb("#A8B9E3"),
                    HorizontalTextAlignment = TextAlignment.End
                },
                optionStack,
                buttons
            }
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#111E3D"),
            Stroke = Color.FromArgb("#5748AE"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(22) },
            Content = modalContent,
            WidthRequest = 340,
            MaximumWidthRequest = 420,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
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
            // The selection task has already been completed; avoid crashing if the modal was dismissed externally.
        }
    }
}
