using Himo.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Himo.Models;

public sealed class Conversation : INotifyPropertyChanged
{
    private string _lastMessage = "";
    private string _time = "";
    private int _unreadCount;
    private DateTime _updatedAt = DateTime.Now;

    public int Id { get; init; }
    public string? RemoteId { get; set; }

    private string _name = "";
    public string Name
    {
        get => _name;
        set => SetField(ref _name, value ?? string.Empty);
    }

    private string _initial = "";
    public string Initial
    {
        get => _initial;
        set => SetField(ref _initial, value ?? string.Empty);
    }

    private Guid? _otherParticipantUserId;
    public Guid? OtherParticipantUserId
    {
        get => _otherParticipantUserId;
        set
        {
            if (_otherParticipantUserId == value) return;
            _otherParticipantUserId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OtherParticipantUserId)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPhotoPath)));
        }
    }

    private bool _hasRemoteProfilePhoto;
    public bool HasRemoteProfilePhoto
    {
        get => _hasRemoteProfilePhoto;
        set
        {
            if (_hasRemoteProfilePhoto == value) return;
            _hasRemoteProfilePhoto = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasRemoteProfilePhoto)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPhotoPath)));
        }
    }

    private long _profilePhotoVersion;
    public long ProfilePhotoVersion
    {
        get => _profilePhotoVersion;
        set
        {
            if (_profilePhotoVersion == value) return;
            _profilePhotoVersion = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfilePhotoVersion)));
        }
    }

    private bool _hasGroupPhoto;
    public bool HasGroupPhoto
    {
        get => _hasGroupPhoto;
        set => SetField(ref _hasGroupPhoto, value);
    }

    private long _groupPhotoVersion;
    public long GroupPhotoVersion
    {
        get => _groupPhotoVersion;
        set => SetField(ref _groupPhotoVersion, value);
    }

    private string? _groupPhotoPath;
    public string? GroupPhotoPath
    {
        get => _groupPhotoPath;
        set
        {
            if (string.Equals(_groupPhotoPath, value, StringComparison.Ordinal)) return;
            _groupPhotoPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupPhotoPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPhotoPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalProfilePhoto)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowProfileInitial)));
        }
    }

    private string? _profilePhotoPath;
    public string? ProfilePhotoPath
    {
        get => _profilePhotoPath;
        set
        {
            if (string.Equals(_profilePhotoPath, value, StringComparison.Ordinal)) return;
            _profilePhotoPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ProfilePhotoPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPhotoPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalProfilePhoto)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowProfileInitial)));
        }
    }

    public string? AvatarPhotoPath => IsGroup ? GroupPhotoPath : ProfilePhotoPath;
    public bool HasLocalProfilePhoto =>
        !string.IsNullOrWhiteSpace(AvatarPhotoPath) && File.Exists(AvatarPhotoPath);
    public bool ShowProfileInitial => !HasLocalProfilePhoto;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    private bool _isArchived;
    public bool IsArchived
    {
        get => _isArchived;
        set => SetField(ref _isArchived, value);
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    private bool _isBlocked;
    public bool IsBlocked
    {
        get => _isBlocked;
        set => SetField(ref _isBlocked, value);
    }

    private bool _isGroup;
    public bool IsGroup
    {
        get => _isGroup;
        set
        {
            if (_isGroup == value) return;
            _isGroup = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGroup)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AvatarPhotoPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalProfilePhoto)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowProfileInitial)));
        }
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set => SetField(ref _updatedAt, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(_lastMessage, next, StringComparison.Ordinal)) return;
            _lastMessage = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMessage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMessagePreview)));
        }
    }

    // Persist the MIME type separately from the display text so the conversation
    // preview can always be rendered in the currently selected application language.
    private string? _lastMessageAttachmentContentType;
    public string? LastMessageAttachmentContentType
    {
        get => _lastMessageAttachmentContentType;
        set
        {
            if (string.Equals(_lastMessageAttachmentContentType, value, StringComparison.OrdinalIgnoreCase)) return;
            _lastMessageAttachmentContentType = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMessageAttachmentContentType)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMessagePreview)));
        }
    }

    [JsonIgnore]
    public string LastMessagePreview
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LastMessageAttachmentContentType))
                return ChatMessage.GetAttachmentPreviewText(LastMessageAttachmentContentType);

            // Older/server conversation snapshots may contain the attachment's
            // filename instead of its MIME type. Restore the short media preview
            // from the filename extension rather than exposing a long device name.
            var legacy = LastMessage.Trim();
            var extension = Path.GetExtension(legacy);
            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".heic", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
                return ChatMessage.GetAttachmentPreviewText("image/unknown");
            if (extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".3gp", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".avi", StringComparison.OrdinalIgnoreCase))
                return ChatMessage.GetAttachmentPreviewText("video/unknown");

            // Older chat caches stored the Arabic preview itself. Recognize those
            // legacy values so they are localized even before the next message sync.
            if (legacy is "📷 صورة" or "📷 الصورة" or "صورة")
                return ChatMessage.GetAttachmentPreviewText("image/unknown");
            if (legacy is "🎙️ رسالة صوتية" or "🎙 رسالة صوتية" or "الرسالة الصوتية")
                return ChatMessage.GetAttachmentPreviewText("audio/unknown");
            if (legacy is "🎬 فيديو" or "فيديو")
                return ChatMessage.GetAttachmentPreviewText("video/unknown");
            if (legacy is "📎 ملف مرفق" or "ملف مرفق")
                return ChatMessage.GetAttachmentPreviewText(null);

            return LastMessage;
        }
    }

    public void SetLastMessageText(string? text)
    {
        LastMessageAttachmentContentType = null;
        LastMessage = text ?? string.Empty;
        NotifyLastMessagePreviewChanged();
    }

    public void SetLastMessageAttachment(string? contentType)
    {
        LastMessageAttachmentContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        LastMessage = ChatMessage.GetAttachmentPreviewText(LastMessageAttachmentContentType);
        NotifyLastMessagePreviewChanged();
    }

    public void SetLastMessageFromServer(
        string? text,
        DateTime updatedAt,
        string? attachmentContentType = null)
    {
        // Conversation snapshots can briefly contain an empty LastMessage while
        // message history is still syncing. Preserve a valid local preview, but
        // still accept MIME metadata when the server provides it.
        var incoming = text?.Trim() ?? string.Empty;
        var hasIncomingAttachmentType = !string.IsNullOrWhiteSpace(attachmentContentType);

        if (string.IsNullOrWhiteSpace(incoming) && !hasIncomingAttachmentType)
            return;

        var sameMessageText = string.Equals(LastMessage, incoming, StringComparison.Ordinal);

        if (hasIncomingAttachmentType)
        {
            LastMessageAttachmentContentType = attachmentContentType!.Trim();
        }
        else if (updatedAt > UpdatedAt && !sameMessageText)
        {
            LastMessageAttachmentContentType = null;
        }

        if (!string.IsNullOrWhiteSpace(incoming))
            LastMessage = incoming;

        NotifyLastMessagePreviewChanged();
    }

    public void NotifyLastMessagePreviewChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastMessagePreview)));

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
    private double _messageFontSize = 16;
    public double MessageFontSize
    {
        get => _messageFontSize;
        set
        {
            if (Math.Abs(_messageFontSize - value) < 0.1) return;
            _messageFontSize = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MessageFontSize)));
        }
    }
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
    private string? _translatedText;
    public string? TranslatedText
    {
        get => _translatedText;
        set
        {
            if (string.Equals(_translatedText, value, StringComparison.Ordinal)) return;
            _translatedText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslatedText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationActionText)));
        }
    }
    private bool _isTranslationVisible;
    public bool IsTranslationVisible
    {
        get => _isTranslationVisible;
        set
        {
            if (_isTranslationVisible == value) return;
            _isTranslationVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTranslationVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationActionText)));
        }
    }
    private bool _translationEnabled;
    public bool TranslationEnabled
    {
        get => _translationEnabled;
        set
        {
            if (_translationEnabled == value) return;
            _translationEnabled = value;
            if (!value) IsTranslationVisible = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationEnabled)));
        }
    }
    private bool _isTranslating;
    public bool IsTranslating
    {
        get => _isTranslating;
        set
        {
            if (_isTranslating == value) return;
            _isTranslating = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTranslating)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TranslationActionText)));
        }
    }
    public string TranslationActionText => IsTranslating ? "جارٍ الترجمة…" : IsTranslationVisible ? "إخفاء الترجمة" : string.IsNullOrWhiteSpace(TranslatedText) ? "ترجمة الرسالة" : "عرض الترجمة";
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
    private bool _isDeleted;
    public bool IsDeleted
    {
        get => _isDeleted;
        set
        {
            if (_isDeleted == value) return;
            _isDeleted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDeleted)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsImageAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAudioAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVideoAttachment)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLocalImagePreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasVideoPreview)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentDisplayName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentLabel)));
        }
    }
    /// <summary>Server-specified expiry for disappearing messages.</summary>
    public DateTime? ExpiresAt { get; set; }
    public string EditedLabel => IsEdited ? "(معدلة)" : string.Empty;
    public string DateSeparatorText { get; set; } = string.Empty;
    public bool HasDateSeparator => !string.IsNullOrWhiteSpace(DateSeparatorText);
    public bool HasReply => !string.IsNullOrWhiteSpace(ReplyToRemoteId) || !string.IsNullOrWhiteSpace(ReplyToText);
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public long? AttachmentSize { get; set; }
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
    private string? _videoThumbnailPath;
    [JsonIgnore]
    public string? VideoThumbnailPath
    {
        get => _videoThumbnailPath;
        set
        {
            if (string.Equals(_videoThumbnailPath, value, StringComparison.Ordinal)) return;
            _videoThumbnailPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VideoThumbnailPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasVideoPreview)));
        }
    }

    private double _videoDisplayWidth = 300;
    [JsonIgnore]
    public double VideoDisplayWidth
    {
        get => _videoDisplayWidth;
        set
        {
            if (Math.Abs(_videoDisplayWidth - value) < 0.01) return;
            _videoDisplayWidth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VideoDisplayWidth)));
        }
    }

    private double _videoDisplayHeight = 420;
    [JsonIgnore]
    public double VideoDisplayHeight
    {
        get => _videoDisplayHeight;
        set
        {
            if (Math.Abs(_videoDisplayHeight - value) < 0.01) return;
            _videoDisplayHeight = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VideoDisplayHeight)));
        }
    }

    private string _videoDurationText = string.Empty;
    [JsonIgnore]
    public string VideoDurationText
    {
        get => _videoDurationText;
        set
        {
            if (string.Equals(_videoDurationText, value, StringComparison.Ordinal)) return;
            _videoDurationText = value ?? string.Empty;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VideoDurationText)));
        }
    }

    public bool IsAttachment => !IsDeleted && !string.IsNullOrWhiteSpace(AttachmentFileName);
    public bool IsImageAttachment => IsAttachment && AttachmentContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasLocalImagePreview => IsImageAttachment && !string.IsNullOrWhiteSpace(AttachmentLocalPath);
    public bool IsAudioAttachment => IsAttachment && AttachmentContentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsVideoAttachment => IsAttachment && AttachmentContentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true;
    public bool HasVideoPreview => IsVideoAttachment && !string.IsNullOrWhiteSpace(VideoThumbnailPath);

    [JsonIgnore]
    public string AttachmentDisplayName
    {
        get
        {
            // Media messages are represented by their localized type label, not
            // by provider-generated or original filenames. Keep real filenames
            // for ordinary documents only.
            if (IsImageAttachment)
                return GetAttachmentTypeLabel("image");
            if (IsVideoAttachment)
                return GetAttachmentTypeLabel("video");

            var fileName = Path.GetFileName(AttachmentFileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fileName) || IsOpaqueAttachmentName(fileName))
                return GetAttachmentTypeLabel(AttachmentContentType ?? string.Empty);
            return fileName;
        }
    }

    [JsonIgnore]
    public string AttachmentLabel
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AttachmentFileName)) return string.Empty;
            if (IsImageAttachment || IsVideoAttachment) return GetAttachmentPreviewText(AttachmentContentType);
            return $"{GetAttachmentPreviewText(AttachmentContentType)} {AttachmentDisplayName}";
        }
    }

    public static string GetAttachmentPreviewText(string? contentType)
    {
        if (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            return $"📷 {GetAttachmentTypeLabel("image")}";
        if (contentType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
            return $"🎙️ {GetAttachmentTypeLabel("audio")}";
        if (contentType?.StartsWith("video/", StringComparison.OrdinalIgnoreCase) == true)
            return $"🎬 {GetAttachmentTypeLabel("video")}";
        return $"📎 {GetAttachmentTypeLabel("file")}";
    }

    private static string GetAttachmentTypeLabel(string type)
    {
        // Keep these short labels available for every language supported by
        // AppLocalization, even when an optional satellite resource is absent.
        var language = AppLocalization.CurrentLanguageCode;
        return (type, language) switch
        {
            ("image", "en") => "Image",
            ("image", "fr") => "Image",
            ("image", "tr") => "Resim",
            ("image", "ur") => "تصویر",
            ("image", "es") => "Imagen",
            ("image", "id") => "Gambar",
            ("audio", "en") => "Voice message",
            ("audio", "fr") => "Message vocal",
            ("audio", "tr") => "Sesli mesaj",
            ("audio", "ur") => "صوتی پیغام",
            ("audio", "es") => "Mensaje de voz",
            ("audio", "id") => "Pesan suara",
            ("video", "en") => "Video",
            ("video", "fr") => "Vidéo",
            ("video", "tr") => "Video",
            ("video", "ur") => "ویڈیو",
            ("video", "es") => "Vídeo",
            ("video", "id") => "Video",
            ("file", "en") => "Attachment",
            ("file", "fr") => "Pièce jointe",
            ("file", "tr") => "Ek dosya",
            ("file", "ur") => "منسلک فائل",
            ("file", "es") => "Archivo adjunto",
            ("file", "id") => "Lampiran",
            ("image", _) => "صورة",
            ("audio", _) => "رسالة صوتية",
            ("video", _) => "فيديو",
            _ => "ملف مرفق"
        };
    }

    private static bool IsOpaqueAttachmentName(string fileName)
    {
        if (fileName.Contains("://", StringComparison.Ordinal) ||
            fileName.StartsWith("content:", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("document:", StringComparison.OrdinalIgnoreCase))
            return true;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (Guid.TryParse(stem, out _))
            return true;

        // Some Android document providers expose a 24-64 character hexadecimal
        // token instead of the user's original file name. Never show that token.
        return stem.Length is >= 24 and <= 64 && stem.All(Uri.IsHexDigit);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
