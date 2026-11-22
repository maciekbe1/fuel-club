"use client";

import { useWatch } from "react-hook-form";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, NetworkError } from "@/lib/api/problem";
import { formatTerms } from "@/lib/format";
import { cn } from "@/lib/utils";
import { useCouponCalculation, useCouponTerms } from "../hooks";
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

  return (
    <section
      aria-labelledby="coupon-heading"
      className="flex flex-col gap-3 rounded-2xl border p-4"
    >
      <h2 id="coupon-heading" className="font-medium">
        Kupon klienta (opcjonalnie)
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

      {terms.isSuccess && couponCode && cardCode && (
        <div
          data-testid="coupon-terms"
          className="flex flex-col gap-3 rounded-xl bg-muted p-3 text-sm"
        >
          <p>
            <span className="font-semibold">{terms.data.offerName}</span>:{" "}
            {formatTerms(terms.data.discountPerLiter, terms.data.maxLiters)}
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
