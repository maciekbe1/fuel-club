import type { TurnMetrics } from "../services";
import { Counts } from "./Counts";
import { Tile } from "./Tile";
import { ms, number, share } from "./labels";

export function TurnMetricsSection({ turns }: { turns: TurnMetrics }) {
  return (
    <section aria-labelledby="turn-metrics" className="flex flex-col gap-3">
      <h2 id="turn-metrics" className="text-lg font-semibold">
        Tury
      </h2>
      <dl
        data-testid="chat-metrics"
        className="grid grid-cols-2 gap-3 sm:grid-cols-4"
      >
        <Tile label="Tury" value={number.format(turns.turns)} />
        <Tile label="Zakończone" value={number.format(turns.done)} />
        <Tile label="Błędy" value={share(turns.errors, turns.turns)} />
        <Tile
          label="Przerwane przez klienta"
          value={number.format(turns.cancelled)}
        />
        <Tile
          label="Pierwsze zdarzenie p50 / p95"
          value={`${ms(turns.p50FirstEventMs)} / ${ms(turns.p95FirstEventMs)}`}
        />
        <Tile
          label="Cała tura p50 / p95"
          value={`${ms(turns.p50TotalMs)} / ${ms(turns.p95TotalMs)}`}
        />
        <Tile
          label="Kroki modelu (suma / p95 na turę)"
          value={
            turns.turnsWithStepData === 0
              ? "brak danych"
              : `${number.format(turns.modelSteps)} / ${number.format(turns.p95ModelSteps ?? 0)}`
          }
        />
        <Tile
          label="Wywołania narzędzi (nie ok)"
          value={
            turns.toolCalls === 0
              ? "0"
              : `${number.format(turns.toolCalls)} (${share(turns.toolCallsNotOk, turns.toolCalls)})`
          }
        />
        <Tile
          label="Tokeny (wej./wyj.)"
          value={
            turns.inputTokens == null
              ? "brak danych"
              : `${number.format(turns.inputTokens)} / ${number.format(turns.outputTokens ?? 0)}`
          }
        />
        <Tile
          label="Tury bez danych o tokenach (poza demo)"
          value={number.format(turns.turnsWithoutTokenData)}
        />
      </dl>
      {turns.errorCodes.length > 0 && (
        <Counts
          label="Kody błędów"
          items={turns.errorCodes.map((c) => [c.key, c.count])}
        />
      )}
      {turns.tools.length > 0 && (
        <Counts
          label="Narzędzia"
          items={turns.tools.map((t) => [`${t.name} - ${t.status}`, t.count])}
        />
      )}
    </section>
  );
}
