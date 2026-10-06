using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

sealed class FcmPushService
{
    private readonly ILogger<FcmPushService> _logger;
    private readonly PostgresStore _store;
    private readonly FirebaseMessaging? _messaging;

    public bool IsEnabled => _messaging is not null;
    public string? ProjectId { get; private set; }

    public FcmPushService(ILogger<FcmPushService> logger, IHostEnvironment environment, PostgresStore store)
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

            FirebaseApp firebaseApp;
            try
            {
                firebaseApp = FirebaseApp.DefaultInstance;
            }
            catch (InvalidOperationException)
            {
                firebaseApp = FirebaseApp.Create(new AppOptions { Credential = credential });
            }

            _messaging = FirebaseMessaging.GetMessaging(firebaseApp);
            ProjectId = firebaseApp.Options.ProjectId;
            _logger.LogInformation("[Himo FCM] Firebase Cloud Messaging is enabled. ProjectId={ProjectId}", ProjectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase Cloud Messaging initialization failed. Push notifications are disabled.");
        }
    }

    public async Task SendMessageAsync(
        IReadOnlyList<string> tokens,
        string senderName,
        string message,
        Guid conversationId,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        if (_messaging is null)
        {
            _logger.LogError("[Himo FCM] Message push skipped: Firebase Messaging is not initialized.");
            return;
        }

        if (tokens.Count == 0)
        {
            _logger.LogWarning("[Himo FCM] Message push skipped: recipient has no registered FCM token.");
            return;
        }

        _logger.LogInformation("[Himo FCM] Preparing message push. Tokens={TokenCount}, ConversationId={ConversationId}, MessageId={MessageId}", tokens.Count, conversationId, messageId);

        var cleanTokens = tokens
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (cleanTokens.Count == 0)
            return;

        var data = new Dictionary<string, string>
        {
            ["conversation_id"] = conversationId.ToString("D"),
            ["message_id"] = messageId.ToString("D"),
            ["notification_type"] = "chat_message",
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
                        Notification = new Notification
                        {
                            Title = string.IsNullOrWhiteSpace(senderName) ? "رسالة جديدة" : senderName,
                            Body = string.IsNullOrWhiteSpace(message) ? "لديك رسالة جديدة" : message
                        },
                        Data = data,
                        Android = new AndroidConfig
                        {
                            // Force timely delivery while the app is backgrounded or terminated.
                            Priority = Priority.High,
                            // Allow delivery before the first unlock after boot as well.
                            DirectBootOk = true,
                            // A message should remain deliverable for up to one day if the
                            // device is temporarily offline.
                            TimeToLive = TimeSpan.FromDays(1),
                            // Make sure a token from another Firebase Android app cannot be
                            // accepted accidentally.
                            RestrictedPackageName = "com.companyname.himo",
                            Notification = new AndroidNotification
                            {
                                ChannelId = "himo_messages_v4",
                                Priority = NotificationPriority.HIGH,
                                Sound = "default",
                                DefaultSound = true,
                                DefaultVibrateTimings = true,
                                Icon = "himo_notification"
                                // Do not set Tag: Android would use the same tag to replace
                                // previous notifications instead of showing each new message.
                            }
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
                            _logger.LogDebug("[Himo FCM] Token delivery accepted. BatchIndex={BatchIndex}", i);
                            continue;
                        }

                        _logger.LogWarning(sendResponse.Exception,
                            "[Himo FCM] Token delivery failed. BatchIndex={BatchIndex}", i);

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
    }
    public async Task SendCallInviteAsync(
        IReadOnlyList<string> tokens,
        string callerName,
        Guid conversationId,
        string mode,
        CancellationToken cancellationToken = default)
    {
        if (_messaging is null || tokens.Count == 0) return;

        var cleanTokens = tokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
        if (cleanTokens.Count == 0) return;

        var data = new Dictionary<string, string>
        {
            ["call_type"] = "invite",
            ["conversation_id"] = conversationId.ToString("D"),
            ["call_mode"] = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "video" : "audio",
            ["is_silent_in_foreground"] = "true"
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
                    Notification = new Notification
                    {
                        Title = string.IsNullOrWhiteSpace(callerName) ? "مكالمة واردة" : callerName,
                        Body = string.Equals(mode, "video", StringComparison.OrdinalIgnoreCase) ? "مكالمة فيديو واردة" : "مكالمة صوتية واردة"
                    },
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        CollapseKey = $"himo-call-{conversationId:D}",
                        Notification = new AndroidNotification
                        {
                            ChannelId = "himo_calls",
                            Priority = NotificationPriority.HIGH,
                            DefaultSound = true,
                            DefaultVibrateTimings = true,
                            Tag = $"himo-call-{conversationId:D}",
                            Sticky = true
                        }
                    }
                };

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await _messaging.SendEachForMulticastAsync(message, timeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FCM call invite delivery failed. ConversationId={ConversationId}", conversationId);
            }
        }
    }

}
