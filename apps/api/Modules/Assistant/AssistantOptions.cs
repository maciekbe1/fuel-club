using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant;

public sealed class AssistantOptions
{
    public const string Section = "Assistant";

    public int MaxMessageLength { get; set; } = 1000;
    public int MaxHistoryItems { get; set; } = 20;
    public int MaxHistoryTextLength { get; set; } = 2000;
    public int MaxToolCalls { get; set; } = 4;
    public TimeSpan TurnTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ToolTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan ConfirmationLifetime { get; set; } = TimeSpan.FromMinutes(10);
    public int ChatPermitLimit { get; set; } = 20;
    public TimeSpan ChatWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Base64, at least 32 bytes; a secret (environment / user-secrets), optional locally.</summary>
    public string? PseudonymKey { get; set; }

    /// <summary>How long the end of a turn may wait for its trace row.</summary>
    public TimeSpan TraceSaveTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>"demo" (default, no network) or "gemini" (needs Gemini:ApiKey).</summary>
    public string Provider { get; set; } = Providers.Demo;

    public GeminiOptions Gemini { get; set; } = new();
}

public static class Providers
{
    public const string Demo = "demo";
    public const string Gemini = "gemini";
}

public sealed class GeminiOptions
{
    /// <summary>A secret: user-secrets or the environment (Assistant__Gemini__ApiKey), never the repo.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Model code from the Gemini models page; check it before switching.</summary>
    public string Model { get; set; } = "gemini-3.5-flash-lite";
    public Uri Endpoint { get; set; } = new("https://generativelanguage.googleapis.com/v1beta/");

    /// <summary>One model call; the whole turn is still bounded by TurnTimeout.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(20);
}

public sealed class AssistantOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AssistantOptions>
{
    public ValidateOptionsResult Validate(string? name, AssistantOptions o)
    {
        var errors = new List<string>();
        if (o.MaxMessageLength is < 1 or > 10_000)
            errors.Add("Assistant:MaxMessageLength must be 1-10000.");
        if (o.MaxHistoryItems is < 0 or > 100)
            errors.Add("Assistant:MaxHistoryItems must be 0-100.");
        if (o.MaxHistoryTextLength is < 1 or > 20_000)
            errors.Add("Assistant:MaxHistoryTextLength must be 1-20000.");
        if (o.MaxToolCalls is < 1 or > 20)
            errors.Add("Assistant:MaxToolCalls must be 1-20.");
        if (o.TurnTimeout <= TimeSpan.Zero || o.TurnTimeout > TimeSpan.FromMinutes(5))
            errors.Add("Assistant:TurnTimeout must be > 0 and <= 5 min.");
        if (o.ToolTimeout <= TimeSpan.Zero || o.ToolTimeout > o.TurnTimeout)
            errors.Add("Assistant:ToolTimeout must be > 0 and <= TurnTimeout.");
        if (o.HeartbeatInterval <= TimeSpan.Zero || o.HeartbeatInterval > TimeSpan.FromMinutes(1))
            errors.Add("Assistant:HeartbeatInterval must be > 0 and <= 1 min.");
        if (
            o.ConfirmationLifetime < TimeSpan.FromSeconds(30)
            || o.ConfirmationLifetime > TimeSpan.FromHours(1)
        )
            errors.Add("Assistant:ConfirmationLifetime must be 30 s - 1 h.");
        if (o.ChatPermitLimit is < 1 or > 1000)
            errors.Add("Assistant:ChatPermitLimit must be 1-1000.");
        if (o.ChatWindow <= TimeSpan.Zero || o.ChatWindow > TimeSpan.FromHours(1))
            errors.Add("Assistant:ChatWindow must be > 0 and <= 1 h.");
        if (!string.IsNullOrWhiteSpace(o.PseudonymKey) && !IsKey(o.PseudonymKey))
            errors.Add("Assistant:PseudonymKey must be base64 of at least 32 bytes.");
        // Outside local development pseudonyms must survive restarts and match across instances.
        if (string.IsNullOrWhiteSpace(o.PseudonymKey) && !environment.IsDevelopment())
            errors.Add("Assistant:PseudonymKey is required outside Development.");
        if (o.TraceSaveTimeout <= TimeSpan.Zero || o.TraceSaveTimeout > TimeSpan.FromSeconds(30))
            errors.Add("Assistant:TraceSaveTimeout must be > 0 and <= 30 s.");
        if (o.Provider is not (Providers.Demo or Providers.Gemini))
            errors.Add("Assistant:Provider must be demo or gemini.");
        if (o.Provider == Providers.Gemini)
        {
            if (string.IsNullOrWhiteSpace(o.Gemini.ApiKey))
                errors.Add(
                    "Assistant:Gemini:ApiKey is required when Assistant:Provider is gemini."
                );
            if (string.IsNullOrWhiteSpace(o.Gemini.Model))
                errors.Add("Assistant:Gemini:Model is required.");
            if (o.Gemini.RequestTimeout <= TimeSpan.Zero || o.Gemini.RequestTimeout > o.TurnTimeout)
                errors.Add("Assistant:Gemini:RequestTimeout must be > 0 and <= TurnTimeout.");
        }
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    private static bool IsKey(string value)
    {
        var buffer = new byte[value.Length];
        return Convert.TryFromBase64String(value, buffer, out var length) && length >= 32;
    }
}
