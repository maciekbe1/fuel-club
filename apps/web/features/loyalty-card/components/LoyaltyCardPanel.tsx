"use client";

import { RetryButton } from "@/components/ui/retry-button";
import { readErrorMessage } from "@/lib/api/problem";
import { useIssueCard, useLoyaltyCard, useRotateCard } from "../hooks";
import { ActiveCard } from "./ActiveCard";
import { NoCard } from "./NoCard";

/**
 * Mutations live here, above the views that come and go (loading, error, no card, card): the
 * idempotency key of an unfinished operation must survive a failed card read in between.
 */
export function LoyaltyCardPanel() {
  const card = useLoyaltyCard();
  const issue = useIssueCard();
  const rotate = useRotateCard();

  if (card.isPending)
    return (
      <div
        role="status"
        aria-busy="true"
        className="aspect-[1.586] w-full max-w-md animate-pulse rounded-3xl bg-muted"
      >
        <span className="sr-only">Ładowanie karty…</span>
      </div>
    );

  if (card.data === undefined)
    return (
      <div
        role="alert"
        className="flex flex-col items-start gap-3 rounded-2xl border p-5"
      >
        <p>
          {readErrorMessage(
            card.error,
            "Nie udało się pobrać karty. Spróbuj ponownie.",
          )}
        </p>
        <RetryButton busy={card.isFetching} onClick={() => card.refetch()} />
      </div>
    );

  return (
    <div className="flex flex-col gap-4">
      {card.isError && (
        <div
          role="alert"
          className="flex max-w-md flex-col items-start gap-2 rounded-2xl border border-destructive/40 p-4 text-sm"
        >
          <p>
            Nie udało się odświeżyć karty - pokazany stan może być nieaktualny.
          </p>
          <RetryButton
            busy={card.isFetching}
            variant="outline"
            onClick={() => card.refetch()}
          >
            Odśwież
          </RetryButton>
        </div>
      )}
      {card.data ? (
        <ActiveCard card={card.data} rotate={rotate} />
      ) : (
        <NoCard issue={issue} />
      )}
    </div>
  );
}
