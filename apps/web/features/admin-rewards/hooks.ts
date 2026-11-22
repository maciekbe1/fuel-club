"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useIdempotentMutation } from "@/lib/idempotency";
import { invalidates, queryKeys } from "@/lib/query-keys";
import {
  adminRewardsService,
  type AdminReward,
  type CreateRewardBody,
  type UpdateRewardBody,
} from "./services";

export function useAdminRewards() {
  return useQuery({
    queryKey: queryKeys.admin.rewards,
    queryFn: adminRewardsService.list,
  });
}

export function useAdminReward(id: string) {
  return useQuery({
    queryKey: [...queryKeys.admin.rewards, id],
    queryFn: () => adminRewardsService.get(id),
  });
}

function useInvalidateRewards() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all(
      invalidates.rewardChange.map((queryKey) =>
        queryClient.invalidateQueries({ queryKey }),
      ),
    );
}

export const CREATE_OPERATION = "admin-reward-create";

/**
 * Same form → same Idempotency-Key until a final answer: a retry after a lost response creates one
 * offer. The key of an unfinished create is kept in sessionStorage, so leaving the screen or
 * reloading cannot drop it and let the same offer be created twice.
 */
export function useCreateReward() {
  const invalidate = useInvalidateRewards();
  return useIdempotentMutation<AdminReward, CreateRewardBody>({
    mutationFn: (body, key) => adminRewardsService.create(body, key),
    persist: CREATE_OPERATION,
    onSettled: invalidate,
  });
}

/**
 * PUT with the version the form was loaded with. Never retried automatically: after a lost response
 * the first attempt may have raised the version, so a blind retry would report a false conflict or,
 * with a reloaded version, silently overwrite someone else's change.
 */
export function useUpdateReward(id: string) {
  const invalidate = useInvalidateRewards();
  return useMutation({
    mutationFn: (body: UpdateRewardBody) =>
      adminRewardsService.update(id, body),
    onSettled: invalidate,
  });
}

/** Reads the stored offer on request (after a conflict or an unknown outcome), bypassing the cache. */
export function useFetchCurrentReward() {
  const queryClient = useQueryClient();
  return (id: string) =>
    queryClient.fetchQuery({
      queryKey: [...queryKeys.admin.rewards, id],
      queryFn: () => adminRewardsService.get(id),
      staleTime: 0,
    });
}
