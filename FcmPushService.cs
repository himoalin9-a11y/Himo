using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

sealed class FcmPushService
{
    private readonly ILogger<FcmPushService> _logger;
    private readonly PostgresStore _store;
    private readonly FirebaseMessaging? _messaging;

    public bool IsEnabled => _messaging is not null;

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
            _logger.LogInformation("Firebase Cloud Messaging is enabled.");
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
        CancellationToken cancellationToken = default)
    {
        if (_messaging is null || tokens.Count == 0)
            return;

        var cleanTokens = tokens
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (cleanTokens.Count == 0)
            return;

        var data = new Dictionary<string, string>
        {
            ["conversation_id"] = conversationId.ToString("D")
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
                                ChannelId = "himo_messages"
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
}
