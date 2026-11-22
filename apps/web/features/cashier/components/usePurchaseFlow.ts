"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useIsMutating } from "@tanstack/react-query";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { ApiError, applyFieldErrors } from "@/lib/api/problem";
import { keepsIdempotencyKey } from "@/lib/idempotency";
import { queryKeys } from "@/lib/query-keys";
import { useForgetCouponPreviews, useRegisterPurchase } from "../hooks";
import {
  purchaseMessages,
  purchaseSchema,
  warsawToday,
  toPurchaseBody,
  type PurchaseValues,
} from "../schemas";
import type { FuelPurchaseBody, FuelPurchaseResult } from "../services";

const FIELDS = [
  "cardCode",
  "couponCode",
  "amount",
  "liters",
  "saleDate",
  "receiptNumber",
] as const;

export type PurchaseFlow = ReturnType<typeof usePurchaseFlow>;

/**
 * The purchase in progress: form values, the mutation with its idempotency key and the body awaiting
 * a retry. Called by the panel, above the station views, so a failed station refresh keeps it.
 */
export function usePurchaseFlow() {
  const purchase = useRegisterPurchase();
  const removePreviews = useForgetCouponPreviews();
  const [previewGeneration, setPreviewGeneration] = useState(0);
  // Which QR scanner holds the camera; the other one is disabled meanwhile.
  const [scanner, setScanner] = useState<"card" | "coupon" | null>(null);
  /** After a purchase attempt the coupon may be redeemed: drop the terms and the calculation. */
  const forgetPreviews = () => {
    removePreviews();
    setPreviewGeneration((generation) => generation + 1);
  };
  const [pending, setPending] = useState<FuelPurchaseBody | null>(null);
  const [saved, setSaved] = useState<FuelPurchaseResult | null>(null);
  const form = useForm<PurchaseValues>({
    resolver: zodResolver(purchaseSchema),
    defaultValues: {
      cardCode: "",
      couponCode: "",
      amount: "",
      liters: "",
      saleDate: warsawToday(),
      receiptNumber: "",
    },
  });

  // While the outcome is unknown the form is locked: a retry must send the same body (same key).
  const unknown =
    purchase.isError && keepsIdempotencyKey(purchase.error)
      ? purchase.error
      : null;
  // A reward handover in flight locks the form too: a changed code would drop its panel, and its
  // late success would clear the next customer's code.
  const handingOver =
    useIsMutating({ mutationKey: queryKeys.cashier.rewardHandover }) > 0;
  // A handover with an unknown outcome locks the card and coupon fields, scanners and the purchase
  // submit, so the next customer cannot be served before it is resolved; "Ponów wydanie" stays
  // enabled (amount, liters, date and receipt too - they change nothing on their own).
  const [handoverUnresolved, setHandoverUnresolved] = useState(false);
  const purchaseLocked = purchase.isPending || unknown !== null;
  // The whole form (fieldset): not the unresolved handover, whose retry button lives inside it.
  const formLocked = purchaseLocked || handingOver;
  const locked = formLocked || handoverUnresolved;

  const send = async (body: FuelPurchaseBody) => {
    setPending(body);
    setSaved(null);
    try {
      const result = await purchase.mutateAsync(body);
      setSaved(result);
      setPending(null);
      forgetPreviews();
      form.reset({
        cardCode: "",
        couponCode: "",
        amount: "",
        liters: "",
        saleDate: body.saleDate,
        receiptNumber: "",
      });
    } catch (error) {
      if (keepsIdempotencyKey(error as Error)) return;
      setPending(null);
      forgetPreviews();
      if (applyFieldErrors(error, form.setError, purchaseMessages, FIELDS))
        return;
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
    purchaseLocked,
    formLocked,
    handoverUnresolved,
    setHandoverUnresolved,
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
  };
}
