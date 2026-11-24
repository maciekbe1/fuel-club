"use client";

import { useState } from "react";
import { QueryError } from "@/components/query/QueryError";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import { readErrorMessage } from "@/lib/api/problem";
import { useConversations } from "../hooks";
import type { ConversationFilters, Traffic } from "../services";
import { ConversationRow } from "./ConversationRow";
import { TRAFFIC } from "./labels";

const WINDOWS = [
  { hours: 24, label: "24 h" },
  { hours: 168, label: "7 dni" },
  { hours: 720, label: "30 dni" },
] as const;

/** Conversations as metadata: times, end, turns, flags. The content is never stored. */
export function ConversationList() {
  const [filters, setFilters] = useState<ConversationFilters>({
    hours: 24,
    traffic: "real",
    suspicious: false,
  });
  const conversations = useConversations(filters);
  const items = conversations.data?.pages.flatMap((page) => page.items) ?? [];

  return (
    <section aria-labelledby="conversations" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-3">
        <ToggleGroup
          aria-label="Okno czasu"
          variant="outline"
          size="sm"
          value={[String(filters.hours)]}
          onValueChange={(value) =>
            value[0] && setFilters({ ...filters, hours: Number(value[0]) })
          }
        >
          {WINDOWS.map((window) => (
            <ToggleGroupItem key={window.hours} value={String(window.hours)}>
              {window.label}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <ToggleGroup
          aria-label="Ruch"
          variant="outline"
          size="sm"
          value={[filters.traffic]}
          onValueChange={(value) =>
            value[0] && setFilters({ ...filters, traffic: value[0] as Traffic })
          }
        >
          {(Object.keys(TRAFFIC) as Traffic[]).map((traffic) => (
            <ToggleGroupItem key={traffic} value={traffic}>
              {TRAFFIC[traffic]}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <Label className="flex items-center gap-2 text-sm font-normal">
          <Checkbox
            checked={filters.suspicious}
            onCheckedChange={(checked) =>
              setFilters({ ...filters, suspicious: checked === true })
            }
          />
          Tylko podejrzane
        </Label>
      </div>

      <h2 id="conversations" className="sr-only">
        Lista rozmów
      </h2>

      {conversations.isPending ? (
        <div role="status" aria-busy="true" className="flex flex-col gap-3">
          <span className="sr-only">Ładowanie rozmów…</span>
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-20 rounded-2xl" />
          ))}
        </div>
      ) : conversations.isError && !conversations.data ? (
        <QueryError
          error={conversations.error}
          fallback="Nie udało się pobrać rozmów. Spróbuj ponownie."
          retrying={conversations.isFetching}
          onRetry={() => conversations.refetch()}
        />
      ) : items.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          Brak rozmów dla wybranych filtrów.
        </p>
      ) : (
        <ol aria-label="Rozmowy" className="flex flex-col gap-3">
          {items.map((item) => (
            <ConversationRow key={item.id} item={item} />
          ))}
        </ol>
      )}

      {conversations.isFetchNextPageError && (
        <p role="alert" className="text-sm text-destructive">
          {readErrorMessage(
            conversations.error,
            "Nie udało się pobrać rozmów. Spróbuj ponownie.",
          )}
        </p>
      )}

      {conversations.hasNextPage && (
        <Button
          variant="outline"
          className="self-center"
          onClick={() => conversations.fetchNextPage()}
          disabled={conversations.isFetchingNextPage}
        >
          {conversations.isFetchingNextPage ? "Ładowanie…" : "Pokaż więcej"}
        </Button>
      )}
    </section>
  );
}
