"use client";

import { QueryError } from "@/components/query/QueryError";
import { Alert } from "@/components/ui/alert";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { Skeleton } from "@/components/ui/skeleton";
import { useActivateCoupon, useCoupons } from "../hooks";
import { CouponItem } from "./CouponItem";
import { LoginLink, sessionExpired } from "./couponLabels";

/** The activation lives here, above the list that refetches after every attempt. */
export function CouponsList() {
  const coupons = useCoupons();
  const activate = useActivateCoupon();

  if (coupons.isPending)
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-3">
        <span className="sr-only">Ładowanie kuponów…</span>
        {[0, 1].map((i) => (
          <Skeleton key={i} className="h-36 rounded-2xl" />
        ))}
      </div>
    );

  if (coupons.data === undefined)
    return (
      <QueryError
        error={coupons.error}
        fallback="Nie udało się pobrać kuponów. Spróbuj ponownie."
        retrying={coupons.isFetching}
        onRetry={() => coupons.refetch()}
      />
    );

  const items = coupons.data.items;

  if (items.length === 0)
    return (
      <section
        aria-labelledby="no-coupons"
        className="flex flex-col items-start gap-3 rounded-3xl border border-dashed p-6"
      >
        <h2 id="no-coupons" className="text-lg font-semibold">
          Nie masz jeszcze kuponów
        </h2>
        <p className="text-muted-foreground">
          Wymień punkty na kupon z rabatem na paliwo.
        </p>
        <Link href="/oferty" className={buttonVariants({ variant: "outline" })}>
          Zobacz oferty
        </Link>
      </section>
    );

  return (
    <div className="flex flex-col gap-4">
      {coupons.isError && (
        <Alert
          className="flex flex-col items-start gap-2 rounded-2xl p-4 text-sm"
          variant="destructive"
        >
          <p>
            Nie udało się odświeżyć kuponów - pokazany stan może być
            nieaktualny.
          </p>
          {sessionExpired(coupons.error) ? (
            <LoginLink />
          ) : (
            <RetryButton
              busy={coupons.isFetching}
              variant="outline"
              onClick={() => coupons.refetch()}
            >
              Odśwież
            </RetryButton>
          )}
        </Alert>
      )}
      <ul aria-label="Kupony" className="flex flex-col gap-4">
        {items.map((coupon) => (
          <CouponItem
            key={coupon.id}
            coupon={coupon}
            activating={activate.isPending && activate.variables === coupon.id}
            error={
              activate.isError && activate.variables === coupon.id
                ? activate.error
                : null
            }
            busy={activate.isPending}
            onActivate={() => activate.mutate(coupon.id)}
          />
        ))}
      </ul>
    </div>
  );
}
