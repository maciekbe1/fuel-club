import { z } from "zod";

export const loginSchema = z.object({
  email: z.email("Podaj poprawny adres e-mail"),
  password: z.string().min(1, "Podaj hasło"),
});

export type LoginValues = z.infer<typeof loginSchema>;

export const passwordRules = [
  {
    id: "length",
    label: "Min. 10 znaków",
    test: (v: string) => v.length >= 10,
  },
  { id: "lower", label: "Mała litera", test: (v: string) => /[a-z]/.test(v) },
  { id: "upper", label: "Wielka litera", test: (v: string) => /[A-Z]/.test(v) },
  { id: "digit", label: "Cyfra", test: (v: string) => /\d/.test(v) },
  {
    id: "special",
    label: "Znak specjalny",
    test: (v: string) => /[^a-zA-Z0-9]/.test(v),
  },
] as const;

export const registerSchema = z
  .object({
    displayName: z
      .string()
      .trim()
      .min(1, "Podaj imię")
      .max(50, "Maksymalnie 50 znaków"),
    email: z.email("Podaj poprawny adres e-mail"),
    password: z
      .string()
      .refine(
        (v) => passwordRules.every((rule) => rule.test(v)),
        "Hasło nie spełnia wymagań",
      ),
    confirmPassword: z.string().min(1, "Powtórz hasło"),
  })
  .refine((values) => values.password === values.confirmPassword, {
    path: ["confirmPassword"],
    message: "Hasła nie są takie same",
  });

export type RegisterValues = z.infer<typeof registerSchema>;

export const authMessages: Record<string, string> = {
  "accounts.required_fields": "To pole jest wymagane",
  "accounts.invalid_credentials": "Nieprawidłowy e-mail lub hasło",
  "accounts.invalid_email": "Niepoprawny adres e-mail",
  "accounts.email_already_exists": "Konto z tym adresem już istnieje",
  "accounts.password_too_short": "Hasło jest za krótkie (min. 10 znaków)",
  "accounts.password_requires_unique_chars": "Hasło musi zawierać różne znaki",
  "accounts.password_requires_non_alphanumeric":
    "Hasło musi zawierać znak specjalny",
  "accounts.password_requires_digit": "Hasło musi zawierać cyfrę",
  "accounts.password_requires_lowercase": "Hasło musi zawierać małą literę",
  "accounts.password_requires_uppercase": "Hasło musi zawierać wielką literę",
  "accounts.invalid_registration": "Nie udało się utworzyć konta",
  "accounts.registration_failed": "Nie udało się utworzyć konta",
  default: "Niepoprawna wartość",
};
