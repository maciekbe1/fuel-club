export { dateTimeFormat as dateTime } from "@/lib/format";
import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { ApiError, NetworkError } from "@/lib/api/problem";
import { keepsIdempotencyKey } from "@/lib/idempotency";

export const STATUS: Record<string, { label: string; className: string }> = {
  Purchased: {
    label: "Do aktywacji",
    className: "bg-brand-orange/15 text-primary",
  },
  Active: {
    label: "Aktywny",
    className:
      "bg-emerald-100 text-emerald-900 dark:bg-emerald-900 dark:text-emerald-100",
  },
  Redeemed: { label: "Wykorzystany", className: "bg-muted text-foreground" },
};

export const sessionExpired = (error: unknown) =>
  error instanceof ApiError && error.status === 401;

export const LoginLink = LoginAgainLink;

export function activationError(error: Error) {
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera - nie wiadomo, czy kupon został aktywowany. Ponów: kod się nie zmieni.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Aktywacja jest właśnie wykonywana. Ponów za chwilę.";
  if (keepsIdempotencyKey(error))
    return "Nie wiadomo, czy kupon został aktywowany. Ponów: kod się nie zmieni.";
  if (error instanceof ApiError && error.code === "rewards.coupon_redeemed")
    return "Ten kupon został już wykorzystany.";
  if (error instanceof ApiError && error.code === "rewards.coupon_not_found")
    return "Nie znaleziono tego kuponu.";
  if (error instanceof ApiError && error.status === 401)
    return "Sesja wygasła. Zaloguj się ponownie.";
  return "Nie udało się aktywować kuponu.";
}
