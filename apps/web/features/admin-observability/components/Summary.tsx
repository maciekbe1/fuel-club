import { QueryError } from "@/components/query/QueryError";
import { Alert } from "@/components/ui/alert";
import { AlertTriangle } from "lucide-react";
import { Skeleton } from "@/components/ui/skeleton";
import { useObservabilitySummary } from "../hooks";
import type { SummaryFilters } from "../services";
import { ConversationMetricsSection } from "./ConversationMetricsSection";
import { CostTile } from "./CostTile";
import { Counts } from "./Counts";
import { Rule } from "./Rule";
import { SummaryFilterBar } from "./SummaryFilterBar";
import { TurnMetricsSection } from "./TurnMetricsSection";
import { RULES, SECURITY } from "./labels";

export function Summary({
  hours,
  filters,
  onFiltersChange,
}: {
  hours: number;
  filters: SummaryFilters;
  onFiltersChange: (filters: SummaryFilters) => void;
}) {
  const summary = useObservabilitySummary(hours, filters);
  if (summary.isPending)
    return (
      <Skeleton role="status" aria-busy="true" className="h-64 rounded-3xl">
        <span className="sr-only">Ładowanie metryk…</span>
      </Skeleton>
    );
  if (summary.data === undefined)
    return (
      <QueryError
        error={summary.error}
        fallback="Nie udało się pobrać danych. Spróbuj ponownie."
        retrying={summary.isFetching}
        onRetry={() => summary.refetch()}
      />
    );
  const { turns, cost, conversations, security, anomalies, options } =
    summary.data;
  const triggered = anomalies.filter((a) => a.triggered);
  const demoOnly = turns.turns > 0 && turns.demoTurns === turns.turns;
  return (
    <>
      <SummaryFilterBar
        value={filters}
        options={options}
        onChange={onFiltersChange}
      />
      {demoOnly && (
        <Alert className="rounded-2xl p-3 text-sm" data-testid="demo-only">
          Wszystkie tury w tym widoku pochodzą z adaptera demo (reguły, bez AI).
          Czasy i liczby pokazują działanie aplikacji i panelu - to nie jest
          pomiar wydajności ani kosztu Gemini.
        </Alert>
      )}
      <TurnMetricsSection turns={turns} />
      <CostTile cost={cost} />
      <ConversationMetricsSection conversations={conversations} />

      <section aria-labelledby="security" className="flex flex-col gap-3">
        <h2 id="security" className="text-lg font-semibold">
          Zdarzenia bezpieczeństwa
        </h2>
        <p className="text-sm text-muted-foreground">
          Bez filtrów ruchu: zdarzenia i reguły liczą wszystko.
        </p>
        {security.length === 0 ? (
          <p className="text-sm text-muted-foreground">Brak w tym oknie.</p>
        ) : (
          <Counts
            label="Zdarzenia bezpieczeństwa"
            items={security.map((s) => [SECURITY[s.key] ?? s.key, s.count])}
          />
        )}
      </section>

      <section aria-labelledby="anomalies" className="flex flex-col gap-3">
        <h2 id="anomalies" className="text-lg font-semibold">
          Reguły anomalii
        </h2>
        <p className="text-sm text-muted-foreground">
          Każda reguła liczy we własnym oknie, niezależnie od okna wyżej.
        </p>
        {triggered.length > 0 && (
          <Alert
            className="flex items-center gap-2 rounded-2xl p-3 text-sm"
            variant="warning"
          >
            <AlertTriangle className="size-4 shrink-0" />
            Przekroczone progi:{" "}
            {triggered.map((a) => RULES[a.rule] ?? a.rule).join(", ")}.
          </Alert>
        )}
        <ul
          aria-label="Reguły"
          className="flex flex-col divide-y rounded-2xl border"
        >
          {anomalies.map((rule) => (
            <Rule key={rule.rule} rule={rule} />
          ))}
        </ul>
      </section>
    </>
  );
}
