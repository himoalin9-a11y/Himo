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
    private string _text = "";
    public string Text
    {
        get => _text;
        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal)) return;
            _text = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }
    public DateTime SentAt { get; init; }
    public bool IsMine { get; init; }
    private bool _isPending;
    public bool IsPending
    {
        get => _isPending;
        set
        {
            if (_isPending == value) return;
            _isPending = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPending)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeliveryStatusText)));
        }
    }
    private string _deliveryStatus = "sent";
    public string DeliveryStatus
    {
        get => _deliveryStatus;
        set
        {
            if (string.Equals(_deliveryStatus, value, StringComparison.OrdinalIgnoreCase)) return;
            _deliveryStatus = string.IsNullOrWhiteSpace(value) ? "sent" : value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeliveryStatus)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeliveryStatusText)));
        }
    }
    public string DeliveryStatusText => DeliveryStatus switch
    {
        "sending" => "◷",
        "read" => "✓✓",
        "delivered" => "✓✓",
        "failed" => "!",
        _ => "✓"
    };
    public string? ClientMessageId { get; set; }
    public string? ReplyToRemoteId { get; set; }
    public string? ReplyToText { get; set; }
    public bool IsEdited { get; set; }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public bool IsDeleted { get; set; }
    public string EditedLabel => IsEdited ? "(معدلة)" : string.Empty;
    public string DateSeparatorText { get; set; } = string.Empty;
    public bool HasDateSeparator => !string.IsNullOrWhiteSpace(DateSeparatorText);
    public bool HasReply => !string.IsNullOrWhiteSpace(ReplyToRemoteId) || !string.IsNullOrWhiteSpace(ReplyToText);
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalImagePreview)));
        }
    }
    public bool IsAttachment => !string.IsNullOrWhiteSpace(AttachmentFileName);
    public bool IsImageAttachment => AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasLocalImagePreview => IsImageAttachment && !string.IsNullOrWhiteSpace(AttachmentLocalPath);
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
