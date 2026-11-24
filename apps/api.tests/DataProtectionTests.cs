using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class DataProtectionTests(ApiFactory factory)
{
    [Fact]
    public async Task Keys_live_in_the_database_so_another_instance_can_read_what_one_protected()
    {
        const string purpose = "FuelClub.Tests.DataProtection";
        var protectedText = factory
            .Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(purpose)
            .Protect("payload");

        await using (var scope = factory.Services.CreateAsyncScope())
            Assert.True(
                await scope
                    .ServiceProvider.GetRequiredService<AppDbContext>()
                    .DataProtectionKeys.AnyAsync()
            );

        // A second host has its own key ring cache: it only knows the key through the database.
        await using var other = factory.WithWebHostBuilder(_ => { });
        var unprotected = other
            .Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(purpose)
            .Unprotect(protectedText);
        Assert.Equal("payload", unprotected);
    }

    [Fact]
    public async Task Cookies_are_secure_when_the_edge_forwards_https()
    {
        async Task<string> CsrfCookie(string? proto)
        {
            var client = factory.CreateClient();
            if (proto is not null)
                client.DefaultRequestHeaders.Add("X-Forwarded-Proto", proto);
            var response = await client.GetAsync("/api/auth/csrf");
            response.EnsureSuccessStatusCode();
            return string.Join("\n", response.Headers.GetValues("Set-Cookie"));
        }

        Assert.Contains("secure", (await CsrfCookie("https")).ToLowerInvariant());
        Assert.DoesNotContain("secure", (await CsrfCookie(null)).ToLowerInvariant());
    }

    private static string NewCertificate()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=FuelClub DataProtection Test",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1)
        );
        return Convert.ToBase64String(
            certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12)
        );
    }

    [Fact]
    public async Task With_a_certificate_new_keys_are_stored_encrypted_and_still_shared()
    {
        var certificate = NewCertificate();
        await using var first = factory.WithWebHostBuilder(b =>
            b.UseSetting("DataProtection:Certificate", certificate)
        );
        var key = first
            .Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow.AddDays(90));

        await using (var scope = first.Services.CreateAsyncScope())
        {
            var stored = await scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .DataProtectionKeys.SingleAsync(k => k.FriendlyName == $"key-{key.KeyId}");
            Assert.Contains("EncryptedXml", stored.Xml);
            Assert.DoesNotContain("<masterKey", stored.Xml);
        }

        // Another instance with the same certificate reads it.
        await using var second = factory.WithWebHostBuilder(b =>
            b.UseSetting("DataProtection:Certificate", certificate)
        );
        var keys = second
            .Services.GetRequiredService<Microsoft.AspNetCore.DataProtection.KeyManagement.IKeyManager>()
            .GetAllKeys();
        Assert.NotNull(keys.Single(k => k.KeyId == key.KeyId).Descriptor);

        await using (var scope = first.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .DataProtectionKeys.Where(k => k.FriendlyName == $"key-{key.KeyId}")
                .ExecuteDeleteAsync();
    }

    [Fact]
    public void Configuration_errors_stop_the_start()
    {
        static IConfiguration Config(params (string, string)[] values) =>
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => v.Item1, v => (string?)v.Item2))
                .Build();
        var production = new TestEnvironment { EnvironmentName = "Production" };

        // Missing certificate outside Development: refused when the host starts (options validation).
        var services = new ServiceCollection();
        services.AddPersistentDataProtection(Config(), production);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() =>
            provider
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<DataProtectionCertificateCheck>>()
                .Value
        );
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddPersistentDataProtection(
                Config(("DataProtection:Certificate", "not-base64")),
                production
            )
        );
        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddClientAddress(
                Config(("ForwardedHeaders:KnownNetworks:0", "172.16.0.0/12 # comment"))
            )
        );
        foreach (var limit in new[] { "0", "11" })
            Assert.Throws<InvalidOperationException>(() =>
                new ServiceCollection().AddClientAddress(
                    Config(("ForwardedHeaders:ForwardLimit", limit))
                )
            );
    }

    [Theory]
    // Cloud Run: edge -> Next -> private hops -> API. The first unknown address from the right wins.
    [InlineData("5", "198.51.100.7, 169.254.8.1, 10.186.0.5", "198.51.100.7")]
    // A client-supplied prefix stays to the left of the address the edge appended.
    [InlineData("5", "6.6.6.6, 198.51.100.7, 169.254.8.1, 10.186.0.5", "198.51.100.7")]
    // The default reads one hop: every client would share the private address.
    [InlineData(null, "198.51.100.7, 169.254.8.1, 10.186.0.5", "10.186.0.5")]
    public async Task Client_address_skips_known_private_hops_by_range(
        string? limit,
        string forwardedFor,
        string expected
    )
    {
        var services = new ServiceCollection();
        services.AddClientAddress(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ForwardedHeaders:KnownNetworks:0"] = "10.186.0.0/20",
                        ["ForwardedHeaders:KnownNetworks:1"] = "169.254.0.0/16",
                        ["ForwardedHeaders:ForwardLimit"] = limit,
                    }
                )
                .Build()
        );
        using var provider = services.BuildServiceProvider();
        var options =
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:169.254.169.126");
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        var middleware = new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            options
        );
        await middleware.Invoke(context);
        Assert.Equal(expected, context.Connection.RemoteIpAddress!.ToString());
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "FuelClub.Api";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            null!;
    }

    [Theory]
    // The configuration deployed on Cloud Run (instructions, "Adres klienta na Cloud Run").
    [InlineData("198.51.100.7, 0.0.0.0", "198.51.100.7")]
    [InlineData("6.6.6.6, 198.51.100.7, 0.0.0.0", "198.51.100.7")]
    public async Task Client_address_on_cloud_run(string forwardedFor, string expected)
    {
        var services = new ServiceCollection();
        services.AddClientAddress(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ForwardedHeaders:KnownNetworks:0"] = "169.254.0.0/16",
                        ["ForwardedHeaders:KnownProxies:0"] = "0.0.0.0",
                        ["ForwardedHeaders:ForwardLimit"] = "3",
                    }
                )
                .Build()
        );
        using var provider = services.BuildServiceProvider();
        var options =
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>();
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:169.254.169.126");
        context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        await new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            options
        ).Invoke(context);
        Assert.Equal(expected, context.Connection.RemoteIpAddress!.ToString());
    }
}
