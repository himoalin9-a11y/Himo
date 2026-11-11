using System.Collections.ObjectModel;
using System.Text.Json;
using Himo.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace Himo.Services;

/// <summary>
/// Keeps a locally cached call history for the Home > Calls tab and merges the
/// authenticated server history so the same calls appear on all of the user's devices.
/// It intentionally stores no phone numbers; entries reference only the Himo conversation id.
/// </summary>
public sealed class CallHistoryService
{
    private const string StorageKey = "himo_recent_call_history_v1";
    private const int MaxEntries = 100;
    private readonly object _sync = new();
    private List<CallHistoryEntry> _items = new();

    public ObservableCollection<CallHistoryEntry> Entries { get; } = new();

    public CallHistoryService()
    {
        try
        {
            var json = Preferences.Default.Get(StorageKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(json))
                _items = JsonSerializer.Deserialize<List<CallHistoryEntry>>(json) ?? new List<CallHistoryEntry>();
        }
        catch
        {
            _items = new List<CallHistoryEntry>();
        }

        _items = _items
            .Where(item => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.ConversationId))
            .OrderByDescending(item => item.StartedAt)
            .Take(MaxEntries)
            .ToList();
        Publish(_items);
    }

    public Guid Begin(string conversationId, string contactName, bool isIncoming, string mode, Guid callId = default)
    {
        if (!Guid.TryParse(conversationId, out var parsedConversationId) || parsedConversationId == Guid.Empty)
            return Guid.Empty;

        var item = new CallHistoryEntry
        {
            Id = callId == Guid.Empty ? Guid.NewGuid() : callId,
            ConversationId = parsedConversationId.ToString("D"),
            ContactName = string.IsNullOrWhiteSpace(contactName) ? "جهة اتصال" : contactName.Trim(),
            IsIncoming = isIncoming,
            Mode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio",
            StartedAt = DateTimeOffset.UtcNow,
            Status = isIncoming ? "مكالمة واردة" : "جارٍ الاتصال"
        };

        lock (_sync)
        {
            if (_items.Any(existing => existing.Id == item.Id))
                return item.Id;
            _items.Insert(0, item);
            if (_items.Count > MaxEntries)
                _items.RemoveRange(MaxEntries, _items.Count - MaxEntries);
            SaveUnsafe();
            PublishUnsafe();
        }
        return item.Id;
    }

    public void MarkConnected(Guid entryId)
    {
        if (entryId == Guid.Empty) return;
        lock (_sync)
        {
            var index = _items.FindIndex(item => item.Id == entryId);
            if (index < 0) return;
            var item = _items[index];
            if (item.ConnectedAt.HasValue || item.EndedAt.HasValue) return;
            _items[index] = item with
            {
                ConnectedAt = DateTimeOffset.UtcNow,
                Status = "متصلة"
            };
            SaveUnsafe();
            PublishUnsafe();
        }
    }

    public void Finish(Guid entryId, string? statusOverride = null)
    {
        if (entryId == Guid.Empty) return;
        lock (_sync)
        {
            var index = _items.FindIndex(item => item.Id == entryId);
            if (index < 0) return;
            var item = _items[index];
            if (item.EndedAt.HasValue) return;

            var finalStatus = !string.IsNullOrWhiteSpace(statusOverride)
                ? statusOverride.Trim()
                : item.ConnectedAt.HasValue
                    ? "مكتملة"
                    : item.IsIncoming
                        ? "مكالمة فائتة"
                        : "لم يتم الرد";

            _items[index] = item with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Status = finalStatus
            };
            SaveUnsafe();
            PublishUnsafe();
        }
    }

    public void MergeRemote(IEnumerable<CallHistoryRemoteDto> remoteEntries)
    {
        if (remoteEntries is null) return;
        var remote = remoteEntries
            .Where(item => item.Id != Guid.Empty && item.ConversationId != Guid.Empty)
            .Select(item => new CallHistoryEntry
            {
                Id = item.Id,
                ConversationId = item.ConversationId.ToString("D"),
                ContactName = string.IsNullOrWhiteSpace(item.ContactName) ? "جهة اتصال" : item.ContactName,
                Mode = string.Equals(item.Mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio",
                IsIncoming = item.IsIncoming,
                StartedAt = item.StartedAt,
                ConnectedAt = item.ConnectedAt,
                EndedAt = item.EndedAt,
                Status = item.Status
            }).ToList();

        lock (_sync)
        {
            var remoteIds = remote.Select(item => item.Id).ToHashSet();
            var localOnly = _items.Where(item => !remoteIds.Contains(item.Id));
            _items = remote.Concat(localOnly)
                .OrderByDescending(item => item.StartedAt)
                .Take(MaxEntries)
                .ToList();
            SaveUnsafe();
            PublishUnsafe();
        }
    }

    public IReadOnlyList<CallHistoryEntry> GetSnapshot()
    {
        lock (_sync) return _items.ToArray();
    }

    private void SaveUnsafe()
    {
        try { Preferences.Default.Set(StorageKey, JsonSerializer.Serialize(_items)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo CallHistory] Save failed: {ex.Message}"); }
    }

    private void PublishUnsafe() => Publish(_items.ToArray());

    private void Publish(IEnumerable<CallHistoryEntry> items)
    {
        var snapshot = items.ToArray();
        void Apply()
        {
            Entries.Clear();
            foreach (var item in snapshot)
                Entries.Add(item);
        }

        if (MainThread.IsMainThread) Apply();
        else MainThread.BeginInvokeOnMainThread(Apply);
    }
}
