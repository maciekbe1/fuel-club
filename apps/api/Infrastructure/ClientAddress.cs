using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// The browser reaches the API through the Next rewrite, so the socket address is the Next server.
/// X-Forwarded-For is trusted only from known proxies: loopback by default (Next on the same host),
/// more in ForwardedHeaders:KnownProxies. Only the last hop is taken (ForwardLimit 1); the outermost
/// proxy must overwrite a client-supplied header (hosting docs).
/// </summary>
public static class ClientAddress
{
    public const string Section = "ForwardedHeaders";

    public static IServiceCollection AddClientAddress(
        this IServiceCollection services,
        IConfiguration configuration
    ) =>
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;
            foreach (
                var proxy in configuration.GetSection($"{Section}:KnownProxies").Get<string[]>() ??
                    []
            )
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
}
