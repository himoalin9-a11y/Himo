using Himo.Models;

namespace Himo.Views;

public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? ImageTemplate { get; set; }
    public DataTemplate? VideoTemplate { get; set; }
    public DataTemplate? AudioTemplate { get; set; }
    public DataTemplate? FileTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not ChatMessage message)
            return TextTemplate ?? throw new InvalidOperationException("Text message template is not configured.");

        if (message.IsImageAttachment) return ImageTemplate ?? TextTemplate!;
        if (message.IsVideoAttachment) return VideoTemplate ?? TextTemplate!;
        if (message.IsAudioAttachment) return AudioTemplate ?? TextTemplate!;
        if (message.IsAttachment) return FileTemplate ?? TextTemplate!;
        return TextTemplate ?? throw new InvalidOperationException("Text message template is not configured.");
    }
}
