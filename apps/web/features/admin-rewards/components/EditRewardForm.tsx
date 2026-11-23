"use client";

import { RetryButton } from "@/components/ui/retry-button";
import { ApiError, readErrorMessage } from "@/lib/api/problem";
import { useAdminReward } from "../hooks";
import { EditLoaded } from "./EditLoaded";

export function EditRewardForm({ id }: { id: string }) {
  const offer = useAdminReward(id);

  if (offer.isPending)
    return (
      <div
        role="status"
        aria-busy="true"
        className="h-96 animate-pulse rounded-3xl bg-muted"
      >
        <span className="sr-only">Ładowanie oferty…</span>
      </div>
    );

  if (offer.data === undefined)
    return (
      <div
        role="alert"
        className="flex flex-col items-start gap-3 rounded-2xl border p-5"
      >
        <p>
          {offer.error instanceof ApiError && offer.error.status === 404
            ? "Nie ma takiej oferty."
            : readErrorMessage(
                offer.error,
                "Nie udało się pobrać oferty. Spróbuj ponownie.",
              )}
        </p>
        <RetryButton busy={offer.isFetching} onClick={() => offer.refetch()} />
      </div>
    );

  // Loaded once: later refetches (after saving) must not reset what the admin is typing.
  return <EditLoaded initial={offer.data} />;
}
