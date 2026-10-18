using System.Text.Json.Serialization;

namespace Himo.Models;

public sealed record CallHistoryEntry
{
    public Guid Id { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public string ContactName { get; init; } = "جهة اتصال";
    public string Mode { get; init; } = "audio";
    public bool IsIncoming { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? ConnectedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public string Status { get; init; } = "جارٍ الاتصال";

    [JsonIgnore]
    public string Initial
    {
        get
        {
            var value = ContactName?.Trim() ?? string.Empty;
            return value.Length == 0 ? "H" : value[..1];
        }
    }

    [JsonIgnore]
    public string CallTypeText => $"{(IsIncoming ? "واردة" : "صادرة")} • {(string.Equals(Mode, "video", StringComparison.OrdinalIgnoreCase) ? "فيديو" : "صوتية")}";

    [JsonIgnore]
    public string TimeText
    {
        get
        {
            var local = StartedAt.ToLocalTime();
            return local.Date == DateTime.Now.Date ? local.ToString("HH:mm") : local.ToString("dd/MM");
        }
    }

    [JsonIgnore]
    public string StatusDisplayText => string.IsNullOrWhiteSpace(DurationText) ? Status : $"{Status} • {DurationText}";

    [JsonIgnore]
    public string DurationText
    {
        get
        {
            if (!ConnectedAt.HasValue) return string.Empty;
            var until = EndedAt ?? DateTimeOffset.UtcNow;
            var duration = until - ConnectedAt.Value;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            return duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
                : $"{duration.Minutes:00}:{duration.Seconds:00}";
        }
    }
}
