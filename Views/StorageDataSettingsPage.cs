using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Linq;
using System.Threading;
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
public sealed class StorageDataSettingsPage : Himo.Services.LocalizedContentPage
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
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly ActivityIndicator _refreshIndicator = new()
    {
        Color = Color.FromArgb("#5636A5"),
        IsVisible = false,
        IsRunning = false,
        HorizontalOptions = LayoutOptions.Center,
        HeightRequest = 22,
        WidthRequest = 22
    };
    private readonly Label _totalStorageValue = StatValueLabel("—");
    private readonly Label _cacheStorageValue = StatValueLabel("—");
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
    private readonly Label _filesEmptyLabel = new() { Text = "لا توجد ملفات محلية متاحة للعرض.", FontSize = 11, TextColor = Color.FromArgb("#756D83"), HorizontalTextAlignment = TextAlignment.Start, FlowDirection = FlowDirection.RightToLeft, LineBreakMode = LineBreakMode.WordWrap, IsVisible = false };
    private readonly Label _conversationEmptyLabel = BodyLabel("لا توجد محادثات محفوظة محليًا.");
    private string _selectedFilter = "الكل";
    private IReadOnlyList<StorageFileEntry> _fileSnapshot = Array.Empty<StorageFileEntry>();

    public StorageDataSettingsPage(ChatService chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        _callHistory = new CallHistoryService();
        Title = "التخزين والبيانات";
        FlowDirection = FlowDirection.RightToLeft;
        BackgroundColor = Color.FromArgb("#211333");
        SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        Shell.SetNavBarIsVisible(this, false);
        Content = BuildLayout();
        EnsureInitialUsageBaseline();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        HimoAppearanceManager.ApplyToTree(this);
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
            BackgroundColor = Color.FromArgb("#211333"),
            FlowDirection = FlowDirection.LeftToRight
        };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        root.Add(BuildHeader(), 0, 0);

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(16, 12, 16, 28),
            Spacing = 15,
            FlowDirection = FlowDirection.RightToLeft,
            BackgroundColor = Color.FromArgb("#211333")
        };
        content.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        content.Add(_refreshIndicator);
        content.Add(BuildSummaryCard());
        content.Add(BuildCacheActionsCard());
        content.Add(BuildNetworkUsageCard());

        var usageNote = new Border
        {
            Padding = new Thickness(13, 12),
            BackgroundColor = Color.FromArgb("#39204F"),
            Stroke = new SolidColorBrush(Color.FromArgb("#594768")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(15) },
            Content = BodyLabel("تعرض هذه الصفحة إجماليات الاستهلاك فقط، ولا تعرض أسماء الملفات أو محتوياتها.")
        };
        usageNote.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        usageNote.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        content.Add(usageNote);

        var reset = new Button
        {
            Text = "إعادة تعيين الإحصائيات",
            HeightRequest = 46,
            CornerRadius = 14,
            FontAttributes = FontAttributes.Bold,
            FontSize = 14,
            BackgroundColor = Color.FromArgb("#C76BFF"),
            TextColor = Colors.White,
            BorderColor = Color.FromArgb("#C76BFF"),
            BorderWidth = 1,
            Margin = new Thickness(0, 0, 0, 2)
        };
        reset.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        reset.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        reset.SetDynamicResource(Button.BorderColorProperty, "HimoAccent");
        reset.Clicked += ResetStatisticsClicked;
        content.Add(reset);

        var scroll = new ScrollView
        {
            BackgroundColor = Color.FromArgb("#211333"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Padding = new Thickness(0),
            Content = content
        };
        scroll.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        root.Add(scroll, 0, 1);
        return root;
    }

    private View BuildHeader()
    {
        // Keep physical columns explicit so the back control and title do not drift on RTL devices.
        var header = new Grid
        {
            Padding = new Thickness(18, 8, 18, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = 44 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 44 }
            },
            ColumnSpacing = 9,
            MinimumHeightRequest = 78,
            FlowDirection = FlowDirection.LeftToRight
        };

        var back = new Button
        {
            Text = string.Empty,
            ImageSource = ImageSource.FromFile("himo_s5_icon_back.png"),
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 9,
            BackgroundColor = Color.FromArgb("#F0ECFA"),
            BorderColor = Color.FromArgb("#DED7EF"),
            BorderWidth = 1,
            CornerRadius = 14,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center
        };
        back.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        back.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);

        var labels = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        var title = new Label
        {
            Text = "التخزين والبيانات", FontSize = 22, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#F7E9FF"), HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        labels.Add(title);
        var subtitle = new Label
        {
            Text = "ملخص التخزين واستخدام الشبكة", FontSize = 11.5,
            TextColor = Color.FromArgb("#C7B8D8"), HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        labels.Add(subtitle);
        header.Add(labels, 1, 0);
        header.Add(CreateIconBadge("storage", 42), 2, 0);
        return header;
    }

    private View BuildSummaryCard()
    {
        var cardContent = new VerticalStackLayout { Spacing = 10, FlowDirection = FlowDirection.RightToLeft };
        cardContent.Add(SectionTitle("استهلاك التخزين"));
        cardContent.Add(StatRow("الذاكرة المؤقتة المستخدمة", _cacheStorageValue, "cache"));
        cardContent.Add(ThinDivider());
        cardContent.Add(StatRow("إجمالي بيانات التطبيق", _totalStorageValue, "storage"));
        cardContent.Add(ThinDivider());
        cardContent.Add(StatRow("حجم الوسائط المخزنة", _mediaStorageValue, "media"));
        return Card(cardContent);
    }

    private View BuildCacheActionsCard()
    {
        var content = new VerticalStackLayout { Spacing = 9, FlowDirection = FlowDirection.RightToLeft };
        content.Add(BuildSectionHeader("الذاكرة المؤقتة", "cache"));
        content.Add(BodyLabel("يمكنك مسح النسخ المؤقتة لتوفير مساحة. لن يتم حذف المحادثات من الخادم."));
        var clearCache = new Button
        {
            Text = "مسح الذاكرة المؤقتة",
            HeightRequest = 44,
            CornerRadius = 13,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#39204F"),
            TextColor = Color.FromArgb("#F7E9FF"),
            BorderColor = Color.FromArgb("#594768"),
            BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Fill
        };
        clearCache.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSoft");
        clearCache.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        clearCache.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        clearCache.Clicked += ClearCacheClicked;
        content.Add(clearCache);
        return Card(content);
    }

    private View BuildStorageManagementCard()
    {
        var content = new VerticalStackLayout
        {
            Spacing = 12,
            FlowDirection = FlowDirection.RightToLeft
        };
        content.Add(BuildSectionHeader("الملفات المحلية", "archive"));
        content.Add(BodyLabel("استعراض الملفات الموجودة في الذاكرة المؤقتة ومرفقات المحادثات. لا تُحذف الرسائل من الخادم عند إزالة نسخة محلية."));
        content.Add(BuildFilterBar());
        content.Add(_filesEmptyLabel);
        content.Add(_fileRows);

        var clearCache = new Button
        {
            Text = "مسح الملفات المؤقتة",
            HeightRequest = 44,
            CornerRadius = 13,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#F1EDFA"),
            TextColor = Color.FromArgb("#4D3586"),
            BorderColor = Color.FromArgb("#DDD5EF"),
            BorderWidth = 1,
            HorizontalOptions = LayoutOptions.Fill
        };
        clearCache.Clicked += ClearCacheClicked;
        content.Add(clearCache);
        return Card(content);
    }

    private View BuildFilterBar()
    {
        var filters = new HorizontalStackLayout
        {
            Spacing = 7,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Thickness(0, 2)
        };

        foreach (var filter in new[] { "الكل", "صور", "فيديو", "صوت", "ملفات" })
        {
            var button = new Button
            {
                Text = filter,
                HeightRequest = 36,
                MinimumWidthRequest = 54,
                Padding = new Thickness(12, 0),
                CornerRadius = 12,
                FontSize = 11.5,
                FontAttributes = FontAttributes.Bold,
                BorderWidth = 1,
                BorderColor = Color.FromArgb("#E1DAF0"),
                BackgroundColor = Color.FromArgb("#F5F2FB"),
                TextColor = Color.FromArgb("#5A526C")
            };
            button.Clicked += (_, _) => SelectFilter(filter);
            _filterButtonMap[filter] = button;
            filters.Children.Add(button);
        }

        UpdateFilterButtonStyles();
        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = filters
        };
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
        var content = new VerticalStackLayout
        {
            Spacing = 8,
            FlowDirection = FlowDirection.RightToLeft
        };
        content.Add(BuildSectionHeader("استخدام الشبكة", "transfer"));
        content.Add(StatRow("البيانات المستلمة", _receivedValue, "download"));
        content.Add(ThinDivider());
        content.Add(StatRow("البيانات المرسلة", _sentValue, "upload"));
        content.Add(ThinDivider());
        content.Add(StatRow("المكالمات", _callUsageValue, "call"));
        content.Add(ThinDivider());
        content.Add(StatRow("الوسائط", _mediaUsageValue, "media"));
        content.Add(ThinDivider());
        content.Add(StatRow("مساحة التخزين", _storageUsageValue, "storage"));
        content.Add(ThinDivider());
        content.Add(StatRow("الرسائل", _messageUsageValue, "message"));
        content.Add(ThinDivider());
        content.Add(StatRow("الحالة", _statusUsageValue, "status"));
        content.Add(ThinDivider());
        content.Add(StatRow("التجوال", _roamingUsageValue, "roaming"));
        return Card(content);
    }

    private static Border Card(View content, Color? background = null, Color? border = null)
    {
        var card = new Border
        {
            BackgroundColor = background ?? Color.FromArgb("#2D1942"),
            Stroke = new SolidColorBrush(border ?? Color.FromArgb("#594768")),
            StrokeThickness = 1,
            Padding = 14,
            Content = content,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#100818")),
                Offset = new Point(0, 1), Radius = 5, Opacity = 0.10f
            }
        };
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    private static Label SectionTitle(string text)
    {
        var label = new Label
        {
            Text = text, FontSize = 14, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#F7E9FF"), HorizontalTextAlignment = TextAlignment.Start,
            FlowDirection = FlowDirection.RightToLeft
        };
        label.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        return label;
    }

    private static Label BodyLabel(string text)
    {
        var label = new Label
        {
            Text = text, FontSize = 11, TextColor = Color.FromArgb("#C7B8D8"),
            HorizontalTextAlignment = TextAlignment.Start, FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        return label;
    }

    private static Label StatValueLabel(string text)
    {
        var label = new Label
        {
            Text = text, FontSize = 11.5, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#C76BFF"), HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center, WidthRequest = 108, MaximumWidthRequest = 108,
            FlowDirection = FlowDirection.LeftToRight, LineBreakMode = LineBreakMode.WordWrap, MaxLines = 3
        };
        label.SetDynamicResource(Label.TextColorProperty, "HimoAccent");
        return label;
    }

    private static string IconFileForName(string iconName) => iconName switch
    {
        "transfer" => "himo_s5_icon_network.png",
        "download" => "himo_s5_icon_download.png",
        "upload" => "himo_s5_icon_upload.png",
        "call" => "himo_s5_icon_call.png",
        "media" => "himo_s5_icon_media.png",
        "message" => "himo_s5_icon_message.png",
        "sound" => "himo_s5_icon_sound.png",
        "status" => "himo_s5_icon_status.png",
        "roaming" => "himo_s5_icon_roaming.png",
        "archive" => "himo_s5_icon_archive.png",
        "back" => "himo_s5_icon_back.png",
        "cache" => "himo_s5_icon_cache.png",
        _ => "himo_s5_icon_storage.png"
    };

    private static Border CreateIconBadge(string iconName, double size = 34)
    {
        var badge = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            BackgroundColor = Color.FromArgb("#39204F"),
            Stroke = new SolidColorBrush(Color.FromArgb("#594768")),
            StrokeThickness = 0.8,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(size * 0.28) },
            Content = new Image
            {
                Source = ImageSource.FromFile(IconFileForName(iconName)),
                WidthRequest = size * 0.56,
                HeightRequest = size * 0.56,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        badge.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoIconBackground");
        badge.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return badge;
    }

    private static Grid BuildSectionHeader(string title, string iconName)
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 9,
            MinimumHeightRequest = 38,
            FlowDirection = FlowDirection.LeftToRight
        };
        var titleLabel = new Label
        {
            Text = title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#F7E9FF"),
            HorizontalTextAlignment = TextAlignment.Start,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0),
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        row.Add(titleLabel, 0, 0);
        row.Add(CreateIconBadge(iconName, 36), 1, 0);
        return row;
    }

    private static View ThinDivider()
    {
        var divider = new BoxView { HeightRequest = 1, Color = Color.FromArgb("#594768"), Margin = new Thickness(0, 1, 0, 1) };
        divider.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
        return divider;
    }

    private static Grid StatRow(string title, Label value, string iconName)
    {
        // Pin the physical order explicitly. MAUI Grid columns follow their
        // declared order; FlowDirection does not mirror the column definitions.
        // Left-to-right physical columns: value (left), Arabic label (middle), icon (right).
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(108) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(32) }
            },
            ColumnSpacing = 8,
            FlowDirection = FlowDirection.LeftToRight,
            MinimumHeightRequest = 49,
            Padding = new Thickness(0, 2),
            HorizontalOptions = LayoutOptions.Fill
        };

        value.WidthRequest = 108;
        value.MaximumWidthRequest = 108;
        value.HorizontalOptions = LayoutOptions.Fill;
        // Values belong in the physical left column. Render their digits left-to-right
        // so Latin digits (0-9), decimal points, units, and durations never reorder.
        value.HorizontalTextAlignment = TextAlignment.Start;
        value.VerticalTextAlignment = TextAlignment.Center;
        value.VerticalOptions = LayoutOptions.Center;
        value.FlowDirection = FlowDirection.LeftToRight;
        value.LineBreakMode = LineBreakMode.WordWrap;
        value.MaxLines = 3;
        row.Add(value, 0, 0);

        var titleLabel = new Label
        {
            Text = title,
            FontSize = 12,
            TextColor = Color.FromArgb("#F7E9FF"),
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Start,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(0),
            FlowDirection = FlowDirection.RightToLeft,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 2
        };
        titleLabel.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        row.Add(titleLabel, 1, 0);

        row.Add(CreateIconBadge(iconName, 32), 2, 0);
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

    private void RefreshScreen() => _ = RefreshScreenAsync();

    private async Task RefreshScreenAsync()
    {
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _refreshIndicator.IsVisible = true;
                _refreshIndicator.IsRunning = true;
            }).ConfigureAwait(false);

            // Directory enumeration and message counting can be expensive on devices
            // with large caches. Keep the UI thread free while collecting a snapshot.
            var snapshot = await Task.Run(BuildScreenSnapshot).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _fileSnapshot = snapshot.Files;
                var total = SaturatingAdd(snapshot.AppDataBytes, snapshot.CacheBytes);
                _cacheStorageValue.Text = FormatGigabytes(snapshot.CacheBytes);
                _totalStorageValue.Text = FormatBytes(total);
                _mediaStorageValue.Text = FormatBytes(snapshot.MediaBytes);

                _receivedValue.Text = snapshot.Network.Received;
                _sentValue.Text = snapshot.Network.Sent;
                _callUsageValue.Text = snapshot.Network.Calls;
                _mediaUsageValue.Text = snapshot.Network.Media;
                _storageUsageValue.Text = FormatBytes(total);
                _messageUsageValue.Text = snapshot.Network.Messages;
                _statusUsageValue.Text = snapshot.Network.Status;
                _roamingUsageValue.Text = snapshot.Network.Roaming;

            }).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _cacheStorageValue.Text = "تعذر القراءة";
                _totalStorageValue.Text = "تعذر القراءة";
                _mediaStorageValue.Text = "تعذر القراءة";
            }).ConfigureAwait(false);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _refreshIndicator.IsRunning = false;
                _refreshIndicator.IsVisible = false;
            }).ConfigureAwait(false);
            _refreshGate.Release();
        }
    }

    private ScreenSnapshot BuildScreenSnapshot()
    {
        var files = CollectFileSnapshot();
        var appDataBytes = GetDirectorySize(FileSystem.Current.AppDataDirectory);
        var cacheBytes = GetDirectorySize(FileSystem.Current.CacheDirectory);
        var mediaBytes = files.Where(x => IsMediaCategory(x.Category)).Sum(x => x.SizeBytes);
        var network = CalculateNetworkStatistics(GetResetTimeUtc(), files);
        return new ScreenSnapshot(files, appDataBytes, cacheBytes, mediaBytes, network);
    }

    private IReadOnlyList<StorageFileEntry> CollectFileSnapshot()
    {
        var entries = new List<StorageFileEntry>();
        var cacheRoot = SafeFullPath(FileSystem.Current.CacheDirectory);
        var outboxRoot = SafeFullPath(IOPath.Combine(FileSystem.Current.AppDataDirectory, "HimoOutbox"));
        AddDirectoryFiles(entries, cacheRoot, "ذاكرة مؤقتة", canDelete: true, includeAllFiles: true);
        AddDirectoryFiles(entries, outboxRoot, "في انتظار الإرسال", canDelete: false, includeAllFiles: true);
        AddConversationMediaFiles(entries);
        return entries
            .Where(x => !string.IsNullOrWhiteSpace(x.FullPath))
            .GroupBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(x => x.SizeBytes)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
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
                var details = $"{messages.Count.ToString("N0", CultureInfo.InvariantCulture)} رسالة محفوظة • {files.Count.ToString("N0", CultureInfo.InvariantCulture)} مرفق • {FormatBytes(localBytes)} محليًا";
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
            BackgroundColor = Color.FromArgb("#2D1942")
        };
        var avatar = new Border
        {
            WidthRequest = 36, HeightRequest = 36,
            Background = new SolidColorBrush(Color.FromArgb("#EDE8F8")), StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
            Content = new Label
            {
                Text = string.IsNullOrWhiteSpace(initial) ? (name.Length == 0 ? "H" : name[..1]) : initial,
                TextColor = Color.FromArgb("#4D3586"), FontSize = 15, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        row.Add(avatar, 0, 0);
        var labels = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        labels.Add(new Label { Text = name, FontSize = 12.5, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2C2342"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.TailTruncation });
        labels.Add(new Label { Text = details, FontSize = 10, TextColor = Color.FromArgb("#7B748D"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.WordWrap });
        row.Add(labels, 1, 0);
        row.Add(new Label { Text = "›", FontSize = 22, TextColor = Color.FromArgb("#71628E"), VerticalTextAlignment = TextAlignment.Center, HorizontalTextAlignment = TextAlignment.Center }, 2, 0);
        var border = new Border
        {
            BackgroundColor = Color.FromArgb("#2D1942"),
            Stroke = new SolidColorBrush(Color.FromArgb("#594768")), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }, Content = row
        };
        return border;
    }

    private NetworkUsageSnapshot CalculateNetworkStatistics(DateTimeOffset resetUtc, IReadOnlyList<StorageFileEntry> files)
    {
        var traffic = GetAppTrafficSinceBaseline();
        var received = traffic.HasValue ? FormatBytes(traffic.Value.Rx) : "غير متاح على هذا النظام";
        var sent = traffic.HasValue ? FormatBytes(traffic.Value.Tx) : "غير متاح على هذا النظام";

        var calls = _callHistory.GetSnapshot()
            .Where(x => x.StartedAt >= resetUtc)
            .ToArray();
        var connectedSeconds = calls
            .Where(x => x.ConnectedAt.HasValue)
            .Sum(x => Math.Max(0, ((x.EndedAt ?? DateTimeOffset.UtcNow) - x.ConnectedAt!.Value).TotalSeconds));
        var callsText = $"{calls.Length.ToString("N0", CultureInfo.InvariantCulture)} مكالمة • {FormatDuration(TimeSpan.FromSeconds(connectedSeconds))}";

        var recentMedia = files.Where(x => x.LastWriteUtc >= resetUtc.UtcDateTime && IsMediaCategory(x.Category)).ToArray();
        var mediaText = $"{recentMedia.Length.ToString("N0", CultureInfo.InvariantCulture)} ملف • {FormatBytes(recentMedia.Sum(x => x.SizeBytes))} محليًا منذ التصفير";

        var counts = GetMessageCountsSince(resetUtc);
        var messagesText = $"{counts.Total.ToString("N0", CultureInfo.InvariantCulture)} رسالة • وارد {counts.Received.ToString("N0", CultureInfo.InvariantCulture)} / صادر {counts.Sent.ToString("N0", CultureInfo.InvariantCulture)}";

        return new NetworkUsageSnapshot(
            received,
            sent,
            callsText,
            mediaText,
            messagesText,
            "لا توجد إحصائية حالات منفصلة في الإصدار الحالي",
            "غير متاح منفصلًا؛ الإجمالي يشمل كل أنواع الاتصال");
    }

    private (long Total, long Received, long Sent) GetMessageCountsSince(DateTimeOffset sinceUtc)
    {
        long total = 0, received = 0, sent = 0;
        try
        {
            foreach (var conversation in _chat.Conversations.ToList())
            {
                try
                {
                    foreach (var message in _chat.GetMessages(conversation.Id).ToList())
                    {
                        DateTimeOffset when;
                        try { when = new DateTimeOffset(message.SentAt).ToUniversalTime(); }
                        catch { continue; }
                        if (when < sinceUtc) continue;
                        total++;
                        if (message.IsMine) sent++; else received++;
                    }
                }
                catch { /* Ignore one conversation while a background sync updates it. */ }
            }
        }
        catch { /* The displayed statistics can remain at zero if a snapshot is in flux. */ }
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
            Himo.Services.AppLocalization.TranslateSourceText("إعادة تعيين الإحصائيات"),
            Himo.Services.AppLocalization.TranslateSourceText("سيبدأ حساب استخدام الشبكة والمكالمات والرسائل والوسائط من الصفر. لن تُحذف المحادثات أو الملفات."),
            Himo.Services.AppLocalization.TranslateSourceText("إعادة تعيين"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
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
        await RefreshScreenAsync();
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تمت إعادة التعيين"), Himo.Services.AppLocalization.TranslateSourceText("تم تصفير الإحصائيات المعروضة. لم يتم حذف أي محتوى."), Himo.Services.AppLocalization.TranslateSourceText("موافق"));
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
            button.BackgroundColor = Color.FromArgb(selected ? "#5636A5" : "#F5F2FB");
            button.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#5A526C");
            button.BorderColor = Color.FromArgb(selected ? "#5636A5" : "#E1DAF0");
        }
    }

    private void RenderFileRows()
    {
        _fileRows.Children.Clear();
        var filtered = _fileSnapshot.Where(entry => MatchesFilter(entry, _selectedFilter)).ToList();
        const int maxVisibleRows = 30;
        var visible = filtered.Take(maxVisibleRows).ToArray();

        if (filtered.Count == 0)
        {
            _filesEmptyLabel.Text = _fileSnapshot.Count == 0
                ? "لا توجد ملفات محلية متاحة للعرض."
                : "لا توجد ملفات ضمن هذا التصنيف.";
            _filesEmptyLabel.IsVisible = true;
            return;
        }

        _filesEmptyLabel.Text = filtered.Count > visible.Length
            ? $"يُعرض أكبر {visible.Length.ToString("N0", CultureInfo.InvariantCulture)} ملفًا من أصل {filtered.Count.ToString("N0", CultureInfo.InvariantCulture)} لتسريع فتح الصفحة."
            : string.Empty;
        _filesEmptyLabel.IsVisible = filtered.Count > visible.Length;

        foreach (var entry in visible)
            _fileRows.Add(BuildFileRow(entry));
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
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 36 }
            },
            ColumnSpacing = 9, FlowDirection = FlowDirection.LeftToRight, Padding = new Thickness(9, 8)
        };
        var categoryIcon = entry.Category switch
        {
            "صورة" or "فيديو" => "media",
            "صوت" => "sound",
            "مستند" => "archive",
            _ => "storage"
        };
        var labels = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, FlowDirection = FlowDirection.RightToLeft };
        labels.Add(new Label { Text = entry.Name, FontSize = 11.5, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2C2342"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
        labels.Add(new Label { Text = $"{entry.Category} • {FormatBytes(entry.SizeBytes)} • {entry.Source}", FontSize = 9.5, TextColor = Color.FromArgb("#7B748D"), HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2 });
        grid.Add(labels, 1, 0);

        var actions = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var open = new Button
        {
            Text = "فتح", FontSize = 10, Padding = new Thickness(9, 0), HeightRequest = 31,
            CornerRadius = 10, BackgroundColor = Color.FromArgb("#F0ECFA"), TextColor = Color.FromArgb("#4D3586"),
            BorderColor = Color.FromArgb("#DED7EF"), BorderWidth = 1
        };
        open.Clicked += async (_, _) => await OpenFileAsync(entry);
        actions.Add(open);

        if (entry.CanDelete)
        {
            var delete = new Button
            {
                Text = "حذف", FontSize = 10, Padding = new Thickness(8, 0), HeightRequest = 31,
                CornerRadius = 10, BackgroundColor = Color.FromArgb("#FFF1F1"), TextColor = Color.FromArgb("#9B3535"),
                BorderColor = Color.FromArgb("#F0DADA"), BorderWidth = 1
            };
            delete.Clicked += async (_, _) => await DeleteFileAsync(entry);
            actions.Add(delete);
        }
        else
        {
            actions.Add(new Label { Text = "محمي", FontSize = 9.5, TextColor = Color.FromArgb("#756D83"), VerticalTextAlignment = TextAlignment.Center });
        }
        grid.Add(actions, 0, 0);
        grid.Add(CreateIconBadge(categoryIcon, 32), 2, 0);
        return new Border
        {
            BackgroundColor = Color.FromArgb("#2D1942"),
            Stroke = new SolidColorBrush(Color.FromArgb("#594768")), StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) }, Content = grid
        };
    }

    private async Task OpenFileAsync(StorageFileEntry entry)
    {
        try
        {
            if (!File.Exists(entry.FullPath))
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الملف"), "لم يعد الملف موجودًا في الذاكرة المحلية.", Himo.Services.AppLocalization.TranslateSourceText("موافق"));
                await RefreshScreenAsync();
                return;
            }
            await Launcher.Default.OpenAsync(new OpenFileRequest(entry.Name, new ReadOnlyFile(entry.FullPath)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("فتح الملف"), "تعذر فتح الملف على هذا الجهاز: " + ex.Message, Himo.Services.AppLocalization.TranslateSourceText("موافق"));
        }
    }

    private async Task DeleteFileAsync(StorageFileEntry entry)
    {
        if (!entry.CanDelete || !IsPathInside(entry.FullPath, FileSystem.Current.CacheDirectory)) return;
        var accepted = await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف النسخة المؤقتة"), $"هل تريد حذف النسخة المحلية من «{entry.Name}»؟ لن تُحذف الرسالة أو المرفق من المحادثة على الخادم.", Himo.Services.AppLocalization.TranslateSourceText("حذف"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
        if (!accepted) return;

        try
        {
            await Task.Run(() =>
            {
                if (File.Exists(entry.FullPath)) File.Delete(entry.FullPath);
            });
            await RefreshScreenAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تعذر الحذف"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("موافق"));
        }
    }

    private async void ClearCacheClicked(object? sender, EventArgs e)
    {
        var accepted = await DisplayAlertAsync(
            Himo.Services.AppLocalization.TranslateSourceText("مسح الوسائط المؤقتة"),
            Himo.Services.AppLocalization.TranslateSourceText("سيتم حذف نسخ الملفات الموجودة في ذاكرة التخزين المؤقت فقط. لن تُحذف المحادثات أو ملفات الإرسال المعلّقة، وقد يعاد تنزيل الوسائط عند فتحها."),
            Himo.Services.AppLocalization.TranslateSourceText("مسح الذاكرة"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
        if (!accepted) return;

        var cacheRoot = SafeFullPath(FileSystem.Current.CacheDirectory);
        var candidates = _fileSnapshot
            .Where(x => x.CanDelete && IsPathInside(x.FullPath, cacheRoot))
            .ToArray();
        var counts = await Task.Run(() =>
        {
            var deleted = 0;
            var failures = 0;
            foreach (var entry in candidates)
            {
                try
                {
                    if (File.Exists(entry.FullPath)) { File.Delete(entry.FullPath); deleted++; }
                }
                catch { failures++; }
            }
            return (Deleted: deleted, Failures: failures);
        });

        await RefreshScreenAsync();
        var result = $"تم حذف {counts.Deleted.ToString("N0", CultureInfo.InvariantCulture)} ملفًا مؤقتًا." +
            (counts.Failures > 0 ? $" تعذر حذف {counts.Failures.ToString("N0", CultureInfo.InvariantCulture)} ملف." : string.Empty);
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("مسح الذاكرة المؤقتة"), result, Himo.Services.AppLocalization.TranslateSourceText("موافق"));
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

    private static string FormatGigabytes(long bytes)
    {
        if (bytes < 0) return "غير متاح";
        return (bytes / (1024d * 1024d * 1024d)).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "غير متاح";
        string[] units = { "بايت", "KB", "MB", "GB", "TB" };
        double amount = bytes;
        var unit = 0;
        while (amount >= 1024 && unit < units.Length - 1) { amount /= 1024; unit++; }
        var number = unit == 0
            ? amount.ToString("N0", CultureInfo.InvariantCulture)
            : amount.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{number} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        if (duration.TotalDays >= 1)
            return string.Format(CultureInfo.InvariantCulture, "{0} يوم {1:00}:{2:00}", (int)duration.TotalDays, duration.Hours, duration.Minutes);
        if (duration.TotalHours >= 1)
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)duration.TotalHours, duration.Minutes);
        return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", duration.Minutes, duration.Seconds);
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

    private sealed record ScreenSnapshot(
        IReadOnlyList<StorageFileEntry> Files,
        long AppDataBytes,
        long CacheBytes,
        long MediaBytes,
        NetworkUsageSnapshot Network);

    private sealed record NetworkUsageSnapshot(
        string Received,
        string Sent,
        string Calls,
        string Media,
        string Messages,
        string Status,
        string Roaming);

    private sealed record StorageFileEntry(
        string FullPath,
        string Name,
        long SizeBytes,
        string Category,
        string Source,
        bool CanDelete,
        DateTime LastWriteUtc);
}
