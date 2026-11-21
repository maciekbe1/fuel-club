"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { CircleCheck, RotateCw } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { fieldA11y, fieldClass, FormAlert, FormField } from "@/components/form/FormField";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, applyFieldErrors, NetworkError } from "@/lib/api/problem";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { cn } from "@/lib/utils";
import { useCashierStation, useRegisterPurchase } from "../hooks";
import { purchaseMessages, purchaseSchema, toPurchaseBody, warsawToday, type PurchaseValues } from "../schemas";
import type { FuelPurchaseBody, FuelPurchaseResult } from "../services";
import { QrScanner } from "./QrScanner";

const FIELDS = ["cardCode", "amount", "liters", "saleDate", "receiptNumber"] as const;
const plain = new Intl.NumberFormat("pl-PL");

/**
 * The purchase in progress (form values, mutation with its idempotency key, the body awaiting a retry)
 * lives here, above the station views: a failed station refresh must not drop an unfinished purchase.
 */
export function CashierPanel() {
  const station = useCashierStation();
  const flow = usePurchaseFlow();

  if (station.data === undefined && station.isPending)
    return (
      <div role="status" aria-busy="true" className="h-96 animate-pulse rounded-3xl bg-muted">
        <span className="sr-only">Ładowanie stanowiska…</span>
      </div>
    );

  if (station.data === undefined) {
    const error = station.error!;
    const forbidden = error instanceof ApiError && (error.status === 403 || error.status === 404);
    return (
      <div role="alert" className="flex flex-col items-start gap-3 rounded-2xl border p-5">
        <p>
          {forbidden
            ? "To konto nie ma przypisanego stanowiska kasjera."
            : "Nie udało się pobrać stanowiska. Spróbuj ponownie."}
        </p>
        {!forbidden && (
          <Button size="sm" onClick={() => station.refetch()} disabled={station.isFetching}>
            <RotateCw className={cn("size-4", station.isFetching && "animate-spin")} aria-hidden />
            Spróbuj ponownie
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {station.isError && (
        <div role="alert" className="flex flex-col items-start gap-2 rounded-2xl border border-destructive/40 p-4 text-sm">
          <p>Nie udało się odświeżyć stanowiska. Pokazane dane mogą być nieaktualne; zakup w toku nie został utracony.</p>
          <Button size="sm" variant="outline" onClick={() => station.refetch()} disabled={station.isFetching}>
            <RotateCw className={cn("size-4", station.isFetching && "animate-spin")} aria-hidden />
            Odśwież
          </Button>
        </div>
      )}
      <dl className="grid grid-cols-2 gap-3 rounded-2xl border bg-card p-4 text-sm" aria-label="Stanowisko">
        <div>
          <dt className="text-muted-foreground">Stacja</dt>
          <dd data-testid="station-code" className="font-mono font-semibold">
            {station.data.stationCode}
          </dd>
        </div>
        <div>
          <dt className="text-muted-foreground">Kasa</dt>
          <dd className="font-mono font-semibold">{station.data.registerCode}</dd>
        </div>
      </dl>
      <PurchaseForm flow={flow} />
    </div>
  );
}

function outcomeUnknown(error: Error) {
  return keepsIdempotencyKey(error);
}

function unknownMessage(error: Error) {
  if (error instanceof ApiError && error.code === "loyalty.customer_busy")
    return "Konto klienta jest chwilowo zajęte. Zakup nie został zapisany - ponów za chwilę.";
  if (error instanceof ApiError && error.code === "idempotency.in_progress")
    return "Ten zakup jest właśnie zapisywany. Ponów za chwilę.";
  if (error instanceof NetworkError)
    return "Nie dotarła odpowiedź serwera - nie wiadomo, czy zakup został zapisany. Ponów: zakup nie zostanie zapisany dwa razy.";
  return "Błąd serwera - nie wiadomo, czy zakup został zapisany. Ponów: zakup nie zostanie zapisany dwa razy.";
}

function usePurchaseFlow() {
  const purchase = useRegisterPurchase();
  const [pending, setPending] = useState<FuelPurchaseBody | null>(null);
  const [saved, setSaved] = useState<FuelPurchaseResult | null>(null);
  const form = useForm<PurchaseValues>({
    resolver: zodResolver(purchaseSchema),
    defaultValues: { cardCode: "", amount: "", liters: "", saleDate: warsawToday(), receiptNumber: "" },
  });

  // While the outcome is unknown the form is locked: a retry must send the same body (same key).
  const unknown = purchase.isError && outcomeUnknown(purchase.error) ? purchase.error : null;
  const locked = purchase.isPending || unknown !== null;

  const send = async (body: FuelPurchaseBody) => {
    setPending(body);
    setSaved(null);
    try {
      const result = await purchase.mutateAsync(body);
      setSaved(result);
      setPending(null);
      form.reset({ cardCode: "", amount: "", liters: "", saleDate: body.saleDate, receiptNumber: "" });
    } catch (error) {
      if (outcomeUnknown(error as Error)) return;
      setPending(null);
      if (applyFieldErrors(error, form.setError, purchaseMessages, FIELDS)) return;
      const code = error instanceof ApiError ? error.code : undefined;
      form.setError("root", {
        message:
          (code && purchaseMessages[code]) ??
          (code === "loyalty.cashier_station_missing"
            ? "To konto nie ma przypisanego stanowiska kasjera."
            : "Nie udało się zapisać zakupu."),
      });
    }
  };

  const onSubmit = form.handleSubmit((values) => send(toPurchaseBody(values)));

  return { purchase, pending, setPending, saved, form, unknown, locked, send, onSubmit };
}

function PurchaseForm({ flow }: { flow: ReturnType<typeof usePurchaseFlow> }) {
  const { purchase, pending, setPending, saved, form, unknown, locked, send, onSubmit } = flow;
  const { errors } = form.formState;
  const [unlocking, setUnlocking] = useState(false);

  return (
    <div className="flex flex-col gap-5">
      {saved && (
        <div role="status" className="flex items-start gap-3 rounded-2xl border border-emerald-600/30 bg-emerald-50 p-4 text-emerald-900 dark:bg-emerald-950 dark:text-emerald-100">
          <CircleCheck className="mt-0.5 size-5 shrink-0" aria-hidden />
          <div>
            <p className="font-semibold">Zakup zapisany. Przyznano {plain.format(saved.pointsAwarded)} pkt.</p>
            {saved.pointsAwarded === 0 && <p className="text-sm">Kwota poniżej 1 zł nie daje punktów.</p>}
          </div>
        </div>
      )}

      {unknown && pending && (
        <div role="alert" className="flex flex-col items-start gap-3 rounded-2xl border border-amber-500/50 bg-amber-50 p-4 text-amber-950 dark:bg-amber-950 dark:text-amber-50">
          <p className="text-sm">{unknownMessage(unknown)}</p>
          {!unlocking ? (
            <div className="flex flex-wrap gap-2">
              <Button onClick={() => send(pending)}>Ponów zapis zakupu</Button>
              <Button variant="outline" onClick={() => setUnlocking(true)}>
                Odblokuj formularz…
              </Button>
            </div>
          ) : (
            <div role="group" aria-label="Potwierdzenie odblokowania" className="flex flex-col gap-2">
              <p className="text-sm font-semibold">Zakup mógł już zostać zapisany.</p>
              <p className="text-sm">
                Odblokowanie nie cofa zapisu na serwerze. Zakup z tym samym numerem paragonu nie zostanie zapisany drugi
                raz, ale <strong>zmiana numeru paragonu omija tę ochronę</strong> i może naliczyć punkty podwójnie. Najpierw
                ponów zapis albo ustal wynik (np. w historii punktów klienta).
              </p>
              <div className="flex flex-wrap gap-2">
                <Button onClick={() => setUnlocking(false)}>Wróć</Button>
                <Button
                  variant="outline"
                  onClick={() => {
                    setUnlocking(false);
                    purchase.reset();
                    setPending(null);
                  }}
                >
                  Rozumiem, odblokuj
                </Button>
              </div>
            </div>
          )}
        </div>
      )}

      <form onSubmit={onSubmit} noValidate className="flex flex-col gap-5">
        <FormAlert message={errors.root?.message} />
        <fieldset disabled={locked} className="flex flex-col gap-5 disabled:opacity-70">
          <legend className="sr-only">Zakup paliwa</legend>

          <FormField id="cardCode" label="Kod karty klienta" error={errors.cardCode}>
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
            disabled={locked}
            onScan={(code) => form.setValue("cardCode", code, { shouldValidate: true, shouldDirty: true })}
          />

          <div className="grid gap-5 sm:grid-cols-2">
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
            <FormField id="saleDate" label="Data sprzedaży" error={errors.saleDate}>
              <Input
                type="date"
                max={warsawToday()}
                className={fieldClass}
                {...fieldA11y("saleDate", errors.saleDate)}
                {...form.register("saleDate")}
              />
            </FormField>
            <FormField id="receiptNumber" label="Numer paragonu" error={errors.receiptNumber}>
              <Input
                autoComplete="off"
                autoCapitalize="characters"
                className={cn(fieldClass, "font-mono")}
                {...fieldA11y("receiptNumber", errors.receiptNumber)}
                {...form.register("receiptNumber")}
              />
            </FormField>
          </div>

          <Button type="submit" size="lg" className="h-12 bg-brand-orange text-brand-navy hover:bg-brand-orange/90">
            {purchase.isPending ? "Zapisywanie…" : "Zapisz zakup"}
          </Button>
        </fieldset>
      </form>
    </div>
  );
}
