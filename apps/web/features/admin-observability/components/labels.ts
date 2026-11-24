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

export const END_REASONS: Record<string, string> = {
  Client: "zakończona przez klienta",
  Idle: "wygasła (brak aktywności)",
  Handoff: "przekazana konsultantowi",
};

export const OUTCOMES: Record<string, string> = {
  Done: "zakończona",
  Error: "błąd",
  Cancelled: "przerwana przez klienta",
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

export const usd = new Intl.NumberFormat("pl-PL", {
  style: "currency",
  currency: "USD",
  maximumFractionDigits: 4,
});

/** "x z y (z%)": the denominator is always shown next to a share. */
export const share = (part: number, whole: number) =>
  whole === 0
    ? "brak danych"
    : `${number.format(part)} z ${number.format(whole)} (${percent.format(part / whole)})`;

export const TRAFFIC = {
  real: "Rzeczywisty",
  synthetic: "Syntetyczny",
  all: "Wszystko",
} as const;

export const ms = (value: number | null | undefined) =>
  value == null ? "brak danych" : `${number.format(value)} ms`;
