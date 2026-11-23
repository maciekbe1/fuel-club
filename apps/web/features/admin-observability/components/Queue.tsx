import { CheckCircle2 } from "lucide-react";
import { QueryError } from "@/components/query/QueryError";
import { Skeleton } from "@/components/ui/skeleton";
import { useMarkReviewed, useReviewQueue } from "../hooks";
import { QueueItem } from "./QueueItem";

export function Queue() {
  const queue = useReviewQueue();
  const mark = useMarkReviewed();
  return (
    <section aria-labelledby="review" className="flex flex-col gap-3">
      <h2 id="review" className="text-lg font-semibold">
        Do przejrzenia
      </h2>
      <p className="text-sm text-muted-foreground">
        Tury z błędem albo odrzuconym narzędziem. Tylko metadane i pseudonim -
        treść rozmów nie jest zapisywana. Przerwanie rozmowy przez klienta nie
        trafia tutaj.
      </p>
      {queue.isPending ? (
        <Skeleton role="status" aria-busy="true" className="h-32 rounded-2xl">
          <span className="sr-only">Ładowanie kolejki…</span>
        </Skeleton>
      ) : queue.data === undefined ? (
        <QueryError
          error={queue.error}
          fallback="Nie udało się pobrać danych. Spróbuj ponownie."
          retrying={queue.isFetching}
          onRetry={() => queue.refetch()}
        />
      ) : queue.data.items.length === 0 ? (
        <p className="flex items-center gap-2 text-sm">
          <CheckCircle2 className="size-4 text-emerald-600" /> Nic do
          przejrzenia.
        </p>
      ) : (
        <ul aria-label="Kolejka przeglądu" className="flex flex-col gap-3">
          {queue.data.items.map((item) => (
            <QueueItem
              key={item.id}
              item={item}
              busy={mark.isPending && mark.variables === item.id}
              failed={mark.isError && mark.variables === item.id}
              onReviewed={() => mark.mutate(item.id)}
            />
          ))}
        </ul>
      )}
    </section>
  );
}
