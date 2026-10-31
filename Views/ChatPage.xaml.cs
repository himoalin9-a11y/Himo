using Microsoft.Maui.Graphics;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Net.Http;
using System.Diagnostics;
using Himo.Services;
using Himo.Models;
using CommunityToolkit.Maui.Views;
using MessageDto = Himo.Services.HimoApiClient.MessageDto;

#if ANDROID
using AndroidX.RecyclerView.Widget;
#endif

namespace Himo.Views;

[QueryProperty(nameof(ConversationId), "id")]
public partial class ChatPage : ContentPage
{
    private const int MessagePageSize = 30;
    private const int InitialVisibleMessageCount = 20;
    private const int MessageSyncPageSize = 100;
    private const string ActiveConversationPreferenceKey = "himo_active_conversation_id";
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    private readonly AccountService _account;
    private readonly INotificationService _notifications;
    private readonly HimoRealtimeService _realtime;
    private readonly RangeObservableCollection<ChatMessage> _visibleMessages = new();
    private Conversation? _avatarBoundConversation;
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _remoteLoadGate = new(1, 1);
    private int _conversationId;
    private int _loadingOlder;
    private bool _allowOlderPaging;
    private bool _initialPositioningLatest;
    private int _firstVisibleItemIndex = -1;
    private int _lastVisibleItemIndex = -1;
    private IDispatcherTimer? _scrollIdleTimer;
    private bool _scrollIdleMediaPending;
    private bool _scrollIdleOlderPending;
    private bool _hasMoreOlderMessages = true;
    private DateTime? _visibleOldestSentAt;
    private string? _visibleOldestRemoteId;
    private string? _remoteConversationId;
    private int _sending;
    private bool _isRecordingAudio;
    private CancellationTokenSource? _typingCts;
    private bool _typingActive;
    private bool _remoteTyping;
    private bool _initialRemoteSyncCompleted;
    private ChatMessage? _replyingTo;
    private readonly HashSet<int> _selectedMessageIds = new();
    // Diagnostic-only stopwatch for identifying which message templates are realized during chat opening.
    private readonly Stopwatch _chatTemplateTraceClock = Stopwatch.StartNew();
    private int _chatTemplateLoadedTraceCount;
    private bool _selectionMode;
#if ANDROID
    private readonly HashSet<global::Android.Views.View> _longPressViews = new();
#endif
    private bool IsConversationMuted => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_muted_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
    private bool IsConversationArchived => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_archived_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
    private bool IsConversationBlocked => !string.IsNullOrWhiteSpace(_remoteConversationId) && Preferences.Default.Get("himo_blocked_conversations", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(_remoteConversationId, StringComparer.OrdinalIgnoreCase);
#if ANDROID
    private global::Android.Media.MediaRecorder? _audioRecorder;
#endif
    private string? _audioRecordingPath;
#if ANDROID
    private global::Android.Media.MediaPlayer? _audioPlayer;
    private Guid? _audioPlayingMessageId;
    private ImageButton? _audioPlayingButton;
    private MediaElement? _inlineVideoElement;
    private CancellationTokenSource? _inlineVideoProgressCts;
    private bool _inlineVideoSliderUpdating;
    private bool _inlineVideoIsPlaying;
    private bool _inlineVideoIsMuted;
    private bool _inlineVideoWasPlayingBeforeBackground;
#endif
    private readonly HashSet<string> _videoPreviewInFlight = new(StringComparer.OrdinalIgnoreCase);
    private MediaElement? _chatAnimatedWallpaperElement;
    private Window? _wallpaperLifecycleWindow;
    private bool _isAppInBackground;

    // Expensive controls are intentionally constructed on demand instead of during InitializeComponent.
    private Grid? InlineVideoOverlay;
    private ContentView? InlineVideoHost;
    private ActivityIndicator? InlineVideoLoading;
    private Label? InlineVideoTitleLabel;
    private Button? InlineVideoCenterPlayButton;
    private Label? InlineVideoCurrentTimeLabel;
    private Slider? InlineVideoSlider;
    private Label? InlineVideoDurationLabel;
    private Button? InlineVideoRewindButton;
    private Button? InlineVideoPlayButton;
    private Button? InlineVideoForwardButton;
    private Button? InlineVideoMuteButton;
    private Border? AttachmentPanel;

    public string ConversationId
    {
        get => _conversationId.ToString();
        set
        {
            if (int.TryParse(value, out var id) && id > 0)
            {
                PrepareForConversationChange();
                _conversationId = id;
                _remoteConversationId = _chat.Conversations.FirstOrDefault(x => x.Id == id)?.RemoteId;
                SetInitialWallpaperPreview();
                return;
            }

            if (Guid.TryParse(value, out var remoteId))
            {
                PrepareForConversationChange();

                _remoteConversationId = remoteId.ToString("D");

                // Fast local path: the conversation list is cached locally.
                // Resolve the remote GUID to the local integer ID without any
                // network call, so the last cached messages can render immediately.
                var cachedConversation = _chat.Conversations.FirstOrDefault(x =>
                    string.Equals(
                        x.RemoteId,
                        _remoteConversationId,
                        StringComparison.OrdinalIgnoreCase));

                _conversationId = cachedConversation?.Id ?? 0;
                SetInitialWallpaperPreview();
            }
        }
    }

    private void PrepareForConversationChange()
    {
        _openCts?.Cancel();
        StopTyping();
        _replyingTo = null;
        _selectionMode = false;
        _selectedMessageIds.Clear();
        _remoteTyping = false;
        _hasMoreOlderMessages = true;
        _allowOlderPaging = false;
        _initialPositioningLatest = true;
        Interlocked.Exchange(ref _initialScrollInProgress, 0);
        _lastVisibleItemIndex = -1;
        _initialRemoteSyncCompleted = false;
        _localFirstRenderCompleted = false;
        _visibleOldestSentAt = null;
        _visibleOldestRemoteId = null;
        _visibleMessages.Clear();
        if (MessageEntry is not null) MessageEntry.Text = string.Empty;
        if (ReplyPreview is not null) ReplyPreview.IsVisible = false;
        if (MessageSearchBar is not null) MessageSearchBar.IsVisible = false;
        if (MessageSearchEntry is not null) MessageSearchEntry.Text = string.Empty;
        if (ScrollToBottomButton is not null) ScrollToBottomButton.IsVisible = false;
    }

    // These handlers are temporary diagnostics. They report the template type and elapsed
    // time when each item root reaches Loaded, capped to avoid flooding the device log.
    private void ChatTextTemplateLoaded(object? sender, EventArgs e) => TraceChatTemplateLoaded("Text");
    private void ChatImageTemplateLoaded(object? sender, EventArgs e) => TraceChatTemplateLoaded("Image");
    private void ChatVideoTemplateLoaded(object? sender, EventArgs e) => TraceChatTemplateLoaded("Video");
    private void ChatAudioTemplateLoaded(object? sender, EventArgs e) => TraceChatTemplateLoaded("Audio");
    private void ChatFileTemplateLoaded(object? sender, EventArgs e) => TraceChatTemplateLoaded("File");

    private void TraceChatTemplateLoaded(string templateKind)
    {
        var count = Interlocked.Increment(ref _chatTemplateLoadedTraceCount);
        if (count > 40)
            return;

        System.Diagnostics.Debug.WriteLine(
            $"[HimoTemplateTrace] rootLoaded={templateKind} elapsed={_chatTemplateTraceClock.ElapsedMilliseconds}ms count={count}");
    }

    public ChatPage(ChatService chat, HimoApiClient api, AccountService account, INotificationService notifications, HimoRealtimeService realtime)
    {
        System.Diagnostics.Debug.WriteLine("[Himo ChatPage] Constructor started.");
        var constructorTrace = Stopwatch.StartNew();
        InitializeComponent();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] InitializeComponent completed elapsed={constructorTrace.ElapsedMilliseconds}ms");
        // Localization traverses the full visual tree; defer it until after the first frame.
        System.Diagnostics.Debug.WriteLine("[Himo ChatPage] XAML initialized successfully.");
        Messages.ItemsSource = _visibleMessages;
        _chat = chat; _api = api; _account = account; _notifications = notifications; _realtime = realtime;
        _realtime.MessageReceived += OnRealtimeMessageReceived;
        _chat.MessagesChanged += OnChatMessagesChanged;
        _realtime.UserPresenceChanged += OnUserPresenceChanged;
        _realtime.UserTypingChanged += OnUserTypingChanged;
        _realtime.MessageDeliveryChanged += OnMessageDeliveryChanged;
        _realtime.MessageEdited += OnMessageEdited;
        _realtime.MessageDeleted += OnMessageDeleted;
        _realtime.Reconnected += OnRealtimeReconnected;

        _scrollIdleTimer = Dispatcher.CreateTimer();
        _scrollIdleTimer.Interval = TimeSpan.FromMilliseconds(650);
        _scrollIdleTimer.IsRepeating = false;
        _scrollIdleTimer.Tick += OnScrollIdleTimerTick;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        EnsureConversationFeaturesOverlay();
    }

    private void EnsureInlineVideoOverlay()
    {
        if (InlineVideoOverlay is not null)
            return;

        if (ChatRoot.Content is not Grid root)
            throw new InvalidOperationException("تعذر إنشاء واجهة تشغيل الفيديو.");

        var overlay = new Grid
        {
            IsVisible = false,
            ZIndex = 200,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        overlay.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var videoHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        videoHost.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoBackground");

        var loading = new ActivityIndicator
        {
            IsVisible = false,
            IsRunning = true,
            Color = Colors.White,
            WidthRequest = 44,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            ZIndex = 8
        };

        var title = new Label
        {
            Text = "الفيديو",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Start,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        title.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var subtitle = new Label
        {
            Text = "مشغل الفيديو",
            FontSize = 10.5,
            VerticalTextAlignment = TextAlignment.Center
        };
        subtitle.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");

        var titleStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        titleStack.Children.Add(title);
        titleStack.Children.Add(subtitle);

        var close = new Button
        {
            Text = "×",
            WidthRequest = 42,
            HeightRequest = 42,
            Padding = 0,
            FontSize = 26,
            FontAttributes = FontAttributes.Bold,
            BackgroundColor = Color.FromArgb("#4DFFFFFF"),
            CornerRadius = 21
        };
        close.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        close.Clicked += CloseInlineVideoClicked;

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        topGrid.Children.Add(titleStack);
        Grid.SetColumn(titleStack, 0);
        topGrid.Children.Add(close);
        Grid.SetColumn(close, 1);

        var topBar = new Border
        {
            Padding = new Thickness(14, 10, 12, 10),
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 10,
            Content = topGrid
        };
        topBar.BackgroundColor = Color.FromArgb("#B20B0F14");

        var centerPlay = new Button
        {
            Text = "▶",
            WidthRequest = 76,
            HeightRequest = 76,
            Padding = 0,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 38,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            ZIndex = 7
        };
        centerPlay.BackgroundColor = Color.FromArgb("#A66C3FD1");
        centerPlay.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
#if ANDROID
        centerPlay.Clicked += InlineVideoPlayClicked;
#endif

        var currentTime = new Label
        {
            Text = "00:00",
            FontSize = 11.5,
            VerticalTextAlignment = TextAlignment.Center
        };
        currentTime.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            HeightRequest = 26,
            ThumbColor = Colors.White
        };
        slider.SetDynamicResource(Slider.MinimumTrackColorProperty, "HimoAccent");
        slider.MaximumTrackColor = Color.FromArgb("#775B92");
#if ANDROID
        slider.ValueChanged += InlineVideoSliderValueChanged;
#endif

        var duration = new Label
        {
            Text = "00:00",
            FontSize = 11.5,
            VerticalTextAlignment = TextAlignment.Center
        };
        duration.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");

        var timeline = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        timeline.Children.Add(currentTime);
        Grid.SetColumn(currentTime, 0);
        timeline.Children.Add(slider);
        Grid.SetColumn(slider, 1);
        timeline.Children.Add(duration);
        Grid.SetColumn(duration, 2);

        Button MakeTransportButton(string text, int width, int height, double fontSize, string backgroundResource)
        {
            var button = new Button
            {
                Text = text,
                WidthRequest = width,
                HeightRequest = height,
                Padding = 0,
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = height / 2
            };
            button.SetDynamicResource(Button.BackgroundColorProperty, backgroundResource);
            button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
            return button;
        }

        var rewind = MakeTransportButton("↶ 10", 62, 42, 11, "HimoCard");
        var play = MakeTransportButton("▶", 50, 50, 18, "HimoAccent");
        var forward = MakeTransportButton("10 ↷", 62, 42, 11, "HimoCard");
        var mute = MakeTransportButton("🔊", 48, 42, 17, "HimoCard");
#if ANDROID
        rewind.Clicked += InlineVideoRewindClicked;
        play.Clicked += InlineVideoPlayClicked;
        forward.Clicked += InlineVideoForwardClicked;
        mute.Clicked += InlineVideoMuteClicked;
#endif

        var transport = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10,
            HorizontalOptions = LayoutOptions.Center
        };
        transport.Children.Add(rewind); Grid.SetColumn(rewind, 1);
        transport.Children.Add(play); Grid.SetColumn(play, 2);
        transport.Children.Add(forward); Grid.SetColumn(forward, 3);
        transport.Children.Add(mute); Grid.SetColumn(mute, 4);

        var bottomStack = new VerticalStackLayout { Spacing = 8 };
        bottomStack.Children.Add(timeline);
        bottomStack.Children.Add(transport);
        var bottomBar = new Border
        {
            Padding = new Thickness(14, 10, 14, 14),
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            ZIndex = 10,
            Content = bottomStack,
            BackgroundColor = Color.FromArgb("#D90B0F14")
        };

        overlay.Children.Add(videoHost);
        overlay.Children.Add(loading);
        overlay.Children.Add(topBar);
        overlay.Children.Add(centerPlay);
        overlay.Children.Add(bottomBar);

        InlineVideoOverlay = overlay;
        InlineVideoHost = videoHost;
        InlineVideoLoading = loading;
        InlineVideoTitleLabel = title;
        InlineVideoCenterPlayButton = centerPlay;
        InlineVideoCurrentTimeLabel = currentTime;
        InlineVideoSlider = slider;
        InlineVideoDurationLabel = duration;
        InlineVideoRewindButton = rewind;
        InlineVideoPlayButton = play;
        InlineVideoForwardButton = forward;
        InlineVideoMuteButton = mute;

        root.Children.Add(overlay);
        Grid.SetRow(overlay, 0);
        Grid.SetRowSpan(overlay, 4);
    }

    private void EnsureAttachmentPanel()
    {
        if (AttachmentPanel is not null || AttachmentPanelHost is null)
            return;

        var panel = new Border
        {
            IsVisible = false,
            Margin = new Thickness(2, 4, 2, 5),
            Padding = new Thickness(10, 9),
            StrokeThickness = 0.8
        };
        panel.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoCard");
        panel.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        panel.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(20) };

        var headerText = new VerticalStackLayout { Spacing = 0 };
        var heading = new Label { Text = "إرسال وسائط", FontSize = 12, FontAttributes = FontAttributes.Bold };
        heading.SetDynamicResource(Label.TextColorProperty, "HimoPrimary");
        var subheading = new Label { Text = "اختر ما تريد إرساله", FontSize = 9.5 };
        subheading.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
        headerText.Children.Add(heading);
        headerText.Children.Add(subheading);

        var smallLine = new BoxView { WidthRequest = 28, HeightRequest = 2, CornerRadius = 1, Color = Color.FromArgb("#E7E2EF"), VerticalOptions = LayoutOptions.Center };
        var panelHeader = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        panelHeader.Children.Add(headerText); Grid.SetColumn(headerText, 0);
        panelHeader.Children.Add(smallLine); Grid.SetColumn(smallLine, 1);

        VerticalStackLayout MakeOption(string icon, string caption, EventHandler click)
        {
            var option = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Center };
            var iconButton = new ImageButton
            {
                Source = icon,
                WidthRequest = 42,
                HeightRequest = 42,
                Padding = 10,
                CornerRadius = 13,
                BorderWidth = 0.8
            };
            iconButton.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoSurface");
            iconButton.SetDynamicResource(ImageButton.BorderColorProperty, "HimoBorder");
            iconButton.Clicked += click;
            var label = new Label { Text = caption, FontSize = 10, HorizontalTextAlignment = TextAlignment.Center };
            label.SetDynamicResource(Label.TextColorProperty, "HimoTextSecondary");
            option.Children.Add(iconButton);
            option.Children.Add(label);
            return option;
        }

        var options = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8,
            HeightRequest = 76
        };
        var cameraOption = MakeOption("himo_icon_chat_camera.png", "الكاميرا", CameraPanelClicked);
        var photoOption = MakeOption("himo_icon_chat_gallery.png", "الصور", PickPhotoClicked);
        var videoOption = MakeOption("himo_icon_chat_video.png", "الفيديو", PickVideoClicked);
        var fileOption = MakeOption("himo_icon_chat_attachment.png", "ملف", PickFileClicked);
        options.Children.Add(cameraOption); Grid.SetColumn(cameraOption, 0);
        options.Children.Add(photoOption); Grid.SetColumn(photoOption, 1);
        options.Children.Add(videoOption); Grid.SetColumn(videoOption, 2);
        options.Children.Add(fileOption); Grid.SetColumn(fileOption, 3);

        var content = new VerticalStackLayout { Spacing = 7 };
        content.Children.Add(panelHeader);
        content.Children.Add(options);
        panel.Content = content;
        AttachmentPanelHost.Content = panel;
        AttachmentPanel = panel;
    }

    private void HideAttachmentPanel()
    {
        if (AttachmentPanel is not null)
            AttachmentPanel.IsVisible = false;
        if (AttachmentPanelHost is not null)
            AttachmentPanelHost.IsVisible = false;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        // Media messages are intentionally NOT sent automatically after the network returns.
        // The user must press the visible "إعادة الإرسال" button.
        if (!IsConversationVisible)
            return;

        if (e.NetworkAccess == NetworkAccess.Internet)
            MainThread.BeginInvokeOnMainThread(() => RefreshMessagesView(scrollToEnd: false));
    }

    protected override void OnAppearing()
    {
        System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] OnAppearing id={_conversationId} remote={_remoteConversationId ?? "(null)"}.");
        var appearTrace = Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine("[HimoOpenTrace] OnAppearing START");
        base.OnAppearing();
        SetInitialWallpaperPreview();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] after base.OnAppearing elapsed={appearTrace.ElapsedMilliseconds}ms");
        // Defer the expensive legacy icon/palette traversal until after the chat
        // first frame. A synchronous tree walk here was responsible for 80-145
        // dropped frames when opening a conversation.
        System.Diagnostics.Debug.WriteLine("[HimoOpenTrace] legacy appearance traversal skipped for ChatPage");
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] appearance styling scheduled elapsed={appearTrace.ElapsedMilliseconds}ms");
        _ = ApplyChatLocalizationAfterFirstFrameAsync();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] localization scheduled elapsed={appearTrace.ElapsedMilliseconds}ms");
        _isAppInBackground = false;
        AttachWallpaperLifecycleHandlers();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] wallpaper handlers attached elapsed={appearTrace.ElapsedMilliseconds}ms");
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        Volatile.Write(ref _conversationVisible, 1);
        UpdateConversationFeatureState();
        StartConversationExpiryTimer();
        UpdateActiveConversationPreference();
        if (_conversationId > 0)
        {
            var currentConversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (currentConversation is not null)
                BindConversationAvatar(currentConversation);
        }

        _ = ApplySavedChatWallpaperAsync();
        ApplySavedChatBubbleColors();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] ApplySavedChatBubbleColors completed elapsed={appearTrace.ElapsedMilliseconds}ms");
        StartPollingFallback();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] StartPollingFallback completed elapsed={appearTrace.ElapsedMilliseconds}ms");

        // Do not execute the first message bind while Shell is still performing
        // the navigation transition. Let Android draw the ChatPage first, then
        // populate the first message page on the next UI turn.
        _openCts?.Cancel();
        _openCts?.Dispose();
        _openCts = new CancellationTokenSource();
        var openToken = _openCts.Token;

        Dispatcher.Dispatch(() =>
        {
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] InitializeChatAsync dispatched at elapsed={appearTrace.ElapsedMilliseconds}ms");
            _ = InitializeChatAsync(openToken);
        });
    }


    private async Task TraceChatAppearanceAsync()
    {
        var trace = Stopwatch.StartNew();
        try
        {
            await HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(this, skipCollectionViewContents: true, batchSize: 128);
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] appearance traversal completed elapsed={trace.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] appearance traversal failed elapsed={trace.ElapsedMilliseconds}ms error={ex.Message}");
        }
    }
    private async Task ApplyChatLocalizationAfterFirstFrameAsync()
    {
        var localizationTrace = Stopwatch.StartNew();
        try
        {
            // Allow Shell/Android to present the chat page before walking all descendants.
            await Task.Yield();
            if (Himo.Services.AppLocalization.CurrentLanguageCode == "ar")
            {
                await Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(
                    () => Himo.Services.AppLocalization.ApplyPageRootOnly(this)).ConfigureAwait(false);
            }
            else
            {
                await Himo.Services.AppLocalization.ApplyToPageInBatchesAsync(
                    this, batchSize: 128, skipCollectionViewContents: false).ConfigureAwait(false);
            }
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] localization completed elapsed={localizationTrace.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Deferred localization failed: {ex.Message}");
        }
    }
    private int _initializationVersion;
    private int _initializationRunning;
    private CancellationTokenSource? _openCts;
    private bool _localFirstRenderCompleted;
    private int _initialScrollInProgress;
    // Read receipts are allowed only while this ChatPage is actually visible.
    // Merely having a ChatPage instance alive in the Shell must never mark a
    // conversation as read.
    private int _conversationVisible;

    private bool IsConversationVisible => Volatile.Read(ref _conversationVisible) == 1;

    private async Task InitializeChatAsync(CancellationToken cancellationToken)
    {
        var initTrace = Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine("[HimoOpenTrace] InitializeChatAsync START");
        if (Interlocked.Exchange(ref _initializationRunning, 1) != 0)
            return;

        var version = Interlocked.Increment(ref _initializationVersion);

        try
        {
            if (version == Volatile.Read(ref _initializationVersion) &&
                !_localFirstRenderCompleted)
            {
                // Let the current navigation/layout frame complete first.
                // Then render the cached last page immediately; do not wait for network.
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();

                if (version == Volatile.Read(ref _initializationVersion) &&
                    !_localFirstRenderCompleted)
                {
                    Load();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] InitializeChatAsync local initialization finished elapsed={initTrace.ElapsedMilliseconds}ms localFirstRender={_localFirstRenderCompleted} visibleCount={_visibleMessages.Count}");
        _ = _notifications.InitializeAsync();

            // Start remote work immediately after the first cached paint. The previous
            // 350ms defer only delayed the first remote history request without fixing
            // the actual contention point.
            _ = StartDeferredBackgroundWorkAsync(version, cancellationToken);
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] background work scheduled elapsed={initTrace.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Initialize failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _initializationRunning, 0);
        }
    }

    private async Task StartDeferredBackgroundWorkAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            _ = InitializeRemoteSyncAsync(version, cancellationToken);
            _ = ClearConversationNotificationAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task InitializeRemoteSyncAsync(int version, CancellationToken cancellationToken)
    {
        var remoteInitTrace = Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine("[HimoOpenTrace] InitializeRemoteSync START");
        try
        {
            await _api.TokenInitialization.WaitAsync(cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] TokenInitialization completed elapsed={remoteInitTrace.ElapsedMilliseconds}ms");
            if (!_api.HasToken || version != Volatile.Read(ref _initializationVersion))
                return;

            cancellationToken.ThrowIfCancellationRequested();
            await ResolveRemoteConversationAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] ResolveRemoteConversation completed elapsed={remoteInitTrace.ElapsedMilliseconds}ms localId={_conversationId} remote={_remoteConversationId}");

            var avatarConversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (avatarConversation is not null)
                _ = EnsureConversationAvatarAsync(avatarConversation);

            if (version != Volatile.Read(ref _initializationVersion))
                return;

            if (!_localFirstRenderCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await MainThread.InvokeOnMainThreadAsync(Load);
            }

            if (version != Volatile.Read(ref _initializationVersion))
                return;

            // Message history has priority during first open. Start SignalR only
            // after the initial page has been loaded so a slow hub negotiation
            // cannot compete with the history request.
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] calling LoadRemote elapsed={remoteInitTrace.ElapsedMilliseconds}ms localFirstRender={_localFirstRenderCompleted} visibleCount={_visibleMessages.Count}");
            await LoadRemoteSafelyAsync(version, cancellationToken);

            if (version != Volatile.Read(ref _initializationVersion))
                return;

            _ = StartRealtimeSafelyAsync(version, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Remote initialization failed: {ex}");
        }
    }

    private async Task StartRealtimeSafelyAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await StartRealtimeAsync(cancellationToken);
            if (version == Volatile.Read(ref _initializationVersion) &&
                Guid.TryParse(_remoteConversationId, out var remoteId))
            {
                var online = await _realtime.GetPresenceAsync(remoteId);
                await MainThread.InvokeOnMainThreadAsync(() => SetPresenceText(online));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Realtime startup failed: {ex}");
        }
    }

    private async Task LoadRemoteSafelyAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            await LoadRemoteAsync(cancellationToken);

            if (version != Volatile.Read(ref _initializationVersion))
                return;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Remote history sync failed: {ex}");
        }
    }

    private async Task ResolveRemoteConversationAsync()
    {
        // Fast cache-only resolution. This must happen before any HTTP call.
        var remoteConversationId = _remoteConversationId;

        if (_conversationId <= 0 &&
            !string.IsNullOrWhiteSpace(remoteConversationId))
        {
            var cachedRemoteConversationId = remoteConversationId!;
            var cached = _chat.Conversations.FirstOrDefault(x =>
                string.Equals(
                    x.RemoteId,
                    cachedRemoteConversationId,
                    StringComparison.OrdinalIgnoreCase));

            if (cached is not null)
            {
                _conversationId = cached.Id;
            }
        }

        if (_conversationId > 0 &&
            !string.IsNullOrWhiteSpace(_remoteConversationId))
        {
            return;
        }

        // The navigation query already contains the remote GUID. Never block the
        // first message load on GetConversationsAsync(): that endpoint calculates
        // unread counts and display names for every conversation and can be much
        // slower than fetching one conversation's messages. Create a lightweight
        // local placeholder immediately, then hydrate its display metadata in the
        // background.
        if (!string.IsNullOrWhiteSpace(_remoteConversationId) && Guid.TryParse(_remoteConversationId, out var remoteId))
        {
            var existing = _chat.Conversations.FirstOrDefault(x =>
                string.Equals(x.RemoteId, remoteId.ToString("D"), StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                _conversationId = existing.Id;
                return;
            }

            var local = _chat.AddConversation("محادثة");
            local.RemoteId = remoteId.ToString("D");
            _conversationId = local.Id;
            return;
        }

        // Recovery path for legacy local records that do not yet have a remote GUID.
        // Normal Home/Search navigation now passes the remote GUID directly, so this
        // path is no longer used during normal chat opening.
        if (_conversationId <= 0 || !_api.HasToken)
            return;

        try
        {
            var local = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (local is null) return;
            if (Guid.TryParse(local.RemoteId, out var alreadyBound))
            {
                _remoteConversationId = alreadyBound.ToString("D");
                return;
            }

            var remoteList = await _api.GetConversationsAsync();
            var localName = local.Name?.Trim() ?? string.Empty;
            var candidates = remoteList
                .Where(x => string.Equals(x.Name?.Trim(), localName, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            if (candidates.Count == 0) return;

            HimoApiClient.ConversationDto? best = null;
            var bestScore = int.MinValue;
            var tied = false;
            foreach (var candidate in candidates)
            {
                var score = 1; // exact display-name match
                var localLastMessage = local.LastMessage?.Trim();
                if (!string.IsNullOrWhiteSpace(localLastMessage) &&
                    !string.Equals(localLastMessage, "محادثة جديدة", StringComparison.Ordinal) &&
                    string.Equals(localLastMessage, candidate.LastMessage?.Trim(), StringComparison.Ordinal))
                    score += 4;

                var delta = Math.Abs((candidate.UpdatedAt.LocalDateTime - local.UpdatedAt).TotalMinutes);
                if (delta <= 5) score += 2;
                else if (delta <= 60) score += 1;

                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    tied = false;
                }
                else if (score == bestScore)
                {
                    tied = true;
                }
            }

            // Name-only matching is acceptable only when it is unique. When a
            // stronger message/time match exists, accept that unique best match.
            if (best is null || (tied && bestScore < 3))
                return;

            local.RemoteId = best.Id.ToString("D");
            local.LastMessage = best.LastMessage ?? string.Empty;
            local.Time = best.UpdatedAt.LocalDateTime.ToString("HH:mm");
            local.UpdatedAt = best.UpdatedAt.LocalDateTime;
            local.UnreadCount = best.UnreadCount;
            _remoteConversationId = local.RemoteId;

            _conversationId = local.Id;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Local conversation hydration failed: {ex.Message}");
        }
    }

    private void Load()
    {
        var localLoadTrace = Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine("[HimoOpenTrace] Load LOCAL START");
        if (_conversationId == 0)
            return;

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation == null)
            return;

        var displayName = string.IsNullOrWhiteSpace(conversation.Name) ? "محادثة" : conversation.Name.Trim();
        NameLabel?.SetValue(Label.TextProperty, displayName);
        InitialLabel?.SetValue(Label.TextProperty, string.IsNullOrWhiteSpace(conversation.Initial) ? displayName[..1] : conversation.Initial);
        BindConversationAvatar(conversation);

        var recentTrace = Stopwatch.StartNew();
        var recent = _chat.GetRecentMessages(_conversationId, InitialVisibleMessageCount);
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] GetRecentMessages count={recent.Count} elapsed={recentTrace.ElapsedMilliseconds}ms");

        // First paint must contain only cached message metadata. Starting image/video
        // downloads here causes network completion and attachment property changes
        // while CollectionView is measuring the initial message cells, which can
        // produce visible freezes on Android. Remote sync prepares only a small
        // number of recent media previews after the chat is already rendered.
        SetMessagesItemsSource(recent);

        // A local cache may contain more history than the first page.
        // We do not sort/copy the whole history just to determine this.
        var fullCacheTrace = Stopwatch.StartNew();
        var localCount = _chat.GetMessages(_conversationId).Count;
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] GetMessages.Count localCount={localCount} elapsed={fullCacheTrace.ElapsedMilliseconds}ms");
        _hasMoreOlderMessages = localCount > recent.Count || _hasMoreOlderMessages;
        UpdateVisiblePagingCursor();
        EmptyState?.SetValue(IsVisibleProperty, recent.Count == 0);
        _localFirstRenderCompleted = true;

        if (UnreadDivider is not null)
            UnreadDivider.IsVisible = conversation.UnreadCount > 0 && recent.Count > 0;

        // Do not mark the conversation as read here. Load() can run while the
        // page is being prepared or while the page is kept alive off-screen.
        // The server-side read receipt is applied only from the visible-page path.

        if (recent.Count > 0)
            ConfigureInitialChatPosition();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] Load LOCAL END total={localLoadTrace.ElapsedMilliseconds}ms recent={recent.Count}");
    }

    private void BindConversationAvatar(Conversation conversation)
    {
        if (!ReferenceEquals(_avatarBoundConversation, conversation))
        {
            if (_avatarBoundConversation is not null)
                _avatarBoundConversation.PropertyChanged -= AvatarConversationPropertyChanged;

            _avatarBoundConversation = conversation;
            _avatarBoundConversation.PropertyChanged += AvatarConversationPropertyChanged;
        }

        DisplayConversationAvatar(conversation);
        _ = EnsureConversationAvatarAsync(conversation);
    }

    private void AvatarConversationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.PropertyName) &&
            e.PropertyName != nameof(Conversation.ProfilePhotoPath) &&
            e.PropertyName != nameof(Conversation.HasRemoteProfilePhoto) &&
            e.PropertyName != nameof(Conversation.OtherParticipantUserId) &&
            e.PropertyName != nameof(Conversation.ProfilePhotoVersion))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_avatarBoundConversation is Conversation conversation)
                DisplayConversationAvatar(conversation);
        });
    }

    private void DisplayConversationAvatar(Conversation conversation)
    {
        var hasPhoto = conversation.HasLocalProfilePhoto;
        ContactPhotoImage.Source = hasPhoto ? ImageSource.FromFile(conversation.ProfilePhotoPath!) : null;
        ContactPhotoImage.IsVisible = hasPhoto;
        InitialLabel.IsVisible = !hasPhoto;
    }

    private async Task EnsureConversationAvatarAsync(Conversation conversation)
    {
        try
        {
            if (!_api.HasToken) return;

            // A chat opened from a notification can start from a lightweight local
            // placeholder. Hydrate participant/avatar metadata in the background;
            // never block the first message render on this request.
            if (conversation.OtherParticipantUserId is null &&
                Guid.TryParse(conversation.RemoteId, out var remoteConversationId))
            {
                var remote = await _api.GetConversationsAsync();
                var details = remote.FirstOrDefault(x => x.Id == remoteConversationId);
                if (details is not null)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        conversation.OtherParticipantUserId = details.OtherParticipantUserId;
                        conversation.HasRemoteProfilePhoto = details.HasProfilePhoto;
                        conversation.ProfilePhotoVersion = details.ProfilePhotoVersion;
                    });
                }
            }

            if (conversation.OtherParticipantUserId is not Guid userId)
                return;

            var photoVersion = conversation.ProfilePhotoVersion;
            var hasRemotePhoto = conversation.HasRemoteProfilePhoto;
            var path = await ProfilePhotoCache.GetPathAsync(_api, userId, hasRemotePhoto, photoVersion);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (conversation.OtherParticipantUserId == userId &&
                    conversation.ProfilePhotoVersion == photoVersion &&
                    conversation.HasRemoteProfilePhoto == hasRemotePhoto)
                {
                    conversation.ProfilePhotoPath = path;
                    DisplayConversationAvatar(conversation);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Contact avatar load failed: {ex.Message}");
        }
    }

    private List<ChatMessage> GetOrderedLocalMessages()
    {
        return _chat.GetMessages(_conversationId)
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private ChatMessage? GetVisibleOldestMessage()
    {
        return _visibleMessages.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.RemoteId))
            ?? _visibleMessages.FirstOrDefault();
    }

    private void UpdateVisiblePagingCursor()
    {
        var oldest = GetVisibleOldestMessage();
        _visibleOldestSentAt = oldest?.SentAt;
        _visibleOldestRemoteId = oldest?.RemoteId;
    }

    private void AppendNewMessagesToVisible()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(AppendNewMessagesToVisible);
            return;
        }

        if (_visibleMessages.Count == 0)
        {
            var recent = _chat.GetRecentMessages(_conversationId, MessagePageSize);
            SetMessagesItemsSource(recent);
            UpdateVisiblePagingCursor();
            return;
        }

        var all = GetOrderedLocalMessages();

        var currentKeys = _visibleMessages.Select(GetMessageUiKey).ToHashSet(StringComparer.Ordinal);
        var newestVisible = _visibleMessages[^1];
        var additions = all
            .Where(x => CompareMessageOrder(x, newestVisible) > 0 && !currentKeys.Contains(GetMessageUiKey(x)))
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.Id)
            .ToList();

        if (additions.Count > 0)
            _visibleMessages.InsertRange(_visibleMessages.Count, additions);

        UpdateMessageDateSeparators(_visibleMessages.ToList());
    }



    private async void ConversationOptionsClicked(object? sender, EventArgs e)
    {
        var selected = await DisplayActionSheetAsync(
            "خيارات المحادثة",
            "إلغاء",
            null,
            "البحث في المحادثة",
            IsConversationMuted ? "إلغاء كتم الإشعارات" : "كتم الإشعارات",
            "الرسائل ذاتية الاختفاء",
            "خلفية الدردشة",
            "الإبلاغ عن المحادثة",
            IsConversationBlocked ? "إلغاء حظر المحادثة" : "حظر المحادثة",
            IsConversationArchived ? "إلغاء أرشفة المحادثة" : "أرشفة المحادثة");

        if (string.IsNullOrWhiteSpace(selected) || selected == "إلغاء")
            return;

        if (selected == "البحث في المحادثة")
            SearchMessagesClicked(sender, e);
        else if (selected == "كتم الإشعارات" || selected == "إلغاء كتم الإشعارات")
            MuteConversationClicked(sender, e);
        else if (selected == "الرسائل ذاتية الاختفاء")
            DisappearingMessagesMenuClicked(sender, e);
        else if (selected == "خلفية الدردشة")
            ChatBackgroundMenuClicked(sender, e);
        else if (selected == "الإبلاغ عن المحادثة")
            ReportConversationClicked(sender, e);
        else if (selected == "حظر المحادثة" || selected == "إلغاء حظر المحادثة")
            BlockConversationClicked(sender, e);
        else if (selected == "أرشفة المحادثة" || selected == "إلغاء أرشفة المحادثة")
            ArchiveConversationClicked(sender, e);
    }


    private void ConversationMenuBackdropTapped(object? sender, TappedEventArgs e)
    {
        // القائمة أصبحت DisplayActionSheet ولا تحتاج إلى طبقة Overlay.
    }

    private void CloseConversationMenu()
    {
        // لا توجد طبقة Overlay لإغلاقها.
    }

    private void UpdateConversationMenuLabels()
    {
        // يتم إنشاء نص خيار الكتم مباشرة عند فتح القائمة.
    }

    private async void CreateGroupMenuClicked(object? sender, EventArgs e)
    {
        CloseConversationMenu();
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إنشاء مجموعة"), "سيتم إنشاء المجموعة من شاشة المجموعات.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    private void SearchMenuClicked(object? sender, EventArgs e)
    {
        CloseConversationMenu();
        SearchMessagesClicked(sender, e);
    }

    private void MuteMenuClicked(object? sender, EventArgs e)
    {
        CloseConversationMenu();
        MuteConversationClicked(sender, e);
    }

    private async void DisappearingMessagesMenuClicked(object? sender, EventArgs e)
    {
        CloseConversationMenu();
        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (conversation is null)
        {
            await DisplayAlertAsync("الرسائل ذاتية الاختفاء", "تعذر العثور على المحادثة الحالية.", "حسنًا");
            return;
        }
        await Navigation.PushAsync(new DisappearingMessagesPage(
            _api, conversation, ConversationFeatureStore.ConversationKey(conversation)));
    }

    private async void ChatBackgroundMenuClicked(object? sender, EventArgs e)
    {
        CloseConversationMenu();

        var selected = await DisplayActionSheetAsync(
            "خلفية الدردشة",
            "إلغاء",
            null,
            "🌸 الأزهار البنفسجية",
            "🌄 غروب الجبال",
            "🌙 بحيرة القمر",
            "🌺 أزهار قريبة",
            "🌊 بحيرة هادئة",
            "〰️ تموجات بحرية",
            "🌲 غابة زمردية",
            "🌃 مدينة مضيئة",
            "🌸 مدينة ساكورا",
            "🏜️ غروب ذهبي",
            "🖼️ اختيار صورة من المعرض");

        if (selected == "🖼️ اختيار صورة من المعرض")
        {
            await PickConversationWallpaperFromGalleryAsync();
            return;
        }

        var wallpaper = selected switch
        {
            "🌸 الأزهار البنفسجية" => "himo_wallpaper_purple_flowers.jpg",
            "🌄 غروب الجبال" => "himo_wallpaper_mountain_sunset.jpg",
            "🌙 بحيرة القمر" => "himo_wallpaper_moon_lake.jpg",
            "🌺 أزهار قريبة" => "himo_wallpaper_close_flowers.jpg",
            "🌊 بحيرة هادئة" => "himo_wallpaper_quiet_lake.jpg",
            "〰️ تموجات بحرية" => "himo_wallpaper_purple_water.jpg",
            "🌲 غابة زمردية" => "himo_wallpaper_emerald_forest.jpg",
            "🌃 مدينة مضيئة" => "himo_wallpaper_city_lights.jpg",
            "🌸 مدينة ساكورا" => "himo_wallpaper_sakura_city.jpg",
            "🏜️ غروب ذهبي" => "himo_wallpaper_golden_desert.jpg",
            _ => null
        };

        if (wallpaper is null)
            return;

        await ApplyChatWallpaperAsync(wallpaper, persist: true);
    }

    private async Task PickConversationWallpaperFromGalleryAsync()
    {
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "اختر صورة لخلفية هذه المحادثة",
                FileTypes = FilePickerFileType.Images
            });
            if (picked is null) return;

            var extension = Path.GetExtension(picked.FileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8 || !extension.StartsWith('.')) extension = ".img";
            var destination = Path.Combine(FileSystem.Current.AppDataDirectory, $"himo_chat_wallpaper_{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
            await using (var input = await picked.OpenReadAsync())
            await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output);

            await ApplyChatWallpaperAsync("local:" + destination, persist: true);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("خلفية الدردشة"), "تعذر استخدام الصورة: " + ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void SetInitialWallpaperPreview()
    {
        if (ChatWallpaper is null)
            return;

        try
        {
            var key = !string.IsNullOrWhiteSpace(_remoteConversationId)
                ? $"himo_chat_background_{_remoteConversationId}"
                : $"himo_chat_background_local_{_conversationId}";
            var selected = Preferences.Default.Get(key, string.Empty);

            if (string.IsNullOrWhiteSpace(selected))
            {
                var mode = Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo");
                if (string.Equals(mode, "animated", StringComparison.OrdinalIgnoreCase))
                {
                    SetChatWallpaperPreview("himo_chat_preview_animated_flowers.jpg");
                    return;
                }

                selected = string.Equals(mode, "theme", StringComparison.OrdinalIgnoreCase)
                    ? Preferences.Default.Get(ChatThemePage.ThemeBackgroundKey, "himo_theme_purple_flow.png")
                    : Preferences.Default.Get(ChatWallpaperSettingsPage.GlobalWallpaperKey, "himo_wallpaper_purple_flowers.jpg");
            }

            if (selected.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            {
                var localPath = selected.Substring("local:".Length);
                if (File.Exists(localPath))
                {
                    ChatWallpaper.Source = ImageSource.FromFile(localPath);
                    ChatWallpaper.Aspect = Aspect.AspectFill;
                    ChatWallpaper.Opacity = 1.0;
                    ChatWallpaper.IsVisible = true;
                    return;
                }

                SetChatWallpaperPreview("himo_chat_preview_purple_flowers.jpg");
                return;
            }

            var normalized = NormalizeChatWallpaperFile(selected);
            SetChatWallpaperPreview(GetChatWallpaperPreviewAsset(normalized));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatWallpaper] Fast preview selection failed: {ex.Message}");
            if (ChatWallpaper.Source is null)
                SetChatWallpaperPreview("himo_chat_preview_purple_flowers.jpg");
        }
    }

    private static string GetChatWallpaperPreviewAsset(string wallpaperFile) => wallpaperFile switch
    {
        "himo_wallpaper_purple_flowers.jpg" => "himo_chat_preview_purple_flowers.jpg",
        "himo_wallpaper_mountain_sunset.jpg" => "himo_chat_preview_mountain_sunset.jpg",
        "himo_wallpaper_moon_lake.jpg" => "himo_chat_preview_moon_lake.jpg",
        "himo_wallpaper_close_flowers.jpg" => "himo_chat_preview_close_flowers.jpg",
        "himo_wallpaper_quiet_lake.jpg" => "himo_chat_preview_quiet_lake.jpg",
        "himo_wallpaper_purple_water.jpg" => "himo_chat_preview_purple_water.jpg",
        "himo_wallpaper_emerald_forest.jpg" => "himo_chat_preview_emerald_forest.jpg",
        "himo_wallpaper_city_lights.jpg" => "himo_chat_preview_city_lights.jpg",
        "himo_wallpaper_sakura_city.jpg" => "himo_chat_preview_sakura_city.jpg",
        "himo_wallpaper_golden_desert.jpg" => "himo_chat_preview_golden_desert.jpg",
        "himo_theme_purple_flow.png" => "himo_chat_preview_theme_purple_flow.jpg",
        "himo_theme_blue_ocean.png" => "himo_chat_preview_theme_blue_ocean.jpg",
        "himo_theme_emerald_flow.png" => "himo_chat_preview_theme_emerald_flow.jpg",
        "himo_theme_rose_glass.png" => "himo_chat_preview_theme_rose_glass.jpg",
        "himo_theme_sunset_silk.png" => "himo_chat_preview_theme_sunset_silk.jpg",
        "himo_theme_neon_orbit.png" => "himo_chat_preview_theme_neon_orbit.jpg",
        "himo_theme_gold_glow.png" => "himo_chat_preview_theme_gold_glow.jpg",
        "himo_theme_aurora.png" => "himo_chat_preview_theme_aurora.jpg",
        _ => "himo_chat_preview_purple_flowers.jpg"
    };

    private void SetChatWallpaperPreview(string assetName)
    {
        if (ChatWallpaper is null)
            return;

        ChatWallpaper.Source = ImageSource.FromFile(assetName);
        ChatWallpaper.Aspect = Aspect.AspectFill;
        ChatWallpaper.Opacity = 1.0;
        ChatWallpaper.IsVisible = true;
    }
    private static bool IsPackagedStaticChatWallpaper(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = NormalizeChatWallpaperFile(value);
        return normalized is
            "himo_wallpaper_purple_flowers.jpg" or
            "himo_wallpaper_mountain_sunset.jpg" or
            "himo_wallpaper_moon_lake.jpg" or
            "himo_wallpaper_close_flowers.jpg" or
            "himo_wallpaper_quiet_lake.jpg" or
            "himo_wallpaper_purple_water.jpg" or
            "himo_wallpaper_emerald_forest.jpg" or
            "himo_wallpaper_city_lights.jpg" or
            "himo_wallpaper_sakura_city.jpg" or
            "himo_wallpaper_golden_desert.jpg" or
            "himo_theme_purple_flow.png" or
            "himo_theme_blue_ocean.png" or
            "himo_theme_emerald_flow.png" or
            "himo_theme_rose_glass.png" or
            "himo_theme_sunset_silk.png" or
            "himo_theme_neon_orbit.png" or
            "himo_theme_gold_glow.png" or
            "himo_theme_aurora.png";
    }

    private void KeepBundledChatWallpaperPreviewDuringOpen(string wallpaper)
    {
        // SetInitialWallpaperPreview() has already selected the matching packaged
        // preview. Do not immediately decode and swap in the full-size image while
        // the initial message cells are being laid out on Android.
        StopChatAnimatedWallpaper();
        if (ChatWallpaper is not null)
        {
            ChatWallpaper.Aspect = Aspect.AspectFill;
            ChatWallpaper.Opacity = 1.0;
            ChatWallpaper.IsVisible = true;
        }
        System.Diagnostics.Debug.WriteLine($"[Himo ChatWallpaper] Keeping packaged preview during chat open: {wallpaper}");
    }

    private async Task ApplySavedChatWallpaperAsync()
    {
        var key = !string.IsNullOrWhiteSpace(_remoteConversationId)
            ? $"himo_chat_background_{_remoteConversationId}"
            : $"himo_chat_background_local_{_conversationId}";

        // A wallpaper selected for this specific conversation keeps priority over
        // the global animated/photo/theme choice.
        var saved = Preferences.Default.Get(key, string.Empty);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            if (IsPackagedStaticChatWallpaper(saved))
            {
                KeepBundledChatWallpaperPreviewDuringOpen(NormalizeChatWallpaperFile(saved));
                return;
            }

            await ApplyChatWallpaperAsync(saved, persist: false);
            return;
        }

        var mode = Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo");
        if (string.Equals(mode, "animated", StringComparison.OrdinalIgnoreCase))
        {
            var animated = Preferences.Default.Get(ChatAnimatedWallpaperPage.GlobalAnimatedWallpaperKey, "url:https://videos.pexels.com/video-files/5138001/5138001-hd_1280_720_30fps.mp4");
            await ApplyChatAnimatedWallpaperAsync(animated);
            return;
        }

        saved = string.Equals(mode, "theme", StringComparison.OrdinalIgnoreCase)
            ? Preferences.Default.Get(ChatThemePage.ThemeBackgroundKey, "himo_theme_purple_flow.png")
            : Preferences.Default.Get(ChatWallpaperSettingsPage.GlobalWallpaperKey, "himo_wallpaper_purple_flowers.jpg");

        if (IsPackagedStaticChatWallpaper(saved))
        {
            KeepBundledChatWallpaperPreviewDuringOpen(NormalizeChatWallpaperFile(saved));
            return;
        }

        await ApplyChatWallpaperAsync(saved, persist: false);
    }
    private async Task ApplyChatAnimatedWallpaperAsync(string animatedValue)
    {
        if (ChatAnimatedWallpaperHost is null || ChatWallpaper is null) return;

        try
        {
            StopChatAnimatedWallpaper();
            var mediaPath = await ChatAnimatedWallpaperPage.ResolveVideoPathAsync(animatedValue);
            if (_isAppInBackground || !IsConversationVisible)
            {
                Debug.WriteLine("[Himo AnimatedWallpaper] Skipping player creation because chat is not in foreground.");
                return;
            }
            var media = new MediaElement
            {
                Aspect = Aspect.AspectFill,
                AndroidViewType = CommunityToolkit.Maui.Core.AndroidViewType.TextureView,
                // Never start playback while the app's window is in the background.
                ShouldAutoPlay = !_isAppInBackground,
                ShouldLoopPlayback = true,
                ShouldShowPlaybackControls = false,
                ShouldKeepScreenOn = false,
                ShouldMute = true,
                Volume = 0,
                InputTransparent = true,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            };

            media.MediaFailed += (_, args) => MainThread.BeginInvokeOnMainThread(async () =>
            {
                System.Diagnostics.Debug.WriteLine($"[Himo AnimatedWallpaper] Playback failed: {args?.ErrorMessage}");
                await ApplyStaticWallpaperFallbackAsync();
            });

            ChatAnimatedWallpaperHost.Content = media;
            ChatAnimatedWallpaperHost.IsVisible = true;
            ChatWallpaper.IsVisible = false;
            _chatAnimatedWallpaperElement = media;
            media.Source = MediaSource.FromFile(mediaPath);
            if (_isAppInBackground)
            {
                try { media.Pause(); } catch { }
            }
            System.Diagnostics.Debug.WriteLine($"[Himo AnimatedWallpaper] Loaded real video file {Path.GetFileName(mediaPath)}; appBackground={_isAppInBackground}.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo AnimatedWallpaper] Could not load wallpaper: {ex}");
            await ApplyStaticWallpaperFallbackAsync();
        }
    }

    private async Task ApplyStaticWallpaperFallbackAsync()
    {
        var previousMode = Preferences.Default.Get(ChatAnimatedWallpaperPage.PreviousBackgroundModeKey, "photo");
        previousMode = string.Equals(previousMode, "theme", StringComparison.OrdinalIgnoreCase) ? "theme" : "photo";
        // If the device cannot decode the chosen clip, stop retrying it on every chat open.
        Preferences.Default.Set(ChatThemePage.BackgroundModeKey, previousMode);
        var fallback = previousMode == "theme"
            ? Preferences.Default.Get(ChatThemePage.ThemeBackgroundKey, "himo_theme_purple_flow.png")
            : Preferences.Default.Get(ChatWallpaperSettingsPage.GlobalWallpaperKey, "himo_wallpaper_purple_flowers.jpg");
        await ApplyChatWallpaperAsync(fallback, persist: false);
    }

    public void NotifyNativeActivityPaused()
    {
        _isAppInBackground = true;

        void StopAndReleaseWallpaperPlayer()
        {
            var media = _chatAnimatedWallpaperElement;
            _chatAnimatedWallpaperElement = null;
            if (media is not null)
            {
                try { media.ShouldAutoPlay = false; } catch { }
                try { media.Pause(); } catch { }
                try { media.Stop(); } catch { }
                try { media.Source = null; } catch { }
            }

            if (ChatAnimatedWallpaperHost is not null)
            {
                ChatAnimatedWallpaperHost.Content = null;
                ChatAnimatedWallpaperHost.IsVisible = false;
            }

            // Drop the native handler so the Android decoder/surface cannot keep
            // rendering the old MediaElement after the app leaves the foreground.
            if (media is not null)
            {
                try { media.Handler?.DisconnectHandler(); } catch { }
            }

            if (ChatWallpaper is not null)
                ChatWallpaper.IsVisible = true;

            Debug.WriteLine("[Himo AnimatedWallpaper] Native Activity paused: player stopped, source cleared, handler disconnected.");
        }

        if (MainThread.IsMainThread)
            StopAndReleaseWallpaperPlayer();
        else
            MainThread.BeginInvokeOnMainThread(StopAndReleaseWallpaperPlayer);
    }

    public void NotifyNativeActivityResumed()
    {
        // Reuse the same guarded resume path used by the MAUI Window lifecycle.
        // It restores the saved wallpaper only if this conversation is visible.
        if (!_isAppInBackground)
            return;

        OnWallpaperWindowResumed(this, EventArgs.Empty);
    }
    private void AttachWallpaperLifecycleHandlers()
    {
        var currentWindow = Window;
        if (currentWindow is null || ReferenceEquals(currentWindow, _wallpaperLifecycleWindow))
            return;

        DetachWallpaperLifecycleHandlers();
        _wallpaperLifecycleWindow = currentWindow;
        _wallpaperLifecycleWindow.Stopped += OnWallpaperWindowStopped;
        _wallpaperLifecycleWindow.Resumed += OnWallpaperWindowResumed;
    }

    private void DetachWallpaperLifecycleHandlers()
    {
        if (_wallpaperLifecycleWindow is null)
            return;

        _wallpaperLifecycleWindow.Stopped -= OnWallpaperWindowStopped;
        _wallpaperLifecycleWindow.Resumed -= OnWallpaperWindowResumed;
        _wallpaperLifecycleWindow = null;
    }

    private void OnWallpaperWindowStopped(object? sender, EventArgs e)
    {
        _isAppInBackground = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Stop and detach the native video view instead of merely pausing it.
            // This prevents decoder/surface playback from continuing while Himo is backgrounded.
            try
            {
                StopChatAnimatedWallpaper();
                if (ChatWallpaper is not null)
                    ChatWallpaper.IsVisible = true;
                Debug.WriteLine("[Himo AnimatedWallpaper] Playback stopped and view detached on background.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Himo AnimatedWallpaper] Stop on background failed: {ex.Message}");
            }
#if ANDROID
            _inlineVideoWasPlayingBeforeBackground = _inlineVideoIsPlaying;
            try { _inlineVideoElement?.Pause(); }
            catch (Exception ex) { Debug.WriteLine($"[Himo InlineVideo] Pause on background failed: {ex.Message}"); }
            if (_inlineVideoWasPlayingBeforeBackground)
            {
                _inlineVideoIsPlaying = false;
                SetInlineVideoPlaybackVisual(false);
                StopInlineVideoProgressLoop();
            }
#endif
        });
    }

    private void OnWallpaperWindowResumed(object? sender, EventArgs e)
    {
        var wasInBackground = _isAppInBackground;
        _isAppInBackground = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!IsConversationVisible)
            {
#if ANDROID
                _inlineVideoWasPlayingBeforeBackground = false;
#endif
                return;
            }

            // Recreate a wallpaper player only after Himo returns to foreground.
            // The old native player was stopped and removed when the window stopped.
            if (wasInBackground &&
                string.Equals(Preferences.Default.Get(ChatThemePage.BackgroundModeKey, "photo"), "animated", StringComparison.OrdinalIgnoreCase))
            {
                _ = RestoreAnimatedWallpaperAfterForegroundAsync();
            }
#if ANDROID
            if (_inlineVideoWasPlayingBeforeBackground &&
                _inlineVideoElement is not null && InlineVideoOverlay?.IsVisible == true)
            {
                try
                {
                    _inlineVideoElement.Play();
                    _inlineVideoIsPlaying = true;
                    SetInlineVideoPlaybackVisual(true);
                    StartInlineVideoProgressLoop();
                }
                catch (Exception ex) { Debug.WriteLine($"[Himo InlineVideo] Resume playback failed: {ex.Message}"); }
            }
            _inlineVideoWasPlayingBeforeBackground = false;
#endif
        });
    }

    private async Task RestoreAnimatedWallpaperAfterForegroundAsync()
    {
        try
        {
            if (!IsConversationVisible || _isAppInBackground)
                return;
            await ApplySavedChatWallpaperAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Himo AnimatedWallpaper] Restore in foreground failed: {ex.Message}");
        }
    }

    private void StopChatAnimatedWallpaper()
    {
        var media = _chatAnimatedWallpaperElement;
        _chatAnimatedWallpaperElement = null;
        if (media is not null)
        {
            try { media.Pause(); } catch { }
            try { media.Stop(); } catch { }
            // Clearing the source releases the decoder and prevents later automatic replay.
            try { media.Source = null; } catch { }
        }
        if (ChatAnimatedWallpaperHost is not null)
        {
            ChatAnimatedWallpaperHost.Content = null;
            ChatAnimatedWallpaperHost.IsVisible = false;
        }
    }

    private static string NormalizeChatWallpaperFile(string? value)
    {
        return value switch
        {
            // Migrate selections stored by the previous code-drawn wallpaper version.
            "flowers" => "himo_wallpaper_purple_flowers.jpg",
            "mountains" => "himo_wallpaper_mountain_sunset.jpg",
            "moon" => "himo_wallpaper_moon_lake.jpg",
            "closeflowers" => "himo_wallpaper_close_flowers.jpg",
            "quietlake" => "himo_wallpaper_quiet_lake.jpg",
            "waves" => "himo_wallpaper_purple_water.jpg",
            "himo_wallpaper_purple_flowers.jpg" => "himo_wallpaper_purple_flowers.jpg",
            "himo_wallpaper_mountain_sunset.jpg" => "himo_wallpaper_mountain_sunset.jpg",
            "himo_wallpaper_moon_lake.jpg" => "himo_wallpaper_moon_lake.jpg",
            "himo_wallpaper_close_flowers.jpg" => "himo_wallpaper_close_flowers.jpg",
            "himo_wallpaper_quiet_lake.jpg" => "himo_wallpaper_quiet_lake.jpg",
            "himo_wallpaper_purple_water.jpg" => "himo_wallpaper_purple_water.jpg",
            "himo_wallpaper_emerald_forest.jpg" => "himo_wallpaper_emerald_forest.jpg",
            "himo_wallpaper_city_lights.jpg" => "himo_wallpaper_city_lights.jpg",
            "himo_wallpaper_sakura_city.jpg" => "himo_wallpaper_sakura_city.jpg",
            "himo_wallpaper_golden_desert.jpg" => "himo_wallpaper_golden_desert.jpg",
            "himo_theme_purple_flow.png" => "himo_theme_purple_flow.png",
            "himo_theme_blue_ocean.png" => "himo_theme_blue_ocean.png",
            "himo_theme_emerald_flow.png" => "himo_theme_emerald_flow.png",
            "himo_theme_rose_glass.png" => "himo_theme_rose_glass.png",
            "himo_theme_sunset_silk.png" => "himo_theme_sunset_silk.png",
            "himo_theme_neon_orbit.png" => "himo_theme_neon_orbit.png",
            "himo_theme_gold_glow.png" => "himo_theme_gold_glow.png",
            "himo_theme_aurora.png" => "himo_theme_aurora.png",
            _ => "himo_wallpaper_purple_flowers.jpg"
        };
    }

    private async Task ApplyChatWallpaperAsync(string fileName, bool persist)
    {
        if (ChatWallpaper is null)
            return;

        StopChatAnimatedWallpaper();
        ChatWallpaper.IsVisible = true;

        var isLocalFile = fileName.StartsWith("local:", StringComparison.OrdinalIgnoreCase);
        var normalizedFile = isLocalFile ? fileName : NormalizeChatWallpaperFile(fileName);

        try
        {
            byte[] imageBytes;
            if (isLocalFile)
            {
                var localPath = fileName.Substring("local:".Length);
                if (!File.Exists(localPath))
                    throw new FileNotFoundException("The selected wallpaper file was not found.", localPath);
                imageBytes = await File.ReadAllBytesAsync(localPath);
            }
            else
            {
                // MAUI includes files under Resources/Raw in the application package.
                await using var sourceStream = await FileSystem.Current.OpenAppPackageFileAsync(normalizedFile);
                using var memory = new MemoryStream();
                await sourceStream.CopyToAsync(memory);
                imageBytes = memory.ToArray();
            }

            ChatWallpaper.Source = ImageSource.FromStream(() => new MemoryStream(imageBytes, writable: false));
            ChatWallpaper.Aspect = Aspect.AspectFill;
            ChatWallpaper.Opacity = 1.0;

            if (persist)
            {
                var key = !string.IsNullOrWhiteSpace(_remoteConversationId)
                    ? $"himo_chat_background_{_remoteConversationId}"
                    : $"himo_chat_background_local_{_conversationId}";
                Preferences.Default.Set(key, normalizedFile);
            }
        }
        catch (Exception ex)
        {
            // Never leave the chat on a blank/white background when an asset cannot be opened.
            // Keep the already visible selected-wallpaper preview instead of
            // clearing the image and exposing the purple page background.
            if (ChatWallpaper.Source is null)
                ChatWallpaper.Source = ImageSource.FromFile("himo_chat_preview_purple_flowers.jpg");
            ChatWallpaper.BackgroundColor = Colors.Transparent;
            System.Diagnostics.Debug.WriteLine($"[Himo ChatWallpaper] Failed to load {normalizedFile}: {ex}");
        }
    }

    private void ApplySavedChatBubbleColors()
    {
        // If the user has not explicitly customized a side, keep the brush's dynamic
        // theme colors. Previously hard-coded purple fallbacks overrode every app theme.
        var preferences = Preferences.Default;
        var hasOutgoingChoice = preferences.ContainsKey(ChatBubbleSettingsPage.OutgoingStartKey)
            && preferences.ContainsKey(ChatBubbleSettingsPage.OutgoingEndKey);
        var hasIncomingChoice = preferences.ContainsKey(ChatBubbleSettingsPage.IncomingStartKey)
            && preferences.ContainsKey(ChatBubbleSettingsPage.IncomingEndKey);

        if (hasOutgoingChoice)
        {
            var outgoingStart = SafePreferenceColor(ChatBubbleSettingsPage.OutgoingStartKey, "#D34DFF");
            var outgoingEnd = SafePreferenceColor(ChatBubbleSettingsPage.OutgoingEndKey, "#6814C7");
            ApplyGradientColors("SenderBubbleBrush", outgoingStart, outgoingEnd);
        }

        if (hasIncomingChoice)
        {
            var incomingStart = SafePreferenceColor(ChatBubbleSettingsPage.IncomingStartKey, "#2F1946");
            var incomingEnd = SafePreferenceColor(ChatBubbleSettingsPage.IncomingEndKey, "#1A0D29");
            ApplyGradientColors("ReceiverBubbleBrush", incomingStart, incomingEnd);
        }
    }

    private static Color SafePreferenceColor(string key, string defaultValue)
    {
        try { return Color.FromArgb(Preferences.Default.Get(key, defaultValue)); }
        catch { return Color.FromArgb(defaultValue); }
    }

    private void ApplyGradientColors(string resourceKey, Color start, Color end)
    {
        if (!Resources.TryGetValue(resourceKey, out var value) || value is not LinearGradientBrush gradient || gradient.GradientStops.Count == 0)
            return;

        foreach (var stop in gradient.GradientStops)
        {
            var t = Math.Clamp(stop.Offset, 0f, 1f);
            stop.Color = new Color(
                start.Red + ((end.Red - start.Red) * t),
                start.Green + ((end.Green - start.Green) * t),
                start.Blue + ((end.Blue - start.Blue) * t),
                start.Alpha + ((end.Alpha - start.Alpha) * t));
        }
    }

    private async void ReportConversationClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId)) return;
        var reason = await DisplayPromptAsync("الإبلاغ عن المحادثة", "اكتب سبب البلاغ:", "إرسال", "إلغاء", "السبب", 200, Keyboard.Text);
        if (string.IsNullOrWhiteSpace(reason)) return;

        try
        {
            await _api.ReportConversationAsync(conversationId, reason);
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الإبلاغ"), Himo.Services.AppLocalization.TranslateSourceText("تم إرسال البلاغ."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الإبلاغ"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }


    private async void BlockConversationClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;
        var blocked = IsConversationBlocked;
        var values = Preferences.Default.Get("himo_blocked_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, _remoteConversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!blocked) values.Add(_remoteConversationId);
        Preferences.Default.Set("himo_blocked_conversations", string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase)));
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المحادثة"), blocked ? Himo.Services.AppLocalization.TranslateSourceText("تم إلغاء حظر المحادثة.") : Himo.Services.AppLocalization.TranslateSourceText("تم حظر المحادثة."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    private async void ArchiveConversationClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;
        var archived = IsConversationArchived;
        var values = Preferences.Default.Get("himo_archived_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, _remoteConversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!archived) values.Add(_remoteConversationId);
        Preferences.Default.Set("himo_archived_conversations", string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase)));
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المحادثة"), archived ? "تم إلغاء أرشفة المحادثة." : Himo.Services.AppLocalization.TranslateSourceText("تمت أرشفة المحادثة."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    private async void AudioCallClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId))
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المكالمة"), "المحادثة غير متصلة بالخادم بعد.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        try
        {
            await SwipePageTransition.NavigateAsync(this, $"CallPage?id={conversationId:D}&mode=audio", SwipeDirection.Left);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المكالمة"), $"تعذر فتح شاشة المكالمة: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void VideoCallClicked(object? sender, EventArgs e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId))
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المكالمة"), "المحادثة غير متصلة بالخادم بعد.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        try
        {
            await SwipePageTransition.NavigateAsync(this, $"CallPage?id={conversationId:D}&mode=video", SwipeDirection.Left);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المكالمة"), $"تعذر فتح شاشة المكالمة: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void MuteConversationClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_remoteConversationId)) return;
        var muted = IsConversationMuted;
        var values = Preferences.Default.Get("himo_muted_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, _remoteConversationId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!muted) values.Add(_remoteConversationId);
        Preferences.Default.Set("himo_muted_conversations", string.Join(',', values.Distinct(StringComparer.OrdinalIgnoreCase)));
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الإشعارات"), muted ? "تم إلغاء كتم إشعارات المحادثة." : Himo.Services.AppLocalization.TranslateSourceText("تم كتم إشعارات المحادثة."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
    }

    protected override void OnDisappearing()
    {
        ClearActiveConversationPreference();
        Volatile.Write(ref _conversationVisible, 0);
        StopConversationExpiryTimer();
        DetachWallpaperLifecycleHandlers();
        StopChatAnimatedWallpaper();
#if ANDROID
        if (_isRecordingAudio) CleanupAudioRecorder();
        StopAudioPlayback();
        CloseInlineVideo();
#endif
        if (_avatarBoundConversation is not null)
        {
            _avatarBoundConversation.PropertyChanged -= AvatarConversationPropertyChanged;
            _avatarBoundConversation = null;
        }
        Volatile.Write(ref _conversationVisible, 0);
        StopPolling();
        StopTyping();
        _openCts?.Cancel();
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        base.OnDisappearing();
    }

    // Incoming calls are routed centrally by PushNotificationManager.
    // ChatPage must not show a second DisplayActionSheet for the same Invite.

    private async Task StartRealtimeAsync(CancellationToken cancellationToken = default)
    {
        await _realtime.StartAsync(cancellationToken);
    }

    private void OnRealtimeReconnected(object? sender, EventArgs e)
    {
        // SignalR may reconnect after a network interruption while the page is still
        // visible. Pull missed messages and flush the durable outbox with the same
        // ClientMessageId values so a lost HTTP response cannot create duplicates.
        _ = LoadRemoteAsync();
        if (Guid.TryParse(_remoteConversationId, out var remoteId))
            _ = RefreshPresenceAfterReconnectAsync(remoteId);
    }

    private int _flushingOutbox;
    // Newly composed text messages bypass the serialized background outbox so an
    // older retry or attachment upload cannot hold up the message the user just sent.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _directMessageSends =
        new(StringComparer.OrdinalIgnoreCase);

    private async Task FlushOutboxAsync(string? prioritizeClientMessageId = null)
    {
        if (!_api.HasToken ||
            Connectivity.Current.NetworkAccess != NetworkAccess.Internet ||
            Interlocked.Exchange(ref _flushingOutbox, 1) != 0)
            return;

        try
        {
            while (_api.HasToken)
            {
                var pendingBatch = _chat.GetPendingMessages()
                    // Directly-sent new text messages are handled by their own request.
                    // Keep them out of the serialized retry queue.
                    .Where(x => string.IsNullOrWhiteSpace(x.ClientMessageId) ||
                                !_directMessageSends.ContainsKey(x.ClientMessageId))
                    .OrderBy(x => !string.IsNullOrWhiteSpace(prioritizeClientMessageId) &&
                                  string.Equals(x.ClientMessageId, prioritizeClientMessageId, StringComparison.Ordinal)
                        ? 0 : 1)
                    .ThenBy(x => x.SentAt)
                    .ThenBy(x => x.Id)
                    .ToList();

                if (pendingBatch.Count == 0)
                    break;

                var madeProgress = false;

                foreach (var pending in pendingBatch)
                {
                    if (!_api.HasToken)
                        break;

                    // A newer send can be marked direct after this batch snapshot was taken.
                    if (!string.IsNullOrWhiteSpace(pending.ClientMessageId) &&
                        _directMessageSends.ContainsKey(pending.ClientMessageId))
                        continue;

                    var conversation = _chat.Conversations
                        .FirstOrDefault(x => x.Id == pending.ConversationId);

                    if (conversation is null || string.IsNullOrWhiteSpace(pending.ClientMessageId))
                        continue;

                    if (!Guid.TryParse(conversation.RemoteId, out var conversationId))
                    {
                        try
                        {
                            await ResolveRemoteConversationAsync();
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[Himo ChatPage] Pending conversation resolve failed: {ex.Message}");
                        }

                        conversation = _chat.Conversations
                            .FirstOrDefault(x => x.Id == pending.ConversationId);

                        if (conversation is null ||
                            !Guid.TryParse(conversation.RemoteId, out conversationId))
                        {
                            continue;
                        }
                    }

                    var clientMessageId = pending.ClientMessageId;
                    _chat.MarkPendingSending(clientMessageId);

                    try
                    {
                        if (pending.IsAttachment)
                        {
                            var localPath = pending.AttachmentLocalPath;
                            if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
                            {
                                _chat.FailPendingMessage(pending.ConversationId, clientMessageId);
                                System.Diagnostics.Debug.WriteLine(
                                    $"[Himo ChatPage] Pending attachment file is missing: {pending.AttachmentFileName}");
                                continue;
                            }

                            await using var stream = File.OpenRead(localPath);
                            var remoteAttachment = await _api.UploadAttachmentAsync(
                                conversationId,
                                stream,
                                pending.AttachmentFileName ?? Path.GetFileName(localPath),
                                pending.AttachmentContentType ?? "application/octet-stream");

                            _chat.CompletePendingAttachmentMessage(
                                pending,
                                remoteAttachment.Id.ToString("D"),
                                remoteAttachment.SentAt.LocalDateTime);

                            madeProgress = true;
                            continue;
                        }

                        var replyId = Guid.TryParse(
                            pending.ReplyToRemoteId,
                            out var parsedReplyId)
                            ? parsedReplyId
                            : (Guid?)null;

                        var remote = await _api.SendMessageAsync(
                            conversationId,
                            pending.Text,
                            clientMessageId,
                            replyId);

                        _chat.CompletePendingMessage(
                            pending.ConversationId,
                            clientMessageId,
                            remote.Id.ToString("D"),
                            remote.SentAt.LocalDateTime);

                        madeProgress = true;
                    }
                    catch (OperationCanceledException)
                    {
                        _chat.FailPendingMessage(
                            pending.ConversationId,
                            clientMessageId);

                        if (!_api.HasToken)
                            break;
                    }
                    catch (Exception ex)
                    {
                        _chat.FailPendingMessage(
                            pending.ConversationId,
                            clientMessageId);

                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Outbox send failed for {clientMessageId}: {ex.Message}");
                    }

                    await Task.Yield();
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                    RefreshMessagesView(scrollToEnd: false));

                if (!madeProgress)
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _flushingOutbox, 0);

            var queuedPending = _chat.GetPendingMessages()
                .Where(x => string.IsNullOrWhiteSpace(x.ClientMessageId) ||
                            !_directMessageSends.ContainsKey(x.ClientMessageId))
                .Any();

            if (_api.HasToken && queuedPending)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    await FlushOutboxAsync();
                });
            }
        }
    }

    private async Task SendPendingTextImmediatelyAsync(
        int localConversationId,
        Guid remoteConversationId,
        string text,
        string clientMessageId,
        string? replyToRemoteId)
    {
        var timing = Stopwatch.StartNew();
        try
        {
            if (!_api.HasToken)
                throw new InvalidOperationException("جلسة الدخول غير متاحة حاليًا.");
            if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
                throw new HttpRequestException("لا يوجد اتصال إنترنت متاح لإرسال الرسالة.");

            var replyId = Guid.TryParse(replyToRemoteId, out var parsedReplyId)
                ? parsedReplyId
                : (Guid?)null;

            System.Diagnostics.Debug.WriteLine(
                $"[HimoTiming] direct text send started conversation={remoteConversationId:D} clientMessageId={clientMessageId}");

            var remote = await _api.SendMessageAsync(
                remoteConversationId,
                text,
                clientMessageId,
                replyId);

            _chat.CompletePendingMessage(
                localConversationId,
                clientMessageId,
                remote.Id.ToString("D"),
                remote.SentAt.LocalDateTime);

            System.Diagnostics.Debug.WriteLine(
                $"[HimoTiming] direct text send acknowledged elapsed={timing.ElapsedMilliseconds}ms remoteId={remote.Id:D}");
        }
        catch (Exception ex)
        {
            _chat.FailPendingMessage(localConversationId, clientMessageId);
            System.Diagnostics.Debug.WriteLine(
                $"[HimoTiming] direct text send failed elapsed={timing.ElapsedMilliseconds}ms clientMessageId={clientMessageId}: {ex}");
        }
        finally
        {
            _directMessageSends.TryRemove(clientMessageId, out _);
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() => RefreshMessagesView(scrollToEnd: false));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Refresh after direct send failed: {ex.Message}");
            }

            // Once the foreground message is handled, continue draining older queued items.
            _ = FlushOutboxAsync();
        }
    }

    private async Task RefreshPresenceAfterReconnectAsync(Guid conversationId)
    {
        var online = await _realtime.GetPresenceAsync(conversationId);
        SetPresenceText(online);
    }

    private void OnMessageEdited(object? sender, MessageDto message)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId) || message.ConversationId != conversationId) return;
        if (_chat.UpdateMessageText(message.Id.ToString("D"), message.Text, DateTime.TryParse(message.EditedAt, out var edited) ? edited : null))
            RefreshMessagesView(scrollToEnd: false);
    }

    private void OnMessageDeleted(object? sender, MessageDto message)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId) || message.ConversationId != conversationId) return;
        if (_chat.UpdateMessageDeleted(message.Id.ToString("D")))
            RefreshMessagesView(scrollToEnd: false);
    }

    private void OnMessageDeliveryChanged(object? sender, (Guid MessageId, Guid UserId, string Status) e)
    {
        // ChatMessage.DeliveryStatus raises PropertyChanged, so rebuilding the
        // entire CollectionView for every receipt only creates unnecessary UI work.
        _chat.UpdateDeliveryStatus(e.MessageId.ToString("D"), e.Status);
    }

    private void OnUserPresenceChanged(object? sender, (Guid UserId, bool IsOnline) e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId)) return;
        if (conversationId == Guid.Empty) return;
        SetPresenceText(e.IsOnline);
    }

    private void OnUserTypingChanged(object? sender, (Guid UserId, Guid ConversationId, bool IsTyping) e)
    {
        if (!Guid.TryParse(_remoteConversationId, out var conversationId) || e.ConversationId != conversationId) return;
        _remoteTyping = e.IsTyping;
        if (StatusLabel is not null)
            StatusLabel.Text = e.IsTyping ? "يكتب الآن..." : "متصل الآن";
    }

    private void SetPresenceText(bool isOnline)
    {
        if (_remoteTyping) return;
        if (StatusLabel is not null) StatusLabel.Text = isOnline ? "متصل الآن" : "غير متصل";
        if (StatusDot is not null)
        {
            StatusDot.Fill = isOnline
                ? new SolidColorBrush(Color.FromArgb("#49D486"))
                : new SolidColorBrush(Color.FromArgb("#A7A0B2"));
        }
    }

    private void MessageEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _conversationId == 0 || !Guid.TryParse(_remoteConversationId, out var conversationId)) return;
        var hasText = !string.IsNullOrWhiteSpace(e.NewTextValue);
        _typingCts?.Cancel();
        if (!hasText)
        {
            if (_typingActive)
            {
                _typingActive = false;
                _ = _realtime.SetTypingAsync(conversationId, false);
            }
            return;
        }

        if (!_typingActive)
        {
            _typingActive = true;
            _ = _realtime.SetTypingAsync(conversationId, true);
        }

        _typingCts = new CancellationTokenSource();
        var token = _typingCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                _typingActive = false;
                await _realtime.SetTypingAsync(conversationId, false, token);
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private void StopTyping()
    {
        _typingCts?.Cancel();
        _typingCts?.Dispose();
        _typingCts = null;
        if (_typingActive)
        {
            _typingActive = false;
            if (Guid.TryParse(_remoteConversationId, out var conversationId))
                _ = _realtime.SetTypingAsync(conversationId, false);
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("..", false);
    }

    private async Task ClearConversationNotificationAsync()
    {
        try
        {
            var notificationConversationId = !string.IsNullOrWhiteSpace(_remoteConversationId)
                ? _remoteConversationId
                : (_conversationId == 0 ? string.Empty : _conversationId.ToString());

            if (string.IsNullOrWhiteSpace(notificationConversationId))
                return;

            await _notifications.ClearConversationAsync(notificationConversationId);
        }
        catch
        {
            // Notification cleanup must never prevent opening a chat.
        }
    }

    private void StartPollingFallback()
    {
        StopPolling();
        if (!_api.HasToken) return;
        _pollCts = new CancellationTokenSource();
        _ = PollMessagesAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await LoadRemoteAsync(cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private void OnChatMessagesChanged(int localConversationId)
    {
        if (!IsConversationVisible)
            return;

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == localConversationId);
        var matchesCurrent = localConversationId == _conversationId ||
            (conversation is not null && !string.IsNullOrWhiteSpace(_remoteConversationId) &&
             string.Equals(conversation.RemoteId, _remoteConversationId, StringComparison.OrdinalIgnoreCase));
        if (!matchesCurrent)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!IsConversationVisible) return;
            if (_conversationId <= 0) _conversationId = localConversationId;
            RefreshMessagesView(scrollToEnd: true);
            EmptyState?.SetValue(IsVisibleProperty, _visibleMessages.Count == 0);
        });
    }

    private void UpdateActiveConversationPreference()
    {
        if (!IsConversationVisible) return;
        var remoteId = _remoteConversationId;
        if (string.IsNullOrWhiteSpace(remoteId) && _conversationId > 0)
            remoteId = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId)?.RemoteId;
        if (!string.IsNullOrWhiteSpace(remoteId) && Guid.TryParse(remoteId, out _))
            Preferences.Default.Set(ActiveConversationPreferenceKey, remoteId);
    }

    private void ClearActiveConversationPreference()
    {
        var activeId = Preferences.Default.Get(ActiveConversationPreferenceKey, string.Empty);
        var currentId = _remoteConversationId;
        if (string.IsNullOrWhiteSpace(currentId) && _conversationId > 0)
            currentId = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId)?.RemoteId;
        if (!string.IsNullOrWhiteSpace(currentId) &&
            string.Equals(activeId, currentId, StringComparison.OrdinalIgnoreCase))
            Preferences.Default.Remove(ActiveConversationPreferenceKey);
    }

    private void OnRealtimeMessageReceived(object? sender, MessageDto message)
    {
        // Persist even if this ChatPage did not find a local row yet. Messages can
        // arrive before HomeViewModel finishes its first server refresh, particularly
        // when the app was opened from a notification.
        _ = HandleRealtimeMessageAsync(message);
    }

    private async Task HandleRealtimeMessageAsync(MessageDto message)
    {
        try
        {
            if (!_account.IsSignedIn || message.ConversationId == Guid.Empty)
                return;

            var remoteConversationId = message.ConversationId.ToString("D");
            var isCurrentRemoteConversation = !string.IsNullOrWhiteSpace(_remoteConversationId) &&
                string.Equals(_remoteConversationId, remoteConversationId, StringComparison.OrdinalIgnoreCase);

            var conversation = _chat.Conversations.FirstOrDefault(x =>
                string.Equals(x.RemoteId, remoteConversationId, StringComparison.OrdinalIgnoreCase));

            // Do not drop the incoming message just because a local conversation row
            // has not been hydrated yet. Create a stable remote-keyed local row first.
            if (conversation is null && isCurrentRemoteConversation)
            {
                conversation = await MainThread.InvokeOnMainThreadAsync(() =>
                    _chat.EnsureRemoteConversation(message.ConversationId, message.SenderPhoneNumber));
            }

            // The global push manager persists messages for other conversations.
            if (conversation is null)
                return;

            if (!Preferences.Default.Get(ChatSettingsPage.KeepArchivedKey, true))
            {
                var archived = Preferences.Default.Get("himo_archived_conversations", string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(x => !string.Equals(x, conversation.RemoteId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                Preferences.Default.Set("himo_archived_conversations", string.Join(',', archived));
            }

            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (isCurrentRemoteConversation && _conversationId <= 0)
                    _conversationId = conversation!.Id;

                _chat.AddRemoteMessage(conversation!.Id, message.Text, message.SentAt.LocalDateTime, isMine,
                    message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType,
                    message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                    message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted, message.ExpiresAt?.LocalDateTime);
            });

            var isCurrentConversation = isCurrentRemoteConversation || conversation.Id == _conversationId;
            if (!isCurrentConversation)
                return;

            var isVisible = IsConversationVisible;
            if (isVisible)
            {
                UpdateActiveConversationPreference();
                await MainThread.InvokeOnMainThreadAsync(() => _chat.MarkAsRead(conversation.Id));
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (_visibleMessages.Count == 0)
                {
                    var recent = _chat.GetRecentMessages(conversation.Id, InitialVisibleMessageCount);
                    SetMessagesItemsSource(recent);
                    UpdateVisiblePagingCursor();
                    EmptyState?.SetValue(IsVisibleProperty, recent.Count == 0);
                }
                else
                {
                    var wasNearBottom = _lastVisibleItemIndex < 0 ||
                                         _lastVisibleItemIndex >= Math.Max(0, _visibleMessages.Count - 3);
                    AppendNewMessagesToVisible();
                    EmptyState?.SetValue(IsVisibleProperty, _visibleMessages.Count == 0);
                    if (isVisible && wasNearBottom && _visibleMessages.Count > 0)
                        ScrollToLatestMessage(animate: false);
                }
            });

            if (isVisible)
            {
                _ = ClearConversationNotificationAsync();
                _ = MarkRemoteConversationReadAsync(message.ConversationId);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Incoming realtime message handling failed: {ex}");
        }
    }

    private async Task MarkRemoteConversationReadAsync(Guid conversationId)
    {
        try { await _api.MarkConversationReadAsync(conversationId); } catch { }
    }

    private async Task LoadRemoteAsync(CancellationToken cancellationToken = default)
    {
        var timing = Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[HimoTiming] LoadRemote START conversation={_remoteConversationId} localId={_conversationId}");
        await _api.TokenInitialization.ConfigureAwait(false);
        if (!_api.HasToken) return;
        if (!await _remoteLoadGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteConversationId))
                return;

            var wasInitialSync = !_initialRemoteSyncCompleted;
            var cacheBeforeSync = _chat.GetMessages(_conversationId);
            var messageCountBeforeSync = cacheBeforeSync.Count;

            IReadOnlyList<MessageDto> remoteMessages;
            if (wasInitialSync)
            {
                // Only the newest page is fetched when the chat opens.
                remoteMessages = await _api.GetMessagesAsync(
                    remoteConversationId,
                    limit: MessagePageSize,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                System.Diagnostics.Debug.WriteLine($"[HimoTiming] LoadRemote API returned count={remoteMessages.Count} elapsed={timing.ElapsedMilliseconds}ms");
            }
            else
            {
                // Always refresh the most recent page. Do not use a device-local
                // timestamp cursor here: the previous server compared ISO TEXT values
                // lexicographically, which can skip messages across UTC offsets.
                // This also refreshes delivered/read receipts for already-cached messages.
                remoteMessages = await _api.GetMessagesAsync(
                    remoteConversationId,
                    limit: MessageSyncPageSize,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _initialRemoteSyncCompleted = true;

            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            foreach (var message in remoteMessages)
            {
                var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
                // Delivery is acknowledged at the realtime/FCM application level,
                // not only while this conversation page is open.

                var added = _chat.AddRemoteMessage(
                    _conversationId, message.Text, message.SentAt.LocalDateTime, isMine,
                    message.Id.ToString(), message.AttachmentFileName, message.AttachmentContentType,
                    message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                    message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted, message.ExpiresAt?.LocalDateTime);

                if (added && !isMine)
                    _ = _realtime.MarkMessageDeliveredAsync(message.Id);

            }

            var allMessages = _chat.GetMessages(_conversationId);
            var receivedNewMessages = allMessages.Count > messageCountBeforeSync;
            var shouldMarkRead = IsConversationVisible && (wasInitialSync || receivedNewMessages);

            // Update the local unread count immediately, but never wait for the
            // notification plugin or the server's read receipt before showing messages.
            if (shouldMarkRead)
                await MainThread.InvokeOnMainThreadAsync(() => _chat.MarkAsRead(_conversationId)).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (wasInitialSync)
                {
                    var recent = allMessages
                        .OrderBy(x => x.SentAt)
                        .ThenBy(x => x.Id)
                        .TakeLast(InitialVisibleMessageCount)
                        .ToList();
                    var bindTimer = Stopwatch.StartNew();
                    SetMessagesItemsSource(recent);
                    System.Diagnostics.Debug.WriteLine($"[HimoTiming] Bind messages count={recent.Count} elapsed={bindTimer.ElapsedMilliseconds}ms total={timing.ElapsedMilliseconds}ms");
                    _hasMoreOlderMessages = allMessages.Count > recent.Count || remoteMessages.Count >= MessagePageSize;
                    UpdateVisiblePagingCursor();
                    EmptyState?.SetValue(IsVisibleProperty, recent.Count == 0);
                    if (recent.Count > 0) ConfigureInitialChatPosition();

                }
                else if (receivedNewMessages)
                {
                    AppendNewMessagesToVisible();
                }
            }).ConfigureAwait(false);

            if (shouldMarkRead)
            {
                // Both acknowledgements are independent of first paint. Run them after
                // binding, so slow push-notification cleanup or network receipts cannot
                // add seconds to the perceived chat opening time.
                _ = ClearConversationNotificationAsync();
                _ = MarkRemoteConversationReadAsync(remoteConversationId);
            }

            System.Diagnostics.Debug.WriteLine($"[HimoTiming] LoadRemote END total={timing.ElapsedMilliseconds}ms");
            QueueVisibleMediaPreparation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] LoadRemote failed: {ex}");
        }
        finally
        {
            _remoteLoadGate.Release();
        }
    }

    private async Task LoadOlderMessagesAsync()
    {
        if (_conversationId == 0 || !_api.HasToken || !_hasMoreOlderMessages)
            return;
        if (Interlocked.Exchange(ref _loadingOlder, 1) != 0)
            return;

        try
        {
            if (!_initialRemoteSyncCompleted)
            {
                await LoadRemoteAsync().ConfigureAwait(false);
                if (!_initialRemoteSyncCompleted) return;
            }

            var anchor = GetVisibleOldestMessage();
            if (anchor is null) return;

            // First consume locally cached history. This is instant and mirrors the
            // local-first behavior of mature chat apps. Only when local history is
            // exhausted do we request the next page from the server.
            var allLocal = GetOrderedLocalMessages();
            var anchorKey = GetMessageUiKey(anchor);
            var anchorIndex = allLocal.FindIndex(x => string.Equals(GetMessageUiKey(x), anchorKey, StringComparison.Ordinal));

            if (anchorIndex > 0)
            {
                var startIndex = Math.Max(0, anchorIndex - MessagePageSize);
                var cachedOlder = allLocal.Skip(startIndex).Take(anchorIndex - startIndex).ToList();
                if (cachedOlder.Count > 0)
                {
                    await PrependOlderMessagesAsync(cachedOlder, anchor).ConfigureAwait(false);
                    _hasMoreOlderMessages = startIndex > 0 || _hasMoreOlderMessages;
                    return;
                }
            }

            var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
            if (conversation?.RemoteId is not string remoteId || !Guid.TryParse(remoteId, out var remoteConversationId))
                return;

            var before = _visibleOldestSentAt.HasValue
                ? new DateTimeOffset(_visibleOldestSentAt.Value)
                : new DateTimeOffset(anchor.SentAt);
            Guid? beforeId = Guid.TryParse(_visibleOldestRemoteId, out var parsedBeforeId) ? parsedBeforeId : null;

            var older = await _api.GetMessagesAsync(
                remoteConversationId,
                before: before,
                beforeId: beforeId,
                limit: MessagePageSize,
                cancellationToken: default).ConfigureAwait(false);

            if (older.Count == 0)
            {
                _hasMoreOlderMessages = false;
                return;
            }

            var myUserId = _account.CurrentAccount?.UserId ?? Guid.Empty;
            foreach (var message in older)
            {
                var isMine = myUserId != Guid.Empty && message.SenderUserId == myUserId;
                _chat.AddRemoteMessage(
                    _conversationId, message.Text, message.SentAt.LocalDateTime, isMine,
                    message.Id.ToString("D"), message.AttachmentFileName, message.AttachmentContentType,
                    message.AttachmentSize, message.Status, message.ReplyToMessageId?.ToString("D"),
                    message.ReplyToText, message.IsEdited, message.EditedAt, message.IsDeleted, message.ExpiresAt?.LocalDateTime);
            }

            var currentCache = _chat.GetMessages(_conversationId);
            var olderKeys = older.Select(x => x.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var olderLocal = currentCache
                .Where(x => !string.IsNullOrWhiteSpace(x.RemoteId) && olderKeys.Contains(x.RemoteId))
                .OrderBy(x => x.SentAt)
                .ThenBy(x => x.Id)
                .ToList();

            if (olderLocal.Count == 0)
            {
                _hasMoreOlderMessages = false;
                return;
            }

            await PrependOlderMessagesAsync(olderLocal, anchor).ConfigureAwait(false);
            _hasMoreOlderMessages = older.Count >= MessagePageSize;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Load older messages failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _loadingOlder, 0);
        }
    }

    private async Task PrependOlderMessagesAsync(IReadOnlyList<ChatMessage> olderMessages, ChatMessage anchor)
    {
        if (olderMessages.Count == 0) return;

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var existing = _visibleMessages.Select(GetMessageUiKey).ToHashSet(StringComparer.Ordinal);
            var toInsert = olderMessages
                .Where(x => existing.Add(GetMessageUiKey(x)))
                .OrderBy(x => x.SentAt)
                .ThenBy(x => x.Id)
                .ToList();

            if (toInsert.Count == 0) return;

            _visibleMessages.InsertRange(0, toInsert);
            UpdateMessageDateSeparators(_visibleMessages.ToList());
            UpdateVisiblePagingCursor();

            await Task.Delay(60);
            await SafeScrollTo(anchor, ScrollToPosition.Start, animate: false);
        });
    }

    private async void RecordAudioClicked(object? sender, EventArgs e)
    {
        if (_isRecordingAudio)
        {
            await StopAndSendAudioAsync();
            return;
        }

#if ANDROID
        try
        {
            if (_conversationId == 0) return;
            var permission = await Permissions.RequestAsync<Permissions.Microphone>();
            if (permission != PermissionStatus.Granted)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الرسائل الصوتية"), "يلزم السماح باستخدام الميكروفون لتسجيل رسالة صوتية.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            _audioRecordingPath = Path.Combine(FileSystem.Current.CacheDirectory, $"himo_voice_{Guid.NewGuid():N}.m4a");
#pragma warning disable CA1422 // MediaRecorder is the available Android API for this recording path; no supported .NET replacement is exposed here.
            _audioRecorder = new global::Android.Media.MediaRecorder();
#pragma warning restore CA1422
            _audioRecorder.SetAudioSource(global::Android.Media.AudioSource.Mic);
            _audioRecorder.SetOutputFormat(global::Android.Media.OutputFormat.Mpeg4);
            _audioRecorder.SetAudioEncoder(global::Android.Media.AudioEncoder.Aac);
            _audioRecorder.SetAudioEncodingBitRate(64000);
            _audioRecorder.SetAudioSamplingRate(44100);
            _audioRecorder.SetOutputFile(_audioRecordingPath);
            _audioRecorder.Prepare();
            _audioRecorder.Start();
            _isRecordingAudio = true;
            RecordButton.Source = ChatIconSource("stop");
            RecordingBar.IsVisible = true;
            MessageEntry.IsEnabled = false;
            SendButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            CleanupAudioRecorder();
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الرسائل الصوتية"), $"تعذر بدء التسجيل: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
#else
        await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الرسائل الصوتية"), "تسجيل الصوت متاح حاليًا على Android.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
#endif
    }

    private async Task StopAndSendAudioAsync()
    {
#if ANDROID
        var path = _audioRecordingPath;
        try
        {
            _audioRecorder?.Stop();
            _audioRecorder?.Reset();
            _audioRecorder?.Release();
            _audioRecorder = null;
            _isRecordingAudio = false;
            RecordButton.Source = ChatIconSource("mic");
            RecordingBar.IsVisible = false;
            MessageEntry.IsEnabled = true;
            SendButton.IsEnabled = true;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > 25L * 1024 * 1024)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الرسائل الصوتية"), "تعذر إرسال التسجيل أو تجاوز حجمه 25 ميجابايت.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            SendButton.IsEnabled = false;
            var stagedPath = await StageAttachmentForOutboxAsync(path, Path.GetFileName(path));
            var fileName = Path.GetFileName(stagedPath);
            var localMessage = _chat.AddPendingAttachmentMessage(
                _conversationId,
                fileName,
                "audio/mp4",
                info.Length,
                stagedPath);
            if (localMessage is null)
                return;

            // Optimistic UI: show the voice card immediately while the upload runs.
            RefreshMessagesView(scrollToEnd: true);

            try
            {
                if (_api.HasToken && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                {
                    await FlushOutboxAsync();
                }
                else
                {
                    // Keep the recording on disk, but mark the message as failed so the
                    // user gets an explicit "إعادة الإرسال" button instead of auto-send.
                    localMessage.DeliveryStatus = "failed";
                    localMessage.IsPending = true;
                    RefreshMessagesView(scrollToEnd: true);
                }
            }
            catch
            {
                localMessage.DeliveryStatus = "failed";
                localMessage.IsPending = true;
                throw;
            }
        }
        catch (Exception ex)
        {
            CleanupAudioRecorder();
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الرسائل الصوتية"), $"تعذر إرسال التسجيل: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            _audioRecordingPath = null;
            MessageEntry.IsEnabled = true;
            SendButton.IsEnabled = true;
        }
#else
        await Task.CompletedTask;
#endif
    }

    private async void AudioClicked(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is not ImageButton button || button.BindingContext is not ChatMessage message || !message.IsAudioAttachment || !Guid.TryParse(message.RemoteId, out var messageId)) return;

        try
        {
            // Tapping the same voice message toggles pause/resume.
            if (_audioPlayer is not null && _audioPlayingMessageId == messageId)
            {
                if (_audioPlayer.IsPlaying)
                {
                    _audioPlayer.Pause();
                    SetAudioButtonIcon(button, playing: false);
                }
                else
                {
                    _audioPlayer.Start();
                    SetAudioButtonIcon(button, playing: true);
                }
                return;
            }

            StopAudioPlayback();

            var path = message.AttachmentLocalPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "voice.m4a");
                message.AttachmentLocalPath = path;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0)
                throw new IOException("لم يتم تنزيل ملف التسجيل من الخادم.");

            var file = new Java.IO.File(path);
            var uri = global::Android.Net.Uri.FromFile(file);
            var player = global::Android.Media.MediaPlayer.Create(
                global::Android.App.Application.Context, uri);

            if (player is null)
                throw new InvalidOperationException("تعذر إنشاء مشغل الصوت لهذا الملف.");

            _audioPlayer = player;
            _audioPlayingMessageId = messageId;
            _audioPlayingButton = button;
            player.SetVolume(1f, 1f);
            SetAudioButtonIcon(button, playing: true);
            player.Completion += (_, _) => MainThread.BeginInvokeOnMainThread(StopAudioPlayback);
            player.Error += (_, args) =>
            {
                var what = args?.What;
                var extra = args?.Extra;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    StopAudioPlayback();
                    await DisplayAlertAsync(
                        Himo.Services.AppLocalization.TranslateSourceText("الرسالة الصوتية"),
                        $"تعذر تشغيل التسجيل الصوتي. MediaPlayer: {what}/{extra}",
                        Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                });
            };

            player.Start();
        }
        catch (Exception ex)
        {
            StopAudioPlayback();
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الرسالة الصوتية"), $"تعذر تشغيل التسجيل: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private void AudioButtonLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is ImageButton button)
            SetAudioButtonIcon(button, playing: false);
#endif
    }

    private static string ChatIconSource(string iconName)
    {
        var baseFile = $"himo_icon_chat_{iconName}.png";
        var styleId = HimoAppearanceManager.ActiveStyleId;
        return styleId switch
        {
            "d_classic" or "d_sky" or "d_light" or "d_graphite" or "d_mint" or "d_rose" or
            "p_royal_gold" or "p_electric_blue" or "p_neon_violet" or "p_emerald" or "p_ruby" or
            "p_silver" or "p_aurora" or "p_rose_gold" => $"himo_theme_{styleId}_{baseFile}",
            _ => baseFile
        };
    }

    private static void SetAudioButtonIcon(ImageButton button, bool playing)
    {
        var icon = ChatIconSource(playing ? "pause" : "play");
        MainThread.BeginInvokeOnMainThread(() => button.Source = icon);
    }

    private void StopAudioPlayback()
    {
        try { _audioPlayer?.Stop(); } catch { }
        try { _audioPlayer?.Release(); } catch { }
        _audioPlayer = null;
        _audioPlayingMessageId = null;
        var button = _audioPlayingButton;
        _audioPlayingButton = null;
        if (button is not null)
        {
            var playIcon = ChatIconSource("play");
            MainThread.BeginInvokeOnMainThread(() => button.Source = playIcon);
        }
    }

    private void CleanupAudioRecorder()
    {
        try { _audioRecorder?.Reset(); } catch { }
        try { _audioRecorder?.Release(); } catch { }
        _audioRecorder = null;
        _isRecordingAudio = false;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (RecordButton is not null) RecordButton.Source = ChatIconSource("mic");
            if (RecordingBar is not null) RecordingBar.IsVisible = false;
            if (MessageEntry is not null) MessageEntry.IsEnabled = true;
            if (SendButton is not null) SendButton.IsEnabled = true;
        });
    }
#endif

    private void AttachmentButtonClicked(object? sender, EventArgs e)
    {
        EnsureAttachmentPanel();
        if (AttachmentPanel is null || AttachmentPanelHost is null)
            return;

        var show = !AttachmentPanel.IsVisible;
        AttachmentPanel.IsVisible = show;
        AttachmentPanelHost.IsVisible = show;
    }

    private async void CameraButtonClicked(object? sender, EventArgs e)
    {
        await CaptureMediaAsync();
    }

    private async void CameraPanelClicked(object? sender, EventArgs e)
    {
        HideAttachmentPanel();
        await CaptureMediaAsync();
    }

    private bool _lastAttachmentUploadSucceeded;

    private async Task CaptureMediaAsync()
    {
        if (_conversationId == 0)
            return;

        try
        {
            HideAttachmentPanel();

            // The capture screen is part of Himo itself. It no longer launches
            // the device Camera application or a MediaPicker capture activity.
            // CameraPage now owns the complete photo flow:
            // capture -> editor -> explicit Send -> return here.
            // Nothing is uploaded while the camera or editor is still open.
            var edited = await CameraPageHelpers.OpenAsync(CameraCaptureMode.Photo);
            if (edited is null)
                return;

            await UploadPickedAttachmentAsync(
                new FileResult(edited.Capture.FilePath, edited.Capture.ContentType));

            if (_lastAttachmentUploadSucceeded && !string.IsNullOrWhiteSpace(edited.Caption))
                await SendCaptionAsMessageAsync(edited.Caption);
        }
        catch (PermissionException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("صلاحية الكاميرا"), "اسمح لـ Himo باستخدام الكاميرا والميكروفون ثم حاول مرة أخرى.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الكاميرا"), $"تعذر فتح كاميرا Himo: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void PickPhotoClicked(object? sender, EventArgs e)
    {
        HideAttachmentPanel();
        try
        {
            if (_conversationId == 0) return;
            var files = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "اختر صورة",
                SelectionLimit = 1
            });
            var file = files.FirstOrDefault();
            if (file is not null)
            {
                var edited = await EditPickedMediaAsync(file);
                if (edited is not null)
                    await UploadPickedAttachmentAsync(edited);
            }
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الصور"), "اختيار الصور غير مدعوم على هذا الجهاز.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (PermissionException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الصور"), "تعذر الوصول إلى الصور. اسمح للتطبيق بالوصول ثم حاول مرة أخرى.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الصور"), $"تعذر اختيار الصورة: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void PickVideoClicked(object? sender, EventArgs e)
    {
        HideAttachmentPanel();
        try
        {
            if (_conversationId == 0) return;
            var files = await MediaPicker.Default.PickVideosAsync(new MediaPickerOptions
            {
                Title = "اختر فيديو",
                SelectionLimit = 1
            });
            var file = files.FirstOrDefault();
            if (file is not null)
            {
                var edited = await EditPickedMediaAsync(file);
                if (edited is not null)
                    await UploadPickedAttachmentAsync(edited);
            }
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفيديو"), "اختيار الفيديو غير مدعوم على هذا الجهاز.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (PermissionException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفيديو"), "تعذر الوصول إلى الفيديوهات. اسمح للتطبيق بالوصول ثم حاول مرة أخرى.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفيديو"), $"تعذر اختيار الفيديو: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async Task<FileResult?> EditPickedMediaAsync(FileResult file)
    {
        try
        {
            var sourcePath = file.FullPath;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                sourcePath = Path.Combine(
                    FileSystem.CacheDirectory,
                    $"Himo_edit_source_{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");

                await using var source = await file.OpenReadAsync();
                await using var destination = File.Create(sourcePath);
                await source.CopyToAsync(destination);
            }

            var edited = await MediaEditorPage.EditAsync(
                sourcePath,
                file.FileName,
                file.ContentType);

            if (edited is null)
                return null;

            return new FileResult(edited.FilePath, edited.ContentType);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("تحرير الوسائط"),
                $"تعذر فتح محرر الوسائط: {ex.Message}",
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return null;
        }
    }

    private async void PickFileClicked(object? sender, EventArgs e)
    {
        HideAttachmentPanel();
        try
        {
            if (_conversationId == 0) return;
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "اختر ملفًا"
            });
            await UploadPickedAttachmentAsync(file);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), $"تعذر اختيار الملف: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async Task UploadPickedAttachmentAsync(FileResult? file)
    {
        _lastAttachmentUploadSucceeded = false;

        try
        {
            if (file is null) return;
            if (_conversationId == 0) return;


            if (file.FileName.Length > 180)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), "اسم الملف طويل جدًا.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            var contentType = file.ContentType;
            if (string.IsNullOrWhiteSpace(contentType))
                contentType = GuessAttachmentContentType(file.FileName);

            var extension = Path.GetExtension(file.FileName);
            var isVideo = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".3gp", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".3g2", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mpeg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".ogv", StringComparison.OrdinalIgnoreCase);

            const long maxRegularAttachmentBytes = 25L * 1024 * 1024;
            const long maxVideoAttachmentBytes = 100L * 1024 * 1024;
            var maxBytes = isVideo ? maxVideoAttachmentBytes : maxRegularAttachmentBytes;

            await using (var selectedStream = await file.OpenReadAsync())
            {
                if (selectedStream.CanSeek && selectedStream.Length > maxBytes)
                {
                    var limitText = isVideo ? "100 ميجابايت" : "25 ميجابايت";
                    await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), $"الحد الأقصى لحجم {(isVideo ? "الفيديو" : "الملف")} {limitText}.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                    return;
                }
            }

            long? attachmentSize = null;
            try
            {
                await using var sizeStream = await file.OpenReadAsync();
                if (sizeStream.CanSeek) attachmentSize = sizeStream.Length;
            }
            catch { }

            // Always persist a local copy before attempting upload. This is what
            // makes offline photo/video capture behave like a messaging app: the
            // message exists locally even when the network is unavailable.
            var stagedPath = await StageAttachmentForOutboxAsync(file);
            var localMessage = _chat.AddPendingAttachmentMessage(
                _conversationId,
                Path.GetFileName(stagedPath),
                contentType,
                attachmentSize,
                stagedPath);
            if (localMessage is null) return;

            RefreshMessagesView(scrollToEnd: true);

            try
            {
                if (_api.HasToken && Connectivity.Current.NetworkAccess == NetworkAccess.Internet)
                {
                    await FlushOutboxAsync();
                    _lastAttachmentUploadSucceeded = string.Equals(localMessage.DeliveryStatus, "sent", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // The local copy stays in HimoOutbox. Mark the message as failed so
                    // the user explicitly controls when the upload is attempted.
                    localMessage.DeliveryStatus = "failed";
                    localMessage.IsPending = true;
                    RefreshMessagesView(scrollToEnd: true);
                }
            }
            catch
            {
                localMessage.DeliveryStatus = "failed";
                localMessage.IsPending = true;
                throw;
            }
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (FeatureNotSupportedException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), "هذه الميزة غير مدعومة على الجهاز الحالي.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (PermissionException)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), "يلزم السماح بالوصول إلى الوسائط ثم المحاولة مرة أخرى.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفقات"), $"تعذر إرسال المرفق: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            if (SendButton is not null) SendButton.IsEnabled = true;
        }
    }

    private async Task SendCaptionAsMessageAsync(string caption)
    {
        var text = caption?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var previousText = MessageEntry?.Text ?? string.Empty;
        try
        {
            if (MessageEntry is not null)
                MessageEntry.Text = text;

            await SendMessageAsync();
        }
        finally
        {
            if (MessageEntry is not null && !string.Equals(MessageEntry.Text, previousText, StringComparison.Ordinal))
                MessageEntry.Text = previousText;
        }
    }

    private static string GuessAttachmentContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".m4v" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".m4a" or ".aac" or ".mp3" or ".wav" or ".ogg" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    private async Task<string> StageAttachmentForOutboxAsync(FileResult file)
    {
        var root = Path.Combine(FileSystem.Current.AppDataDirectory, "HimoOutbox");
        Directory.CreateDirectory(root);
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
        var safeName = $"{Guid.NewGuid():N}{ext}";
        var path = Path.Combine(root, safeName);

        await using var source = await file.OpenReadAsync();
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination);
        await destination.FlushAsync();
        return path;
    }

    private async Task<string> StageAttachmentForOutboxAsync(string sourcePath, string fileName)
    {
        var root = Path.Combine(FileSystem.Current.AppDataDirectory, "HimoOutbox");
        Directory.CreateDirectory(root);
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".bin";
        var path = Path.Combine(root, $"{Guid.NewGuid():N}{ext}");
        await using var source = File.OpenRead(sourcePath);
        await using var destination = File.Create(path);
        await source.CopyToAsync(destination);
        await destination.FlushAsync();
        return path;
    }

    private async Task PrepareVisibleImagePreviewsAsync(IEnumerable<ChatMessage> messages)
    {
        if (messages is null) return;

        var imageMessages = messages
            .Where(x => x is not null && x.IsImageAttachment)
            .ToList();

        if (imageMessages.Count == 0) return;

        var tasks = imageMessages.Select(PrepareImagePreviewAsync);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task PrepareImagePreviewAsync(ChatMessage? message)
    {
        if (message is null || !message.IsImageAttachment || !Guid.TryParse(message.RemoteId, out var messageId)) return;
        if (!string.IsNullOrWhiteSpace(message.AttachmentLocalPath) && File.Exists(message.AttachmentLocalPath)) return;

        try
        {
            var path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "image");
            message.AttachmentLocalPath = path;
        }
        catch
        {
            // Inline preview is optional; the attachment can still be opened manually.
        }
    }

    private async void ImagePreviewClicked(object? sender, TappedEventArgs e)
    {
        if (sender is not Image image || image.BindingContext is not ChatMessage message)
            return;

        await OpenImagePreviewAsync(message);
    }

    // Kept for XAML variants that place the tap gesture on the image card/border
    // instead of the Image itself.
    private async void ImagePreviewCardTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element element || element.BindingContext is not ChatMessage message)
            return;

        await OpenImagePreviewAsync(message);
    }

    private async Task OpenImagePreviewAsync(ChatMessage message)
    {
        if (!message.IsImageAttachment || !Guid.TryParse(message.RemoteId, out var messageId))
            return;

        // When message selection mode is active, tapping the image/card selects or
        // deselects the message instead of opening the image.
        if (_selectionMode)
        {
            ToggleMessageSelection(message);
            return;
        }

        try
        {
            var path = message.AttachmentLocalPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "image");
                message.AttachmentLocalPath = path;
            }

            var imagePath = path ?? throw new IOException("لم يتم تنزيل الصورة بشكل صحيح.");
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath) || new FileInfo(imagePath).Length <= 0)
                throw new IOException("لم يتم تنزيل الصورة بشكل صحيح.");

            await Navigation.PushModalAsync(
                new ImagePreviewPage(imagePath, message.AttachmentFileName ?? "الصورة"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الصورة"), $"تعذر فتح الصورة: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void AttachmentClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || !message.IsAttachment || !Guid.TryParse(message.RemoteId, out var messageId)) return;
        try
        {
            var path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "file");
            await Launcher.Default.OpenAsync(new OpenFileRequest(message.AttachmentFileName ?? "file", new ReadOnlyFile(path)));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("المرفق"), $"تعذر فتح الملف: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async Task PrepareInitialMediaPreviewsAsync(
        IEnumerable<ChatMessage> images,
        IEnumerable<ChatMessage> videos,
        CancellationToken cancellationToken)
    {
        try
        {
            // Keep the first CollectionView frame dedicated to message layout.
            // Media thumbnails are visual enhancements and can wait one frame.
            await Task.Delay(120, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await Task.WhenAll(
                PrepareVisibleImagePreviewsAsync(images),
                PrepareVisibleVideoPreviewsAsync(videos)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Deferred media preview failed: {ex.Message}");
        }
    }

    private void VideoAttachmentLoaded(object? sender, EventArgs e)
    {
        if (sender is not Border border || border.BindingContext is not ChatMessage message)
            return;

        // A recycled CollectionView cell must never start a network download.
        // Video thumbnails are generated only by the deferred preview path.
        border.IsVisible = IsVideoAttachment(message);
    }

    private async Task PrepareVisibleVideoPreviewsAsync(IEnumerable<ChatMessage> messages)
    {
        if (messages is null) return;

        var videoMessages = messages
            .Where(x => x is not null && x.IsVideoAttachment)
            .TakeLast(6)
            .ToList();

        if (videoMessages.Count == 0) return;

        var tasks = videoMessages.Select(PrepareVideoPreviewAsync);
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task PrepareVideoPreviewAsync(ChatMessage message)
    {
        if (message is null || !message.IsVideoAttachment) return;
        if (!string.IsNullOrWhiteSpace(message.VideoThumbnailPath) && File.Exists(message.VideoThumbnailPath)) return;

        var key = !string.IsNullOrWhiteSpace(message.RemoteId)
            ? message.RemoteId!
            : !string.IsNullOrWhiteSpace(message.ClientMessageId)
                ? message.ClientMessageId!
                : $"{_conversationId}_{message.Id}";

        lock (_videoPreviewInFlight)
        {
            if (!_videoPreviewInFlight.Add(key)) return;
        }

        try
        {
            var path = message.AttachmentLocalPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                if (!Guid.TryParse(message.RemoteId, out var messageId)) return;
                path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "video.mp4").ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0) return;
                message.AttachmentLocalPath = path;
            }

#if ANDROID
            var result = await Task.Run(() => BuildVideoThumbnail(path!, key)).ConfigureAwait(false);
            if (result is null) return;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                message.VideoThumbnailPath = result.Value.ThumbnailPath;
                message.VideoDisplayWidth = result.Value.DisplayWidth;
                message.VideoDisplayHeight = result.Value.DisplayHeight;
                message.VideoDurationText = FormatVideoTime(result.Value.DurationMilliseconds);
            }).ConfigureAwait(false);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Video preview failed: {ex.Message}");
        }
        finally
        {
            lock (_videoPreviewInFlight)
                _videoPreviewInFlight.Remove(key);
        }
    }

#if ANDROID
    private static VideoThumbnailResult? BuildVideoThumbnail(string path, string key)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        using var retriever = new global::Android.Media.MediaMetadataRetriever();
        retriever.SetDataSource(path);

        var width = ParseMetadataInt(retriever.ExtractMetadata(global::Android.Media.MetadataKey.VideoWidth));
        var height = ParseMetadataInt(retriever.ExtractMetadata(global::Android.Media.MetadataKey.VideoHeight));
        var rotation = ParseMetadataInt(retriever.ExtractMetadata(global::Android.Media.MetadataKey.VideoRotation));
        var duration = ParseMetadataLong(retriever.ExtractMetadata(global::Android.Media.MetadataKey.Duration));

        if (rotation is 90 or 270)
            (width, height) = (height, width);

        if (width <= 0 || height <= 0)
        {
            width = 720;
            height = 1280;
        }

        using var rawFrame = retriever.GetFrameAtTime(0, global::Android.Media.Option.ClosestSync);
        if (rawFrame is null) return null;

        global::Android.Graphics.Bitmap? prepared = rawFrame;
        global::Android.Graphics.Bitmap? scaledThumb = null;
        try
        {
            if (rotation is 90 or 180 or 270)
            {
                using var matrix = new global::Android.Graphics.Matrix();
                matrix.PostRotate(rotation);
                prepared = global::Android.Graphics.Bitmap.CreateBitmap(
                    rawFrame, 0, 0, rawFrame.Width, rawFrame.Height, matrix, true);
            }

            if (prepared is null || prepared.IsRecycled) return null;

            var cacheName = $"himo_video_thumb_{SanitizeCacheKey(key)}.jpg";
            var thumbPath = Path.Combine(FileSystem.Current.CacheDirectory, cacheName);

            const int maxThumbnailPixels = 720;
            var scale = Math.Min(1d, Math.Min((double)maxThumbnailPixels / Math.Max(1, prepared.Width), (double)maxThumbnailPixels / Math.Max(1, prepared.Height)));
            var thumb = prepared;
            if (scale < 0.999d)
            {
                var tw = Math.Max(1, (int)Math.Round(prepared.Width * scale));
                var th = Math.Max(1, (int)Math.Round(prepared.Height * scale));
                scaledThumb = global::Android.Graphics.Bitmap.CreateScaledBitmap(prepared, tw, th, true);
                thumb = scaledThumb;
            }

            using (var stream = File.Create(thumbPath))
            {
                if (!thumb.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 86, stream))
                    return null;
            }

            const double maxWidth = 300;
            const double maxHeight = 420;
            var aspect = width / (double)Math.Max(1, height);
            var displayWidth = maxWidth;
            var displayHeight = displayWidth / aspect;
            if (displayHeight > maxHeight)
            {
                displayHeight = maxHeight;
                displayWidth = displayHeight * aspect;
            }

            if (displayWidth < 150)
            {
                displayWidth = 150;
                displayHeight = Math.Min(maxHeight, displayWidth / aspect);
            }

            return new VideoThumbnailResult(
                thumbPath,
                Math.Round(displayWidth, 1),
                Math.Round(displayHeight, 1),
                Math.Max(0, Math.Min(duration, int.MaxValue)));
        }
        finally
        {
            if (scaledThumb is not null)
                scaledThumb.Dispose();
            if (prepared is not null && !ReferenceEquals(prepared, rawFrame))
                prepared.Dispose();
        }
    }

    private static int ParseMetadataInt(string? value) => int.TryParse(value, out var result) ? Math.Max(0, result) : 0;
    private static long ParseMetadataLong(string? value) => long.TryParse(value, out var result) ? Math.Max(0, result) : 0;

    private static string SanitizeCacheKey(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        var cleaned = new string(chars);
        if (cleaned.Length > 50) cleaned = cleaned[^50..];
        return string.IsNullOrWhiteSpace(cleaned) ? Guid.NewGuid().ToString("N") : cleaned;
    }

    private readonly record struct VideoThumbnailResult(
        string ThumbnailPath,
        double DisplayWidth,
        double DisplayHeight,
        long DurationMilliseconds);
#endif

    private void FileAttachmentLoaded(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ChatMessage message)
            border.IsVisible = message.IsAttachment && !message.IsAudioAttachment && !message.IsImageAttachment && !IsVideoAttachment(message);
    }

    private async void VideoPreviewClicked(object? sender, EventArgs e)
    {
        if (sender is not BindableObject bindable || bindable.BindingContext is not ChatMessage message)
            return;

        await PlayVideoAttachmentAsync(message);
    }

    private async Task PlayVideoAttachmentAsync(ChatMessage message)
    {
        if (!message.IsAttachment || !IsVideoAttachment(message) ||
            !Guid.TryParse(message.RemoteId, out var messageId))
            return;

        if (_selectionMode)
        {
            ToggleMessageSelection(message);
            return;
        }

        try
        {
            var path = message.AttachmentLocalPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || new FileInfo(path).Length <= 0)
            {
                path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "video.mp4");
                message.AttachmentLocalPath = path;
            }

            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
                throw new IOException("لم يتم تنزيل الفيديو بشكل صحيح.");

#if ANDROID
            await OpenInlineVideoAsync(path, message.AttachmentFileName ?? "الفيديو");
#else
            await Launcher.Default.OpenAsync(new OpenFileRequest(message.AttachmentFileName ?? "video", new ReadOnlyFile(path)));
#endif
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفيديو"), $"تعذر تشغيل الفيديو: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private static bool IsVideoAttachment(ChatMessage message)
    {
        var contentType = message.AttachmentContentType ?? string.Empty;
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return true;

        var extension = Path.GetExtension(message.AttachmentFileName ?? string.Empty);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".3gp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".3g2", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogv", StringComparison.OrdinalIgnoreCase);
    }

    private async Task OpenInlineVideoAsync(string path, string fileName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("ملف الفيديو غير موجود.", path);

#if ANDROID
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            EnsureInlineVideoOverlay();
            if (InlineVideoHost is null || InlineVideoOverlay is null)
                throw new InvalidOperationException("تعذر إنشاء مساحة تشغيل الفيديو داخل التطبيق.");

            CloseInlineVideoCore();

            InlineVideoTitleLabel?.SetValue(Label.TextProperty, string.IsNullOrWhiteSpace(fileName) ? "الفيديو" : fileName);
            InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, "00:00");
            InlineVideoDurationLabel?.SetValue(Label.TextProperty, "00:00");
            InlineVideoSlider?.SetValue(Slider.ValueProperty, 0d);
            InlineVideoLoading?.SetValue(IsVisibleProperty, true);
            InlineVideoOverlay.IsVisible = true;
            _inlineVideoIsPlaying = false;
            _inlineVideoIsMuted = false;
            SetInlineVideoPlaybackVisual(false);
            InlineVideoMuteButton?.SetValue(Button.TextProperty, "🔊");

            var media = new MediaElement
            {
                Aspect = Aspect.AspectFit,
                // Do not auto-start an attachment video if the app was backgrounded
                // while the file/player was still being prepared.
                ShouldAutoPlay = !_isAppInBackground,
                ShouldShowPlaybackControls = false,
                ShouldKeepScreenOn = true,
                ShouldMute = false,
                Volume = 1.0
            };

            media.MediaOpened += (_, _) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var duration = media.Duration;
                    var milliseconds = Math.Max(0, (int)Math.Min(duration.TotalMilliseconds, int.MaxValue));
                    InlineVideoLoading?.SetValue(IsVisibleProperty, false);
                    InlineVideoDurationLabel?.SetValue(Label.TextProperty, FormatVideoTime(milliseconds));
                    InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, "00:00");
                    if (InlineVideoSlider is not null)
                    {
                        _inlineVideoSliderUpdating = true;
                        InlineVideoSlider.Minimum = 0;
                        InlineVideoSlider.Maximum = Math.Max(1, milliseconds);
                        InlineVideoSlider.Value = 0;
                        _inlineVideoSliderUpdating = false;
                    }
                    if (_isAppInBackground)
                    {
                        try { media.Pause(); } catch { }
                        _inlineVideoIsPlaying = false;
                        SetInlineVideoPlaybackVisual(false);
                    }
                    else
                    {
                        _inlineVideoIsPlaying = true;
                        SetInlineVideoPlaybackVisual(true);
                    }
                });
                if (!_isAppInBackground)
                    StartInlineVideoProgressLoop();
            };

            media.MediaFailed += (_, args) =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    StopInlineVideoProgressLoop();
                    var message = string.IsNullOrWhiteSpace(args?.ErrorMessage)
                        ? "تعذر تشغيل الفيديو على هذا الجهاز."
                        : $"تعذر تشغيل الفيديو: {args.ErrorMessage}";
                    CloseInlineVideo();
                    await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفيديو"), message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                });
            };

            media.MediaEnded += (_, _) =>
            {
                _inlineVideoIsPlaying = false;
                StopInlineVideoProgressLoop();
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var duration = media.Duration;
                    var milliseconds = Math.Max(0, (int)Math.Min(duration.TotalMilliseconds, int.MaxValue));
                    _inlineVideoSliderUpdating = true;
                    if (InlineVideoSlider is not null) InlineVideoSlider.Value = Math.Max(0, milliseconds);
                    _inlineVideoSliderUpdating = false;
                    InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, FormatVideoTime(milliseconds));
                    SetInlineVideoPlaybackVisual(false);
                });
            };

            media.PositionChanged += (_, _) =>
            {
                if (_inlineVideoSliderUpdating) return;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (InlineVideoSlider is null) return;
                    var milliseconds = Math.Max(0, (int)Math.Min(media.Position.TotalMilliseconds, int.MaxValue));
                    _inlineVideoSliderUpdating = true;
                    InlineVideoSlider.Value = Math.Min(milliseconds, InlineVideoSlider.Maximum);
                    _inlineVideoSliderUpdating = false;
                    InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, FormatVideoTime(milliseconds));
                });
            };

            InlineVideoHost.Content = media;
            _inlineVideoElement = media;
            media.Source = MediaSource.FromFile(path);
        });
#else
        await Launcher.Default.OpenAsync(new OpenFileRequest(fileName, new ReadOnlyFile(path)));
#endif
    }

#if ANDROID
    private void SetInlineVideoPlaybackVisual(bool isPlaying)
    {
        var text = isPlaying ? "❚❚" : "▶";
        InlineVideoPlayButton?.SetValue(Button.TextProperty, text);
        InlineVideoCenterPlayButton?.SetValue(Button.TextProperty, text);
    }

    private async void InlineVideoRewindClicked(object? sender, EventArgs e)
        => await SeekInlineVideoByAsync(TimeSpan.FromSeconds(-10));

    private async void InlineVideoForwardClicked(object? sender, EventArgs e)
        => await SeekInlineVideoByAsync(TimeSpan.FromSeconds(10));

    private async Task SeekInlineVideoByAsync(TimeSpan delta)
    {
        var media = _inlineVideoElement;
        if (media is null) return;

        try
        {
            var duration = media.Duration;
            var current = media.Position;
            var target = current + delta;
            if (target < TimeSpan.Zero) target = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && target > duration) target = duration;

            await media.SeekTo(target);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var milliseconds = Math.Max(0, (long)Math.Min(target.TotalMilliseconds, int.MaxValue));
                if (InlineVideoSlider is not null)
                {
                    _inlineVideoSliderUpdating = true;
                    InlineVideoSlider.Value = Math.Min(milliseconds, InlineVideoSlider.Maximum);
                    _inlineVideoSliderUpdating = false;
                }
                InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, FormatVideoTime(milliseconds));
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Video seek failed: {ex.Message}");
        }
    }

    private void InlineVideoMuteClicked(object? sender, EventArgs e)
    {
        var media = _inlineVideoElement;
        if (media is null) return;

        try
        {
            _inlineVideoIsMuted = !_inlineVideoIsMuted;
            media.ShouldMute = _inlineVideoIsMuted;
            InlineVideoMuteButton?.SetValue(Button.TextProperty, _inlineVideoIsMuted ? "🔇" : "🔊");
        }
        catch (Exception ex)
        {
            _inlineVideoIsMuted = false;
            System.Diagnostics.Debug.WriteLine($"[Himo ChatPage] Video mute failed: {ex.Message}");
        }
    }

    private void InlineVideoPlayClicked(object? sender, EventArgs e)
    {
        var media = _inlineVideoElement;
        if (media is null) return;

        try
        {
            if (_inlineVideoIsPlaying)
            {
                media.Pause();
                _inlineVideoIsPlaying = false;
                SetInlineVideoPlaybackVisual(false);
                StopInlineVideoProgressLoop();
            }
            else
            {
                media.Play();
                _inlineVideoIsPlaying = true;
                SetInlineVideoPlaybackVisual(true);
                StartInlineVideoProgressLoop();
            }
        }
        catch (Exception ex)
        {
            _ = DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الفيديو"), $"تعذر التحكم في الفيديو: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void InlineVideoSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_inlineVideoSliderUpdating || _inlineVideoElement is null) return;
        try
        {
            var position = TimeSpan.FromMilliseconds(Math.Max(0, e.NewValue));
            _ = _inlineVideoElement.SeekTo(position);
            InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, FormatVideoTime((int)Math.Min(position.TotalMilliseconds, int.MaxValue)));
        }
        catch { }
    }

    private void StartInlineVideoProgressLoop()
    {
        StopInlineVideoProgressLoop();
        _inlineVideoProgressCts = new CancellationTokenSource();
        _ = UpdateInlineVideoProgressAsync(_inlineVideoProgressCts.Token);
    }

    private async Task UpdateInlineVideoProgressAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var media = _inlineVideoElement;
                if (media is not null && _inlineVideoIsPlaying)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (InlineVideoSlider is null) return;
                        var duration = Math.Max(1, (int)Math.Min(media.Duration.TotalMilliseconds, int.MaxValue));
                        var position = Math.Max(0, (int)Math.Min(media.Position.TotalMilliseconds, int.MaxValue));
                        _inlineVideoSliderUpdating = true;
                        InlineVideoSlider.Maximum = duration;
                        InlineVideoSlider.Value = Math.Min(position, duration);
                        _inlineVideoSliderUpdating = false;
                        InlineVideoCurrentTimeLabel?.SetValue(Label.TextProperty, FormatVideoTime(position));
                    });
                }

                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                try { await Task.Delay(250, cancellationToken); } catch { break; }
            }
        }
    }

    private void StopInlineVideoProgressLoop()
    {
        try { _inlineVideoProgressCts?.Cancel(); } catch { }
        try { _inlineVideoProgressCts?.Dispose(); } catch { }
        _inlineVideoProgressCts = null;
    }

    private static string FormatVideoTime(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var totalSeconds = milliseconds / 1000;
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours:00}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }
#endif

    private void CloseInlineVideoClicked(object? sender, EventArgs e) => CloseInlineVideo();

    private void CloseInlineVideo()
    {
        if (MainThread.IsMainThread)
            CloseInlineVideoCore();
        else
            MainThread.BeginInvokeOnMainThread(CloseInlineVideoCore);
    }

    private void CloseInlineVideoCore()
    {
        StopInlineVideoProgressLoop();
#if ANDROID
        try { _inlineVideoElement?.Stop(); } catch { }
        _inlineVideoElement = null;
        _inlineVideoIsPlaying = false;
        _inlineVideoIsMuted = false;
#endif
        if (InlineVideoHost is not null)
            InlineVideoHost.Content = null;
        if (InlineVideoLoading is not null) InlineVideoLoading.IsVisible = false;
        if (InlineVideoOverlay is not null) InlineVideoOverlay.IsVisible = false;
    }

    private async void AttachmentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border border || border.BindingContext is not ChatMessage message || !message.IsAttachment ||
            message.IsAudioAttachment || message.IsImageAttachment || !IsVideoAttachment(message))
            return;

        if (_selectionMode)
        {
            ToggleMessageSelection(message);
            return;
        }

        await PlayVideoAttachmentAsync(message);
    }

    private async void EditClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || !message.IsMine || !Guid.TryParse(message.RemoteId, out var messageId) || message.IsAttachment) return;
        var edited = await DisplayPromptAsync("تعديل الرسالة", "عدّل النص:", initialValue: message.Text, maxLength: 4000, keyboard: Keyboard.Text);
        if (edited is null) return;
        edited = edited.Trim();
        if (string.IsNullOrWhiteSpace(edited) || string.Equals(edited, message.Text, StringComparison.Ordinal)) return;
        try
        {
            var remote = await _api.EditMessageAsync(messageId, edited);
            _chat.UpdateMessageText(remote.Id.ToString("D"), remote.Text, DateTime.TryParse(remote.EditedAt, out var at) ? at : null);
            RefreshMessagesView(scrollToEnd: false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تعديل الرسالة"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void DeleteClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || message.IsDeleted)
            return;

        if (!Guid.TryParse(message.RemoteId, out var messageId))
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الرسالة"), "هذه الرسالة لم تحصل بعد على رقم من الخادم، لذلك لا يمكن حذفها الآن.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        var confirm = await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الرسالة"), "هل تريد حذف الرسالة؟", Himo.Services.AppLocalization.TranslateSourceText("حذف"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
        if (!confirm) return;

        try
        {
            var deleted = await _api.DeleteMessageAsync(messageId);
            _chat.UpdateMessageDeleted(deleted.Id.ToString("D"));
            RefreshMessagesView(scrollToEnd: false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الرسالة"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private async void ForwardClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || message.IsDeleted || message.IsAttachment || string.IsNullOrWhiteSpace(message.Text)) return;

        try
        {
            var conversations = await _api.GetConversationsAsync();
            var targets = conversations
                .Where(x => !string.Equals(x.Id.ToString("D"), _remoteConversationId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedAt)
                .ToList();

            if (targets.Count == 0)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تحويل الرسالة"), Himo.Services.AppLocalization.TranslateSourceText("لا توجد محادثات أخرى متاحة."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            var options = targets.Select(x => string.IsNullOrWhiteSpace(x.Name) ? "محادثة" : x.Name).ToArray();
            var selected = await DisplayActionSheetAsync("تحويل الرسالة إلى", "إلغاء", null, options);
            if (string.IsNullOrWhiteSpace(selected) || string.Equals(selected, "إلغاء", StringComparison.Ordinal)) return;

            var target = targets.FirstOrDefault(x => string.Equals(x.Name, selected, StringComparison.Ordinal));
            if (target is null) return;

            await _api.SendMessageAsync(target.Id, message.Text);
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تحويل الرسالة"), Himo.Services.AppLocalization.TranslateSourceText("تم تحويل الرسالة."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("تحويل الرسالة"), ex.Message, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void ReplyClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message) return;
        _replyingTo = message;
        ReplyPreviewLabel?.Text = string.IsNullOrWhiteSpace(message.Text) ? "رسالة مرفقة" : message.Text;
        ReplyPreview?.SetValue(IsVisibleProperty, true);
        MessageEntry?.Focus();
    }

    private void ClearReplyClicked(object? sender, EventArgs e) => ClearReply();

    private async void MessageActionsClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message || message.IsDeleted) return;

        var actions = new List<string> { "تحديد" };
        if (!message.IsAttachment)
        {
            actions.Add("رد");
            actions.Add("تحويل");
        }
        if (message.IsMine)
        {
            if (!message.IsAttachment) actions.Add("تعديل");
            actions.Add("حذف");
        }

        var selected = await DisplayActionSheetAsync("خيارات الرسالة", "إلغاء", null, actions.ToArray());
        if (string.Equals(selected, "تحديد", StringComparison.Ordinal))
            SelectMessageClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "رد", StringComparison.Ordinal))
            ReplyClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "تحويل", StringComparison.Ordinal))
            ForwardClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "تعديل", StringComparison.Ordinal))
            EditClicked(button, EventArgs.Empty);
        else if (string.Equals(selected, "حذف", StringComparison.Ordinal))
            DeleteClicked(button, EventArgs.Empty);
    }

#if ANDROID
    private void ImagePreviewLoaded(object? sender, EventArgs e)
    {
        if (sender is not Image image || image.Handler?.PlatformView is not global::Android.Views.View nativeView)
            return;

        // The Image consumes the touch sequence itself, so the parent message
        // bubble cannot receive a long-press when the user presses directly
        // on the photo. Attach the same native long-click behavior to the image.
        if (!nativeView.LongClickable)
            nativeView.LongClickable = true;

        nativeView.LongClick += (_, args) =>
        {
            if (image.BindingContext is ChatMessage message && !message.IsDeleted)
            {
                MessageLongPressed(message);
                args.Handled = true;
            }
        };
    }

    private void MessageBubbleLoaded(object? sender, EventArgs e)
    {
        if (sender is not Border border || border.Handler?.PlatformView is not global::Android.Views.View nativeView)
            return;

        // CollectionView recycles message cells. Attach the native tap/long-click
        // handlers only once to each native view, while always reading the current
        // Border.BindingContext when the gesture happens.
        if (!_longPressViews.Add(nativeView))
            return;

        nativeView.Clickable = true;
        nativeView.LongClickable = true;

        // While selection mode is active, a light tap toggles this message
        // instead of cancelling the selection. This allows selecting multiple
        // messages one after another.
        nativeView.Click += (_, _) =>
        {
            if (!_selectionMode) return;
            if (border.BindingContext is ChatMessage message && !message.IsDeleted)
                ToggleMessageSelection(message);
        };

        nativeView.LongClick += (_, args) =>
        {
            if (border.BindingContext is ChatMessage message && !message.IsDeleted)
            {
                MessageLongPressed(message);
                args.Handled = true;
            }
        };
    }
#endif

    private void MessageLongPressed(ChatMessage? message)
    {
        if (message is null || message.IsDeleted) return;

        // First long-press starts selection with this message. If selection mode
        // is already active, a long-press toggles this message as well.
        if (_selectionMode)
        {
            ToggleMessageSelection(message);
            return;
        }

        _selectionMode = true;
        _selectedMessageIds.Clear();
        _selectedMessageIds.Add(message.Id);

        foreach (var item in _chat.GetMessages(_conversationId))
            item.IsSelected = item.Id == message.Id;

        UpdateSelectionBar();
    }

    private async void ShareSelectedClicked(object? sender, EventArgs e)
    {
        var selected = _chat.GetMessages(_conversationId)
            .Where(x => _selectedMessageIds.Contains(x.Id) && !x.IsDeleted)
            .OrderBy(x => x.SentAt)
            .ToList();

        if (selected.Count == 0)
        {
            ExitSelectionMode();
            return;
        }

        if (selected.Count > 1)
        {
            var text = string.Join(Environment.NewLine + Environment.NewLine,
                selected.Select(x => string.IsNullOrWhiteSpace(x.Text) ? "رسالة مرفقة" : x.Text.Trim()));
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "مشاركة الرسائل",
                Text = text
            });
            ExitSelectionMode();
            return;
        }

        var message = selected[0];
        try
        {
            if (message.IsAttachment && Guid.TryParse(message.RemoteId, out var messageId))
            {
                var path = message.AttachmentLocalPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    path = await _api.DownloadAttachmentAsync(messageId, message.AttachmentFileName ?? "file");
                    message.AttachmentLocalPath = path;
                }

                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "مشاركة المرفق",
                    File = new ShareFile(path)
                });
            }
            else
            {
                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Title = "مشاركة الرسالة",
                    Text = message.Text ?? string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("مشاركة الرسالة"), $"تعذر مشاركة الرسالة: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            ExitSelectionMode();
        }
    }

    private void CommentSelectedClicked(object? sender, EventArgs e)
    {
        var selected = _chat.GetMessages(_conversationId)
            .Where(x => _selectedMessageIds.Contains(x.Id) && !x.IsDeleted)
            .OrderBy(x => x.SentAt)
            .ToList();

        if (selected.Count != 1)
        {
            _ = DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("التعليق"), Himo.Services.AppLocalization.TranslateSourceText("اختر رسالة واحدة فقط للتعليق عليها."), Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        var message = selected[0];
        ExitSelectionMode();
        _replyingTo = message;
        ReplyPreviewLabel?.SetValue(Label.TextProperty,
            string.IsNullOrWhiteSpace(message.Text) ? "رسالة مرفقة" : message.Text);
        if (ReplyPreview is not null) ReplyPreview.IsVisible = true;
        MessageEntry?.Focus();
    }

    private void ToggleMessageSelection(ChatMessage message)
    {
        if (message.IsDeleted) return;
        if (!_selectionMode) _selectionMode = true;

        if (!_selectedMessageIds.Add(message.Id))
        {
            _selectedMessageIds.Remove(message.Id);
            message.IsSelected = false;
        }
        else
        {
            message.IsSelected = true;
        }

        if (_selectedMessageIds.Count == 0) ExitSelectionMode();
        else UpdateSelectionBar();
    }

    private void UpdateSelectionBar()
    {
        SelectionBar?.SetValue(IsVisibleProperty, _selectionMode);
        SelectionCountLabel?.SetValue(Label.TextProperty, $"{_selectedMessageIds.Count} محددة");
    }

    private void ExitSelectionMode()
    {
        foreach (var message in _chat.GetMessages(_conversationId))
            message.IsSelected = false;
        _selectedMessageIds.Clear();
        _selectionMode = false;
        SelectionBar?.SetValue(IsVisibleProperty, false);
    }

    private void SelectMessageClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.BindingContext is not ChatMessage message) return;
        ToggleMessageSelection(message);
    }

    private async void DeleteSelectedClicked(object? sender, EventArgs e)
    {
        var selected = _chat.GetMessages(_conversationId)
            .Where(x => _selectedMessageIds.Contains(x.Id) && !x.IsDeleted)
            .ToList();

        if (selected.Count == 0)
        {
            ExitSelectionMode();
            return;
        }

        var deletable = selected.Where(x => Guid.TryParse(x.RemoteId, out _)).ToList();
        if (deletable.Count == 0)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الرسائل"), "الرسائل المحددة لم تحصل بعد على أرقام من الخادم، أرسلها وانتظر اكتمال الإرسال ثم حاول الحذف.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        var confirm = await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الرسائل"), $"حذف {deletable.Count} رسالة؟", Himo.Services.AppLocalization.TranslateSourceText("حذف"), Himo.Services.AppLocalization.TranslateSourceText("إلغاء"));
        if (!confirm) return;

        var failures = new List<string>();
        foreach (var message in deletable)
        {
            if (!Guid.TryParse(message.RemoteId, out var messageId))
                continue;

            try
            {
                var deleted = await _api.DeleteMessageAsync(messageId);
                _chat.UpdateMessageDeleted(deleted.Id.ToString("D"));
            }
            catch (Exception ex)
            {
                failures.Add(string.IsNullOrWhiteSpace(ex.Message) ? "تعذر حذف إحدى الرسائل." : ex.Message);
            }
        }

        ExitSelectionMode();
        RefreshMessagesView(scrollToEnd: false);

        if (failures.Count > 0)
        {
            var details = string.Join(Environment.NewLine, failures.Distinct().Take(3));
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("حذف الرسائل"), details, Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
    }

    private void ClearReply()
    {
        _replyingTo = null;
        if (ReplyPreview is not null) ReplyPreview.IsVisible = false;
        if (ReplyPreviewLabel is not null) ReplyPreviewLabel.Text = string.Empty;
    }

    private async void SendClicked(object? sender, EventArgs e)
    {
        // Entry.Completed is raised by the keyboard Enter key; the explicit Send button remains usable.
        if (ReferenceEquals(sender, MessageEntry) && !Preferences.Default.Get(ChatSettingsPage.EnterToSendKey, true))
            return;

        if (Interlocked.Exchange(ref _sending, 1) != 0)
            return;

        // Keep the editor enabled while sending. Disabling an Android Entry dismisses
        // the soft keyboard, which forced users to reopen it after every message.
        var shouldKeepKeyboard = !string.IsNullOrWhiteSpace(MessageEntry?.Text)
            && (ReferenceEquals(sender, MessageEntry) || ReferenceEquals(sender, SendButton));

        try
        {
            if (SendButton is not null)
            {
                SendButton.IsEnabled = false;
                SendButton.Source = ChatIconSource("send");
            }

            await SendMessageAsync();
        }
        finally
        {
            if (SendButton is not null)
            {
                SendButton.IsEnabled = true;
                SendButton.Source = ChatIconSource("send");
            }

            Interlocked.Exchange(ref _sending, 0);

            if (shouldKeepKeyboard)
                KeepComposerKeyboardOpen();
        }
    }

    private void KeepComposerKeyboardOpen()
    {
        if (MessageEntry is null)
            return;

        // Entry.Completed may cause Android's IME to close its soft keyboard.
        // Refocus after the send operation and explicitly request the IME again.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
        {
            if (MessageEntry is null || !MessageEntry.IsVisible || !MessageEntry.IsEnabled)
                return;

            MessageEntry.Focus();

#if ANDROID
            try
            {
                var nativeEntry = MessageEntry.Handler?.PlatformView as Android.Widget.EditText;
                var inputMethodManager = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?
                    .GetSystemService(Android.Content.Context.InputMethodService)
                    as Android.Views.InputMethods.InputMethodManager;

                if (nativeEntry is not null)
                {
                    nativeEntry.RequestFocus();
                    inputMethodManager?.ShowSoftInput(
                        nativeEntry,
                        Android.Views.InputMethods.ShowFlags.Implicit);
                }
            }
            catch
            {
                // A keyboard service failure must not interrupt sending messages.
            }
#endif
        });
    }
    private async void ResendFailedMessageClicked(object? sender, EventArgs e)
    {
        if (sender is not Element element || element.BindingContext is not ChatMessage message)
            return;

        await ResendFailedMessageAsync(message, element);
    }

    private async void ResendFailedMessageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element element || element.BindingContext is not ChatMessage message)
            return;

        await ResendFailedMessageAsync(message, element);
    }

    private async Task ResendFailedMessageAsync(ChatMessage message, Element? sourceElement)
    {
        if (!message.IsMine || !string.Equals(message.DeliveryStatus, "failed", StringComparison.OrdinalIgnoreCase))
            return;

        if (sourceElement is VisualElement visual)
            visual.IsEnabled = false;

        try
        {
            if (!_api.HasToken || Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
            {
                await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إعادة الإرسال"), "لا يوجد اتصال بالإنترنت حاليًا. أعد المحاولة بعد الاتصال بالشبكة.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
                return;
            }

            // Text messages already have a durable ClientMessageId and are retried by the manual button.
            if (!message.IsAttachment && !string.IsNullOrWhiteSpace(message.ClientMessageId))
            {
                if (_chat.MarkPendingSending(message.ClientMessageId))
                {
                    RefreshMessagesView(scrollToEnd: false);
                    await FlushOutboxAsync();
                    return;
                }
            }

            // Audio recordings keep their local cache path, so they can be uploaded again.
            if (message.IsAttachment && !string.IsNullOrWhiteSpace(message.AttachmentLocalPath) && File.Exists(message.AttachmentLocalPath))
            {
                var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == message.ConversationId);
                if (conversation is null || !Guid.TryParse(conversation.RemoteId, out var conversationId))
                {
                    await ResolveRemoteConversationAsync();
                    conversation = _chat.Conversations.FirstOrDefault(x => x.Id == message.ConversationId);
                }

                if (conversation is null || !Guid.TryParse(conversation.RemoteId, out conversationId))
                    throw new InvalidOperationException("تعذر ربط المحادثة بالخادم حاليًا.");

                message.IsPending = true;
                message.DeliveryStatus = "sending";
                RefreshMessagesView(scrollToEnd: false);

                await using var stream = File.OpenRead(message.AttachmentLocalPath);
                var remote = await _api.UploadAttachmentAsync(
                    conversationId,
                    stream,
                    message.AttachmentFileName ?? Path.GetFileName(message.AttachmentLocalPath),
                    message.AttachmentContentType ?? "application/octet-stream");

                _chat.CompletePendingAttachmentMessage(message, remote.Id.ToString(), remote.SentAt.LocalDateTime);
                RefreshMessagesView(scrollToEnd: false);
                return;
            }

            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إعادة الإرسال"), "لا توجد نسخة محلية من هذا الملف لإعادة رفعه. أعد اختيار الملف من المرفقات.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        catch (Exception ex)
        {
            message.IsPending = true;
            message.DeliveryStatus = "failed";
            RefreshMessagesView(scrollToEnd: false);
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("إعادة الإرسال"), $"تعذرت إعادة الإرسال: {ex.Message}", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
        }
        finally
        {
            if (sourceElement is VisualElement visualElement)
                visualElement.IsEnabled = true;
        }
    }

    private async Task SendMessageAsync()
    {
        StopTyping();
        var text = MessageEntry?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || _conversationId == 0)
            return;
        if (text.Length > 4000)
        {
            await DisplayAlertAsync(Himo.Services.AppLocalization.TranslateSourceText("الإرسال"), "الرسالة طويلة جدًا. الحد الأقصى 4000 حرف.", Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        var conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        if (!_api.HasToken)
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الإرسال"),
                Himo.Services.AppLocalization.TranslateSourceText("جلسة الدخول غير متاحة حاليًا. سجّل الدخول مرة أخرى ثم أعد الإرسال."),
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        if (conversation is null)
            return;

        if (!Guid.TryParse(conversation.RemoteId, out _))
        {
            // The user can press Send before the background conversation resolution
            // has completed. Resolve it now instead of rejecting a valid message.
            await ResolveRemoteConversationAsync();
            conversation = _chat.Conversations.FirstOrDefault(x => x.Id == _conversationId);
        }

        if (conversation is null || !Guid.TryParse(conversation.RemoteId, out var remoteConversationId))
        {
            await DisplayAlertAsync(
                Himo.Services.AppLocalization.TranslateSourceText("الإرسال"),
                Himo.Services.AppLocalization.TranslateSourceText("تعذر ربط المحادثة بالخادم حاليًا. انتظر لحظة ثم أرسل الرسالة مرة أخرى."),
                Himo.Services.AppLocalization.TranslateSourceText("حسنًا"));
            return;
        }

        var clientMessageId = Guid.NewGuid().ToString("D");
        // Register before adding the pending row so a concurrent outbox flush cannot
        // capture it and queue it behind unrelated older messages.
        _directMessageSends.TryAdd(clientMessageId, 0);

        var pending = _chat.AddPendingMessage(
            _conversationId,
            text,
            clientMessageId,
            _replyingTo?.RemoteId,
            _replyingTo?.Text);

        if (pending is null)
        {
            _directMessageSends.TryRemove(clientMessageId, out _);
            return;
        }

        // Optimistic UI: show the message immediately, then POST it independently
        // from the background retry queue so a slow old item cannot delay this send.
        MessageEntry?.Text = string.Empty;
        ClearReply();
        RefreshMessagesView(scrollToEnd: true);

        _ = SendPendingTextImmediatelyAsync(
            _conversationId,
            remoteConversationId,
            text,
            clientMessageId,
            pending.ReplyToRemoteId);
    }

    private static string GetMessageUiKey(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.RemoteId))
            return "r:" + message.RemoteId;

        return "l:" + message.ConversationId + ":" + message.Id;
    }

    private static int CompareMessageOrder(ChatMessage left, ChatMessage right)
    {
        var byTime = left.SentAt.CompareTo(right.SentAt);
        if (byTime != 0) return byTime;
        return left.Id.CompareTo(right.Id);
    }

    private void UpdateMessageDateSeparators(IReadOnlyList<ChatMessage> orderedMessages)
    {
        DateTime? previousDate = null;
        foreach (var message in orderedMessages)
        {
            var date = message.SentAt.Date;
            message.DateSeparatorText = previousDate != date
                ? date == DateTime.Today
                    ? "اليوم"
                    : date == DateTime.Today.AddDays(-1)
                        ? "أمس"
                        : date.ToString("dd/MM/yyyy")
                : string.Empty;

            previousDate = date;
        }
    }

    private void SetMessagesItemsSource(IReadOnlyList<ChatMessage> messages)
    {
        var bindTrace = Stopwatch.StartNew();
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] SetMessagesItemsSource START count={messages.Count} mainThread={MainThread.IsMainThread}");
        if (!MainThread.IsMainThread)
        {
            var snapshot = messages.ToList();
            MainThread.BeginInvokeOnMainThread(() => SetMessagesItemsSource(snapshot));
            return;
        }

        var fontChoice = Preferences.Default.Get(ChatSettingsPage.ChatFontSizeKey, "متوسط");
        var fontSize = fontChoice switch { "صغير" => 14d, "كبير" => 18d, _ => 16d };
        var showMedia = Preferences.Default.Get(ChatSettingsPage.ShowMediaKey, true);
        var orderedMessages = messages
            .Where(x => showMedia || (string.IsNullOrWhiteSpace(x.AttachmentFileName) && string.IsNullOrWhiteSpace(x.AttachmentContentType)))
            .OrderBy(x => x.SentAt)
            .ThenBy(x => x.Id)
            .ToList();
        ApplyTranslationPreference(orderedMessages);
        foreach (var item in orderedMessages) item.MessageFontSize = fontSize;
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] message normalization count={orderedMessages.Count} elapsed={bindTrace.ElapsedMilliseconds}ms");

        UpdateMessageDateSeparators(orderedMessages);
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] date separators updated elapsed={bindTrace.ElapsedMilliseconds}ms");

        var currentKeys = _visibleMessages.Select(GetMessageUiKey).ToList();
        var desiredKeys = orderedMessages.Select(GetMessageUiKey).ToList();

        if (currentKeys.SequenceEqual(desiredKeys, StringComparer.Ordinal))
        {
            System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] SetMessagesItemsSource unchanged total={bindTrace.ElapsedMilliseconds}ms");
            return;
        }

        // One Reset for the whole batch instead of one CollectionChanged event
        // per message. This avoids long UI stalls when a chat contains history.
        _visibleMessages.ReplaceRange(orderedMessages);
        System.Diagnostics.Debug.WriteLine($"[HimoOpenTrace] ReplaceRange completed count={orderedMessages.Count} elapsed={bindTrace.ElapsedMilliseconds}ms");
        UpdateVisiblePagingCursor();
    }

    private void SearchMessagesClicked(object? sender, EventArgs e)
    {
        MessageSearchBar?.SetValue(IsVisibleProperty, true);
        MessageSearchEntry?.Focus();
    }

    private void CloseMessageSearchClicked(object? sender, EventArgs e)
    {
        if (MessageSearchEntry is not null)
            MessageSearchEntry.Text = string.Empty;
        MessageSearchBar?.SetValue(IsVisibleProperty, false);
        var recent = GetOrderedLocalMessages();
        var visible = recent.Count > MessagePageSize ? recent.Skip(recent.Count - MessagePageSize).ToList() : recent;
        SetMessagesItemsSource(visible);
        UpdateVisiblePagingCursor();
    }

    private void MessageSearchChanged(object? sender, EventArgs e)
    {
        ApplyMessageSearch();
    }

    private void ApplyMessageSearch()
    {
        var all = _chat.GetMessages(_conversationId);
        var query = MessageSearchEntry?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            SetMessagesItemsSource(all);
            return;
        }

        var filtered = all.Where(x =>
            (!string.IsNullOrWhiteSpace(x.Text) && x.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(x.ReplyToText) && x.ReplyToText.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(x.AttachmentFileName) && x.AttachmentFileName.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();

        SetMessagesItemsSource(filtered);
        EmptyState?.SetValue(IsVisibleProperty, filtered.Count == 0);
    }

    private void QueueVisibleMediaPreparation()
    {
        _scrollIdleMediaPending = true;
        ArmScrollIdleTimer();
    }

    private void QueueOlderPagingAfterScrollIdle()
    {
        _scrollIdleOlderPending = true;
        ArmScrollIdleTimer();
    }

    private void ArmScrollIdleTimer()
    {
        if (_scrollIdleTimer is null) return;
        _scrollIdleTimer.Stop();
        _scrollIdleTimer.Start();
    }

    private async void OnScrollIdleTimerTick(object? sender, EventArgs e)
    {
        _scrollIdleTimer?.Stop();

        var loadOlder = _scrollIdleOlderPending;
        var prepareMedia = _scrollIdleMediaPending;
        _scrollIdleOlderPending = false;
        _scrollIdleMediaPending = false;

        if (!IsConversationVisible) return;

        if (loadOlder)
        {
            await LoadOlderMessagesAsync().ConfigureAwait(false);
            return;
        }

        if (prepareMedia)
            await PrepareVisibleMediaAfterScrollIdleAsync().ConfigureAwait(false);
    }

    private async Task PrepareVisibleMediaAfterScrollIdleAsync()
    {
        try
        {
            var first = Volatile.Read(ref _firstVisibleItemIndex);
            var last = Volatile.Read(ref _lastVisibleItemIndex);
            if (first < 0 || last < first)
            {
                first = Math.Max(0, _visibleMessages.Count - 4);
                last = Math.Max(first, _visibleMessages.Count - 1);
            }

            var visible = _visibleMessages
                .Skip(first)
                .Take(Math.Max(0, last - first + 1))
                .ToList();

            var image = visible.FirstOrDefault(x => x.IsImageAttachment);
            if (image is not null)
                await PrepareImagePreviewAsync(image).ConfigureAwait(false);

            var video = visible.FirstOrDefault(x => x.IsVideoAttachment);
            if (video is not null)
                await PrepareVideoPreviewAsync(video).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Himo ChatPage] Visible media preparation failed: {ex.Message}");
        }
    }

    private void ConfigureInitialChatPosition()
    {
        if (_visibleMessages.Count == 0)
        {
            _initialPositioningLatest = false;
            _allowOlderPaging = true;
            return;
        }

        _initialPositioningLatest = true;
        _allowOlderPaging = false;

#if ANDROID
        try
        {
            if (Messages?.Handler?.PlatformView is RecyclerView recycler &&
                recycler.GetLayoutManager() is LinearLayoutManager layoutManager)
            {
                // Let RecyclerView anchor the short first page from the bottom.
                // Do not issue a second native scroll here: that extra layout pass
                // was visible as a long Android HWUI/Davey stall on slower devices.
                layoutManager.ReverseLayout = false;
                layoutManager.StackFromEnd = true;
                _initialPositioningLatest = false;
                _allowOlderPaging = true;
                _lastVisibleItemIndex = Math.Max(-1, _visibleMessages.Count - 1);
                ScrollToBottomButton?.SetValue(IsVisibleProperty, false);
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] Native layout setup failed: {ex}");
        }
#endif

        // Portable MAUI fallback. One post only; no polling/delays.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (Messages is not null && _visibleMessages.Count > 0)
                {
                    Messages.ScrollTo(
                        _visibleMessages.Count - 1,
                        group: null,
                        position: ScrollToPosition.End,
                        animate: false);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo ChatPage] Initial MAUI positioning failed: {ex}");
            }
            finally
            {
                _initialPositioningLatest = false;
                _allowOlderPaging = true;
                _lastVisibleItemIndex = Math.Max(-1, _visibleMessages.Count - 1);
                ScrollToBottomButton?.SetValue(IsVisibleProperty, false);
            }
        });
    }

    private void MessagesLoaded(object? sender, EventArgs e)
    {
        // Kept for older XAML variants. Current chat opening does not use this
        // event because the initial anchor is configured exactly once by Load().
    }

    private void MessagesScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        var messages = _visibleMessages;
        if (messages.Count == 0) return;

        _firstVisibleItemIndex = e.FirstVisibleItemIndex;
        _lastVisibleItemIndex = e.LastVisibleItemIndex;

        // Never do network/file/media work during the active drag. It is scheduled
        // only after the list has been idle for the timer interval.
        QueueVisibleMediaPreparation();

        // The first CollectionView layout normally reports index 0 even though the
        // page has just been opened. Do not interpret that initial layout as the
        // user's request to load older messages. Older paging becomes legal only
        // after the initial scroll-to-latest pass has completed.
        if (!_initialPositioningLatest && _allowOlderPaging &&
            _initialRemoteSyncCompleted &&
            e.FirstVisibleItemIndex >= 0 &&
            e.FirstVisibleItemIndex <= 2 &&
            _hasMoreOlderMessages)
        {
            QueueOlderPagingAfterScrollIdle();
        }

        if (ScrollToBottomButton is null) return;
        var lastVisible = e.LastVisibleItemIndex;
        var shouldShowButton = lastVisible >= 0 && lastVisible < messages.Count - 2;
        if (ScrollToBottomButton.IsVisible != shouldShowButton)
            ScrollToBottomButton.IsVisible = shouldShowButton;
    }

    private int FindVisibleMessageIndex(ChatMessage message)
    {
        if (message is null || _visibleMessages.Count == 0)
            return -1;

        // Prefer the actual object reference when available.
        var index = _visibleMessages.IndexOf(message);
        if (index >= 0)
            return index;

        // The collection may have been refreshed with new instances. Compare by
        // the stable remote/local key instead of the object reference.
        var key = GetMessageUiKey(message);
        for (var i = 0; i < _visibleMessages.Count; i++)
        {
            if (string.Equals(GetMessageUiKey(_visibleMessages[i]), key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private async Task SafeScrollTo(
        ChatMessage? message,
        ScrollToPosition position = ScrollToPosition.End,
        bool animate = false)
    {
        if (message is null)
            return;

        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await SafeScrollTo(message, position, animate));
            return;
        }

        var index = FindVisibleMessageIndex(message);
        if (index < 0 || index >= _visibleMessages.Count)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Himo ChatPage] SafeScrollTo skipped: message is not in ItemsSource.");
            return;
        }

        await SafeScrollToIndex(index, position, animate);
    }

    private async Task SafeScrollToIndex(
        int index,
        ScrollToPosition position = ScrollToPosition.End,
        bool animate = false)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
                await SafeScrollToIndex(index, position, animate));
            return;
        }

        if (Messages is null || _visibleMessages.Count == 0)
            return;

        // Validate against the current MAUI ItemsSource before touching the native
        // Android RecyclerView. The collection can change between async awaits.
        if (index < 0 || index >= _visibleMessages.Count)
            return;

        try
        {
#if ANDROID
            if (Messages.Handler?.PlatformView is RecyclerView recycler &&
                recycler.IsAttachedToWindow)
            {
                var adapterCount = recycler.GetAdapter()?.ItemCount ?? 0;
                if (index < 0 || index >= adapterCount)
                    return;

                recycler.Post(() =>
                {
                    try
                    {
                        var currentCount = recycler.GetAdapter()?.ItemCount ?? 0;
                        if (index >= 0 && index < currentCount)
                            recycler.ScrollToPosition(index);
                    }
                    catch (global::Java.Lang.IllegalArgumentException ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Android SafeScrollTo ignored invalid position: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Himo ChatPage] Android SafeScrollTo failed: {ex}");
                    }
                });

                return;
            }
#endif

            // Portable MAUI fallback. Use the validated numeric index rather than
            // passing an arbitrary object reference to CollectionView.ScrollTo.
            var currentCount = _visibleMessages.Count;
            if (index < 0 || index >= currentCount)
                return;

            Messages.ScrollTo(index, group: null, position: position, animate: animate);
        }
        catch (global::Java.Lang.IllegalArgumentException ex)
        {
            // MAUI can surface the Android RecyclerView failure as a Java exception.
            // Treat it as a stale-scroll race instead of letting it crash the app.
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] SafeScrollTo ignored Android invalid target: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] SafeScrollTo ignored invalid target: {ex.Message}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Himo ChatPage] SafeScrollTo failed safely: {ex}");
        }
    }

    private void ScrollToLatestMessage(bool animate)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => ScrollToLatestMessage(animate));
            return;
        }

        if (Messages is null || _visibleMessages.Count == 0)
            return;

        _initialPositioningLatest = false;
        _allowOlderPaging = true;

        _ = SafeScrollToIndex(_visibleMessages.Count - 1, ScrollToPosition.End, animate);

        ScrollToBottomButton?.SetValue(IsVisibleProperty, false);
    }


    private void ScrollToBottomClicked(object? sender, EventArgs e)
    {
        var messages = _chat.GetMessages(_conversationId);
        if (messages.Count > 0)
            ScrollToLatestMessage(animate: true);

        if (ScrollToBottomButton is not null)
            ScrollToBottomButton.IsVisible = false;
    }

    private void RefreshMessagesView(bool scrollToEnd)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => RefreshMessagesView(scrollToEnd));
            return;
        }

        AppendNewMessagesToVisible();
        EmptyState?.SetValue(IsVisibleProperty, _visibleMessages.Count == 0);

        if (scrollToEnd && _visibleMessages.Count > 0)
            ScrollToLatestMessage(animate: true);
    }


    private sealed class RangeObservableCollection<T> : ObservableCollection<T>
    {
        public void ReplaceRange(IEnumerable<T> items)
        {
            CheckReentrancy();

            Items.Clear();
            foreach (var item in items)
                Items.Add(item);

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void InsertRange(int index, IEnumerable<T> items)
        {
            CheckReentrancy();
            var values = items.ToList();
            if (values.Count == 0) return;

            for (var i = 0; i < values.Count; i++)
                Items.Insert(index + i, values[i]);

            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, values, index));
        }
    }

}