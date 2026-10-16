using System.Collections.ObjectModel;
using Himo.Models;
using Himo.Services;
using Himo.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Storage;

namespace Himo.Views;

/// <summary>
/// Displays the full local address book. Browsing and filtering stay on-device;
/// only phone numbers are sent to the API after the user explicitly consents to matching.
/// </summary>
public partial class SearchPage : ContentPage
{
    private readonly HimoApiClient _api;
    private readonly HomeViewModel _home;
    private readonly ObservableCollection<DeviceContactEntry> _visibleContacts = new();
    private readonly List<LocalContactSnapshot> _localContacts = new();
    private readonly List<DeviceContactEntry> _allEntries = new();
    private readonly Dictionary<string, ContactMatchDto> _matchedByPhone = new(StringComparer.Ordinal);
    private bool _contactsLoaded;
    private bool _loadingContacts;
    private bool _matchingContacts;
    private bool _matchingCompleted;
    private bool _openingConversation;

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
        if (!_contactsLoaded && !_loadingContacts)
            _ = LoadContactsAsync();
    }

    private async Task LoadContactsAsync()
    {
        if (_loadingContacts) return;
#if ANDROID
        _loadingContacts = true;
        SetContactLoading(true, "جارٍ طلب إذن جهات الاتصال...");
        try
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.ContactsRead>();
            if (permission != PermissionStatus.Granted)
                permission = await Permissions.RequestAsync<Permissions.ContactsRead>();

            if (permission != PermissionStatus.Granted)
            {
                ContactsStatusLabel.Text = "لم يتم منح إذن جهات الاتصال. يمكنك تفعيله من إعدادات Android ثم فتح هذه الصفحة مجددًا.";
                return;
            }

            var contacts = await Contacts.Default.GetAllAsync();
            if (contacts is null)
            {
                ContactsStatusLabel.Text = "تعذر قراءة جهات الاتصال من الهاتف.";
                return;
            }

            _localContacts.Clear();
            foreach (var contact in contacts)
            {
                if (contact is null) continue;

                var phoneNumbers = contact.Phones?
                    .Select(phone => phone?.PhoneNumber?.Trim() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray() ?? Array.Empty<string>();

                var emailAddresses = contact.Emails?
                    .Select(email => email?.EmailAddress?.Trim() ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray() ?? Array.Empty<string>();

                var displayName = contact.DisplayName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = phoneNumbers.FirstOrDefault() ?? emailAddresses.FirstOrDefault() ?? "جهة اتصال بدون اسم";

                // Do not drop contacts that only have a name or email address.
                var normalizedPhones = phoneNumbers
                    .Select(NormalizePhoneForMatching)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                _localContacts.Add(new LocalContactSnapshot(displayName, phoneNumbers, emailAddresses, normalizedPhones));
            }

            _localContacts.Sort((left, right) => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name));
            _contactsLoaded = true;
            RebuildContactEntries();
            ContactsStatusLabel.Text = _localContacts.Count == 1
                ? "تم تحميل جهة اتصال واحدة من الهاتف. القائمة محلية ولا تُرفع للخادم."
                : $"تم تحميل {_localContacts.Count} جهة اتصال من الهاتف. القائمة محلية ولا تُرفع للخادم.";
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

    private void SetContactLoading(bool isLoading, string status)
    {
        ContactsLoadIndicator.IsVisible = isLoading;
        ContactsLoadIndicator.IsRunning = isLoading;
        if (!string.IsNullOrWhiteSpace(status))
            ContactsStatusLabel.Text = status;
    }

    private void ContactFilterTextChanged(object? sender, TextChangedEventArgs e) => ApplyContactFilter();

    private void ApplyContactFilter()
    {
        var query = ContactFilterEntry.Text?.Trim() ?? string.Empty;
        _visibleContacts.Clear();
        foreach (var contact in _allEntries)
        {
            if (contact.Matches(query))
                _visibleContacts.Add(contact);
        }

        if (_contactsLoaded)
        {
            var total = _localContacts.Count;
            ContactsStatusLabel.Text = string.IsNullOrWhiteSpace(query)
                ? $"جهات الاتصال: {total}"
                : $"نتائج التصفية: {_visibleContacts.Count} من {total}";
        }
    }

    private void RebuildContactEntries()
    {
        _allEntries.Clear();
        foreach (var local in _localContacts)
        {
            ContactMatchDto? match = null;
            foreach (var normalizedPhone in local.NormalizedPhones)
            {
                if (_matchedByPhone.TryGetValue(normalizedPhone, out match))
                    break;
            }

            _allEntries.Add(new DeviceContactEntry(
                local.Name,
                string.Join(" • ", local.PhoneNumbers),
                string.Join(" • ", local.EmailAddresses),
                local.NormalizedPhones,
                match?.Id,
                match?.Name,
                _matchingCompleted));
        }
        ApplyContactFilter();
    }

    private async void MatchContactsClicked(object? sender, EventArgs e) => await MatchContactsAsync();

    private async Task MatchContactsAsync()
    {
        if (_matchingContacts || _loadingContacts) return;
        if (!_contactsLoaded)
        {
            await LoadContactsAsync();
            if (!_contactsLoaded) return;
        }

        await _api.TokenInitialization;
        if (!_api.HasToken)
        {
            await DisplayAlertAsync("جهات الاتصال", "سجّل الدخول أولًا للبحث عن حسابات Himo.", "حسنًا");
            return;
        }

        if (!Preferences.Default.Get("himo_contacts_lookup_consent", false))
        {
            var consent = await DisplayAlertAsync(
                "الخصوصية وجهات الاتصال",
                "للمطابقة، سيرسل Himo أرقام الهاتف فقط من جهات اتصالك عبر اتصال آمن. لن تُرسل الأسماء أو عناوين البريد، ولن يحفظ الخادم دفتر العناوين. ستظهر فقط الحسابات التي سمح أصحابها بالعثور عليهم.",
                "موافقة ومتابعة",
                "إلغاء");
            if (!consent) return;
            Preferences.Default.Set("himo_contacts_lookup_consent", true);
        }

        var allNumbers = _localContacts
            .SelectMany(contact => contact.NormalizedPhones)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (allNumbers.Length == 0)
        {
            await DisplayAlertAsync("جهات الاتصال", "لا توجد أرقام هاتف صالحة للمطابقة. ستظل بقية جهات الاتصال ظاهرة في القائمة.", "حسنًا");
            return;
        }

        _matchingContacts = true;
        MatchContactsButton.IsEnabled = false;
        MatchContactsButton.Text = "جارٍ المطابقة...";
        SetContactLoading(true, "جارٍ مطابقة أرقام الهاتف مع حسابات Himo...");
        try
        {
            _matchedByPhone.Clear();
            _matchingCompleted = false;
            var matchedUsers = new Dictionary<Guid, ContactMatchDto>();
            var batches = allNumbers.Chunk(200).ToArray();
            for (var index = 0; index < batches.Length; index++)
            {
                ContactsStatusLabel.Text = $"جارٍ مطابقة الأرقام ({index + 1}/{batches.Length})...";
                var matches = await _api.FindUsersByPhoneNumbersAsync(batches[index]);
                foreach (var match in matches)
                {
                    matchedUsers[match.Id] = match;
                    var matchPhone = NormalizePhoneForMatching(match.MatchedPhoneNumber);
                    if (!string.IsNullOrWhiteSpace(matchPhone))
                        _matchedByPhone[matchPhone] = match;
                }
            }

            var responseCanLinkContacts = matchedUsers.Count == 0
                || matchedUsers.Values.All(user => !string.IsNullOrWhiteSpace(user.MatchedPhoneNumber));
            _matchingCompleted = responseCanLinkContacts;
            RebuildContactEntries();
            var matchedContactCount = _allEntries.Count(contact => contact.HasHimoAccount);
            if (!responseCanLinkContacts || (matchedUsers.Count > 0 && matchedContactCount == 0))
            {
                _matchingCompleted = false;
                RebuildContactEntries();
                ContactsStatusLabel.Text = "عثر الخادم على حسابات، لكن لا يمكن ربطها بالأسماء بعد. انشر تحديث Himo.Api ثم أعد المطابقة.";
            }
            else
            {
                ContactsStatusLabel.Text = matchedContactCount == 0
                    ? $"لم نعثر على حسابات Himo مطابقة بين {_localContacts.Count} جهة اتصال. بقيت جميع جهات الاتصال ظاهرة."
                    : $"تمت مطابقة {matchedContactCount} جهة اتصال مع حسابات Himo. اضغط «محادثة» بجوار أي حساب لبدء محادثة.";
            }
        }
        catch (Exception ex)
        {
            _matchingCompleted = false;
            _matchedByPhone.Clear();
            RebuildContactEntries();
            ContactsStatusLabel.Text = "تعذرت مطابقة جهات الاتصال. تحقق من اتصال الخادم ثم حاول مجددًا.";
            System.Diagnostics.Debug.WriteLine($"[Himo Contacts] Matching failed: {ex}");
            await DisplayAlertAsync("مطابقة جهات الاتصال", ex.Message, "حسنًا");
        }
        finally
        {
            _matchingContacts = false;
            MatchContactsButton.IsEnabled = true;
            MatchContactsButton.Text = "مطابقة جهات الاتصال مع Himo";
            ContactsLoadIndicator.IsVisible = false;
            ContactsLoadIndicator.IsRunning = false;
        }
    }

    private async void ContactActionClicked(object? sender, EventArgs e)
    {
        if (_openingConversation || sender is not Button button || button.BindingContext is not DeviceContactEntry contact)
            return;
        if (!contact.HimoUserId.HasValue) return;

        _openingConversation = true;
        button.IsEnabled = false;
        var originalText = button.Text;
        button.Text = "جارٍ...";
        try
        {
            var userName = !string.IsNullOrWhiteSpace(contact.HimoUserName) ? contact.HimoUserName! : contact.Name;
            var conversation = await _home.CreateConversationWithUserAsync(userName, contact.HimoUserId.Value);
            var routeId = !string.IsNullOrWhiteSpace(conversation.RemoteId)
                ? conversation.RemoteId
                : conversation.Id.ToString();
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync($"chat?id={routeId}", false);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("المحادثة", ex.Message, "حسنًا");
        }
        finally
        {
            _openingConversation = false;
            button.Text = originalText;
            button.IsEnabled = contact.HasHimoAccount;
        }
    }

    private async void BackClicked(object sender, EventArgs e)
    {
        if (Shell.Current is not null)
            await Shell.Current.GoToAsync("..", false);
    }

    private static string NormalizePhoneForMatching(string? value)
    {
        var digits = PhoneNumberUtils.Normalize(value);
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];
        if (digits.Length == 10 && digits.StartsWith("05", StringComparison.Ordinal))
            digits = "966" + digits[1..];
        else if (digits.Length == 9 && digits.StartsWith("5", StringComparison.Ordinal))
            digits = "966" + digits;
        return digits.Length is >= 7 and <= 15 ? digits : string.Empty;
    }

    private sealed record LocalContactSnapshot(
        string Name,
        string[] PhoneNumbers,
        string[] EmailAddresses,
        string[] NormalizedPhones);
}
