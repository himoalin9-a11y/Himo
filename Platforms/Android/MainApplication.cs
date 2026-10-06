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
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

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

            var callChannel = new global::Android.App.NotificationChannel(
                "himo_calls_v2",
                "مكالمات Himo",
                global::Android.App.NotificationImportance.High)
            {
                Description = "المكالمات الصوتية والمرئية الواردة"
            };
            callChannel.SetSound(
                global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone),
                null);
            manager.CreateNotificationChannel(callChannel);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Early channel initialization failed: {ex}");
        }
    }
}
