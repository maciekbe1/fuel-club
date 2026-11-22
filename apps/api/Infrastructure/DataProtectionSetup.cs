using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// Keys in the database: a redeploy or a second instance keeps sessions, CSRF tokens and chat
/// confirmations valid. Outside Development they are encrypted with a certificate given in
/// DataProtection:Certificate (base64 PFX, from the environment), so the database and its backups
/// hold no usable key material - the secret itself stays in the environment.
/// </summary>
public static class DataProtectionSetup
{
    public const string ApplicationName = "FuelClub";
    public const string Section = "DataProtection";

    public static IServiceCollection AddPersistentDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var builder = services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AppDbContext>();
        var certificate = LoadCertificate(configuration);
        if (certificate is not null)
            // Unprotect with the given certificate too: on Linux it is not in a certificate store.
            builder
                .ProtectKeysWithCertificate(certificate)
                .UnprotectKeysWithAnyCertificate(certificate);
        // Checked when the host starts, not when it is built: the migration bundle builds the host
        // without starting it and needs no certificate.
        services
            .AddOptions<DataProtectionCertificateCheck>()
            .Configure(check => check.Configured = certificate is not null)
            .Validate(
                check => check.Configured || environment.IsDevelopment(),
                $"{Section}:Certificate is required outside Development (keys must not be stored unencrypted)."
            )
            .ValidateOnStart();
        return services;
    }

    private static X509Certificate2? LoadCertificate(IConfiguration configuration)
    {
        var encoded = configuration[$"{Section}:Certificate"];
        if (string.IsNullOrWhiteSpace(encoded))
            return null;
        try
        {
            return X509CertificateLoader.LoadPkcs12(
                Convert.FromBase64String(encoded.Trim()),
                configuration[$"{Section}:CertificatePassword"]
            );
        }
        catch (Exception error)
            when (error is FormatException or System.Security.Cryptography.CryptographicException)
        {
            throw new InvalidOperationException(
                $"{Section}:Certificate is not a base64 PKCS#12 certificate (check the password)."
            );
        }
    }
}

public sealed class DataProtectionCertificateCheck
{
    public bool Configured { get; set; }
}
