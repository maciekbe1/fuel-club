using System.Text.Json;

namespace FuelClub.Api.Modules.Assistant.Chat;

/// <summary>
/// Writes SSE frames ("event:" + one-line JSON "data:" + blank line) and heartbeat comments. Writes are
/// serialized: the heartbeat timer and the turn share the response.
/// </summary>
public sealed class SseWriter(HttpResponse response, JsonSerializerOptions json) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task EventAsync<T>(string name, T payload, CancellationToken cancellationToken) =>
        WriteAsync(
            $"event: {name}\ndata: {JsonSerializer.Serialize(payload, json)}\n\n",
            cancellationToken
        );

    public Task HeartbeatAsync(CancellationToken cancellationToken) =>
        WriteAsync(": ping\n\n", cancellationToken);

    private async Task WriteAsync(string frame, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await response.WriteAsync(frame, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}
