using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Threading.Tasks;
using Himo.Models;
using Himo.Services;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Separate network-usage details page. Counts and byte totals are deliberately
/// labelled by their source so unsupported per-category network counters are not
/// presented as if Android provided them.
/// </summary>
public sealed class NetworkUsageSettingsPage : ContentPage
{
    private const string RxBaselineKey = "himo_data_usage_uid_rx_baseline_v1";
    private const string TxBaselineKey = "himo_data_usage_uid_tx_baseline_v1";
    private const string ResetTicksKey = "himo_data_usage_reset_utc_ticks_v1";

    private readonly ChatService _chat;
    private readonly CallHistoryService _callHistory = new();
    private readonly Label _totalUsageValue = StatValueLabel("جارٍ القراءة");
    private readonly Label _receivedValue = StatValueLabel("جارٍ القراءة");
    private readonly Label _sentValue = StatValueLabel("جارٍ القراءة");
    private readonly Label _callUsageValue = StatValueLabel("—");
    private readonly Label _mediaUsageValue = StatValueLabel("—");
    private readonly Label _storageUsageValue = StatValueLabel("—");
    private readonly Label _messageUsageValue = StatValueLabel("—");
    private readonly Label _statusUsageValue = StatValueLabel("—");
    private readonly Label _roamingUsageValue = StatValueLabel("—");

    public NetworkUsageSettingsPage(ChatService chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Title = "استخدام الشبكة";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#07122E");
        Shell.SetNavBarIsVisible(this, false);
        EnsureInitialUsageBaseline();
        Content = BuildLayout();
        RefreshStatistics();
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

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(16, 8, 16, 24),
            Spacing = 13
        };
        content.Add(BuildTrafficSummaryCard());
        content.Add(BuildUsageBreakdownCard());
        content.Add(BodyLabel("البيانات المستلمة والمرسلة مأخوذة من عدّاد Android الخاص بالتطبيق منذ آخر إعادة تعيين. أحجام الوسائط والتخزين محسوبة من الملفات المحلية، وليست قياسًا دقيقًا لحركة الشبكة لكل فئة."));

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
            Spacing = 1,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        labels.Add(new Label
        {
            Text = "استخدام الشبكة", FontSize = 23, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.End
        });
        labels.Add(new Label
        {
            Text = "تفاصيل استهلاك البيانات والمكالمات والوسائط", FontSize = 11.5,
            TextColor = Color.FromArgb("#A6B9E9"), HorizontalTextAlignment = TextAlignment.End
        });
        header.Add(labels, 1, 0);
        header.Add(new BoxView { WidthRequest = 42, HeightRequest = 42, Opacity = 0 }, 2, 0);
        return header;
    }

    private View BuildTrafficSummaryCard()
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Add(SectionTitle("إجمالي استخدام الشبكة"));
        content.Add(_totalUsageValue);
        content.Add(ThinDivider());
        content.Add(StatRow("البيانات المستلمة", _receivedValue, "↓"));
        content.Add(ThinDivider());
        content.Add(StatRow("البيانات المرسلة", _sentValue, "↑"));
        return Card(content, Color.FromArgb("#171E4D"), Color.FromArgb("#5549B4"));
    }

    private View BuildUsageBreakdownCard()
    {
        var content = new VerticalStackLayout { Spacing = 10 };
        content.Add(SectionTitle("تفاصيل الاستخدام"));
        content.Add(StatRow("المكالمات", _callUsageValue, "☎"));
        content.Add(ThinDivider());
        content.Add(StatRow("الوسائط المحلية", _mediaUsageValue, "▧"));
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

    private void RefreshStatistics()
    {
        var resetUtc = GetResetTimeUtc();
        var traffic = GetAppTrafficSinceBaseline();
        if (traffic.HasValue)
        {
            _receivedValue.Text = FormatBytes(traffic.Value.Rx);
            _sentValue.Text = FormatBytes(traffic.Value.Tx);
            _totalUsageValue.Text = FormatBytes(SaturatingAdd(traffic.Value.Rx, traffic.Value.Tx));
        }
        else
        {
            _receivedValue.Text = "غير متاح على هذا النظام";
            _sentValue.Text = "غير متاح على هذا النظام";
            _totalUsageValue.Text = "غير متاح على هذا النظام";
        }

        var calls = _callHistory.GetSnapshot()
            .Where(x => x.StartedAt >= resetUtc)
            .ToArray();
        var connectedSeconds = calls
            .Where(x => x.ConnectedAt.HasValue)
            .Sum(x => Math.Max(0, ((x.EndedAt ?? DateTimeOffset.UtcNow) - x.ConnectedAt!.Value).TotalSeconds));
        _callUsageValue.Text = $"{calls.Length:N0} مكالمة • {FormatDuration(TimeSpan.FromSeconds(connectedSeconds))}";

        var media = GetLocalMediaSnapshot(resetUtc);
        _mediaUsageValue.Text = $"{media.Count:N0} ملف • {FormatBytes(media.Bytes)} منذ التصفير";

        var appData = GetDirectorySize(FileSystem.Current.AppDataDirectory);
        var cache = GetDirectorySize(FileSystem.Current.CacheDirectory);
        _storageUsageValue.Text = FormatBytes(SaturatingAdd(appData, cache));

        var counts = GetMessageCountsSince(resetUtc);
        _messageUsageValue.Text = $"{counts.Total:N0} رسالة • وارد {counts.Received:N0} / صادر {counts.Sent:N0}";
        _statusUsageValue.Text = "لا توجد إحصائية حالات منفصلة في الإصدار الحالي";
        _roamingUsageValue.Text = "غير متاح منفصلًا؛ إجمالي حركة التطبيق يشمل الاتصال";
    }

    private (int Count, long Bytes) GetLocalMediaSnapshot(DateTimeOffset sinceUtc)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long bytes = 0;
        foreach (var conversation in _chat.Conversations.ToList())
        {
            try
            {
                foreach (var message in _chat.GetMessages(conversation.Id))
                {
                    var localPath = message.AttachmentLocalPath;
                    if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                        continue;
                    string fullPath;
                    try { fullPath = IOPath.GetFullPath(localPath); }
                    catch { fullPath = localPath; }
                    if (paths.Contains(fullPath))
                        continue;
                    try
                    {
                        var info = new FileInfo(fullPath);
                        if (info.LastWriteTimeUtc < sinceUtc.UtcDateTime)
                            continue;
                        paths.Add(fullPath);
                        bytes = SaturatingAdd(bytes, info.Length);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch { }
        }
        return (paths.Count, bytes);
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
            if (rxBase < 0 || rx < rxBase)
            {
                rxBase = rx;
                Preferences.Default.Set(RxBaselineKey, rxBase);
            }
            if (txBase < 0 || tx < txBase)
            {
                txBase = tx;
                Preferences.Default.Set(TxBaselineKey, txBase);
            }
            return (Math.Max(0, rx - rxBase), Math.Max(0, tx - txBase));
        }
        catch { return null; }
#else
        return null;
#endif
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

    private async void ResetStatisticsClicked(object? sender, EventArgs e)
    {
        var accepted = await DisplayAlertAsync(
            "إعادة تعيين الإحصائيات",
            "سيبدأ حساب استخدام الشبكة والمكالمات والرسائل والوسائط من الصفر. لن تُحذف المحادثات أو الملفات.",
            "إعادة تعيين", "إلغاء");
        if (!accepted) return;

        Preferences.Default.Set(ResetTicksKey, DateTimeOffset.UtcNow.UtcDateTime.Ticks);
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
        RefreshStatistics();
        await DisplayAlertAsync("تمت إعادة التعيين", "تم تصفير الإحصائيات المعروضة. لم يتم حذف أي محتوى.", "موافق");
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
        catch { }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshStatistics();
    }

    private static DateTimeOffset GetResetTimeUtc()
    {
        var ticks = Preferences.Default.Get(ResetTicksKey, 0L);
        if (ticks <= 0) return DateTimeOffset.MinValue;
        try { return new DateTimeOffset(ticks, TimeSpan.Zero); }
        catch { return DateTimeOffset.MinValue; }
    }

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

    private static long SaturatingAdd(long left, long right)
        => right > 0 && left > long.MaxValue - right ? long.MaxValue : Math.Max(0, left) + Math.Max(0, right);

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
        VerticalTextAlignment = TextAlignment.Center, WidthRequest = 150, MaximumWidthRequest = 150,
        LineBreakMode = LineBreakMode.WordWrap, MaxLines = 4
    };

    private static View ThinDivider() => new BoxView
    {
        HeightRequest = 1, Color = Color.FromArgb("#26365F"), Margin = new Thickness(0)
    };

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
            MinimumHeightRequest = 36
        };
        row.Add(new Border
        {
            WidthRequest = 31,
            HeightRequest = 31,
            Background = new SolidColorBrush(Color.FromArgb("#332D78")),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = new Label
            {
                Text = glyph, FontSize = 17, TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        }, 0, 0);
        row.Add(new Label
        {
            Text = title, FontSize = 12, TextColor = Color.FromArgb("#E6ECFF"),
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 2
        }, 1, 0);
        row.Add(value, 2, 0);
        return row;
    }
}
