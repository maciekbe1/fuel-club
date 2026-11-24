using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// The browser reaches the API through the Next rewrite, so the socket address is the Next server.
/// X-Forwarded-For is trusted only from known proxies: loopback by default (Next on the same host),
/// more in ForwardedHeaders:KnownProxies (addresses) and KnownNetworks (CIDR). ForwardLimit (1 by
/// default) is how many hops are read from the right; each must come from a known proxy, so the
/// first unknown address is the client. Cloud Run puts several private hops between the edge and
/// the API (ADR 0010). Next drops a client-supplied header unless TRUST_FORWARDED_FOR is set behind
/// an edge proxy that sets it (ADR 0009).
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
        var limit = configuration.GetValue<int?>($"{Section}:ForwardLimit") ?? 1;
        if (limit is < 1 or > 10)
            throw new InvalidOperationException($"{Section}:ForwardLimit must be 1-10.");
        return services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Proto: behind an HTTPS edge the request counts as HTTPS, so the session and CSRF
            // cookies (SecurePolicy SameAsRequest) are Secure although Next calls the API over HTTP.
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = limit;
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
