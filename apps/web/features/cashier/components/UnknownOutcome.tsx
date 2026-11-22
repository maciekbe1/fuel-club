"use client";

import { useState } from "react";
import { Button } from "@/components/ui/button";
import { ApiError, NetworkError } from "@/lib/api/problem";

function unknownMessage(error: Error) {
  if (error instanceof ApiError && error.code === "loyalty.customer_busy")
    return "Konto klienta jest chwilowo zajęte. Zakup nie został zapisany - ponów za chwilę.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Ten zakup jest właśnie zapisywany. Ponów za chwilę.";
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera - nie wiadomo, czy zakup został zapisany. Ponów: zakup nie zostanie zapisany dwa razy.";
  return "Błąd serwera - nie wiadomo, czy zakup został zapisany. Ponów: zakup nie zostanie zapisany dwa razy.";
}

/**
 * The purchase may already be saved. Offers the safe retry (same body, same key) first; unlocking
 * needs a confirmation, because a new receipt number would bypass the duplicate protection.
 */
export function UnknownOutcome({
  error,
  onRetry,
  onUnlock,
}: {
  error: Error;
  onRetry: () => void;
  onUnlock: () => void;
}) {
  const [unlocking, setUnlocking] = useState(false);

  return (
    <div
      role="alert"
      className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-amber-950 dark:bg-amber-950 dark:text-amber-50"
    >
      <p className="text-sm">{unknownMessage(error)}</p>
      {!unlocking ? (
        <div className="flex flex-wrap gap-2">
          <Button onClick={onRetry}>Ponów zapis zakupu</Button>
          <Button variant="outline" onClick={() => setUnlocking(true)}>
            Odblokuj formularz…
          </Button>
        </div>
      ) : (
        <div
          role="group"
          aria-label="Potwierdzenie odblokowania"
          className="flex flex-col gap-2"
        >
          <p className="text-sm font-semibold">
            Zakup mógł już zostać zapisany.
          </p>
          <p className="text-sm">
            Odblokowanie nie cofa zapisu na serwerze. Zakup z tym samym numerem
            paragonu nie zostanie zapisany drugi raz, ale{" "}
            <strong>zmiana numeru paragonu omija tę ochronę</strong> i może
            naliczyć punkty podwójnie. Najpierw ponów zapis albo ustal wynik
            (np. w historii punktów klienta).
          </p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={() => setUnlocking(false)}>Wróć</Button>
            <Button
              variant="outline"
              onClick={() => {
                setUnlocking(false);
                onUnlock();
              }}
            >
              Rozumiem, odblokuj
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
