"use client";

import { useInfiniteQuery } from "@tanstack/react-query";
import { queryKeys } from "@/lib/query-keys";
import { historyService } from "./services";

/** Newest first. Pages follow the server cursor; it is not a snapshot (see API docs). */
export function useHistory() {
  return useInfiniteQuery({
    queryKey: queryKeys.transactions,
    queryFn: ({ pageParam }) => historyService.page(pageParam),
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  });
}
