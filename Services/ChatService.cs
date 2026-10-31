using System.Collections.ObjectModel;
using System.Text.Json;
using Himo.Models;
using Microsoft.Maui.ApplicationModel;

namespace Himo.Services;

public sealed class ChatService
{
    private const string StoreFileName = "himo_chat_store.json";
    private readonly string _storePath;
    private readonly object _sync = new();
    private readonly Dictionary<int, ObservableCollection<ChatMessage>> _messages = new();
    private bool _loaded;
    private readonly Task<PreparedChatStore?> _storeLoadTask;

    public ObservableCollection<Conversation> Conversations { get; } = new();

    // Raised when realtime/push sync changes the local conversation preview or ordering.
    public event EventHandler? ConversationsChanged;
    // Raised when an incoming/outgoing message is added to the local cache.
    public event Action<int>? MessagesChanged;

    private void NotifyConversationsChanged()
    {
        void Notify()
        {
            try { ConversationsChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo ChatStore] Conversation change subscriber failed: {ex.Message}"); }
        }

        if (MainThread.IsMainThread) Notify();
        else MainThread.BeginInvokeOnMainThread(Notify);
    }

    private void NotifyMessagesChanged(int conversationId)
    {
        void Notify()
        {
            try { MessagesChanged?.Invoke(conversationId); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo ChatStore] Message change subscriber failed: {ex.Message}"); }
        }

        if (MainThread.IsMainThread) Notify();
        else MainThread.BeginInvokeOnMainThread(Notify);
    }

    public ChatService()
    {
        _storePath = Path.Combine(FileSystem.Current.AppDataDirectory, StoreFileName);
        // The chat cache can grow substantially. Read and deserialize it away from
        // Android's UI thread so resolving App/ChatService cannot trigger a cold-start ANR.
        // Deserialize and prepare the full cache on a worker. Chat histories can be
        // large; grouping thousands of messages on the UI thread caused startup stalls.
        _storeLoadTask = Task.Run(() => PrepareStoreSnapshot(ReadStoreSnapshot()));
    }

    /// <summary>
    /// Loads the local cache without blocking the UI thread and publishes it on the
    /// UI thread before the first conversation refresh. Repeated calls are harmless.
    /// </summary>
    public async Task InitializeAsync()
    {
        PreparedChatStore? snapshot;
        try
        {
            snapshot = await _storeLoadTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatStore] Background load failed: {ex.Message}");
            snapshot = null;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            lock (_sync)
                ApplyStoreSnapshot(snapshot);
        });
    }

    public ObservableCollection<ChatMessage> GetMessages(int conversationId)
    {
        EnsureLoaded();
        lock (_sync)
        {
            if (_messages.TryGetValue(conversationId, out var messages)) return messages;
            messages = new ObservableCollection<ChatMessage>();
            _messages[conversationId] = messages;
            return messages;
        }
    }

    public List<ChatMessage> GetRecentMessages(int conversationId, int count)
    {
        if (count <= 0)
            return new List<ChatMessage>();

        EnsureLoaded();
        lock (_sync)
        {

            if (!_messages.TryGetValue(conversationId, out var messages) || messages.Count == 0)
                return new List<ChatMessage>();

            // ChatService keeps each conversation chronologically ordered.
            // Take only the tail without sorting the full history.
            var start = Math.Max(0, messages.Count - count);
            var result = new List<ChatMessage>(messages.Count - start);

            for (var i = start; i < messages.Count; i++)
                result.Add(messages[i]);

            return result;
        }
    }

    public void ClearConversationMessages(int conversationId)
    {
        EnsureLoaded();
        lock (_sync)
        {
            if (_messages.TryGetValue(conversationId, out var messages)) messages.Clear();
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is not null)
            {
                conversation.LastMessage = string.Empty;
                conversation.UnreadCount = 0;
                conversation.Time = DateTime.Now.ToString("HH:mm");
                conversation.UpdatedAt = DateTime.Now;
            }
            Save();
        }
        NotifyMessagesChanged(conversationId);
        NotifyConversationsChanged();
    }

    public bool RemoveConversation(int conversationId)
    {
        EnsureLoaded();
        var removed = false;
        lock (_sync)
        {
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is null) return false;

            Conversations.Remove(conversation);
            _messages.Remove(conversationId);
            Save();
            removed = true;
        }

        if (removed) NotifyConversationsChanged();
        return removed;
    }

    public int PurgeExpiredMessages(int conversationId, DateTime? now = null)
    {
        EnsureLoaded();
        var cutoff = now ?? DateTime.Now;
        var expiredCount = 0;
        lock (_sync)
        {
            if (!_messages.TryGetValue(conversationId, out var messages)) return 0;
            var expired = messages.Where(m => m.ExpiresAt.HasValue && m.ExpiresAt.Value <= cutoff).ToList();
            if (expired.Count == 0) return 0;
            expiredCount = expired.Count;
            foreach (var message in expired) messages.Remove(message);
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is not null)
            {
                var latest = messages.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).FirstOrDefault();
                conversation.LastMessage = latest?.Text ?? string.Empty;
                conversation.Time = latest?.SentAt.ToString("HH:mm") ?? string.Empty;
                conversation.UpdatedAt = latest?.SentAt ?? cutoff;
            }
            Save();
        }
        NotifyMessagesChanged(conversationId);
        NotifyConversationsChanged();
        return expiredCount;
    }

    public void ReplaceConversations(IEnumerable<Conversation> items)
    {
        EnsureLoaded();
        var ordered = items.OrderByDescending(x => x.UpdatedAt).ToList();
        lock (_sync)
        {
            // Preserve Conversation object instances and only insert/move/remove the
            // rows that truly changed. Clearing this ObservableCollection on every
            // poll made CollectionView rebuild all native rows and caused visible lag.
            for (var targetIndex = 0; targetIndex < ordered.Count; targetIndex++)
            {
                var wanted = ordered[targetIndex];
                if (targetIndex < Conversations.Count &&
                    ReferenceEquals(Conversations[targetIndex], wanted))
                    continue;

                var existingIndex = Conversations.IndexOf(wanted);
                if (existingIndex >= 0)
                    Conversations.Move(existingIndex, targetIndex);
                else
                    Conversations.Insert(targetIndex, wanted);
            }

            while (Conversations.Count > ordered.Count)
                Conversations.RemoveAt(Conversations.Count - 1);

            // Drop cached messages that no longer belong to a server conversation.
            var activeIds = Conversations.Select(x => x.Id).ToHashSet();
            foreach (var staleId in _messages.Keys.Where(id => !activeIds.Contains(id)).ToList())
                _messages.Remove(staleId);

            Save();
        }
    }

    /// <summary>
    /// Guarantees that an incoming message always has a local conversation row,
    /// even when the app was opened directly from a push notification or the local
    /// cache has not been hydrated yet.
    /// Call on the UI thread because Conversations is observable.
    /// </summary>
    public Conversation EnsureRemoteConversation(Guid remoteId, string? fallbackName = null)
    {
        if (remoteId == Guid.Empty)
            throw new ArgumentException("معرّف المحادثة غير صالح.", nameof(remoteId));

        EnsureLoaded();
        Conversation result;
        var changed = false;
        lock (_sync)
        {
            var normalizedRemoteId = remoteId.ToString("D");
            var existing = Conversations.FirstOrDefault(x =>
                string.Equals(x.RemoteId, normalizedRemoteId, StringComparison.OrdinalIgnoreCase));
            var cleanFallback = fallbackName?.Trim() ?? string.Empty;

            if (existing is not null)
            {
                result = existing;
                if ((string.IsNullOrWhiteSpace(existing.Name) || string.Equals(existing.Name, "محادثة", StringComparison.Ordinal)) &&
                    !string.IsNullOrWhiteSpace(cleanFallback))
                {
                    existing.Name = cleanFallback;
                    existing.Initial = cleanFallback[..1];
                    Save();
                    changed = true;
                }
                if (!_messages.ContainsKey(existing.Id))
                    _messages[existing.Id] = new ObservableCollection<ChatMessage>();
            }
            else
            {
                var id = Conversations.Count == 0 ? 1 : Conversations.Max(x => x.Id) + 1;
                var name = string.IsNullOrWhiteSpace(cleanFallback) ? "محادثة" : cleanFallback;
                var now = DateTime.Now;
                result = new Conversation
                {
                    Id = id,
                    RemoteId = normalizedRemoteId,
                    Name = name,
                    Initial = name[..1],
                    LastMessage = "محادثة جديدة",
                    Time = now.ToString("HH:mm"),
                    UpdatedAt = now
                };
                Conversations.Insert(0, result);
                _messages[id] = new ObservableCollection<ChatMessage>();
                Save();
                changed = true;
            }
        }

        if (changed) NotifyConversationsChanged();
        return result;
    }

    /// <summary>Creates or refreshes a local conversation using the API's authoritative metadata.</summary>
    public Conversation UpsertRemoteConversation(HimoApiClient.ConversationDto remote, string? fallbackName = null)
    {
        if (remote is null || remote.Id == Guid.Empty)
            throw new ArgumentException("بيانات المحادثة غير صالحة.", nameof(remote));

        var fallback = !string.IsNullOrWhiteSpace(remote.Name) ? remote.Name : fallbackName;
        var conversation = EnsureRemoteConversation(remote.Id, fallback);
        lock (_sync)
        {
            conversation.RemoteId = remote.Id.ToString("D");
            if (!string.IsNullOrWhiteSpace(remote.Name))
            {
                conversation.Name = remote.Name;
                conversation.Initial = remote.Name.Trim()[..1];
            }
            if (!string.IsNullOrWhiteSpace(remote.LastMessage))
                conversation.LastMessage = remote.LastMessage;
            conversation.Time = remote.UpdatedAt.LocalDateTime.ToString("HH:mm");
            conversation.UpdatedAt = remote.UpdatedAt.LocalDateTime;
            conversation.UnreadCount = remote.UnreadCount;
            conversation.OtherParticipantUserId = remote.OtherParticipantUserId;
            conversation.HasRemoteProfilePhoto = remote.HasProfilePhoto;
            conversation.ProfilePhotoVersion = remote.ProfilePhotoVersion;
            conversation.IsArchived = remote.IsArchived;
            conversation.IsMuted = remote.IsMuted;
            conversation.IsBlocked = remote.IsBlocked;
            conversation.IsGroup = remote.IsGroup;
            _messages.TryAdd(conversation.Id, new ObservableCollection<ChatMessage>());
            Save();
        }

        NotifyConversationsChanged();
        return conversation;
    }

    public void ClearAll()
    {
        EnsureLoaded();
        lock (_sync)
        {
            Conversations.Clear();
            _messages.Clear();
            Save();
        }
    }

    public void MarkAsRead(int conversationId)
    {
        EnsureLoaded();
        lock (_sync)
        {
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is null || conversation.UnreadCount == 0) return;
            conversation.UnreadCount = 0;
            Save();
        }
        NotifyConversationsChanged();
    }

    public Conversation AddConversation(string name)
    {
        var clean = name.Trim();
        if (string.IsNullOrWhiteSpace(clean)) throw new ArgumentException("اسم المحادثة مطلوب.", nameof(name));
        EnsureLoaded();
        lock (_sync)
        {
            var id = Conversations.Count == 0 ? 1 : Conversations.Max(x => x.Id) + 1;
            var now = DateTime.Now;
            var c = new Conversation { Id = id, Name = clean, Initial = clean.Length > 0 ? clean[0].ToString() : "H", LastMessage = "محادثة جديدة", Time = now.ToString("HH:mm"), UpdatedAt = now };
            Conversations.Insert(0, c);
            _messages[id] = new ObservableCollection<ChatMessage>();
            Save();
            return c;
        }
    }

    public ChatMessage? Send(int conversationId, string text)
    {
        var clean = text.Trim();
        if (conversationId <= 0 || string.IsNullOrWhiteSpace(clean)) return null;
        if (clean.Length > 4000) return null;

        EnsureLoaded();
        lock (_sync)
        {
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is null) return null;

            var messages = GetMessages(conversationId);
            var next = messages.Count == 0 ? 1 : messages.Max(x => x.Id) + 1;
            var message = new ChatMessage
            {
                Id = next,
                ConversationId = conversationId,
                Text = clean,
                SentAt = DateTime.Now,
                IsMine = true
            };

            InsertMessageChronological(messages, message);
            conversation.LastMessage = clean;
            conversation.Time = message.SentAt.ToString("HH:mm");
            conversation.UpdatedAt = message.SentAt;
            Save();
            return message;
        }
    }

    public ChatMessage? AddPendingAttachmentMessage(
        int conversationId,
        string fileName,
        string contentType,
        long? attachmentSize,
        string? localPath = null)
    {
        if (conversationId <= 0 || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentType))
            return null;

        EnsureLoaded();
        lock (_sync)
        {
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is null) return null;

            var messages = GetMessages(conversationId);
            var next = messages.Count == 0 ? 1 : messages.Max(x => x.Id) + 1;
            var now = DateTime.Now;
            var message = new ChatMessage
            {
                Id = next,
                ConversationId = conversationId,
                Text = string.Empty,
                SentAt = now,
                IsMine = true,
                IsPending = true,
                DeliveryStatus = "sending",
                ClientMessageId = Guid.NewGuid().ToString("D"),
                AttachmentFileName = fileName,
                AttachmentContentType = contentType,
                AttachmentSize = attachmentSize,
                AttachmentLocalPath = localPath
            };

            InsertMessageChronological(messages, message);
            conversation.LastMessage = message.IsAudioAttachment
                ? "رسالة صوتية"
                : message.IsImageAttachment
                    ? "صورة"
                    : contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                        ? "فيديو"
                        : fileName;
            conversation.Time = now.ToString("HH:mm");
            conversation.UpdatedAt = now;
            Save();
            return message;
        }
    }

    public void CompletePendingAttachmentMessage(ChatMessage pending, string remoteId, DateTime sentAt)
    {
        if (pending is null || string.IsNullOrWhiteSpace(remoteId)) return;

        EnsureLoaded();
        lock (_sync)
        {
            pending.RemoteId = remoteId;
            pending.IsPending = false;
            SetDeliveryStatusIfNewer(pending, "sent");
            pending.ClientMessageId = null;
            Save();
        }
    }

    public ChatMessage? AddPendingMessage(int conversationId, string text, string clientMessageId, string? replyToRemoteId = null, string? replyToText = null, bool isEdited = false, string? editedAt = null)
    {
        var clean = text.Trim();
        if (conversationId <= 0 || string.IsNullOrWhiteSpace(clean) || string.IsNullOrWhiteSpace(clientMessageId)) return null;

        EnsureLoaded();
        lock (_sync)
        {
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is null) return null;

            var messages = GetMessages(conversationId);
            var next = messages.Count == 0 ? 1 : messages.Max(x => x.Id) + 1;
            var now = DateTime.Now;
            var message = new ChatMessage
            {
                Id = next,
                ConversationId = conversationId,
                Text = clean,
                SentAt = now,
                IsMine = true,
                IsPending = true,
                DeliveryStatus = "sending",
                ClientMessageId = clientMessageId,
                ReplyToRemoteId = replyToRemoteId,
                ReplyToText = replyToText
            };

            InsertMessageChronological(messages, message);
            conversation.LastMessage = clean;
            conversation.Time = now.ToString("HH:mm");
            conversation.UpdatedAt = now;
            Save();
            return message;
        }
    }

    public void CompletePendingMessage(int conversationId, string clientMessageId, string remoteId, DateTime sentAt)
    {
        if (string.IsNullOrWhiteSpace(clientMessageId) || string.IsNullOrWhiteSpace(remoteId)) return;

        EnsureLoaded();
        lock (_sync)
        {
            var messages = GetMessages(conversationId);
            var pending = messages.FirstOrDefault(x => string.Equals(x.ClientMessageId, clientMessageId, StringComparison.OrdinalIgnoreCase));
            if (pending is null) return;

            pending.RemoteId = remoteId;
            pending.IsPending = false;
            SetDeliveryStatusIfNewer(pending, "sent");
            pending.ClientMessageId = null;
            // ChatMessage uses init-only values for text/time, so keep the optimistic
            // timestamp for a stable UI and only attach the authoritative server ID.
            Save();
        }
    }

    public void FailPendingMessage(int conversationId, string clientMessageId)
    {
        if (string.IsNullOrWhiteSpace(clientMessageId)) return;

        EnsureLoaded();
        lock (_sync)
        {
            var messages = GetMessages(conversationId);
            var pending = messages.FirstOrDefault(x => string.Equals(x.ClientMessageId, clientMessageId, StringComparison.OrdinalIgnoreCase));
            if (pending is null) return;
            pending.IsPending = true;
            pending.DeliveryStatus = "failed";
            Save();
        }
    }

    public IReadOnlyList<ChatMessage> GetPendingMessages()
    {
        EnsureLoaded();
        lock (_sync)
        {
            return _messages.Values
                .SelectMany(x => x)
                .Where(x => x.IsMine && x.IsPending &&
                            string.Equals(x.DeliveryStatus, "sending", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(x.ClientMessageId) &&
                            (!x.IsAttachment || !string.IsNullOrWhiteSpace(x.AttachmentLocalPath)))
                .OrderBy(x => x.SentAt)
                .ToList();
        }
    }

    public bool MarkPendingSending(string clientMessageId)
    {
        if (string.IsNullOrWhiteSpace(clientMessageId)) return false;
        EnsureLoaded();
        lock (_sync)
        {
            foreach (var messages in _messages.Values)
            {
                var pending = messages.FirstOrDefault(x => string.Equals(x.ClientMessageId, clientMessageId, StringComparison.OrdinalIgnoreCase) && x.IsPending);
                if (pending is null) continue;
                pending.DeliveryStatus = "sending";
                Save();
                return true;
            }
        }
        return false;
    }

    public int MarkFailedAttachmentsForRetry()
    {
        EnsureLoaded();
        lock (_sync)
        {
            var count = 0;
            foreach (var messages in _messages.Values)
            {
                foreach (var message in messages)
                {
                    if (!message.IsMine || !message.IsAttachment || !message.IsPending ||
                        !string.Equals(message.DeliveryStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(message.AttachmentLocalPath) ||
                        !File.Exists(message.AttachmentLocalPath) ||
                        string.IsNullOrWhiteSpace(message.ClientMessageId))
                        continue;

                    message.DeliveryStatus = "sending";
                    count++;
                }
            }

            if (count > 0) Save();
            return count;
        }
    }

    private static int DeliveryStatusRank(string? status) => (status ?? string.Empty).Trim().ToLowerInvariant() switch
    {
        "sending" => 0,
        "failed" => 0,
        "sent" => 1,
        "received" => 1,
        "delivered" => 2,
        "read" => 3,
        _ => 1
    };

    private static bool SetDeliveryStatusIfNewer(ChatMessage message, string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        var candidate = status.Trim().ToLowerInvariant();
        var current = string.IsNullOrWhiteSpace(message.DeliveryStatus) ? "sent" : message.DeliveryStatus.Trim().ToLowerInvariant();
        if (string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase)) return false;
        if (DeliveryStatusRank(candidate) < DeliveryStatusRank(current)) return false;
        message.DeliveryStatus = candidate;
        return true;
    }

    public bool UpdateDeliveryStatus(string remoteId, string status)
    {
        if (string.IsNullOrWhiteSpace(remoteId) || string.IsNullOrWhiteSpace(status)) return false;
        EnsureLoaded();
        lock (_sync)
        {
            foreach (var messages in _messages.Values)
            {
                var message = messages.FirstOrDefault(x => string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase) && x.IsMine);
                if (message is null) continue;
                if (!SetDeliveryStatusIfNewer(message, status)) return false;
                Save();
                return true;
            }
        }
        return false;
    }

    public bool UpdateMessageDeleted(string remoteId)
    {
        if (string.IsNullOrWhiteSpace(remoteId)) return false;
        EnsureLoaded();
        lock (_sync)
        {
            foreach (var messages in _messages.Values)
            {
                var message = messages.FirstOrDefault(x => string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase));
                if (message is null) continue;
                message.Text = "تم حذف هذه الرسالة";
                message.IsDeleted = true;
                message.IsEdited = false;
                Save();
                return true;
            }
        }
        return false;
    }

    public bool UpdateMessageText(string remoteId, string text, DateTime? editedAt = null)
    {
        if (string.IsNullOrWhiteSpace(remoteId)) return false;
        EnsureLoaded();
        lock (_sync)
        {
            foreach (var messages in _messages.Values)
            {
                var message = messages.FirstOrDefault(x => string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase));
                if (message is null) continue;
                message.Text = text;
                message.IsEdited = true;
                Save();
                return true;
            }
        }
        return false;
    }

    public bool AddRemoteMessage(int conversationId, string text, DateTime sentAt, bool isMine, string? remoteId = null, string? attachmentFileName = null, string? attachmentContentType = null, long? attachmentSize = null, string? deliveryStatus = null, string? replyToRemoteId = null, string? replyToText = null, bool isEdited = false, string? editedAt = null, bool isDeleted = false, DateTime? expiresAt = null)
    {
        EnsureLoaded();
        lock (_sync)
        {
            var messages = GetMessages(conversationId);
            var localTime = sentAt.ToLocalTime();

            if (!string.IsNullOrWhiteSpace(remoteId))
            {
                // A sender sees the authoritative server response through HTTP, while
                // polling may also discover it before that response is processed.
                // Reconcile an optimistic pending copy instead of adding a duplicate.
                if (isMine)
                {
                    var pending = messages.FirstOrDefault(x =>
                        x.IsPending &&
                        x.IsMine &&
                        ((x.IsAttachment && string.Equals(x.AttachmentFileName, attachmentFileName, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(x.AttachmentContentType, attachmentContentType, StringComparison.OrdinalIgnoreCase)) ||
                         (!x.IsAttachment && string.Equals(x.Text, text, StringComparison.Ordinal))) &&
                        Math.Abs((x.SentAt - localTime).TotalSeconds) <= 60);
                    if (pending is not null)
                    {
                        pending.RemoteId = remoteId;
                        pending.ExpiresAt = expiresAt;
                        pending.IsPending = false;
                        SetDeliveryStatusIfNewer(pending, string.IsNullOrWhiteSpace(deliveryStatus) ? "sent" : deliveryStatus);
                        pending.ClientMessageId = null;
                        if (pending.IsAttachment && attachmentSize.HasValue)
                        {
                            // Preserve the local preview/download cache but update the authoritative size.
                            // AttachmentSize is init-only, so no assignment is required here.
                        }
                        Save();
                        NotifyMessagesChanged(conversationId);
                        NotifyConversationsChanged();
                        return false;
                    }
                }

                // Server messages have stable IDs; use them as the authoritative
                // deduplication key so two legitimate identical messages sent close
                // together are never collapsed into one.
                var existingRemote = messages.FirstOrDefault(x => string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase));
                if (existingRemote is not null)
                {
                    // The message content is a duplicate, but its receipt may have advanced.
                    var metadataChanged = existingRemote.IsMine && isMine && SetDeliveryStatusIfNewer(existingRemote, deliveryStatus);
                    if (expiresAt.HasValue && existingRemote.ExpiresAt != expiresAt)
                    {
                        existingRemote.ExpiresAt = expiresAt;
                        metadataChanged = true;
                    }
                    if (metadataChanged)
                    {
                        Save();
                        NotifyMessagesChanged(conversationId);
                    }
                    return false;
                }
            }
            else if (messages.Any(x => x.RemoteId is null && x.Text == text && Math.Abs((x.SentAt - localTime).TotalSeconds) <= 2))
            {
                // Only use the text/time fallback for legacy or local records that
                // have no server ID.
                return false;
            }

            var next = messages.Count == 0 ? 1 : messages.Max(x => x.Id) + 1;
            var remoteMessage = new ChatMessage { Id = next, RemoteId = remoteId, ConversationId = conversationId, Text = text, SentAt = localTime, IsMine = isMine, AttachmentFileName = attachmentFileName, AttachmentContentType = attachmentContentType, AttachmentSize = attachmentSize, DeliveryStatus = string.IsNullOrWhiteSpace(deliveryStatus) ? (isMine ? "sent" : "received") : deliveryStatus, ReplyToRemoteId = replyToRemoteId, ReplyToText = replyToText, IsEdited = isEdited, IsDeleted = isDeleted, ExpiresAt = expiresAt };
            InsertMessageChronological(messages, remoteMessage);
            var c = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (c != null)
            {
                c.LastMessage = text;
                c.Time = localTime.ToString("HH:mm");
                c.UpdatedAt = localTime;
                if (!isMine) c.UnreadCount++;
            }
            Save();
            NotifyMessagesChanged(conversationId);
            NotifyConversationsChanged();
            return true;
        }
    }

    private static void InsertMessageChronological(ObservableCollection<ChatMessage> messages, ChatMessage message)
    {
        var index = messages.Count;
        while (index > 0)
        {
            var previous = messages[index - 1];
            if (message.SentAt > previous.SentAt ||
                (message.SentAt == previous.SentAt && message.Id >= previous.Id))
                break;
            index--;
        }

        messages.Insert(index, message);
    }

    private ChatStore? ReadStoreSnapshot()
    {
        try
        {
            if (!File.Exists(_storePath))
                return null;

            var json = File.ReadAllText(_storePath);
            var store = JsonSerializer.Deserialize<ChatStore>(json);
            if (store is null || store.Conversations is null || store.Messages is null)
                throw new InvalidDataException("ملف مخزن المحادثات لا يحتوي على قوائم صالحة.");
            return store;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatStore] Could not read local cache: {ex.Message}");
            // Preserve a broken cache for diagnosis but do not retry the same bad file
            // on every launch. This work runs on the background loader, not the UI thread.
            try
            {
                if (File.Exists(_storePath))
                {
                    var backupPath = Path.Combine(FileSystem.Current.AppDataDirectory, StoreFileName + ".corrupt");
                    File.Copy(_storePath, backupPath, overwrite: true);
                    File.Delete(_storePath);
                }
            }
            catch (Exception backupEx)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo ChatStore] Could not quarantine cache: {backupEx.Message}");
            }
            return null;
        }
    }

    private static PreparedChatStore? PrepareStoreSnapshot(ChatStore? store)
    {
        if (store is null)
            return null;

        var prepared = new PreparedChatStore
        {
            Conversations = store.Conversations.OrderByDescending(x => x.UpdatedAt).ToList()
        };

        // Group, deduplicate, and create message collections on the background worker.
        // These collections are not bound to a page until a conversation is opened.
        foreach (var group in store.Messages.GroupBy(x => x.ConversationId))
        {
            var messages = new List<ChatMessage>();
            var seenRemoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var message in group.OrderBy(x => x.SentAt).ThenBy(x => x.Id))
            {
                if (message.IsPending && !message.IsAttachment)
                    message.DeliveryStatus = "failed";

                if (!string.IsNullOrWhiteSpace(message.RemoteId) &&
                    !seenRemoteIds.Add(message.RemoteId))
                    continue;

                messages.Add(message);
            }

            if (messages.Count > 0)
                prepared.Messages[group.Key] = new ObservableCollection<ChatMessage>(messages);
        }

        return prepared;
    }

    // Caller holds _sync. Collection-bound conversations are published on the UI thread,
    // while the expensive message grouping work is already complete on the worker.
    private void ApplyStoreSnapshot(PreparedChatStore? store)
    {
        if (_loaded)
            return;

        Volatile.Write(ref _loaded, true);
        if (store is null)
            return;

        foreach (var conversation in store.Conversations)
            Conversations.Add(conversation);

        foreach (var pair in store.Messages)
            _messages[pair.Key] = pair.Value;
    }

    private void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded))
            return;

        PreparedChatStore? snapshot;
        try
        {
            // The task includes both JSON deserialization and message grouping on a
            // worker, so this synchronous compatibility path only waits for prepared data.
            snapshot = _storeLoadTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo ChatStore] Cache initialization fallback failed: {ex.Message}");
            snapshot = null;
        }

        void Apply()
        {
            lock (_sync)
                ApplyStoreSnapshot(snapshot);
        }

        if (MainThread.IsMainThread)
            Apply();
        else
            MainThread.InvokeOnMainThreadAsync(Apply).GetAwaiter().GetResult();
    }

    private int _saveRequested;
    private int _saveWorkerRunning;

    private void Save()
    {
        // Persistence must not serialize the entire chat store on the UI thread.
        // Delivery/read updates can happen frequently and the synchronous JSON write
        // was causing visible freezes as the local cache grew.
        Interlocked.Exchange(ref _saveRequested, 1);
        if (Interlocked.Exchange(ref _saveWorkerRunning, 1) != 0) return;
        _ = Task.Run(SaveWorkerAsync);
    }

    private async Task SaveWorkerAsync()
    {
        try
        {
            do
            {
                Interlocked.Exchange(ref _saveRequested, 0);

                ChatStore snapshot;
                lock (_sync)
                {
                    snapshot = new ChatStore
                    {
                        Conversations = Conversations.ToList(),
                        Messages = _messages.Values.SelectMany(x => x).OrderBy(x => x.SentAt).ToList()
                    };
                }

                var json = JsonSerializer.Serialize(snapshot);
                Directory.CreateDirectory(FileSystem.Current.AppDataDirectory);
                await File.WriteAllTextAsync(_storePath, json).ConfigureAwait(false);
            }
            while (Volatile.Read(ref _saveRequested) != 0);
        }
        catch
        {
            // Local persistence must never block or crash the UI.
        }
        finally
        {
            Interlocked.Exchange(ref _saveWorkerRunning, 0);
            // A mutation can arrive between the last loop check and releasing the
            // worker flag. Start another worker in that narrow race window.
            if (Volatile.Read(ref _saveRequested) != 0 &&
                Interlocked.Exchange(ref _saveWorkerRunning, 1) == 0)
                _ = Task.Run(SaveWorkerAsync);
        }
    }

    private sealed class PreparedChatStore
    {
        public List<Conversation> Conversations { get; set; } = new();
        public Dictionary<int, ObservableCollection<ChatMessage>> Messages { get; } = new();
    }

    private sealed class ChatStore
    {
        public List<Conversation> Conversations { get; set; } = new();
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
