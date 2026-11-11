using Himo.Services;
using Himo.Models;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using System.Security.Cryptography;
using System.Text;

namespace Himo.Views;

public partial class ChatPage
{
    private Grid? _conversationLockOverlay;
    private Entry? _conversationLockPinEntry;
    private Label? _conversationLockError;
    private IDispatcherTimer? _conversationExpiryTimer;
    private int _unlockFailures;

    private string CurrentConversationFeatureKey()
    {
        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation is not null) return ConversationFeatureStore.ConversationKey(conversation);
        if (Guid.TryParse(_remoteConversationId, out var remoteId)) return remoteId.ToString("D").ToLowerInvariant();
        return $"local-{_conversationId}";
    }

    private void EnsureConversationFeaturesOverlay()
    {
        if (_conversationLockOverlay is not null || ChatRoot.Content is not Grid root) return;
        var overlay = new Grid
        {
            IsVisible = false,
            ZIndex = 10000,
            Padding = new Thickness(24),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        overlay.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");
        var body = new VerticalStackLayout { Spacing = 13, Padding = new Thickness(20), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        var card = new Border { Content = body, WidthRequest = 320, Padding = 0, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 23 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        card.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var title = new Label { Text = "هذه الدردشة مقفلة", FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        body.Add(new Label { Text = "▣", FontSize = 36, HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#8B5CF6") });
        body.Add(title);
        var message = new Label { Text = "أدخل رمز PIN لفتح المحادثة", FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
        message.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        body.Add(message);
        _conversationLockPinEntry = new Entry { Placeholder = "رمز من 4 إلى 8 أرقام", IsPassword = true, Keyboard = Keyboard.Numeric, MaxLength = 8, HorizontalTextAlignment = TextAlignment.Center };
        _conversationLockPinEntry.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSurface");
        _conversationLockPinEntry.SetDynamicResource(Entry.TextColorProperty, "HimoPrimary");
        body.Add(_conversationLockPinEntry);
        _conversationLockError = new Label { IsVisible = false, FontSize = 12, TextColor = Color.FromArgb("#D9485F"), HorizontalTextAlignment = TextAlignment.Center };
        body.Add(_conversationLockError);
        var open = new Button { Text = "فتح المحادثة", HeightRequest = 46, CornerRadius = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White };
        open.SetDynamicResource(Button.BackgroundColorProperty, "HimoAccent");
        open.Clicked += UnlockConversationClicked;
        body.Add(open);
        overlay.Add(card);
        root.Add(overlay);
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, Math.Max(1, root.RowDefinitions.Count));
        _conversationLockOverlay = overlay;
    }

    private void UpdateConversationFeatureState()
    {
        EnsureConversationFeaturesOverlay();
        if (_conversationLockOverlay is not null)
            _conversationLockOverlay.IsVisible = ConversationFeatureStore.IsLocked(CurrentConversationFeatureKey());
        ApplyTranslationPreference(_chat.GetMessages(_conversationId));
        ApplyTranslationPreference(_visibleMessages);
        if (_conversationLockOverlay?.IsVisible == true)
        {
            _conversationLockPinEntry?.Focus();
        }
    }

    private void ApplyTranslationPreference(IEnumerable<ChatMessage> messages)
    {
        var enabled = ConversationFeatureStore.IsTranslationEnabled(CurrentConversationFeatureKey());
        foreach (var message in messages)
            message.TranslationEnabled = enabled && !message.IsAttachment && !string.IsNullOrWhiteSpace(message.Text);
    }

    private void StartConversationExpiryTimer()
    {
        if (_conversationExpiryTimer is null)
        {
            _conversationExpiryTimer = Dispatcher.CreateTimer();
            _conversationExpiryTimer.Interval = TimeSpan.FromSeconds(5);
            _conversationExpiryTimer.IsRepeating = true;
            _conversationExpiryTimer.Tick += (_, _) =>
            {
                if (!IsConversationVisible || _conversationId <= 0) return;
                var purged = _chat.PurgeExpiredMessages(_conversationId);
                if (purged > 0)
                    RefreshMessagesView(scrollToEnd: false);
            };
        }
        if (!_conversationExpiryTimer.IsRunning) _conversationExpiryTimer.Start();
    }

    private void StopConversationExpiryTimer() => _conversationExpiryTimer?.Stop();

    private async void UnlockConversationClicked(object? sender, EventArgs e)
    {
        var key = CurrentConversationFeatureKey();
        var pin = _conversationLockPinEntry?.Text?.Trim() ?? string.Empty;
        if (!await ConversationFeatureStore.HasPinAsync(key))
        {
            if (await ConversationFeatureStore.ConfigurePinAsync(this, key))
            {
                if (_conversationLockOverlay is not null) _conversationLockOverlay.IsVisible = false;
                _unlockFailures = 0;
            }
            return;
        }

        if (await ConversationFeatureStore.VerifyPinAsync(key, pin))
        {
            _unlockFailures = 0;
            if (_conversationLockError is not null) _conversationLockError.IsVisible = false;
            if (_conversationLockPinEntry is not null) _conversationLockPinEntry.Text = string.Empty;
            if (_conversationLockOverlay is not null) _conversationLockOverlay.IsVisible = false;
            return;
        }

        _unlockFailures++;
        if (_conversationLockError is not null)
        {
            _conversationLockError.Text = _unlockFailures >= 5 ? "تأكد من الرمز وحاول مجددًا." : "الرمز غير صحيح. حاول مرة أخرى.";
            _conversationLockError.IsVisible = true;
        }
        if (_conversationLockPinEntry is not null)
        {
            _conversationLockPinEntry.Text = string.Empty;
            _conversationLockPinEntry.Focus();
        }
    }

    private Grid? _headerActionsOverlay;
    private Grid? _groupMemberPickerOverlay;
    private readonly HashSet<Guid> _selectedGroupCallMembers = new();
    private IReadOnlyList<HimoApiClient.GroupMemberDto> _cachedGroupHeaderMembers = Array.Empty<HimoApiClient.GroupMemberDto>();

    private async void HeaderActionsClicked(object? sender, EventArgs e)
    {
        if (_headerActionsOverlay is not null)
        {
            CloseHeaderActionsMenu();
            return;
        }

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation?.IsGroup != true)
        {
            var infoOption = AppLocalization.TranslateSourceText("معلومات المحادثة");
            var cancelOption = AppLocalization.TranslateSourceText("إلغاء");
            var selected = await DisplayActionSheetAsync(
                AppLocalization.TranslateSourceText("إجراءات المحادثة"), cancelOption, null,
                AppLocalization.TranslateSourceText("اتصال صوتي"),
                AppLocalization.TranslateSourceText("اتصال مرئي"), infoOption,
                AppLocalization.TranslateSourceText("خيارات المحادثة"));
            if (selected == AppLocalization.TranslateSourceText("اتصال صوتي")) AudioCallClicked(sender, e);
            else if (selected == AppLocalization.TranslateSourceText("اتصال مرئي")) VideoCallClicked(sender, e);
            else if (selected == infoOption && conversation is not null)
                await Navigation.PushAsync(new ConversationInfoPage(_chat, _api, _account, conversation), animated: true);
            else if (selected == AppLocalization.TranslateSourceText("خيارات المحادثة")) ConversationOptionsClicked(sender, e);
            return;
        }

        ShowHeaderActionsMenu();
        if (_cachedGroupHeaderMembers.Count == 0 && Guid.TryParse(_remoteConversationId, out var groupId))
        {
            try
            {
                var details = await _api.GetGroupDetailsAsync(groupId);
                _cachedGroupHeaderMembers = details.Members ?? Array.Empty<HimoApiClient.GroupMemberDto>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Header menu member refresh failed: {ex.Message}");
            }
        }
    }

    private void ShowHeaderActionsMenu()
    {
        if (ChatRoot.Content is not Grid root || _headerActionsOverlay is not null) return;
        var overlay = new Grid { ZIndex = 9000, BackgroundColor = Colors.Transparent };
        var outside = new Button { BackgroundColor = Colors.Transparent, BorderWidth = 0, Text = string.Empty };
        outside.Clicked += (_, _) => CloseHeaderActionsMenu();
        overlay.Add(outside);

        var menu = new Border
        {
            Padding = new Thickness(0, 7),
            WidthRequest = 238,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(58, 8, 0, 0),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 17 },
            ZIndex = 2
        };
        menu.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        menu.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var stack = new VerticalStackLayout { Spacing = 0 };
        AddHeaderMenuItem(stack, "☎", "مكالمة صوتية", () => { CloseHeaderActionsMenu(); AudioCallClicked(this, EventArgs.Empty); });
        AddHeaderMenuItem(stack, "▣", "مكالمة فيديو", () => { CloseHeaderActionsMenu(); VideoCallClicked(this, EventArgs.Empty); });
        AddHeaderMenuItem(stack, "☷", "تحديد الأشخاص", () => { CloseHeaderActionsMenu(); ShowGroupMemberPicker(); });
        menu.Content = stack;
        overlay.Add(menu);
        root.Add(overlay);
        _headerActionsOverlay = overlay;
    }

    private void AddHeaderMenuItem(VerticalStackLayout stack, string symbol, string caption, Action action)
    {
        var button = new Button
        {
            Text = $"{symbol}     {AppLocalization.TranslateSourceText(caption)}",
            FontSize = 16,
            Padding = new Thickness(17, 13),
            HorizontalOptions = LayoutOptions.Fill,
            CornerRadius = 0,
            BackgroundColor = Colors.Transparent,
            TextColor = Application.Current?.RequestedTheme == AppTheme.Dark ? Colors.White : Colors.Black
        };
        button.Clicked += (_, _) => action();
        stack.Add(button);
    }

    private void CloseHeaderActionsMenu()
    {
        if (_headerActionsOverlay is null) return;
        if (ChatRoot.Content is Grid root) root.Remove(_headerActionsOverlay);
        _headerActionsOverlay = null;
    }

    private void ShowGroupMemberPicker()
    {
        if (ChatRoot.Content is not Grid root || _groupMemberPickerOverlay is not null) return;
        if (_cachedGroupHeaderMembers.Count == 0)
        {
            _ = DisplayAlertAsync(AppLocalization.TranslateSourceText("المجموعة"),
                AppLocalization.TranslateSourceText("تعذر تحميل أعضاء المجموعة. أعد فتح القائمة بعد التحقق من الاتصال."),
                AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        _selectedGroupCallMembers.Clear();
        var overlay = new Grid { ZIndex = 9100, Padding = new Thickness(22), BackgroundColor = Color.FromArgb("#99000000") };
        var card = new Border { Padding = 16, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Fill, MaximumHeightRequest = 560, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 } };
        card.SetDynamicResource(Border.BackgroundColorProperty, "HimoCard");
        card.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        var body = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) }, RowSpacing = 12 };
        var audio = new Button { Text = AppLocalization.TranslateSourceText("اتصال صوتي"), CornerRadius = 12, FontSize = 13, IsEnabled = false };
        var video = new Button { Text = AppLocalization.TranslateSourceText("اتصال فيديو"), CornerRadius = 12, FontSize = 13, IsEnabled = false };
        var title = new Label { Text = AppLocalization.TranslateSourceText("تحديد الأشخاص"), FontSize = 18, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        body.Add(title); Grid.SetRow(title, 0);
        var list = new CollectionView { SelectionMode = SelectionMode.None, ItemsSource = _cachedGroupHeaderMembers.Where(x => x.Id != _account.CurrentAccount?.UserId).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), ItemTemplate = new DataTemplate(() =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) }, Padding = new Thickness(8, 7) };
            var name = new Label { FontSize = 14, VerticalTextAlignment = TextAlignment.Center };
            name.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
            name.SetBinding(Label.TextProperty, nameof(HimoApiClient.GroupMemberDto.Name));
            var check = new CheckBox { VerticalOptions = LayoutOptions.Center, Color = Color.FromArgb("#A56BFF") };
            check.CheckedChanged += (_, args) =>
            {
                if (check.BindingContext is HimoApiClient.GroupMemberDto member)
                {
                    if (args.Value) _selectedGroupCallMembers.Add(member.Id);
                    else _selectedGroupCallMembers.Remove(member.Id);
                    audio.IsEnabled = video.IsEnabled = _selectedGroupCallMembers.Count > 0;
                }
            };
            row.Add(name); row.Add(check); Grid.SetColumn(check, 1);
            return row;
        }) };
        body.Add(list); Grid.SetRow(list, 1);
        var actions = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Star) }, ColumnSpacing = 10 };
        audio.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        video.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoAccent");
        audio.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        video.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        // The selected IDs are passed to CallPage and sent as targeted SignalR invites.
        audio.Clicked += async (_, _) => await StartSelectedGroupCallAsync(false);
        video.Clicked += async (_, _) => await StartSelectedGroupCallAsync(true);
        actions.Add(audio); actions.Add(video, 1, 0);
        body.Add(actions); Grid.SetRow(actions, 2);
        var footer = new Label { Text = AppLocalization.TranslateSourceText("اختر أعضاء المجموعة للمتابعة"), FontSize = 11, HorizontalTextAlignment = TextAlignment.Center };
        footer.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        var close = new Button { Text = AppLocalization.TranslateSourceText("إغلاق"), FontSize = 12, BackgroundColor = Colors.Transparent };
        close.Clicked += (_, _) => CloseGroupMemberPicker();
        var footerStack = new VerticalStackLayout { Spacing = 3 };
        footerStack.Add(footer); footerStack.Add(close);
        body.Add(footerStack); Grid.SetRow(footerStack, 3);
        card.Content = body; overlay.Add(card); root.Add(overlay); _groupMemberPickerOverlay = overlay;
    }

    private async Task StartSelectedGroupCallAsync(bool video)
    {
        if (_selectedGroupCallMembers.Count == 0 ||
            !Guid.TryParse(_remoteConversationId, out var conversationId)) return;

        var selectedIds = _selectedGroupCallMembers
            .Where(id => id != Guid.Empty && id != (_account.CurrentAccount?.UserId ?? Guid.Empty))
            .Distinct()
            .ToArray();
        if (selectedIds.Length == 0) return;

        CloseGroupMemberPicker();
        try
        {
            // If a group call is already active, join it instead of creating a
            // second room. The remaining members can join that room independently.
            if (await TryJoinActiveGroupCallAsync(conversationId)) return;

            var selected = Uri.EscapeDataString(string.Join(",", selectedIds));
            var mode = video ? "video" : "audio";
            await SwipePageTransition.NavigateAsync(
                this,
                $"CallPage?id={conversationId:D}&mode={mode}&selectedParticipantIds={selected}",
                SwipeDirection.Left);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                AppLocalization.TranslateSourceText("المكالمة"),
                $"تعذر بدء المكالمة مع الأعضاء المحددين: {ex.Message}",
                AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void CloseGroupMemberPicker()
    {
        if (_groupMemberPickerOverlay is null) return;
        if (ChatRoot.Content is Grid root) root.Remove(_groupMemberPickerOverlay);
        _groupMemberPickerOverlay = null;
    }

    private async void ConversationHeaderTapped(object? sender, TappedEventArgs e)
    {
        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation is null)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("معلومات المحادثة"),Himo.Services.AppLocalization.TranslateSourceText("تعذر العثور على بيانات المحادثة المحلية."),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        try
        {
            if (conversation.IsGroup)
                await Navigation.PushAsync(new GroupConversationInfoPage(_chat, _api, _account, conversation), animated: true);
            else
                await Navigation.PushAsync(new ConversationInfoPage(_chat, _api, _account, conversation), animated: true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("معلومات المحادثة"),Himo.Services.AppLocalization.TranslateSourceText("تعذر فتح صفحة المعلومات: " + Himo.Services.AppLocalization.TranslateSourceText(ex.Message)),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void TranslateMessageClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message) return;
        if (!ConversationFeatureStore.IsTranslationEnabled(CurrentConversationFeatureKey())) return;
        if (!string.IsNullOrWhiteSpace(message.TranslatedText))
        {
            message.IsTranslationVisible = !message.IsTranslationVisible;
            return;
        }
        if (message.IsTranslating || string.IsNullOrWhiteSpace(message.Text)) return;

        message.IsTranslating = true;
        try
        {
            var translated = await _api.TranslateTextAsync(message.Text, AppLocalization.CurrentLanguageCode);
            message.TranslatedText = translated;
            message.IsTranslationVisible = !string.IsNullOrWhiteSpace(translated);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("ترجمة الرسائل"),Himo.Services.AppLocalization.TranslateSourceText("تعذرت الترجمة. تأكد من إعداد خدمة الترجمة في خادم Himo.\n" + Himo.Services.AppLocalization.TranslateSourceText(ex.Message)),Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            message.IsTranslating = false;
        }
    }
}
