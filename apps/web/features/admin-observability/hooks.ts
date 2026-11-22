"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { queryKeys } from "@/lib/query-keys";
import { observabilityService } from "./services";

export function useObservabilitySummary(hours: number) {
  return useQuery({
    queryKey: queryKeys.admin.observabilitySummary(hours),
    queryFn: () => observabilityService.summary(hours),
  });
}

export function useReviewQueue() {
  return useQuery({
    queryKey: queryKeys.admin.reviewQueue,
    queryFn: observabilityService.queue,
  });
}

export function useMarkReviewed() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: observabilityService.markReviewed,
    onSettled: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.admin.reviewQueue }),
  });
}
