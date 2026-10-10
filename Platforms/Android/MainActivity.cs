using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Plugin.Firebase.CloudMessaging;

namespace Himo;

[global::Android.App.Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    ResizeableActivity = true,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTask,
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.ScreenSize
        | global::Android.Content.PM.ConfigChanges.Orientation
        | global::Android.Content.PM.ConfigChanges.UiMode
        | global::Android.Content.PM.ConfigChanges.ScreenLayout
        | global::Android.Content.PM.ConfigChanges.SmallestScreenSize
        | global::Android.Content.PM.ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private static string? _pendingConversationId;
    private static string? _pendingCallConversationId;
    private static string? _pendingCallMode;
    private static string? _pendingCallId;
    private static int _navigationInProgress;
    private static int _callNavigationInProgress;
    private const string PendingConversationPreferenceKey = "himo_pending_conversation_id";
    private const string PendingCallConversationPreferenceKey = "himo_pending_call_conversation_id";
    private const string PendingCallModePreferenceKey = "himo_pending_call_mode";
    private const string PendingCallIdPreferenceKey = "himo_pending_call_id";

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

    public static void SetPendingCall(string conversationId, string mode, string? callId = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;
        _pendingCallConversationId = conversationId;
        _pendingCallMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        _pendingCallId = Guid.TryParse(callId, out var parsedCallId) && parsedCallId != Guid.Empty
            ? parsedCallId.ToString("D")
            : null;

        Preferences.Default.Set(PendingCallConversationPreferenceKey, conversationId);
        Preferences.Default.Set(PendingCallModePreferenceKey, _pendingCallMode);
        if (_pendingCallId is not null)
            Preferences.Default.Set(PendingCallIdPreferenceKey, _pendingCallId);
        else
            Preferences.Default.Remove(PendingCallIdPreferenceKey);
    }

    public static void ClearPendingCall()
    {
        _pendingCallConversationId = null;
        _pendingCallMode = null;
        _pendingCallId = null;
        Preferences.Default.Remove(PendingCallConversationPreferenceKey);
        Preferences.Default.Remove(PendingCallModePreferenceKey);
        Preferences.Default.Remove(PendingCallIdPreferenceKey);
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        try { FirebaseCloudMessagingImplementation.OnNewIntent(Intent); } catch { }
        CreateNotificationChannel();
        CaptureNotificationIntent(Intent);
        RestorePendingConversation();
        RestorePendingCall();
        TryNavigateToPendingCall();
        TryNavigateToPendingConversation();
        SchedulePendingCallNavigation();
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
        RestorePendingCall();
        TryNavigateToPendingCall();
        TryNavigateToPendingConversation();
    }

    public static void TryNavigateToPendingCall()
    {
        RestorePendingCall();
        var conversationId = _pendingCallConversationId;
        var mode = _pendingCallMode ?? "audio";
        if (string.IsNullOrWhiteSpace(conversationId)) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Interlocked.Exchange(ref _callNavigationInProgress, 1) != 0) return;
            try
            {
                var shell = Shell.Current;
                if (shell is null) return;
                conversationId = _pendingCallConversationId;
                mode = _pendingCallMode ?? "audio";
                var callId = _pendingCallId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(conversationId)) return;
                var callIdQuery = string.IsNullOrWhiteSpace(callId)
                    ? string.Empty
                    : $"&callId={Uri.EscapeDataString(callId)}";
                await shell.GoToAsync($"CallPage?id={Uri.EscapeDataString(conversationId)}&mode={Uri.EscapeDataString(mode)}&incoming=true{callIdQuery}");
                ClearPendingCall();
            }
            catch
            {
                // Shell may still be initializing; startup retry handles it.
            }
            finally
            {
                Volatile.Write(ref _callNavigationInProgress, 0);
            }
        });
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


    private static void RestorePendingCall()
    {
        if (string.IsNullOrWhiteSpace(_pendingCallConversationId))
            _pendingCallConversationId = Preferences.Default.Get(PendingCallConversationPreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(_pendingCallMode))
            _pendingCallMode = Preferences.Default.Get(PendingCallModePreferenceKey, "audio");
        if (string.IsNullOrWhiteSpace(_pendingCallId))
            _pendingCallId = Preferences.Default.Get(PendingCallIdPreferenceKey, string.Empty);
    }

    private static void SchedulePendingCallNavigation()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                RestorePendingCall();
                if (string.IsNullOrWhiteSpace(_pendingCallConversationId)) break;
                await Task.Delay(attempt == 0 ? 350 : 500);
                TryNavigateToPendingCall();
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

        var conversationId = intent.GetStringExtra("conversation_id");
        var callType = intent.GetStringExtra("call_type");
        var callMode = intent.GetStringExtra("call_mode");
        var callId = intent.GetStringExtra("call_id");
        if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingCall(conversationId, callMode ?? "audio", callId);
            return;
        }

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingConversation(conversationId);
            return;
        }

        var found = FindNotificationData(intent.Extras);
        if (found.CallConversationId is not null)
        {
            SetPendingCall(found.CallConversationId, found.CallMode ?? "audio", found.CallId);
            return;
        }
        if (found.ConversationId is not null)
            SetPendingConversation(found.ConversationId);
    }

    private static (string? ConversationId, string? CallConversationId, string? CallMode, string? CallId) FindNotificationData(global::Android.OS.Bundle? bundle)
    {
        if (bundle is null) return (null, null, null, null);
        var conversationId = bundle.GetString("conversation_id");
        var callType = bundle.GetString("call_type");
        var callMode = bundle.GetString("call_mode");
        var callId = bundle.GetString("call_id");
        if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conversationId))
            return (conversationId, conversationId, callMode, callId);
        if (!string.IsNullOrWhiteSpace(conversationId)) return (conversationId, null, null, null);

        foreach (var key in bundle.KeySet() ?? new global::System.Collections.Generic.HashSet<string>())
        {
            try
            {
                if (bundle.Get(key) is global::Android.OS.Bundle nested)
                {
                    var found = FindNotificationData(nested);
                    if (found.CallConversationId is not null || found.ConversationId is not null) return found;
                }
            }
            catch { }
        }
        return (null, null, null, null);
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


        var channel = new global::Android.App.NotificationChannel(
            Platforms.Android.Services.NotificationService.MessageChannelId,
            "رسائل Himo",
            global::Android.App.NotificationImportance.High)
        {
            Description = "إشعارات الرسائل الجديدة في Himo"
        };
        channel.SetSound(
            global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Notification),
            null);
        manager.CreateNotificationChannel(channel);

        var calls = new global::Android.App.NotificationChannel(
            Platforms.Android.Services.NotificationService.CallsChannelId,
            "مكالمات Himo",
            global::Android.App.NotificationImportance.High)
        {
            Description = "المكالمات الصوتية والمرئية الواردة"
        };
        var ringtoneAudioAttributes = new global::Android.Media.AudioAttributes.Builder()
            .SetUsage(global::Android.Media.AudioUsageKind.NotificationRingtone)
            .SetContentType(global::Android.Media.AudioContentType.Sonification)
            .Build();
        calls.SetSound(
            global::Android.Media.RingtoneManager.GetDefaultUri(global::Android.Media.RingtoneType.Ringtone),
            ringtoneAudioAttributes);
        manager.CreateNotificationChannel(calls);
        FirebaseCloudMessagingImplementation.ChannelId = Platforms.Android.Services.NotificationService.MessageChannelId;
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
