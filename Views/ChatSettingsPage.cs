using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using IOPath = System.IO.Path;

namespace Himo.Views;

/// <summary>
/// Chat preferences. This page intentionally owns its visual helpers so the
/// change can be shipped as a small, self-contained patch without altering
/// global navigation or other settings pages.
/// </summary>
public sealed class ChatSettingsPage : ContentPage
{
    public const string EnterToSendKey = "himo_chat_enter_to_send";
    public const string ShowMediaKey = "himo_chat_show_media";
    public const string ChatFontSizeKey = "himo_chat_font_size";
    public const string StickerSuggestionsKey = "himo_chat_sticker_suggestions";
    public const string RecentMediaSuggestionsKey = "himo_chat_recent_media_suggestions";
    public const string KeepArchivedKey = "himo_chat_keep_archived";
    public const string BackupCellularKey = "himo_chat_backup_cellular";
    public const string BackupVideosKey = "himo_chat_backup_videos";
    public const string BackupFrequencyKey = "himo_chat_backup_frequency";

    private readonly ChatService _chat;
    private static readonly Color IconTileColor = Color.FromArgb("#F0EAFE");
    private static readonly Color IconTileBorderColor = Color.FromArgb("#E0D7F8");

    public ChatSettingsPage(ChatService chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Title = "الدردشات";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F7F6FB");
        SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(16, 14, 16, 28),
            Spacing = 17,
            FlowDirection = FlowDirection.RightToLeft
        };
        content.Add(BuildHeading("إعدادات الدردشة", "تحكّم في طريقة عرض الرسائل والوسائط", "himo_s6_chat_chat_messages.png"));

        var fontButton = ValueButton(Preferences.Default.Get(ChatFontSizeKey, "متوسط"));
        fontButton.Clicked += async (_, _) => await ChooseFontSizeAsync(fontButton);

        content.Add(Section("تفضيلات المحادثة", "himo_s6_chat_keyboard_send.png",
            ToggleRow("مفتاح الإدخال للإرسال", "استخدم مفتاح الإدخال لإرسال الرسالة", "himo_s6_chat_keyboard_send.png", EnterToSendKey, true),
            ToggleRow("عرض الوسائط داخل المحادثة", "إظهار الصور والفيديوهات والمرفقات", "himo_s6_chat_media.png", ShowMediaKey, true),
            SettingRow("حجم الخط", "اضبط حجم نصوص الرسائل", "himo_s6_chat_font_size.png", fontButton),
            ToggleRow("اقتراحات الملصقات", "إظهار اقتراحات الملصقات أثناء الكتابة", "himo_s6_chat_sticker.png", StickerSuggestionsKey, true),
            ToggleRow("اقتراحات الوسائط الحديثة", "اقتراح الصور والوسائط المستخدمة مؤخرًا", "himo_s6_chat_recent_media.png", RecentMediaSuggestionsKey, true)));

        content.Add(Section("مظهر المحادثة", "himo_s6_chat_chat_theme.png",
            ActionRow("سمة الدردشة", "اختر ألوانًا متناسقة لواجهة المحادثة", "himo_s6_chat_chat_theme.png", "تخصيص", async () => await Navigation.PushAsync(new ChatThemePage())),
            ActionRow("الخلفيات المتحركة", "خلفيات متحركة هادئة للمحادثة", "himo_s6_chat_video_wallpaper.png", "استعراض", async () => await Navigation.PushAsync(new ChatAnimatedWallpaperPage())),
            ActionRow("فقاعات الرسائل", "خصص شكل الرسائل المرسلة والمستقبلة", "himo_s6_chat_chat_bubble.png", "الألوان", async () => await Navigation.PushAsync(new ChatBubbleSettingsPage())),
            ActionRow("خلفية الدردشة", "اختر خلفية من الصور المتاحة أو المعرض", "himo_s6_chat_wallpaper.png", "الخلفيات", async () => await Navigation.PushAsync(new ChatWallpaperSettingsPage()))));

        content.Add(Section("الأرشفة", "himo_s6_chat_archive.png",
            ToggleRow("الاحتفاظ بالدردشات المؤرشفة", "إبقاء المحادثة في الأرشيف عند وصول رسالة جديدة", "himo_s6_chat_archive.png", KeepArchivedKey, true)));

        content.Add(Section("النسخ الاحتياطي", "himo_s6_chat_backup.png",
            ActionRow("النسخ الاحتياطي للدردشات", "صدّر المحادثات والرسائل المحفوظة على هذا الجهاز", "himo_s6_chat_backup.png", "إدارة", async () => await Navigation.PushAsync(new ChatBackupSettingsPage(_chat)))));

        var footer = new Label
        {
            Text = "تُحفظ تفضيلاتك تلقائيًا على هذا الجهاز.",
            FontSize = 11,
            HorizontalTextAlignment = TextAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(8, 2, 8, 0)
        };
        footer.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        content.Add(footer);
        Content = new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
    }

    private async Task ChooseFontSizeAsync(Button button)
    {
        var chosen = await DisplayActionSheetAsync("حجم الخط", "إلغاء", null, "صغير", "متوسط", "كبير");
        if (chosen is not ("صغير" or "متوسط" or "كبير"))
            return;

        Preferences.Default.Set(ChatFontSizeKey, chosen);
        button.Text = chosen;
        await DisplayAlertAsync("حجم الخط", "تم حفظ التفضيل. سيُستخدم الحجم عند فتح المحادثة التالية.", "موافق");
    }

    private static View BuildHeading(string title, string subtitle, string iconName)
    {
        var labels = new VerticalStackLayout { Spacing = 4, FlowDirection = FlowDirection.RightToLeft, VerticalOptions = LayoutOptions.Center };
        var heading = new Label { Text = title, FontSize = 22, FontAttributes = FontAttributes.Bold, FlowDirection = FlowDirection.RightToLeft, HorizontalTextAlignment = TextAlignment.Start };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = subtitle, FontSize = 12, FlowDirection = FlowDirection.RightToLeft, HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.WordWrap };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(heading);
        labels.Add(detail);

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 13,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Thickness(0, 0, 0, 2)
        };
        grid.Add(IconBadge(iconName, 50, 27), 0, 0);
        grid.Add(labels, 1, 0);
        return grid;
    }

    private static View Section(string title, string iconName, params View[] rows)
    {
        var sectionTitle = new Label
        {
            Text = title,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Margin = new Thickness(2, 0, 2, 0),
            FlowDirection = FlowDirection.RightToLeft,
            HorizontalTextAlignment = TextAlignment.Start
        };
        sectionTitle.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var titleGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.LeftToRight
        };
        titleGrid.Add(sectionTitle, 0, 0);
        titleGrid.Add(IconBadge(iconName, 30, 16), 1, 0);

        var rowStack = new VerticalStackLayout { Spacing = 0, FlowDirection = FlowDirection.RightToLeft };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
                rowStack.Add(Divider());
            rowStack.Add(rows[i]);
        }

        var section = new VerticalStackLayout { Spacing = 8, FlowDirection = FlowDirection.RightToLeft };
        section.Add(titleGrid);
        section.Add(Surface(rowStack));
        return section;
    }

    private static Border Surface(View content)
    {
        var border = new Border
        {
            Padding = new Thickness(12, 5),
            BackgroundColor = Color.FromArgb("#FFFFFF"),
            Stroke = new SolidColorBrush(Color.FromArgb("#E3DDEF")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) },
            Content = content
        };
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        border.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return border;
    }

    private static BoxView Divider()
    {
        var divider = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 1), BackgroundColor = Color.FromArgb("#EDE8F6") };
        divider.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
        return divider;
    }

    private static View ToggleRow(string title, string description, string iconName, string preferenceKey, bool defaultValue)
    {
        var toggle = new Microsoft.Maui.Controls.Switch { IsToggled = Preferences.Default.Get(preferenceKey, defaultValue), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        toggle.Toggled += (_, e) => Preferences.Default.Set(preferenceKey, e.Value);
        return SettingRow(title, description, iconName, toggle);
    }

    private static View ActionRow(string title, string description, string iconName, string actionText, Func<Task> callback)
    {
        var button = ActionButton(actionText, callback);
        return SettingRow(title, description, iconName, button);
    }

    private static View SettingRow(string title, string description, string iconName, View control)
    {
        var labels = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        var heading = new Label { Text = title, FontSize = 13.5, FontAttributes = FontAttributes.Bold, FlowDirection = FlowDirection.RightToLeft, HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.WordWrap };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = description, FontSize = 11, FlowDirection = FlowDirection.RightToLeft, HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2 };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(heading);
        labels.Add(detail);

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 11,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 66,
            Margin = new Thickness(0, 3)
        };
        grid.Add(control, 0, 0);
        grid.Add(labels, 1, 0);
        grid.Add(IconBadge(iconName, 42, 22), 2, 0);
        return grid;
    }

    private static Button ValueButton(string value)
    {
        var button = new Button { Text = value, FontSize = 12, FontAttributes = FontAttributes.Bold, CornerRadius = 12, Padding = new Thickness(12, 0), HeightRequest = 38, MinimumWidthRequest = 68 };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        return button;
    }

    private static Button ActionButton(string text, Func<Task> callback)
    {
        var button = new Button { Text = text, FontSize = 11.5, FontAttributes = FontAttributes.Bold, CornerRadius = 11, Padding = new Thickness(11, 0), HeightRequest = 36, MinimumWidthRequest = 62 };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled)
                return;
            button.IsEnabled = false;
            try
            {
                await callback();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Himo chat settings navigation/action failed: {ex}");
            }
            finally
            {
                button.IsEnabled = true;
            }
        };
        return button;
    }

    private static Border IconBadge(string assetName, double size, double iconSize)
    {
        var badge = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Padding = Math.Max(4, (size - iconSize) / 2),
            BackgroundColor = IconTileColor,
            Stroke = new SolidColorBrush(IconTileBorderColor),
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(size / 2) },
            Content = new Image
            {
                Source = assetName,
                WidthRequest = iconSize,
                HeightRequest = iconSize,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        return badge;
    }
}

public sealed class ChatBackupSettingsPage : ContentPage
{
    private static readonly Color IconTileColor = Color.FromArgb("#F0EAFE");
    private static readonly Color IconTileBorderColor = Color.FromArgb("#E0D7F8");
    private readonly ChatService _chat;
    private readonly Picker _frequency = new() { Title = "اختر التكرار", ItemsSource = new List<string> { "يوميًا", "أسبوعيًا", "شهريًا", "فقط عند الضغط على نسخ احتياطي", "متوقف" }, WidthRequest = 150, FontSize = 11.5 };
    private readonly Microsoft.Maui.Controls.Switch _cellular = new();
    private readonly Microsoft.Maui.Controls.Switch _videos = new();
    private readonly Label _status = new() { FontSize = 11.5, LineBreakMode = LineBreakMode.WordWrap };

    public ChatBackupSettingsPage(ChatService chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Title = "النسخ الاحتياطي";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F7F6FB");
        SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        _status.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        _frequency.SelectedItem = Preferences.Default.Get(ChatSettingsPage.BackupFrequencyKey, "فقط عند الضغط على نسخ احتياطي");
        _cellular.IsToggled = Preferences.Default.Get(ChatSettingsPage.BackupCellularKey, false);
        _videos.IsToggled = Preferences.Default.Get(ChatSettingsPage.BackupVideosKey, false);
        _frequency.SelectedIndexChanged += (_, _) => Preferences.Default.Set(ChatSettingsPage.BackupFrequencyKey, _frequency.SelectedItem?.ToString() ?? "فقط عند الضغط على نسخ احتياطي");
        _cellular.Toggled += (_, e) => Preferences.Default.Set(ChatSettingsPage.BackupCellularKey, e.Value);
        _videos.Toggled += (_, e) => Preferences.Default.Set(ChatSettingsPage.BackupVideosKey, e.Value);

        var content = new VerticalStackLayout { Padding = new Thickness(16, 14, 16, 28), Spacing = 17, FlowDirection = FlowDirection.RightToLeft };
        content.Add(BuildHeading("النسخ الاحتياطي", "احتفظ بنسخة من المحادثات على هذا الجهاز", "himo_s6_chat_backup.png"));
        content.Add(Section("التخزين السحابي", "himo_s6_chat_cloud_storage.png",
            ActionRow("مساحة تخزين Google", "فتح صفحة إدارة مساحة التخزين من المتصفح", "himo_s6_chat_cloud_storage.png", "فتح", async () => await Launcher.Default.OpenAsync("https://one.google.com/storage"))));

        var frequencyLabels = new[] { "تكرار النسخ الاحتياطي", "هذا اختيار محفوظ محليًا؛ لا توجد مزامنة تلقائية مهيأة" };
        content.Add(Section("النسخ الاحتياطي المحلي", "himo_s6_chat_frequency.png",
            SettingRow(frequencyLabels[0], frequencyLabels[1], "himo_s6_chat_frequency.png", _frequency),
            SettingRow("استخدام الشبكة الخلوية", "السماح باستخدام بيانات الهاتف عند إعداد نسخ احتياطي", "himo_s6_chat_network.png", _cellular),
            SettingRow("تضمين ملفات الفيديو", "يشمل ملفات الفيديو في ملف النسخة الاحتياطية", "himo_s6_chat_video.png", _videos),
            ActionRow("تصدير نسخة احتياطية", "إنشاء ملف JSON من الرسائل والمحادثات المحلية", "himo_s6_chat_backup.png", "نسخ الآن", ExportBackupAsync)));

        var statusBorder = new Border
        {
            Padding = new Thickness(13, 11),
            BackgroundColor = Color.FromArgb("#F0EAFE"),
            Stroke = new SolidColorBrush(Color.FromArgb("#E0D7F8")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Content = _status
        };
        statusBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        statusBorder.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        content.Add(statusBorder);
        Content = new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
    }

    private static View BuildHeading(string title, string subtitle, string iconName)
    {
        var labels = new VerticalStackLayout { Spacing = 4, FlowDirection = FlowDirection.RightToLeft, VerticalOptions = LayoutOptions.Center };
        var heading = new Label { Text = title, FontSize = 22, FontAttributes = FontAttributes.Bold, FlowDirection = FlowDirection.RightToLeft };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var detail = new Label { Text = subtitle, FontSize = 12, FlowDirection = FlowDirection.RightToLeft, LineBreakMode = LineBreakMode.WordWrap };
        detail.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(heading);
        labels.Add(detail);
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 13, FlowDirection = FlowDirection.LeftToRight };
        grid.Add(IconBadge(iconName, 50, 27), 0, 0);
        grid.Add(labels, 1, 0);
        return grid;
    }

    private static View Section(string title, string iconName, params View[] rows)
    {
        var titleLabel = new Label { Text = title, FontSize = 14, FontAttributes = FontAttributes.Bold, FlowDirection = FlowDirection.RightToLeft };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, FlowDirection = FlowDirection.LeftToRight };
        header.Add(titleLabel, 0, 0);
        header.Add(IconBadge(iconName, 30, 16), 1, 0);
        var body = new VerticalStackLayout { Spacing = 0, FlowDirection = FlowDirection.RightToLeft };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
                body.Add(Divider());
            body.Add(rows[i]);
        }
        var card = new Border { Padding = new Thickness(12, 5), BackgroundColor = Color.FromArgb("#FFFFFF"), Stroke = new SolidColorBrush(Color.FromArgb("#E3DDEF")), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(19) }, Content = body };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var stack = new VerticalStackLayout { Spacing = 8, FlowDirection = FlowDirection.RightToLeft };
        stack.Add(header);
        stack.Add(card);
        return stack;
    }

    private static BoxView Divider()
    {
        var divider = new BoxView { HeightRequest = 1, Margin = new Thickness(0, 1), BackgroundColor = Color.FromArgb("#EDE8F6") };
        divider.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
        return divider;
    }

    private static View SettingRow(string title, string description, string iconName, View control)
    {
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        var titleLabel = new Label { Text = title, FontSize = 13, FontAttributes = FontAttributes.Bold, FlowDirection = FlowDirection.RightToLeft, LineBreakMode = LineBreakMode.WordWrap };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subLabel = new Label { Text = description, FontSize = 11, FlowDirection = FlowDirection.RightToLeft, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2 };
        subLabel.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(titleLabel);
        labels.Add(subLabel);
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10, FlowDirection = FlowDirection.LeftToRight, MinimumHeightRequest = 66, Margin = new Thickness(0, 3) };
        grid.Add(control, 0, 0);
        grid.Add(labels, 1, 0);
        grid.Add(IconBadge(iconName, 42, 22), 2, 0);
        return grid;
    }

    private View ActionRow(string title, string subtitle, string iconName, string buttonText, Func<Task> action)
    {
        var button = new Button { Text = buttonText, FontSize = 11.5, FontAttributes = FontAttributes.Bold, CornerRadius = 11, Padding = new Thickness(11, 0), HeightRequest = 36, MinimumWidthRequest = 62 };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { _status.Text = "تعذر تنفيذ العملية: " + ex.Message; }
            finally { button.IsEnabled = true; }
        };
        return SettingRow(title, subtitle, iconName, button);
    }

    private static Border IconBadge(string assetName, double size, double iconSize)
    {
        return new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            Padding = Math.Max(4, (size - iconSize) / 2),
            BackgroundColor = IconTileColor,
            Stroke = new SolidColorBrush(IconTileBorderColor),
            StrokeThickness = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(size / 2) },
            Content = new Image { Source = assetName, WidthRequest = iconSize, HeightRequest = iconSize, Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center }
        };
    }

    private async Task ExportBackupAsync()
    {
        try
        {
            var backup = new
            {
                format = "HimoLocalChatBackup",
                version = 1,
                createdAtUtc = DateTime.UtcNow,
                note = "Contains conversations and messages currently stored in this device's local ChatService cache.",
                conversations = _chat.Conversations.Select(c => new
                {
                    c.Id, c.RemoteId, c.Name, c.Initial, c.LastMessage, c.Time, c.UpdatedAt,
                    messages = _chat.GetMessages(c.Id).Select(m => new
                    {
                        m.Id, m.RemoteId, m.ConversationId, m.Text, m.SentAt, m.IsMine,
                        m.AttachmentFileName, m.AttachmentContentType, m.AttachmentSize,
                        m.ReplyToRemoteId, m.ReplyToText, m.IsEdited, m.IsDeleted, m.DeliveryStatus
                    }).ToList()
                }).ToList()
            };
            var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
            var path = IOPath.Combine(FileSystem.CacheDirectory, $"HimoChatBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(path, json);
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "نسخة احتياطية للدردشة", File = new ShareFile(path, "application/json") });
            _status.Text = "تم إنشاء ملف النسخة الاحتياطية وفتح قائمة المشاركة.";
        }
        catch (Exception ex)
        {
            _status.Text = "تعذر إنشاء النسخة الاحتياطية: " + ex.Message;
        }
    }
}
