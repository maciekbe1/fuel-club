"use client";

import { QueryError } from "@/components/query/QueryError";
import { Alert } from "@/components/ui/alert";
import { RetryButton } from "@/components/ui/retry-button";
import { Skeleton } from "@/components/ui/skeleton";
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
      <Skeleton
        role="status"
        aria-busy="true"
        className="aspect-[1.586] w-full max-w-md rounded-3xl"
      >
        <span className="sr-only">Ładowanie karty…</span>
      </Skeleton>
    );

  if (card.data === undefined)
    return (
      <QueryError
        error={card.error}
        fallback="Nie udało się pobrać karty. Spróbuj ponownie."
        retrying={card.isFetching}
        onRetry={() => card.refetch()}
      />
    );

  return (
    <div className="flex flex-col gap-4">
      {card.isError && (
        <Alert
          className="flex max-w-md flex-col items-start gap-2 rounded-2xl p-4 text-sm"
          variant="destructive"
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
        </Alert>
      )}
      {card.data ? (
        <ActiveCard card={card.data} rotate={rotate} />
      ) : (
        <NoCard issue={issue} />
      )}
    </div>
  );
}
