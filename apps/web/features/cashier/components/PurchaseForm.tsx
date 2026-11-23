"use client";

import { CircleCheck } from "lucide-react";
import { FormAlert } from "@/components/form/FormAlert";
import { fieldA11y, fieldClass, FormField } from "@/components/form/FormField";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { moneyFormat as money, numberFormat as plain } from "@/lib/format";
import { cn } from "@/lib/utils";
import { looksLikeCardCode, warsawToday } from "../schemas";
import { CouponSection } from "./CouponSection";
import { QrScanner } from "./QrScanner";
import { UnknownOutcome } from "./UnknownOutcome";
import type { PurchaseFlow } from "./usePurchaseFlow";

export function PurchaseForm({ flow }: { flow: PurchaseFlow }) {
  const {
    purchase,
    pending,
    setPending,
    saved,
    form,
    unknown,
    locked,
    send,
    onSubmit,
    scanner,
    setScanner,
    previewGeneration,
    forgetPreviews,
  } = flow;
  const { errors } = form.formState;

  return (
    <div className="flex flex-col gap-5">
      {saved && (
        <div
          role="status"
          className="flex items-start gap-3 rounded-2xl border border-emerald-600/30 bg-emerald-50 p-4 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-100"
        >
          <CircleCheck className="mt-0.5 size-5 shrink-0" aria-hidden />
          <div>
            <p className="font-semibold">
              Zakup zapisany. Przyznano {plain.format(saved.pointsAwarded)} pkt.
            </p>
            <p data-testid="purchase-result" className="text-sm">
              {saved.discount > 0
                ? `Rabat z kuponu ${money.format(saved.discount)} · do zapłaty ${money.format(saved.paidAmount)}`
                : `Do zapłaty ${money.format(saved.paidAmount)}`}
            </p>
            {saved.pointsAwarded === 0 && (
              <p className="text-sm">Kwota poniżej 1 zł nie daje punktów.</p>
            )}
          </div>
        </div>
      )}

      {unknown && pending && (
        <UnknownOutcome
          error={unknown}
          onRetry={() => send(pending)}
          onUnlock={() => {
            purchase.reset();
            setPending(null);
            // The coupon may have been redeemed by the purchase whose outcome is unknown.
            forgetPreviews();
          }}
        />
      )}

      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-5">
        <FormAlert message={errors.root?.message} />
        <fieldset
          disabled={locked}
          className="flex flex-col gap-5 disabled:opacity-70"
        >
          <legend className="sr-only">Zakup paliwa</legend>

          <FormField
            id="cardCode"
            label="Kod karty klienta"
            error={errors.cardCode}
          >
            <Input
              autoComplete="off"
              autoCapitalize="characters"
              spellCheck={false}
              placeholder="Wklej, wpisz albo zeskanuj"
              className={cn(fieldClass, "font-mono")}
              {...fieldA11y("cardCode", errors.cardCode)}
              {...form.register("cardCode")}
            />
          </FormField>
          <QrScanner
            disabled={locked || scanner === "coupon"}
            onActiveChange={(active) => setScanner(active ? "card" : null)}
            accept={looksLikeCardCode}
            subject="karty klienta"
            onScan={(code) =>
              form.setValue("cardCode", code, {
                shouldValidate: true,
                shouldDirty: true,
              })
            }
          />

          <div className="grid gap-5 sm:grid-cols-2">
            {/* Amount and liters first: the coupon preview below uses them. */}
            <FormField id="amount" label="Kwota (zł)" error={errors.amount}>
              <Input
                inputMode="decimal"
                autoComplete="off"
                placeholder="0,00"
                className={fieldClass}
                {...fieldA11y("amount", errors.amount)}
                {...form.register("amount")}
              />
            </FormField>
            <FormField id="liters" label="Litry" error={errors.liters}>
              <Input
                inputMode="decimal"
                autoComplete="off"
                placeholder="0,000"
                className={fieldClass}
                {...fieldA11y("liters", errors.liters)}
                {...form.register("liters")}
              />
            </FormField>
            <FormField
              id="saleDate"
              label="Data sprzedaży"
              error={errors.saleDate}
            >
              <Input
                type="date"
                max={warsawToday()}
                className={fieldClass}
                {...fieldA11y("saleDate", errors.saleDate)}
                {...form.register("saleDate")}
              />
            </FormField>
            <FormField
              id="receiptNumber"
              label="Numer paragonu"
              error={errors.receiptNumber}
            >
              <Input
                autoComplete="off"
                autoCapitalize="characters"
                className={cn(fieldClass, "font-mono")}
                {...fieldA11y("receiptNumber", errors.receiptNumber)}
                {...form.register("receiptNumber")}
              />
            </FormField>
          </div>

          {/* A new generation remounts the section: its calculation goes with the forgotten previews. */}
          <CouponSection key={previewGeneration} flow={flow} />

          <Button
            type="submit"
            size="lg"
            className="h-12 bg-brand-orange text-brand-navy hover:bg-brand-orange/90"
          >
            {purchase.isPending ? "Zapisywanie…" : "Zapisz zakup"}
          </Button>
        </fieldset>
      </form>
    </div>
  );
}
