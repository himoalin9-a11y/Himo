using System.Text.Json;
using Himo.Services;
using Himo.Models;
using Microsoft.Maui.Storage;
using Microsoft.Maui.ApplicationModel;

namespace Himo.Views;

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
    private static readonly Color CardColor = Color.FromArgb("#FFFFFF");
    private static readonly Color BorderColor = Color.FromArgb("#D9C5F4");
    private static readonly Color AccentColor = Color.FromArgb("#7D24EC");
    private static readonly Color MutedColor = Color.FromArgb("#7A679B");

    public ChatSettingsPage(ChatService chat)
    {
        _chat = chat;
        Title = "الدردشات";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F3EDFF");

        var content = new VerticalStackLayout { Padding = new Thickness(16, 12), Spacing = 12 };
        content.Add(SettingsVisuals.TitleRow("settings_icon_chat.png", "إعدادات الدردشة", 19));
        content.Add(ToggleCard("مفتاح الإدخال للإرسال", "عند التفعيل يرسل الضغط على Enter الرسالة", EnterToSendKey, true));
        content.Add(ToggleCard("إمكانية رؤية الوسائط", "إظهار الصور والفيديوهات والمرفقات داخل المحادثة", ShowMediaKey, true));
        content.Add(FontSizeCard());
        content.Add(ToggleCard("اقتراحات الملصقات", "حفظ تفضيل اقتراح الملصقات أثناء الكتابة", StickerSuggestionsKey, true));
        content.Add(ToggleCard("اقتراحات أحدث الوسائط", "حفظ تفضيل اقتراح الوسائط الحديثة", RecentMediaSuggestionsKey, true));
        content.Add(Header("مظهر الدردشة"));
        content.Add(ActionCard("سمة الدردشة", "تصاميم لونية متموجة وتجريدية تُستخدم كخلفية مستقلة", "تخصيص", async () => await Navigation.PushAsync(new ChatThemePage())));
        content.Add(ActionCard("خلفيات متحركة", "مقاطع طبيعية صامتة تتحرك بسلاسة خلف الرسائل", "استعراض", async () => await Navigation.PushAsync(new ChatAnimatedWallpaperPage())));
        content.Add(ActionCard("فقاعة الدردشة", "اختر ألوانًا لامعة للرسائل المرسلة والمستقبلة", "الألوان", async () => await Navigation.PushAsync(new ChatBubbleSettingsPage())));
        content.Add(ActionCard("خلفية الدردشة", "خلفيات فوتوغرافية للزهور والمدن والطبيعة أو صورة من المعرض", "الخلفيات", async () => await Navigation.PushAsync(new ChatWallpaperSettingsPage())));
        content.Add(Header("الدردشة المؤرشفة"));
        content.Add(ToggleCard("الاحتفاظ بالدردشة في الأرشيف", "عند إيقافه، تُخرج المحادثة من الأرشيف عند وصول رسالة جديدة", KeepArchivedKey, true));
        content.Add(Header("النسخ الاحتياطي"));
        content.Add(ActionCard("نسخ احتياطي للدردشة", "تصدير المحادثات والرسائل المحفوظة على هذا الجهاز", "فتح", async () => await Navigation.PushAsync(new ChatBackupSettingsPage(_chat))));
        Content = new ScrollView { Content = content };
    }

    private static Label Header(string text) => new() { Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = MutedColor, Margin = new Thickness(3, 8, 3, 0) };

    private static Border Wrap(View content) => new()
    {
        BackgroundColor = CardColor, Stroke = BorderColor, StrokeThickness = 1, Padding = 14,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 }, Content = content
    };

    private static Border ToggleCard(string title, string description, string key, bool defaultValue)
    {
        var toggle = new Switch { IsToggled = Preferences.Default.Get(key, defaultValue), VerticalOptions = LayoutOptions.Center };
        toggle.Toggled += (_, e) => Preferences.Default.Set(key, e.Value);
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        labels.Add(new Label { Text = title, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#300653") });
        labels.Add(new Label { Text = description, FontSize = 11, TextColor = MutedColor });
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            FlowDirection = FlowDirection.LeftToRight
        };
        grid.Add(toggle, 0, 0);
        grid.Add(labels, 1, 0);
        grid.Add(SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(title), 38, 22), 2, 0);
        return Wrap(grid);
    }

    private Border FontSizeCard()
    {
        var selected = Preferences.Default.Get(ChatFontSizeKey, "متوسط");
        var button = new Button { Text = selected, FontSize = 13, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#7D24EC"), CornerRadius = 12, Padding = new Thickness(14, 8) };
        button.Clicked += async (_, _) =>
        {
            var current = Preferences.Default.Get(ChatFontSizeKey, "متوسط");
            var chosen = await DisplayActionSheetAsync("حجم الخط", "إلغاء", null, "صغير", "متوسط", "كبير");
            if (chosen is "صغير" or "متوسط" or "كبير")
            {
                Preferences.Default.Set(ChatFontSizeKey, chosen);
                button.Text = chosen;
                await DisplayAlertAsync("حجم الخط", "سيُطبّق الحجم على نصوص الرسائل عند فتح المحادثة التالية.", "موافق");
            }
        };
        var labels = new VerticalStackLayout { Spacing = 3 };
        labels.Add(new Label { Text = "حجم الخط", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#300653") });
        labels.Add(new Label { Text = "صغير أو متوسط أو كبير", FontSize = 11, TextColor = MutedColor });
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight
        };
        grid.Add(button, 0, 0);
        grid.Add(labels, 1, 0);
        grid.Add(SettingsVisuals.IconBadge("settings_icon_text.png", 38, 22), 2, 0);
        return Wrap(grid);
    }

    private static Border ActionCard(string title, string description, string action, Func<Task> callback)
    {
        var button = new Button { Text = action, FontSize = 12, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#7D24EC"), CornerRadius = 12, Padding = new Thickness(14, 8) };
        button.Clicked += async (_, _) => await callback();
        var labels = new VerticalStackLayout { Spacing = 3 };
        labels.Add(new Label { Text = title, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#300653") });
        labels.Add(new Label { Text = description, FontSize = 11, TextColor = MutedColor });
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
            FlowDirection = FlowDirection.LeftToRight
        };
        grid.Add(button, 0, 0);
        grid.Add(labels, 1, 0);
        grid.Add(SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(title), 38, 22), 2, 0);
        return Wrap(grid);
    }
}

public sealed class ChatBackupSettingsPage : ContentPage
{
    private readonly ChatService _chat;
    private static readonly Color Muted = Color.FromArgb("#7A679B");
    private readonly Picker _frequency = new() { Title = "اختر التكرار", ItemsSource = new List<string> { "يوميًا", "أسبوعيًا", "شهريًا", "فقط عند الضغط على نسخ احتياطي", "متوقف" } };
    private readonly Switch _cellular = new();
    private readonly Switch _videos = new();
    private readonly Label _status = new() { TextColor = Muted, FontSize = 12 };

    public ChatBackupSettingsPage(ChatService chat)
    {
        _chat = chat;
        Title = "نسخ احتياطي للدردشة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#F3EDFF");
        _frequency.SelectedItem = Preferences.Default.Get(ChatSettingsPage.BackupFrequencyKey, "فقط عند الضغط على نسخ احتياطي");
        _cellular.IsToggled = Preferences.Default.Get(ChatSettingsPage.BackupCellularKey, false);
        _videos.IsToggled = Preferences.Default.Get(ChatSettingsPage.BackupVideosKey, false);
        _frequency.SelectedIndexChanged += (_, _) => Preferences.Default.Set(ChatSettingsPage.BackupFrequencyKey, _frequency.SelectedItem?.ToString() ?? "فقط عند الضغط على نسخ احتياطي");
        _cellular.Toggled += (_, e) => Preferences.Default.Set(ChatSettingsPage.BackupCellularKey, e.Value);
        _videos.Toggled += (_, e) => Preferences.Default.Set(ChatSettingsPage.BackupVideosKey, e.Value);

        var content = new VerticalStackLayout { Padding = 16, Spacing = 12 };
        content.Add(Section("حساب Google", "ربط Google Drive يتطلب إعداد OAuth ومشروع Google Cloud؛ التصدير المحلي متاح أدناه."));
        content.Add(Action("إدارة مساحة التخزين في Google", "فتح مساحة تخزين Google", "فتح", async () => await Launcher.Default.OpenAsync("https://one.google.com/storage")));
        content.Add(Section("النسخ الاحتياطي التلقائي", "هذا الاختيار يُحفظ كتفضيل؛ لا توجد مهمة خلفية أو مزامنة Google Drive مهيأة في المشروع الحالي."));
        content.Add(_frequency);
        content.Add(Action("النسخ الاحتياطي", "إنشاء نسخة JSON من المحادثات والرسائل المحفوظة محليًا", "نسخ احتياطي الآن", ExportBackupAsync));
        content.Add(Toggle("النسخ الاحتياطي باستخدام الشبكة الخلوية", _cellular));
        content.Add(Toggle("تضمين الفيديوهات في الدردشة", _videos));
        content.Add(_status);
        Content = new ScrollView { Content = content };
    }

    private static View Section(string title, string subtitle)
    {
        var stack = new VerticalStackLayout { Spacing = 4, FlowDirection = FlowDirection.RightToLeft };
        stack.Add(new Label { Text = title, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#300653"), HorizontalTextAlignment = TextAlignment.End });
        stack.Add(new Label { Text = subtitle, FontSize = 11, TextColor = Muted, HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap });
        var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10, FlowDirection = FlowDirection.LeftToRight };
        row.Add(stack, 0, 0);
        row.Add(SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(title), 40, 22), 1, 0);
        return new Border { BackgroundColor = Color.FromArgb("#FFFFFF"), Stroke = Color.FromArgb("#D9C5F4"), StrokeThickness = 1, Padding = 14, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Content = row };
    }

    private static View Toggle(string title, Switch toggle)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12, FlowDirection = FlowDirection.LeftToRight };
        grid.Add(toggle, 0, 0);
        grid.Add(new Label { Text = title, FontSize = 13, TextColor = Color.FromArgb("#300653"), VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.End, FlowDirection = FlowDirection.RightToLeft }, 1, 0);
        grid.Add(SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(title), 36, 20), 2, 0);
        return grid;
    }

    private static View Action(string title, string subtitle, string buttonText, Func<Task> action)
    {
        var button = new Button { Text = buttonText, BackgroundColor = Color.FromArgb("#7D24EC"), TextColor = Colors.White, CornerRadius = 12, FontSize = 12 };
        button.Clicked += async (_, _) => await action();
        var labels = new VerticalStackLayout { Spacing = 3, FlowDirection = FlowDirection.RightToLeft };
        labels.Add(new Label { Text = title, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#300653"), HorizontalTextAlignment = TextAlignment.End });
        labels.Add(new Label { Text = subtitle, FontSize = 11, TextColor = Muted, HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap });
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10, FlowDirection = FlowDirection.LeftToRight };
        grid.Add(button, 0, 0); grid.Add(labels, 1, 0);
        grid.Add(SettingsVisuals.IconBadge(SettingsVisuals.IconForSetting(title), 40, 22), 2, 0);
        return new Border { BackgroundColor = Color.FromArgb("#FFFFFF"), Stroke = Color.FromArgb("#D9C5F4"), StrokeThickness = 1, Padding = 14, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Content = grid };
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
            var path = Path.Combine(FileSystem.CacheDirectory, $"HimoChatBackup_{DateTime.Now:yyyyMMdd_HHmmss}.json");
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
