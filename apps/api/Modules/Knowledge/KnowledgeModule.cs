namespace FuelClub.Api.Modules.Knowledge;

/// <summary>The approved program rules and their search, read by the assistant (P14).</summary>
public static class KnowledgeModule
{
    public static IServiceCollection AddKnowledge(this IServiceCollection services) =>
        services.AddScoped<ProgramRulesSearch>();
}
