using Microsoft.AspNetCore.SignalR.Client;
using Himo.Models;
using MessageDto = Himo.Services.HimoApiClient.MessageDto;

namespace Himo.Services;

public sealed class HimoRealtimeService
{
    private readonly HimoApiClient _api;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static readonly TimeSpan ConnectionTimeout =
        TimeSpan.FromSeconds(12);

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

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_api.HasToken)
            return;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var existing = _connection;

            // إذا كان الاتصال يعمل بالفعل أو في طور الاتصال/
            // إعادة الاتصال فلا ننشئ اتصالًا ثانيًا.
            if (existing is not null)
            {
                if (existing.State == HubConnectionState.Connected ||
                    existing.State == HubConnectionState.Connecting ||
                    existing.State == HubConnectionState.Reconnecting)
                {
                    return;
                }

                _connection = null;

                try
                {
                    await existing.DisposeAsync();
                }
                catch
                {
                    // الاتصال القديم متوقف، لذلك لا نسمح لفشل تنظيفه
                    // بمنع إنشاء اتصال جديد.
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            Uri hubUri;

            try
            {
                var baseUri = new Uri(_api.BaseUrl);
                hubUri = new Uri(baseUri, "hubs/chat");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"عنوان خادم المحادثات غير صالح. BaseUrl = '{_api.BaseUrl}'",
                    ex);
            }

            var connection = new HubConnectionBuilder()
                .WithUrl(
                    hubUri,
                    options =>
                    {
                        options.AccessTokenProvider = () =>
                        {
                            var token = _api.GetAccessToken();

                            if (string.IsNullOrWhiteSpace(token))
                            {
                                throw new InvalidOperationException(
                                    "رمز الدخول غير موجود أو منتهي الصلاحية.");
                            }

                            return Task.FromResult<string?>(token);
                        };
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
                using var timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                timeoutCts.CancelAfter(ConnectionTimeout);

                await connection.StartAsync(timeoutCts.Token);

                // نتأكد من أن الاتصال وصل فعلًا إلى الحالة Connected.
                if (connection.State != HubConnectionState.Connected)
                {
                    throw new InvalidOperationException(
                        $"فشل اتصال SignalR. الحالة الحالية: {connection.State}");
                }
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                if (ReferenceEquals(_connection, connection))
                    _connection = null;

                try
                {
                    await connection.DisposeAsync();
                }
                catch
                {
                    // تجاهل فشل التنظيف.
                }

                throw new TimeoutException(
                    $"انتهت مهلة الاتصال بخادم المكالمات بعد {ConnectionTimeout.TotalSeconds:0} ثانية.");
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_connection, connection))
                    _connection = null;

                try
                {
                    await connection.DisposeAsync();
                }
                catch
                {
                    // تجاهل فشل التنظيف.
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[HimoRealtimeService] SignalR StartAsync failed: {ex}");

                throw;
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
                    try
                    {
                        MessageReceived?.Invoke(this, message);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] MessageReceived handler failed: {ex}");
                    }
                });
            });

        connection.On<Guid, bool>(
            "UserPresenceChanged",
            (userId, isOnline) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        UserPresenceChanged?.Invoke(
                            this,
                            (userId, isOnline));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] UserPresenceChanged handler failed: {ex}");
                    }
                });
            });

        connection.On<Guid, Guid, bool>(
            "UserTypingChanged",
            (userId, conversationId, isTyping) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        UserTypingChanged?.Invoke(
                            this,
                            (userId, conversationId, isTyping));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] UserTypingChanged handler failed: {ex}");
                    }
                });
            });

        connection.On<MessageDto>(
            "MessageEdited",
            message =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        MessageEdited?.Invoke(this, message);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] MessageEdited handler failed: {ex}");
                    }
                });
            });

        connection.On<MessageDto>(
            "MessageDeleted",
            message =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        MessageDeleted?.Invoke(this, message);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] MessageDeleted handler failed: {ex}");
                    }
                });
            });

        connection.On<CallSignalMessage>(
            "CallSignalReceived",
            signal =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        CallSignalReceived?.Invoke(this, signal);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] CallSignalReceived handler failed: {ex}");
                    }
                });
            });

        connection.On<Guid, Guid, string>(
            "MessageDeliveryChanged",
            (messageId, userId, status) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        MessageDeliveryChanged?.Invoke(
                            this,
                            (messageId, userId, status));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[HimoRealtimeService] MessageDeliveryChanged handler failed: {ex}");
                    }
                });
            });

        connection.Reconnecting += error =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] SignalR reconnecting. " +
                $"Error: {error?.ToString() ?? "none"}");

            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] SignalR reconnected. " +
                $"ConnectionId: {connectionId ?? "null"}");

            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    Reconnected?.Invoke(
                        this,
                        EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[HimoRealtimeService] Reconnected handler failed: {ex}");
                }
            });

            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] SignalR connection closed. " +
                $"Error: {error?.ToString() ?? "none"}");

            return Task.CompletedTask;
        };
    }

    public async Task EnsureConnectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_api.HasToken)
        {
            throw new InvalidOperationException(
                "لم يتم تسجيل الدخول إلى الخادم.");
        }

        // إذا كان الاتصال يعمل فلا داعي لأي عملية إضافية.
        if (_connection?.State == HubConnectionState.Connected)
            return;

        try
        {
            // محاولة اتصال واحدة فقط.
            // AutomaticReconnect سيتولى الانقطاعات بعد نجاح الاتصال.
            await StartAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] EnsureConnectedAsync failed: {ex}");

            throw new InvalidOperationException(
                BuildConnectionErrorMessage(ex),
                ex);
        }

        if (_connection?.State == HubConnectionState.Connected)
            return;

        throw new InvalidOperationException(
            $"تعذر الاتصال بخادم المكالمات. حالة الاتصال الحالية: " +
            $"{_connection?.State.ToString() ?? "غير موجود"}.");
    }

    private static string BuildConnectionErrorMessage(Exception exception)
    {
        var root = exception;

        while (root.InnerException is not null)
            root = root.InnerException;

        var message = root.Message?.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            return "تعذر الاتصال بخادم المكالمات. " +
                   "تحقق من اتصال الإنترنت وإعدادات الخادم.";
        }

        return
            $"تعذر الاتصال بخادم المكالمات.\n\n" +
            $"السبب: {message}";
    }

    public async Task SendCallSignalAsync(
        Guid conversationId,
        string type,
        string? payload = null,
        CancellationToken cancellationToken = default)
    {
        if (!_api.HasToken)
        {
            throw new InvalidOperationException(
                "لم يتم تسجيل الدخول إلى الخادم.");
        }

        await EnsureConnectedAsync(cancellationToken);

        var connection = _connection;

        if (connection?.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "اتصال خادم المكالمات غير متاح حاليًا.");
        }

        await connection.SendAsync(
            "SendCallSignal",
            conversationId,
            type,
            payload,
            cancellationToken);
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] GetPresenceAsync failed: {ex}");

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] MarkMessageDeliveredAsync failed: {ex}");
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HimoRealtimeService] SetTypingAsync failed: {ex}");
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

            // إزالة المرجع أولًا حتى لا تبدأ عملية أخرى باستخدام
            // الاتصال أثناء الإغلاق.
            _connection = null;

            try
            {
                if (connection.State != HubConnectionState.Disconnected)
                {
                    using var timeoutCts =
                        new CancellationTokenSource(
                            TimeSpan.FromSeconds(5));

                    await connection.StopAsync(
                        timeoutCts.Token);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[HimoRealtimeService] StopAsync failed: {ex}");
            }

            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[HimoRealtimeService] DisposeAsync failed: {ex}");
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed record CallSignalMessage(
    Guid ConversationId,
    Guid SenderUserId,
    string Type,
    string? Payload);