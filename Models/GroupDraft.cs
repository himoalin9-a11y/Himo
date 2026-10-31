using System.Collections.ObjectModel;

namespace Himo.Models;

public sealed class GroupDraft
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DisappearingSeconds { get; set; }
    public bool AllMembersCanAddMembers { get; set; }
    public bool AllMembersCanShareInviteLink { get; set; }
    public bool RequireAdminApproval { get; set; }
    public bool AdminsOnlyMessages { get; set; }
    public ObservableCollection<GroupMemberDraft> Members { get; } = new();
}

public sealed class GroupMemberDraft
{
    public Guid UserId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public bool HasProfilePhoto { get; set; }
    public long ProfilePhotoVersion { get; set; }
    public string? PhotoPath { get; set; }
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "؟" : Name.Trim()[0].ToString().ToUpperInvariant();
}
