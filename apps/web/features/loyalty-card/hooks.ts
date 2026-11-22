"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useIdempotentMutation } from "@/lib/idempotency";
import { queryKeys } from "@/lib/query-keys";
import { loyaltyCardService } from "./services";

export function useLoyaltyCard() {
  return useQuery({
    queryKey: queryKeys.loyaltyCard,
    queryFn: loyaltyCardService.get,
  });
}

/**
 * Issue and rotation never display their own response: a replayed response describes the original
 * operation, not the current state. After every attempt the card is read again with GET, and the
 * mutation stays pending until that read finishes.
 */
function useCardMutation(call: (key: string) => Promise<unknown>) {
  const queryClient = useQueryClient();
  return useIdempotentMutation<unknown>({
    mutationFn: (_vars, key) => call(key),
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.loyaltyCard }),
  });
}

export const useIssueCard = () => useCardMutation(loyaltyCardService.issue);

export const useRotateCard = () => useCardMutation(loyaltyCardService.rotate);
