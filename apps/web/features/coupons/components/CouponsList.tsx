"use client";

import { CheckCircle2, Ticket } from "lucide-react";
import Link from "next/link";
import { CodeQr } from "@/components/code/CodeQr";
import { CopyButton } from "@/components/code/CopyButton";
import { LoginAgainLink } from "@/components/auth/LoginAgainLink";
import { Button, buttonVariants } from "@/components/ui/button";
import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, NetworkError, readErrorMessage } from "@/lib/api/problem";
import { benefitTerms, formatPoints, isProduct } from "@/lib/format";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { cn } from "@/lib/utils";
import { useActivateCoupon, useCoupons } from "../hooks";
import { formatCouponCode, type Coupon } from "../services";

const dateTime = new Intl.DateTimeFormat("pl-PL", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Europe/Warsaw",
});

const STATUS: Record<string, { label: string; className: string }> = {
  Purchased: {
    label: "Do aktywacji",
    className: "bg-brand-orange/15 text-primary",
  },
  Active: {
    label: "Aktywny",
    className:
      "bg-emerald-100 text-emerald-900 dark:bg-emerald-900 dark:text-emerald-100",
  },
  Redeemed: { label: "Wykorzystany", className: "bg-muted text-foreground" },
};

const sessionExpired = (error: unknown) =>
  error instanceof ApiError && error.status === 401;

const LoginLink = LoginAgainLink;

function activationError(error: Error) {
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera - nie wiadomo, czy kupon został aktywowany. Ponów: kod się nie zmieni.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Aktywacja jest właśnie wykonywana. Ponów za chwilę.";
  if (keepsIdempotencyKey(error))
    return "Nie wiadomo, czy kupon został aktywowany. Ponów: kod się nie zmieni.";
  if (error instanceof ApiError && error.code === "rewards.coupon_redeemed")
    return "Ten kupon został już wykorzystany.";
  if (error instanceof ApiError && error.code === "rewards.coupon_not_found")
    return "Nie znaleziono tego kuponu.";
  if (error instanceof ApiError && error.status === 401)
    return "Sesja wygasła. Zaloguj się ponownie.";
  return "Nie udało się aktywować kuponu.";
}

/** The activation lives here, above the list that refetches after every attempt. */
export function CouponsList() {
  const coupons = useCoupons();
  const activate = useActivateCoupon();

  if (coupons.isPending)
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-3">
        <span className="sr-only">Ładowanie kuponów…</span>
        {[0, 1].map((i) => (
          <div key={i} className="h-36 animate-pulse rounded-2xl bg-muted" />
        ))}
      </div>
    );

  if (coupons.data === undefined)
    return (
      <div
        role="alert"
        className="flex flex-col items-start gap-3 rounded-2xl border p-5"
      >
        <p>
          {readErrorMessage(
            coupons.error,
            "Nie udało się pobrać kuponów. Spróbuj ponownie.",
          )}
        </p>
        {sessionExpired(coupons.error) ? (
          <LoginLink />
        ) : (
          <RetryButton
            busy={coupons.isFetching}
            onClick={() => coupons.refetch()}
          />
        )}
      </div>
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
        <div
          role="alert"
          className="flex flex-col items-start gap-2 rounded-2xl border border-destructive/40 p-4 text-sm"
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
        </div>
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

function CouponItem({
  coupon,
  activating,
  error,
  busy,
  onActivate,
}: {
  coupon: Coupon;
  activating: boolean;
  error: Error | null;
  busy: boolean;
  onActivate: () => void;
}) {
  const headingId = `coupon-${coupon.id}`;
  // An unknown status is shown as is, never as "used".
  const status = STATUS[coupon.status] ?? {
    label: coupon.status,
    className: "bg-muted text-foreground",
  };
  const unknown = error !== null && keepsIdempotencyKey(error);

  return (
    <li
      aria-labelledby={headingId}
      data-testid="coupon"
      data-coupon-id={coupon.id}
      data-status={coupon.status}
      className="flex flex-col gap-4 rounded-2xl border bg-card p-4"
    >
      <div className="flex items-start gap-3">
        <span className="grid size-11 shrink-0 place-items-center rounded-xl bg-brand-orange/15 text-primary">
          {coupon.status === "Redeemed" ? (
            <CheckCircle2 className="size-5" aria-hidden />
          ) : (
            <Ticket className="size-5" aria-hidden />
          )}
        </span>
        <div className="flex flex-1 flex-col gap-1">
          <h2 id={headingId} className="font-semibold">
            {coupon.offerName}
          </h2>
          <p className="text-sm">
            {benefitTerms(coupon)
              ? `Rabat ${benefitTerms(coupon)}`
              : "Nagroda do odbioru w kasie"}
          </p>
          <p className="text-xs text-muted-foreground">
            Kupiony {dateTime.format(new Date(coupon.purchasedAt))} za{" "}
            {formatPoints(coupon.pointsCost)}
            {coupon.activatedAt &&
              ` · aktywowany ${dateTime.format(new Date(coupon.activatedAt))}`}
            {coupon.redeemedAt &&
              ` · wykorzystany ${dateTime.format(new Date(coupon.redeemedAt))}`}
          </p>
        </div>
        <span
          data-testid="coupon-status"
          className={cn(
            "shrink-0 rounded-full px-2.5 py-1 text-xs font-medium",
            status.className,
          )}
        >
          {status.label}
        </span>
      </div>

      {coupon.status === "Purchased" && (
        <div className="flex flex-col items-start gap-2">
          <p className="text-sm text-muted-foreground">
            {isProduct(coupon)
              ? "Aktywuj odbiór - dostaniesz kod do pokazania w kasie. Aktywacja nie wydaje nagrody."
              : "Aktywuj kupon przed tankowaniem - dostaniesz kod do pokazania przy kasie. Aktywacja nie zużywa kuponu."}
          </p>
          <Button onClick={onActivate} disabled={busy}>
            {activating
              ? "Aktywuję…"
              : unknown
                ? "Ponów aktywację"
                : isProduct(coupon)
                  ? "Aktywuj odbiór"
                  : "Aktywuj kupon"}
          </Button>
        </div>
      )}

      {coupon.status === "Active" && coupon.code && (
        <div className="flex flex-col gap-3">
          <figure className="flex flex-col items-center gap-3 rounded-2xl border bg-white p-4 text-neutral-900">
            <CodeQr
              code={coupon.code}
              label="Kod QR kuponu do zeskanowania przy kasie"
              testId="coupon-qr"
              className="size-48 max-w-full"
            />
            <figcaption
              data-testid="coupon-code"
              data-code={coupon.code}
              aria-label={`Kod kuponu: ${coupon.code.split("").join(" ")}`}
              className="text-center font-mono text-sm font-semibold tracking-wider break-all"
            >
              {formatCouponCode(coupon.code)}
            </figcaption>
          </figure>
          <p className="text-sm text-muted-foreground">
            {isProduct(coupon)
              ? "Pokaż kod w kasie razem z kartą - kasjer wyda nagrodę."
              : "Pokaż przy kasie razem z kartą. Kupon działa przy jednym tankowaniu."}
          </p>
          <CopyButton code={coupon.code} />
        </div>
      )}

      {/* Only while still Purchased: once the GET shows the coupon active, the outcome is known. */}
      {error && coupon.status === "Purchased" && (
        <p
          role="alert"
          className={cn(
            "text-sm",
            unknown ? "text-amber-900 dark:text-amber-200" : "text-destructive",
          )}
        >
          {activationError(error)}
        </p>
      )}
      {error && coupon.status === "Purchased" && sessionExpired(error) && (
        <LoginLink />
      )}
    </li>
  );
}
