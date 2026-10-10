using Microsoft.AspNetCore.SignalR.Client;
using Himo.Models;
using MessageDto = Himo.Services.HimoApiClient.MessageDto;

namespace Himo.Services;

public sealed class HimoRealtimeService
{
    private readonly HimoApiClient _api;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler<MessageDto>? MessageReceived;
    public event EventHandler<(Guid UserId, bool IsOnline)>? UserPresenceChanged;
    public event EventHandler<(Guid UserId, Guid ConversationId, bool IsTyping)>? UserTypingChanged;
    public event EventHandler<(Guid MessageId, Guid UserId, string Status)>? MessageDeliveryChanged;
    public event EventHandler<MessageDto>? MessageEdited;
    public event EventHandler<MessageDto>? MessageDeleted;
    public event EventHandler? Reconnected;
    public event EventHandler<CallSignalMessage>? CallSignalReceived;

    public bool IsConnected =>
        _connection?.State == HubConnectionState.Connected;

    public HimoRealtimeService(HimoApiClient api)
    {
        _api = api;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_api.HasToken)
            return;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var existing = _connection;

            // لا تعيد إنشاء الاتصال إذا كان يعمل أو في طور الاتصال/إعادة الاتصال.
            if (existing is not null)
            {
                if (existing.State == HubConnectionState.Connected ||
                    existing.State == HubConnectionState.Connecting ||
                    existing.State == HubConnectionState.Reconnecting)
                {
                    return;
                }

                // يوجد اتصال قديم لكنه متوقف.
                _connection = null;

                try
                {
                    await existing.DisposeAsync();
                }
                catch
                {
                    // لا نسمح لاتصال قديم متوقف بمنع إنشاء الاتصال الجديد.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            var baseUri = new Uri(_api.BaseUrl);
            var hubUri = new Uri(baseUri, "hubs/chat");

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUri, options =>
                {
                    options.AccessTokenProvider = () =>
                        Task.FromResult(_api.GetAccessToken());
                })
                .WithAutomaticReconnect(
                    new[]
                    {
                        TimeSpan.Zero,
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(5),
                        TimeSpan.FromSeconds(15)
                    })
                .Build();

            RegisterHandlers(connection);

            _connection = connection;

            try
            {
                await connection.StartAsync(cancellationToken);
            }
            catch
            {
                // إذا فشل الاتصال، ننظف هذا الاتصال فقط.
                if (ReferenceEquals(_connection, connection))
                    _connection = null;

                try
                {
                    await connection.DisposeAsync();
                }
                catch
                {
                    // تجاهل فشل تنظيف الاتصال.
                }

                // HTTP incremental sync يبقى مسار الاحتياط.
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RegisterHandlers(HubConnection connection)
    {
        connection.On<MessageDto>(
            "MessageReceived",
            message =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MessageReceived?.Invoke(this, message);
                });
            });

        connection.On<Guid, bool>(
            "UserPresenceChanged",
            (userId, isOnline) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UserPresenceChanged?.Invoke(
                        this,
                        (userId, isOnline));
                });
            });

        connection.On<Guid, Guid, bool>(
            "UserTypingChanged",
            (userId, conversationId, isTyping) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UserTypingChanged?.Invoke(
                        this,
                        (userId, conversationId, isTyping));
                });
            });

        connection.On<MessageDto>(
            "MessageEdited",
            message =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MessageEdited?.Invoke(this, message);
                });
            });

        connection.On<MessageDto>(
            "MessageDeleted",
            message =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MessageDeleted?.Invoke(this, message);
                });
            });

        connection.On<CallSignalMessage>(
            "CallSignalReceived",
            signal =>
            {
                var hasEnvelope = CallSignalEnvelope.TryParse(signal.Payload, out var envelope);
                System.Diagnostics.Debug.WriteLine(
                    $"[Himo Realtime] RX {signal.Type}; conversation={signal.ConversationId:D}; " +
                    $"sender={signal.SenderUserId:D}; payloadLength={signal.Payload?.Length ?? 0}; " +
                    $"envelope={hasEnvelope}; envelopeCallId={(hasEnvelope ? envelope.CallId.ToString("D") : "raw")}");

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Realtime] Dispatch RX {signal.Type} to call handlers; " +
                        $"conversation={signal.ConversationId:D}; " +
                        $"payloadLength={signal.Payload?.Length ?? 0}; " +
                        $"envelopeCallId={(hasEnvelope ? envelope.CallId.ToString("D") : "raw")}");
                    CallSignalReceived?.Invoke(this, signal);
                });
            });

        connection.On<Guid, Guid, string>(
            "MessageDeliveryChanged",
            (messageId, userId, status) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    MessageDeliveryChanged?.Invoke(
                        this,
                        (messageId, userId, status));
                });
            });

        connection.Reconnecting += _ =>
        {
            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Reconnected?.Invoke(this, EventArgs.Empty);
            });

            return Task.CompletedTask;
        };

        connection.Closed += _ =>
        {
            // لا ننشئ اتصالاً جديداً من هنا.
            // AutomaticReconnect يتولى حالات انقطاع الشبكة المؤقتة.
            // إذا أصبح الاتصال Closed نهائياً، StartAsync في الصفحة التالية
            // يستطيع إنشاء اتصال جديد.
            return Task.CompletedTask;
        };
    }


    /// <summary>
    /// Ensures SignalR is connected before a call is accepted or signaling starts.
    /// The call-signal send path also retries connections independently.
    /// </summary>
    public async Task EnsureConnectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_api.HasToken)
            throw new InvalidOperationException("لم يتم تسجيل الدخول إلى الخادم.");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var nextStartAttempt = DateTimeOffset.MinValue;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = _connection;
            if (connection?.State == HubConnectionState.Connected)
                return;

            var now = DateTimeOffset.UtcNow;
            if ((connection is null || connection.State == HubConnectionState.Disconnected) &&
                now >= nextStartAttempt)
            {
                nextStartAttempt = now.AddSeconds(2);
                try
                {
                    await StartAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Realtime] EnsureConnectedAsync attempt failed: {ex}");
                }
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                var finalState = _connection?.State.ToString() ?? "NoConnection";
                throw new TimeoutException(
                    $"تعذر الاتصال بخادم المكالمات خلال 15 ثانية (حالة الاتصال: {finalState}).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    public async Task SendCallSignalAsync(
        Guid conversationId,
        string type,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        // Never silently discard SDP or ICE when SignalR is reconnecting.
        // Wait briefly for the existing automatic-reconnect path, and restart
        // a fully disconnected connection when possible.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var nextStartAttempt = DateTimeOffset.MinValue;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connection = _connection;
            if (connection?.State == HubConnectionState.Connected)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Realtime] TX {type}; conversation={conversationId:D}; " +
                        $"payloadLength={payload?.Length ?? 0}; state=Connected");

                    // InvokeAsync waits for the hub method to finish. This makes
                    // signaling failures visible to the caller instead of treating
                    // a queued/local send as a confirmed signal.
                    await connection.InvokeAsync(
                        "SendCallSignal",
                        conversationId,
                        type,
                        payload,
                        cancellationToken);

                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Realtime] TX complete {type}; conversation={conversationId:D}");
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Realtime] TX failed {type}; conversation={conversationId:D}; " +
                        $"state={connection.State}; error={ex.Message}");
                    throw;
                }
            }

            var now = DateTimeOffset.UtcNow;
            if ((connection is null || connection.State == HubConnectionState.Disconnected) &&
                now >= nextStartAttempt)
            {
                nextStartAttempt = now.AddSeconds(2);
                try
                {
                    await StartAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Himo Realtime] Reconnect attempt before {type} failed: {ex.Message}");
                }
            }

            if (now >= deadline)
            {
                var finalState = _connection?.State.ToString() ?? "NoConnection";
                throw new TimeoutException(
                    $"Cannot send call signal '{type}': SignalR did not become connected within 15 seconds (state={finalState}).");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    public async Task<bool> GetPresenceAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var connection = _connection;

        if (connection?.State != HubConnectionState.Connected)
            return false;

        try
        {
            return await connection.InvokeAsync<bool>(
                "GetPresence",
                conversationId,
                cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkMessageDeliveredAsync(
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var connection = _connection;

        if (connection?.State != HubConnectionState.Connected)
            return;

        try
        {
            await connection.SendAsync(
                "MarkMessageDelivered",
                messageId,
                cancellationToken);
        }
        catch
        {
            // HTTP synchronization remains the fallback.
        }
    }

    public async Task SetTypingAsync(
        Guid conversationId,
        bool isTyping,
        CancellationToken cancellationToken = default)
    {
        var connection = _connection;

        if (connection?.State != HubConnectionState.Connected)
            return;

        try
        {
            await connection.SendAsync(
                isTyping ? "StartTyping" : "StopTyping",
                conversationId,
                cancellationToken);
        }
        catch
        {
            // Typing state is best-effort.
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();

        try
        {
            var connection = _connection;

            if (connection is null)
                return;

            // إزالة المرجع أولاً حتى لا تبدأ صفحة أخرى باستخدام الاتصال
            // أثناء عملية الإغلاق.
            _connection = null;

            try
            {
                if (connection.State != HubConnectionState.Disconnected)
                    await connection.StopAsync();
            }
            catch
            {
                // لا نسمح لفشل إغلاق WebSocket بتعطيل الصفحة.
            }

            try
            {
                await connection.DisposeAsync();
            }
            catch
            {
                // تجاهل أخطاء التنظيف.
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record CallSignalMessage(Guid ConversationId, Guid SenderUserId, string Type, string? Payload);
