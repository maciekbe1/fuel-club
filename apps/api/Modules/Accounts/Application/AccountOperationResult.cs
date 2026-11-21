namespace FuelClub.Api.Modules.Accounts.Application;

public sealed record AccountValidationError(string Field, string Code);

public sealed record AccountOperationResult(bool Succeeded, string? ErrorCode, IReadOnlyList<AccountValidationError> Errors)
{
    public static AccountOperationResult Success() => new(true, null, []);
    public static AccountOperationResult Failure(string code, IReadOnlyList<AccountValidationError>? errors = null) => new(false, code, errors ?? []);
}
