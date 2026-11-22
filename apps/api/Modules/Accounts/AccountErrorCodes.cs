namespace FuelClub.Api.Modules.Accounts
{
    public static class AccountErrorCodes
    {
        public const string RequiredFields = "accounts.required_fields";
        public const string InvalidCredentials = "accounts.invalid_credentials";
        public const string RegistrationFailed = "accounts.registration_failed";
        public const string InvalidEmail = "accounts.invalid_email";
        public const string EmailAlreadyExists = "accounts.email_already_exists";
        public const string PasswordTooShort = "accounts.password_too_short";
        public const string PasswordRequiresUniqueChars = "accounts.password_requires_unique_chars";
        public const string PasswordRequiresNonAlphanumeric =
            "accounts.password_requires_non_alphanumeric";
        public const string PasswordRequiresDigit = "accounts.password_requires_digit";
        public const string PasswordRequiresLower = "accounts.password_requires_lowercase";
        public const string PasswordRequiresUpper = "accounts.password_requires_uppercase";
        public const string InvalidRegistration = "accounts.invalid_registration";
    }
}
