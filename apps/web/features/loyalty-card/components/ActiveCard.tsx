"use client";

import { RefreshCw } from "lucide-react";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import type { useRotateCard } from "../hooks";
import { formatCardCode, type LoyaltyCard } from "../services";
import { CardQr } from "./CardQr";
import { CopyButton } from "./CopyButton";
import { mutationError, orange } from "./messages";

const issuedAt = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "long",
  timeStyle: "short",
});

/** The rotation mutation comes from the panel, so its idempotency key survives this view re-mounting. */
export function ActiveCard({ card, rotate }: { card: LoyaltyCard; rotate: ReturnType<typeof useRotateCard> }) {
  const [confirming, setConfirming] = useState(false);
  // An unknown outcome keeps the retry visible even if this view was re-mounted in between.
  const awaitingRetry = rotate.isError && keepsIdempotencyKey(rotate.error);
  const [rotated, setRotated] = useState(false);

  const confirmRotation = () =>
    rotate.mutate(undefined, {
      onSuccess: () => {
        setConfirming(false);
        setRotated(true);
      },
    });

  return (
    <div className="flex w-full max-w-md flex-col gap-6">
      <section
        aria-labelledby="card-heading"
        className="relative flex aspect-[1.586] w-full flex-col justify-between overflow-hidden rounded-3xl bg-linear-to-br from-orange-500 to-orange-700 p-6 text-white shadow-xl shadow-orange-900/20"
      >
        <div className="pointer-events-none absolute -top-12 -right-12 size-44 rounded-full bg-white/10" />
        <div className="pointer-events-none absolute -bottom-16 -left-10 size-48 rounded-full bg-white/10" />
        <h2 id="card-heading" className="relative text-sm font-semibold">
          Karta Fuel Club
        </h2>
        <div className="relative flex flex-col gap-1">
          <p className="text-xs uppercase tracking-widest text-white/70">Kod karty</p>
          <p
            data-testid="card-code"
            data-code={card.code}
            aria-label={`Kod karty: ${card.code.split("").join(" ")}`}
            className="font-mono text-xl font-semibold tracking-wider break-all sm:text-2xl"
          >
            {formatCardCode(card.code)}
          </p>
        </div>
        <p className="relative text-xs text-white/70">Wydana {issuedAt.format(new Date(card.issuedAt))}</p>
      </section>

      <figure className="flex flex-col items-center gap-3 rounded-3xl border bg-white p-5 text-neutral-900">
        <CardQr code={card.code} className="size-56 max-w-full" />
        <figcaption className="text-center text-sm">Pokaż kod QR przy kasie</figcaption>
      </figure>

      <p className="text-sm text-muted-foreground">
        Kasjer może też wpisać kod ręcznie - podyktuj go albo skopiuj. Kod nie zawiera Twoich danych osobowych.
      </p>

      <CopyButton code={card.code} />

      <section aria-labelledby="rotate-heading" className="flex flex-col items-start gap-3 rounded-2xl border p-5">
        <h2 id="rotate-heading" className="font-medium">
          Wymiana kodu
        </h2>
        <p className="text-sm text-muted-foreground">
          Jeśli ktoś mógł zobaczyć Twój kod, wymień go. Stary kod przestanie działać od razu.
        </p>

        {rotated && !rotate.isPending && (
          <p role="status" className="text-sm font-medium text-emerald-700 dark:text-emerald-400">
            Nowy kod jest aktywny.
          </p>
        )}

        {!confirming && !awaitingRetry ? (
          <Button
            variant="outline"
            onClick={() => {
              setRotated(false);
              setConfirming(true);
            }}
          >
            <RefreshCw className="size-4" aria-hidden />
            Wymień kod
          </Button>
        ) : (
          <div role="group" aria-label="Potwierdzenie wymiany kodu" className="flex flex-col gap-3">
            <p className="text-sm font-medium">
              {awaitingRetry
                ? "Wymiana mogła już zostać wykonana. Ponowienie jest bezpieczne - kod nie zostanie wymieniony drugi raz."
                : "Na pewno wymienić kod? Stary przestanie działać."}
            </p>
            <div className="flex flex-wrap gap-2">
              <Button onClick={confirmRotation} disabled={rotate.isPending} className={orange}>
                {rotate.isPending
                  ? "Wymiana…"
                  : rotate.isError && keepsIdempotencyKey(rotate.error)
                    ? "Ponów wymianę"
                    : "Tak, wymień"}
              </Button>
              {/* Before sending, "Anuluj" abandons the rotation. With an unknown outcome there is nothing to
                  cancel: the server may already have rotated, so only a safe retry is offered. */}
              {!awaitingRetry && (
                <Button variant="outline" onClick={() => setConfirming(false)} disabled={rotate.isPending}>
                  Anuluj
                </Button>
              )}
            </div>
          </div>
        )}

        {rotate.isError && (
          <p role="alert" className="text-sm text-destructive">
            {mutationError(rotate.error)}
          </p>
        )}
      </section>
    </div>
  );
}
