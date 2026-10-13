namespace Himo.Services;

public interface INotificationService
{
    bool IsEnabled { get; }
    Task InitializeAsync();
    Task SetEnabledAsync(bool enabled);
    Task ShowMessageAsync(string senderName, string message, string conversationId);
    Task ShowIncomingCallAsync(string callerName, string conversationId, string mode, string callId);
    Task ClearIncomingCallAsync(string conversationId, string callId, bool suppressFurtherRinging = true);
    bool IsIncomingCallSuppressed(string callId);
    Task ClearConversationAsync(string conversationId);
    Task ClearAllAsync();
}
