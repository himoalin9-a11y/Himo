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

    private async void ConversationHeaderTapped(object? sender, TappedEventArgs e)
    {
        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation is null)
        {
            await DisplayAlertAsync("معلومات المحادثة", "تعذر العثور على بيانات المحادثة المحلية.", "حسنًا");
            return;
        }

        try
        {
            await Navigation.PushAsync(new ConversationInfoPage(_chat, _api, _account, conversation), animated: true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("معلومات المحادثة", "تعذر فتح صفحة المعلومات: " + ex.Message, "حسنًا");
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
            await DisplayAlertAsync("ترجمة الرسائل", "تعذرت الترجمة. تأكد من إعداد خدمة الترجمة في خادم Himo.\n" + ex.Message, "حسنًا");
        }
        finally
        {
            message.IsTranslating = false;
        }
    }
}
