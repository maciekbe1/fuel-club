using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Tracing;

/// <summary>
/// Stable pseudonyms for traces: HMAC-SHA256 of the user id with Assistant:PseudonymKey (base64,
/// at least 32 bytes, from the environment / user-secrets). Without a key a random one is used for
/// this process only: pseudonyms then change on restart, which is fine for a local demo.
/// </summary>
public sealed class TracePseudonyms
{
    private readonly byte[] _key;

    public TracePseudonyms(IOptions<AssistantOptions> options, ILogger<TracePseudonyms> logger)
    {
        var configured = options.Value.PseudonymKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            _key = RandomNumberGenerator.GetBytes(32);
            logger.LogWarning(
                "Assistant:PseudonymKey is not set: trace pseudonyms change on every restart"
            );
        }
        else
            _key = Convert.FromBase64String(configured);
    }

    public string For(string userId) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(userId))[..16]);
}
