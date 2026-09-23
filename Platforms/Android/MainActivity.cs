using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Plugin.Firebase.CloudMessaging;

namespace Himo;

[global::Android.App.Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTop,
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.ScreenSize
        | global::Android.Content.PM.ConfigChanges.Orientation
        | global::Android.Content.PM.ConfigChanges.UiMode
        | global::Android.Content.PM.ConfigChanges.ScreenLayout
        | global::Android.Content.PM.ConfigChanges.SmallestScreenSize
        | global::Android.Content.PM.ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static string? _pendingConversationId;
    private static int _navigationInProgress;
    private const string PendingConversationPreferenceKey = "himo_pending_conversation_id";

    public static string? PendingConversationId => _pendingConversationId;

    public static void ClearPendingConversation()
    {
        _pendingConversationId = null;
        Preferences.Default.Remove(PendingConversationPreferenceKey);
    }

    public static void SetPendingConversation(string? conversationId)
    {
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            _pendingConversationId = conversationId;
            Preferences.Default.Set(PendingConversationPreferenceKey, conversationId);
        }
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try { FirebaseCloudMessagingImplementation.OnNewIntent(Intent); } catch { }
        CreateNotificationChannel();
        CaptureNotificationIntent(Intent);
        RestorePendingConversation();
        TryNavigateToPendingConversation();
        SchedulePendingConversationNavigation();
    }

    protected override void OnNewIntent(global::Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        if (intent is null) return;
        Intent = intent;
        try { FirebaseCloudMessagingImplementation.OnNewIntent(intent); } catch { }
        CaptureNotificationIntent(intent);
        RestorePendingConversation();
        TryNavigateToPendingConversation();
    }

    public static void TryNavigateToPendingConversation()
    {
        var conversationId = _pendingConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            conversationId = Preferences.Default.Get(PendingConversationPreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(conversationId))
                _pendingConversationId = conversationId;
        }
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Interlocked.Exchange(ref _navigationInProgress, 1) != 0) return;

            try
            {
                // Read again on the UI thread so a newer notification intent wins.
                conversationId = _pendingConversationId;
                if (string.IsNullOrWhiteSpace(conversationId)) return;

                var shell = Shell.Current;
                if (shell is null) return;

                await shell.GoToAsync($"chat?id={Uri.EscapeDataString(conversationId)}");
                if (string.Equals(_pendingConversationId, conversationId, StringComparison.Ordinal))
                    ClearPendingConversation();
            }
            catch
            {
                // The shell may still be initializing; App will retry after startup.
            }
            finally
            {
                Volatile.Write(ref _navigationInProgress, 0);
            }
        });
    }


    private static void RestorePendingConversation()
    {
        if (string.IsNullOrWhiteSpace(_pendingConversationId))
        {
            var saved = Preferences.Default.Get(PendingConversationPreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
                _pendingConversationId = saved;
        }
    }

    private static void SchedulePendingConversationNavigation()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                RestorePendingConversation();
                if (string.IsNullOrWhiteSpace(_pendingConversationId)) break;
                await Task.Delay(attempt == 0 ? 350 : 500);
                TryNavigateToPendingConversation();
            }
        });
    }

    private static void CaptureNotificationIntent(global::Android.Content.Intent? intent)
    {
        if (intent is null) return;

        // Depending on the Android/FCM/plugin path, the conversation id can be
        // placed directly in the intent or inside the plugin's FCM notification
        // bundle. Handle both so notification taps work from foreground,
        // background, and cold-start states.
        var conversationId = intent.GetStringExtra("conversation_id");
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingConversation(conversationId);
            return;
        }

        var extras = intent.Extras;
        conversationId = FindConversationId(extras);
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            _pendingConversationId = conversationId;
            Preferences.Default.Set(PendingConversationPreferenceKey, conversationId);
        }
    }

    private static string? FindConversationId(global::Android.OS.Bundle? bundle)
    {
        if (bundle is null) return null;

        if (bundle.ContainsKey("conversation_id"))
        {
            var direct = bundle.GetString("conversation_id");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;
        }

        // Plugin.Firebase may wrap the FCM payload in a Bundle under
        // IntentKeyFCMNotification. Walk nested bundles as a fallback.
        foreach (var key in bundle.KeySet() ?? new global::System.Collections.Generic.HashSet<string>())
        {
            try
            {
                var value = bundle.Get(key);
                if (value is global::Android.OS.Bundle nested)
                {
                    var found = FindConversationId(nested);
                    if (!string.IsNullOrWhiteSpace(found))
                        return found;
                }
            }
            catch
            {
                // A malformed/foreign extra must not prevent app startup.
            }
        }

        return null;
    }

    public static void ConfigureFirebaseMessagingChannel()
    {
        CreateNotificationChannel();
    }

    private static void CreateNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26)) return;

        var context = global::Android.App.Application.Context;
        var manager = context.GetSystemService(global::Android.Content.Context.NotificationService)
            as global::Android.App.NotificationManager;
        if (manager is null) return;

        const string channelId = "himo_messages";
        var channel = new global::Android.App.NotificationChannel(
            channelId,
            "رسائل Himo",
            global::Android.App.NotificationImportance.Default)
        {
            Description = "إشعارات الرسائل الجديدة في Himo"
        };
        manager.CreateNotificationChannel(channel);
        FirebaseCloudMessagingImplementation.ChannelId = channelId;
    }

    public static bool IsNotificationPermissionGranted()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return true;

        var context = global::Android.App.Application.Context;
        return global::AndroidX.Core.Content.ContextCompat.CheckSelfPermission(
            context,
            global::Android.Manifest.Permission.PostNotifications) == global::Android.Content.PM.Permission.Granted;
    }

    public static void OpenNotificationSettings()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;

            var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionAppNotificationSettings);
            intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName);
            intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not open Android notification settings: {ex}");
        }
    }

    public static void RequestNotificationPermissionIfNeeded()
    {
        // Do not ask Android for notification permission when the user has
        // explicitly disabled Himo notifications inside the app.
        if (!Preferences.Get("himo_notifications_enabled", true))
            return;

        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            return;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
            return;

        if (!IsNotificationPermissionGranted())
        {
            global::AndroidX.Core.App.ActivityCompat.RequestPermissions(
                activity,
                new[] { global::Android.Manifest.Permission.PostNotifications },
                7001);
        }
    }
}
