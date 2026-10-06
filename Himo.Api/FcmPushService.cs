using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using System.Text.Json;

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
        if (string.IsNullOrWhiteSpace(serviceAccountPath) &&
            File.Exists("/etc/secrets/firebase.json"))
        {
            serviceAccountPath = "/etc/secrets/firebase.json";
        }

        if (string.IsNullOrWhiteSpace(serviceAccountPath))
            serviceAccountPath = Path.Combine(environment.ContentRootPath, "App_Data", "firebase-service-account.json");

        try
        {
            GoogleCredential credential;
            string? projectId = null;

            if (!string.IsNullOrWhiteSpace(json))
            {
                credential = GoogleCredential.FromJson(json);

                try
                {
                    using var jsonDocument = JsonDocument.Parse(json);
                    if (jsonDocument.RootElement.TryGetProperty("project_id", out var projectIdElement))
                        projectId = projectIdElement.GetString();
                }
                catch (JsonException)
                {
                    // Credential parsing above is the authoritative validation.
                }

                _logger.LogInformation(
                    "[Himo FCM] Firebase credentials loaded from HIMO_FIREBASE_SERVICE_ACCOUNT_JSON. ProjectId={ProjectId}",
                    projectId ?? "(from credential)");
            }
            else if (File.Exists(serviceAccountPath))
            {
                var fileJson = File.ReadAllText(serviceAccountPath);
                credential = GoogleCredential.FromJson(fileJson);

                try
                {
                    using var jsonDocument = JsonDocument.Parse(fileJson);
                    if (jsonDocument.RootElement.TryGetProperty("project_id", out var projectIdElement))
                        projectId = projectIdElement.GetString();
                }
                catch (JsonException)
                {
                    // Credential parsing above is the authoritative validation.
                }

                _logger.LogInformation(
                    "[Himo FCM] Firebase credentials loaded from {Path}. ProjectId={ProjectId}",
                    serviceAccountPath,
                    projectId ?? "(from credential)");
            }
            else
            {
                _logger.LogWarning("Firebase push is disabled because no Firebase service-account credentials were found.");
                return;
            }

            // DefaultInstance is null when no default Firebase app exists.
            // The previous code only handled an exception, so initialization could
            // continue with a null app and end up disabled.
            var firebaseApp = FirebaseApp.DefaultInstance
                ?? FirebaseApp.Create(new AppOptions
                {
                    Credential = credential,
                    ProjectId = projectId
                });

            _messaging = FirebaseMessaging.GetMessaging(firebaseApp);
            ProjectId = firebaseApp.Options.ProjectId ?? projectId;

            if (string.IsNullOrWhiteSpace(ProjectId))
            {
                _logger.LogWarning(
                    "[Himo FCM] Firebase initialized but ProjectId is empty. Push notifications remain disabled.");
                return;
            }

            _logger.LogInformation(
                "[Himo FCM] Firebase Cloud Messaging is enabled. ProjectId={ProjectId}",
                ProjectId);
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
                            Priority = Priority.High,
                            Notification = new AndroidNotification
                            {
                                ChannelId = "himo_messages_v5",
                                Priority = NotificationPriority.HIGH,
                                DefaultSound = true,
                                DefaultVibrateTimings = true,
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
                        if (sendResponse.IsSuccess) continue;

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
                            ChannelId = "himo_calls_v2",
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
