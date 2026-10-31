using System;
using System.Collections.Generic;

// Shared group API contracts used by MongoStore and Program.cs.
// Keep these in the global namespace to match the existing API source files.
record GroupMemberDto(
    Guid Id,
    string Name,
    bool IsAdmin,
    bool HasProfilePhoto,
    long ProfilePhotoVersion,
    DateTimeOffset JoinedAt,
    string Status = "",
    string Username = "");

record GroupDetailsDto(
    Guid Id,
    string Name,
    string Description,
    string InviteCode,
    bool HasPhoto,
    long PhotoVersion,
    bool CanManage,
    bool CanShareInviteLink,
    int DisappearingSeconds,
    IReadOnlyList<GroupMemberDto> Members);
