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
        // Plugin.Firebase's FirebaseMessagingService also runs when Android starts
        // the app process for a data-only FCM message. The callback is configured
        // here (Application lifetime), so it does not depend on MainActivity having
        // been created yet. Call invites become call-category full-screen intents;
        // terminal call states remove the same notification by call ID.
        FirebaseCloudMessagingImplementation.ShowLocalNotificationAction = notification =>
        {
            try
            {
                // The Firebase callback may be invoked without a notification object.
                // Cache nullable fields safely before processing the optional data map.
                var notificationTitle = notification?.Title;
                var notificationBody = notification?.Body;
                var data = notification?.Data;
                if (data is not null && data.TryGetValue("call_type", out var callType))
                {
                    data.TryGetValue("conversation_id", out var conversationId);
                    data.TryGetValue("call_id", out var callId);
                    data.TryGetValue("call_mode", out var callMode);
                    data.TryGetValue("caller_name", out var callerName);
                    var service = new Platforms.Android.Services.NotificationService();

                    if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase))
                    {
                        // The foreground notification event routes to CallPage. Only
                        // create a full-screen system notification when app is backgrounded.
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
                    else if (string.Equals(callType, "reject", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(callType, "end", StringComparison.OrdinalIgnoreCase))
                    {
                        _ = service.ClearIncomingCallAsync(
                            conversationId ?? string.Empty, callId ?? string.Empty, suppressFurtherRinging: true);
                        Platforms.Android.Services.NotificationService.NotifyIncomingCallTerminatedByPush(
                            conversationId ?? string.Empty, callId ?? string.Empty);
                    }
                    else if (!string.IsNullOrWhiteSpace(conversationId))
                    {
                        _ = service.ShowMessageAsync(
                            notificationTitle ?? "رسالة جديدة", notificationBody ?? "لديك رسالة جديدة", conversationId);
                    }
                    return;
                }

                // Generic message pushes are handled by the existing foreground
                // manager / system tray. Don't manufacture an unrouteable notification.
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
