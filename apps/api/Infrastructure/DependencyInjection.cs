using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration
        )
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(
                    configuration.GetConnectionString("DefaultConnection")
                        ?? throw new InvalidOperationException(
                            "Connection string 'DefaultConnection' is not configured."
                        )
                )
            );
            services.AddHostedService<DatabaseSchemaVerifier>();

            return services;
        }
    }
}
