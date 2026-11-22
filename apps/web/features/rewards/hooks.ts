"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useIdempotentMutation } from "@/lib/idempotency";
import { invalidates, queryKeys } from "@/lib/query-keys";
import {
  rewardsService,
  type ExchangeResult,
  type ExchangeVars,
} from "./services";

export function useRewards() {
  return useQuery({
    queryKey: queryKeys.rewards,
    queryFn: rewardsService.list,
  });
}

export const EXCHANGE_OPERATION = "rewards-exchange";

/**
 * Same offer and version → same Idempotency-Key until a final answer, so a retry after a lost
 * response buys at most one coupon. After every attempt the balance, history, coupons and the
 * catalog are read again: a replayed response describes the original exchange, not the current state.
 * The key of an unfinished exchange is kept in sessionStorage, so leaving the screen cannot drop it.
 */
export function useExchange() {
  const queryClient = useQueryClient();
  return useIdempotentMutation<ExchangeResult, ExchangeVars>({
    mutationFn: (vars, key) => rewardsService.exchange(vars, key),
    persist: EXCHANGE_OPERATION,
    onSettled: () =>
      Promise.all(
        [...invalidates.exchange, queryKeys.rewards].map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      ),
  });
}
