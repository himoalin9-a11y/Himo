using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

sealed class FcmPushService
{
    private readonly ILogger<FcmPushService> _logger;
    private readonly MongoStore _store;
    private readonly FirebaseMessaging? _messaging;

    public bool IsEnabled => _messaging is not null;
    public string? ProjectId { get; private set; }

    public FcmPushService(ILogger<FcmPushService> logger, IHostEnvironment environment, MongoStore store)
    {
        _logger = logger;
        _store = store;

        // Production-friendly credential sources, in this order:
        // 1) HIMO_FIREBASE_SERVICE_ACCOUNT_JSON (JSON content, recommended for hosted secrets)
        // 2) HIMO_FIREBASE_SERVICE_ACCOUNT (path to a JSON file)
        // 3) GOOGLE_APPLICATION_CREDENTIALS (standard Google credential path)
        // 4) App_Data/firebase-service-account.json (local/server fallback)
        var json = Environment.GetEnvironmentVariable("HIMO_FIREBASE_SERVICE_ACCOUNT_JSON");
        var serviceAccountPath = Environment.GetEnvironmentVariable("HIMO_FIREBASE_SERVICE_ACCOUNT");
        if (string.IsNullOrWhiteSpace(serviceAccountPath))
            serviceAccountPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        if (string.IsNullOrWhiteSpace(serviceAccountPath))
            serviceAccountPath = Path.Combine(environment.ContentRootPath, "App_Data", "firebase-service-account.json");

        try
        {
            GoogleCredential credential;
            if (!string.IsNullOrWhiteSpace(json))
            {
                credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
                _logger.LogInformation("Firebase Cloud Messaging credentials loaded from environment JSON.");
            }
            else if (File.Exists(serviceAccountPath))
            {
                using var credentialStream = File.OpenRead(serviceAccountPath);
                credential = CredentialFactory.FromStream<ServiceAccountCredential>(credentialStream).ToGoogleCredential();
                _logger.LogInformation("Firebase Cloud Messaging credentials loaded from {Path}.", serviceAccountPath);
            }
            else
            {
                _logger.LogWarning("Firebase push is disabled because no Firebase service-account credentials were found.");
                return;
            }

            FirebaseApp? firebaseApp = null;
            try
            {
                firebaseApp = FirebaseApp.DefaultInstance;
            }
            catch (InvalidOperationException)
            {
                // No default Firebase app exists yet; create the Himo app from
                // the service-account credential loaded above.
            }

            firebaseApp ??= FirebaseApp.Create(new AppOptions { Credential = credential });

            _messaging = FirebaseMessaging.GetMessaging(firebaseApp);
            ProjectId = firebaseApp.Options?.ProjectId;
            _logger.LogInformation("[Himo FCM] Firebase Cloud Messaging is enabled. ProjectId={ProjectId}", ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase Cloud Messaging initialization failed. Push notifications are disabled.");
        }
    }

    public async Task<IReadOnlyList<string>> SendMessageAsync(
        IReadOnlyList<string> tokens,
        string senderName,
        string message,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var acceptedTokens = new List<string>();

        if (_messaging is null)
        {
            _logger.LogError("[Himo FCM] Message push skipped: Firebase Messaging is not initialized.");
            return acceptedTokens;
        }

        if (tokens.Count == 0)
        {
            _logger.LogWarning("[Himo FCM] Message push skipped: recipient has no registered FCM token.");
            return acceptedTokens;
        }

        var cleanTokens = tokens
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (cleanTokens.Count == 0)
            return acceptedTokens;

        var data = new Dictionary<string, string>
        {
            // Chat pushes are data-only so the Android app can always render them
            // with Himo's own notification icon and a PendingIntent carrying the
            // conversation ID. A top-level FCM Notification is system-rendered
            // and cannot reliably use the app's deep-link/call routing.
            ["conversation_id"] = conversationId.ToString("D"),
            ["message_id"] = messageId.ToString("D"),
            ["notification_type"] = "chat_message",
            ["title"] = string.IsNullOrWhiteSpace(senderName) ? "رسالة جديدة" : senderName,
            ["body"] = string.IsNullOrWhiteSpace(message) ? "لديك رسالة جديدة" : message,
            ["is_silent_in_foreground"] = "true"
        };

        try
        {
            var totalSuccess = 0;
            var totalFailure = 0;
            var removedTokenCount = 0;

            foreach (var batch in cleanTokens.Chunk(500))
            {
                try
                {
                    var multicast = new MulticastMessage
                    {
#pragma warning disable CS0618 // FirebaseAdmin currently uses registration tokens here; Fids are a different identifier type.
                        Tokens = batch.ToList(),
#pragma warning restore CS0618
                        Data = data,
                        // Keep this a high-priority data message. The Android
                        // MainApplication callback posts the local notification
                        // using the Himo icon and conversation PendingIntent.
                        Android = new AndroidConfig
                        {
                            Priority = Priority.High
                        }
                    };

                    // Keep one slow FCM request from holding the API request indefinitely.
                    // The caller cancellation is still honored immediately.
                    using var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    batchCts.CancelAfter(TimeSpan.FromSeconds(15));
                    var response = await _messaging.SendEachForMulticastAsync(multicast, batchCts.Token);
                    totalSuccess += response.SuccessCount;
                    totalFailure += response.FailureCount;

                    for (var i = 0; i < response.Responses.Count && i < batch.Length; i++)
                    {
                        var sendResponse = response.Responses[i];
                        if (sendResponse.IsSuccess)
                        {
                            acceptedTokens.Add(batch[i]);
                            continue;
                        }

                        if (sendResponse.Exception is FirebaseMessagingException { MessagingErrorCode: MessagingErrorCode.Unregistered })
                        {
                            try
                            {
                                _store.RemovePushToken(batch[i]);
                                removedTokenCount++;
                            }
                            catch (Exception cleanupEx)
                            {
                                // A database cleanup failure must not turn an otherwise completed
                                // FCM batch into a failed batch. The token can be cleaned up later.
                                _logger.LogWarning(cleanupEx,
                                    "Could not remove an expired FCM token from the database.");
                            }
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    // The per-batch timeout is intentional. Treat it as a failed batch,
                    // but continue with any remaining batches.
                    totalFailure += batch.Length;
                    _logger.LogWarning(
                        "FCM batch send timed out after 15 seconds. BatchSize={BatchSize}.",
                        batch.Length);
                }
                catch (Exception ex)
                {
                    totalFailure += batch.Length;
                    _logger.LogError(ex, "FCM batch send failed. BatchSize={BatchSize}.", batch.Length);
                }
            }

            if (removedTokenCount > 0)
                _logger.LogInformation("Removed {RemovedTokenCount} expired FCM token(s) from the database.", removedTokenCount);

            if (totalFailure == 0)
            {
                _logger.LogInformation(
                    "FCM message delivery completed successfully. Tokens={TokenCount}, Success={SuccessCount}.",
                    cleanTokens.Count,
                    totalSuccess);
            }
            else
            {
                _logger.LogWarning(
                    "FCM message delivery completed with failures. Tokens={TokenCount}, Success={SuccessCount}, Failure={FailureCount}.",
                    cleanTokens.Count,
                    totalSuccess,
                    totalFailure);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("FCM message send was canceled because the request was canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FCM message send failed.");
        }

        return acceptedTokens;
    }
    public async Task SendCallInviteAsync(
        IReadOnlyList<string> tokens,
        string callerName,
        Guid conversationId,
        string mode,
        Guid callId,
        CancellationToken cancellationToken = default)
    {
        if (_messaging is null || tokens.Count == 0) return;

        var cleanTokens = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
        if (cleanTokens.Count == 0) return;

        var normalizedMode = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio";
        var title = string.IsNullOrWhiteSpace(callerName) ? "مكالمة واردة" : callerName.Trim();
        var body = normalizedMode == "video" ? "مكالمة فيديو واردة" : "مكالمة صوتية واردة";
        var data = new Dictionary<string, string>
        {
            // Important: do not set FirebaseAdmin.Messaging.Message.Notification here.
            // Notification+data is auto-posted by Android while the app is backgrounded,
            // which cannot carry Himo's FullScreenIntent/notification cancellation logic.
            ["call_type"] = "invite",
            ["conversation_id"] = conversationId.ToString("D"),
            ["call_id"] = callId.ToString("D"),
            ["call_mode"] = normalizedMode,
            ["caller_name"] = title,
            ["title"] = title,
            ["body"] = body,
            ["is_silent_in_foreground"] = "false"
        };

        foreach (var batch in cleanTokens.Chunk(500))
        {
            try
            {
                var message = new MulticastMessage
                {
#pragma warning disable CS0618
                    Tokens = batch.ToList(),
#pragma warning restore CS0618
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        // Do not assign per-call collapse keys: FCM only retains up to
                        // four different collapse keys per device. Call IDs are validated
                        // and terminal states are tombstoned on the client instead.
                        TimeToLive = TimeSpan.FromSeconds(45)
                    }
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var response = await _messaging.SendEachForMulticastAsync(message, timeout.Token);
                _logger.LogInformation(
                    "FCM call invite result. ConversationId={ConversationId}, CallId={CallId}, Success={Success}, Failure={Failure}",
                    conversationId, callId, response.SuccessCount, response.FailureCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM call invite delivery failed. ConversationId={ConversationId}; CallId={CallId}", conversationId, callId);
            }
        }
    }

    public async Task SendCallStateAsync(
        IReadOnlyList<string> tokens,
        Guid conversationId,
        Guid callId,
        string state,
        CancellationToken cancellationToken = default)
    {
        if (_messaging is null || tokens.Count == 0 || callId == Guid.Empty) return;
        var normalizedState = (state ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedState is not ("answering" or "accept" or "reject" or "end")) return;

        var cleanTokens = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
        if (cleanTokens.Count == 0) return;

        // title/body intentionally cause the plugin's custom local-notification action
        // to run in a cold process; MainApplication intercepts these state events and
        // cancels the ringing notification instead of displaying a notification.
        var data = new Dictionary<string, string>
        {
            ["call_type"] = normalizedState,
            ["conversation_id"] = conversationId.ToString("D"),
            ["call_id"] = callId.ToString("D"),
            ["title"] = "__HIMO_CALL_STATE__",
            ["body"] = "__HIMO_CALL_STATE__",
            ["is_silent_in_foreground"] = "false"
        };

        foreach (var batch in cleanTokens.Chunk(500))
        {
            try
            {
                var message = new MulticastMessage
                {
#pragma warning disable CS0618
                    Tokens = batch.ToList(),
#pragma warning restore CS0618
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        // Keep state events non-collapsible: FCM does not guarantee ordering.
                        // A terminal cancellation must outlive the 45-second Invite TTL;
                        // otherwise an offline receiver could get the Invite but lose End/Reject.
                        TimeToLive = normalizedState is "end" or "reject"
                            ? TimeSpan.FromMinutes(2)
                            : TimeSpan.FromSeconds(45)
                    }
                };
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // Terminal states run off the SignalR request path, so allow Firebase longer
                // to accept them; this cannot hold the caller's End/Reject UI open.
                timeout.CancelAfter(normalizedState is "end" or "reject"
                    ? TimeSpan.FromSeconds(10)
                    : TimeSpan.FromSeconds(5));
                var response = await _messaging.SendEachForMulticastAsync(message, timeout.Token);
                _logger.LogInformation(
                    "FCM call state result. State={State}, ConversationId={ConversationId}, CallId={CallId}, Success={Success}, Failure={Failure}",
                    normalizedState, conversationId, callId, response.SuccessCount, response.FailureCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM call-state delivery failed. State={State}, ConversationId={ConversationId}, CallId={CallId}",
                    normalizedState, conversationId, callId);
            }
        }
    }

}
