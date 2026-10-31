using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Himo.Models;

/// <summary>Phone-book contact or a Himo account already known to this user.</summary>
public sealed class DeviceContactEntry : INotifyPropertyChanged
{
    private string? _profilePhotoPath;

    public DeviceContactEntry(
        string name,
        string phoneNumbersDisplay,
        string emailAddressesDisplay,
        string[] normalizedPhoneNumbers,
        Guid? himoUserId = null,
        string? himoUserName = null,
        bool matchingCompleted = false,
        bool isSelf = false,
        bool isExistingConversation = false,
        bool hasRemoteProfilePhoto = false,
        long profilePhotoVersion = 0,
        string? profilePhotoPath = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "جهة اتصال بدون اسم" : name.Trim();
        PhoneNumbersDisplay = phoneNumbersDisplay ?? string.Empty;
        EmailAddressesDisplay = emailAddressesDisplay ?? string.Empty;
        NormalizedPhoneNumbers = normalizedPhoneNumbers ?? Array.Empty<string>();
        HimoUserId = himoUserId;
        HimoUserName = himoUserName;
        MatchingCompleted = matchingCompleted;
        IsSelf = isSelf;
        IsExistingConversation = isExistingConversation;
        HasRemoteProfilePhoto = hasRemoteProfilePhoto;
        ProfilePhotoVersion = Math.Max(0, profilePhotoVersion);
        _profilePhotoPath = profilePhotoPath;
    }

    public string Name { get; }
    public string PhoneNumbersDisplay { get; }
    public string EmailAddressesDisplay { get; }
    public string[] NormalizedPhoneNumbers { get; }
    public Guid? HimoUserId { get; }
    public string? HimoUserName { get; }
    public bool MatchingCompleted { get; }
    public bool IsSelf { get; }
    public bool IsExistingConversation { get; }
    public bool HasRemoteProfilePhoto { get; }
    public long ProfilePhotoVersion { get; }

    public string? ProfilePhotoPath
    {
        get => _profilePhotoPath;
        set
        {
            if (string.Equals(_profilePhotoPath, value, StringComparison.Ordinal)) return;
            _profilePhotoPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasProfilePhotoPath));
            OnPropertyChanged(nameof(ShowInitial));
        }
    }

    public bool HasProfilePhotoPath =>
        !string.IsNullOrWhiteSpace(ProfilePhotoPath) && File.Exists(ProfilePhotoPath);
    public bool ShowInitial => !HasProfilePhotoPath;
    public bool HasHimoAccount => HimoUserId.HasValue;
    public bool CanPerformAction => HasHimoAccount || NormalizedPhoneNumbers.Length > 0
        || !string.IsNullOrWhiteSpace(PhoneNumbersDisplay);

    // Keep the name from the address book for the user's own number, then mark it clearly.
    public string DisplayName
    {
        get
        {
            var baseName = IsSelf
                ? Name
                : HasHimoAccount && !string.IsNullOrWhiteSpace(HimoUserName)
                    ? HimoUserName.Trim()
                    : Name;
            return IsSelf ? $"{baseName} (أنا)" : baseName;
        }
    }

    public string Initial => string.IsNullOrWhiteSpace(IsSelf ? Name : DisplayName)
        ? "؟"
        : (IsSelf ? Name : DisplayName)[..1];

    public string AccountStatus => IsSelf
        ? "حسابك الشخصي"
        : IsExistingConversation
            ? "محادثة سابقة"
            : HasHimoAccount
                ? "مسجل في Himo"
                : string.Empty;

    public string ActionText => HasHimoAccount ? "محادثة" : "دعوة";

    public bool Matches(string query, string? normalizedPhoneQuery = null)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;

        if (DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || (HimoUserName?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || EmailAddressesDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        var queryDigits = Himo.Services.PhoneNumberUtils.Normalize(query);
        if (queryDigits.Length >= 2
            && Himo.Services.PhoneNumberUtils.Normalize(PhoneNumbersDisplay)
                .Contains(queryDigits, StringComparison.Ordinal))
            return true;

        if (!string.IsNullOrWhiteSpace(normalizedPhoneQuery))
        {
            foreach (var number in NormalizedPhoneNumbers)
            {
                if (number.StartsWith(normalizedPhoneQuery, StringComparison.Ordinal)
                    || normalizedPhoneQuery.StartsWith(number, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
