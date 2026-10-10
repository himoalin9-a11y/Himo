using System.Collections.ObjectModel;
using System.Windows.Input;
using Himo.Models;
using Himo.Services;
using Microsoft.Maui.ApplicationModel;

namespace Himo.ViewModels;

public sealed class HomeViewModel
{
    private readonly ChatService _chat;
    private readonly HimoApiClient _api;
    public ObservableCollection<Conversation> Conversations => _chat.Conversations;
    public ObservableCollection<Conversation> FilteredConversations { get; } = new();
    public string SearchText { get; set; } = "";
    public ICommand SearchCommand { get; }
    public bool HasServerSession => _api.HasToken;

    public HomeViewModel(ChatService chat, HimoApiClient api)
    {
        _chat = chat; _api = api; SearchCommand = new Command(Filter); Filter();
    }

    public async Task<bool> RefreshFromServerAsync()
    {
        if (!_api.HasToken) return false;
        try
        {
            var remote = await _api.GetConversationsAsync();
            var existingByRemoteId = Conversations
                .Where(x => !string.IsNullOrWhiteSpace(x.RemoteId))
                .GroupBy(x => x.RemoteId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);
            var nextLocalId = Conversations.Count == 0 ? 1 : Conversations.Max(x => x.Id) + 1;

            var mapped = remote.Select(x =>
            {
                var remoteId = x.Id.ToString();
                var existing = existingByRemoteId.TryGetValue(remoteId, out var current) ? current : null;
                return new Conversation
                {
                    // Keep the local ID stable across refreshes so cached messages remain
                    // attached to the same conversation even when server ordering changes.
                    Id = existing?.Id ?? nextLocalId++,
                    RemoteId = remoteId,
                    Name = x.Name,
                    Initial = GetInitial(x.Name),
                    OtherParticipantUserId = x.OtherParticipantUserId,
                    HasRemoteProfilePhoto = x.HasProfilePhoto,
                    ProfilePhotoVersion = x.ProfilePhotoVersion,
                    ProfilePhotoPath = x.OtherParticipantUserId is Guid photoUserId && x.HasProfilePhoto
                        ? ProfilePhotoCache.Find(photoUserId, x.ProfilePhotoVersion)
                        : null,
                    LastMessage = x.LastMessage,
                    Time = x.UpdatedAt.LocalDateTime.ToString("HH:mm"),
                    UpdatedAt = x.UpdatedAt.LocalDateTime,
                    UnreadCount = x.UnreadCount
                };
            }).ToList();
            _chat.ReplaceConversations(mapped);
            Filter();
            // Load missing avatars after the conversation list is already rendered.
            // Do not make opening the home screen wait for photo downloads.
            _ = LoadConversationPhotosAsync(mapped);
            return true;
        }
        catch { /* Keep local conversations available when the server is unavailable. */
            return false;
        }
    }

    public async Task<Conversation> CreateConversationWithUserAsync(string name, Guid userId)
    {
        if (!_api.HasToken)
            throw new InvalidOperationException("يجب تسجيل الدخول قبل بدء محادثة مع مستخدم.");

        var remote = await _api.CreateConversationAsync(name, userId);
        return UpsertRemoteConversation(remote);
    }

    public async Task<Conversation> CreateConversationAsync(string name)
    {
        if (!_api.HasToken)
            throw new InvalidOperationException("يجب تسجيل الدخول قبل بدء محادثة.");

        // The current server model creates real conversations through a selected
        // participant. Do not silently create a local-only conversation that
        // cannot exchange messages with another user.
        throw new InvalidOperationException("اختر مستخدمًا من البحث لبدء محادثة.");
    }

    private Conversation UpsertRemoteConversation(HimoApiClient.ConversationDto remote)
    {
        var remoteId = remote.Id.ToString();
        var local = Conversations.FirstOrDefault(x =>
            string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase));

        if (local is null)
        {
            local = _chat.AddConversation(remote.Name);
            local.RemoteId = remoteId;
        }

        local.OtherParticipantUserId = remote.OtherParticipantUserId;
        local.HasRemoteProfilePhoto = remote.HasProfilePhoto;
        local.ProfilePhotoVersion = remote.ProfilePhotoVersion;
        local.ProfilePhotoPath = remote.OtherParticipantUserId is Guid photoUserId && remote.HasProfilePhoto
            ? ProfilePhotoCache.Find(photoUserId, remote.ProfilePhotoVersion)
            : null;
        local.LastMessage = remote.LastMessage;
        local.Time = remote.UpdatedAt.LocalDateTime.ToString("HH:mm");
        _ = LoadConversationPhotosAsync(new[] { local });
        local.UpdatedAt = remote.UpdatedAt.LocalDateTime;
        local.UnreadCount = remote.UnreadCount;
        Filter();
        return local;
    }

    private async Task LoadConversationPhotosAsync(IEnumerable<Conversation> conversations)
    {
        foreach (var conversation in conversations)
        {
            if (conversation.OtherParticipantUserId is not Guid userId)
                continue; // Group conversations use their own initials until group photos are supported.

            var revision = conversation.ProfilePhotoVersion;
            var hasPhoto = conversation.HasRemoteProfilePhoto;
            try
            {
                var path = await ProfilePhotoCache.GetPathAsync(_api, userId, hasPhoto, revision);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    // Do not apply an old download if a refresh has already supplied
                    // a newer avatar version for this conversation.
                    if (conversation.OtherParticipantUserId == userId &&
                        conversation.ProfilePhotoVersion == revision &&
                        conversation.HasRemoteProfilePhoto == hasPhoto)
                    {
                        conversation.ProfilePhotoPath = path;
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo] Profile photo load failed for {userId}: {ex.Message}");
            }
        }
    }

    private static string GetInitial(string? name)
    {
        var value = name?.Trim() ?? string.Empty;
        return value.Length == 0 ? "H" : value[0].ToString();
    }

    public void Filter()
    {
        FilteredConversations.Clear();
        var term = SearchText?.Trim() ?? "";
        var archived = Preferences.Default.Get("himo_archived_conversations", string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var item in Conversations.OrderByDescending(x => x.UpdatedAt)
                     .Where(x => string.IsNullOrWhiteSpace(x.RemoteId) || !archived.Contains(x.RemoteId, StringComparer.OrdinalIgnoreCase))
                     .Where(x => string.IsNullOrWhiteSpace(term) || x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || x.LastMessage.Contains(term, StringComparison.OrdinalIgnoreCase)))
            FilteredConversations.Add(item);
    }
}
