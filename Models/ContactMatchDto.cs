namespace Himo.Models;

/// <summary>A Himo account found by matching one of the caller's phone contacts.</summary>
public sealed record ContactMatchDto(Guid Id, string Name)
{
    /// <summary>The caller-submitted phone number that caused this opt-in discovery match.</summary>
    public string? MatchedPhoneNumber { get; init; }

    /// <summary>Email returned by email search; never filled from phone discovery.</summary>
    public string? Email { get; init; }

    public bool HasProfilePhoto { get; init; }
    public long ProfilePhotoVersion { get; init; }

    public string Initial
    {
        get
        {
            var value = Name?.Trim() ?? string.Empty;
            return value.Length == 0 ? "H" : value[..1];
        }
    }

    public string DetailText => !string.IsNullOrWhiteSpace(Email)
        ? Email
        : !string.IsNullOrWhiteSpace(MatchedPhoneNumber)
            ? MatchedPhoneNumber
            : "حساب Himo متاح للبحث";
}
