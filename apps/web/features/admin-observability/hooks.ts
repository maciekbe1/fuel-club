"use client";

import {
  keepPreviousData,
  useInfiniteQuery,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { queryKeys } from "@/lib/query-keys";
import {
  type ConversationFilters,
  observabilityService,
  type SummaryFilters,
} from "./services";

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

/** Pages follow the server cursor; it is not a snapshot (see the API description). */
export function useConversations(filters: ConversationFilters) {
  return useInfiniteQuery({
    queryKey: queryKeys.admin.conversations(filters),
    queryFn: ({ pageParam }) =>
      observabilityService.conversations(filters, pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}

export function useConversationTrace(id: string) {
  return useQuery({
    queryKey: queryKeys.admin.conversationTrace(id),
    queryFn: () => observabilityService.conversation(id),
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
