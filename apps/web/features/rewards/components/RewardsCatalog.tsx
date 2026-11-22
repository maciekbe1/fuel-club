"use client";

import { Ticket } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { Button } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, NetworkError, readErrorMessage } from "@/lib/api/problem";
import { keepsPendingOperation, usePendingVars } from "@/lib/idempotency";
import { EXCHANGE_OPERATION, useExchange, useRewards } from "../hooks";
import { benefitTerms, formatPoints, isProduct } from "@/lib/format";
import type { ExchangeVars, Reward } from "../services";

const toVars = (offer: Reward): ExchangeVars => ({
  id: offer.id,
  version: offer.version,
  name: offer.name,
  product: isProduct(offer),
});

const FINAL_MESSAGES: Record<string, string> = {
  "rewards.offer_changed":
    "Warunki oferty zmieniły się. Sprawdź nowe warunki i potwierdź wymianę ponownie. Punkty nie zostały pobrane.",
  "rewards.offer_inactive":
    "Ta oferta nie jest już dostępna. Punkty nie zostały pobrane.",
  "rewards.offer_not_found":
    "Ta oferta nie jest już dostępna. Punkty nie zostały pobrane.",
  "rewards.insufficient_points":
    "Masz za mało punktów na tę ofertę. Punkty nie zostały pobrane.",
};

function finalMessage(error: unknown) {
  const code = error instanceof ApiError ? error.code : undefined;
  return (
    (code && FINAL_MESSAGES[code]) ??
    "Nie udało się wymienić punktów. Punkty nie zostały pobrane."
  );
}

function unknownMessage(error: Error) {
  if (error instanceof ApiError && error.status === 401)
    return "Sesja wygasła - zaloguj się ponownie i ponów wymianę: kupon nie zostanie kupiony dwa razy.";
  if (
    error instanceof ApiError &&
    (error.code === "loyalty.customer_busy" ||
      error.code === "rewards.offer_busy")
  )
    return "Wymiana jest chwilowo niemożliwa. Punkty nie zostały pobrane - ponów za chwilę.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Ta wymiana jest właśnie wykonywana. Ponów za chwilę.";
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera - nie wiadomo, czy wymiana się udała. Ponów: kupon nie zostanie kupiony dwa razy.";
  return "Błąd serwera - nie wiadomo, czy wymiana się udała. Ponów: kupon nie zostanie kupiony dwa razy.";
}

type Outcome =
  | { kind: "success"; offerName: string; product: boolean }
  | { kind: "final"; message: string };

/**
 * The exchange lives here, above the list that refetches: the idempotency key of an unfinished
 * exchange must survive a catalog refresh. The confirmation shows the terms of the version that is
 * sent; changed terms (409 offer_changed) close it, so the new terms need a new confirmation.
 */
export function RewardsCatalog() {
  const rewards = useRewards();
  const exchange = useExchange();
  const [selected, setSelected] = useState<Reward | null>(null);
  const [outcome, setOutcome] = useState<Outcome | null>(null);
  // An exchange left unfinished before this screen was (re)mounted, e.g. after a lost response.
  // Read after hydration (the server snapshot is null), cleared once it got a final answer.
  const stored = usePendingVars<ExchangeVars>(EXCHANGE_OPERATION);
  const [cleared, setCleared] = useState(false);
  const restored = cleared ? null : stored;

  // While the outcome is unknown only the same exchange may be retried (same offer, version, key).
  const unknown =
    exchange.isError && keepsPendingOperation(exchange.error)
      ? exchange.error
      : null;
  const interrupted = restored !== null && exchange.isIdle;
  const locked = exchange.isPending || unknown !== null || interrupted;
  const retryVars = selected ? toVars(selected) : restored;

  const confirm = async (vars: ExchangeVars) => {
    setOutcome(null);
    try {
      await exchange.mutateAsync(vars);
      setSelected(null);
      setCleared(true);
      setOutcome({
        kind: "success",
        offerName: vars.name,
        product: vars.product === true,
      });
    } catch (error) {
      if (keepsPendingOperation(error)) return;
      setSelected(null);
      setCleared(true);
      setOutcome({ kind: "final", message: finalMessage(error) });
    }
  };

  if (rewards.isPending)
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-3">
        <span className="sr-only">Ładowanie ofert…</span>
        {[0, 1].map((i) => (
          <div key={i} className="h-36 animate-pulse rounded-2xl bg-muted" />
        ))}
      </div>
    );

  if (rewards.data === undefined)
    return (
      <div
        role="alert"
        className="flex flex-col items-start gap-3 rounded-2xl border p-5"
      >
        <p>
          {readErrorMessage(
            rewards.error,
            "Nie udało się pobrać ofert. Spróbuj ponownie.",
          )}
        </p>
        {rewards.error instanceof ApiError && rewards.error.status === 401 ? (
          <LoginAgainLink />
        ) : (
          <RetryButton
            busy={rewards.isFetching}
            onClick={() => rewards.refetch()}
          />
        )}
      </div>
    );

  const offers = rewards.data.items;

  return (
    <div className="flex flex-col gap-4">
      {rewards.isError && (
        <div
          role="alert"
          className="flex flex-col items-start gap-2 rounded-2xl border border-destructive/40 p-4 text-sm"
        >
          <p>
            Nie udało się odświeżyć ofert - pokazane warunki mogą być
            nieaktualne. Serwer i tak sprawdzi je przy wymianie.
          </p>
          <RetryButton
            busy={rewards.isFetching}
            variant="outline"
            onClick={() => rewards.refetch()}
          >
            Odśwież
          </RetryButton>
        </div>
      )}

      {outcome?.kind === "success" && (
        <p
          role="status"
          className="rounded-2xl border border-emerald-500/40 bg-emerald-50 p-4 text-sm text-emerald-950 dark:bg-emerald-950 dark:text-emerald-50"
        >
          {outcome.product ? (
            <>
              Wymieniono punkty na nagrodę „{outcome.offerName}”. Aktywuj odbiór
              w{" "}
              <Link href="/kupony" className="font-medium underline">
                Moich kuponach i nagrodach
              </Link>{" "}
              i pokaż kod w kasie.
            </>
          ) : (
            <>
              Kupiono kupon „{outcome.offerName}”. Aktywuj go w{" "}
              <Link href="/kupony" className="font-medium underline">
                Moich kuponach
              </Link>{" "}
              przed tankowaniem.
            </>
          )}
        </p>
      )}
      {outcome?.kind === "final" && (
        <p
          role="alert"
          className="rounded-2xl border border-destructive/40 p-4 text-sm"
        >
          {outcome.message}
        </p>
      )}
      {(unknown || interrupted) && retryVars && (
        <div
          role="alert"
          className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-amber-950 dark:bg-amber-950 dark:text-amber-50"
        >
          <p className="text-sm">
            {unknown
              ? unknownMessage(unknown)
              : `Wymiana na kupon „${retryVars.name}” mogła się nie zakończyć - nie wiadomo, czy kupon został kupiony. Ponów: kupon nie zostanie kupiony dwa razy.`}
          </p>
          <Button
            onClick={() => confirm(retryVars)}
            disabled={exchange.isPending}
          >
            Ponów wymianę
          </Button>
        </div>
      )}

      {offers.length === 0 ? (
        <section
          aria-labelledby="no-offers"
          className="flex flex-col gap-2 rounded-3xl border border-dashed p-6"
        >
          <h2 id="no-offers" className="text-lg font-semibold">
            Brak dostępnych ofert
          </h2>
          <p className="text-muted-foreground">
            Nowe oferty pojawią się tutaj. Punkty zbierasz dalej przy każdym
            tankowaniu.
          </p>
        </section>
      ) : (
        <div className="flex flex-col gap-6">
          {[
            {
              label: "Rabaty na paliwo",
              items: offers.filter((offer) => !isProduct(offer)),
            },
            {
              label: "Nagrody",
              items: offers.filter((offer) => isProduct(offer)),
            },
          ]
            .filter((group) => group.items.length > 0)
            .map((group) => (
              <section key={group.label} className="flex flex-col gap-3">
                <h2 className="text-lg font-semibold">{group.label}</h2>
                <ul aria-label={group.label} className="flex flex-col gap-3">
                  {group.items.map((offer) => (
                    <OfferItem
                      key={offer.id}
                      offer={offer}
                      confirming={selected?.id === offer.id}
                      confirmed={selected?.id === offer.id ? selected : null}
                      locked={locked}
                      pending={exchange.isPending && selected?.id === offer.id}
                      onSelect={() => {
                        setOutcome(null);
                        exchange.reset();
                        setSelected(offer);
                      }}
                      onCancel={() => setSelected(null)}
                      onConfirm={() => confirm(toVars(selected!))}
                    />
                  ))}
                </ul>
              </section>
            ))}
        </div>
      )}
    </div>
  );
}

function OfferItem({
  offer,
  confirming,
  confirmed,
  locked,
  pending,
  onSelect,
  onCancel,
  onConfirm,
}: {
  offer: Reward;
  confirming: boolean;
  /** Snapshot of the terms being confirmed: the request sends exactly this version. */
  confirmed: Reward | null;
  locked: boolean;
  pending: boolean;
  onSelect: () => void;
  onCancel: () => void;
  onConfirm: () => void;
}) {
  const headingId = `offer-${offer.id}`;
  return (
    <li
      aria-labelledby={headingId}
      data-testid="offer"
      data-offer-id={offer.id}
      className="flex flex-col gap-3 rounded-2xl border bg-card p-4"
    >
      <div className="flex items-start gap-3">
        <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-brand-orange/15 text-primary">
          <Ticket className="size-5" aria-hidden />
        </span>
        <div className="flex flex-1 flex-col gap-1">
          <h2 id={headingId} className="font-semibold">
            {offer.name}
          </h2>
          {offer.description && (
            <p className="text-sm text-muted-foreground">{offer.description}</p>
          )}
          <p className="text-sm">
            {benefitTerms(offer)
              ? `Rabat ${benefitTerms(offer)}`
              : "Nagroda do odbioru w kasie"}
          </p>
        </div>
        <span className="shrink-0 font-semibold" data-testid="offer-cost">
          {formatPoints(offer.pointsCost)}
        </span>
      </div>

      {!confirming ? (
        <Button
          variant="outline"
          className="self-start"
          onClick={onSelect}
          disabled={locked}
        >
          Wymień punkty
        </Button>
      ) : (
        <div
          role="group"
          aria-label="Potwierdzenie wymiany"
          className="flex flex-col gap-3 rounded-xl bg-muted p-3"
        >
          <p className="text-sm">
            Wymienisz <strong>{formatPoints(confirmed!.pointsCost)}</strong> na{" "}
            {benefitTerms(confirmed!)
              ? `kupon „${confirmed!.name}”: rabat ${benefitTerms(confirmed!)} przy jednym tankowaniu.`
              : `nagrodę „${confirmed!.name}”, do odbioru w kasie.`}
          </p>
          <div className="flex flex-wrap gap-2">
            <Button onClick={onConfirm} disabled={locked} autoFocus>
              {pending ? "Wymieniam…" : "Potwierdź wymianę"}
            </Button>
            <Button variant="outline" onClick={onCancel} disabled={locked}>
              Anuluj
            </Button>
          </div>
        </div>
      )}
    </li>
  );
}
