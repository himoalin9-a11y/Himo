using Himo.Services;

namespace Himo.Platforms.Android.Services;

public sealed class NotificationService : INotificationService
{
    public const string MessageChannelId = "himo_messages_v4";
    private const string ChannelName = "رسائل Himo";
    private const string ChannelDescription = "إشعارات الرسائل الجديدة في Himo";
    private const int NotificationIdBase = 12000;
    private const string EnabledPreferenceKey = "himo_notifications_enabled";

    public bool IsEnabled => Preferences.Default.Get(EnabledPreferenceKey, true);

    public Task InitializeAsync()
    {
        EnsureChannels();
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        Preferences.Set(EnabledPreferenceKey, enabled);
        if (!enabled)
            return ClearAllAsync();

        EnsureChannels();
        return Task.CompletedTask;
    }

    public Task ShowMessageAsync(string senderName, string message, string conversationId)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(conversationId))
            return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        if (context is null)
            return Task.CompletedTask;

        if (OperatingSystem.IsAndroidVersionAtLeast(33) &&
            global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
                context,
                global::Android.Manifest.Permission.PostNotifications) != global::Android.Content.PM.Permission.Granted)
        {
            System.Diagnostics.Debug.WriteLine("[Himo Push] Notification permission is not granted.");
            return Task.CompletedTask;
        }

        EnsureChannels();

        var title = string.IsNullOrWhiteSpace(senderName) ? "رسالة جديدة" : senderName.Trim();
        var text = string.IsNullOrWhiteSpace(message) ? "لديك رسالة جديدة" : message.Trim();
        var notificationManager = global::AndroidX.Core.App.NotificationManagerCompat.From(context);
        if (!notificationManager.AreNotificationsEnabled())
        {
            System.Diagnostics.Debug.WriteLine("[Himo Push] Android notifications are disabled for Himo.");
            return Task.CompletedTask;
        }

        var notificationId = GetNotificationId(conversationId);

        var intent = new global::Android.Content.Intent(context, typeof(MainActivity));
        intent.SetAction(global::Android.Content.Intent.ActionView);
        intent.SetFlags(global::Android.Content.ActivityFlags.SingleTop | global::Android.Content.ActivityFlags.ClearTop);
        intent.PutExtra("conversation_id", conversationId);

        var pendingIntentFlags = global::Android.App.PendingIntentFlags.UpdateCurrent;
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.M)
            pendingIntentFlags |= global::Android.App.PendingIntentFlags.Immutable;

        var pendingIntent = global::Android.App.PendingIntent.GetActivity(
            context,
            notificationId,
            intent,
            pendingIntentFlags);

        if (pendingIntent is null)
            return Task.CompletedTask;

        var builder = new global::AndroidX.Core.App.NotificationCompat.Builder(context, MessageChannelId)
            .SetSmallIcon(Resource.Drawable.himo_notification)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new global::AndroidX.Core.App.NotificationCompat.BigTextStyle().BigText(text))
            .SetPriority(global::AndroidX.Core.App.NotificationCompat.PriorityHigh)
            .SetCategory(global::AndroidX.Core.App.NotificationCompat.CategoryMessage)
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(false)
            .SetContentIntent(pendingIntent)
            .SetVisibility(global::AndroidX.Core.App.NotificationCompat.VisibilityPrivate);

        notificationManager.Notify(notificationId, builder.Build());

        return Task.CompletedTask;
    }

    public Task ClearConversationAsync(string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return Task.CompletedTask;

        var context = global::Android.App.Application.Context;
        global::AndroidX.Core.App.NotificationManagerCompat.From(context)
            .Cancel(GetNotificationId(conversationId));
        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        var context = global::Android.App.Application.Context;
        global::AndroidX.Core.App.NotificationManagerCompat.From(context).CancelAll();
        return Task.CompletedTask;
    }

    private static void EnsureChannels()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
            return;

        try
        {
            var context = global::Android.App.Application.Context;
            var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
                as global::Android.App.NotificationManager;
            if (manager is null)
                return;

            var channel = new global::Android.App.NotificationChannel(
                MessageChannelId,
                ChannelName,
                global::Android.App.NotificationImportance.High)
            {
                Description = ChannelDescription
            };
            channel.SetSound(
                global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification),
                null);
            manager.CreateNotificationChannel(channel);

            var calls = new global::Android.App.NotificationChannel(
                "himo_calls",
                "مكالمات Himo",
                global::Android.App.NotificationImportance.High)
            {
                Description = "المكالمات الصوتية والمرئية الواردة"
            };
            calls.SetSound(
                global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone),
                null);
            manager.CreateNotificationChannel(calls);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Himo Push] Notification channel initialization failed: {ex}");
        }
    }

    private static int GetNotificationId(string conversationId)
    {
        unchecked
        {
            if (Guid.TryParse(conversationId, out var remoteId))
            {
                var bytes = remoteId.ToByteArray();
                var hash = 17;
                foreach (var b in bytes)
                    hash = (hash * 31) + b;
                return NotificationIdBase + (hash & 0x3fffffff);
            }

            var fallback = 17;
            foreach (var ch in conversationId)
                fallback = (fallback * 31) + ch;
            return NotificationIdBase + (fallback & 0x3fffffff);
        }
    }
}
