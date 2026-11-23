using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.DemoData;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant;
using FuelClub.Api.Modules.Knowledge;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Observability;
using FuelClub.Api.Modules.Rewards;
using FuelClub.Api.Modules.Support;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddCsrf();

// Numbers must be JSON numbers ("10" is rejected); OpenAPI then types them as numbers only.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = System
        .Text
        .Json
        .Serialization
        .JsonNumberHandling
        .Strict
);
builder.Services.AddIdempotency(builder.Configuration);
builder.Services.AddClientAddress(builder.Configuration);
builder.Services.AddPersistentDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddOpenApi(options =>
{
    options.AddIdempotencyHeader();
    options.AddSupportHubContract();
});
builder.Services.AddAccounts();
builder.Services.AddLoyalty(builder.Configuration);
builder.Services.AddRewards(builder.Configuration);
builder.Services.AddKnowledge();
builder.Services.AddAssistant(builder.Configuration);
builder.Services.AddSupport(builder.Configuration);
builder.Services.AddTelemetry(builder.Configuration);
builder.Services.AddObservability(builder.Configuration);
builder.Services.AddHostedService<DemoAccountsSeeder>();

var app = builder.Build();

// One-off maintenance command; hosted services (seeding, provisioning) do not start in this mode.
if (args is [DemoPasswordReset.Command])
{
    Environment.ExitCode = await DemoPasswordReset.RunAsync(
        app.Services,
        app.Environment,
        app.Configuration
    );
    return;
}

app.UseForwardedHeaders();
app.UseSecurityEvents();
app.UseHttpsRedirection();
app.UseMiddleware<SupportHubOriginGuard>();
app.UseAuthentication();
app.UseAuthorization();

// After authorization: policies partition by the authenticated user, and rejected callers never count.
app.UseRateLimiter();
app.UseIdempotencyBodyBuffering();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Browser UI for trying endpoints: /scalar (reads /openapi/v1.json). Development only.
    app.MapScalarApiReference(options => options.WithTitle("Fuel Club API"));
    app.MapHealthChecks("/health/db");
}

app.MapCsrfEndpoint();
app.MapAccountsEndpoints();
app.MapAdminEndpoints();
app.MapLoyaltyEndpoints();
app.MapRewardsEndpoints();
app.MapAssistantEndpoints();
app.MapSupportEndpoints();
app.MapObservabilityEndpoints();

foreach (var additional in app.Services.GetServices<IAdditionalEndpoints>())
    additional.Map(app);

app.Run();

public partial class Program { }
