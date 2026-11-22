"use client";

import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { readErrorMessage } from "@/lib/api/problem";
import { formatPoints, formatTerms } from "@/lib/format";
import { cn } from "@/lib/utils";
import { useAdminRewards } from "../hooks";

export function AdminRewardsList() {
  const rewards = useAdminRewards();

  if (rewards.isPending)
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-3">
        <span className="sr-only">Ładowanie ofert…</span>
        {[0, 1, 2].map((i) => (
          <div key={i} className="h-20 animate-pulse rounded-2xl bg-muted" />
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
        <RetryButton
          busy={rewards.isFetching}
          onClick={() => rewards.refetch()}
        />
      </div>
    );

  const items = rewards.data.items;
  return (
    <div className="flex flex-col gap-4">
      <Link
        href="/admin/oferty/nowa"
        className={cn(buttonVariants(), "self-start")}
      >
        Nowa oferta
      </Link>
      {items.length === 0 ? (
        <p className="text-muted-foreground">Brak ofert.</p>
      ) : (
        <ul aria-label="Oferty" className="flex flex-col gap-3">
          {items.map((offer) => (
            <li
              key={offer.id}
              data-testid="admin-offer"
              className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border bg-card p-4"
            >
              <div className="flex flex-col gap-1">
                <p className="font-semibold">{offer.name}</p>
                <p className="text-sm text-muted-foreground">
                  {formatPoints(offer.pointsCost)} ·{" "}
                  {formatTerms(offer.discountPerLiter, offer.maxLiters)} ·
                  wersja {offer.version}
                </p>
              </div>
              <div className="flex items-center gap-3">
                <span
                  data-testid="admin-offer-status"
                  className={cn(
                    "rounded-full px-2.5 py-1 text-xs font-medium",
                    offer.isActive
                      ? "bg-emerald-100 text-emerald-900 dark:bg-emerald-900 dark:text-emerald-100"
                      : "bg-muted text-foreground",
                  )}
                >
                  {offer.isActive ? "Aktywna" : "Wyłączona"}
                </span>
                <Link
                  href={`/admin/oferty/${offer.id}`}
                  aria-label={`Edytuj ofertę ${offer.name}`}
                  className={buttonVariants({ variant: "outline", size: "sm" })}
                >
                  Edytuj
                </Link>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
