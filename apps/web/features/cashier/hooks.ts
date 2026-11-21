"use client";

import { useQuery } from "@tanstack/react-query";
import { useIdempotentMutation } from "@/lib/idempotency";
import { queryKeys } from "@/lib/query-keys";
import { cashierService, type FuelPurchaseBody, type FuelPurchaseResult } from "./services";

export function useCashierStation() {
  return useQuery({ queryKey: queryKeys.cashier.station, queryFn: cashierService.station });
}

/** Same body → same Idempotency-Key until a final answer; a retry after a lost response is safe. */
export function useRegisterPurchase() {
  return useIdempotentMutation<FuelPurchaseResult, FuelPurchaseBody>({
    mutationFn: (body, key) => cashierService.purchase(body, key),
  });
}
