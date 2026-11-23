"use client";

import Link from "next/link";
import { QueryError } from "@/components/query/QueryError";
import { Button, buttonVariants } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { readErrorMessage } from "@/lib/api/problem";
import { useHistory } from "../hooks";
import { HistoryItem } from "./HistoryItem";

export function HistoryList() {
  const history = useHistory();

  if (history.isPending)
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-3">
        <span className="sr-only">Ładowanie historii…</span>
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-24 rounded-2xl" />
        ))}
      </div>
    );

  if (history.isError && !history.data)
    return (
      <QueryError
        error={history.error}
        fallback="Nie udało się pobrać historii. Spróbuj ponownie."
        retrying={history.isFetching}
        onRetry={() => history.refetch()}
      />
    );

  const items = history.data.pages.flatMap((page) => page.items);

  if (items.length === 0)
    return (
      <section
        aria-labelledby="empty-history"
        className="flex flex-col items-start gap-3 rounded-3xl border border-dashed p-6"
      >
        <h2 id="empty-history" className="text-lg font-semibold">
          Nie masz jeszcze żadnych operacji
        </h2>
        <p className="text-muted-foreground">
          Pokaż kartę przy kasie podczas tankowania - punkty i szczegóły zakupu
          pojawią się tutaj.
        </p>
        <Link href="/karta" className={buttonVariants({ variant: "outline" })}>
          Pokaż kartę
        </Link>
      </section>
    );

  return (
    <div className="flex flex-col gap-4">
      <ol aria-label="Operacje punktowe" className="flex flex-col gap-3">
        {items.map((item) => (
          <HistoryItem key={item.id} item={item} />
        ))}
      </ol>

      {history.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {readErrorMessage(
            history.error,
            "Nie udało się pobrać historii. Spróbuj ponownie.",
          )}
        </p>
      )}

      {history.hasNextPage && (
        <Button
          variant="outline"
          className="self-center"
          onClick={() => history.fetchNextPage()}
          disabled={history.isFetchingNextPage}
        >
          {history.isFetchingNextPage ? "Ładowanie…" : "Pokaż więcej"}
        </Button>
      )}

      <p className="text-xs text-muted-foreground">
        Historia pokazuje zmiany punktów. Zakup za mniej niż 1 zł nie daje
        punktów, więc się tu nie pojawia.
      </p>
    </div>
  );
}
