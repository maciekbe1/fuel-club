using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Accounts.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AccountsApiTests(ApiFactory factory)
{
    private static readonly string Password = "Valid-Test-Password1!";

    [Fact]
    public async Task Register_returns_field_errors_for_missing_fields()
    {
        using var client = factory.CreateClient();

        var response = await client.PostWithCsrfAsync(
            "/api/auth/register",
            new
            {
                email = "",
                password = "",
                displayName = "",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("accounts.required_fields", body!.Code);
        Assert.Contains("email", body.Errors!.Keys);
        Assert.Contains("password", body.Errors.Keys);
        Assert.Contains("displayName", body.Errors.Keys);
        Assert.All(
            body.Errors.Values.SelectMany(errors => errors),
            error =>
            {
                Assert.False(string.IsNullOrWhiteSpace(error.Field));
                Assert.Equal("accounts.required_fields", error.Code);
            }
        );
    }

    [Fact]
    public async Task Login_rejects_missing_fields_with_field_errors()
    {
        using var client = factory.CreateClient();

        var response = await client.PostWithCsrfAsync(
            "/api/auth/login",
            new { email = "", password = "" }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("accounts.required_fields", body!.Code);
        Assert.Contains("email", body.Errors!.Keys);
        Assert.Contains("password", body.Errors.Keys);
    }

    [Fact]
    public async Task Account_session_supports_login_me_logout_and_rejects_bad_credentials()
    {
        var email = $"integration-{Guid.NewGuid():N}@example.test";
        try
        {
            using var client = factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    HandleCookies = true,
                    AllowAutoRedirect = false,
                }
            );

            var unauthenticatedMe = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedMe.StatusCode);

            var register = await client.PostWithCsrfAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = Password,
                    displayName = "Integration Test",
                    role = "Admin",
                }
            );
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);

            var wrongLogin = await client.PostWithCsrfAsync(
                "/api/auth/login",
                new { email, password = "Wrong-password1!" }
            );
            Assert.Equal(HttpStatusCode.Unauthorized, wrongLogin.StatusCode);

            var login = await client.PostWithCsrfAsync(
                "/api/auth/login",
                new { email, password = Password }
            );
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
            Assert.Contains(login.Headers, header => header.Key == "Set-Cookie");

            var authenticatedMe = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, authenticatedMe.StatusCode);
            var profile = await authenticatedMe.Content.ReadFromJsonAsync<ProfileResponse>();
            Assert.Equal(email, profile!.Email);
            Assert.Equal("Integration Test", profile.DisplayName);
            Assert.Contains("User", profile.Roles);
            Assert.DoesNotContain("Admin", profile.Roles);

            var logout = await client.PostWithCsrfAsync("/api/auth/logout", body: null);
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
            Assert.Contains(logout.Headers, header => header.Key == "Set-Cookie");

            var meAfterLogout = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, meAfterLogout.StatusCode);
        }
        finally
        {
            await factory.DeleteTestUserAsync(email);
        }
    }

    [Fact]
    public async Task Admin_endpoint_requires_admin_role()
    {
        var userEmail = $"role-check-user-{Guid.NewGuid():N}@example.test";
        var adminEmail = $"role-check-admin-{Guid.NewGuid():N}@example.test";
        try
        {
            using var anonymousClient = factory.CreateClient();
            var unauthorized = await anonymousClient.GetAsync("/api/admin/access");
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            using var userClient = factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    HandleCookies = true,
                }
            );
            var register = await userClient.PostWithCsrfAsync(
                "/api/auth/register",
                new
                {
                    email = userEmail,
                    password = Password,
                    displayName = "Regular User",
                }
            );
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            var userLogin = await userClient.PostWithCsrfAsync(
                "/api/auth/login",
                new { email = userEmail, password = Password }
            );
            Assert.Equal(HttpStatusCode.NoContent, userLogin.StatusCode);
            var forbidden = await userClient.GetAsync("/api/admin/access");
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

            using var adminClient = factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                {
                    HandleCookies = true,
                }
            );
            await factory.CreateTestUserAsync(adminEmail, Password, "Admin");
            var adminLogin = await adminClient.PostWithCsrfAsync(
                "/api/auth/login",
                new { email = adminEmail, password = Password }
            );
            Assert.Equal(HttpStatusCode.NoContent, adminLogin.StatusCode);
            var allowed = await adminClient.GetAsync("/api/admin/access");
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        }
        finally
        {
            await factory.DeleteTestUserAsync(userEmail);
            await factory.DeleteTestUserAsync(adminEmail);
        }
    }

    [Fact]
    public async Task Admin_provisioning_refuses_to_promote_an_existing_regular_account()
    {
        var email = $"role-check-existing-{Guid.NewGuid():N}@example.test";
        try
        {
            using var client = factory.CreateClient();
            var registration = await client.PostWithCsrfAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = Password,
                    displayName = "Existing User",
                }
            );
            Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["BootstrapAdmin:Email"] = email,
                        ["BootstrapAdmin:Password"] = Password,
                    }
                )
                .Build();
            var provisioner = new AdminAccountProvisioner(
                factory.Services.GetRequiredService<IServiceScopeFactory>(),
                configuration
            );

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                provisioner.StartAsync(CancellationToken.None)
            );
            Assert.Contains("already exists without the Admin role", error.Message);

            using var scope = factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.False(await users.IsInRoleAsync(user!, RoleNames.Admin));
            Assert.True(await users.IsInRoleAsync(user!, RoleNames.User));
        }
        finally
        {
            await factory.DeleteTestUserAsync(email);
        }
    }

    [Fact]
    public async Task Mutating_request_without_csrf_token_is_rejected()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { email = "", password = "" }
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProblemResponse>();
        Assert.Equal("csrf.invalid", body!.Code);
    }

    [Fact]
    public async Task Csrf_token_issued_before_login_is_rejected_after_login()
    {
        var email = $"integration-{Guid.NewGuid():N}@example.test";
        try
        {
            using var client = factory.CreateClient();
            await client.PostWithCsrfAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = Password,
                    displayName = "Csrf Test",
                }
            );
            var anonymousToken = await client.GetCsrfTokenAsync();

            var login = await client.PostWithCsrfAsync(
                "/api/auth/login",
                new { email, password = Password }
            );
            Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

            using var staleLogout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            staleLogout.Headers.Add(TestHttpExtensions.CsrfHeader, anonymousToken);
            var response = await client.SendAsync(staleLogout);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await factory.DeleteTestUserAsync(email);
        }
    }

    private sealed record ProblemResponse(string? Code, Dictionary<string, FieldError[]>? Errors);

    private sealed record FieldError(string Field, string Code);

    private sealed record ProfileResponse(
        string Id,
        string? Email,
        string DisplayName,
        string[] Roles
    );
}
