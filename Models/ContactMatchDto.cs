namespace Himo.Models;

/// <summary>A Himo account found through the combined people search or phone discovery.</summary>
public sealed record ContactMatchDto(Guid Id, string Name)
{
    /// <summary>The caller-submitted phone number that caused this opt-in discovery match, if any.</summary>
    public string? MatchedPhoneNumber { get; init; }

    /// <summary>Email returned by email search; never filled from phone discovery.</summary>
    public string? Email { get; init; }

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
        : "حساب Himo متاح للبحث";
}
