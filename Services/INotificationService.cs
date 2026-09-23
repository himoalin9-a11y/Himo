namespace Himo.Services;

public interface INotificationService
{
    bool IsEnabled { get; }
    Task InitializeAsync();
    Task SetEnabledAsync(bool enabled);
    Task ShowMessageAsync(string senderName, string message, string conversationId);
    Task ClearConversationAsync(string conversationId);
    Task ClearAllAsync();
}
