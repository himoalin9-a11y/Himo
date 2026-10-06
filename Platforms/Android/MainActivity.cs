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
    private static string? _pendingMessageId;
    private static string? _pendingMessageText;
    private static string? _pendingMessageSender;
    private static string? _pendingMessageSentAt;
    private static string? _pendingCallConversationId;
    private static string? _pendingCallMode;
    private static int _navigationInProgress;
    private static int _callNavigationInProgress;
    private const string PendingConversationPreferenceKey = "himo_pending_conversation_id";
    private const string PendingMessageIdPreferenceKey = "himo_pending_message_id";
    private const string PendingMessageTextPreferenceKey = "himo_pending_message_text";
    private const string PendingMessageSenderPreferenceKey = "himo_pending_message_sender";
    private const string PendingMessageSentAtPreferenceKey = "himo_pending_message_sent_at";
    private const string PendingCallConversationPreferenceKey = "himo_pending_call_conversation_id";
    private const string PendingCallModePreferenceKey = "himo_pending_call_mode";

    public static string? PendingConversationId => _pendingConversationId;

    public static void ClearPendingConversation()
    {
        _pendingConversationId = null;
        Preferences.Default.Remove(PendingConversationPreferenceKey);
    }

    public static void SetPendingConversation(string? conversationId)
        => SetPendingConversation(conversationId, null, null, null, null);

    public static void SetPendingConversation(string? conversationId, string? messageId, string? messageText, string? senderName, string? sentAt)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        _pendingConversationId = conversationId;
        Preferences.Default.Set(PendingConversationPreferenceKey, conversationId);

        if (!string.IsNullOrWhiteSpace(messageId))
        {
            _pendingMessageId = messageId;
            _pendingMessageText = messageText ?? string.Empty;
            _pendingMessageSender = senderName ?? string.Empty;
            _pendingMessageSentAt = sentAt ?? string.Empty;
            Preferences.Default.Set(PendingMessageIdPreferenceKey, _pendingMessageId);
            Preferences.Default.Set(PendingMessageTextPreferenceKey, _pendingMessageText);
            Preferences.Default.Set(PendingMessageSenderPreferenceKey, _pendingMessageSender);
            Preferences.Default.Set(PendingMessageSentAtPreferenceKey, _pendingMessageSentAt);
        }
    }

    public static bool TryConsumePendingMessage(out string? messageId, out string? messageText, out string? senderName, out DateTimeOffset sentAt)
    {
        messageId = _pendingMessageId ?? Preferences.Default.Get(PendingMessageIdPreferenceKey, string.Empty);
        messageText = _pendingMessageText ?? Preferences.Default.Get(PendingMessageTextPreferenceKey, string.Empty);
        senderName = _pendingMessageSender ?? Preferences.Default.Get(PendingMessageSenderPreferenceKey, string.Empty);
        var sentAtText = _pendingMessageSentAt ?? Preferences.Default.Get(PendingMessageSentAtPreferenceKey, string.Empty);

        if (string.IsNullOrWhiteSpace(messageId) || !Guid.TryParse(messageId, out _))
        {
            sentAt = DateTimeOffset.UtcNow;
            return false;
        }

        if (!DateTimeOffset.TryParse(sentAtText, out sentAt))
            sentAt = DateTimeOffset.UtcNow;

        _pendingMessageId = null;
        _pendingMessageText = null;
        _pendingMessageSender = null;
        _pendingMessageSentAt = null;
        Preferences.Default.Remove(PendingMessageIdPreferenceKey);
        Preferences.Default.Remove(PendingMessageTextPreferenceKey);
        Preferences.Default.Remove(PendingMessageSenderPreferenceKey);
        Preferences.Default.Remove(PendingMessageSentAtPreferenceKey);
        return true;
    }

    public static void SetPendingCall(string conversationId, string mode)
    {
        if (string.IsNullOrWhiteSpace(conversationId)) return;
        _pendingCallConversationId = conversationId;
        _pendingCallMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        Preferences.Default.Set(PendingCallConversationPreferenceKey, conversationId);
        Preferences.Default.Set(PendingCallModePreferenceKey, _pendingCallMode);
    }

    public static void ClearPendingCall()
    {
        _pendingCallConversationId = null;
        _pendingCallMode = null;
        Preferences.Default.Remove(PendingCallConversationPreferenceKey);
        Preferences.Default.Remove(PendingCallModePreferenceKey);
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
                if (string.IsNullOrWhiteSpace(conversationId)) return;
                await shell.GoToAsync($"///CallPage?id={Uri.EscapeDataString(conversationId)}&mode={Uri.EscapeDataString(mode)}&incoming=true");
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

        _pendingMessageId ??= Preferences.Default.Get(PendingMessageIdPreferenceKey, string.Empty);
        _pendingMessageText ??= Preferences.Default.Get(PendingMessageTextPreferenceKey, string.Empty);
        _pendingMessageSender ??= Preferences.Default.Get(PendingMessageSenderPreferenceKey, string.Empty);
        _pendingMessageSentAt ??= Preferences.Default.Get(PendingMessageSentAtPreferenceKey, string.Empty);
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
        var messageId = intent.GetStringExtra("message_id");
        var messageText = intent.GetStringExtra("message_text");
        var senderName = intent.GetStringExtra("sender_name");
        var sentAt = intent.GetStringExtra("sent_at");
        var callType = intent.GetStringExtra("call_type");
        var callMode = intent.GetStringExtra("call_mode");
        if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingCall(conversationId, callMode ?? "audio");
            return;
        }

        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            SetPendingConversation(conversationId, messageId, messageText, senderName, sentAt);
            return;
        }

        var found = FindNotificationData(intent.Extras);
        if (found.CallConversationId is not null)
        {
            SetPendingCall(found.CallConversationId, found.CallMode ?? "audio");
            return;
        }
        if (found.ConversationId is not null)
            SetPendingConversation(found.ConversationId);
    }

    private static (string? ConversationId, string? CallConversationId, string? CallMode) FindNotificationData(global::Android.OS.Bundle? bundle)
    {
        if (bundle is null) return (null, null, null);
        var conversationId = bundle.GetString("conversation_id");
        var callType = bundle.GetString("call_type");
        var callMode = bundle.GetString("call_mode");
        if (string.Equals(callType, "invite", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(conversationId))
            return (conversationId, conversationId, callMode);
        if (!string.IsNullOrWhiteSpace(conversationId)) return (conversationId, null, null);

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
        return (null, null, null);
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
