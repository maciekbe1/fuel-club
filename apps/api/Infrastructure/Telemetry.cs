using FuelClub.Api.Modules.Assistant.Tracing;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// Optional OpenTelemetry export of the assistant spans (OTLP over HTTP, e.g. to a local Phoenix).
/// Off unless Telemetry:OtlpEndpoint is set. Only the assistant's own source is exported: no HTTP
/// instrumentation, so URLs, headers and bodies never leave the process. Export runs in the
/// background (batch processor): an unreachable collector does not slow down or break a chat.
/// Exporter headers (an API key) come only from OTEL_EXPORTER_OTLP_HEADERS.
/// </summary>
public static class Telemetry
{
    public const string EndpointKey = "Telemetry:OtlpEndpoint";

    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        var endpoint = configuration[EndpointKey];
        if (string.IsNullOrWhiteSpace(endpoint))
            return services;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"{EndpointKey} must be an absolute URL.");

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource =>
                resource
                    .AddService("fuelclub-api")
                    // Phoenix groups traces by this project name.
                    .AddAttributes([new("openinference.project.name", "fuel-club")])
            )
            .WithTracing(tracing =>
                tracing
                    .AddSource(AssistantTelemetry.SourceName)
                    .AddOtlpExporter(exporter =>
                    {
                        exporter.Endpoint = uri;
                        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    })
            );
        return services;
    }
}
