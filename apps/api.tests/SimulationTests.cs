using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SimulationTests(ApiFactory factory)
{
    [Fact]
    public async Task Without_simulation_the_endpoint_does_not_exist()
    {
        var response = await factory.CreateClient().GetAsync("/api/simulation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_simulation_instance_reports_its_run_and_configured_model()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Simulation:Enabled"] = "true",
                            ["Simulation:RunId"] = "demo-1",
                        }
                    )
            )
        );

        var simulation = await host.CreateClient()
            .GetFromJsonAsync<SimulationResponse>("/api/simulation");

        Assert.Equal(new SimulationResponse("demo-1", "demo", "demo-rules", true), simulation);
    }

    [Theory]
    [InlineData("Development", true, "demo-1", true)]
    [InlineData("Development", false, null, true)]
    [InlineData("Production", true, "demo-1", false)]
    [InlineData("Staging", true, "demo-1", false)]
    [InlineData("Development", true, null, false)]
    [InlineData("Development", true, "Demo 1", false)]
    [InlineData("Development", true, "-x", false)]
    public void Simulation_runs_only_in_development_with_a_valid_run_id(
        string environment,
        bool enabled,
        string? runId,
        bool valid
    )
    {
        var result = new SimulationOptionsValidator(new Environment(environment)).Validate(
            null,
            new SimulationOptions { Enabled = enabled, RunId = runId }
        );

        Assert.Equal(valid, result.Succeeded);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
