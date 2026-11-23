import { CheckCircle2, Ticket } from "lucide-react";
import { CodeQr } from "@/components/code/CodeQr";
import { CopyButton } from "@/components/code/CopyButton";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { benefitTerms, formatPoints, isProduct } from "@/lib/format";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { cn } from "@/lib/utils";
import { formatCouponCode, type Coupon } from "../services";
import {
  LoginLink,
  STATUS,
  activationError,
  dateTime,
  sessionExpired,
} from "./couponLabels";

export function CouponItem({
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
              : coupon.status === "Redeemed"
                ? "Nagroda wydana w kasie"
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
        <Badge
          data-testid="coupon-status"
          variant="secondary"
          className={cn("h-auto whitespace-normal", status.className)}
        >
          {status.label}
        </Badge>
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
