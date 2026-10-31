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
        FirebaseCloudMessagingImplementation.ChannelId = Platforms.Android.Services.NotificationService.MessageChannelId;
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
        // This callback runs from the Firebase messaging service, including when
        // Android starts the app process for a background/data message. It must
        // handle both call invites and ordinary chat messages; otherwise a message
        // with conversation_id but without call_type is silently discarded.
        FirebaseCloudMessagingImplementation.ShowLocalNotificationAction = notification =>
        {
            try
            {
                var notificationTitle = notification?.Title;
                var notificationBody = notification?.Body;
                var data = notification?.Data;
                if (data is null)
                    return;

                data.TryGetValue("conversation_id", out var conversationId);
                data.TryGetValue("call_type", out var callType);
                data.TryGetValue("call_id", out var callId);
                data.TryGetValue("call_mode", out var callMode);
                data.TryGetValue("caller_name", out var callerName);

                var service = new Platforms.Android.Services.NotificationService();

                if (!string.IsNullOrWhiteSpace(callType))
                {
                    if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase))
                    {
                        // The foreground notification manager routes invites directly
                        // to CallPage. Background invites need a real Android call
                        // notification with its own full-screen/content PendingIntent.
                        if (!MainActivity.IsAppForeground)
                        {
                            _ = service.ShowIncomingCallAsync(
                                string.IsNullOrWhiteSpace(callerName)
                                    ? notificationTitle ?? "مكالمة واردة"
                                    : callerName,
                                conversationId ?? string.Empty,
                                callMode ?? "audio",
                                callId ?? string.Empty);
                        }
                    }
                    else if (string.Equals(callType, "answering", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(callType, "accept", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = service.ClearIncomingCallAsync(
                            conversationId ?? string.Empty, callId ?? string.Empty, suppressFurtherRinging: true);
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallAnsweredByPush(
                            conversationId ?? string.Empty, callId ?? string.Empty, callType);
                    }
                    else if (string.Equals(callType, "reject", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(callType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = service.ClearIncomingCallAsync(
                            conversationId ?? string.Empty, callId ?? string.Empty, suppressFurtherRinging: true);
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallTerminatedByPush(
                            conversationId ?? string.Empty, callId ?? string.Empty);
                    }

                    // Never convert call-state messages into chat notifications.
                    return;
                }

                // Ordinary messages do not have call_type. Plugin.Firebase's
                // custom local-notification callback replaces its default handling,
                // so explicitly post a Himo notification when the app is backgrounded.
                if (MainActivity.IsAppForeground ||
                    string.IsNullOrWhiteSpace(conversationId) ||
                    !Guid.TryParse(conversationId, out var parsedConversationId) ||
                    parsedConversationId == Guid.Empty)
                    return;

                data.TryGetValue("title", out var dataTitle);
                data.TryGetValue("body", out var dataBody);

                var title = !string.IsNullOrWhiteSpace(notificationTitle)
                    ? notificationTitle
                    : !string.IsNullOrWhiteSpace(dataTitle) ? dataTitle : "رسالة جديدة";
                var body = !string.IsNullOrWhiteSpace(notificationBody)
                    ? notificationBody
                    : !string.IsNullOrWhiteSpace(dataBody) ? dataBody : "لديك رسالة جديدة";

                _ = service.ShowMessageAsync(title, body, parsedConversationId.ToString("D"));
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo Push] Background message notification posted. ConversationId={parsedConversationId:D}");
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
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;

            var context = global::Android.App.Application.Context;
            var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;
            if (manager is null)
                return;

            var messageChannel = new global::Android.App.NotificationChannel(
                Platforms.Android.Services.NotificationService.MessageChannelId,
                "رسائل Himo",
                global::Android.App.NotificationImportance.High)
            {
                Description = "إشعارات الرسائل الجديدة في Himo"
            };
            messageChannel.SetSound(
                global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification),
                null);
            manager.CreateNotificationChannel(messageChannel);

        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Early channel initialization failed: {ex}");
        }
    }
}
