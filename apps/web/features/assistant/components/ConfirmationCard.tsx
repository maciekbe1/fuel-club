"use client";

import { TicketCheck } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { Button, buttonVariants } from "@/components/ui/button";
import { ApiError, NetworkError } from "@/lib/api/problem";
import { formatTerms } from "@/lib/format";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { useConfirmActivation } from "../hooks";
import type { Turn } from "../hooks";
import type { ChatConfirmation } from "../services";

const time = new Intl.DateTimeFormat("pl-PL", {
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});

function confirmationError(error: Error) {
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera - nie wiadomo, czy kupon został aktywowany. Ponów: kod się nie zmieni.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Aktywacja jest właśnie wykonywana. Ponów za chwilę.";
  if (keepsIdempotencyKey(error))
    return "Nie wiadomo, czy kupon został aktywowany. Ponów: kod się nie zmieni.";
  if (error instanceof ApiError) {
    if (error.code === "assistant.confirmation_expired")
      return "Propozycja wygasła. Poproś asystenta o nową.";
    if (error.code === "rewards.coupon_redeemed")
      return "Ten kupon został już wykorzystany.";
    if (error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
    if (error.status === 404) return "Ta propozycja jest nieważna.";
  }
  return "Nie udało się aktywować kuponu.";
}

/**
 * Consent is this button, never the model's text. Declining only hides the buttons: nothing was
 * changed by the proposal, so there is nothing to undo.
 */
export function ConfirmationCard({
  confirmation,
  turnStatus,
}: {
  confirmation: ChatConfirmation;
  turnStatus: Turn["status"];
}) {
  const waiting = turnStatus === "streaming";
  const available = turnStatus === "done" || waiting;
  const confirm = useConfirmActivation();
  const [declined, setDeclined] = useState(false);
  const error = confirm.isError ? confirm.error : null;
  const retryable = error !== null && keepsIdempotencyKey(error);
  const final = error !== null && !retryable;

  return (
    <section
      aria-label={`Aktywacja kuponu ${confirmation.offerName}`}
      data-testid="chat-confirmation"
      className="flex flex-col gap-3 rounded-2xl border border-brand-orange/50 p-4"
    >
      <div className="flex items-start gap-3">
        <TicketCheck className="mt-0.5 size-5 shrink-0 text-primary" />
        <div className="flex flex-col gap-0.5">
          <p className="font-medium">
            Aktywować kupon „{confirmation.offerName}”?
          </p>
          <p className="text-sm text-muted-foreground">
            {formatTerms(confirmation.discountPerLiter, confirmation.maxLiters)}
            . Propozycja ważna do{" "}
            {time.format(new Date(confirmation.expiresAt))}.
          </p>
        </div>
      </div>

      {confirm.isSuccess ? (
        <p role="status" className="text-sm font-medium">
          Kupon jest aktywny. Kod pokażesz przy kasie z ekranu{" "}
          <Link href="/kupony" className="underline">
            Moje kupony
          </Link>
          .
        </p>
      ) : declined ? (
        <p className="text-sm text-muted-foreground">
          Pominięto - kupon bez zmian.
        </p>
      ) : !available ? (
        <p className="text-sm text-muted-foreground">
          Odpowiedź nie została dokończona - zapytaj ponownie, aby aktywować.
        </p>
      ) : (
        <>
          {error && (
            <div role="alert" className="flex flex-col gap-2 text-sm">
              <p>{confirmationError(error)}</p>
              {error instanceof ApiError && error.status === 401 && (
                <LoginAgainLink />
              )}
            </div>
          )}
          {!final && (
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                disabled={confirm.isPending || waiting}
                onClick={() => confirm.mutate(confirmation.token)}
              >
                {waiting
                  ? "Czekam na koniec odpowiedzi…"
                  : confirm.isPending
                    ? "Aktywowanie…"
                    : retryable
                      ? "Ponów aktywację"
                      : "Aktywuj kupon"}
              </Button>
              {!retryable && (
                <Button
                  type="button"
                  variant="outline"
                  disabled={confirm.isPending || waiting}
                  onClick={() => setDeclined(true)}
                >
                  Nie teraz
                </Button>
              )}
            </div>
          )}
          {final && (
            <Link
              href="/kupony"
              className={buttonVariants({ variant: "outline", size: "sm" })}
            >
              Moje kupony
            </Link>
          )}
        </>
      )}
    </section>
  );
}
