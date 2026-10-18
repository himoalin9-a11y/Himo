namespace Himo.Models;

/// <summary>Full local-device contact shown in Himo's contacts screen. Personal details remain on-device.</summary>
public sealed class DeviceContactEntry
{
    public DeviceContactEntry(
        string name,
        string phoneNumbersDisplay,
        string emailAddressesDisplay,
        string[] normalizedPhoneNumbers,
        Guid? himoUserId = null,
        string? himoUserName = null,
        bool matchingCompleted = false)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "جهة اتصال بدون اسم" : name.Trim();
        PhoneNumbersDisplay = phoneNumbersDisplay ?? string.Empty;
        EmailAddressesDisplay = emailAddressesDisplay ?? string.Empty;
        NormalizedPhoneNumbers = normalizedPhoneNumbers ?? Array.Empty<string>();
        HimoUserId = himoUserId;
        HimoUserName = himoUserName;
        MatchingCompleted = matchingCompleted;
    }

    public string Name { get; }
    public string PhoneNumbersDisplay { get; }
    public string EmailAddressesDisplay { get; }
    public string[] NormalizedPhoneNumbers { get; }
    public Guid? HimoUserId { get; }
    public string? HimoUserName { get; }
    public bool MatchingCompleted { get; }
    public bool HasHimoAccount => HimoUserId.HasValue;
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "؟" : Name[..1];
    public string AccountStatus => HasHimoAccount
        ? "موجود على Himo"
        : MatchingCompleted ? "لم يتم العثور على حساب Himo" : "لم تتم المطابقة بعد";
    public string ActionText => HasHimoAccount
        ? "محادثة"
        : MatchingCompleted ? "غير موجود" : "مطابقة أولًا";

    public bool Matches(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || PhoneNumbersDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
            || EmailAddressesDisplay.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
