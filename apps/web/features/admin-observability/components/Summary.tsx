import { AlertTriangle } from "lucide-react";
import { useObservabilitySummary } from "../hooks";
import { Counts } from "./Counts";
import { LoadError } from "./LoadError";
import { Rule } from "./Rule";
import { Tile } from "./Tile";
import { RULES, SECURITY, ms, number } from "./labels";

export function Summary({ hours }: { hours: number }) {
  const summary = useObservabilitySummary(hours);
  if (summary.isPending)
    return (
      <div
        role="status"
        aria-busy="true"
        className="h-64 animate-pulse rounded-3xl bg-muted"
      >
        <span className="sr-only">Ładowanie metryk…</span>
      </div>
    );
  if (summary.data === undefined)
    return (
      <LoadError
        error={summary.error}
        busy={summary.isFetching}
        retry={() => summary.refetch()}
      />
    );
  const { chat, security, anomalies } = summary.data;
  const triggered = anomalies.filter((a) => a.triggered);
  return (
    <>
      <section aria-labelledby="chat-metrics" className="flex flex-col gap-3">
        <h2 id="chat-metrics" className="text-lg font-semibold">
          Czat
        </h2>
        <dl
          data-testid="chat-metrics"
          className="grid grid-cols-2 gap-3 sm:grid-cols-4"
        >
          <Tile label="Tury" value={number.format(chat.turns)} />
          <Tile label="Zakończone" value={number.format(chat.done)} />
          <Tile label="Błędy" value={number.format(chat.errors)} />
          <Tile
            label="Przerwane przez klienta"
            value={number.format(chat.cancelled)}
          />
          <Tile
            label="p95 do pierwszego zdarzenia"
            value={ms(chat.p95FirstEventMs)}
          />
          <Tile label="p95 całej tury" value={ms(chat.p95TotalMs)} />
          <Tile
            label="Tokeny (wej./wyj.)"
            value={
              chat.inputTokens == null
                ? "brak danych"
                : `${number.format(chat.inputTokens)} / ${number.format(chat.outputTokens ?? 0)}`
            }
          />
          <Tile
            label="Tury bez danych o tokenach"
            value={number.format(chat.turnsWithoutTokenData)}
          />
        </dl>
        {chat.errorCodes.length > 0 && (
          <Counts
            label="Kody błędów"
            items={chat.errorCodes.map((c) => [c.key, c.count])}
          />
        )}
        {chat.tools.length > 0 && (
          <Counts
            label="Narzędzia"
            items={chat.tools.map((t) => [`${t.name} - ${t.status}`, t.count])}
          />
        )}
      </section>

      <section aria-labelledby="security" className="flex flex-col gap-3">
        <h2 id="security" className="text-lg font-semibold">
          Zdarzenia bezpieczeństwa
        </h2>
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
          <p
            role="alert"
            className="flex items-center gap-2 rounded-2xl border border-amber-500/50 bg-amber-50 p-3 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
          >
            <AlertTriangle className="size-4 shrink-0" />
            Przekroczone progi:{" "}
            {triggered.map((a) => RULES[a.rule] ?? a.rule).join(", ")}.
          </p>
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
