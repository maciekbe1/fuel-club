import { ApiError, NetworkError } from "@/lib/api/problem";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { SUPPORT_MESSAGE_MAX_LENGTH, type SupportSide } from "./services";

/** System messages carry event codes; each side reads them in its own words. */
export const SYSTEM_TEXT: Record<SupportSide, Record<string, string>> = {
  customer: {
    claimed: "Konsultant dołączył do rozmowy.",
    released: "Konsultant oddał zgłoszenie - czekasz na kolejnego.",
    idle_returned: "Konsultant nie odpowiadał - zgłoszenie wróciło do kolejki.",
    closed_by_customer: "Rozmowa zakończona przez Ciebie.",
    closed_by_support: "Konsultant zakończył rozmowę.",
    expired: "Nikt nie zdążył odpowiedzieć - zgłoszenie wygasło.",
  },
  agent: {
    claimed: "Zgłoszenie przejęte.",
    released: "Zgłoszenie oddane do kolejki.",
    idle_returned:
      "Brak odpowiedzi konsultanta - zgłoszenie wróciło do kolejki.",
    closed_by_customer: "Klient zakończył rozmowę.",
    closed_by_support: "Konsultant zakończył rozmowę.",
    expired: "Zgłoszenie wygasło.",
  },
};

export const SUPPORT_CONSENT =
  "Rozmowa z konsultantem jest zapisywana i przechowywana 30 dni od zakończenia.";

export function statusText(side: SupportSide, status: string) {
  switch (status) {
    case "Waiting":
      return side === "customer" ? "Czekasz na konsultanta" : "W kolejce";
    case "Assigned":
      return side === "customer"
        ? "Rozmawiasz z konsultantem"
        : "Twoje zgłoszenie";
    case "Closed":
      return "Rozmowa zakończona";
    case "Expired":
      return "Zgłoszenie wygasło";
    default:
      return status;
  }
}

const unknownOutcome = (error: Error) =>
  error instanceof NetworkError || keepsIdempotencyKey(error);

export function sendError(side: SupportSide, error: Error) {
  if (unknownOutcome(error))
    return "Nie wiadomo, czy wiadomość dotarła. Wyślij ponownie - nie pojawi się dwa razy.";
  if (error instanceof ApiError) {
    if (error.code === "support.case_closed")
      return "Rozmowa jest już zakończona.";
    if (error.code === "support.not_assignee")
      return "To zgłoszenie nie jest już przypisane do Ciebie.";
    if (error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
    if (error.status === 400)
      return `Wiadomość musi mieć od 1 do ${SUPPORT_MESSAGE_MAX_LENGTH} znaków.`;
  }
  return side === "customer"
    ? "Nie udało się wysłać wiadomości."
    : "Nie udało się wysłać odpowiedzi.";
}

export function createError(error: Error) {
  if (unknownOutcome(error))
    return "Nie wiadomo, czy zgłoszenie powstało. Spróbuj ponownie - nie powstanie drugie.";
  if (error instanceof ApiError) {
    if (error.status === 429)
      return "Za dużo prób w krótkim czasie. Spróbuj za chwilę.";
    if (error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
    if (error.status === 403)
      return "Rozmowa z konsultantem jest dostępna tylko dla kont klientów.";
    if (error.status === 400)
      return `Wiadomość musi mieć od 1 do ${SUPPORT_MESSAGE_MAX_LENGTH} znaków.`;
  }
  return "Nie udało się połączyć z konsultantem.";
}

export function agentActionError(error: Error) {
  if (error instanceof ApiError) {
    if (error.code === "support.case_taken")
      return "To zgłoszenie przejął już inny konsultant.";
    if (error.code === "support.case_closed")
      return "To zgłoszenie jest już zamknięte.";
    if (error.code === "support.not_assignee")
      return "To zgłoszenie nie jest już przypisane do Ciebie.";
    if (error.code === "support.case_not_found")
      return "Tego zgłoszenia już nie ma.";
    if (error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
    if (error.status === 403)
      return "Panel jest dostępny tylko dla konsultantów.";
  }
  return "Nie udało się. Spróbuj ponownie.";
}
