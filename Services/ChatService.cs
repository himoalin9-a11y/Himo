using System.Collections.ObjectModel;
using System.Text.Json;
using Himo.Models;

namespace Himo.Services;

public sealed class ChatService
{
    private const string StoreFileName = "himo_chat_store.json";
    private const int MaxMessagesPerConversation = 1000;
    private const int MaxCachedMessages = 10000;
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

            messages.Add(message);
            conversation.LastMessage = clean;
            conversation.Time = message.SentAt.ToString("HH:mm");
            conversation.UpdatedAt = message.SentAt;
            Save();
            return message;
        }
    }

    public bool AddRemoteMessage(int conversationId, string text, DateTime sentAt, bool isMine, string? remoteId = null, string? attachmentFileName = null, string? attachmentContentType = null, long? attachmentSize = null)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var messages = GetMessages(conversationId);
            var localTime = sentAt.ToLocalTime();

            if (!string.IsNullOrWhiteSpace(remoteId))
            {
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
            messages.Add(new ChatMessage { Id = next, RemoteId = remoteId, ConversationId = conversationId, Text = text, SentAt = localTime, IsMine = isMine, AttachmentFileName = attachmentFileName, AttachmentContentType = attachmentContentType, AttachmentSize = attachmentSize });
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

                            foreach (var message in group.OrderBy(x => x.SentAt))
                            {
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
                            TrimMessageCache();
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

    private void Save()
    {
        try
        {
            TrimMessageCache();
            var store = new ChatStore { Conversations = Conversations.ToList(), Messages = _messages.Values.SelectMany(x => x).OrderBy(x => x.SentAt).ToList() };
            Directory.CreateDirectory(FileSystem.Current.AppDataDirectory);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(store));
        }
        catch { }
    }

    private void TrimMessageCache()
    {
        foreach (var pair in _messages)
        {
            while (pair.Value.Count > MaxMessagesPerConversation)
                pair.Value.RemoveAt(0);
        }

        var allMessages = _messages.Values
            .SelectMany(x => x)
            .OrderBy(x => x.SentAt)
            .ToList();

        if (allMessages.Count <= MaxCachedMessages) return;

        var removeCount = allMessages.Count - MaxCachedMessages;
        var toRemove = allMessages.Take(removeCount).ToHashSet();
        foreach (var pair in _messages)
        {
            for (var i = pair.Value.Count - 1; i >= 0; i--)
            {
                if (toRemove.Contains(pair.Value[i]))
                    pair.Value.RemoveAt(i);
            }
        }
    }

    private sealed class ChatStore
    {
        public List<Conversation> Conversations { get; set; } = new();
        public List<ChatMessage> Messages { get; set; } = new();
    }
}
