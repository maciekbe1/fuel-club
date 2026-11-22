"use client";

import {
  useMutationState,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useRef, useState } from "react";
import { ApiError } from "@/lib/api/problem";
import {
  keepsIdempotencyKey,
  keepsPendingOperation,
  useIdempotentMutation,
} from "@/lib/idempotency";
import { queryKeys } from "@/lib/query-keys";
import {
  cashierService,
  type CouponPreview,
  type FuelPurchaseBody,
  type FuelPurchaseResult,
  type RewardHandoverBody,
  type RewardHandoverResult,
} from "./services";

export function useCashierStation() {
  return useQuery({
    queryKey: queryKeys.cashier.station,
    queryFn: cashierService.station,
  });
}

/** Same body → same Idempotency-Key until a final answer; a retry after a lost response is safe. */
export function useRegisterPurchase() {
  return useIdempotentMutation<FuelPurchaseResult, FuelPurchaseBody>({
    mutationFn: (body, key) => cashierService.purchase(body, key),
  });
}

/**
 * A handover answer where this attempt certainly handed nothing over (limit, customer busy). After an
 * unknown outcome it says nothing about the earlier attempt with the same key: the rate limiter runs
 * before idempotency.
 */
export const isCertainRefusal = (error: unknown) =>
  error instanceof ApiError &&
  (error.status === 429 || error.code === "loyalty.customer_busy");

type HandoverAttempt = {
  vars: RewardHandoverBody | undefined;
  status: string;
  error: unknown;
  at: number;
};

/** Unknown after the last final answer: a later attempt ended without a known outcome. */
function unresolvedIn(attempts: HandoverAttempt[]) {
  let unresolved = false;
  for (const attempt of [...attempts].sort((a, b) => a.at - b.at)) {
    if (attempt.status === "success") unresolved = false;
    else if (attempt.status !== "error") continue;
    else if (!keepsPendingOperation(attempt.error)) unresolved = false;
    else if (
      keepsIdempotencyKey(attempt.error) &&
      !isCertainRefusal(attempt.error)
    )
      unresolved = true;
    // A certain refusal or a 401 leaves the earlier state as it was.
  }
  return unresolved;
}

/**
 * Whether a handover of this pair (or, without one, of any pair) has an unknown outcome. Derived
 * from the mutation cache, not pushed between components: a late answer, a route hidden by
 * Activity and a remounted panel all read the same state, and nothing has to be cleared by hand.
 */
export function useHandoverUnresolved(pair?: RewardHandoverBody) {
  const attempts = useMutationState({
    filters: { mutationKey: queryKeys.cashier.rewardHandover },
    select: (mutation): HandoverAttempt => ({
      vars: mutation.state.variables as RewardHandoverBody | undefined,
      status: mutation.state.status,
      error: mutation.state.error,
      at: mutation.state.submittedAt,
    }),
  });
  const pairOf = (vars: RewardHandoverBody | undefined) =>
    vars ? `${vars.cardCode}:${vars.couponCode}` : "";
  const byPair = new Map<string, HandoverAttempt[]>();
  for (const attempt of attempts) {
    const key = pairOf(attempt.vars);
    if (pair && key !== pairOf(pair)) continue;
    byPair.set(key, [...(byPair.get(key) ?? []), attempt]);
  }
  return [...byPair.values()].some(unresolvedIn);
}

/** Same card and code → same Idempotency-Key until a final answer: a lost response is retried safely. */
export function useHandOverReward() {
  return useIdempotentMutation<RewardHandoverResult, RewardHandoverBody>({
    mutationKey: queryKeys.cashier.rewardHandover,
    mutationFn: (body, key) => cashierService.handOverReward(body, key),
  });
}

/**
 * The coupon's terms for a complete card and coupon code: one request per pair while it stays in
 * memory (5 min, cleared after every purchase), no retries, no refetch on focus - the preview is
 * limited per cashier. The key holds both codes, so a late answer for an older pair never shows up
 * for the current one.
 */
export function useCouponTerms(
  cardCode: string | null,
  couponCode: string | null,
) {
  return useQuery({
    queryKey: queryKeys.cashier.couponPreview(cardCode ?? "", couponCode ?? ""),
    queryFn: () =>
      cashierService.previewCoupon({
        cardCode,
        couponCode,
        amount: null,
        liters: null,
      }),
    enabled: cardCode !== null && couponCode !== null,
    retry: false,
    staleTime: Infinity,
    gcTime: 5 * 60_000,
    refetchOnWindowFocus: false,
  });
}

type Calculation = NonNullable<CouponPreview["calculation"]>;
export type CalculationInputs = {
  cardCode: string;
  couponCode: string;
  amount: number;
  liters: number;
};
/** The last "Przelicz" answer with the inputs it was computed for. */
export type Computed = CalculationInputs & {
  result: Calculation | null;
  error: unknown;
};

/**
 * "Przelicz": the preview with amount and liters, only on request. Every call has a number; an answer
 * for a call that was superseded or forgotten (new code) is ignored. `calculatingFor` names the
 * inputs in flight, so a pending call for another card does not block the current one.
 */
export function useCouponCalculation() {
  const [computed, setComputed] = useState<Computed | null>(null);
  const [calculatingFor, setCalculatingFor] =
    useState<CalculationInputs | null>(null);
  const request = useRef(0);

  const forget = () => {
    request.current++;
    setComputed(null);
    setCalculatingFor(null);
  };

  const calculate = async (inputs: CalculationInputs) => {
    const id = ++request.current;
    setCalculatingFor(inputs);
    try {
      const response = await cashierService.previewCoupon(inputs);
      if (id !== request.current) return;
      setComputed({ ...inputs, result: response.calculation, error: null });
    } catch (error) {
      if (id !== request.current) return;
      setComputed({ ...inputs, result: null, error });
    } finally {
      if (id === request.current) setCalculatingFor(null);
    }
  };

  return { computed, calculatingFor, calculate, forget };
}

/** After a purchase the coupon may be redeemed: no preview may be served from memory. */
export function useForgetCouponPreviews() {
  const queryClient = useQueryClient();
  return () =>
    queryClient.removeQueries({ queryKey: queryKeys.cashier.couponPreviews });
}
