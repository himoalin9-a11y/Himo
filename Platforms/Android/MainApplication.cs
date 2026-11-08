using System;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Plugin.Firebase.CloudMessaging;

[assembly: global::Android.App.UsesPermission(global::Android.Manifest.Permission.RecordAudio)]
[assembly: global::Android.App.UsesPermission(global::Android.Manifest.Permission.Camera)]

namespace Himo;

[global::Android.App.Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, global::Android.Runtime.JniHandleOwnership ownership)
        : base(handle, ownership)
    {
        FirebaseCloudMessagingImplementation.ChannelId = Platforms.Android.Services.NotificationService.GetCurrentMessageChannelId();
        EnsureNotificationChannels();
        // This routine queries the system notification manager and reflectively
        // walks active notifications. It is cleanup work, not launch-critical UI;
        // doing it on Application.OnCreate can delay the first activity frame.
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try { Platforms.Android.Services.NotificationService.ClearExistingCallNotifications(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Himo Startup] Stale call notification cleanup skipped: {ex.Message}"); }
        });
        ConfigureFcmLocalNotificationAction();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    private static void ConfigureFcmLocalNotificationAction()
    {
        // This callback is installed for the application lifetime. Message pushes
        // are data-only so Himo can select the per-category tone on this device.
        FirebaseCloudMessagingImplementation.ShowLocalNotificationAction = notification =>
        {
            try
            {
                var data = notification?.Data;
                if (data is null) return;

                var notificationTitle = notification?.Title;
                var notificationBody = notification?.Body;
                if (data.TryGetValue("title", out var dataTitle) && !string.IsNullOrWhiteSpace(dataTitle))
                    notificationTitle = dataTitle;
                if (data.TryGetValue("body", out var dataBody) && !string.IsNullOrWhiteSpace(dataBody))
                    notificationBody = dataBody;

                if (data.TryGetValue("call_type", out var callType))
                {
                    data.TryGetValue("conversation_id", out var conversationId);
                    data.TryGetValue("call_id", out var callId);
                    data.TryGetValue("call_mode", out var callMode);
                    data.TryGetValue("caller_name", out var callerName);
                    var service = new Platforms.Android.Services.NotificationService();

                    if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase))
                    {
                        if (MainActivity.IsAppForeground) return;
                        _ = service.ShowIncomingCallAsync(
                            string.IsNullOrWhiteSpace(callerName) ? notificationTitle ?? "مكالمة واردة" : callerName,
                            conversationId ?? string.Empty,
                            callMode ?? "audio",
                            callId ?? string.Empty);
                    }
                    else if (string.Equals(callType, "answering", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(callType, "accept", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = service.ClearIncomingCallAsync(
                            conversationId ?? string.Empty, callId ?? string.Empty, suppressFurtherRinging: true);
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallAnsweredByPush(
                            conversationId ?? string.Empty, callId ?? string.Empty, callType);
                    }
                    else if (string.Equals(callType, "leave", StringComparison.OrdinalIgnoreCase) ||
                             (string.Equals(callType, "reject", StringComparison.OrdinalIgnoreCase) &&
                              data.TryGetValue("is_group_peer_exit", out var peerExitText) &&
                              string.Equals(peerExitText, "true", StringComparison.OrdinalIgnoreCase)))
                    {
                        // A group participant (including the original initiator) left.
                        // Dismiss only this device's pending ring notification; accepted
                        // participants must remain in their existing peer-to-peer mesh.
                        _ = service.ClearIncomingCallAsync(
                            conversationId ?? string.Empty, callId ?? string.Empty, suppressFurtherRinging: true);
                    }
                    else if (string.Equals(callType, "reject", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(callType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = service.ClearIncomingCallAsync(
                            conversationId ?? string.Empty, callId ?? string.Empty, suppressFurtherRinging: true);
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallTerminatedByPush(
                            conversationId ?? string.Empty, callId ?? string.Empty);
                    }
                    return;
                }

                if (MainActivity.IsAppForeground ||
                    !data.TryGetValue("notification_type", out var notificationType) ||
                    !string.Equals(notificationType, "chat_message", StringComparison.OrdinalIgnoreCase) ||
                    !data.TryGetValue("conversation_id", out var messageConversationId) ||
                    string.IsNullOrWhiteSpace(messageConversationId))
                    return;

                var isGroup = data.TryGetValue("is_group", out var groupText) &&
                              bool.TryParse(groupText, out var parsedGroup) && parsedGroup;
                var messageService = new Platforms.Android.Services.NotificationService();
                _ = messageService.ShowMessageAsync(
                    notificationTitle ?? "رسالة جديدة",
                    notificationBody ?? "لديك رسالة جديدة",
                    messageConversationId,
                    isGroup);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Himo Push] FCM local notification action failed: {ex}");
            }
        };
    }

    private static void EnsureNotificationChannels()
    {
        try
        {
            Platforms.Android.Services.NotificationService.RefreshNotificationChannels();
            FirebaseCloudMessagingImplementation.ChannelId =
                Platforms.Android.Services.NotificationService.GetCurrentMessageChannelId();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Early channel initialization failed: {ex}");
        }
    }
}
