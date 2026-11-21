using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts.Application;
using FuelClub.Api.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Accounts
{
    public static class AccountsServiceCollectionExtensions
    {
        public static IServiceCollection AddAccounts(this IServiceCollection services)
        {
            services.AddScoped<IAccountsService, IdentityAccountsService>();
            services.AddHostedService<RoleInitializer>();
            services.AddHostedService<AdminAccountProvisioner>();
            services.AddIdentity<ApplicationUser, IdentityRole>(options =>
                {
                    options.User.RequireUniqueEmail = true;
                    options.Password.RequiredLength = 10;
                })
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

            services.AddAuthorization();
            services.ConfigureApplicationCookie(options =>
            {
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

            return services;
        }
    }
}
