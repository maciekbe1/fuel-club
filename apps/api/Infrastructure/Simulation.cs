using System.Text.RegularExpressions;
using FuelClub.Api.Modules.Assistant.Model;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// Synthetic traffic (ADR 0017). Set only by the configuration of a separate demo instance
/// (Development, its own database): no request can switch it on or change the run.
/// </summary>
public sealed class SimulationOptions
{
    public const string Section = "Simulation";

    public bool Enabled { get; set; }

    /// <summary>Marks every turn and conversation of this instance; the simulator checks it.</summary>
    public string? RunId { get; set; }
}

public sealed partial class SimulationOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SimulationOptions>
{
    public ValidateOptionsResult Validate(string? name, SimulationOptions o)
    {
        if (!o.Enabled)
            return ValidateOptionsResult.Success;
        if (!environment.IsDevelopment())
            return ValidateOptionsResult.Fail("Simulation:Enabled is allowed only in Development.");
        return o.RunId is { } run && RunIdFormat().IsMatch(run)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Simulation:RunId is required with Simulation:Enabled: 1-40 characters a-z, 0-9, '-'."
            );
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,39}$")]
    public static partial Regex RunIdFormat();
}

/// <summary>What the simulator verifies before it sends anything: the run and the configured model.</summary>
public sealed record SimulationResponse(string RunId, string Provider, string Model, bool Demo);

public static class Simulation
{
    public static IServiceCollection AddSimulation(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<SimulationOptions>()
            .Bind(configuration.GetSection(SimulationOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SimulationOptions>, SimulationOptionsValidator>();
        return services;
    }

    /// <summary>Mapped only on a simulation instance; elsewhere the path does not exist.</summary>
    public static void MapSimulationEndpoint(this WebApplication app)
    {
        if (
            app.Services.GetRequiredService<IOptions<SimulationOptions>>().Value
            is not { Enabled: true, RunId: { } runId }
        )
            return;
        app.MapGet(
                "/api/simulation",
                (IChatModel model) =>
                    TypedResults.Ok(
                        new SimulationResponse(runId, model.Provider, model.Model, model.Demo)
                    )
            )
            .WithTags("Simulation")
            .AllowAnonymous();
    }
}
