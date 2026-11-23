export const RULES: Record<string, string> = {
  access_denied: "Odmowy dostępu (403)",
  login_failed: "Nieudane logowania",
  idempotency_conflict: "Konflikty kluczy idempotencji",
  repeated_redemption: "Ponowne użycie odebranego kuponu",
  rate_limited: "Przekroczone limity (429)",
  tool_limit: "Limit narzędzi w turze",
  rejected_tools: "Tury z odrzuconym narzędziem",
  ai_errors: "Udział błędów asystenta",
};

export const SECURITY: Record<string, string> = {
  AccessDenied: "Odmowy dostępu",
  LoginFailed: "Nieudane logowania",
  IdempotencyConflict: "Konflikty kluczy",
  RepeatedRedemption: "Ponowny odbiór kuponu",
  RateLimited: "Limity (429)",
};

export const REASONS: Record<string, string> = {
  error: "błąd tury",
  rejected_tool: "odrzucone narzędzie",
  tool_failure: "awaria narzędzia",
  repeated_errors: "powtarzające się błędy użytkownika",
};

export const dateTime = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "short",
  timeStyle: "medium",
  timeZone: "Europe/Warsaw",
});

export const number = new Intl.NumberFormat("pl-PL", {
  maximumFractionDigits: 1,
});

export const percent = new Intl.NumberFormat("pl-PL", {
  style: "percent",
  maximumFractionDigits: 1,
});

export const ms = (value: number | null | undefined) =>
  value == null ? "brak danych" : `${number.format(value)} ms`;
