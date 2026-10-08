using System.Collections.ObjectModel;
using System.Text.Json;
using Himo.Models;

namespace Himo.Services;

public sealed class ChatService
{
    private const string StoreFileName = "himo_chat_store.json";
    private readonly string _storePath;
    private readonly object _sync = new();
    private readonly Dictionary<int, ObservableCollection<ChatMessage>> _messages = new();
    private bool _loaded;

    public ObservableCollection<Conversation> Conversations { get; } = new();

    public ChatService()
    {
        _storePath = Path.Combine(FileSystem.Current.AppDataDirectory, StoreFileName);
        Load();
    }

    public ObservableCollection<ChatMessage> GetMessages(int conversationId)
    {
        lock (_sync)
        {
            EnsureLoaded();
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

        lock (_sync)
        {
            EnsureLoaded();

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

    public void ReplaceConversations(IEnumerable<Conversation> items)
    {
        lock (_sync)
        {
            EnsureLoaded();
            Conversations.Clear();
            foreach (var item in items.OrderByDescending(x => x.UpdatedAt)) Conversations.Add(item);

            // Drop cached messages that no longer belong to a server conversation.
            // This keeps deleted/removed conversations from leaving orphaned data
            // in the local store indefinitely.
            var activeIds = Conversations.Select(x => x.Id).ToHashSet();
            foreach (var staleId in _messages.Keys.Where(id => !activeIds.Contains(id)).ToList())
                _messages.Remove(staleId);

            Save();
        }
    }

    public void ClearAll()
    {
        lock (_sync)
        {
            EnsureLoaded();
            Conversations.Clear();
            _messages.Clear();
            Save();
        }
    }

    public void MarkAsRead(int conversationId)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var conversation = Conversations.FirstOrDefault(x => x.Id == conversationId);
            if (conversation is null || conversation.UnreadCount == 0) return;
            conversation.UnreadCount = 0;
            Save();
        }
    }

    public Conversation AddConversation(string name)
    {
        var clean = name.Trim();
        if (string.IsNullOrWhiteSpace(clean)) throw new ArgumentException("اسم المحادثة مطلوب.", nameof(name));
        lock (_sync)
        {
            EnsureLoaded();
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

        lock (_sync)
        {
            EnsureLoaded();
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

        lock (_sync)
        {
            EnsureLoaded();
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

        lock (_sync)
        {
            EnsureLoaded();
            pending.RemoteId = remoteId;
            pending.IsPending = false;
            pending.DeliveryStatus = "sent";
            pending.ClientMessageId = null;
            Save();
        }
    }

    public ChatMessage? AddPendingMessage(int conversationId, string text, string clientMessageId, string? replyToRemoteId = null, string? replyToText = null, bool isEdited = false, string? editedAt = null)
    {
        var clean = text.Trim();
        if (conversationId <= 0 || string.IsNullOrWhiteSpace(clean) || string.IsNullOrWhiteSpace(clientMessageId)) return null;

        lock (_sync)
        {
            EnsureLoaded();
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

        lock (_sync)
        {
            EnsureLoaded();
            var messages = GetMessages(conversationId);
            var pending = messages.FirstOrDefault(x => string.Equals(x.ClientMessageId, clientMessageId, StringComparison.OrdinalIgnoreCase));
            if (pending is null) return;

            pending.RemoteId = remoteId;
            pending.IsPending = false;
            pending.DeliveryStatus = "sent";
            pending.ClientMessageId = null;
            // ChatMessage uses init-only values for text/time, so keep the optimistic
            // timestamp for a stable UI and only attach the authoritative server ID.
            Save();
        }
    }

    public void FailPendingMessage(int conversationId, string clientMessageId)
    {
        if (string.IsNullOrWhiteSpace(clientMessageId)) return;

        lock (_sync)
        {
            EnsureLoaded();
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
        lock (_sync)
        {
            EnsureLoaded();
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
        lock (_sync)
        {
            EnsureLoaded();
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
        lock (_sync)
        {
            EnsureLoaded();
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

    public bool UpdateDeliveryStatus(string remoteId, string status)
    {
        if (string.IsNullOrWhiteSpace(remoteId) || string.IsNullOrWhiteSpace(status)) return false;
        lock (_sync)
        {
            EnsureLoaded();
            foreach (var messages in _messages.Values)
            {
                var message = messages.FirstOrDefault(x => string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase) && x.IsMine);
                if (message is null) continue;
                message.DeliveryStatus = status;
                Save();
                return true;
            }
        }
        return false;
    }

    public bool UpdateMessageDeleted(string remoteId)
    {
        if (string.IsNullOrWhiteSpace(remoteId)) return false;
        lock (_sync)
        {
            EnsureLoaded();
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
        lock (_sync)
        {
            EnsureLoaded();
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

    public bool AddRemoteMessage(int conversationId, string text, DateTime sentAt, bool isMine, string? remoteId = null, string? attachmentFileName = null, string? attachmentContentType = null, long? attachmentSize = null, string? deliveryStatus = null, string? replyToRemoteId = null, string? replyToText = null, bool isEdited = false, string? editedAt = null, bool isDeleted = false)
    {
        lock (_sync)
        {
            EnsureLoaded();
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
                        pending.IsPending = false;
                        pending.DeliveryStatus = string.IsNullOrWhiteSpace(deliveryStatus) ? "sent" : deliveryStatus;
                        pending.ClientMessageId = null;
                        if (pending.IsAttachment && attachmentSize.HasValue)
                        {
                            // Preserve the local preview/download cache but update the authoritative size.
                            // AttachmentSize is init-only, so no assignment is required here.
                        }
                        Save();
                        return false;
                    }
                }

                // Server messages have stable IDs; use them as the authoritative
                // deduplication key so two legitimate identical messages sent close
                // together are never collapsed into one.
                if (messages.Any(x => string.Equals(x.RemoteId, remoteId, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            else if (messages.Any(x => x.RemoteId is null && x.Text == text && Math.Abs((x.SentAt - localTime).TotalSeconds) <= 2))
            {
                // Only use the text/time fallback for legacy or local records that
                // have no server ID.
                return false;
            }

            var next = messages.Count == 0 ? 1 : messages.Max(x => x.Id) + 1;
            var remoteMessage = new ChatMessage { Id = next, RemoteId = remoteId, ConversationId = conversationId, Text = text, SentAt = localTime, IsMine = isMine, AttachmentFileName = attachmentFileName, AttachmentContentType = attachmentContentType, AttachmentSize = attachmentSize, DeliveryStatus = string.IsNullOrWhiteSpace(deliveryStatus) ? (isMine ? "sent" : "received") : deliveryStatus, ReplyToRemoteId = replyToRemoteId, ReplyToText = replyToText, IsEdited = isEdited, IsDeleted = isDeleted };
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

    private void Load()
    {
        lock (_sync)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(_storePath))
                {
                    var store = JsonSerializer.Deserialize<ChatStore>(File.ReadAllText(_storePath));
                    if (store != null)
                    {
                        foreach (var c in store.Conversations.OrderByDescending(x => x.UpdatedAt))
                            Conversations.Add(c);

                        foreach (var group in store.Messages.GroupBy(x => x.ConversationId))
                        {
                            var list = new ObservableCollection<ChatMessage>();
                            var seenRemoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                            foreach (var message in group.OrderBy(x => x.SentAt).ThenBy(x => x.Id))
                            {
                                // Pending messages are durable outbox entries. Text messages
                                // keep the previous failed-on-reload behavior. Attachments, however,
                                // already have a local file in HimoOutbox and must remain queued so
                                // the app can upload them automatically when connectivity returns.
                                if (message.IsPending && !message.IsAttachment)
                                    message.DeliveryStatus = "failed";

                                if (!string.IsNullOrWhiteSpace(message.RemoteId) &&
                                    !seenRemoteIds.Add(message.RemoteId))
                                    continue;

                                list.Add(message);
                            }

                            if (list.Count > 0)
                                _messages[group.Key] = list;
                        }

                        if (Conversations.Count > 0)
                        {
                                    return;
                        }
                    }
                }
            }
            catch
            {
                // Preserve a broken cache for diagnosis, then start clean so a
                // malformed local file cannot break startup repeatedly.
                try
                {
                    if (File.Exists(_storePath))
                    {
                        var backupPath = Path.Combine(FileSystem.Current.AppDataDirectory, StoreFileName + ".corrupt");
                        File.Copy(_storePath, backupPath, overwrite: true);
                        File.Delete(_storePath);
                    }
                }
                catch { }
            }
        }
    }

    private void EnsureLoaded() { if (!_loaded) Load(); }

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

    private sealed class ChatStore
    {
        public List<Conversation> Conversations { get; set; } = new();
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
