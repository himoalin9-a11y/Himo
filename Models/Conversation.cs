using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Himo.Models;

public sealed class Conversation : INotifyPropertyChanged
{
    private string _lastMessage = "";
    private string _time = "";
    private int _unreadCount;
    private DateTime _updatedAt = DateTime.Now;

    public int Id { get; init; }
    public string? RemoteId { get; set; }
    public string Name { get; init; } = "";
    public string Initial { get; init; } = "";

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetField(ref _updatedAt, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        set => SetField(ref _lastMessage, value ?? "");
    }

    public string Time
    {
        get => _time;
        set => SetField(ref _time, value ?? "");
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set => SetField(ref _unreadCount, Math.Max(0, value));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ChatMessage : INotifyPropertyChanged
{
    public int Id { get; init; }
    public string? RemoteId { get; set; }
    public int ConversationId { get; init; }
    public string Text { get; init; } = "";
    public DateTime SentAt { get; init; }
    public bool IsMine { get; init; }
    public string? AttachmentFileName { get; init; }
    public string? AttachmentContentType { get; init; }
    public long? AttachmentSize { get; init; }
    private string? _attachmentLocalPath;
    public string? AttachmentLocalPath
    {
        get => _attachmentLocalPath;
        set
        {
            if (string.Equals(_attachmentLocalPath, value, StringComparison.Ordinal)) return;
            _attachmentLocalPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentLocalPath)));
        }
    }
    public bool IsAttachment => !string.IsNullOrWhiteSpace(AttachmentFileName);
    public bool IsImageAttachment => AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsAudioAttachment => AttachmentContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
    public string AttachmentLabel => string.IsNullOrWhiteSpace(AttachmentFileName)
        ? string.Empty
        : IsImageAttachment
            ? $"📷 {AttachmentFileName}"
            : IsAudioAttachment
                ? $"🎙️ {AttachmentFileName}"
                : $"📎 {AttachmentFileName}";
    public event PropertyChangedEventHandler? PropertyChanged;
}
