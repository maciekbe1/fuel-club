using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.DemoData;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Rewards;

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
builder.Services.AddApiRateLimiting();
builder.Services.AddOpenApi(options => options.AddIdempotencyHeader());
builder.Services.AddAccounts();
builder.Services.AddLoyalty(builder.Configuration);
builder.Services.AddRewards(builder.Configuration);
builder.Services.AddAssistant(builder.Configuration);
builder.Services.AddTelemetry(builder.Configuration);
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

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// After authorization: policies partition by the authenticated user, and rejected callers never count.
app.UseRateLimiter();
app.UseIdempotencyBodyBuffering();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapDatabaseHealthEndpoint();
}

app.MapCsrfEndpoint();
app.MapAccountsEndpoints();
app.MapAdminEndpoints();
app.MapLoyaltyEndpoints();
app.MapRewardsEndpoints();
app.MapAssistantEndpoints();

foreach (var additional in app.Services.GetServices<IAdditionalEndpoints>())
    additional.Map(app);

app.Run();

public partial class Program { }
