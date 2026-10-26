using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Himo.Models;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Storage;
using MessageDto = Himo.Services.HimoApiClient.MessageDto;

namespace Himo.Views;

/// <summary>Conversation profile and settings opened by tapping the name/photo in ChatPage.</summary>
public sealed class ConversationInfoPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Conversation _conversation;
    private readonly string _key;
    private readonly Guid? _remoteId;
    private readonly HorizontalStackLayout _mediaStrip = new() { Spacing = 7, Padding = new Thickness(0, 4) };
    private readonly Label _mediaCount = new() { FontSize = 12, FontAttributes = FontAttributes.Bold };
    private readonly Label _storageSummary = new() { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Switch _lockSwitch = new() { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
    private readonly Switch _translationSwitch = new() { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
    private readonly Button _topFavoriteButton = new() { WidthRequest = 42, HeightRequest = 42, CornerRadius = 13, FontSize = 21 };
    private readonly Button _favoriteButton = new() { WidthRequest = 48, HeightRequest = 44, CornerRadius = 14, FontSize = 23 };
    private bool _settingsBusy;
    private bool _isSyncingSettings;
    private List<ChatMessage> _messages = new();

    public ConversationInfoPage(ChatService chat, HimoApiClient api, AccountService account, Conversation conversation)
    {
        _chat = chat;
        _api = api;
        _account = account;
        _conversation = conversation;
        _key = ConversationFeatureStore.ConversationKey(conversation);
        _remoteId = Guid.TryParse(conversation.RemoteId, out var id) ? id : null;

        Title = conversation.Name;
        BackgroundColor = Colors.Transparent;
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var top = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(14, 10), ColumnSpacing = 12 };
        var back = MakeButton("‹", 42);
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        top.Add(back, 0, 0);
        var title = MakeLabel("معلومات المحادثة", 19, true);
        title.VerticalOptions = LayoutOptions.Center;
        top.Add(title, 1, 0);
        top.Add(_topFavoriteButton, 2, 0);
        foreach (var favoriteButton in new[] { _topFavoriteButton, _favoriteButton })
        {
            favoriteButton.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
            favoriteButton.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
            favoriteButton.BorderWidth = 1;
            favoriteButton.Clicked += (_, _) => ToggleFavorite();
        }
        SyncFavoriteButton();
        root.Add(top, 0, 0);

        var content = new VerticalStackLayout { Spacing = 13, Padding = new Thickness(14, 5, 14, 26) };
        var profile = MakeCard(new VerticalStackLayout { Spacing = 7, HorizontalOptions = LayoutOptions.Center, Padding = new Thickness(10, 13) });
        var profileStack = (VerticalStackLayout)profile.Content;
        var avatarGrid = new Grid { WidthRequest = 86, HeightRequest = 86, HorizontalOptions = LayoutOptions.Center };
        var avatarBorder = new Border { WidthRequest = 82, HeightRequest = 82, StrokeThickness = 2, Padding = 0, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        avatarBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        avatarBorder.SetDynamicResource(Border.StrokeProperty, "HimoAccent");
        avatarBorder.StrokeShape = new RoundRectangle { CornerRadius = 41 };
        if (!string.IsNullOrWhiteSpace(_conversation.ProfilePhotoPath) && File.Exists(_conversation.ProfilePhotoPath))
        {
            avatarBorder.Content = new Image { Source = ImageSource.FromFile(_conversation.ProfilePhotoPath), Aspect = Aspect.AspectFill, WidthRequest = 82, HeightRequest = 82, Clip = new EllipseGeometry { Center = new Point(41, 41), RadiusX = 41, RadiusY = 41 } };
        }
        else
        {
            var initial = MakeLabel(string.IsNullOrWhiteSpace(_conversation.Initial) ? _conversation.Name[..Math.Min(1, _conversation.Name.Length)] : _conversation.Initial, 30, true);
            initial.HorizontalTextAlignment = TextAlignment.Center;
            initial.VerticalTextAlignment = TextAlignment.Center;
            avatarBorder.Content = initial;
        }
        avatarGrid.Add(avatarBorder);
        profileStack.Add(avatarGrid);
        var name = MakeLabel(_conversation.Name, 20, true);
        name.HorizontalTextAlignment = TextAlignment.Center;
        profileStack.Add(name);
        var sub = MakeLabel(_conversation.IsGroup ? "معلومات المجموعة" : "معلومات جهة الاتصال والمحادثة", 12, false, secondary: true);
        sub.HorizontalTextAlignment = TextAlignment.Center;
        profileStack.Add(sub);
        content.Add(profile);

        var sharedCardBody = new VerticalStackLayout { Spacing = 10, Padding = 14 };
        var sharedHeader = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        sharedHeader.Add(MakeLabel("الوسائط والروابط والمستندات", 15, true), 0, 0);
        var viewAll = MakeButton("عرض الكل  ›", 36);
        viewAll.FontSize = 12;
        viewAll.Clicked += async (_, _) => await OpenSharedAsync("media");
        sharedHeader.Add(viewAll, 1, 0);
        sharedCardBody.Add(sharedHeader);
        _mediaCount.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        sharedCardBody.Add(_mediaCount);
        _mediaStrip.HorizontalOptions = LayoutOptions.Fill;
        var stripScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = _mediaStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
        stripScroll.HeightRequest = 82;
        sharedCardBody.Add(stripScroll);
        var sharedCard = MakeCard(sharedCardBody);
        // Header/category buttons remain independently clickable; no gesture is put over the buttons.
        content.Add(sharedCard);

        var storageBody = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12, Padding = 13 };
        storageBody.Add(MakeGlyph("▤"), 0, 0);
        var storageText = new VerticalStackLayout { Spacing = 3 };
        storageText.Add(MakeLabel("إدارة مساحة التخزين", 15, true));
        _storageSummary.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        storageText.Add(_storageSummary);
        storageBody.Add(storageText, 1, 0);
        storageBody.Add(MakeLabel("›", 25, true), 2, 0);
        var storageCard = MakeCard(storageBody);
        var storageTap = new TapGestureRecognizer();
        storageTap.Tapped += async (_, _) => await Navigation.PushAsync(new ConversationSharedContentPage(_chat, _api, _account, _conversation, "media", storageMode: true));
        storageCard.GestureRecognizers.Add(storageTap);
        content.Add(storageCard);

        var timerBody = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12, Padding = 13 };
        timerBody.Add(MakeGlyph("◷"), 0, 0);
        var timerText = new VerticalStackLayout { Spacing = 3 };
        timerText.Add(MakeLabel("الرسائل ذاتية الاختفاء", 15, true));
        var timerSummary = MakeLabel("إيقاف", 12, false, secondary: true);
        timerText.Add(timerSummary);
        timerBody.Add(timerText, 1, 0);
        timerBody.Add(MakeLabel("›", 25, true), 2, 0);
        var timerCard = MakeCard(timerBody);
        var timerTap = new TapGestureRecognizer();
        timerTap.Tapped += async (_, _) =>
        {
            await Navigation.PushAsync(new DisappearingMessagesPage(_api, _conversation, _key));
            timerSummary.Text = await ReadTimerSummaryAsync();
        };
        timerCard.GestureRecognizers.Add(timerTap);
        content.Add(timerCard);
        _ = MainThread.InvokeOnMainThreadAsync(async () => timerSummary.Text = await ReadTimerSummaryAsync());

        _lockSwitch.IsToggled = ConversationFeatureStore.IsLocked(_key);
        content.Add(BuildSwitchCard("قفل الدردشة", "حماية فتح هذه المحادثة برمز PIN على هذا الجهاز", "▣", _lockSwitch, async value => await ToggleLockAsync(value)));
        _translationSwitch.IsToggled = ConversationFeatureStore.IsTranslationEnabled(_key);
        content.Add(BuildSwitchCard("ترجمة الرسائل", "اعرض الترجمة أسفل الرسالة الأصلية، ويمكن إخفاؤها لكل رسالة", "文", _translationSwitch, value =>
        {
            ConversationFeatureStore.SetTranslationEnabled(_key, value);
            return Task.CompletedTask;
        }));

        var favBody = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12, Padding = 13 };
        favBody.Add(MakeGlyph("♡"), 0, 0);
        favBody.Add(MakeLabel("إضافة إلى المفضلة", 15, true), 1, 0);
        favBody.Add(_favoriteButton, 2, 0);
        var favCard = MakeCard(favBody);
        content.Add(favCard);

        var clearButton = new Button { Text = "مسح محتوى الدردشة", CornerRadius = 14, HeightRequest = 50, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#C73E55") };
        clearButton.Clicked += ClearChatClicked;
        content.Add(clearButton);

        var scroll = new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        root.Add(scroll, 0, 1);
        Content = root;
    }

    private View MakePill(string text, string tab)
    {
        var button = MakeButton(text, 37);
        button.FontSize = 12;
        button.Clicked += async (_, _) => await OpenSharedAsync(tab);
        return button;
    }

    private View BuildSwitchCard(string title, string description, string glyph, Switch toggle, Func<bool, Task> changed)
    {
        var body = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12, Padding = 13 };
        body.Add(MakeGlyph(glyph), 0, 0);
        var labels = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        labels.Add(MakeLabel(title, 15, true));
        var desc = MakeLabel(description, 11.5, false, secondary: true);
        desc.LineBreakMode = LineBreakMode.WordWrap;
        labels.Add(desc);
        body.Add(labels, 1, 0);
        body.Add(toggle, 2, 0);
        var card = MakeCard(body);
        toggle.Toggled += async (_, e) =>
        {
            if (_settingsBusy || _isSyncingSettings) return;
            _settingsBusy = true;
            try { await changed(e.Value); }
            catch (Exception ex) { await DisplayAlertAsync("تعذر حفظ الإعداد", ex.Message, "حسنًا"); toggle.IsToggled = !e.Value; }
            finally { _settingsBusy = false; }
        };
        return card;
    }

    private async Task ToggleLockAsync(bool value)
    {
        if (value)
        {
            if (!await ConversationFeatureStore.HasPinAsync(_key))
            {
                var created = await ConversationFeatureStore.ConfigurePinAsync(this, _key);
                _lockSwitch.IsToggled = created;
            }
            else
            {
                ConversationFeatureStore.SetLocked(_key, true);
            }
        }
        else
        {
            ConversationFeatureStore.SetLocked(_key, false);
        }
    }

    private void ToggleFavorite()
    {
        var current = ConversationFeatureStore.IsFavorite(_key);
        ConversationFeatureStore.SetFavorite(_key, !current);
        SyncFavoriteButton();
    }

    private void SyncFavoriteButton()
    {
        var favorite = ConversationFeatureStore.IsFavorite(_key);
        var glyph = favorite ? "♥" : "♡";
        var color = favorite ? Color.FromArgb("#E94F76") : Color.FromArgb("#8B93A7");
        foreach (var button in new[] { _favoriteButton, _topFavoriteButton })
        {
            button.Text = glyph;
            button.TextColor = color;
            button.BackgroundColor = Colors.Transparent;
        }
    }

    private async Task<string> ReadTimerSummaryAsync()
    {
        var seconds = ConversationFeatureStore.GetLocalDisappearingSeconds(_key);
        if (_remoteId.HasValue && _api.HasToken)
        {
            try
            {
                seconds = await _api.GetDisappearingSecondsAsync(_remoteId.Value);
                ConversationFeatureStore.SetLocalDisappearingSeconds(_key, seconds);
            }
            catch { }
        }
        return seconds switch { 3600 => "ساعة واحدة", 86400 => "24 ساعة", 432000 => "5 أيام", 2592000 => "30 يومًا", _ => "إيقاف" };
    }

    private async Task OpenSharedAsync(string tab)
    {
        await Navigation.PushAsync(new ConversationSharedContentPage(_chat, _api, _account, _conversation, tab));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            _messages = await ConversationSharedContentPage.LoadAllMessagesAsync(_chat, _api, _account, _conversation);
            _mediaStrip.Children.Clear();
            var media = _messages.Where(m => m.IsImageAttachment || m.IsVideoAttachment).OrderByDescending(m => m.SentAt).ToList();
            _mediaCount.Text = $"الوسائط {media.Count}  •  الروابط {CountLinks(_messages)}  •  المستندات {_messages.Count(IsDocument)}";
            foreach (var message in media.Take(7).Reverse())
                _mediaStrip.Add(await BuildMiniMediaAsync(message));
            var sentBytes = _messages.Where(m => m.IsMine && m.IsAttachment).Sum(m => m.AttachmentSize ?? 0);
            var receivedBytes = _messages.Where(m => !m.IsMine && m.IsAttachment).Sum(m => m.AttachmentSize ?? 0);
            _storageSummary.Text = $"مُرسل {FormatBytes(sentBytes)}   •   مُستلم {FormatBytes(receivedBytes)}";
            _isSyncingSettings = true;
            try
            {
                _lockSwitch.IsToggled = ConversationFeatureStore.IsLocked(_key);
                _translationSwitch.IsToggled = ConversationFeatureStore.IsTranslationEnabled(_key);
            }
            finally { _isSyncingSettings = false; }
            SyncFavoriteButton();
        }
        catch (Exception ex)
        {
            _mediaCount.Text = "تعذر تحميل ملخص الوسائط: " + ex.Message;
        }
    }

    private async Task<View> BuildMiniMediaAsync(ChatMessage message)
    {
        var card = new Border { WidthRequest = 70, HeightRequest = 70, Padding = 0, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 11 } };
        card.SetDynamicResource(Border.BackgroundColorProperty, "HimoSurface");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var localPath = message.AttachmentLocalPath;
        if (message.IsImageAttachment && (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
            && Guid.TryParse(message.RemoteId, out var remoteMessageId) && _api.HasToken)
        {
            try
            {
                localPath = await _api.DownloadAttachmentAsync(remoteMessageId, message.AttachmentFileName ?? "image");
                message.AttachmentLocalPath = localPath;
            }
            catch { /* Keep a lightweight image glyph if preview download fails. */ }
        }
        if (message.IsImageAttachment && !string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            card.Content = new Image { Source = ImageSource.FromFile(localPath), Aspect = Aspect.AspectFill };
        }
        else
        {
            var glyph = MakeLabel(message.IsVideoAttachment ? "▶" : "▧", 22, true);
            glyph.HorizontalTextAlignment = TextAlignment.Center;
            glyph.VerticalTextAlignment = TextAlignment.Center;
            card.Content = glyph;
        }
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try
            {
                var path = localPath;
                if ((string.IsNullOrWhiteSpace(path) || !File.Exists(path)) &&
                    message.IsImageAttachment && Guid.TryParse(message.RemoteId, out var id) && _api.HasToken)
                    path = await _api.DownloadAttachmentAsync(id, message.AttachmentFileName ?? "image");
                if (message.IsImageAttachment && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    await Navigation.PushAsync(new ImagePreviewPage(path, message.AttachmentFileName ?? "الصورة"));
                else
                    await OpenSharedAsync("media");
            }
            catch { await OpenSharedAsync("media"); }
        };
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private async void ClearChatClicked(object? sender, EventArgs e)
    {
        var confirm = await DisplayAlertAsync("مسح محتوى الدردشة", "سيتم مسح الرسائل الحالية من هذا الحساب مع الإبقاء على المحادثة في القائمة. هل تريد المتابعة؟", "مسح المحتوى", "إلغاء");
        if (!confirm) return;
        try
        {
            if (_remoteId.HasValue && _api.HasToken)
                await _api.ClearConversationContentAsync(_remoteId.Value);
            _chat.ClearConversationMessages(_conversation.Id);
            await DisplayAlertAsync("تم المسح", "تم مسح محتوى الدردشة مع الإبقاء على المحادثة.", "حسنًا");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر مسح المحتوى", ex.Message, "حسنًا");
        }
    }

    internal static bool IsDocument(ChatMessage m) => m.IsAttachment && !m.IsImageAttachment && !m.IsVideoAttachment && !m.IsAudioAttachment;
    internal static int CountLinks(IEnumerable<ChatMessage> messages) => messages.Sum(m => Regex.Matches(m.Text ?? string.Empty, @"https?://[^\s<>]+", RegexOptions.IgnoreCase).Count);
    internal static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB" : bytes >= 1024L * 1024 ? $"{bytes / (1024d * 1024):0.##} MB" : bytes >= 1024 ? $"{bytes / 1024d:0.##} KB" : $"{bytes} B";

    internal static Border MakeCard(View content)
    {
        var card = new Border { Content = content, Padding = 0, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 17 } };
        card.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return card;
    }

    internal static Label MakeLabel(string text, double size, bool bold, bool secondary = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap };
        label.SetDynamicResource(Label.TextColorProperty, secondary ? "HimoTextSecondary" : "HimoPrimary");
        return label;
    }

    internal static Button MakeButton(string text, double size)
    {
        var button = new Button { Text = text, WidthRequest = size, HeightRequest = size, Padding = new Thickness(7, 3), CornerRadius = 13, FontSize = 14, FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(Button.BackgroundColorProperty, "HimoSurface");
        button.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        button.BorderWidth = 1;
        return button;
    }

    internal static Label MakeGlyph(string glyph)
    {
        var label = MakeLabel(glyph, 23, true);
        label.HorizontalTextAlignment = TextAlignment.Center;
        label.VerticalTextAlignment = TextAlignment.Center;
        label.WidthRequest = 34;
        return label;
    }
}

/// <summary>Tabbed, horizontally swipeable media/links/documents gallery and per-chat storage viewer.</summary>
public sealed class ConversationSharedContentPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly Conversation _conversation;
    private readonly bool _storageMode;
    private readonly string _key;
    private readonly Grid _root = new();
    private readonly VerticalStackLayout _itemsHost = new() { Spacing = 9, Padding = new Thickness(14, 8, 14, 24) };
    private readonly Label _summary = new() { FontSize = 12 };
    private string _tab;
    private List<ChatMessage> _messages = new();
    private IReadOnlyList<SharedContentItem> _visibleItems = Array.Empty<SharedContentItem>();

    public ConversationSharedContentPage(ChatService chat, HimoApiClient api, AccountService account, Conversation conversation, string initialTab = "media", bool storageMode = false)
    {
        _chat = chat; _api = api; _account = account; _conversation = conversation; _key = ConversationFeatureStore.ConversationKey(conversation); _storageMode = storageMode;
        _tab = storageMode ? "media" : NormalizeTab(initialTab);
        Title = storageMode ? "إدارة مساحة التخزين" : "الوسائط والروابط والمستندات";
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        _root.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) };
        _root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12, Padding = new Thickness(14, 10) };
        var back = ConversationInfoPage.MakeButton("‹", 42);
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0);
        header.Add(ConversationInfoPage.MakeLabel(Title, 18, true), 1, 0);
        _root.Add(header, 0, 0);

        var tabBar = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 7, Padding = new Thickness(14, 0, 14, 10) };
        var media = ConversationInfoPage.MakeButton("الوسائط", 42); media.WidthRequest = -1; media.HorizontalOptions = LayoutOptions.Fill;
        var links = ConversationInfoPage.MakeButton("الروابط", 42); links.WidthRequest = -1; links.HorizontalOptions = LayoutOptions.Fill;
        var docs = ConversationInfoPage.MakeButton("المستندات", 42); docs.WidthRequest = -1; docs.HorizontalOptions = LayoutOptions.Fill;
        media.Clicked += async (_, _) => await SetTabAsync("media");
        links.Clicked += async (_, _) => await SetTabAsync("links");
        docs.Clicked += async (_, _) => await SetTabAsync("documents");
        tabBar.Add(media, 0, 0); tabBar.Add(links, 1, 0); tabBar.Add(docs, 2, 0);
        if (_storageMode) tabBar.IsVisible = false;
        _root.Add(tabBar, 0, 1);

        var body = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        _summary.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        _summary.Margin = new Thickness(15, 1, 15, 7);
        body.Add(_summary, 0, 0);
        var listScroll = new ScrollView { Content = _itemsHost, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        body.Add(listScroll, 0, 1);
        _root.Add(body, 0, 2);
        var swipeLeft = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        swipeLeft.Swiped += async (_, _) => await SetTabAsync(NextTab(_tab, 1));
        var swipeRight = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        swipeRight.Swiped += async (_, _) => await SetTabAsync(NextTab(_tab, -1));
        _root.GestureRecognizers.Add(swipeLeft); _root.GestureRecognizers.Add(swipeRight);
        Content = _root;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _itemsHost.Children.Clear();
        _itemsHost.Add(new ActivityIndicator { IsRunning = true, IsVisible = true, HorizontalOptions = LayoutOptions.Center, Margin = new Thickness(20) });
        _messages = await LoadAllMessagesAsync(_chat, _api, _account, _conversation);
        _itemsHost.Children.Clear();
        if (_storageMode)
        {
            var sent = _messages.Where(m => m.IsMine && m.IsAttachment).Sum(m => m.AttachmentSize ?? 0);
            var received = _messages.Where(m => !m.IsMine && m.IsAttachment).Sum(m => m.AttachmentSize ?? 0);
            _summary.Text = $"المجموع: {ConversationInfoPage.FormatBytes(sent + received)}  •  المرسل: {ConversationInfoPage.FormatBytes(sent)}  •  المستلم: {ConversationInfoPage.FormatBytes(received)}";
            _tab = "media";
        }
        await RenderItemsAsync();
    }

    private static string NormalizeTab(string tab) => tab is "links" or "documents" ? tab : "media";
    private static string NextTab(string tab, int direction)
    {
        var tabs = new[] { "media", "links", "documents" };
        var index = Array.IndexOf(tabs, NormalizeTab(tab));
        return tabs[(index + direction + tabs.Length) % tabs.Length];
    }

    private async Task SetTabAsync(string tab)
    {
        if (_storageMode) return;
        _tab = NormalizeTab(tab);
        await RenderItemsAsync();
    }

    internal static async Task<List<ChatMessage>> LoadAllMessagesAsync(ChatService chat, HimoApiClient api, AccountService account, Conversation conversation)
    {
        var local = chat.GetMessages(conversation.Id).ToList();
        if (!Guid.TryParse(conversation.RemoteId, out var remoteId) || !api.HasToken)
            return local.OrderBy(m => m.SentAt).ToList();

        try
        {
            var all = new Dictionary<string, ChatMessage>(StringComparer.OrdinalIgnoreCase);
            DateTimeOffset? before = null;
            Guid? beforeId = null;
            var myId = account.CurrentAccount?.UserId ?? Guid.Empty;
            for (var pageNumber = 0; pageNumber < 100; pageNumber++)
            {
                var page = await api.GetMessagesAsync(remoteId, before: before, beforeId: beforeId, limit: 1000);
                if (page.Count == 0) break;
                foreach (var dto in page)
                {
                    var key = dto.Id.ToString("D");
                    all[key] = new ChatMessage
                    {
                        Id = 0, RemoteId = key, ConversationId = conversation.Id, Text = dto.Text,
                        SentAt = dto.SentAt.LocalDateTime, IsMine = myId != Guid.Empty && dto.SenderUserId == myId,
                        AttachmentFileName = dto.AttachmentFileName, AttachmentContentType = dto.AttachmentContentType,
                        AttachmentSize = dto.AttachmentSize, DeliveryStatus = dto.Status,
                        ReplyToRemoteId = dto.ReplyToMessageId?.ToString("D"), ReplyToText = dto.ReplyToText,
                        IsEdited = dto.IsEdited, IsDeleted = dto.IsDeleted, ExpiresAt = dto.ExpiresAt?.LocalDateTime
                    };
                }
                if (page.Count < 1000) break;
                var oldest = page.OrderBy(x => x.SentAt).ThenBy(x => x.Id).First();
                before = oldest.SentAt;
                beforeId = oldest.Id;
            }
            foreach (var message in local)
            {
                var id = message.RemoteId;
                if (!string.IsNullOrWhiteSpace(id) && all.TryGetValue(id, out var remote) && string.IsNullOrWhiteSpace(remote.AttachmentLocalPath))
                    remote.AttachmentLocalPath = message.AttachmentLocalPath;
            }
            return all.Values.OrderBy(m => m.SentAt).ThenBy(m => m.RemoteId).ToList();
        }
        catch
        {
            return local.OrderBy(m => m.SentAt).ToList();
        }
    }

    private async Task RenderItemsAsync()
    {
        _itemsHost.Children.Clear();
        IEnumerable<SharedContentItem> items;
        if (_storageMode)
        {
            items = _messages.Where(m => m.IsImageAttachment || m.IsVideoAttachment)
                .Select(m => ToAttachmentItem(m)).ToList();
        }
        else if (_tab == "links")
        {
            items = _messages.SelectMany(m => Regex.Matches(m.Text ?? string.Empty, @"https?://[^\s<>]+", RegexOptions.IgnoreCase)
                .Select(match => new SharedContentItem { Title = TrimUrl(match.Value), Subtitle = m.SentAt.ToString("yyyy/MM/dd HH:mm"), Kind = "link", Url = CleanUrl(match.Value), Message = m }));
        }
        else if (_tab == "documents")
        {
            items = _messages.Where(ConversationInfoPage.IsDocument).Select(ToAttachmentItem).ToList();
        }
        else
        {
            items = _messages.Where(m => m.IsImageAttachment || m.IsVideoAttachment || m.IsAudioAttachment).Select(ToAttachmentItem).ToList();
        }

        var list = items.OrderByDescending(x => x.Message.SentAt).ToList();
        // Remote message pages may not carry a local path. Download image thumbnails lazily
        // so images are displayed as images in Storage, Media, and any attachment listing.
        foreach (var item in list.Where(x => x.Kind == "image"))
        {
            if (!string.IsNullOrWhiteSpace(item.LocalPath) && File.Exists(item.LocalPath)) continue;
            if (!Guid.TryParse(item.Message.RemoteId, out var attachmentId) || !_api.HasToken) continue;
            try
            {
                var path = await _api.DownloadAttachmentAsync(attachmentId, item.Message.AttachmentFileName ?? "image");
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    item.LocalPath = path;
                    item.Message.AttachmentLocalPath = path;
                }
            }
            catch { /* Keep a fallback icon if this attachment cannot be downloaded. */ }
        }
        _visibleItems = list;
        _summary.Text = _storageMode ? $"{list.Count} صورة أو فيديو" : _tab switch { "links" => $"{list.Count} رابطًا", "documents" => $"{list.Count} مستندًا", _ => $"{list.Count} ملفًا صوتيًا أو صورة أو فيديو" };
        if (list.Count == 0)
        {
            var empty = new VerticalStackLayout { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Padding = 30, Spacing = 8 };
            empty.Add(ConversationInfoPage.MakeLabel(_tab == "links" ? "لا توجد روابط في هذه المحادثة" : _tab == "documents" ? "لا توجد مستندات في هذه المحادثة" : "لا توجد وسائط في هذه المحادثة", 16, true));
            _itemsHost.Add(empty);
            return;
        }
        if (_storageMode)
        {
            var gallery = new Grid { ColumnSpacing = 7, RowSpacing = 7, ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var tile = new Border { HeightRequest = 112, Padding = 0, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 10 } };
                tile.SetDynamicResource(Border.BackgroundColorProperty, "HimoSurface");
                tile.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
                if (item.Kind == "image" && !string.IsNullOrWhiteSpace(item.LocalPath) && File.Exists(item.LocalPath))
                    tile.Content = new Image { Source = ImageSource.FromFile(item.LocalPath), Aspect = Aspect.AspectFill };
                else
                {
                    var fallback = ConversationInfoPage.MakeLabel(item.Kind == "video" ? "▶" : "▧", 28, true);
                    fallback.HorizontalTextAlignment = TextAlignment.Center;
                    fallback.VerticalTextAlignment = TextAlignment.Center;
                    tile.Content = fallback;
                }
                var tap = new TapGestureRecognizer();
                tap.Tapped += async (_, _) => await OpenItemAsync(item);
                tile.GestureRecognizers.Add(tap);
                gallery.Add(tile, i % 3, i / 3);
                if (i / 3 + 1 > gallery.RowDefinitions.Count)
                    gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }
            _itemsHost.Add(gallery);
        }
        else
        {
            foreach (var item in list)
                _itemsHost.Add(BuildItemRow(item));
        }
        await Task.CompletedTask;
    }

    private static string TrimUrl(string url) => url.Length > 56 ? url[..53] + "…" : url;
    private static string CleanUrl(string url) => url.TrimEnd('.', ',', ';', ')', ']', '}', '،', '؛');
    private static SharedContentItem ToAttachmentItem(ChatMessage m) => new()
    {
        Title = m.AttachmentFileName ?? (m.IsImageAttachment ? "صورة" : m.IsVideoAttachment ? "فيديو" : "ملف"),
        Subtitle = $"{m.SentAt:yyyy/MM/dd HH:mm}  •  {ConversationInfoPage.FormatBytes(m.AttachmentSize ?? 0)}  •  {(m.IsMine ? "مرسل" : "مستلم")}",
        Kind = m.IsImageAttachment ? "image" : m.IsVideoAttachment ? "video" : m.IsAudioAttachment ? "audio" : "document",
        Message = m, LocalPath = m.AttachmentLocalPath
    };

    private View BuildItemRow(SharedContentItem item)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 11, Padding = new Thickness(10, 9) };
        var visual = new Border { WidthRequest = 58, HeightRequest = 58, StrokeThickness = 1, Padding = 0, StrokeShape = new RoundRectangle { CornerRadius = 10 } };
        visual.SetDynamicResource(Border.BackgroundColorProperty, "HimoSurface"); visual.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        if (item.Kind == "image" && !string.IsNullOrWhiteSpace(item.LocalPath) && File.Exists(item.LocalPath))
            visual.Content = new Image { Source = ImageSource.FromFile(item.LocalPath), Aspect = Aspect.AspectFill };
        else
        {
            var icon = ConversationInfoPage.MakeLabel(item.Kind switch { "link" => "↗", "video" => "▶", "image" => "▧", "audio" => "♫", _ => "▤" }, 23, true);
            icon.HorizontalTextAlignment = TextAlignment.Center; icon.VerticalTextAlignment = TextAlignment.Center;
            visual.Content = icon;
        }
        grid.Add(visual, 0, 0);
        var labels = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var title = ConversationInfoPage.MakeLabel(item.Title, 14, true); title.MaxLines = 2;
        labels.Add(title);
        var sub = ConversationInfoPage.MakeLabel(item.Subtitle, 11, false, secondary: true); sub.MaxLines = 2;
        labels.Add(sub);
        grid.Add(labels, 1, 0);
        grid.Add(ConversationInfoPage.MakeLabel("›", 25, true), 2, 0);
        var card = ConversationInfoPage.MakeCard(grid);
        var tap = new TapGestureRecognizer(); tap.Tapped += async (_, _) => await OpenItemAsync(item);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private async Task OpenItemAsync(SharedContentItem item)
    {
        try
        {
            if (item.Kind == "link" && Uri.TryCreate(item.Url, UriKind.Absolute, out var uri))
            {
                await Launcher.Default.OpenAsync(uri);
                return;
            }
            var message = item.Message;
            var path = item.LocalPath;
            if ((string.IsNullOrWhiteSpace(path) || !File.Exists(path)) && Guid.TryParse(message.RemoteId, out var messageId))
                path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "file");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await DisplayAlertAsync("الملف", "لم أتمكن من تنزيل الملف من الخادم.", "حسنًا"); return;
            }
            if (item.Kind == "image")
                await Navigation.PushAsync(new ImagePreviewPage(path, item.Title));
            else
                await Launcher.Default.OpenAsync(new OpenFileRequest(item.Title, new ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر فتح الملف", ex.Message, "حسنًا");
        }
    }

    private sealed class SharedContentItem
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string? Url { get; init; }
        public ChatMessage Message { get; init; } = null!;
        public string? LocalPath { get; set; }
    }
}

/// <summary>Conversation-level disappearing-message timer, persisted by the API for both participants.</summary>
public sealed class DisappearingMessagesPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly Conversation _conversation;
    private readonly string _key;
    private readonly Dictionary<int, RadioButton> _options = new();
    private int _selected;
    private readonly Label _status = ConversationInfoPage.MakeLabel("يطبق المؤقت على الرسائل الجديدة في هذه المحادثة.", 12, false, secondary: true);

    public DisappearingMessagesPage(HimoApiClient api, Conversation conversation, string key)
    {
        _api = api; _conversation = conversation; _key = key;
        Title = "الرسائل ذاتية الاختفاء";
        BuildPage();
        AppLocalization.AttachToPage(this);
    }

    private void BuildPage()
    {
        var root = new Grid { RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }, Padding = new Thickness(14) };
        root.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 };
        var back = ConversationInfoPage.MakeButton("‹", 42); back.Clicked += async (_, _) => await Navigation.PopAsync();
        header.Add(back, 0, 0); header.Add(ConversationInfoPage.MakeLabel("مؤقت الرسائل", 19, true), 1, 0);
        root.Add(header, 0, 0);
        var body = new VerticalStackLayout { Spacing = 9, Padding = new Thickness(0, 15) };
        body.Add(ConversationInfoPage.MakeCard(new VerticalStackLayout { Padding = 14, Spacing = 6, Children = { ConversationInfoPage.MakeLabel("بعد كم تختفي الرسائل؟", 16, true), _status } }));
        AddOption(body, 3600, "ساعة", "تختفي الرسائل بعد ساعة من إرسالها");
        AddOption(body, 86400, "24 ساعة", "تختفي الرسائل بعد يوم");
        AddOption(body, 432000, "5 أيام", "تختفي الرسائل بعد خمسة أيام");
        AddOption(body, 2592000, "30 يومًا", "تختفي الرسائل بعد ثلاثين يومًا");
        AddOption(body, 0, "إيقاف", "تبقى الرسائل حتى تحذف يدويًا");
        root.Add(new ScrollView { Content = body }, 0, 1);
        var save = new Button { Text = "حفظ المؤقت", HeightRequest = 50, CornerRadius = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        save.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        save.Clicked += SaveClicked;
        root.Add(save, 0, 2);
        Content = root;
    }

    private void AddOption(VerticalStackLayout host, int seconds, string title, string description)
    {
        var radio = new RadioButton { GroupName = "disappearing-timer", Value = seconds, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
        radio.CheckedChanged += (_, e) => { if (e.Value) _selected = seconds; };
        _options[seconds] = radio;
        var body = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(12, 9), ColumnSpacing = 8 };
        var labels = new VerticalStackLayout { Spacing = 3 };
        labels.Add(ConversationInfoPage.MakeLabel(title, 15, true));
        labels.Add(ConversationInfoPage.MakeLabel(description, 11.5, false, secondary: true));
        body.Add(labels, 0, 0); body.Add(radio, 1, 0);
        var card = ConversationInfoPage.MakeCard(body);
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => radio.IsChecked = true; card.GestureRecognizers.Add(tap);
        host.Add(card);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _selected = ConversationFeatureStore.GetLocalDisappearingSeconds(_key);
        if (Guid.TryParse(_conversation.RemoteId, out var remoteId) && _api.HasToken)
        {
            try { _selected = await _api.GetDisappearingSecondsAsync(remoteId); }
            catch { }
        }
        foreach (var pair in _options) pair.Value.IsChecked = pair.Key == _selected;
    }

    private async void SaveClicked(object? sender, EventArgs e)
    {
        try
        {
            if (!Guid.TryParse(_conversation.RemoteId, out var remoteId) || !_api.HasToken)
            {
                await DisplayAlertAsync("المحادثة غير متصلة", "يجب أن تكون المحادثة متصلة بالخادم لحفظ مؤقت يعمل على الهاتفين.", "حسنًا");
                return;
            }
            await _api.SetDisappearingSecondsAsync(remoteId, _selected);
            ConversationFeatureStore.SetLocalDisappearingSeconds(_key, _selected);
            await DisplayAlertAsync("تم الحفظ", "سيطبق المؤقت على الرسائل الجديدة في هذه المحادثة على الطرفين.", "حسنًا");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر حفظ المؤقت", ex.Message, "حسنًا");
        }
    }
}
