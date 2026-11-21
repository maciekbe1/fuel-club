"use client";

import { Check, Copy, RefreshCw, RotateCw } from "lucide-react";
import { useEffect, useState } from "react";
import { Button } from "@/components/ui/button";
import { ApiError, NetworkError } from "@/lib/api/problem";
import { cn } from "@/lib/utils";
import { useIssueCard, useLoyaltyCard, useRotateCard } from "../hooks";
import { formatCardCode, type LoyaltyCard } from "../services";
import { CardQr } from "./CardQr";

const issuedAt = new Intl.DateTimeFormat("pl-PL", { dateStyle: "long", timeStyle: "short" });

function readError(error: Error) {
  if (error instanceof NetworkError) return "Brak połączenia z serwerem. Spróbuj ponownie.";
  if (error instanceof ApiError && error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
  return "Nie udało się pobrać karty. Spróbuj ponownie.";
}

function mutationError(error: Error) {
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera, więc nie wiemy, czy operacja się udała. Ponów - nie zostanie wykonana dwa razy.";
  if (error instanceof ApiError && error.code === "loyalty.customer_busy")
    return "Na Twoim koncie trwa inna operacja. Ponów za chwilę.";
  if (error instanceof ApiError && error.code === "loyalty.card_already_issued") return "Masz już aktywną kartę.";
  if (error instanceof ApiError && error.code === "loyalty.card_missing") return "Nie masz jeszcze karty do wymiany.";
  if (error instanceof ApiError && error.status === 401) return "Sesja wygasła. Zaloguj się ponownie.";
  return "Operacja się nie udała. Spróbuj ponownie.";
}

const retryable = (error: Error) =>
  error instanceof NetworkError || (error instanceof ApiError && (error.status >= 500 || error.code === "idempotency.in_progress"));

const orange = "bg-brand-orange text-brand-navy hover:bg-brand-orange/90";

/**
 * Mutations live here, above the views that come and go (loading, error, no card, card): the
 * idempotency key of an unfinished operation must survive a failed card read in between.
 */
export function LoyaltyCardPanel() {
  const card = useLoyaltyCard();
  const issue = useIssueCard();
  const rotate = useRotateCard();

  if (card.isPending)
    return (
      <div role="status" aria-busy="true" className="aspect-[1.586] w-full max-w-md animate-pulse rounded-3xl bg-muted">
        <span className="sr-only">Ładowanie karty…</span>
      </div>
    );

  if (card.data === undefined)
    return (
      <div role="alert" className="flex flex-col items-start gap-3 rounded-2xl border p-5">
        <p>{readError(card.error)}</p>
        <Button size="sm" onClick={() => card.refetch()} disabled={card.isFetching}>
          <RotateCw className={cn("size-4", card.isFetching && "animate-spin")} aria-hidden />
          Spróbuj ponownie
        </Button>
      </div>
    );

  return (
    <div className="flex flex-col gap-4">
      {card.isError && (
        <div role="alert" className="flex max-w-md flex-col items-start gap-2 rounded-2xl border border-destructive/40 p-4 text-sm">
          <p>Nie udało się odświeżyć karty - pokazany stan może być nieaktualny.</p>
          <Button size="sm" variant="outline" onClick={() => card.refetch()} disabled={card.isFetching}>
            <RotateCw className={cn("size-4", card.isFetching && "animate-spin")} aria-hidden />
            Odśwież
          </Button>
        </div>
      )}
      {card.data ? <ActiveCard card={card.data} rotate={rotate} /> : <NoCard issue={issue} />}
    </div>
  );
}

type CardMutation = ReturnType<typeof useIssueCard>;

function NoCard({ issue }: { issue: CardMutation }) {

  return (
    <section aria-labelledby="no-card-heading" className="flex max-w-md flex-col items-start gap-4 rounded-3xl border bg-card p-6">
      <h2 id="no-card-heading" className="text-lg font-semibold">
        Nie masz jeszcze karty
      </h2>
      <p className="text-muted-foreground">
        Karta Fuel Club to kod, który pokazujesz przy kasie. Dzięki niemu punkty za tankowanie trafią na Twoje konto.
      </p>
      <Button size="lg" className={orange} onClick={() => issue.mutate()} disabled={issue.isPending}>
        {issue.isPending ? "Wydawanie…" : issue.isError && retryable(issue.error) ? "Ponów" : "Wydaj kartę"}
      </Button>
      {issue.isError && (
        <p role="alert" className="text-sm text-destructive">
          {mutationError(issue.error)}
        </p>
      )}
    </section>
  );
}

function ActiveCard({ card, rotate }: { card: LoyaltyCard; rotate: CardMutation }) {
  const [confirming, setConfirming] = useState(false);
  // An unknown outcome keeps the retry visible even if this view was re-mounted in between.
  const awaitingRetry = rotate.isError && retryable(rotate.error);
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
                {rotate.isPending ? "Wymiana…" : rotate.isError && retryable(rotate.error) ? "Ponów wymianę" : "Tak, wymień"}
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

function CopyButton({ code }: { code: string }) {
  const [state, setState] = useState<"idle" | "copied" | "failed">("idle");

  useEffect(() => {
    if (state === "idle") return;
    const timer = setTimeout(() => setState("idle"), 2500);
    return () => clearTimeout(timer);
  }, [state]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setState("copied");
    } catch {
      setState("failed");
    }
  };

  return (
    <div className="flex items-center gap-3">
      <Button variant="outline" onClick={copy}>
        {state === "copied" ? <Check className="size-4" aria-hidden /> : <Copy className="size-4" aria-hidden />}
        Kopiuj kod
      </Button>
      <span role="status" className="text-sm text-muted-foreground">
        {state === "copied" && "Skopiowano"}
        {state === "failed" && "Nie udało się skopiować - przepisz kod ręcznie."}
      </span>
    </div>
  );
}
