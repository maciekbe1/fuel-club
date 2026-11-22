using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Confirmation;

/// <summary>What the user agrees to: one coupon of one user, from one proposal, until a deadline.</summary>
public sealed record ActivationProposal(
    Guid ProposalId,
    string UserId,
    Guid CouponId,
    DateTimeOffset ExpiresAt
);

public enum TokenCheck
{
    Valid,
    Invalid,
    Expired,
}

/// <summary>
/// Stateless confirmation tokens (ADR 0007): Data Protection over the proposal. The deadline is in the
/// payload and checked with TimeProvider (ITimeLimitedDataProtector uses its own clock).
/// </summary>
public sealed class ConfirmationTokens(
    IDataProtectionProvider provider,
    TimeProvider clock,
    IOptions<AssistantOptions> options
)
{
    public const string Purpose = "FuelClub.Assistant.CouponActivation.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public (ActivationProposal Proposal, string Token) Issue(string userId, Guid couponId)
    {
        var proposal = new ActivationProposal(
            Guid.CreateVersion7(),
            userId,
            couponId,
            clock.GetUtcNow() + options.Value.ConfirmationLifetime
        );
        return (proposal, _protector.Protect(JsonSerializer.Serialize(proposal)));
    }

    /// <summary>Invalid also covers "issued for another user": the caller learns nothing more.</summary>
    public (TokenCheck Check, ActivationProposal? Proposal) Read(string? token, string userId)
    {
        if (string.IsNullOrWhiteSpace(token))
            return (TokenCheck.Invalid, null);
        ActivationProposal? proposal;
        try
        {
            proposal = JsonSerializer.Deserialize<ActivationProposal>(_protector.Unprotect(token));
        }
        catch (Exception error)
            when (error is CryptographicException or JsonException or FormatException)
        {
            return (TokenCheck.Invalid, null);
        }
        if (proposal is null || !string.Equals(proposal.UserId, userId, StringComparison.Ordinal))
            return (TokenCheck.Invalid, null);
        if (clock.GetUtcNow() >= proposal.ExpiresAt)
            return (TokenCheck.Expired, proposal);
        return (TokenCheck.Valid, proposal);
    }
}
