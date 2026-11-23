import type { ConversationMetrics } from "../services";
import { Tile } from "./Tile";
import { number, share } from "./labels";

/** Handoff once per conversation; no end reason means the matter was solved (ADR 0017, 0018). */
export function ConversationMetricsSection({
  conversations: c,
}: {
  conversations: ConversationMetrics;
}) {
  return (
    <section
      aria-labelledby="conversation-metrics"
      className="flex flex-col gap-3"
    >
      <h2 id="conversation-metrics" className="text-lg font-semibold">
        Rozmowy
      </h2>
      <dl
        data-testid="conversation-metrics"
        className="grid grid-cols-2 gap-3 sm:grid-cols-4"
      >
        <Tile label="Rozpoczęte" value={number.format(c.started)} />
        <Tile label="Otwarte" value={number.format(c.open)} />
        <Tile
          label="Zakończone przez klienta"
          value={number.format(c.endedByClient)}
        />
        <Tile label="Bezczynne (Idle)" value={number.format(c.endedIdle)} />
        <Tile
          label="Z propozycją konsultanta"
          value={share(c.withHandoffProposal, c.started)}
        />
        <Tile
          label="Przekazane (ze zgłoszeniem)"
          value={share(c.withCase, c.started)}
        />
        <Tile
          label="Odsetek rozmów bez przekazania"
          value={share(c.endedWithoutCase, c.ended)}
        />
      </dl>
      <p className="text-xs text-muted-foreground">
        Mianownik odsetka: zakończone rozmowy. „Bez przekazania” to nie
        rozwiązana sprawa - klient mógł zrezygnować, a „Bezczynne” to upływ
        czasu, nie sukces.
      </p>
    </section>
  );
}
