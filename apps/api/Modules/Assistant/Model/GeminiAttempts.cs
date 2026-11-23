using System.Runtime.CompilerServices;

namespace FuelClub.Api.Modules.Assistant.Model;

/// <summary>
/// Counts the attempts of one model step that got no HTTP answer (connection lost, cut off). The
/// handler sits inside the retry pipeline, so it sees every attempt; the count flows with the
/// step's async context. Such an attempt may have been billed while its answer was lost, so the
/// retried step's usage is not the whole cost (ADR 0018). An error status (429, 5xx) is an answer
/// and is not billed.
/// </summary>
public sealed class GeminiAttempts : DelegatingHandler
{
    private static readonly AsyncLocal<StrongBox<int>?> Current = new();

    /// <summary>Starts counting for the calling step; read Value after the request.</summary>
    public static StrongBox<int> Begin() => Current.Value = new StrongBox<int>();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            if (Current.Value is { } lost)
                Interlocked.Increment(ref lost.Value);
            throw;
        }
    }
}
