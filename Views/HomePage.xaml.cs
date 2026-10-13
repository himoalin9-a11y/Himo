using System.Collections.ObjectModel;
using System.Windows.Input;
using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Storage;

namespace Himo.Views;

public partial class HomePage : ContentPage, IHomeSwipeSectionHost
{
    private readonly HomeViewModel _vm;
    private readonly HimoApiClient _api;
    private readonly ProfileService _profile;
    private readonly CallHistoryService _callHistory;
    private readonly ObservableCollection<ContactMatchDto> _phoneMatches = new();
    private CancellationTokenSource? _pollCts;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _phoneSearchBusy;
    private bool _contactSearchBusy;
    private bool _openingMatchedConversation;
    private bool _openingConversation;
    private string _currentSection = "chats";
    private bool _sectionTransitioning;
    private bool _selectionMode;
    private Conversation? _suppressTapConversation;
    private DateTime _suppressTapUntilUtc = DateTime.MinValue;
    private readonly HashSet<Conversation> _selectedConversations = new();

    public ICommand ConversationTapCommand { get; }
    public ICommand ConversationLongPressCommand { get; }

    public HomePage(HomeViewModel vm, HimoApiClient api, ProfileService profile, CallHistoryService callHistory)
    {
        // Commands used through x:Reference bindings must exist before InitializeComponent
        // evaluates the Behavior bindings in the conversation-row DataTemplate.
        ConversationTapCommand = new Command<Conversation>(async conversation =>
        {
            await HandleConversationTapAsync(conversation);
        });
        ConversationLongPressCommand = new Command<Conversation>(BeginConversationSelection);
        InitializeComponent();
        _vm = vm;
        _api = api;
        _profile = profile;
        _callHistory = callHistory;
        BindingContext = vm;
        CallsCollection.ItemsSource = _callHistory.Entries;
        PhoneSearchResults.ItemsSource = _phoneMatches;
        DisplayOwnProfilePhoto(_profile.Profile.PhotoPath);
        ShowSection("chats");
    }

    private void SearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _vm.SearchText = e.NewTextValue ?? string.Empty;
        _vm.Filter();
        UpdateEmptyState();
    }

    private void BeginConversationSelection(Conversation? conversation)
    {
        if (conversation is null || _currentSection != "chats") return;

        if (!_selectionMode)
        {
            _selectionMode = true;
            _selectedConversations.Clear();
            foreach (var item in _vm.FilteredConversations) item.IsSelected = false;
        }

        if (_selectedConversations.Add(conversation))
            conversation.IsSelected = true;

        // Suppress the tap that some Android builds emit after a native long-press.
        _suppressTapConversation = conversation;
        _suppressTapUntilUtc = DateTime.UtcNow.AddMilliseconds(550);
        UpdateSelectionToolbar();
    }

    private async Task HandleConversationTapAsync(Conversation? conversation)
    {
        if (conversation is null || _currentSection != "chats")
            return;

        // A native long-press can be followed by a tap on some Android builds.
        if (ReferenceEquals(_suppressTapConversation, conversation) && DateTime.UtcNow <= _suppressTapUntilUtc)
        {
            _suppressTapConversation = null;
            System.Diagnostics.Debug.WriteLine($"[Himo ChatTap] Suppressed tap after long-press id={conversation.Id}");
            return;
        }

        if (_selectionMode)
        {
            if (_selectedConversations.Remove(conversation))
                conversation.IsSelected = false;
            else
            {
                _selectedConversations.Add(conversation);
                conversation.IsSelected = true;
            }

            if (_selectedConversations.Count == 0)
                ClearConversationSelection();
            else
                UpdateSelectionToolbar();
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[Himo ChatNav] Opening tapped row id={conversation.Id} remote={conversation.RemoteId ?? "(null)"} name={conversation.Name}");
        await OpenConversationAsync(conversation);
    }

    private async Task OpenConversationAsync(Conversation conversation)
    {
        // A refresh can replace/update list items while the user taps. Prevent
        // duplicate Shell pushes and never send an invalid RemoteId to ChatPage.
        if (_openingConversation)
            return;

        var shell = Shell.Current;
        if (shell is null)
        {
            await DisplayAlertAsync("تعذر فتح المحادثة", "واجهة التنقل غير جاهزة. أغلق الصفحة وافتحها مجددًا.", "موافق");
            return;
        }

        string routeId;
        if (Guid.TryParse(conversation.RemoteId, out var remoteId))
        {
            routeId = remoteId.ToString("D");
        }
        else if (conversation.Id > 0)
        {
            // ChatPage supports the local numeric ID as a fallback for legacy
            // conversations whose RemoteId is empty or malformed.
            routeId = conversation.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            await DisplayAlertAsync("تعذر فتح المحادثة", "معرّف المحادثة غير صالح. حدّث قائمة المحادثات ثم أعد المحاولة.", "موافق");
            return;
        }

        _openingConversation = true;
        try
        {
            var route = $"chat?id={Uri.EscapeDataString(routeId)}";
            System.Diagnostics.Debug.WriteLine($"[Himo ChatNav] Navigating route={route} localId={conversation.Id} remoteId={conversation.RemoteId ?? "(null)"}");
            await MainThread.InvokeOnMainThreadAsync(() => shell.GoToAsync(route, false));
            System.Diagnostics.Debug.WriteLine($"[Himo ChatNav] Navigation completed route={route}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo] Open conversation failed (id={routeId}): {ex}");
            await DisplayAlertAsync("تعذر فتح المحادثة", "حدث خطأ أثناء فتح المحادثة. أعد المحاولة؛ إذا تكرر الخطأ فراجع سجل التطبيق.", "موافق");
        }
        finally
        {
            _openingConversation = false;
        }
    }

    private void ClearConversationSelection()
    {
        foreach (var conversation in _selectedConversations)
            conversation.IsSelected = false;
        _selectedConversations.Clear();
        _selectionMode = false;
        _suppressTapConversation = null;
        UpdateSelectionToolbar();
    }

    private void UpdateSelectionToolbar()
    {
        if (SelectionActionBar is null) return;
        var selected = _selectedConversations.ToList();
        var active = _selectionMode && _currentSection == "chats" && selected.Count > 0;
        HomeHeader.IsVisible = !active;
        SelectionActionBar.IsVisible = active;
        SelectedConversationCountLabel.Text = selected.Count == 1
            ? "محادثة واحدة"
            : $"تم تحديد {selected.Count}";

        var allMuted = selected.Count > 0 && selected.All(item => item.IsMuted);
        MuteSelectedButton.Source = allMuted ? "selection_bell_on.png" : "selection_bell_off.png";
        SemanticProperties.SetDescription(MuteSelectedButton, allMuted ? "تفعيل إشعارات المحادثات المحددة" : "كتم إشعارات المحادثات المحددة");

        var showingArchive = _vm.ShowArchived;
        ArchiveSelectedButton.Source = showingArchive ? "selection_unarchive.png" : "selection_archive.png";
        SemanticProperties.SetDescription(ArchiveSelectedButton, showingArchive ? "إخراج المحادثات من الأرشيف" : "أرشفة المحادثات المحددة");

        var canBlock = selected.Count == 1 && !selected[0].IsGroup;
        BlockSelectedButton.IsVisible = canBlock;
        if (canBlock)
        {
            BlockSelectedButton.Source = selected[0].IsBlocked ? "selection_unblock.png" : "selection_block.png";
            SemanticProperties.SetDescription(BlockSelectedButton, selected[0].IsBlocked ? "إلغاء حظر الشخص" : "حظر الشخص المحدد");
        }

        // Collapse the block-icon slot entirely when multiple chats are selected.
        SelectionActionsGrid.ColumnDefinitions.Clear();
        SelectionActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        SelectionActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var iconColumns = canBlock ? 5 : 4;
        for (var i = 0; i < iconColumns; i++)
            SelectionActionsGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(34)));
        Grid.SetColumn(DeleteSelectedButton, 2);
        Grid.SetColumn(MuteSelectedButton, 3);
        Grid.SetColumn(ArchiveSelectedButton, 4);
        if (canBlock)
        {
            Grid.SetColumn(BlockSelectedButton, 5);
            Grid.SetColumn(CancelSelectionButton, 6);
        }
        else
        {
            Grid.SetColumn(CancelSelectionButton, 5);
        }
    }

    private async void NewConversationClicked(object? sender, EventArgs e)
    {
        var shell = Shell.Current;
        if (shell is null) return;
        await shell.GoToAsync("search", false);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = SwipePageTransition.AnimateIncomingAsync(this);
        UpdateEmptyState();
        DisplayOwnProfilePhoto(_profile.Profile.PhotoPath);
        StartPolling();
        _ = RefreshOwnProfilePhotoAsync();
        _ = InitializeHomeAsync();
    }

    public void SelectSectionAfterSwipeReturn(string section)
    {
        if (section is "chats" or "calls" or "phone")
            ShowSection(section);
    }

    private void DisplayOwnProfilePhoto(string? path)
    {
        var hasPhoto = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        HomeProfilePhotoImage.Source = hasPhoto ? ImageSource.FromFile(path!) : null;
        HomeProfilePhotoImage.IsVisible = hasPhoto;
        HomeProfilePlaceholderIcon.IsVisible = !hasPhoto;
    }

    private async Task RefreshOwnProfilePhotoAsync()
    {
        try
        {
            await _api.TokenInitialization;
            if (!_api.HasToken) return;

            var serverProfile = await _api.GetMyProfileAsync();
            if (serverProfile.HasProfilePhoto == true)
            {
                var photo = await _api.GetMyProfilePhotoAsync();
                if (photo is null) return;
                var path = _profile.SavePhoto(photo.Bytes, photo.ContentType);
                await MainThread.InvokeOnMainThreadAsync(() => DisplayOwnProfilePhoto(path));
            }
            else if (serverProfile.HasProfilePhoto == false)
            {
                _profile.ClearPhoto();
                await MainThread.InvokeOnMainThreadAsync(() => DisplayOwnProfilePhoto(null));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Profile avatar refresh failed: {ex.Message}");
        }
    }

    private async Task InitializeHomeAsync()
    {
        await Task.Yield();
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Initial refresh failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        StopPolling();
        base.OnDisappearing();
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        try
        {
            RefreshIndicator.IsVisible = true;
            RefreshIndicator.IsRunning = true;

            var connected = await _vm.RefreshFromServerAsync();
            UpdateEmptyState();
            ConnectionLabel.Text = connected ? "متصل بالخادم" : "غير متصل بالخادم";
            RetryButton.IsVisible = !connected;
        }
        finally
        {
            RefreshIndicator.IsRunning = false;
            RefreshIndicator.IsVisible = false;
            _refreshGate.Release();
        }
    }

    private void UpdateEmptyState()
    {
        var hasSearch = !string.IsNullOrWhiteSpace(_vm.SearchText);
        var hasResults = _vm.FilteredConversations.Count > 0;
        EmptyTitle.Text = hasSearch && !hasResults
            ? "لا توجد نتائج"
            : _vm.ShowArchived ? "لا توجد محادثات مؤرشفة" : "لا توجد محادثات بعد";
        EmptyHint.Text = hasSearch && !hasResults
            ? "جرّب اسمًا أو كلمة بحث أخرى."
            : _vm.ShowArchived
                ? "تظهر المحادثات المؤرشفة هنا ويمكنك إعادتها إلى القائمة الرئيسية."
                : "ابدأ محادثة جديدة أو ابحث عن أصدقائك برقم الهاتف.";
    }

    private void StartPolling()
    {
        StopPolling();
        if (!_vm.HasServerSession) return;
        _pollCts = new CancellationTokenSource();
        _ = PollAsync(_pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                await RefreshAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Home] Poll failed: {ex.Message}");
            }
        }
    }

    private async void ConversationRefresh_Refreshing(object sender, EventArgs e)
    {
        try { await RefreshAsync(); }
        finally { ConversationRefresh.IsRefreshing = false; }
    }

    private async void RetryClicked(object sender, EventArgs e)
    {
        var button = sender as Button;
        var originalText = button?.Text;
        if (button is not null)
        {
            button.IsEnabled = false;
            button.Text = "جارٍ الاتصال...";
        }
        try { await RefreshAsync(); }
        finally
        {
            if (button is not null)
            {
                button.Text = originalText ?? "إعادة المحاولة";
                button.IsEnabled = true;
            }
        }
    }

    private async void CreateGroupClicked(object? sender, EventArgs e)
    {
        OverflowMenu.IsVisible = false;
        await Shell.Current.GoToAsync("CreateGroupPage");
    }

    private async void SearchUserClicked(object sender, EventArgs e)
    {
        OverflowMenu.IsVisible = false;
        var shell = Shell.Current;
        if (shell is not null) await shell.GoToAsync("search", false);
    }

    private void OverflowMenuClicked(object sender, EventArgs e)
    {
        OverflowMenu.IsVisible = !OverflowMenu.IsVisible;
    }

    private async void SettingsClicked(object? sender, EventArgs e)
    {
        OverflowMenu.IsVisible = false;
        await SwipePageTransition.NavigateAsync(this, "settings", SwipeDirection.Left);
    }

    private async void ProfileClicked(object? sender, EventArgs e)
    {
        OverflowMenu.IsVisible = false;
        await SwipePageTransition.NavigateAsync(this, "profile", SwipeDirection.Left);
    }

    private async void ChatsTabClicked(object? sender, EventArgs e) => await NavigateSectionAsync("chats");
    private async void CallsTabClicked(object? sender, EventArgs e) => await NavigateSectionAsync("calls");
    private async void PhoneSearchTabClicked(object? sender, EventArgs e) => await NavigateSectionAsync("phone");

    private async void ChatsSectionSwiped(object? sender, SwipedEventArgs e) => await HandleSectionSwipeAsync("chats", e);
    private async void CallsSectionSwiped(object? sender, SwipedEventArgs e) => await HandleSectionSwipeAsync("calls", e);
    private async void PhoneSectionSwiped(object? sender, SwipedEventArgs e) => await HandleSectionSwipeAsync("phone", e);

    private async Task HandleSectionSwipeAsync(string sourceSection, SwipedEventArgs e)
    {
        if (_sectionTransitioning || sourceSection != _currentSection)
            return;

        // The profile page follows phone search in the same horizontal sequence.
        if (sourceSection == "phone" && e.Direction == SwipeDirection.Left)
        {
            await SwipePageTransition.NavigateAsync(this, "profile", e.Direction);
            return;
        }

        var order = new[] { "chats", "calls", "phone" };
        var currentIndex = Array.IndexOf(order, _currentSection);
        var delta = e.Direction == SwipeDirection.Left ? 1 : -1;
        var nextIndex = currentIndex + delta;
        if (nextIndex < 0 || nextIndex >= order.Length)
            return;

        await NavigateSectionAsync(order[nextIndex], e.Direction);
    }

    private void ShowSection(string section)
    {
        _currentSection = section;
        ChatsSection.IsVisible = section == "chats";
        CallsSection.IsVisible = section == "calls";
        PhoneSearchSection.IsVisible = section == "phone";

        foreach (var view in new VisualElement[] { ChatsSection, CallsSection, PhoneSearchSection })
        {
            view.Opacity = 1;
            view.TranslationX = 0;
            view.ZIndex = 0;
        }

        UpdateSectionChrome(section);
    }

    private async Task NavigateSectionAsync(string section, SwipeDirection? swipeDirection = null)
    {
        if (_sectionTransitioning || string.IsNullOrWhiteSpace(section))
            return;
        if (section != "chats" && _selectionMode)
            ClearConversationSelection();

        if (section == _currentSection)
        {
            UpdateSectionChrome(section);
            return;
        }

        var outgoing = GetSectionView(_currentSection);
        var incoming = GetSectionView(section);
        if (outgoing is null || incoming is null)
        {
            ShowSection(section);
            return;
        }

        _sectionTransitioning = true;
        try
        {
            // A tap follows the same visual order as a swipe so navigation feels consistent.
            var order = new[] { "chats", "calls", "phone" };
            var oldIndex = Array.IndexOf(order, _currentSection);
            var newIndex = Array.IndexOf(order, section);
            var direction = swipeDirection ?? (newIndex >= oldIndex ? SwipeDirection.Left : SwipeDirection.Right);
            var sign = direction == SwipeDirection.Left ? 1 : -1;

            incoming.IsVisible = true;
            incoming.Opacity = 0;
            incoming.TranslationX = sign * 48;
            incoming.ZIndex = 2;
            outgoing.ZIndex = 1;
            UpdateSectionChrome(section);

            var outX = -sign * 28;
            var outgoingAnimation = Task.WhenAll(
                outgoing.TranslateToAsync(outX, 0, 150, Easing.CubicIn),
                outgoing.FadeToAsync(0, 135, Easing.CubicIn));
            var incomingAnimation = Task.WhenAll(
                incoming.TranslateToAsync(0, 0, 190, Easing.CubicOut),
                incoming.FadeToAsync(1, 190, Easing.CubicOut));
            await Task.WhenAll(outgoingAnimation, incomingAnimation);

            outgoing.IsVisible = false;
            outgoing.Opacity = 1;
            outgoing.TranslationX = 0;
            outgoing.ZIndex = 0;
            incoming.Opacity = 1;
            incoming.TranslationX = 0;
            incoming.ZIndex = 0;
            _currentSection = section;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Section transition failed: {ex.Message}");
            ShowSection(section);
        }
        finally
        {
            _sectionTransitioning = false;
        }
    }

    private VisualElement? GetSectionView(string section) => section switch
    {
        "chats" => ChatsSection,
        "calls" => CallsSection,
        "phone" => PhoneSearchSection,
        _ => null
    };

    private void UpdateSectionChrome(string section)
    {
        ChatSearchCard.IsVisible = section == "chats";
        HomeCreateGroupButton.IsVisible = section == "chats" && !_vm.ShowArchived && !_selectionMode;
        OverflowMenu.IsVisible = false;
        Search.Placeholder = _vm.ShowArchived ? "البحث في المحادثات المؤرشفة" : "البحث في محادثاتك";
        ArchiveListButton.Source = _vm.ShowArchived ? "selection_unarchive.png" : "selection_archive.png";
        ArchiveListButton.BackgroundColor = _vm.ShowArchived ? Color.FromArgb("#4C2E60") : Colors.Transparent;
        UpdateSelectionToolbar();

        var selected = Color.FromArgb("#3A1755");
        var idle = Color.FromArgb("#160A24");
        var selectedStroke = new SolidColorBrush(Color.FromArgb("#9E3BEC"));
        var idleStroke = new SolidColorBrush(Colors.Transparent);
        ChatsTabSurface.BackgroundColor = section == "chats" ? selected : idle;
        CallsTabSurface.BackgroundColor = section == "calls" ? selected : idle;
        PhoneTabSurface.BackgroundColor = section == "phone" ? selected : idle;
        ChatsTabSurface.Stroke = section == "chats" ? selectedStroke : idleStroke;
        CallsTabSurface.Stroke = section == "calls" ? selectedStroke : idleStroke;
        PhoneTabSurface.Stroke = section == "phone" ? selectedStroke : idleStroke;
        ChatsTabLabel.TextColor = section == "chats" ? Color.FromArgb("#F5DFFF") : Color.FromArgb("#B7A2D2");
        CallsTabLabel.TextColor = section == "calls" ? Color.FromArgb("#F5DFFF") : Color.FromArgb("#B7A2D2");
        PhoneTabLabel.TextColor = section == "phone" ? Color.FromArgb("#F5DFFF") : Color.FromArgb("#B7A2D2");
    }

    private void ArchiveListClicked(object? sender, EventArgs e)
    {
        ClearConversationSelection();
        _vm.ToggleArchiveView();
        UpdateSectionChrome(_currentSection);
        UpdateEmptyState();
    }

    private async void PhoneSearchCompleted(object? sender, EventArgs e) => await SearchPhoneNumberAsync();
    private async void PhoneSearchClicked(object? sender, EventArgs e) => await SearchPhoneNumberAsync();

    private async Task SearchPhoneNumberAsync()
    {
        if (_phoneSearchBusy || _contactSearchBusy) return;
        var enteredPhone = PhoneSearchEntry.Text?.Trim() ?? string.Empty;
        var digits = PhoneNumberUtils.Normalize(enteredPhone);
        if (digits.Length is < 7 or > 15)
        {
            PhoneSearchStatusLabel.Text = "أدخل رقم هاتف صحيحًا من 7 إلى 15 رقمًا مع مفتاح الدولة عند الحاجة.";
            PhoneSearchStatusLabel.TextColor = Color.FromArgb("#FF8AB7");
            return;
        }

        _phoneSearchBusy = true;
        PhoneSearchButton.IsEnabled = false;
        FindContactsButton.IsEnabled = false;
        PhoneSearchButton.Text = "جارٍ البحث...";
        PhoneSearchProgress.IsVisible = true;
        PhoneSearchStatusLabel.Text = "يتم البحث عن الحسابات التي تسمح بالعثور عليها...";
        PhoneSearchStatusLabel.TextColor = Color.FromArgb("#C59BE8");
        _phoneMatches.Clear();

        try
        {
            await _api.TokenInitialization;
            if (!_api.HasToken)
            {
                PhoneSearchStatusLabel.Text = "سجّل الدخول أولًا للبحث عن حسابات Himo.";
                return;
            }

            var matches = await _api.FindUsersByPhoneNumbersAsync(new[] { enteredPhone });
            foreach (var match in matches)
                _phoneMatches.Add(match);

            if (matches.Count == 0)
            {
                PhoneSearchStatusLabel.Text = "لم نعثر على حساب يطابق هذا الرقم. قد لا يكون الرقم مضافًا أو البحث عنه غير مفعّل.";
            }
            else
            {
                PhoneSearchStatusLabel.Text = matches.Count == 1
                    ? "عثرنا على حساب واحد. اضغط عليه لبدء محادثة."
                    : $"عثرنا على {matches.Count} حسابات. اختر حسابًا لبدء محادثة.";
            }
        }
        catch (Exception ex)
        {
            PhoneSearchStatusLabel.Text = "تعذر البحث الآن. تحقق من الاتصال بالخادم ثم حاول مجددًا.";
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Phone lookup failed: {ex}");
            await DisplayAlertAsync("البحث برقم الهاتف", ex.Message, "حسنًا");
        }
        finally
        {
            _phoneSearchBusy = false;
            PhoneSearchButton.IsEnabled = true;
            FindContactsButton.IsEnabled = true;
            PhoneSearchButton.Text = "بحث عن الرقم";
            PhoneSearchProgress.IsVisible = false;
        }
    }

    private async void FindContactsClicked(object? sender, EventArgs e) => await SearchDeviceContactsAsync();

    private async Task SearchDeviceContactsAsync()
    {
        if (_contactSearchBusy || _phoneSearchBusy) return;
        await _api.TokenInitialization;
        if (!_api.HasToken)
        {
            await DisplayAlertAsync("جهات الاتصال", "سجّل الدخول أولًا للبحث عن أصدقائك.", "حسنًا");
            return;
        }

        if (!Preferences.Default.Get("himo_contacts_lookup_consent", false))
        {
            var consent = await DisplayAlertAsync(
                "الخصوصية وجهات الاتصال",
                "سيقرأ Himo أرقام الهاتف المحفوظة في جهات اتصالك ويرسل الأرقام اللازمة للمطابقة عبر اتصال آمن. لن يحفظ الخادم دفتر العناوين؛ سيعيد فقط حسابات Himo المطابقة التي سمح أصحابها بالعثور عليهم.",
                "متابعة",
                "إلغاء");
            if (!consent) return;
            Preferences.Default.Set("himo_contacts_lookup_consent", true);
        }

#if ANDROID
        try
        {
            _contactSearchBusy = true;
            PhoneSearchButton.IsEnabled = false;
            FindContactsButton.IsEnabled = false;
            FindContactsButton.Text = "جارٍ قراءة الأسماء...";
            PhoneSearchProgress.IsVisible = true;
            PhoneSearchStatusLabel.TextColor = Color.FromArgb("#C59BE8");
            PhoneSearchStatusLabel.Text = "جارٍ طلب الإذن وقراءة جهات الاتصال...";
            _phoneMatches.Clear();

            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.ContactsRead>();

            if (permission != PermissionStatus.Granted)
            {
                PhoneSearchStatusLabel.Text = "لم يتم منح إذن جهات الاتصال. يمكنك تفعيله من إعدادات Android ثم المحاولة مجددًا.";
                return;
            }

            var contacts = await Contacts.Default.GetAllAsync();
            if (contacts is null)
            {
                PhoneSearchStatusLabel.Text = "تعذر قراءة جهات الاتصال من الهاتف.";
                return;
            }

            var numbers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contact in contacts)
            {
                if (contact?.Phones is null) continue;
                foreach (var contactPhone in contact.Phones)
                {
                    var normalized = NormalizeContactNumber(contactPhone?.PhoneNumber);
                    if (normalized is not null)
                        numbers.Add(normalized);
                    if (numbers.Count >= 5000) break;
                }
                if (numbers.Count >= 5000) break;
            }

            if (numbers.Count == 0)
            {
                PhoneSearchStatusLabel.Text = "لم نعثر على أرقام هاتف صالحة في جهات اتصالك.";
                return;
            }

            var matches = new Dictionary<Guid, ContactMatchDto>();
            var batches = numbers.Chunk(200).ToArray();
            for (var i = 0; i < batches.Length; i++)
            {
                PhoneSearchStatusLabel.Text = $"جارٍ مطابقة الأرقام... ({i + 1}/{batches.Length})";
                var found = await _api.FindUsersByPhoneNumbersAsync(batches[i]);
                foreach (var match in found)
                    matches[match.Id] = match;
            }

            var ordered = matches.Values.OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList();
            foreach (var match in ordered)
                _phoneMatches.Add(match);

            PhoneSearchStatusLabel.Text = ordered.Count == 0
                ? "لم نعثر على حسابات Himo مطابقة. قد لا تكون الأرقام مسجلة أو لم يفعّل أصحاب الحسابات خيار العثور عليهم."
                : $"عُثر على {ordered.Count} حسابًا من جهات اتصالك. اضغط على حساب لبدء محادثة.";
        }
        catch (Exception ex)
        {
            PhoneSearchStatusLabel.Text = "تعذر البحث في جهات الاتصال. تحقق من الاتصال بالخادم ثم حاول مجددًا.";
            System.Diagnostics.Debug.WriteLine($"[Himo Home] Contacts lookup failed: {ex}");
            await DisplayAlertAsync("البحث في جهات الاتصال", ex.Message, "حسنًا");
        }
        finally
        {
            _contactSearchBusy = false;
            PhoneSearchButton.IsEnabled = true;
            FindContactsButton.IsEnabled = true;
            FindContactsButton.Text = "البحث في جهات الاتصال";
            PhoneSearchProgress.IsVisible = false;
        }
#else
        await DisplayAlertAsync("جهات الاتصال", "ميزة البحث في جهات الاتصال متاحة حاليًا على Android.", "حسنًا");
#endif
    }

    private static string? NormalizeContactNumber(string? value)
    {
        var digits = PhoneNumberUtils.Normalize(value);
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal))
            digits = "966" + digits[1..];
        else if (digits.Length == 9 && digits.StartsWith("5", StringComparison.Ordinal))
            digits = "966" + digits;
        return digits.Length is >= 7 and <= 15 ? digits : null;
    }

    private async void PhoneSearchResults_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_openingMatchedConversation || e.CurrentSelection.FirstOrDefault() is not ContactMatchDto selected)
            return;

        if (sender is CollectionView collection)
            collection.SelectedItem = null;

        _openingMatchedConversation = true;
        try
        {
            PhoneSearchStatusLabel.Text = "جارٍ تجهيز المحادثة...";
            var conversation = await _vm.CreateConversationWithUserAsync(selected.Name, selected.Id);
            var routeId = !string.IsNullOrWhiteSpace(conversation.RemoteId)
                ? conversation.RemoteId
                : conversation.Id.ToString();
            ShowSection("chats");
            var shell = Shell.Current;
            if (shell is not null) await shell.GoToAsync($"chat?id={routeId}", false);
        }
        catch (Exception ex)
        {
            PhoneSearchStatusLabel.Text = "تعذر بدء المحادثة.";
            await DisplayAlertAsync("المحادثة", ex.Message, "حسنًا");
        }
        finally
        {
            _openingMatchedConversation = false;
        }
    }

    private void CancelConversationSelectionClicked(object? sender, EventArgs e) => ClearConversationSelection();

    private async void DeleteSelectedConversationsClicked(object? sender, EventArgs e)
    {
        if (_selectedConversations.Count == 0) return;
        var selected = _selectedConversations.ToList();
        var confirmed = await DisplayAlertAsync(
            "حذف المحادثات",
            selected.Count == 1
                ? $"حذف محادثة {selected[0].Name} من قائمتك؟ يمكنك البحث عن الشخص وبدء محادثة معه لاحقًا."
                : $"حذف {selected.Count} محادثات من قائمتك؟ يمكنك البحث عن الأشخاص وبدء محادثات جديدة لاحقًا.",
            "حذف", "إلغاء");
        if (!confirmed) return;
        await ApplyConversationActionAsync("delete", selected);
    }

    private async void MuteSelectedConversationsClicked(object? sender, EventArgs e)
    {
        if (_selectedConversations.Count == 0) return;
        var selected = _selectedConversations.ToList();
        var action = selected.All(item => item.IsMuted) ? "unmute" : "mute";
        await ApplyConversationActionAsync(action, selected);
    }

    private async void ArchiveSelectedConversationsClicked(object? sender, EventArgs e)
    {
        if (_selectedConversations.Count == 0) return;
        var action = _vm.ShowArchived ? "unarchive" : "archive";
        await ApplyConversationActionAsync(action, _selectedConversations.ToList());
    }

    private async void BlockSelectedConversationClicked(object? sender, EventArgs e)
    {
        if (_selectedConversations.Count != 1) return;
        var selected = _selectedConversations.Single();
        if (selected.IsGroup)
        {
            await DisplayAlertAsync("الحظر", "يمكن حظر حساب فردي فقط، وليس مجموعة كاملة.", "حسنًا");
            return;
        }

        var action = selected.IsBlocked ? "unblock" : "block";
        if (action == "block")
        {
            var confirmed = await DisplayAlertAsync(
                "حظر الحساب",
                $"هل تريد حظر {selected.Name}؟ لن تتمكن الحسابات المحظورة من إرسال رسائل أو بدء مكالمات مباشرة بينكما.",
                "حظر", "إلغاء");
            if (!confirmed) return;
        }
        await ApplyConversationActionAsync(action, new[] { selected });
    }

    private async Task ApplyConversationActionAsync(string action, IReadOnlyCollection<Conversation> selected)
    {
        var succeeded = new List<Conversation>();
        var failures = new List<string>();
        await _api.TokenInitialization;

        foreach (var conversation in selected)
        {
            try
            {
                if (Guid.TryParse(conversation.RemoteId, out var remoteId) && remoteId != Guid.Empty)
                {
                    if (!_api.HasToken)
                        throw new InvalidOperationException("سجّل الدخول مجددًا لتنفيذ هذا الإجراء.");
                    await _api.SetConversationActionAsync(remoteId, action);
                }
                else if (action is "block" or "unblock")
                {
                    throw new InvalidOperationException("الحظر متاح للمحادثات المرتبطة بحساب على الخادم فقط.");
                }

                switch (action)
                {
                    case "delete":
                        _vm.Conversations.Remove(conversation);
                        UpdateLegacyArchiveList(conversation, archived: false);
                        break;
                    case "archive":
                        conversation.IsArchived = true;
                        UpdateLegacyArchiveList(conversation, archived: true);
                        break;
                    case "unarchive":
                        conversation.IsArchived = false;
                        UpdateLegacyArchiveList(conversation, archived: false);
                        break;
                    case "mute":
                        conversation.IsMuted = true;
                        break;
                    case "unmute":
                        conversation.IsMuted = false;
                        break;
                    case "block":
                        conversation.IsBlocked = true;
                        break;
                    case "unblock":
                        conversation.IsBlocked = false;
                        break;
                }
                succeeded.Add(conversation);
            }
            catch (Exception ex)
            {
                failures.Add($"{conversation.Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Himo Home] Conversation action '{action}' failed: {ex}");
            }
        }

        ClearConversationSelection();
        _vm.Filter();
        UpdateEmptyState();

        // Refresh the flags from the server after persisting per-account actions.
        if (succeeded.Count > 0 && _api.HasToken)
            await _vm.RefreshFromServerAsync();

        if (failures.Count > 0)
        {
            var summary = failures.Count == 1 ? failures[0] : $"تعذر تنفيذ الإجراء على {failures.Count} محادثات.";
            await DisplayAlertAsync("تعذر إكمال بعض الإجراءات", summary, "حسنًا");
            return;
        }

        var resultMessage = action switch
        {
            "delete" => succeeded.Count == 1 ? "تم حذف المحادثة من قائمتك. يمكنك البحث عن الشخص لبدء محادثة مجددًا." : $"تم حذف {succeeded.Count} محادثات من قائمتك.",
            "archive" => "تمت أرشفة المحادثات المحددة. افتح رمز الأرشيف بجانب البحث لعرضها لاحقًا.",
            "unarchive" => "تمت إعادة المحادثات إلى القائمة الرئيسية.",
            "mute" => "تم كتم إشعارات المحادثات المحددة.",
            "unmute" => "تم تفعيل إشعارات المحادثات المحددة.",
            "block" => "تم حظر الحساب. لن يتمكن من إرسال رسائل أو بدء مكالمات مباشرة معك.",
            "unblock" => "تم إلغاء الحظر.",
            _ => "تم تحديث المحادثات المحددة."
        };
        await DisplayAlertAsync("Himo", resultMessage, "حسنًا");
    }

    private static void UpdateLegacyArchiveList(Conversation conversation, bool archived)
    {
        if (string.IsNullOrWhiteSpace(conversation.RemoteId)) return;
        var existing = Preferences.Default.Get("himo_archived_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (archived) existing.Add(conversation.RemoteId);
        else existing.Remove(conversation.RemoteId);
        Preferences.Default.Set("himo_archived_conversations", string.Join(',', existing));
    }

    private async void CallAudioAgainClicked(object? sender, EventArgs e) => await OpenCallAgainAsync(sender, "audio");
    private async void CallVideoAgainClicked(object? sender, EventArgs e) => await OpenCallAgainAsync(sender, "video");

    private async Task OpenCallAgainAsync(object? sender, string mode)
    {
        if (sender is not ImageButton button || button.BindingContext is not CallHistoryEntry call)
            return;
        if (!Guid.TryParse(call.ConversationId, out var conversationId) || conversationId == Guid.Empty)
        {
            await DisplayAlertAsync("المكالمة", "المحادثة المرتبطة بهذه المكالمة غير متاحة. افتح المحادثة من القائمة أولًا.", "حسنًا");
            return;
        }

        try
        {
            var peer = Uri.EscapeDataString(call.ContactName ?? "جهة اتصال");
            await Shell.Current.GoToAsync($"CallPage?id={conversationId:D}&mode={mode}&peerName={peer}");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المكالمة", ex.Message, "حسنًا");
        }
    }
}
