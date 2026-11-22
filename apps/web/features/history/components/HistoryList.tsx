"use client";

import { Fuel, Ticket } from "lucide-react";
import Link from "next/link";
import { Button, buttonVariants } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { readErrorMessage } from "@/lib/api/problem";
import { cn } from "@/lib/utils";
import { useHistory } from "../hooks";
import type { Transaction } from "../services";

const points = new Intl.NumberFormat("pl-PL", { signDisplay: "always" });
const plain = new Intl.NumberFormat("pl-PL");
const money = new Intl.NumberFormat("pl-PL", {
  style: "currency",
  currency: "PLN",
});
const liters = new Intl.NumberFormat("pl-PL", { maximumFractionDigits: 3 });
const dateTime = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});
const saleDate = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeZone: "UTC",
});

const TYPE_LABELS: Record<string, string> = {
  FuelPurchase: "Tankowanie",
  CouponExchange: "Wymiana na kupon",
};

export function HistoryList() {
  const history = useHistory();

  if (history.isPending)
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-3">
        <span className="sr-only">Ładowanie historii…</span>
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-24 animate-pulse rounded-2xl bg-muted" />
        ))}
      </div>
    );

  if (history.isError && !history.data)
    return (
      <div
        role="alert"
        className="flex flex-col items-start gap-3 rounded-2xl border p-5"
      >
        <p>
          {readErrorMessage(
            history.error,
            "Nie udało się pobrać historii. Spróbuj ponownie.",
          )}
        </p>
        <RetryButton
          busy={history.isFetching}
          onClick={() => history.refetch()}
        />
      </div>
    );

  const items = history.data.pages.flatMap((page) => page.items);

  if (items.length === 0)
    return (
      <section
        aria-labelledby="empty-history"
        className="flex flex-col items-start gap-3 rounded-3xl border border-dashed p-6"
      >
        <h2 id="empty-history" className="text-lg font-semibold">
          Nie masz jeszcze żadnych operacji
        </h2>
        <p className="text-muted-foreground">
          Pokaż kartę przy kasie podczas tankowania - punkty i szczegóły zakupu
          pojawią się tutaj.
        </p>
        <Link href="/karta" className={buttonVariants({ variant: "outline" })}>
          Pokaż kartę
        </Link>
      </section>
    );

  return (
    <div className="flex flex-col gap-4">
      <ol aria-label="Operacje punktowe" className="flex flex-col gap-3">
        {items.map((item) => (
          <HistoryItem key={item.id} item={item} />
        ))}
      </ol>

      {history.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {readErrorMessage(
            history.error,
            "Nie udało się pobrać historii. Spróbuj ponownie.",
          )}
        </p>
      )}

      {history.hasNextPage && (
        <Button
          variant="outline"
          className="self-center"
          onClick={() => history.fetchNextPage()}
          disabled={history.isFetchingNextPage}
        >
          {history.isFetchingNextPage ? "Ładowanie…" : "Pokaż więcej"}
        </Button>
      )}

      <p className="text-xs text-muted-foreground">
        Historia pokazuje zmiany punktów. Zakup za mniej niż 1 zł nie daje
        punktów, więc się tu nie pojawia.
      </p>
    </div>
  );
}

function HistoryItem({ item }: { item: Transaction }) {
  const purchase = item.fuelPurchase;
  const exchange = item.couponExchange;
  const positive = item.delta > 0;
  const Icon = item.type === "CouponExchange" ? Ticket : Fuel;

  return (
    <li
      data-testid="history-item"
      data-id={item.id}
      className="flex gap-4 rounded-2xl border bg-card p-4"
    >
      <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-brand-orange/15 text-primary">
        <Icon className="size-5" aria-hidden />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <div className="flex items-baseline justify-between gap-3">
          <span className="font-medium">
            {TYPE_LABELS[item.type] ?? "Operacja"}
          </span>
          <span
            data-testid="history-delta"
            className={cn(
              "font-semibold tabular-nums",
              positive
                ? "text-emerald-700 dark:text-emerald-400"
                : "text-destructive",
            )}
          >
            {points.format(item.delta)} pkt
          </span>
        </div>
        {purchase && (
          <p className="text-sm text-muted-foreground">
            {[
              `Stacja ${purchase.stationCode}`,
              saleDate.format(new Date(`${purchase.saleDate}T00:00:00Z`)),
              money.format(purchase.amount),
              `${liters.format(purchase.liters)} l`,
            ].join(" · ")}
          </p>
        )}
        {exchange && (
          <p className="text-sm text-muted-foreground">{exchange.offerName}</p>
        )}
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 text-xs text-muted-foreground">
          <time dateTime={item.createdAt}>
            {dateTime.format(new Date(item.createdAt))}
          </time>
          <span>
            Saldo po:{" "}
            <span className="tabular-nums">
              {plain.format(item.balanceAfter)} pkt
            </span>
          </span>
        </div>
      </div>
    </li>
  );
}
