"use client";

import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, readErrorMessage } from "@/lib/api/problem";
import { cn } from "@/lib/utils";
import { useWallet } from "../hooks";

const points = new Intl.NumberFormat("pl-PL");

export function formatPoints(value: number) {
  return `${points.format(value)} pkt`;
}

export function BalanceCard({ className }: { className?: string }) {
  const wallet = useWallet();

  return (
    <section
      aria-labelledby="balance-heading"
      aria-busy={wallet.isPending}
      className={cn(
        "relative overflow-hidden rounded-3xl bg-brand-navy p-6 text-brand-navy-foreground shadow-xl shadow-brand-navy/20",
        className,
      )}
    >
      <div className="pointer-events-none absolute -top-16 -right-16 size-48 rounded-full bg-brand-orange/25 blur-2xl" />
      <h2
        id="balance-heading"
        className="relative text-sm font-medium uppercase tracking-widest text-white/70"
      >
        Twoje saldo
      </h2>

      <div className="relative mt-3 min-h-16" aria-live="polite">
        {wallet.isPending && (
          <div className="flex flex-col gap-2" role="status">
            <span className="sr-only">Ładowanie salda…</span>
            <div className="h-10 w-40 animate-pulse rounded-lg bg-white/15" />
            <div className="h-4 w-56 animate-pulse rounded bg-white/10" />
          </div>
        )}

        {wallet.isError && (
          <div className="flex flex-col items-start gap-3" role="alert">
            <p className="text-sm text-white/90">
              {readErrorMessage(
                wallet.error,
                "Nie udało się pobrać salda. Spróbuj ponownie za chwilę.",
              )}
            </p>
            {wallet.error instanceof ApiError && wallet.error.status === 401 ? (
              <Link
                href="/login"
                className={cn(
                  buttonVariants({ size: "sm" }),
                  "bg-brand-orange text-brand-navy hover:bg-brand-orange/90",
                )}
              >
                Zaloguj się
              </Link>
            ) : (
              <RetryButton
                busy={wallet.isFetching}
                onClick={() => wallet.refetch()}
                className="bg-brand-orange text-brand-navy hover:bg-brand-orange/90"
              />
            )}
          </div>
        )}

        {wallet.isSuccess && (
          <>
            <p className="text-4xl font-bold tabular-nums tracking-tight sm:text-5xl">
              {formatPoints(wallet.data.balance)}
            </p>
            <p className="mt-2 text-sm text-white/70">
              {wallet.data.balance === 0
                ? "Zatankuj z kartą Fuel Club, a punkty pojawią się tutaj."
                : "1 punkt za każdy pełny 1 zł wydany na paliwo."}
            </p>
          </>
        )}
      </div>
    </section>
  );
}
