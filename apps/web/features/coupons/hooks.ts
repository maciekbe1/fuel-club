"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useIdempotentMutation } from "@/lib/idempotency";
import { invalidates, queryKeys } from "@/lib/query-keys";
import { couponsService } from "./services";

export function useCoupons() {
  return useQuery({
    queryKey: queryKeys.coupons,
    queryFn: couponsService.list,
  });
}

/**
 * The activation response is never displayed: a replayed response describes the original
 * activation. After every attempt the coupons are read again with GET, and the mutation stays
 * pending until that read finishes. Activating an active coupon again returns the same code, so a
 * retry (even with a new key after leaving the screen) cannot change the code.
 */
export function useActivateCoupon() {
  const queryClient = useQueryClient();
  return useIdempotentMutation<unknown, string>({
    mutationFn: (id, key) => couponsService.activate(id, key),
    onSettled: () =>
      Promise.all(
        invalidates.activate.map((queryKey) =>
          queryClient.invalidateQueries({ queryKey }),
        ),
      ),
  });
}
