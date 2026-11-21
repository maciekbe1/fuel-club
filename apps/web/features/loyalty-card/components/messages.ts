import { ApiError, NetworkError } from "@/lib/api/problem";
import { keepsIdempotencyKey } from "@/lib/idempotency";

/** Message for a failed issue or rotation; an unknown outcome is safe to retry with the same key. */
export function mutationError(error: Error) {
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera, więc nie wiemy, czy operacja się udała. Ponów - nie zostanie wykonana dwa razy.";
  if (error instanceof ApiError && error.code === "loyalty.customer_busy")
    return "Na Twoim koncie trwa inna operacja. Ponów za chwilę.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Ta operacja jest właśnie wykonywana. Ponów za chwilę.";
  // Every other error that keeps the key (5xx, unreadable response) has an unknown outcome too.
  if (keepsIdempotencyKey(error))
    return "Nie wiemy, czy operacja się udała. Ponów - nie zostanie wykonana dwa razy.";
  if (error instanceof ApiError && error.code === "loyalty.card_already_issued") return "Masz już aktywną kartę.";
  if (error instanceof ApiError && error.code === "loyalty.card_missing") return "Nie masz jeszcze karty do wymiany.";
  if (error instanceof ApiError && error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
  return "Operacja się nie udała. Spróbuj ponownie.";
}

export const orange = "bg-brand-orange text-brand-navy hover:bg-brand-orange/90";
