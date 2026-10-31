using System.Collections.Concurrent;
using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Himo.Platforms.Android.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;

namespace Himo.Views;

[QueryProperty(nameof(ExistingGroupId), "groupId")]
[QueryProperty(nameof(ExistingGroupName), "groupName")]
public partial class CreateGroupPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private readonly GroupDraft _draft = new();
    private readonly Dictionary<Guid, GroupMemberDraft> _selected = new();
    private readonly List<GroupSelectableMember> _chatMembers = new();
    private readonly List<GroupSelectableMember> _phoneMembers = new();
    private readonly List<GroupSelectableMember> _searchMembers = new();
    private readonly List<GroupInviteContact> _inviteContacts = new();
    private readonly Dictionary<Guid, GroupSelectableMember> _knownMembers = new();
    private readonly Dictionary<Guid, List<(Border Border, Label Mark)>> _selectionIndicators = new();
    private readonly Dictionary<Guid, List<(Image Image, Label Initial)>> _avatarViews = new();
    private readonly Dictionary<Guid, View> _selectedChipViews = new();
    private readonly Dictionary<Guid, (Image Image, Label Initial)> _selectedAvatarViews = new();
    private readonly HashSet<Guid> _avatarLoadsStarted = new();
    private readonly SemaphoreSlim _avatarDownloadGate = new(3, 3);
    // Keep the first frame deliberately light; all contacts remain available through search and paging.
    private const int RowsPerPage = 6;
    private const int AvatarPrefetchCount = 2;
    private int _memberRefreshGeneration;
    private int _chatRenderLimit = RowsPerPage;
    private int _phoneRenderLimit = RowsPerPage;
    private int _searchRenderLimit = RowsPerPage;
    private int _inviteRenderLimit = RowsPerPage;
    private HorizontalStackLayout _selectedHost = new();
    private VerticalStackLayout _chatHost = new();
    private VerticalStackLayout _phoneHost = new();
    private VerticalStackLayout _searchHost = new();
    private View? _searchSection;
    private View? _chatSection;
    private View? _phoneSection;
    private View? _inviteSection;
    private VerticalStackLayout _inviteHost = new();
    private Entry _searchEntry = new();
    private Label _selectedCountLabel = new();
    private Border _selectedCard = new();
    private Button _nextButton = new();
    private Button _footerActionButton = new();
    private ActivityIndicator _busy = new();
    private bool _contactsLoaded;
    private bool _searching;
    private CancellationTokenSource? _memberFilterCts;
    private bool _creating;
    private string? _existingGroupId;
    private string? _existingGroupName;

    public string ExistingGroupId
    {
        get => _existingGroupId ?? string.Empty;
        set => _existingGroupId = value?.Trim();
    }

    public string ExistingGroupName
    {
        get => _existingGroupName ?? string.Empty;
        set => _existingGroupName = value?.Trim();
    }

    private bool IsAddingToExistingGroup =>
        Guid.TryParse(_existingGroupId, out var id) && id != Guid.Empty;

    public CreateGroupPage(HimoApiClient api, HomeViewModel home)
    {
        InitializeComponent();
        AppLocalization.AttachToPage(this);
        _api = api;
        _home = home;
        BuildSelectionUi();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateNavigationActions();
        if (_contactsLoaded) return;
        _contactsLoaded = true;
        await LoadPeopleAsync();
    }

    private void BuildSelectionUi()
    {
        var page = new Grid
        {
            Padding = new Thickness(18, 10, 18, 14),
            RowSpacing = 12,
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto),
                new(GridLength.Star), new(GridLength.Auto)
            }
        };
        page.SetDynamicResource(BackgroundColorProperty, "HimoBackground");

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(46), new(GridLength.Star), new(104) },
            ColumnSpacing = 10,
            HeightRequest = 50
        };
        var back = MakeIconButton("‹", 42);
        back.Clicked += BackClicked;
        header.Add(back, 0, 0);
        var title = MakeLabel(IsAddingToExistingGroup ? "إضافة أعضاء" : "مجموعة جديدة", 20, true, primary: true);
        header.Add(title, 1, 0);

        _nextButton = MakeActionButton("متابعة  ‹", 44);
        _nextButton.IsVisible = false;
        _nextButton.Clicked += NextClicked;
        header.Add(_nextButton, 2, 0);
        page.Add(header, 0, 0);

        var searchRow = new Grid
        {
            Padding = new Thickness(14, 0),
            HeightRequest = 54,
            ColumnDefinitions = new ColumnDefinitionCollection { new(28), new(GridLength.Star) },
            ColumnSpacing = 10
        };
        var searchIcon = new Image
        {
            Source = "himo_theme_p_neon_violet_home_search_neon.png",
            WidthRequest = 22,
            HeightRequest = 22,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center
        };
        searchRow.Add(searchIcon, 0, 0);
        _searchEntry = new Entry
        {
            Placeholder = "ابحث بالاسم أو رقم الهاتف",
            FontSize = 14,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            ReturnType = ReturnType.Search,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalOptions = LayoutOptions.Center
        };
        _searchEntry.TextChanged += SearchTextChanged;
        _searchEntry.Completed += SearchCompleted;
        searchRow.Add(_searchEntry, 1, 0);
        var searchCard = MakeCard(searchRow);
        searchCard.Padding = new Thickness(0);
        searchCard.StrokeThickness = 1;
        page.Add(searchCard, 0, 2);

        var selectedContent = new VerticalStackLayout { Spacing = 7, Padding = new Thickness(12, 9) };
        var selectedHeader = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) } };
        selectedHeader.Add(MakeLabel("المحددون", 12, true, primary: true), 0, 0);
        _selectedCountLabel = MakeLabel("0", 12, true, accent: true);
        selectedHeader.Add(_selectedCountLabel, 1, 0);
        selectedContent.Add(selectedHeader);
        _selectedHost = new HorizontalStackLayout { Spacing = 9, Padding = new Thickness(1, 1) };
        selectedContent.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HeightRequest = 68,
            Content = _selectedHost,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never
        });
        _selectedCard = MakeCard(selectedContent);
        _selectedCard.IsVisible = false;
        _selectedCard.Padding = new Thickness(0);
        page.Add(_selectedCard, 0, 1);

        var body = new VerticalStackLayout { Spacing = 15, Padding = new Thickness(0, 2, 0, 8) };
        _searchSection = MakeSection("نتائج البحث", _searchHost);
        _searchSection.IsVisible = false;
        _chatSection = MakeSection("محادثاتك", _chatHost);
        _phoneSection = MakeSection("جهات اتصال Himo", _phoneHost);
        _inviteSection = MakeSection("دعوة الأصدقاء", _inviteHost);
        _chatSection.IsVisible = false;
        _phoneSection.IsVisible = false;
        _inviteSection.IsVisible = false;
        body.Add(_searchSection);
        body.Add(_chatSection);
        body.Add(_phoneSection);
        body.Add(_inviteSection);

        page.Add(new ScrollView
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never
        }, 0, 3);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 2, 0, 0)
        };
        _busy = new ActivityIndicator { IsVisible = false, IsRunning = false, VerticalOptions = LayoutOptions.Center };
        _busy.SetDynamicResource(ActivityIndicator.ColorProperty, "HimoAccent");
        footer.Add(_busy, 0, 0);
        _footerActionButton = MakeActionButton("إضافة الأعضاء", 50);
        _footerActionButton.Clicked += NextClicked;
        _footerActionButton.IsVisible = false;
        footer.Add(_footerActionButton, 1, 0);
        page.Add(footer, 0, 4);

        RootGrid.Children.Clear();
        RootGrid.Children.Add(page);
    }

    private View MakeSection(string title, VerticalStackLayout host)
    {
        var section = new VerticalStackLayout { Spacing = 8 };
        var heading = MakeLabel(title, 14, true, primary: true);
        heading.Margin = new Thickness(2, 0, 2, 0);
        section.Add(heading);
        host.Children.Clear();
        var listCard = MakeCard(host);
        listCard.Padding = new Thickness(5, 2);
        listCard.StrokeThickness = 0.8;
        section.Add(listCard);
        return section;
    }

    private async Task LoadPeopleAsync()
    {
        try
        {
            // Use the already-loaded conversation cache. Refreshing the whole home
            // feed here delayed the picker and duplicated work when opening this page.
            foreach (var conversation in _home.Conversations
                         .Where(x => !x.IsGroup && x.OtherParticipantUserId.HasValue)
                         .GroupBy(x => x.OtherParticipantUserId!.Value)
                         .Select(g => g.OrderByDescending(x => x.UpdatedAt).First())
                         .OrderBy(x => x.Name, StringComparer.CurrentCulture))
            {
                var id = conversation.OtherParticipantUserId!.Value;
                if (id == Guid.Empty) continue;
                var member = new GroupSelectableMember(
                    id, conversation.Name, null, null,
                    conversation.HasRemoteProfilePhoto,
                    conversation.ProfilePhotoVersion,
                    conversation.ProfilePhotoPath);
                _chatMembers.Add(member);
                _knownMembers[id] = member;
            }

            RefreshMemberLists();

            // Do not make the page wait for Android's entire address book or the
            // server-side phone matching. The user can already select recent chats.
            _ = LoadPhoneContactsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Groups] Load people failed: {ex}");
            await DisplayAlertAsync("جهات الاتصال", "تعذر تحميل الأشخاص الآن. يمكنك البحث عن حساب بالاسم أو رقم الهاتف.", "حسنًا");
        }
    }

    private async Task LoadPhoneContactsAsync()
    {
#if ANDROID
        if (!Preferences.Default.Get("himo_contacts_lookup_consent", false))
        {
            var consent = await DisplayAlertAsync(
                "الخصوصية وجهات الاتصال",
                "سيقرأ Himo أرقام الهاتف المحفوظة في جهات اتصالك ويرسل الأرقام اللازمة للمطابقة عبر اتصال آمن. لن يحفظ الخادم دفتر العناوين؛ سيعيد فقط حسابات Himo المطابقة التي سمح أصحابها بالعثور عليهم.",
                "متابعة", "إلغاء");
            if (!consent) return;
            Preferences.Default.Set("himo_contacts_lookup_consent", true);
        }

        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted) return;

            // Read the address book away from the UI thread and dispose native
            // ContentProvider cursors deterministically.
            var contacts = await AndroidContactReader.ReadAllAsync();
            var phoneContacts = await Task.Run(() => contacts
                .SelectMany(contact => contact.PhoneNumbers.Select(phone =>
                {
                    var normalized = NormalizePhone(phone);
                    return normalized is null
                        ? null
                        : new GroupInviteContact(
                            string.IsNullOrWhiteSpace(contact.Name) ? phone : contact.Name,
                            phone,
                            normalized);
                }))
                .Where(contact => contact is not null)
                .Cast<GroupInviteContact>()
                .GroupBy(contact => contact.NormalizedPhone!, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList());

            await _api.TokenInitialization;
            if (!_api.HasToken)
            {
                _inviteContacts.AddRange(phoneContacts
                    .GroupBy(x => x.NormalizedPhone!, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderBy(x => x.Name, StringComparer.CurrentCulture));
                RefreshMemberLists();
                return;
            }

            var uniqueNumbers = phoneContacts.Select(x => x.NormalizedPhone!).Distinct(StringComparer.Ordinal).Take(5000).ToArray();
            var matches = new ConcurrentDictionary<Guid, ContactMatchDto>();
            var matchedNumbers = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            var batches = uniqueNumbers.Chunk(200).ToArray();
            using (var matchGate = new SemaphoreSlim(3, 3))
            {
                await Task.WhenAll(batches.Select(async batch =>
                {
                    await matchGate.WaitAsync();
                    try
                    {
                        var found = await _api.FindUsersByPhoneNumbersAsync(batch);
                        foreach (var match in found)
                        {
                            if (match.Id == Guid.Empty) continue;
                            matches[match.Id] = match;
                            var normalizedMatch = NormalizePhone(match.MatchedPhoneNumber);
                            if (normalizedMatch is not null) matchedNumbers.TryAdd(normalizedMatch, 0);
                        }
                    }
                    finally { matchGate.Release(); }
                }));
            }

            var chatIds = _chatMembers.Select(x => x.UserId).ToHashSet();
            var matchSnapshot = matches.Values.ToArray();
            var matchedNumberSet = matchedNumbers.Keys.ToHashSet(StringComparer.Ordinal);
            var matchedAny = matchedNumberSet.Count > 0
                ? matchedNumberSet
                : matchSnapshot.Select(x => NormalizePhone(x.MatchedPhoneNumber)).Where(x => x is not null).Cast<string>().ToHashSet(StringComparer.Ordinal);

            // Sorting and grouping a large phone book is CPU work; keep it off the UI thread.
            var prepared = await Task.Run(() =>
            {
                var members = matchSnapshot
                    .Where(x => !chatIds.Contains(x.Id))
                    .OrderBy(x => x.Name, StringComparer.CurrentCulture)
                    .Select(x => new GroupSelectableMember(
                        x.Id, x.Name, null, x.MatchedPhoneNumber,
                        x.HasProfilePhoto, x.ProfilePhotoVersion))
                    .ToList();
                var invites = phoneContacts
                    .Where(x => !matchedAny.Contains(x.NormalizedPhone!))
                    .GroupBy(x => x.NormalizedPhone!, StringComparer.Ordinal)
                    .Select(g => g.First())
                    .OrderBy(x => x.Name, StringComparer.CurrentCulture)
                    .ToList();
                return (members, invites);
            });

            foreach (var member in prepared.members)
            {
                _phoneMembers.Add(member);
                _knownMembers[member.UserId] = member;
            }
            _inviteContacts.AddRange(prepared.invites);

            RefreshMemberLists();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Groups] Phone contacts lookup failed: {ex}");
            await DisplayAlertAsync("جهات الاتصال", "تعذر مطابقة جهات الاتصال الآن. يمكنك متابعة اختيار الأشخاص الذين تتواصل معهم بالفعل.", "حسنًا");
        }
#else
        await Task.CompletedTask;
#endif
    }

    private static string? NormalizePhone(string? value)
    {
        var digits = PhoneNumberUtils.Normalize(value);
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal)) digits = "966" + digits[1..];
        else if (digits.Length == 9 && digits.StartsWith("5", StringComparison.Ordinal)) digits = "966" + digits;
        return digits.Length is >= 7 and <= 15 ? digits : null;
    }

    private async void SearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = e.NewTextValue?.Trim() ?? string.Empty;
        _memberFilterCts?.Cancel();
        _memberFilterCts?.Dispose();
        var cts = new CancellationTokenSource();
        _memberFilterCts = cts;
        try
        {
            // Debounce typing so a long contact list is not rebuilt for every key.
            await Task.Delay(140, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_memberFilterCts, cts)) return;
            _chatRenderLimit = _phoneRenderLimit = _searchRenderLimit = _inviteRenderLimit = RowsPerPage;
            RefreshMemberLists(query);
        }
        catch (OperationCanceledException)
        {
            // A newer search text superseded this one.
        }
    }

    private async void SearchCompleted(object? sender, EventArgs e)
    {
        var query = _searchEntry.Text?.Trim() ?? string.Empty;
        if (query.Length < 3 || _searching) return;
        try
        {
            _searching = true;
            _busy.IsVisible = _busy.IsRunning = true;
            await _api.TokenInitialization;
            var users = await _api.SearchGroupMembersAsync(query);
            _searchMembers.Clear();
            var existing = _chatMembers.Concat(_phoneMembers).Select(x => x.UserId).ToHashSet();
            foreach (var user in users.Where(x => x.Id != Guid.Empty && !existing.Contains(x.Id)))
            {
                var member = new GroupSelectableMember(
                    user.Id, user.Name, null, user.Email,
                    user.HasProfilePhoto, user.ProfilePhotoVersion);
                _searchMembers.Add(member);
                _knownMembers[user.Id] = member;
            }
            RefreshMemberLists(query);
            _ = LoadMemberAvatarsAsync(_searchMembers);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("البحث عن الأشخاص", ex.Message, "حسنًا");
        }
        finally
        {
            _searching = false;
            _busy.IsVisible = _busy.IsRunning = false;
        }
    }

    private void RefreshMemberLists(string? filter = null)
    {
        filter ??= _searchEntry.Text?.Trim() ?? string.Empty;
        var generation = Interlocked.Increment(ref _memberRefreshGeneration);

        // Snapshot on the UI thread, but do all potentially large filtering/sorting
        // on a worker. Older searches are discarded before touching the visual tree.
        var searchSnapshot = _searchMembers.ToArray();
        var chatSnapshot = _chatMembers.ToArray();
        var phoneSnapshot = _phoneMembers.ToArray();
        var inviteSnapshot = _inviteContacts.ToArray();
        _ = RefreshMemberListsCoreAsync(filter, generation, searchSnapshot, chatSnapshot, phoneSnapshot, inviteSnapshot);
    }

    private async Task RefreshMemberListsCoreAsync(
        string filter,
        int generation,
        GroupSelectableMember[] searchSnapshot,
        GroupSelectableMember[] chatSnapshot,
        GroupSelectableMember[] phoneSnapshot,
        GroupInviteContact[] inviteSnapshot)
    {
        try
        {
            var result = await Task.Run(() => (
                Search: searchSnapshot.Where(x => Matches(x, filter)).ToList(),
                Chats: chatSnapshot.Where(x => Matches(x, filter)).ToList(),
                Phone: phoneSnapshot.Where(x => Matches(x, filter)).ToList(),
                Invites: inviteSnapshot.Where(x => Matches(x, filter)).ToList()));

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (generation != Volatile.Read(ref _memberRefreshGeneration)) return;
                var hasFilter = !string.IsNullOrWhiteSpace(filter);

                _selectionIndicators.Clear();
                _avatarViews.Clear();

                if (_searchSection is not null) _searchSection.IsVisible = hasFilter && result.Search.Count > 0;
                if (_chatSection is not null) _chatSection.IsVisible = result.Chats.Count > 0;
                if (_phoneSection is not null) _phoneSection.IsVisible = result.Phone.Count > 0;
                if (_inviteSection is not null) _inviteSection.IsVisible = result.Invites.Count > 0;

                RenderRows(_searchHost, result.Search, _searchRenderLimit, () => _searchRenderLimit += RowsPerPage);
                RenderRows(_chatHost, result.Chats, _chatRenderLimit, () => _chatRenderLimit += RowsPerPage);
                RenderRows(_phoneHost, result.Phone, _phoneRenderLimit, () => _phoneRenderLimit += RowsPerPage);
                RenderInviteRows(_inviteHost, result.Invites, _inviteRenderLimit, () => _inviteRenderLimit += RowsPerPage);
                RefreshSelectedStrip();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Groups] Member list refresh failed: {ex.Message}");
        }
    }

    private static bool Matches(GroupSelectableMember item, string query) =>
        string.IsNullOrWhiteSpace(query) || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        (item.Subtitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (item.Contact?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool Matches(GroupInviteContact item, string query) =>
        string.IsNullOrWhiteSpace(query) || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Phone.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void RenderRows(
        VerticalStackLayout host,
        IReadOnlyList<GroupSelectableMember> members,
        int renderLimit,
        Action increaseLimit)
    {
        host.Children.Clear();
        var visibleMembers = members.Take(renderLimit).ToArray();
        for (var i = 0; i < visibleMembers.Length; i++)
        {
            host.Children.Add(CreateMemberRow(visibleMembers[i]));
            if (i < visibleMembers.Length - 1)
                host.Children.Add(CreateDivider());
        }

        if (members.Count > visibleMembers.Length)
        {
            var remaining = members.Count - visibleMembers.Length;
            var more = MakeOutlineButton($"عرض المزيد ({remaining})", 160, 38);
            more.HorizontalOptions = LayoutOptions.Center;
            more.Margin = new Thickness(0, 8, 0, 8);
            more.Clicked += (_, _) =>
            {
                increaseLimit();
                RefreshMemberLists();
            };
            host.Children.Add(more);
        }

        // Prefetch images only for rows actually materialized on screen, not for
        // every contact in the phone book (which could enqueue thousands of tasks).
        _ = LoadMemberAvatarsAfterFirstFrameAsync(visibleMembers.Take(AvatarPrefetchCount).ToArray());
    }

    private View CreateMemberRow(GroupSelectableMember member)
    {
        var selected = _selected.ContainsKey(member.UserId);
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(48), new(GridLength.Star), new(30) },
            ColumnSpacing = 11,
            Padding = new Thickness(8, 9),
            MinimumHeightRequest = 62
        };

        var avatar = CreateMemberAvatar(member.UserId, member.Name, member.PhotoPath, 44, 17);
        avatar.VerticalOptions = LayoutOptions.Center;
        row.Add(avatar, 0, 0);

        var details = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        details.Add(MakeLabel(member.Name, 14, true, primary: true));
        var secondary = member.Contact;
        if (string.IsNullOrWhiteSpace(secondary) && !string.IsNullOrWhiteSpace(member.Subtitle) &&
            member.Subtitle is not "محادثة موجودة" and not "جهة اتصال تستخدم Himo" and not "نتيجة بحث")
            secondary = member.Subtitle;
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            var detailLabel = MakeLabel(secondary, 11, false, secondary: true);
            details.Add(detailLabel);
        }
        row.Add(details, 1, 0);

        var checkMark = new Label
        {
            Text = selected ? "✓" : string.Empty,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        checkMark.SetDynamicResource(Label.TextColorProperty, "HimoOnAccent");
        var check = new Border
        {
            WidthRequest = 25,
            HeightRequest = 25,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(13) },
            Content = checkMark,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        check.SetDynamicResource(BackgroundColorProperty, selected ? "HimoAccent" : "HimoSoft");
        check.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
        if (!_selectionIndicators.TryGetValue(member.UserId, out var indicators))
            _selectionIndicators[member.UserId] = indicators = new List<(Border Border, Label Mark)>();
        indicators.Add((check, checkMark));
        row.Add(check, 2, 0);

        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => SetMemberSelected(member, !_selected.ContainsKey(member.UserId)))
        });
        SemanticProperties.SetDescription(row, selected ? $"إلغاء تحديد {member.Name}" : $"تحديد {member.Name}");
        return row;
    }

    private void RenderInviteRows(
        VerticalStackLayout host,
        IReadOnlyList<GroupInviteContact> contacts,
        int renderLimit,
        Action increaseLimit)
    {
        host.Children.Clear();
        var visibleContacts = contacts.Take(renderLimit).ToArray();
        for (var i = 0; i < visibleContacts.Length; i++)
        {
            var contact = visibleContacts[i];
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new(46), new(GridLength.Star), new(76) },
                ColumnSpacing = 10,
                Padding = new Thickness(8, 8),
                MinimumHeightRequest = 60
            };
            var initial = string.IsNullOrWhiteSpace(contact.Name) ? "؟" : contact.Name.Trim()[0].ToString();
            var avatar = new Border
            {
                WidthRequest = 42,
                HeightRequest = 42,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(21) },
                Content = MakeLabel(initial, 16, true, accent: true)
            };
            avatar.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
            avatar.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
            row.Add(avatar, 0, 0);

            var labels = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
            labels.Add(MakeLabel(contact.Name, 13.5, true, primary: true));
            labels.Add(MakeLabel(contact.Phone, 11, false, secondary: true));
            row.Add(labels, 1, 0);

            var inviteText = MakeLabel("دعوة", 11, true, accent: true);
            inviteText.HorizontalTextAlignment = TextAlignment.Center;
            inviteText.VerticalTextAlignment = TextAlignment.Center;
            var invite = new Border
            {
                WidthRequest = 72,
                HeightRequest = 36,
                Padding = new Thickness(6, 0),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(11) },
                Content = inviteText,
                VerticalOptions = LayoutOptions.Center
            };
            invite.SetDynamicResource(BackgroundColorProperty, "HimoSurface");
            invite.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
            invite.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await InviteContactAsync(contact))
            });
            row.Add(invite, 2, 0);
            host.Children.Add(row);
            if (i < visibleContacts.Length - 1) host.Children.Add(CreateDivider());
        }

        if (contacts.Count > visibleContacts.Length)
        {
            var more = MakeOutlineButton($"عرض المزيد ({contacts.Count - visibleContacts.Length})", 160, 38);
            more.HorizontalOptions = LayoutOptions.Center;
            more.Margin = new Thickness(0, 8, 0, 8);
            more.Clicked += (_, _) =>
            {
                increaseLimit();
                RefreshMemberLists();
            };
            host.Children.Add(more);
        }
    }

    private BoxView CreateDivider()
    {
        var divider = new BoxView { HeightRequest = 1, Margin = new Thickness(8, 0), Opacity = 0.7 };
        divider.SetDynamicResource(VisualElement.BackgroundColorProperty, "HimoDivider");
        return divider;
    }

    private async Task InviteContactAsync(GroupInviteContact contact)
    {
        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "دعوة إلى Himo",
                Text = $"مرحبًا {contact.Name}، أدعوك للانضمام إلى Himo للتواصل معي. رقم التواصل: {contact.Phone}"
            });
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("دعوة جهة اتصال", ex.Message, "حسنًا");
        }
    }

    private void SetMemberSelected(GroupSelectableMember member, bool selected)
    {
        if (selected)
        {
            if (!_selected.ContainsKey(member.UserId))
            {
                _selected[member.UserId] = new GroupMemberDraft
                {
                    UserId = member.UserId,
                    Name = member.Name,
                    PhoneNumber = member.Contact,
                    HasProfilePhoto = member.HasProfilePhoto,
                    ProfilePhotoVersion = member.ProfilePhotoVersion,
                    PhotoPath = member.PhotoPath
                };
            }
        }
        else
        {
            _selected.Remove(member.UserId);
        }

        // Update only this person's check indicators and selected chip. Do not
        // recreate all selected avatars/buttons on every tap.
        UpdateSelectionIndicators(member.UserId, selected);

        _draft.Members.Clear();
        foreach (var item in _selected.Values)
            _draft.Members.Add(item);

        RefreshSelectedStrip();
        if (selected && member.HasProfilePhoto && string.IsNullOrWhiteSpace(member.PhotoPath))
            _ = LoadMemberAvatarsAsync(new[] { member });
    }

    private void UpdateSelectionIndicators(Guid userId, bool selected)
    {
        if (!_selectionIndicators.TryGetValue(userId, out var indicators)) return;
        foreach (var (border, mark) in indicators)
        {
            mark.Text = selected ? "✓" : string.Empty;
            border.SetDynamicResource(BackgroundColorProperty, selected ? "HimoAccent" : "HimoSoft");
            border.SetDynamicResource(Border.StrokeProperty, selected ? "HimoAccent" : "HimoBorder");
        }
    }

    private async Task LoadMemberAvatarsAfterFirstFrameAsync(GroupSelectableMember[] source)
    {
        // Let the first screen paint before any profile-photo downloads complete and
        // trigger native bitmap decoding. Only the first two rows of each section are prefetched.
        await Task.Delay(280);
        if (!IsVisible || source.Length == 0) return;
        await LoadMemberAvatarsAsync(source);
    }

    private async Task LoadMemberAvatarsAsync(IEnumerable<GroupSelectableMember> source)
    {
        var candidates = source
            .Where(x => x.HasProfilePhoto && string.IsNullOrWhiteSpace(x.PhotoPath) && _avatarLoadsStarted.Add(x.UserId))
            .ToArray();
        if (candidates.Length == 0) return;

        var tasks = candidates.Select(async member =>
        {
            await _avatarDownloadGate.WaitAsync();
            try
            {
                var path = await ProfilePhotoCache.GetPathAsync(
                    _api, member.UserId, true, member.ProfilePhotoVersion);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    member.PhotoPath = path;
                    if (_selected.TryGetValue(member.UserId, out var selectedMember))
                        selectedMember.PhotoPath = path;

                    if (_avatarViews.TryGetValue(member.UserId, out var views))
                    {
                        foreach (var (image, initial) in views)
                        {
                            image.Source = ImageSource.FromFile(path);
                            image.IsVisible = true;
                            initial.IsVisible = false;
                        }
                    }
                    if (_selectedAvatarViews.TryGetValue(member.UserId, out var selectedAvatar))
                    {
                        selectedAvatar.Image.Source = ImageSource.FromFile(path);
                        selectedAvatar.Image.IsVisible = true;
                        selectedAvatar.Initial.IsVisible = false;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Groups] Avatar load failed for {member.UserId}: {ex.Message}");
            }
            finally
            {
                _avatarDownloadGate.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private Border CreateMemberAvatar(Guid userId, string name, string? photoPath, double size, double initialSize)
        => CreateMemberAvatar(userId, name, photoPath, size, initialSize, out _, out _);

    private Border CreateMemberAvatar(
        Guid userId, string name, string? photoPath, double size, double initialSize,
        out Image photo, out Label initial)
    {
        initial = MakeLabel(
            string.IsNullOrWhiteSpace(name) ? "؟" : name.Trim()[0].ToString(),
            initialSize, true, accent: true);
        initial.HorizontalTextAlignment = TextAlignment.Center;
        initial.VerticalTextAlignment = TextAlignment.Center;

        var hasPhoto = !string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath);
        photo = new Image
        {
            Aspect = Aspect.AspectFill,
            IsVisible = hasPhoto,
            Source = hasPhoto ? ImageSource.FromFile(photoPath!) : null
        };
        initial.IsVisible = !hasPhoto;

        var content = new Grid();
        content.Add(initial);
        content.Add(photo);

        if (userId != Guid.Empty)
        {
            if (!_avatarViews.TryGetValue(userId, out var views))
                _avatarViews[userId] = views = new List<(Image Image, Label Initial)>();
            views.Add((photo, initial));
        }

        var avatar = new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(size / 2) },
            Clip = new EllipseGeometry
            {
                Center = new Point(size / 2, size / 2),
                RadiusX = size / 2,
                RadiusY = size / 2
            },
            Content = content
        };
        avatar.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        avatar.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return avatar;
    }

    private void RefreshSelectedStrip()
    {
        _selectedCountLabel.Text = _selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _selectedCard.IsVisible = _selected.Count > 0;
        UpdateNavigationActions();

        // Remove only chips for users who were deselected.
        foreach (var userId in _selectedChipViews.Keys.Where(id => !_selected.ContainsKey(id)).ToArray())
        {
            _selectedHost.Children.Remove(_selectedChipViews[userId]);
            _selectedChipViews.Remove(userId);
            _selectedAvatarViews.Remove(userId);
        }

        // Add only the newly selected user; existing chips remain untouched.
        foreach (var member in _selected.Values)
        {
            if (_selectedChipViews.ContainsKey(member.UserId))
                continue;

            var avatar = CreateMemberAvatar(
                member.UserId, member.Name, member.PhotoPath, 36, 14,
                out var avatarImage, out var avatarInitial);
            _selectedAvatarViews[member.UserId] = (avatarImage, avatarInitial);
            avatar.HorizontalOptions = LayoutOptions.Center;

            var name = MakeLabel(member.Name, 10.5, true, primary: true);
            name.HorizontalTextAlignment = TextAlignment.Center;
            name.WidthRequest = 62;
            name.LineBreakMode = LineBreakMode.TailTruncation;

            var removeLabel = MakeLabel("×", 13, true, accent: true);
            removeLabel.HorizontalTextAlignment = TextAlignment.Center;
            removeLabel.VerticalTextAlignment = TextAlignment.Center;
            var remove = new Border
            {
                WidthRequest = 20,
                HeightRequest = 20,
                Padding = 0,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Content = removeLabel,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start
            };
            remove.SetDynamicResource(BackgroundColorProperty, "HimoSurfaceElevated");
            remove.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    if (_knownMembers.TryGetValue(member.UserId, out var selectable))
                        SetMemberSelected(selectable, false);
                })
            });

            var avatarGrid = new Grid { WidthRequest = 40, HeightRequest = 39 };
            avatarGrid.Add(avatar, 0, 0);
            avatarGrid.Add(remove, 0, 0);
            var chipContent = new VerticalStackLayout
            {
                Spacing = 3,
                WidthRequest = 68,
                HorizontalOptions = LayoutOptions.Center,
                Children = { avatarGrid, name }
            };
            _selectedChipViews[member.UserId] = chipContent;
            _selectedHost.Children.Add(chipContent);
        }
    }

    private void UpdateNavigationActions()
    {
        var hasSelection = _selected.Count > 0;
        _nextButton.IsVisible = hasSelection && !IsAddingToExistingGroup;
        _footerActionButton.IsVisible = hasSelection && IsAddingToExistingGroup;
        if (IsAddingToExistingGroup)
            _footerActionButton.Text = "إضافة الأعضاء";
    }

    private async void NextClicked(object? sender, EventArgs e)
    {
        if (_selected.Count == 0 || _creating) return;
        if (IsAddingToExistingGroup)
        {
            await AddToExistingGroupAsync();
            return;
        }
        var settings = new GroupSettingsPage(_api, _home, _draft);
        await Navigation.PushAsync(settings, true);
    }

    private async Task AddToExistingGroupAsync()
    {
        if (_creating || _selected.Count == 0) return;
        if (!Guid.TryParse(_existingGroupId, out var groupId)) return;
        try
        {
            _creating = true;
            _busy.IsVisible = _busy.IsRunning = true;
            var result = await _api.AddGroupMembersAsync(groupId, _selected.Keys.ToArray());
            await _home.RefreshFromServerAsync();
            await DisplayAlertAsync("إضافة أعضاء", $"تمت إضافة {result.AddedCount} عضو.", "حسنًا");
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("تعذر إضافة الأعضاء", ex.Message, "حسنًا");
        }
        finally
        {
            _creating = false;
            _busy.IsVisible = _busy.IsRunning = false;
        }
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..", false);
    }

    private static Border MakeCard(View content)
    {
        var border = new Border
        {
            Content = content,
            Padding = new Thickness(10),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) }
        };
        border.SetDynamicResource(BackgroundColorProperty, "HimoSurface");
        border.SetDynamicResource(Border.StrokeProperty, "HimoBorder");
        return border;
    }

    private static Label MakeLabel(string text, double size, bool bold, bool primary = false, bool secondary = false, bool accent = false)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation };
        label.SetDynamicResource(Label.TextColorProperty, primary ? "HimoPrimary" : secondary ? "HimoTextSecondary" : accent ? "HimoAccent" : "HimoPrimary");
        return label;
    }

    private static Button MakeIconButton(string text, double size)
    {
        var button = new Button { Text = text, FontSize = 25, WidthRequest = size, HeightRequest = size, CornerRadius = (int)(size / 2), Padding = 0, BorderWidth = 1 };
        button.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        button.SetDynamicResource(Button.TextColorProperty, "HimoPrimary");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        return button;
    }

    private static Button MakeActionButton(string text, double height)
    {
        var button = new Button { Text = text, FontSize = 16, HeightRequest = height, CornerRadius = 16, FontAttributes = FontAttributes.Bold, Padding = new Thickness(14, 0) };
        button.SetDynamicResource(BackgroundColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.TextColorProperty, "HimoOnAccent");
        return button;
    }

    private static Button MakeOutlineButton(string text, double width, double height)
    {
        var button = new Button { Text = text, FontSize = 11, WidthRequest = width, HeightRequest = height, CornerRadius = 12, Padding = new Thickness(5, 0), BorderWidth = 1, FontAttributes = FontAttributes.Bold };
        button.SetDynamicResource(BackgroundColorProperty, "HimoSoft");
        button.SetDynamicResource(Button.TextColorProperty, "HimoAccent");
        button.SetDynamicResource(Button.BorderColorProperty, "HimoBorder");
        return button;
    }

    private sealed class GroupSelectableMember
    {
        public Guid UserId { get; }
        public string Name { get; }
        public string? Subtitle { get; }
        public string? Contact { get; }
        public bool HasProfilePhoto { get; }
        public long ProfilePhotoVersion { get; }
        public string? PhotoPath { get; set; }
        public string Initial => string.IsNullOrWhiteSpace(Name) ? "؟" : Name.Trim()[0].ToString();

        public GroupSelectableMember(
            Guid userId, string name, string? subtitle, string? contact,
            bool hasProfilePhoto = false, long profilePhotoVersion = 0, string? photoPath = null)
        {
            UserId = userId;
            Name = name ?? string.Empty;
            Subtitle = subtitle;
            Contact = contact;
            HasProfilePhoto = hasProfilePhoto;
            ProfilePhotoVersion = profilePhotoVersion;
            PhotoPath = photoPath;
        }
    }

    private sealed record GroupInviteContact(string Name, string Phone, string? NormalizedPhone);
}
