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
    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public HimoRealtimeService(HimoApiClient api) => _api = api;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_api.HasToken) return;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected) return;
            await StopCoreAsync();
            var baseUri = new Uri(_api.BaseUrl);
            var hubUri = new Uri(baseUri, "hubs/chat");
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUri, options =>
                {
                    options.AccessTokenProvider = () => Task.FromResult(_api.GetAccessToken());
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) })
                .Build();

            _connection.On<MessageDto>("MessageReceived", message =>
            {
                MainThread.BeginInvokeOnMainThread(() => MessageReceived?.Invoke(this, message));
            });

            _connection.Closed += async _ =>
            {
                // Automatic reconnect handles transient network loss. A closed
                // connection is left stopped until the next page appearance.
                await Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);
        }
        catch
        {
            await StopCoreAsync();
            // HTTP incremental sync remains the fallback path.
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _gate.Release(); }
    }

    private async Task StopCoreAsync()
    {
        if (_connection is null) return;
        try { await _connection.StopAsync(); } catch { }
        try { await _connection.DisposeAsync(); } catch { }
        _connection = null;
    }
}
