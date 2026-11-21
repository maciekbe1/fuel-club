namespace FuelClub.Api.Infrastructure.Idempotency;

public static class IdempotencyErrorCodes
{
    public const string KeyRequired = "idempotency.key_required";
    public const string KeyInvalid = "idempotency.key_invalid";
    public const string KeyReused = "idempotency.key_reused";
    public const string InProgress = "idempotency.in_progress";
    public const string InvalidJson = "idempotency.invalid_json";
    public const string DuplicateJsonProperty = "idempotency.duplicate_json_property";
}
