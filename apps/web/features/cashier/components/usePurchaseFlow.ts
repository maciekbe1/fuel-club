"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { ApiError, applyFieldErrors } from "@/lib/api/problem";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { useRegisterPurchase } from "../hooks";
import { purchaseMessages, purchaseSchema, warsawToday, toPurchaseBody, type PurchaseValues } from "../schemas";
import type { FuelPurchaseBody, FuelPurchaseResult } from "../services";

const FIELDS = ["cardCode", "amount", "liters", "saleDate", "receiptNumber"] as const;

export type PurchaseFlow = ReturnType<typeof usePurchaseFlow>;

/**
 * The purchase in progress: form values, the mutation with its idempotency key and the body awaiting
 * a retry. Called by the panel, above the station views, so a failed station refresh keeps it.
 */
export function usePurchaseFlow() {
  const purchase = useRegisterPurchase();
  const [pending, setPending] = useState<FuelPurchaseBody | null>(null);
  const [saved, setSaved] = useState<FuelPurchaseResult | null>(null);
  const form = useForm<PurchaseValues>({
    resolver: zodResolver(purchaseSchema),
    defaultValues: {
      cardCode: "",
      amount: "",
      liters: "",
      saleDate: warsawToday(),
      receiptNumber: "",
    },
  });

  // While the outcome is unknown the form is locked: a retry must send the same body (same key).
  const unknown = purchase.isError && keepsIdempotencyKey(purchase.error) ? purchase.error : null;
  const locked = purchase.isPending || unknown !== null;

  const send = async (body: FuelPurchaseBody) => {
    setPending(body);
    setSaved(null);
    try {
      const result = await purchase.mutateAsync(body);
      setSaved(result);
      setPending(null);
      form.reset({
        cardCode: "",
        amount: "",
        liters: "",
        saleDate: body.saleDate,
        receiptNumber: "",
      });
    } catch (error) {
      if (keepsIdempotencyKey(error as Error)) return;
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

  return {
    purchase,
    pending,
    setPending,
    saved,
    form,
    unknown,
    locked,
    send,
    onSubmit,
  };
}
