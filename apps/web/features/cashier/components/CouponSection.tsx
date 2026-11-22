"use client";

import { useState } from "react";
import { useWatch } from "react-hook-form";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, NetworkError } from "@/lib/api/problem";
import { benefitTerms, isProduct } from "@/lib/format";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { cn } from "@/lib/utils";
import {
  useCouponCalculation,
  useCouponTerms,
  useForgetCouponPreviews,
  useHandOverReward,
} from "../hooks";
import {
  looksLikeCardCode,
  looksLikeCouponCode,
  normalizeCode,
  parseDecimal,
} from "../schemas";
import { QrScanner } from "./QrScanner";
import type { PurchaseFlow } from "./usePurchaseFlow";

const money = new Intl.NumberFormat("pl-PL", {
  style: "currency",
  currency: "PLN",
});
const plain = new Intl.NumberFormat("pl-PL");

function previewError(error: unknown) {
  if (error instanceof ApiError && error.status === 429)
    return "Limit podglądów kuponów na tej kasie został wyczerpany. Spróbuj za chwilę - zakup z kuponem można zapisać bez podglądu.";
  if (error instanceof ApiError && error.code === "rewards.coupon_not_found")
    return "Nie znaleziono aktywnego kuponu tego klienta o tym kodzie. Kupon musi być aktywowany w telefonie klienta.";
  if (error instanceof ApiError && error.code === "rewards.coupon_redeemed")
    return "Ten kupon został już wykorzystany.";
  if (error instanceof ApiError && error.code === "rewards.coupon_wrong_kind")
    return "To kod nagrody - rabatu nie da się przeliczyć. Wydaj nagrodę przyciskiem.";
  if (error instanceof ApiError && error.code === "loyalty.card_not_found")
    return "Nie znaleziono aktywnej karty o tym kodzie.";
  if (error instanceof ApiError && error.status === 401)
    return "Sesja wygasła. Zaloguj się ponownie.";
  if (error instanceof NetworkError)
    return "Brak połączenia - nie udało się sprawdzić kuponu.";
  return "Nie udało się sprawdzić kuponu.";
}

/**
 * The coupon field and its preview. The terms are read once the card and coupon codes are complete;
 * the discount only on "Przelicz" (the preview is limited per cashier). Changing a code drops the
 * calculation, changing the amount or liters marks it outdated. The purchase sends the code, never
 * the computed discount: the server checks the coupon again.
 */
export function CouponSection({ flow }: { flow: PurchaseFlow }) {
  const { form, locked, scanner, setScanner } = flow;
  const { errors } = form.formState;
  const [cardText, couponText, amountText, litersText] = useWatch({
    control: form.control,
    name: ["cardCode", "couponCode", "amount", "liters"],
  });

  const cardCode = looksLikeCardCode(cardText) ? normalizeCode(cardText) : null;
  const couponCode = looksLikeCouponCode(couponText)
    ? normalizeCode(couponText)
    : null;
  const amount = parseDecimal(amountText, 2);
  const liters = parseDecimal(litersText, 3);

  const terms = useCouponTerms(cardCode, couponCode);
  const { computed, calculatingFor, calculate, forget } =
    useCouponCalculation();
  const calculating =
    calculatingFor !== null &&
    calculatingFor.cardCode === cardCode &&
    calculatingFor.couponCode === couponCode;

  const sameCodes =
    computed !== null &&
    computed.cardCode === cardCode &&
    computed.couponCode === couponCode;
  const outdated =
    sameCodes && (computed.amount !== amount || computed.liters !== liters);

  const onCalculate = () => {
    if (!cardCode || !couponCode || amount === null || liters === null) return;
    void calculate({ cardCode, couponCode, amount, liters });
  };

  const hasCoupon = couponText.trim() !== "";
  const couponField = form.register("couponCode");
  // The last handed-over reward; cleared when the cashier types or scans another code.
  // Shown for the card it was handed over to; another card (next customer, form reset) hides it.
  const [handedOver, setHandedOver] = useState<{
    offerName: string;
    cardCode: string;
  } | null>(null);

  return (
    <section
      aria-labelledby="coupon-heading"
      className="flex flex-col gap-3 rounded-2xl border p-4"
    >
      <h2 id="coupon-heading" className="font-medium">
        Kupon lub nagroda klienta (opcjonalnie)
      </h2>
      <FormField id="couponCode" label="Kod kuponu" error={errors.couponCode}>
        <Input
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          placeholder="Wklej, wpisz albo zeskanuj"
          className={cn(fieldClass, "font-mono")}
          {...fieldA11y("couponCode", errors.couponCode)}
          {...couponField}
          onChange={(event) => {
            forget();
            setHandedOver(null);
            return couponField.onChange(event);
          }}
        />
      </FormField>
      <QrScanner
        disabled={locked || scanner === "card"}
        onActiveChange={(active) => setScanner(active ? "coupon" : null)}
        accept={looksLikeCouponCode}
        subject="kuponu"
        onScan={(code) => {
          forget();
          setHandedOver(null);
          form.setValue("couponCode", code, {
            shouldValidate: true,
            shouldDirty: true,
          });
        }}
      />

      {hasCoupon && couponCode && !cardCode && (
        <p className="text-sm text-muted-foreground">
          Podaj kod karty klienta, żeby sprawdzić kupon.
        </p>
      )}

      {terms.isFetching && (
        <p role="status" className="text-sm text-muted-foreground">
          Sprawdzanie kuponu…
        </p>
      )}

      {terms.isError && couponCode && cardCode && (
        <p role="alert" className="text-sm text-destructive">
          {previewError(terms.error)}
        </p>
      )}

      {terms.isSuccess && couponCode && cardCode && isProduct(terms.data) && (
        <RewardHandover
          // A new pair of codes is a new operation: no state (error, unknown outcome) carries over.
          key={`${cardCode}:${couponCode}`}
          offerName={terms.data.offerName}
          cardCode={cardCode}
          couponCode={couponCode}
          locked={locked}
          onDone={() => {
            setHandedOver({ offerName: terms.data.offerName, cardCode });
            form.setValue("couponCode", "", { shouldDirty: true });
          }}
        />
      )}
      {handedOver && handedOver.cardCode === cardCode && (
        <p
          role="status"
          className="rounded-xl border border-emerald-600/30 bg-emerald-50 p-3 text-sm font-medium text-emerald-900 dark:bg-emerald-950 dark:text-emerald-100"
        >
          Wydano nagrodę „{handedOver.offerName}”. Bez tankowania i bez punktów.
        </p>
      )}

      {terms.isSuccess && couponCode && cardCode && !isProduct(terms.data) && (
        <div
          data-testid="coupon-terms"
          className="flex flex-col gap-3 rounded-xl bg-muted p-3 text-sm"
        >
          <p>
            <span className="font-semibold">{terms.data.offerName}</span>:{" "}
            {benefitTerms(terms.data)}
          </p>

          {sameCodes && computed?.result && (
            <dl
              data-testid="coupon-calculation"
              data-outdated={outdated}
              aria-label="Wyliczenie rabatu"
              className={cn("grid grid-cols-3 gap-2", outdated && "opacity-50")}
            >
              <div>
                <dt className="text-muted-foreground">Rabat</dt>
                <dd className="font-semibold tabular-nums">
                  {money.format(computed.result.discount)}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Do zapłaty</dt>
                <dd className="font-semibold tabular-nums">
                  {money.format(computed.result.paidAmount)}
                </dd>
              </div>
              <div>
                <dt className="text-muted-foreground">Punkty</dt>
                <dd
                  data-testid="calc-points"
                  className="font-semibold tabular-nums"
                >
                  {plain.format(computed.result.pointsAwarded)}
                </dd>
              </div>
            </dl>
          )}
          {outdated && computed?.result && (
            <p role="status" className="text-sm font-medium">
              Kwota albo litry się zmieniły - wyliczenie jest nieaktualne.
              Przelicz ponownie.
            </p>
          )}
          {sameCodes && computed?.error != null && (
            <p role="alert" className="text-sm text-destructive">
              {previewError(computed.error)}
            </p>
          )}

          <Button
            type="button"
            variant="outline"
            size="sm"
            className="self-start"
            disabled={
              locked || calculating || amount === null || liters === null
            }
            onClick={onCalculate}
          >
            {calculating ? "Przeliczanie…" : "Przelicz rabat"}
          </Button>
          {(amount === null || liters === null) && (
            <p className="text-xs text-muted-foreground">
              Wpisz kwotę i litry, żeby przeliczyć rabat.
            </p>
          )}
        </div>
      )}
    </section>
  );
}

/**
 * Answers where this attempt certainly handed nothing over. After an unknown outcome they say nothing
 * about the earlier attempt with the same key (the rate limiter runs before idempotency), so the
 * outcome stays unknown.
 */
function busyMessage(error: unknown) {
  if (error instanceof ApiError && error.status === 429)
    return "Limit prób na tej kasie został wyczerpany. Nagroda nie została wydana - spróbuj za chwilę.";
  if (error instanceof ApiError && error.code === "loyalty.customer_busy")
    return "Trwa inna operacja tego klienta. Nagroda nie została wydana - spróbuj za chwilę.";
  return null;
}

function handoverError(error: unknown) {
  if (error instanceof ApiError && error.code === "rewards.coupon_redeemed")
    return "Ta nagroda została już wydana.";
  if (error instanceof ApiError && error.code === "rewards.coupon_not_found")
    return "Nie znaleziono aktywnej nagrody tego klienta o tym kodzie.";
  if (error instanceof ApiError && error.code === "loyalty.card_not_found")
    return "Nie znaleziono aktywnej karty o tym kodzie.";
  if (error instanceof ApiError && error.status === 401)
    return "Sesja wygasła. Zaloguj się ponownie.";
  return "Nie udało się wydać nagrody.";
}

/**
 * A product reward (ADR 0013): handed over instead of a purchase - no amount, no points. After an
 * unknown outcome only "Ponów wydanie" with the same key is offered, so it is never handed over twice.
 */
function RewardHandover({
  offerName,
  cardCode,
  couponCode,
  locked,
  onDone,
}: {
  offerName: string;
  cardCode: string;
  couponCode: string;
  locked: boolean;
  onDone: () => void;
}) {
  const handOver = useHandOverReward();
  const forgetPreviews = useForgetCouponPreviews();
  // An earlier attempt with this key ended without a known outcome.
  const [uncertain, setUncertain] = useState(false);
  const busy = handOver.isError ? busyMessage(handOver.error) : null;
  const unknown =
    handOver.isError &&
    keepsIdempotencyKey(handOver.error) &&
    (uncertain || !busy)
      ? handOver.error
      : null;
  const refusal = unknown ? null : busy;

  const send = async () => {
    try {
      await handOver.mutateAsync({ cardCode, couponCode });
      forgetPreviews();
      onDone();
    } catch (error) {
      // Shown below from the mutation state.
      if (keepsIdempotencyKey(error) && !busyMessage(error)) setUncertain(true);
    }
  };

  return (
    <div
      data-testid="reward-handover"
      className="flex flex-col gap-3 rounded-xl bg-muted p-3 text-sm"
    >
      <p>
        <span className="font-semibold">{offerName}</span>: nagroda do wydania.
        Nie zapisuj tankowania z tym kodem.
      </p>
      {refusal && (
        <p role="alert" className="text-destructive">
          {refusal}
        </p>
      )}
      {handOver.isError && !unknown && !refusal && (
        <p role="alert" className="text-destructive">
          {handoverError(handOver.error)}
        </p>
      )}
      {unknown && (
        <p role="alert" className="font-medium">
          {unknown instanceof NetworkError
            ? "Nie dotarła odpowiedź serwera - nie wiadomo, czy nagroda została wydana."
            : "Nie wiadomo, czy nagroda została wydana."}{" "}
          {busy && "Serwer chwilowo odmówił ponowienia - spróbuj za chwilę. "}
          Ponów: nagroda nie zostanie wydana dwa razy.
        </p>
      )}
      <Button
        type="button"
        className="self-start"
        disabled={locked || handOver.isPending}
        onClick={send}
      >
        {handOver.isPending
          ? "Wydawanie…"
          : unknown
            ? "Ponów wydanie"
            : "Wydaj nagrodę"}
      </Button>
    </div>
  );
}
