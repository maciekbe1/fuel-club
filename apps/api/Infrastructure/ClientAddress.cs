using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// The browser reaches the API through the Next rewrite, so the socket address is the Next server.
/// X-Forwarded-For is trusted only from known proxies: loopback by default (Next on the same host),
/// more in ForwardedHeaders:KnownProxies (addresses) and KnownNetworks (CIDR). Only the last hop
/// is taken (ForwardLimit 1). Next drops a client-supplied header unless TRUST_FORWARDED_FOR is set
/// behind an edge proxy that sets it (ADR 0009).
/// </summary>
public static class ClientAddress
{
    public const string Section = "ForwardedHeaders";

    public static IServiceCollection AddClientAddress(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        // Parsed now: a malformed value stops the start instead of failing every request later.
        var proxies = Parse(configuration, "KnownProxies", IPAddress.Parse);
        var networks = Parse(configuration, "KnownNetworks", System.Net.IPNetwork.Parse);
        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Proto: behind an HTTPS edge the request counts as HTTPS, so the session and CSRF
            // cookies (SecurePolicy SameAsRequest) are Secure although Next calls the API over HTTP.
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            foreach (var proxy in proxies)
                options.KnownProxies.Add(proxy);
            // A private hosting network (Next in another container) has no fixed address: a range.
            foreach (var network in networks)
                options.KnownIPNetworks.Add(network);
        });
    }

    private static List<T> Parse<T>(
        IConfiguration configuration,
        string key,
        Func<string, T> parse
    ) =>
        (configuration.GetSection($"{Section}:{key}").Get<string[]>() ?? [])
            .Select(value =>
            {
                try
                {
                    return parse(value.Trim());
                }
                catch (FormatException)
                {
                    throw new InvalidOperationException(
                        $"{Section}:{key} has an invalid value: '{value}'."
                    );
                }
            })
            .ToList();
}
