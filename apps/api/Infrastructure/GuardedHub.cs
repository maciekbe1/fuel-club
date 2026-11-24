namespace FuelClub.Api.Infrastructure;

/// <summary>
/// A SignalR hub whose negotiation and WebSocket upgrade must come from an allowed Origin
/// (Support:HubAllowedOrigins, checked by HubOriginGuard). Each module registers its own hub.
/// </summary>
public sealed record GuardedHub(string Path, string ErrorCode);
