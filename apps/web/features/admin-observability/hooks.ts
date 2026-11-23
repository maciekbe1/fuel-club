"use client";

import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { queryKeys } from "@/lib/query-keys";
import { observabilityService, type SummaryFilters } from "./services";

/** The previous numbers stay on screen while a new filter loads, so the filters do not jump. */
export function useObservabilitySummary(
  hours: number,
  filters: SummaryFilters,
) {
  return useQuery({
    queryKey: queryKeys.admin.observabilitySummary(hours, filters),
    queryFn: () => observabilityService.summary(hours, filters),
    placeholderData: keepPreviousData,
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
