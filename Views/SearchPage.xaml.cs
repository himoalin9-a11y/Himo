using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Himo.Platforms.Android.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Shows phone contacts, placing discoverable Himo members first. Phone-number
/// lookup happens automatically after consent; names and the address book stay local.
/// </summary>
public partial class SearchPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private List<DeviceContactEntry> _visibleContacts = new();
    private List<LocalContactSnapshot> _localContacts = new();
    private List<DeviceContactEntry> _allEntries = new();
    private readonly Dictionary<string, ContactMatchDto> _matchedByPhone = new(StringComparer.Ordinal);
    private HimoApiClient.UserProfileResponse? _myProfile;
    private bool _contactsLoaded;
    private bool _loadingContacts;
    private bool _discoveringContacts;
    private bool _discoveryAttempted;
    private bool _discoveryDeclined;
    private bool _discoveryCompleted;
    private bool _handlingContactAction;
    private CancellationTokenSource? _filterCts;
    private string _phoneRegionIso = string.Empty;

    public SearchPage(HimoApiClient api, HomeViewModel home)
    {
        InitializeComponent();
        _api = api;
        _home = home;
        ContactsList.ItemsSource = _visibleContacts;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Keep the large address book page responsive: no full-page transition and
        // no recursive theme walk through generated CollectionView rows.
        _ = HimoAppearanceManager.ApplyToTreeAfterFirstFrameAsync(
            this, skipCollectionViewContents: true, batchSize: 12);

        if (!_contactsLoaded && !_loadingContacts)
            _ = LoadContactsAsync();
        else if (_contactsLoaded && !_discoveryAttempted && !_discoveringContacts && !_discoveryDeclined)
            _ = DiscoverRegisteredHimoContactsAsync();
        else if (_contactsLoaded)
            _ = RebuildContactEntriesAsync(); // Refresh accounts created or opened since the last visit.
    }

    protected override void OnDisappearing()
    {
        _filterCts?.Cancel();
        base.OnDisappearing();
    }

    private async Task LoadContactsAsync()
    {
        if (_loadingContacts) return;
#if ANDROID
        _loadingContacts = true;
        SetContactLoading(true, "جارٍ تحميل جهات الاتصال...");
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.ContactsRead>();

            if (permission != PermissionStatus.Granted)
            {
                _localContacts = new List<LocalContactSnapshot>();
                _contactsLoaded = false; // Retry the permission request if the user grants it in Android settings.
                await LoadSignedInProfileAsync();
                await RebuildContactEntriesAsync();
                ContactsStatusLabel.Text = "يمكنك العثور على حسابك ومحادثاتك السابقة دون إذن جهات الاتصال.";
                return;
            }

            // Native cursors are read on a worker and disposed inside AndroidContactReader.
            var nativeContacts = await AndroidContactReader.ReadAllAsync();
            _phoneRegionIso = GetPhoneRegionIso();
            var regionIso = _phoneRegionIso;
            var snapshots = await Task.Run(() => nativeContacts.Select(contact =>
            {
                var phoneNumbers = contact.PhoneNumbers
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                var emailAddresses = contact.EmailAddresses
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var displayName = string.IsNullOrWhiteSpace(contact.Name)
                    ? phoneNumbers.FirstOrDefault() ?? emailAddresses.FirstOrDefault() ?? "جهة اتصال بدون اسم"
                    : contact.Name.Trim();

                var normalizedPhones = phoneNumbers
                    .Select(value => NormalizePhoneForMatching(value, regionIso))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                return new LocalContactSnapshot(displayName, phoneNumbers, emailAddresses, normalizedPhones);
            }).ToList());

            _localContacts = snapshots;
            _contactsLoaded = true;
            await RebuildContactEntriesAsync();

            ContactsStatusLabel.Text = _localContacts.Count == 0
                ? "لا توجد جهات اتصال على الهاتف."
                : $"جهات الاتصال: {_localContacts.Count}";

            // Discover registered Himo users automatically; there is no manual Match button.
            await DiscoverRegisteredHimoContactsAsync();
        }
        catch (Exception ex)
        {
            ContactsStatusLabel.Text = "تعذر تحميل جهات الاتصال. تحقق من إذن Android ثم حاول مجددًا.";
            System.Diagnostics.Debug.WriteLine($"[Himo Contacts] Local contact load failed: {ex}");
            await DisplayAlertAsync("جهات الاتصال", ex.Message, "حسنًا");
        }
        finally
        {
            _loadingContacts = false;
            SetContactLoading(false, ContactsStatusLabel.Text);
        }
#else
        ContactsStatusLabel.Text = "عرض جهات الاتصال متاح حاليًا على Android فقط.";
        await Task.CompletedTask;
#endif
    }

    private async Task DiscoverRegisteredHimoContactsAsync()
    {
        if (_discoveringContacts || _discoveryAttempted || _discoveryDeclined || !_contactsLoaded)
            return;

        _discoveringContacts = true;
        try
        {
            await _api.TokenInitialization;
            if (!_api.HasToken)
            {
                ContactsStatusLabel.Text = $"جهات الاتصال: {_localContacts.Count} · سجّل الدخول لعرض مستخدمي Himo.";
                return;
            }

            // Load the signed-in profile independently of phone-discovery consent.
            // The owner should always be able to find and open their own self-chat.
            await LoadSignedInProfileAsync();

            if (!Preferences.Default.Get("himo_contacts_lookup_consent", false))
            {
                var consent = await DisplayAlertAsync(
                    "عرض مستخدمي Himo",
                    "لإظهار الأشخاص المسجلين في Himo ضمن جهات اتصالك، سيرسل التطبيق أرقام الهاتف فقط للتحقق بصورة آمنة. لن تُرسل الأسماء أو عناوين البريد، ولن يُحفظ دفتر الهاتف على الخادم.",
                    "متابعة",
                    "لاحقًا");
                if (!consent)
                {
                    _discoveryDeclined = true;
                    await RebuildContactEntriesAsync(); // Keep self and existing chats visible without phone matching.
                    ContactsStatusLabel.Text = $"جهات الاتصال: {_localContacts.Count}";
                    return;
                }

                Preferences.Default.Set("himo_contacts_lookup_consent", true);
            }

            var allNumbers = _localContacts
                .SelectMany(contact => contact.NormalizedPhones)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (allNumbers.Length == 0)
            {
                _discoveryAttempted = true;
                _discoveryCompleted = true;
                await RebuildContactEntriesAsync();
                ContactsStatusLabel.Text = $"جهات الاتصال: {_localContacts.Count}";
                return;
            }

            SetContactLoading(true, "جارٍ عرض حسابات Himo...");

            // The API accepts up to 250 numbers per request. Keep batches below that limit.
            foreach (var batch in allNumbers.Chunk(200))
            {
                var matches = await _api.FindUsersByPhoneNumbersAsync(batch);
                foreach (var match in matches)
                {
                    var canonicalPhone = NormalizePhoneForMatching(match.MatchedPhoneNumber, _phoneRegionIso);
                    if (!string.IsNullOrWhiteSpace(canonicalPhone))
                        _matchedByPhone[canonicalPhone] = match;
                }
            }

            // The signed-in user takes priority if their number exists in the local address book.
            AddOwnPhoneToMatchMap();
            _discoveryCompleted = true;
            _discoveryAttempted = true;
            await RebuildContactEntriesAsync();

            var registeredCount = _allEntries.Count(contact => contact.HasHimoAccount);
            var inviteCount = _allEntries.Count - registeredCount;
            ContactsStatusLabel.Text = registeredCount > 0
                ? $"على Himo: {registeredCount}   ·   للدعوة: {inviteCount}"
                : $"لم يظهر أي حساب Himo ضمن جهات اتصالك · يمكنك دعوة {inviteCount} شخصًا.";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Contacts] Registered-contact discovery failed: {ex}");
            await RebuildContactEntriesAsync(); // Keep self and previous chats visible if phone lookup fails.
            ContactsStatusLabel.Text = "تعذر تحديث مطابقة الأرقام. ما زالت حساباتك ومحادثاتك السابقة متاحة.";
        }
        finally
        {
            _discoveringContacts = false;
            ContactsLoadIndicator.IsVisible = false;
            ContactsLoadIndicator.IsRunning = false;
        }
    }

    private async Task LoadSignedInProfileAsync()
    {
        await _api.TokenInitialization;
        if (!_api.HasToken) return;
        try
        {
            _myProfile ??= await _api.GetMyProfileAsync();
            AddOwnPhoneToMatchMap();
        }
        catch (Exception profileException)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Contacts] Own profile lookup failed: {profileException.Message}");
        }

        // Load existing one-to-one accounts even when their numbers are not present
        // in the phone book; this is the user's own conversation history.
        await _home.RefreshFromServerAsync();
    }

    private void AddOwnPhoneToMatchMap()
    {
        if (_myProfile is null || _myProfile.UserId == Guid.Empty) return;
        var ownPhone = NormalizePhoneForMatching(_myProfile.ContactPhoneNumber, _phoneRegionIso);
        if (string.IsNullOrWhiteSpace(ownPhone)) return;

        _matchedByPhone[ownPhone] = new ContactMatchDto(_myProfile.UserId, _myProfile.Name)
        {
            MatchedPhoneNumber = "+" + ownPhone,
            HasProfilePhoto = _myProfile.HasProfilePhoto == true,
            ProfilePhotoVersion = _myProfile.ProfilePhotoVersion
        };
    }

    private void SetContactLoading(bool isLoading, string status)
    {
        ContactsLoadIndicator.IsVisible = isLoading;
        ContactsLoadIndicator.IsRunning = isLoading;
        if (!string.IsNullOrWhiteSpace(status))
            ContactsStatusLabel.Text = status;
    }

    private async void ContactFilterTextChanged(object? sender, TextChangedEventArgs e)
    {
        await ApplyContactFilterAsync();
    }

    private async Task ApplyContactFilterAsync(bool immediate = false)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        var cts = new CancellationTokenSource();
        _filterCts = cts;
        var token = cts.Token;
        var query = PhoneNumberUtils.NormalizeDigits(ContactFilterEntry.Text?.Trim() ?? string.Empty);
        var normalizedPhoneQuery = NormalizePhoneForMatching(query, _phoneRegionIso);

        try
        {
            if (!immediate)
                await Task.Delay(180, token);

            var snapshot = _allEntries.ToArray();
            var matching = await Task.Run(
                () => snapshot.Where(contact => contact.Matches(query, normalizedPhoneQuery)).ToList(), token);
            token.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (token.IsCancellationRequested || !ReferenceEquals(_filterCts, cts)) return;
                _visibleContacts = matching;
                ContactsList.ItemsSource = matching;

                if (_contactsLoaded)
                {
                    var total = _allEntries.Count;
                    ContactsStatusLabel.Text = string.IsNullOrWhiteSpace(query)
                        ? _discoveryCompleted
                            ? $"على Himo: {matching.Count(contact => contact.HasHimoAccount)}   ·   جهات الاتصال: {total}"
                            : $"جهات الاتصال: {total}"
                        : $"النتائج: {matching.Count} من {total}";
                }
            });
        }
        catch (OperationCanceledException)
        {
            // The newest query supersedes the previous filtering operation.
        }
    }

    private async Task RebuildContactEntriesAsync()
    {
        var locals = _localContacts.ToArray();
        var matchedByPhone = new Dictionary<string, ContactMatchDto>(_matchedByPhone, StringComparer.Ordinal);
        var profile = _myProfile;
        var conversations = _home.Conversations
            .Where(item => !item.IsGroup && item.OtherParticipantUserId.HasValue)
            .GroupBy(item => item.OtherParticipantUserId!.Value)
            .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
            .ToArray();

        var entries = await Task.Run(() =>
        {
            var result = new List<DeviceContactEntry>(locals.Length + conversations.Length + 1);
            var conversationByUser = conversations.ToDictionary(item => item.OtherParticipantUserId!.Value);
            var ownId = profile?.UserId ?? Guid.Empty;
            var ownPhone = profile is null ? string.Empty : NormalizePhoneForMatching(profile.ContactPhoneNumber, _phoneRegionIso);

            foreach (var local in locals)
            {
                ContactMatchDto? match = null;

                // Prefer the user's own number over another match so the local address-book
                // name is retained and can be marked with (أنا).
                if (!string.IsNullOrWhiteSpace(ownPhone) && local.NormalizedPhones.Contains(ownPhone, StringComparer.Ordinal))
                    matchedByPhone.TryGetValue(ownPhone, out match);

                if (match is null)
                {
                    foreach (var normalizedPhone in local.NormalizedPhones)
                    {
                        if (matchedByPhone.TryGetValue(normalizedPhone, out match))
                            break;
                    }
                }

                var isSelf = ownId != Guid.Empty && match?.Id == ownId;
                conversationByUser.TryGetValue(match?.Id ?? Guid.Empty, out var existingConversation);
                var photoPath = existingConversation?.ProfilePhotoPath;
                if (string.IsNullOrWhiteSpace(photoPath) && match is not null && match.HasProfilePhoto)
                    photoPath = ProfilePhotoCache.Find(match.Id, match.ProfilePhotoVersion);

                result.Add(new DeviceContactEntry(
                    local.Name,
                    string.Join(" • ", local.PhoneNumbers),
                    string.Join(" • ", local.EmailAddresses),
                    local.NormalizedPhones,
                    match?.Id,
                    match?.Name,
                    _discoveryCompleted,
                    isSelf: isSelf,
                    isExistingConversation: existingConversation is not null,
                    hasRemoteProfilePhoto: match?.HasProfilePhoto == true || existingConversation?.HasRemoteProfilePhoto == true,
                    profilePhotoVersion: match?.ProfilePhotoVersion ?? existingConversation?.ProfilePhotoVersion ?? 0,
                    profilePhotoPath: photoPath));
            }

            var representedIds = result
                .Where(entry => entry.HimoUserId.HasValue)
                .Select(entry => entry.HimoUserId!.Value)
                .ToHashSet();

            // Always include the current account, even if its own number is not saved in the phone book.
            if (profile is not null && ownId != Guid.Empty && !representedIds.Contains(ownId))
            {
                var ownPhones = string.IsNullOrWhiteSpace(ownPhone) ? Array.Empty<string>() : new[] { ownPhone };
                result.Add(new DeviceContactEntry(
                    profile.Name,
                    profile.ContactPhoneNumber ?? string.Empty,
                    string.Empty,
                    ownPhones,
                    ownId,
                    profile.Name,
                    matchingCompleted: true,
                    isSelf: true,
                    hasRemoteProfilePhoto: profile.HasProfilePhoto == true,
                    profilePhotoVersion: profile.ProfilePhotoVersion,
                    profilePhotoPath: profile.HasProfilePhoto == true
                        ? ProfilePhotoCache.Find(ownId, profile.ProfilePhotoVersion)
                        : null));
                representedIds.Add(ownId);
            }

            // Also include one-to-one accounts the user already chats with, even when
            // their numbers are absent from the phone book or phone discovery is disabled.
            foreach (var conversation in conversations)
            {
                if (conversation.OtherParticipantUserId is not Guid userId || userId == ownId || representedIds.Contains(userId))
                    continue;

                result.Add(new DeviceContactEntry(
                    conversation.Name,
                    string.Empty,
                    string.Empty,
                    Array.Empty<string>(),
                    userId,
                    conversation.Name,
                    matchingCompleted: true,
                    isExistingConversation: true,
                    hasRemoteProfilePhoto: conversation.HasRemoteProfilePhoto,
                    profilePhotoVersion: conversation.ProfilePhotoVersion,
                    profilePhotoPath: conversation.ProfilePhotoPath));
                representedIds.Add(userId);
            }

            return result
                .OrderByDescending(contact => contact.IsSelf)
                .ThenByDescending(contact => contact.HasHimoAccount)
                .ThenBy(contact => contact.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        });

        _allEntries = entries;
        await ApplyContactFilterAsync(immediate: true);
        _ = LoadContactPhotosAsync(entries);
    }

    private async Task LoadContactPhotosAsync(IReadOnlyList<DeviceContactEntry> entries)
    {
        using var gate = new SemaphoreSlim(3, 3);
        var work = entries
            .Where(entry => entry.HimoUserId.HasValue && entry.HasRemoteProfilePhoto && !entry.HasProfilePhotoPath)
            .Select(async entry =>
            {
                await gate.WaitAsync();
                try
                {
                    var path = await ProfilePhotoCache.GetPathAsync(
                        _api,
                        entry.HimoUserId!.Value,
                        entry.HasRemoteProfilePhoto,
                        entry.ProfilePhotoVersion);
                    if (!string.IsNullOrWhiteSpace(path))
                        await MainThread.InvokeOnMainThreadAsync(() => entry.ProfilePhotoPath = path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Himo Contacts] Avatar load failed for {entry.HimoUserId}: {ex.Message}");
                }
                finally
                {
                    gate.Release();
                }
            });
        await Task.WhenAll(work);
    }

    private async void ContactActionClicked(object? sender, EventArgs e)
    {
        if (_handlingContactAction || sender is not Button button || button.BindingContext is not DeviceContactEntry contact)
            return;

        if (!contact.CanPerformAction)
            return;

        _handlingContactAction = true;
        var originalText = button.Text;
        button.IsEnabled = false;
        button.Text = "جارٍ...";
        try
        {
            if (contact.HasHimoAccount && contact.HimoUserId.HasValue)
            {
                var userName = !string.IsNullOrWhiteSpace(contact.HimoUserName)
                    ? contact.HimoUserName!
                    : contact.Name;
                var conversation = await _home.CreateConversationWithUserAsync(userName, contact.HimoUserId.Value);
                var routeId = !string.IsNullOrWhiteSpace(conversation.RemoteId)
                    ? conversation.RemoteId
                    : conversation.Id.ToString();
                if (Shell.Current is not null)
                    await SwipePageTransition.NavigateAsync(
                        this, $"chat?id={Uri.EscapeDataString(routeId)}", SwipeDirection.Left);
            }
            else
            {
                var phone = contact.PhoneNumbersDisplay
                    .Split('•', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(phone))
                {
                    await DisplayAlertAsync("دعوة", "لا يوجد رقم هاتف لهذه الجهة.", "حسنًا");
                    return;
                }

                var body = "أدعوك للانضمام إلى Himo للتواصل والمحادثة.";
                await Sms.Default.ComposeAsync(new SmsMessage(body, new[] { phone }));
            }
        }
        catch (FeatureNotSupportedException)
        {
            try
            {
                await Share.Default.RequestAsync(new ShareTextRequest
                {
                    Title = "دعوة إلى Himo",
                    Text = "أدعوك للانضمام إلى Himo للتواصل والمحادثة."
                });
            }
            catch (Exception shareException)
            {
                await DisplayAlertAsync("دعوة", shareException.Message, "حسنًا");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync(contact.HasHimoAccount ? "المحادثة" : "دعوة", ex.Message, "حسنًا");
        }
        finally
        {
            _handlingContactAction = false;
            button.Text = originalText;
            button.IsEnabled = contact.CanPerformAction;
        }
    }

    private async void OpenQrScannerClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await SwipePageTransition.NavigateAsync(this, "qr-scanner", SwipeDirection.Left);
    }

    private async void BackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await SwipePageTransition.GoBackAsync(this, SwipeDirection.Right);
    }

    private static string NormalizePhoneForMatching(string? value, string regionIso)
    {
        var input = PhoneNumberUtils.NormalizeDigits(value?.Trim());
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // Convert international IDD prefix to '+' before E.164 parsing.
        if (input.StartsWith("00", StringComparison.Ordinal))
            input = "+" + input[2..];

#if ANDROID
        if (regionIso.Length == 2)
        {
            try
            {
                var e164 = global::Android.Telephony.PhoneNumberUtils.FormatNumberToE164(input, regionIso);
                if (!string.IsNullOrWhiteSpace(e164))
                {
                    var e164Digits = PhoneNumberUtils.Normalize(e164);
                    if (e164Digits.Length is >= 7 and <= 15)
                        return e164Digits;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Contacts] E.164 parsing failed: {ex.Message}");
            }
        }
#endif

        // An explicit international number is still searchable if Android cannot validate it.
        var digits = PhoneNumberUtils.Normalize(input);
        return digits.Length is >= 7 and <= 15 ? digits : string.Empty;
    }

    private static string GetPhoneRegionIso()
    {
#if ANDROID
        try
        {
            var context = global::Android.App.Application.Context;
            var telephony = context.GetSystemService(global::Android.Content.Context.TelephonyService)
                as global::Android.Telephony.TelephonyManager;

            // The SIM region is the best default for local address-book numbers,
            // including when the device is roaming in another country.
            try
            {
                var simIso = telephony?.SimCountryIso;
                if (!string.IsNullOrWhiteSpace(simIso) && simIso.Length == 2)
                    return simIso.ToUpperInvariant();
            }
            catch { }

            try
            {
                var networkIso = telephony?.NetworkCountryIso;
                if (!string.IsNullOrWhiteSpace(networkIso) && networkIso.Length == 2)
                    return networkIso.ToUpperInvariant();
            }
            catch { }

            try
            {
                var localeIso = global::Java.Util.Locale.Default?.Country;
                if (!string.IsNullOrWhiteSpace(localeIso) && localeIso.Length == 2)
                    return localeIso.ToUpperInvariant();
            }
            catch { }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Contacts] Region lookup failed: {ex.Message}");
        }
#endif
        try
        {
            var region = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
            return region?.Length == 2 ? region.ToUpperInvariant() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record LocalContactSnapshot(
        string Name,
        string[] PhoneNumbers,
        string[] EmailAddresses,
        string[] NormalizedPhones);
}
