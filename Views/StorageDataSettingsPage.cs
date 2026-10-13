using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using Himo.Models;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
namespace Himo.Views;

/// <summary>
/// Storage and data usage screen. File management is deliberately restricted to
/// app-owned cache/media files; the message database and account/session stores
/// are never offered for deletion from this page.
/// </summary>
public sealed class StorageDataSettingsPage : ContentPage
{
    private const string RxBaselineKey = "himo_data_usage_uid_rx_baseline_v1";
    private const string TxBaselineKey = "himo_data_usage_uid_tx_baseline_v1";
    private const string ResetTicksKey = "himo_data_usage_reset_utc_ticks_v1";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".heif", ".avif" };
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mov", ".m4v", ".mkv", ".webm", ".3gp", ".avi", ".mpeg", ".mpg" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".aac", ".wav", ".ogg", ".opus", ".amr", ".3ga", ".flac" };
    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".zip", ".rar", ".7z", ".json" };

    private readonly ChatService _chat;
    private readonly CallHistoryService _callHistory;
    private readonly VerticalStackLayout _fileRows = new() { Spacing = 7 };
    private readonly VerticalStackLayout _conversationRows = new() { Spacing = 7 };
    private readonly Dictionary<string, Button> _filterButtonMap = new(StringComparer.Ordinal);
    private readonly Label _totalStorageValue = StatValueLabel("—");
    private readonly Label _mediaStorageValue = StatValueLabel("—");
    private readonly Label _fileCountValue = StatValueLabel("—");
    private readonly Label _receivedValue = StatValueLabel("جارٍ القراءة");
    private readonly Label _sentValue = StatValueLabel("جارٍ القراءة");
    private readonly Label _callUsageValue = StatValueLabel("—");
    private readonly Label _mediaUsageValue = StatValueLabel("—");
    private readonly Label _storageUsageValue = StatValueLabel("—");
    private readonly Label _messageUsageValue = StatValueLabel("—");
    private readonly Label _statusUsageValue = StatValueLabel("—");
    private readonly Label _roamingUsageValue = StatValueLabel("—");
    private readonly Label _filesEmptyLabel = BodyLabel("لا توجد ملفات محلية متاحة للعرض.");
    private readonly Label _conversationEmptyLabel = BodyLabel("لا توجد محادثات محفوظة محليًا.");
    private string _selectedFilter = "الكل";
    private IReadOnlyList<StorageFileEntry> _fileSnapshot = Array.Empty<StorageFileEntry>();

    public StorageDataSettingsPage(ChatService chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _callHistory = new CallHistoryService();
        Title = "التخزين والبيانات";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#07122E");
        Shell.SetNavBarIsVisible(this, false);
        Content = BuildLayout();
        EnsureInitialUsageBaseline();
        RefreshScreen();
    }

    private View BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            BackgroundColor = Color.FromArgb("#07122E")
        };
        root.Add(BuildHeader(), 0, 0);

        var content = new VerticalStackLayout { Padding = new Thickness(16, 8, 16, 24), Spacing = 13 };
        content.Add(BuildSummaryCard());
        content.Add(BuildStorageManagementCard());
        content.Add(BuildConversationsCard());
        content.Add(BuildNetworkUsageCard());

        var reset = new Button
        {
            Text = "إعادة تعيين الإحصائيات",
            HeightRequest = 48,
            CornerRadius = 15,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            BackgroundColor = Color.FromArgb("#342461"),
            TextColor = Color.FromArgb("#F4EEFF"),
            BorderColor = Color.FromArgb("#7152BC"),
            BorderWidth = 1,
            Margin = new Thickness(0, 4, 0, 0)
        };
        reset.Clicked += ResetStatisticsClicked;
        content.Add(BodyLabel("إحصائيات الشبكة تعتمد على عدّاد التطبيق في Android منذ آخر إعادة تعيين. لا يمكن فصل بيانات التجوال أو حساب استهلاك الحالات بدقة في الإصدار الحالي."));
        content.Add(reset);

        root.Add(new ScrollView
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = content
        }, 0, 1);
        return root;
    }

    private View BuildHeader()
    {
        var header = new Grid
        {
            Padding = new Thickness(18, 12, 18, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 48 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 48 }
            },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.LeftToRight
        };

        var back = new Button
        {
            Text = "‹", WidthRequest = 42, HeightRequest = 42, Padding = 0,
            FontSize = 34, FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#1B2850"), TextColor = Color.FromArgb("#E7ECFF"),
            BorderColor = Color.FromArgb("#334A82"), BorderWidth = 1, CornerRadius = 15,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center
        };
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var labels = new VerticalStackLayout
        {
            Spacing = 1, HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft
        };
        labels.Add(new Label
        {
            Text = "التخزين والبيانات", FontSize = 23, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.End
        });
        labels.Add(new Label
        {
            Text = "إدارة الملفات ومراجعة استخدام الشبكة", FontSize = 11.5,
            TextColor = Color.FromArgb("#A6B9E9"), HorizontalTextAlignment = TextAlignment.End
        });
        header.Add(labels, 1, 0);
        header.Add(new BoxView { WidthRequest = 42, HeightRequest = 42, Opacity = 0 }, 2, 0);
        return header;
    }

    private View BuildSummaryCard()
    {
        var cardContent = new VerticalStackLayout { Spacing = 10 };
        cardContent.Add(SectionTitle("ملخص التخزين"));
        cardContent.Add(StatRow("إجمالي مساحة التطبيق", _totalStorageValue, "▤"));
        cardContent.Add(ThinDivider());
        cardContent.Add(StatRow("الوسائط والملفات المؤقتة", _mediaStorageValue, "▧"));
        cardContent.Add(ThinDivider());
        cardContent.Add(StatRow("عدد الملفات المحلية", _fileCountValue, "▣"));
        return Card(cardContent, Color.FromArgb("#171E4D"), Color.FromArgb("#5549B4"));
    }

    private View BuildStorageManagementCard()
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Add(SectionTitle("إدارة التخزين"));
        content.Add(BodyLabel("تصفح الصور والفيديو والصوت والمستندات المخزنة مؤقتًا داخل التطبيق. حذف نسخة مؤقتة لا يحذف الرسالة من الطرف الآخر."));

        var filters = new HorizontalStackLayout { Spacing = 6, FlowDirection = FlowDirection.RightToLeft };
        foreach (var filter in new[] { "الكل", "صور", "فيديو", "صوت", "ملفات" })
        {
            var button = new Button
            {
                Text = filter, FontSize = 11.5, Padding = new Thickness(11, 0),
                HeightRequest = 34, CornerRadius = 12, BorderWidth = 1,
                BackgroundColor = Color.FromArgb(filter == _selectedFilter ? "#6744B8" : "#17264A"),
                BorderColor = Color.FromArgb(filter == _selectedFilter ? "#9A80E6" : "#304574"),
                TextColor = Color.FromArgb("#EEF2FF")
            };
            _filterButtonMap[filter] = button;
            button.Clicked += (_, _) => SelectFilter(filter);
            filters.Add(button);
        }
        var filtersScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = filters, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
        content.Add(filtersScroll);
        content.Add(_filesEmptyLabel);
        content.Add(_fileRows);

        var clearCache = new Button
        {
            Text = "مسح الوسائط المؤقتة", HeightRequest = 42, CornerRadius = 13,
            FontSize = 12, FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#24335F"), TextColor = Color.FromArgb("#DDE6FF"),
            BorderColor = Color.FromArgb("#405A91"), BorderWidth = 1
        };
        clearCache.Clicked += ClearCacheClicked;
        content.Add(clearCache);
        content.Add(BodyLabel("ملفات الإرسال المعلّقة محمية ولا تُحذف من زر مسح الذاكرة المؤقتة."));
        return Card(content);
    }

    private View BuildConversationsCard()
    {
        var content = new VerticalStackLayout { Spacing = 9 };
        content.Add(SectionTitle("قائمة المحادثات"));
        content.Add(BodyLabel("تُعرض أعداد الرسائل والمرفقات التي يحتفظ بها التطبيق محليًا لكل محادثة."));
        content.Add(_conversationEmptyLabel);
        content.Add(_conversationRows);
        return Card(content);
    }

    private View BuildNetworkUsageCard()
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Add(SectionTitle("استخدام الشبكة"));
        content.Add(StatRow("البيانات المستلمة", _receivedValue, "↓"));
        content.Add(ThinDivider());
        content.Add(StatRow("البيانات المرسلة", _sentValue, "↑"));
        content.Add(ThinDivider());
        content.Add(StatRow("المكالمات", _callUsageValue, "☎"));
        content.Add(ThinDivider());
        content.Add(StatRow("الوسائط", _mediaUsageValue, "▧"));
        content.Add(ThinDivider());
        content.Add(StatRow("مساحة التخزين", _storageUsageValue, "▤"));
        content.Add(ThinDivider());
        content.Add(StatRow("الرسائل", _messageUsageValue, "✉"));
        content.Add(ThinDivider());
        content.Add(StatRow("الحالة", _statusUsageValue, "◉"));
        content.Add(ThinDivider());
        content.Add(StatRow("التجوال", _roamingUsageValue, "⇄"));
        return Card(content);
    }

    private static Border Card(View content, Color? background = null, Color? border = null) => new()
    {
        Background = new SolidColorBrush(background ?? Color.FromArgb("#101D3D")),
        Stroke = new SolidColorBrush(border ?? Color.FromArgb("#263B6B")),
        StrokeThickness = 1,
        Padding = 14,
        Content = content,
        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) }
    };

    private static Label SectionTitle(string text) => new()
    {
        Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#F0F3FF"), HorizontalTextAlignment = TextAlignment.End
    };

    private static Label BodyLabel(string text) => new()
    {
        Text = text, FontSize = 11.5, TextColor = Color.FromArgb("#9EAFDA"),
        HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap
    };

    private static Label StatValueLabel(string text) => new()
    {
        Text = text, FontSize = 11.5, FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#D9E4FF"), HorizontalTextAlignment = TextAlignment.Start,
        VerticalTextAlignment = TextAlignment.Center, WidthRequest = 142, MaximumWidthRequest = 142,
        LineBreakMode = LineBreakMode.WordWrap, MaxLines = 3
    };

    private static View ThinDivider() => new BoxView { HeightRequest = 1, Color = Color.FromArgb("#26365F"), Margin = new Thickness(0, 0, 0, 0) };

    private static Grid StatRow(string title, Label value, string glyph)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 9,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 34
        };
        var icon = new Border
        {
            WidthRequest = 31, HeightRequest = 31,
            Background = new SolidColorBrush(Color.FromArgb("#332D78")),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = new Label
            {
                Text = glyph, FontSize = 17, TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        row.Add(icon, 0, 0);
        row.Add(new Label
        {
            Text = title, FontSize = 12, TextColor = Color.FromArgb("#E6ECFF"),
            VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2
        }, 1, 0);
        row.Add(value, 2, 0);
        return row;
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        try
        {
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync();
            else
            {
                var shell = Shell.Current;
                if (shell is not null)
                    await shell.GoToAsync("..");
            }
        }
        catch { /* Back navigation is best-effort if the parent shell is closing. */ }
    }

    private void RefreshScreen()
    {
        RefreshFileList();
        RefreshStorageSummary();
        RefreshConversationList();
        RefreshNetworkStatistics();
        UpdateFilterButtonStyles();
    }

    private void RefreshFileList()
    {
        var entries = new List<StorageFileEntry>();
        var cacheRoot = SafeFullPath(FileSystem.Current.CacheDirectory);
        var outboxRoot = SafeFullPath(IOPath.Combine(FileSystem.Current.AppDataDirectory, "HimoOutbox"));
        AddDirectoryFiles(entries, cacheRoot, "ذاكرة مؤقتة", canDelete: true, includeAllFiles: true);
        AddDirectoryFiles(entries, outboxRoot, "في انتظار الإرسال", canDelete: false, includeAllFiles: true);
        AddConversationMediaFiles(entries);
        _fileSnapshot = entries
            .Where(x => !string.IsNullOrWhiteSpace(x.FullPath))
            .GroupBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.SizeBytes)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        RenderFileRows();
    }

    private static void AddDirectoryFiles(List<StorageFileEntry> output, string root, string source, bool canDelete, bool includeAllFiles)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var full = SafeFullPath(path);
                    var info = new FileInfo(full);
                    if (!info.Exists || info.Length < 0) continue;
                    var extension = info.Extension;
                    var category = GetFileCategory(extension);
                    if (!includeAllFiles && category == "أخرى") continue;
                    var deletable = canDelete && (IsMediaCategory(category) || info.Name.StartsWith("himo_", StringComparison.OrdinalIgnoreCase));
                    output.Add(new StorageFileEntry(full, info.Name, info.Length, category, source, deletable, info.LastWriteTimeUtc));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void EnsureInitialUsageBaseline()
    {
        if (Preferences.Default.Get(ResetTicksKey, 0L) <= 0)
            Preferences.Default.Set(ResetTicksKey, DateTimeOffset.UtcNow.UtcDateTime.Ticks);
#if ANDROID
        try
        {
            var uid = Android.OS.Process.MyUid();
            var rx = Android.Net.TrafficStats.GetUidRxBytes(uid);
            var tx = Android.Net.TrafficStats.GetUidTxBytes(uid);
            if (Preferences.Default.Get(RxBaselineKey, -1L) < 0 && rx >= 0)
                Preferences.Default.Set(RxBaselineKey, rx);
            if (Preferences.Default.Get(TxBaselineKey, -1L) < 0 && tx >= 0)
                Preferences.Default.Set(TxBaselineKey, tx);
        }
        catch { }
#endif
    }

    private void AddConversationMediaFiles(List<StorageFileEntry> output)
    {
        try
        {
            foreach (var conversation in _chat.Conversations.ToList())
            {
                foreach (var message in _chat.GetMessages(conversation.Id))
                {
                    var path = message.AttachmentLocalPath;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    var full = SafeFullPath(path);
                    if (output.Any(x => string.Equals(x.FullPath, full, StringComparison.OrdinalIgnoreCase))) continue;
                    try
                    {
                        var info = new FileInfo(full);
                        var category = GetFileCategory(info.Extension);
                        var canDelete = IsPathInside(full, FileSystem.Current.CacheDirectory) &&
                            (IsMediaCategory(category) || info.Name.StartsWith("himo_", StringComparison.OrdinalIgnoreCase));
                        output.Add(new StorageFileEntry(full, info.Name, info.Length, category, "مرفق محادثة", canDelete, info.LastWriteTimeUtc));
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private void RefreshStorageSummary()
    {
        var appDataBytes = GetDirectorySize(FileSystem.Current.AppDataDirectory);
        var cacheBytes = GetDirectorySize(FileSystem.Current.CacheDirectory);
        var mediaBytes = _fileSnapshot.Sum(x => x.SizeBytes);
        var total = FormatBytes(SaturatingAdd(appDataBytes, cacheBytes));
        _totalStorageValue.Text = total;
        _storageUsageValue.Text = total;
        _mediaStorageValue.Text = FormatBytes(mediaBytes);
        _fileCountValue.Text = _fileSnapshot.Count.ToString("N0", CultureInfo.CurrentCulture);
    }

    private void RefreshConversationList()
    {
        _conversationRows.Children.Clear();
        var conversations = _chat.Conversations.ToList();
        _conversationEmptyLabel.IsVisible = conversations.Count == 0;
        foreach (var conversation in conversations)
        {
            try
            {
                var messages = _chat.GetMessages(conversation.Id).ToList();
                var files = messages.Where(m => m.IsAttachment).ToList();
                long localBytes = 0;
                var localCount = 0;
                foreach (var message in files)
                {
                    var attachmentPath = message.AttachmentLocalPath;
                    if (string.IsNullOrWhiteSpace(attachmentPath)) continue;
                    try
                    {
                        var info = new FileInfo(attachmentPath);
                        if (info.Exists) { localBytes = SaturatingAdd(localBytes, info.Length); localCount++; }
                    }
                    catch { }
                }
                var details = $"{messages.Count:N0} رسالة محفوظة • {files.Count:N0} مرفق • {FormatBytes(localBytes)} محليًا";
                var conversationName = conversation.Name;
                var displayName = string.IsNullOrWhiteSpace(conversationName) ? "محادثة" : conversationName;
                var initial = conversation.Initial;
                var row = BuildConversationRow(displayName, details, initial);
                _conversationRows.Add(row);
            }
            catch { /* One malformed local conversation should not hide the remaining list. */ }
        }
    }

    private static View BuildConversationRow(string name, string details, string? initial)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 38 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 22 }
            },
            ColumnSpacing = 9,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Thickness(10, 8),
            BackgroundColor = Color.FromArgb("#152448")
        };
        var avatar = new Border
        {
            WidthRequest = 36, HeightRequest = 36,
            Background = new SolidColorBrush(Color.FromArgb("#4437A2")), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Content = new Label
            {
                Text = string.IsNullOrWhiteSpace(initial) ? (name.Length == 0 ? "H" : name[..1]) : initial,
                TextColor = Colors.White, FontSize = 15, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        row.Add(avatar, 0, 0);
        var labels = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        labels.Add(new Label { Text = name, FontSize = 12.5, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#EEF2FF"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.TailTruncation });
        labels.Add(new Label { Text = details, FontSize = 10, TextColor = Color.FromArgb("#9EAFDA"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap });
        row.Add(labels, 1, 0);
        row.Add(new Label { Text = "›", FontSize = 22, TextColor = Color.FromArgb("#91A9E7"), VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center }, 2, 0);
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb("#152448")),
            Stroke = new SolidColorBrush(Color.FromArgb("#293D69")), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }, Content = row
        };
        return border;
    }

    private void RefreshNetworkStatistics()
    {
        var resetUtc = GetResetTimeUtc();
        var traffic = GetAppTrafficSinceBaseline();
        _receivedValue.Text = traffic.HasValue ? FormatBytes(traffic.Value.Rx) : "غير متاح على هذا النظام";
        _sentValue.Text = traffic.HasValue ? FormatBytes(traffic.Value.Tx) : "غير متاح على هذا النظام";

        var calls = _callHistory.GetSnapshot()
            .Where(x => x.StartedAt >= resetUtc)
            .ToArray();
        var connected = calls.Where(x => x.ConnectedAt.HasValue)
            .Sum(x => Math.Max(0, ((x.EndedAt ?? DateTimeOffset.UtcNow) - x.ConnectedAt!.Value).TotalSeconds));
        _callUsageValue.Text = $"{calls.Length:N0} مكالمة • {FormatDuration(TimeSpan.FromSeconds(connected))}";

        var recentMedia = _fileSnapshot.Where(x => x.LastWriteUtc >= resetUtc.UtcDateTime && IsMediaCategory(x.Category)).ToArray();
        var mediaBytes = recentMedia.Sum(x => x.SizeBytes);
        _mediaUsageValue.Text = $"{recentMedia.Length:N0} ملف • {FormatBytes(mediaBytes)} محليًا منذ التصفير";

        var messageCounts = GetMessageCountsSince(resetUtc);
        _messageUsageValue.Text = $"{messageCounts.Total:N0} رسالة • وارد {messageCounts.Received:N0} / صادر {messageCounts.Sent:N0}";

        _statusUsageValue.Text = "لا توجد إحصائية حالات منفصلة في الإصدار الحالي";
        _roamingUsageValue.Text = "غير متاح منفصلًا؛ الإجمالي يشمل كل أنواع الاتصال";
    }

    private (long Total, long Received, long Sent) GetMessageCountsSince(DateTimeOffset sinceUtc)
    {
        long total = 0, received = 0, sent = 0;
        foreach (var conversation in _chat.Conversations.ToList())
        {
            try
            {
                foreach (var message in _chat.GetMessages(conversation.Id))
                {
                    DateTimeOffset when;
                    try { when = new DateTimeOffset(message.SentAt).ToUniversalTime(); }
                    catch { continue; }
                    if (when < sinceUtc) continue;
                    total++;
                    if (message.IsMine) sent++; else received++;
                }
            }
            catch { }
        }
        return (total, received, sent);
    }

    private (long Rx, long Tx)? GetAppTrafficSinceBaseline()
    {
#if ANDROID
        try
        {
            var uid = Android.OS.Process.MyUid();
            var rx = Android.Net.TrafficStats.GetUidRxBytes(uid);
            var tx = Android.Net.TrafficStats.GetUidTxBytes(uid);
            if (rx < 0 || tx < 0) return null;

            var rxBase = Preferences.Default.Get(RxBaselineKey, -1L);
            var txBase = Preferences.Default.Get(TxBaselineKey, -1L);
            if (rxBase < 0 || rx < rxBase) { rxBase = rx; Preferences.Default.Set(RxBaselineKey, rxBase); }
            if (txBase < 0 || tx < txBase) { txBase = tx; Preferences.Default.Set(TxBaselineKey, txBase); }
            return (Math.Max(0, rx - rxBase), Math.Max(0, tx - txBase));
        }
        catch { return null; }
#else
        return null;
#endif
    }

    private static DateTimeOffset GetResetTimeUtc()
    {
        var ticks = Preferences.Default.Get(ResetTicksKey, 0L);
        if (ticks <= 0) return DateTimeOffset.MinValue;
        try { return new DateTimeOffset(ticks, TimeSpan.Zero); }
        catch { return DateTimeOffset.MinValue; }
    }

    private async void ResetStatisticsClicked(object? sender, EventArgs e)
    {
        var accepted = await DisplayAlertAsync(
            "إعادة تعيين الإحصائيات",
            "سيبدأ حساب استخدام الشبكة والمكالمات والرسائل والوسائط من الصفر. لن تُحذف المحادثات أو الملفات.",
            "إعادة تعيين", "إلغاء");
        if (!accepted) return;

        var now = DateTimeOffset.UtcNow;
        Preferences.Default.Set(ResetTicksKey, now.UtcDateTime.Ticks);
#if ANDROID
        try
        {
            var uid = Android.OS.Process.MyUid();
            var rx = Android.Net.TrafficStats.GetUidRxBytes(uid);
            var tx = Android.Net.TrafficStats.GetUidTxBytes(uid);
            if (rx >= 0) Preferences.Default.Set(RxBaselineKey, rx);
            if (tx >= 0) Preferences.Default.Set(TxBaselineKey, tx);
        }
        catch { }
#endif
        RefreshNetworkStatistics();
        await DisplayAlertAsync("تمت إعادة التعيين", "تم تصفير الإحصائيات المعروضة. لم يتم حذف أي محتوى.", "موافق");
    }

    private void SelectFilter(string filter)
    {
        _selectedFilter = filter;
        UpdateFilterButtonStyles();
        RenderFileRows();
    }

    private void UpdateFilterButtonStyles()
    {
        foreach (var (name, button) in _filterButtonMap)
        {
            var selected = name == _selectedFilter;
            button.BackgroundColor = Color.FromArgb(selected ? "#6744B8" : "#17264A");
            button.BorderColor = Color.FromArgb(selected ? "#9A80E6" : "#304574");
        }
    }

    private void RenderFileRows()
    {
        _fileRows.Children.Clear();
        var filtered = _fileSnapshot.Where(entry => MatchesFilter(entry, _selectedFilter)).ToList();
        _filesEmptyLabel.IsVisible = filtered.Count == 0;
        if (filtered.Count == 0) return;

        foreach (var entry in filtered)
        {
            _fileRows.Add(BuildFileRow(entry));
        }
    }

    private static bool MatchesFilter(StorageFileEntry entry, string filter) => filter switch
    {
        "صور" => entry.Category == "صورة",
        "فيديو" => entry.Category == "فيديو",
        "صوت" => entry.Category == "صوت",
        "ملفات" => entry.Category is "مستند" or "أخرى",
        _ => true
    };

    private View BuildFileRow(StorageFileEntry entry)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 34 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(9, 8)
        };
        var glyph = entry.Category switch { "صورة" => "▧", "فيديو" => "▷", "صوت" => "♫", "مستند" => "▤", _ => "•" };
        grid.Add(new Border
        {
            WidthRequest = 32, HeightRequest = 32, Background = new SolidColorBrush(Color.FromArgb("#332D78")),
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = new Label { Text = glyph, FontSize = 17, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center }
        }, 0, 0);

        var labels = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        labels.Add(new Label { Text = entry.Name, FontSize = 11.5, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#EDF2FF"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
        labels.Add(new Label { Text = $"{entry.Category} • {FormatBytes(entry.SizeBytes)} • {entry.Source}", FontSize = 9.5, TextColor = Color.FromArgb("#9EAFDA"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 });
        grid.Add(labels, 1, 0);

        var actions = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var open = new Button
        {
            Text = "فتح", FontSize = 10, Padding = new Thickness(9, 0), HeightRequest = 31,
            CornerRadius = 10, BackgroundColor = Color.FromArgb("#243A72"), TextColor = Color.FromArgb("#E6ECFF")
        };
        open.Clicked += async (_, _) => await OpenFileAsync(entry);
        actions.Add(open);

        if (entry.CanDelete)
        {
            var delete = new Button
            {
                Text = "حذف", FontSize = 10, Padding = new Thickness(8, 0), HeightRequest = 31,
                CornerRadius = 10, BackgroundColor = Color.FromArgb("#4A1E43"), TextColor = Color.FromArgb("#FFE6F4")
            };
            delete.Clicked += async (_, _) => await DeleteFileAsync(entry);
            actions.Add(delete);
        }
        else
        {
            actions.Add(new Label { Text = "محمي", FontSize = 9.5, TextColor = Color.FromArgb("#CFBDFE"), VerticalTextAlignment = TextAlignment.Center });
        }
        grid.Add(actions, 2, 0);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb("#152448")),
            Stroke = new SolidColorBrush(Color.FromArgb("#293D69")), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }, Content = grid
        };
    }

    private async Task OpenFileAsync(StorageFileEntry entry)
    {
        try
        {
            if (!File.Exists(entry.FullPath))
            {
                await DisplayAlertAsync("الملف", "لم يعد الملف موجودًا في الذاكرة المحلية.", "موافق");
                RefreshScreen();
                return;
            }
            await Launcher.Default.OpenAsync(new OpenFileRequest(entry.Name, new ReadOnlyFile(entry.FullPath)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("فتح الملف", "تعذر فتح الملف على هذا الجهاز: " + ex.Message, "موافق");
        }
    }

    private async Task DeleteFileAsync(StorageFileEntry entry)
    {
        if (!entry.CanDelete || !IsPathInside(entry.FullPath, FileSystem.Current.CacheDirectory)) return;
        var accepted = await DisplayAlertAsync("حذف النسخة المؤقتة", $"هل تريد حذف النسخة المحلية من «{entry.Name}»؟ لن تُحذف الرسالة أو المرفق من المحادثة على الخادم.", "حذف", "إلغاء");
        if (!accepted) return;

        try
        {
            if (File.Exists(entry.FullPath)) File.Delete(entry.FullPath);
            RefreshScreen();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر الحذف", ex.Message, "موافق");
        }
    }

    private async void ClearCacheClicked(object? sender, EventArgs e)
    {
        var accepted = await DisplayAlertAsync(
            "مسح الوسائط المؤقتة",
            "سيتم حذف نسخ الملفات الموجودة في ذاكرة التخزين المؤقت فقط. لن تُحذف المحادثات أو ملفات الإرسال المعلّقة، وقد يعاد تنزيل الوسائط عند فتحها.",
            "مسح الذاكرة", "إلغاء");
        if (!accepted) return;

        var cacheRoot = SafeFullPath(FileSystem.Current.CacheDirectory);
        var deleted = 0;
        var failures = 0;
        foreach (var entry in _fileSnapshot.Where(x => x.CanDelete && IsPathInside(x.FullPath, cacheRoot)).ToArray())
        {
            try
            {
                if (File.Exists(entry.FullPath)) { File.Delete(entry.FullPath); deleted++; }
            }
            catch { failures++; }
        }
        RefreshScreen();
        var result = $"تم حذف {deleted:N0} ملفًا مؤقتًا." + (failures > 0 ? $" تعذر حذف {failures:N0} ملف." : string.Empty);
        await DisplayAlertAsync("مسح الذاكرة المؤقتة", result, "موافق");
    }

    private static string GetFileCategory(string extension)
    {
        if (ImageExtensions.Contains(extension)) return "صورة";
        if (VideoExtensions.Contains(extension)) return "فيديو";
        if (AudioExtensions.Contains(extension)) return "صوت";
        if (DocumentExtensions.Contains(extension)) return "مستند";
        return "أخرى";
    }

    private static bool IsMediaCategory(string category) => category is "صورة" or "فيديو" or "صوت" or "مستند";

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            if (!Directory.Exists(path)) return 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size = SaturatingAdd(size, new FileInfo(file).Length); }
                catch { }
            }
        }
        catch { }
        return size;
    }

    private static long SaturatingAdd(long left, long right) => right > 0 && left > long.MaxValue - right ? long.MaxValue : Math.Max(0, left) + Math.Max(0, right);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "غير متاح";
        string[] units = { "بايت", "KB", "MB", "GB", "TB" };
        double amount = bytes;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1) { amount /= 1024; unit++; }
        return unit == 0 ? $"{amount:N0} {units[unit]}" : $"{amount:0.##} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays} يوم {duration.Hours:00}:{duration.Minutes:00}";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}";
        return $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string SafeFullPath(string path)
    {
        try { return IOPath.GetFullPath(path); }
        catch { return path; }
    }

    private static bool IsPathInside(string filePath, string directoryPath)
    {
        try
        {
            var fullFile = SafeFullPath(filePath).TrimEnd(IOPath.DirectorySeparatorChar) + IOPath.DirectorySeparatorChar;
            var fullRoot = SafeFullPath(directoryPath).TrimEnd(IOPath.DirectorySeparatorChar) + IOPath.DirectorySeparatorChar;
            return fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private sealed record StorageFileEntry(
        string FullPath,
        string Name,
        long SizeBytes,
        string Category,
        string Source,
        bool CanDelete,
        DateTime LastWriteUtc);
}
