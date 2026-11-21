using System.Security.Claims;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Accounts.Application;
using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Accounts.Infrastructure;

internal sealed class IdentityAccountsService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext dbContext
) : IAccountsService
{
    public async Task<AccountOperationResult> RegisterAsync(RegisterRequest request)
    {
        if (
            string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Password)
            || string.IsNullOrWhiteSpace(request.DisplayName)
        )
            return AccountOperationResult.Failure(
                AccountErrorCodes.RequiredFields,
                RequiredFieldErrors(request)
            );

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = request.DisplayName.Trim(),
        };
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return AccountOperationResult.Failure(
                AccountErrorCodes.RegistrationFailed,
                result.Errors.Select(MapIdentityError).ToArray()
            );

        var roleResult = await userManager.AddToRoleAsync(user, RoleNames.User);
        if (!roleResult.Succeeded)
            return AccountOperationResult.Failure(
                AccountErrorCodes.RegistrationFailed,
                roleResult.Errors.Select(MapIdentityError).ToArray()
            );

        await transaction.CommitAsync();
        return AccountOperationResult.Success();
    }

    public async Task<AccountOperationResult> LoginAsync(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return AccountOperationResult.Failure(
                AccountErrorCodes.RequiredFields,
                RequiredFieldErrors(request)
            );

        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return AccountOperationResult.Failure(AccountErrorCodes.InvalidCredentials);

        var result = await signInManager.PasswordSignInAsync(user, request.Password, false, true);
        return result.Succeeded
            ? AccountOperationResult.Success()
            : AccountOperationResult.Failure(AccountErrorCodes.InvalidCredentials);
    }

    public async Task<AccountProfile?> GetCurrentUserAsync(ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
            return null;

        var roles = await userManager.GetRolesAsync(user);
        return new AccountProfile(user.Id, user.Email, user.DisplayName, roles);
    }

    public async Task LogoutAsync() => await signInManager.SignOutAsync();

    private static IReadOnlyList<AccountValidationError> RequiredFieldErrors(
        RegisterRequest request
    )
    {
        var errors = new List<AccountValidationError>();
        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add(new("email", AccountErrorCodes.RequiredFields));
        if (string.IsNullOrWhiteSpace(request.Password))
            errors.Add(new("password", AccountErrorCodes.RequiredFields));
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            errors.Add(new("displayName", AccountErrorCodes.RequiredFields));
        return errors;
    }

    private static IReadOnlyList<AccountValidationError> RequiredFieldErrors(LoginRequest request)
    {
        var errors = new List<AccountValidationError>();
        if (string.IsNullOrWhiteSpace(request.Email))
            errors.Add(new("email", AccountErrorCodes.RequiredFields));
        if (string.IsNullOrWhiteSpace(request.Password))
            errors.Add(new("password", AccountErrorCodes.RequiredFields));
        return errors;
    }

    private static AccountValidationError MapIdentityError(IdentityError error)
    {
        var (field, code) = error.Code switch
        {
            "InvalidEmail" => ("email", AccountErrorCodes.InvalidEmail),
            "DuplicateEmail" or "DuplicateUserName" => (
                "email",
                AccountErrorCodes.EmailAlreadyExists
            ),
            "PasswordTooShort" => ("password", AccountErrorCodes.PasswordTooShort),
            "PasswordRequiresUniqueChars" => (
                "password",
                AccountErrorCodes.PasswordRequiresUniqueChars
            ),
            "PasswordRequiresNonAlphanumeric" => (
                "password",
                AccountErrorCodes.PasswordRequiresNonAlphanumeric
            ),
            "PasswordRequiresDigit" => ("password", AccountErrorCodes.PasswordRequiresDigit),
            "PasswordRequiresLower" => ("password", AccountErrorCodes.PasswordRequiresLower),
            "PasswordRequiresUpper" => ("password", AccountErrorCodes.PasswordRequiresUpper),
            _ => ("registration", AccountErrorCodes.InvalidRegistration),
        };
        return new(field, code);
    }
}
