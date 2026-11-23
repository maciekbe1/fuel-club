"use client";

import { QueryError } from "@/components/query/QueryError";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api/problem";
import { useAdminReward } from "../hooks";
import { EditLoaded } from "./EditLoaded";

export function EditRewardForm({ id }: { id: string }) {
  const offer = useAdminReward(id);

  if (offer.isPending)
    return (
      <Skeleton role="status" aria-busy="true" className="h-96 rounded-3xl">
        <span className="sr-only">Ładowanie oferty…</span>
      </Skeleton>
    );

  if (offer.data === undefined)
    return (
      <QueryError
        error={offer.error}
        fallback="Nie udało się pobrać oferty. Spróbuj ponownie."
        message={
          offer.error instanceof ApiError && offer.error.status === 404
            ? "Nie ma takiej oferty."
            : undefined
        }
        retrying={offer.isFetching}
        onRetry={() => offer.refetch()}
      />
    );

  // Loaded once: later refetches (after saving) must not reset what the admin is typing.
  return <EditLoaded initial={offer.data} />;
}
